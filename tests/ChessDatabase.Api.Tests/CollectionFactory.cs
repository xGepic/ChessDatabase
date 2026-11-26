using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// Runs the real backend in-process against a fresh temporary SQLite file.
/// One factory per test keeps every test's Collection isolated.
/// </summary>
/// <param name="directory">
/// Folder for the database. Pass the same folder to a second factory to "restart" the app on the
/// same Collection; the caller then owns the folder and deletes it.
/// </param>
/// <param name="clock">Replaces the system clock, e.g. a FakeTimeProvider.</param>
/// <param name="dailyBackups">Daily backups are off in tests unless asked for.</param>
public sealed class CollectionFactory(string? directory = null, TimeProvider? clock = null, bool dailyBackups = false)
    : WebApplicationFactory<Program>
{
    private readonly bool _ownsDirectory = directory is null;

    public string Directory { get; } = directory ?? NewDirectory();

    public static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "ChessDatabaseTests", Guid.NewGuid().ToString("N"));

    /// <summary>Extra configuration, applied after the defaults above.</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Replaces services for one test, e.g. the HTTP handler behind an online source.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        System.IO.Directory.CreateDirectory(Directory);
        builder.UseSetting("Collection:DatabasePath", Path.Combine(Directory, "collection.db"));
        builder.UseSetting("Backup:Daily", dailyBackups ? "true" : "false");
        // Tests never reach Lichess: anything not faked goes to a port nothing listens on.
        builder.UseSetting("Masters:BaseUrl", "http://127.0.0.1:9/");
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            if (clock is not null) services.AddSingleton(clock);
            ConfigureServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (_ownsDirectory) DeleteDirectory(Directory);
    }

    public static void DeleteDirectory(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(path, recursive: true); } catch (IOException) { }
    }
}
