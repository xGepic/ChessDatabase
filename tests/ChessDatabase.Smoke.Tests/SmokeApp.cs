using System.Net.Http.Json;
using System.Text.Json;
using ChessDatabase.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
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
    private WebApplication? _lichess;

    /// <summary>The fake Lichess masters explorer's answer by FEN; null (the default) answers 503.</summary>
    public Func<string, string?> MastersJson { get; set; } = _ => null;

    /// <summary>The fake Lichess's Master Game PGNs by id.</summary>
    public Dictionary<string, string> MasterPgns { get; } = [];

    /// <summary>The fake Lichess tablebase's answer by FEN; null (the default) answers 503.</summary>
    public Func<string, string?> TablebaseJson { get; set; } = _ => null;
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
        var lichessUrl = await StartFakeLichessAsync();
        var builder = ChessDatabaseApp.CreateBuilder([], new WebApplicationOptions
        {
            Args = [$"--Collection:DatabasePath={databasePath}", $"--Masters:BaseUrl={lichessUrl}", $"--Tablebase:BaseUrl={lichessUrl}"],
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

    public async Task ImportAsync(string pgn)
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        (await http.PostAsJsonAsync("/api/import/text", new { pgn })).EnsureSuccessStatusCode();
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

    /// <summary>Adds a line (SAN from the start) to the Owner's Repertoire for a colour.</summary>
    public async Task AddRepertoireLineAsync(string side, params string[] moves)
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        (await http.PostAsJsonAsync($"/api/repertoire/{side}/lines", new { parentId = (int?)null, moves })).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Points the Engine at the scripted fake engine. Keys are FENs (first four fields), values the
    /// engine's "info" lines (text after "info ") and its best move; see tests/ChessDatabase.FakeEngine.
    /// </summary>
    public async Task UseFakeEngineAsync(Dictionary<string, (string[] Infos, string Bestmove)> positions, int lineDelayMs = 0)
    {
        var script = Path.Combine(_directory, "engine-script.json");
        await File.WriteAllTextAsync(script, JsonSerializer.Serialize(new
        {
            name = "Fake Engine 1.0",
            lineDelayMs,
            positions = positions.ToDictionary(p => p.Key, p => new { infos = p.Value.Infos, bestmove = p.Value.Bestmove }),
        }));
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        (await http.PutAsJsonAsync("/api/engines/1", new
        {
            path = Path.Combine(AppContext.BaseDirectory, "FakeEngine.exe"),
            arguments = $"\"{script}\"",
            threads = 1,
            hashMb = 16,
            multiPv = 3,
        })).EnsureSuccessStatusCode();
    }

    /// <summary>A stand-in for explorer.lichess.org on a loopback port, so smoke tests never go online.</summary>
    private async Task<string> StartFakeLichessAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _lichess = builder.Build();
        _lichess.MapGet("/masters", (string fen) => MastersJson(fen) is { } json
            ? Results.Text(json, "application/json")
            : Results.StatusCode(503));
        _lichess.MapGet("/standard", (string fen) => TablebaseJson(fen) is { } json
            ? Results.Text(json, "application/json")
            : Results.StatusCode(503));
        _lichess.MapGet("/masters/pgn/{id}", (string id) => MasterPgns.TryGetValue(id, out var pgn)
            ? Results.Text(pgn, "application/x-chess-pgn")
            : Results.NotFound());
        await _lichess.StartAsync();
        return _lichess.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
    }

    public async Task DisposeAsync()
    {
        if (_lichess is not null) await _lichess.DisposeAsync();
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
