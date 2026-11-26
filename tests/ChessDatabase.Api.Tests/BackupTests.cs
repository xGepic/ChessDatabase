using System.Net;
using System.Net.Http.Json;
using ChessDatabase.Api.Backup;
using ChessDatabase.Api.Collection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ChessDatabase.Api.Tests;

public class BackupTests
{
    private static readonly DateTimeOffset Morning = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Back_up_now_copies_the_Collection_into_a_database_with_the_same_Games()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync(opponent: "Huber, Thomas");
        await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3");

        var status = await client.BackUpNowAsync();

        Assert.NotNull(status.LastBackupAt);
        var file = Assert.Single(Directory.GetFiles(status.Folder, "*.db"));
        await using var copy = OpenDatabase(file);
        var copied = await copy.Games.Include(g => g.Opponent).Include(g => g.MoveNodes).SingleAsync();
        Assert.Equal("Huber, Thomas", copied.Opponent.Name);
        Assert.Equal(3, copied.MoveNodes.Count);
    }

    [Fact]
    public async Task The_default_folder_is_next_to_the_Collection_and_can_be_changed()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<BackupStatusDto>("/api/backups");
        Assert.Equal(Path.Combine(factory.Directory, "Backups"), initial!.Folder);
        Assert.True(initial.IsDefault);
        Assert.Null(initial.LastBackupAt);

        var elsewhere = Path.Combine(factory.Directory, "Elsewhere");
        var put = await client.PutAsJsonAsync("/api/backups/folder", new { folder = elsewhere });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var status = await client.BackUpNowAsync();
        Assert.Equal(elsewhere, status.Folder);
        Assert.False(status.IsDefault);
        Assert.Single(Directory.GetFiles(elsewhere, "*.db"));

        await client.PutAsJsonAsync("/api/backups/folder", new { folder = "" });
        Assert.True((await client.GetFromJsonAsync<BackupStatusDto>("/api/backups"))!.IsDefault);
    }

    [Fact]
    public async Task A_relative_backup_folder_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/backups/folder", new { folder = "backups" });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Only_the_ten_newest_backups_are_kept()
    {
        var clock = new FakeTimeProvider(Morning);
        using var factory = new CollectionFactory(clock: clock);
        var client = factory.CreateClient();

        var names = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var status = await client.BackUpNowAsync();
            names.Add(Path.GetFileName(Directory.GetFiles(status.Folder, "*.db").Max()!));
            clock.Advance(TimeSpan.FromHours(1));
        }

        var left = Directory.GetFiles(Path.Combine(factory.Directory, "Backups"), "*.db").Select(Path.GetFileName).Order();
        Assert.Equal(names.Skip(2), left);
    }

    [Fact]
    public async Task A_backup_is_made_on_the_first_start_of_each_day_only()
    {
        var directory = CollectionFactory.NewDirectory();
        var backups = Path.Combine(directory, "Backups");
        try
        {
            var clock = new FakeTimeProvider(Morning);
            await StartAndStopAsync(directory, clock);
            Assert.Single(Directory.GetFiles(backups, "*.db"));

            clock.Advance(TimeSpan.FromHours(3));
            await StartAndStopAsync(directory, clock);
            Assert.Single(Directory.GetFiles(backups, "*.db"));

            clock.Advance(TimeSpan.FromHours(16)); // the next morning, 04:00
            await StartAndStopAsync(directory, clock);
            Assert.Equal(2, Directory.GetFiles(backups, "*.db").Length);
        }
        finally
        {
            CollectionFactory.DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task While_running_a_backup_is_made_every_24_hours()
    {
        var clock = new FakeTimeProvider(Morning);
        using var factory = new CollectionFactory(clock: clock, dailyBackups: true);
        factory.CreateClient();
        var scheduler = factory.Services.GetServices<IHostedService>().OfType<DailyBackups>().Single();
        var backups = Path.Combine(factory.Directory, "Backups");
        Assert.Single(Directory.GetFiles(backups, "*.db"));

        await WaitUntilAsync(() => scheduler.NextDue == Morning.AddHours(24));
        clock.Advance(TimeSpan.FromHours(23));
        await Task.Delay(200);
        Assert.Single(Directory.GetFiles(backups, "*.db"));

        clock.Advance(TimeSpan.FromHours(1));
        await WaitUntilAsync(() => Directory.GetFiles(backups, "*.db").Length == 2);

        await WaitUntilAsync(() => scheduler.NextDue == Morning.AddHours(48));
        clock.Advance(TimeSpan.FromHours(24));
        await WaitUntilAsync(() => Directory.GetFiles(backups, "*.db").Length == 3);
    }

    private static async Task StartAndStopAsync(string directory, TimeProvider clock)
    {
        using var factory = new CollectionFactory(directory, clock, dailyBackups: true);
        factory.CreateClient();
        await Task.CompletedTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }

    private static CollectionDbContext OpenDatabase(string file) =>
        new(new DbContextOptionsBuilder<CollectionDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options);
}


public static class BackupTestExtensions
{
    public static async Task<BackupStatusDto> BackUpNowAsync(this HttpClient client)
    {
        var response = await client.PostAsync("/api/backups", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BackupStatusDto>())!;
    }
}
