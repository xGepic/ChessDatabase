using System.Net.Http.Json;
using System.Text.Json;
using ChessDatabase.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ChessDatabase.Smoke.Tests;

/// <summary>
/// Runs the real app (backend + built Angular UI) on a loopback port against a temporary
/// Collection, and drives it with Playwright's Chromium.
/// </summary>
public sealed class SmokeApp : IAsyncLifetime
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ChessDatabaseSmoke", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string BaseUrl { get; private set; } = "";
    public IPage Page { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var uiRoot = Path.Combine(RepositoryRoot(), "ui", "dist", "ui", "browser");
        if (!File.Exists(Path.Combine(uiRoot, "index.html")))
            throw new InvalidOperationException($"The UI is not built ({uiRoot}). Run ./build.ps1 test.");

        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "collection.db");
        var builder = ChessDatabaseApp.CreateBuilder([], new WebApplicationOptions
        {
            Args = [$"--Collection:DatabasePath={databasePath}"],
            EnvironmentName = "Testing",
            ContentRootPath = _directory,
            WebRootPath = uiRoot,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = ChessDatabaseApp.Build(builder);
        await _app.StartAsync();
        BaseUrl = _app.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true, Channel = "msedge" });
        Page = await _browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        Page.SetDefaultTimeout(10_000);
    }

    public async Task SetOwnerNameAsync(string name)
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        (await http.PutAsJsonAsync("/api/settings", new { ownerName = name })).EnsureSuccessStatusCode();
    }

    /// <summary>Creates a Game with the given main line straight through the API, to start a test mid-story.</summary>
    public async Task<int> CreateGameAsync(params string[] moves)
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var player = await (await http.PostAsJsonAsync("/api/players", new { name = "Fixture Opponent" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var game = await (await http.PostAsJsonAsync("/api/games",
                new { ownerColour = "white", opponentId = player.GetProperty("id").GetInt32(), result = "1-0" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var gameId = game.GetProperty("id").GetInt32();
        int? parentId = null;
        foreach (var move in moves)
        {
            var response = await http.PostAsJsonAsync($"/api/games/{gameId}/moves", new { parentId, move });
            response.EnsureSuccessStatusCode();
            parentId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nodeId").GetInt32();
        }
        return gameId;
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChessDatabase.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
