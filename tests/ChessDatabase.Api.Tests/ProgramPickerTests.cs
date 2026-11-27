using System.Net;
using System.Net.Http.Json;
using ChessDatabase.Api.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>Stands in for the Windows "Open" dialog: answers with a set path (null: cancelled), optionally after a wait.</summary>
public sealed class FakeFilePicker(string? answer, TaskCompletionSource? hold = null) : IFilePicker
{
    public List<FilePickRequest> Requests { get; } = [];

    public async Task<string?> PickFileAsync(FilePickRequest request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request);
        if (hold is not null) await hold.Task.WaitAsync(cancellationToken);
        return answer;
    }
}

public sealed record PickedProgramDto(string? Path);

/// <summary>"Browse…" beside an Engine's program: the backend, on the Owner's own PC, shows the Windows "Open" dialog.</summary>
public class ProgramPickerTests
{
    private static CollectionFactory WithPicker(FakeFilePicker picker) =>
        new() { ConfigureServices = services => services.AddSingleton<IFilePicker>(picker) };

    private static Task<HttpResponseMessage> BrowseAsync(HttpClient client, string name, string currentPath) =>
        client.PostAsJsonAsync("/api/engines/browse", new { name, currentPath });

    [Fact]
    public async Task Browsing_returns_the_chosen_program_and_starts_in_the_current_ones_folder()
    {
        var picker = new FakeFilePicker(@"C:\Tools\stockfish\stockfish-windows-x86-64-avx2.exe");
        using var factory = WithPicker(picker);
        var client = factory.CreateClient();

        var response = await BrowseAsync(client, "Stockfish", FakeEngine.ExePath);

        response.EnsureSuccessStatusCode();
        Assert.Equal(@"C:\Tools\stockfish\stockfish-windows-x86-64-avx2.exe", (await response.Content.ReadFromJsonAsync<PickedProgramDto>())!.Path);
        var request = Assert.Single(picker.Requests);
        Assert.Equal(Path.GetDirectoryName(FakeEngine.ExePath), request.InitialFolder);
        Assert.Contains("Stockfish", request.Title);
        Assert.Contains("*.exe", request.Filter);
    }

    [Fact]
    public async Task Without_a_current_program_the_dialog_starts_in_downloads()
    {
        var picker = new FakeFilePicker(null);
        using var factory = WithPicker(picker);
        var client = factory.CreateClient();

        await BrowseAsync(client, "Lc0", "");

        Assert.EndsWith("Downloads", Assert.Single(picker.Requests).InitialFolder);
    }

    [Fact]
    public async Task Cancelling_the_dialog_returns_no_path()
    {
        using var factory = WithPicker(new FakeFilePicker(null));
        var client = factory.CreateClient();

        var response = await BrowseAsync(client, "Lc0", "");

        response.EnsureSuccessStatusCode();
        Assert.Null((await response.Content.ReadFromJsonAsync<PickedProgramDto>())!.Path);
    }

    [Fact]
    public async Task Only_one_dialog_opens_at_a_time()
    {
        var hold = new TaskCompletionSource();
        var picker = new FakeFilePicker(@"C:\x.exe", hold);
        using var factory = WithPicker(picker);
        var client = factory.CreateClient();

        var first = BrowseAsync(client, "Stockfish", "");
        await Wait.UntilAsync(() => picker.Requests.Count == 1);
        var second = await BrowseAsync(client, "Stockfish", "");
        hold.SetResult();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        (await first).EnsureSuccessStatusCode();
    }
}
