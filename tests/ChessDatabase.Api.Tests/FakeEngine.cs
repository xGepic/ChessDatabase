using System.Net.Http.Json;
using System.Text.Json;
using ChessDatabase.Rules;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// A scripted UCI engine (tests/ChessDatabase.FakeEngine) with its script and command log in a
/// temporary folder. Positions missing from the script get a default answer (see its Program.cs).
/// </summary>
public sealed class FakeEngine : IDisposable
{
    private readonly string _directory = CollectionFactory.NewDirectory();
    private readonly Dictionary<string, object> _positions = [];

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "FakeEngine.exe");

    public string Name { get; init; } = "Fake Engine 1.0";
    public int LineDelayMs { get; init; }

    /// <summary>The UCI options it announces; Lc0, for one, has no Hash.</summary>
    public string[] Options { get; init; } = ["Threads", "Hash", "MultiPV"];

    /// <summary>Exits when asked "isready", as Lc0 does when its weights file is missing.</summary>
    public bool QuitOnIsReady { get; init; }
    public string LogPath => Path.Combine(_directory, "commands.log");

    /// <summary>Canned output for the position after the given moves from the start.</summary>
    public FakeEngine Answer(string[] moves, string bestmove, params string[] infos)
    {
        _positions[Key(moves)] = new { infos, bestmove };
        return this;
    }

    /// <summary>The arguments the engine is started with: its script, written now, and its log.</summary>
    public string Arguments()
    {
        Directory.CreateDirectory(_directory);
        var script = Path.Combine(_directory, "script.json");
        File.WriteAllText(script, JsonSerializer.Serialize(new { name = Name, lineDelayMs = LineDelayMs, options = Options, quitOnIsReady = QuitOnIsReady, positions = _positions }));
        return $"\"{script}\" --log \"{LogPath}\"";
    }

    /// <summary>Every command the engine has received, across all its runs.</summary>
    public string[] Commands()
    {
        if (!File.Exists(LogPath)) return [];
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream).ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string Key(params string[] moves)
    {
        var position = Position.Initial;
        foreach (var uci in moves)
            position = position.Play(Move.TryParseUci(uci, out var move) ? move : position.ParseSan(uci));
        return string.Join(' ', position.ToFen().Split(' ').Take(4));
    }

    public void Dispose() => CollectionFactory.DeleteDirectory(_directory);
}

public sealed record AnalysisLineDto(int Rank, int Depth, int? Cp, int? Mate, double WhitePercent, List<string> San, List<string> Uci);

public sealed record AnalysisUpdateDto(int RequestId, List<AnalysisLineDto> Lines);

public static class EngineTestExtensions
{
    public static async Task<HttpResponseMessage> PutEngineAsync(this HttpClient client, object body) =>
        await client.PutAsJsonAsync("/api/engines/1", body);

    public static async Task UseAsync(
        this HttpClient client, FakeEngine engine, int threads = 16, int hashMb = 8192, int multiPv = 3, int? interactiveSecondsLimit = null)
    {
        var response = await client.PutEngineAsync(new
        {
            path = FakeEngine.ExePath, arguments = engine.Arguments(), threads, hashMb, multiPv, interactiveSecondsLimit,
        });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A SignalR connection to the analysis hub, collecting every update it receives.</summary>
    public static async Task<AnalysisClient> ConnectAnalysisAsync(this CollectionFactory factory)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/analysis"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var client = new AnalysisClient(connection);
        await connection.StartAsync();
        return client;
    }
}

public sealed class AnalysisClient : IAsyncDisposable
{
    private readonly List<AnalysisUpdateDto> _updates = [];
    private readonly List<string> _errors = [];

    public AnalysisClient(HubConnection connection)
    {
        Connection = connection;
        connection.On<AnalysisUpdateDto>("lines", u => { lock (_updates) _updates.Add(u); });
        connection.On<string>("engineError", e => { lock (_errors) _errors.Add(e); });
    }

    public HubConnection Connection { get; }

    public IReadOnlyList<AnalysisUpdateDto> Updates { get { lock (_updates) return [.. _updates]; } }

    public IReadOnlyList<string> Errors { get { lock (_errors) return [.. _errors]; } }

    public Task AnalyseAsync(int requestId, params string[] moves) => Connection.InvokeAsync("Analyse", requestId, moves);

    public Task StopAsync() => Connection.InvokeAsync("Stop");

    /// <summary>Waits for an update that satisfies the condition and returns it.</summary>
    public async Task<AnalysisUpdateDto> WaitForAsync(Func<AnalysisUpdateDto, bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (Updates.LastOrDefault(condition) is { } update) return update;
            await Task.Delay(25);
        }
        throw new TimeoutException("No such update. Received: " + JsonSerializer.Serialize(Updates));
    }

    public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
}

public static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string because = "")
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition(), because);
    }
}
