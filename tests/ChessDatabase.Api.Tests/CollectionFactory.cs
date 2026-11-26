using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// Runs the real backend in-process against a fresh temporary SQLite file.
/// One factory per test keeps every test's Collection isolated.
/// </summary>
public sealed class CollectionFactory : WebApplicationFactory<Program>
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ChessDatabaseTests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory);
        builder.UseSetting("Collection:DatabasePath", Path.Combine(_directory, "collection.db"));
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
