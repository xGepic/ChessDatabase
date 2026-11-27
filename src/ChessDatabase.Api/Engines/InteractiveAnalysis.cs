using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;
using Microsoft.AspNetCore.SignalR;

namespace ChessDatabase.Api.Engines;

/// <summary>One Engine line: score from White's side, moves in SAN and UCI.</summary>
/// <param name="Rank">1 for the Engine's best line.</param>
/// <param name="WhitePercent">White's winning chance (0–100) by the Lichess formula, for the evaluation bar.</param>
public sealed record AnalysisLineDto(
    int Rank, int Depth, int? Cp, int? Mate, double WhitePercent, IReadOnlyList<string> San, IReadOnlyList<string> Uci);

/// <param name="RequestId">Echoes the request, so the UI can drop updates for a position it has left.</param>
/// <param name="Lines">Best first. Empty when the position has no legal moves.</param>
public sealed record AnalysisUpdateDto(int RequestId, IReadOnlyList<AnalysisLineDto> Lines);

/// <summary>
/// Interactive Analysis over SignalR. The UI calls Analyse with the moves from the start to the
/// position on the board; the Interactive Analysis engine searches until the next Analyse or Stop
/// (or the time limit) and streams its lines back as "lines" messages. Closing the connection
/// (leaving the Game) stops the Engine too.
/// </summary>
public sealed class AnalysisHub(InteractiveAnalysis analysis) : Hub
{
    public const string Path = "/hubs/analysis";

    public Task Analyse(int requestId, string[] moves) => analysis.AnalyseAsync(Context.ConnectionId, requestId, moves);

    public Task Stop() => analysis.StopAsync(Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception) => analysis.CloseAsync(Context.ConnectionId);
}

/// <summary>Keeps one Engine session per connection.</summary>
public sealed class InteractiveAnalysis(EngineHost host, IServiceScopeFactory scopes, IHubContext<AnalysisHub> hub) : IAsyncDisposable
{
    /// <summary>
    /// A running Engine and the settings it was started with. A change in Settings (another
    /// Interactive Analysis engine, or this one's path, options or threads) restarts it on the next move.
    /// </summary>
    private sealed record EngineSession(InteractiveSession Session, string Settings);

    /// <summary>
    /// Per connection: a gate so its calls run one at a time, its session, and whether it has
    /// closed. A call still waiting at the gate when the connection closes finds it closed and
    /// starts nothing, so no Engine outlives its Game view.
    /// </summary>
    private sealed class Connection
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public EngineSession? Running { get; set; }
        public bool Closed { get; set; }
    }

    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public async Task AnalyseAsync(string connectionId, int requestId, IReadOnlyList<string> moves)
    {
        var connection = _connections.GetOrAdd(connectionId, _ => new Connection());
        var position = Position.Initial;
        foreach (var text in moves)
        {
            if (!Move.TryParseUci(text, out var move) || !position.IsLegal(move))
                throw new HubException($"'{text}' is not a legal move in that position.");
            position = position.Play(move);
        }

        await connection.Gate.WaitAsync();
        try
        {
            if (connection.Closed) return;
            EngineRecord definition;
            using (var scope = scopes.CreateScope())
                definition = await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().InteractiveEngineAsync();
            var settings = SettingsOf(definition);

            if (connection.Running is { } running && (running.Session.HasEnded || running.Settings != settings))
            {
                await running.Session.DisposeAsync();
                connection.Running = null;
            }
            connection.Running ??= new EngineSession(await StartSessionAsync(connectionId, definition), settings);
            try
            {
                await connection.Running.Session.AnalyseAsync(requestId, moves, position);
            }
            catch (EngineException e)
            {
                await connection.Running.Session.DisposeAsync();
                connection.Running = null;
                throw new HubException(e.Message);
            }
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    /// <summary>Everything that needs a new Engine process when it changes.</summary>
    private static string SettingsOf(EngineRecord definition) => JsonSerializer.Serialize(new
    {
        definition.Id, definition.Path, definition.Arguments, definition.Threads, definition.HashMb, definition.MultiPv,
        definition.InteractiveSecondsLimit, definition.Options,
    });

    /// <summary>The Owner switched the Engine off: its process ends, the connection stays usable.</summary>
    public Task StopAsync(string connectionId) => EndSessionAsync(connectionId, close: false);

    /// <summary>The connection is gone (Game view left, browser tab closed).</summary>
    public Task CloseAsync(string connectionId) => EndSessionAsync(connectionId, close: true);

    private async Task EndSessionAsync(string connectionId, bool close)
    {
        if (!_connections.TryGetValue(connectionId, out var connection)) return;
        await connection.Gate.WaitAsync();
        try
        {
            if (connection.Running is { } running) await running.Session.DisposeAsync();
            connection.Running = null;
            if (close)
            {
                connection.Closed = true;
                _connections.TryRemove(connectionId, out _);
            }
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    /// <exception cref="HubException">The Engine could not be started.</exception>
    private async Task<InteractiveSession> StartSessionAsync(string connectionId, EngineRecord definition)
    {
        try
        {
            var engine = await host.StartAsync(definition, definition.MultiPv);
            var client = hub.Clients.Client(connectionId);
            return new InteractiveSession(engine, definition.MultiPv, definition.InteractiveSecondsLimit,
                update => client.SendAsync("lines", update),
                message => client.SendAsync("engineError", message));
        }
        catch (EngineException e)
        {
            throw new HubException(e.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_connections.Values.Select(c => c.Running?.Session).OfType<InteractiveSession>().Select(s => s.DisposeAsync().AsTask()));
        _connections.Clear();
    }
}

/// <summary>
/// One running Engine following the Owner around a Game. Switching position stops the current
/// search and waits for its "bestmove", so no line from the old position is shown for the new one.
/// </summary>
public sealed class InteractiveSession : IAsyncDisposable
{
    /// <summary>Lines are sent when a full set of lines at a depth is in, or at least this often.</summary>
    private static readonly TimeSpan MaxQuiet = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a stopped search may take to answer before the Engine counts as hung.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly UciEngine _engine;
    private readonly int _multiPv;
    private readonly int? _secondsLimit;
    private readonly Func<AnalysisUpdateDto, Task> _publish;
    private readonly Func<string, Task> _fail;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _readLoop;
    private volatile Search? _search;
    private volatile bool _hasEnded;
    private bool _disposed;

    private sealed class Search(int requestId, Position position, int expectedLines)
    {
        public int RequestId { get; } = requestId;
        public Position Position { get; } = position;
        public int ExpectedLines { get; } = expectedLines;
        public SortedDictionary<int, AnalysisLineDto> Lines { get; } = [];
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stopwatch SincePublished { get; } = Stopwatch.StartNew();
    }

    /// <param name="secondsLimit">Null: search until told otherwise.</param>
    public InteractiveSession(
        UciEngine engine, int multiPv, int? secondsLimit, Func<AnalysisUpdateDto, Task> publish, Func<string, Task> fail)
    {
        _engine = engine;
        _multiPv = multiPv;
        _secondsLimit = secondsLimit;
        _publish = publish;
        _fail = fail;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>True once the Engine process has gone away (crashed, quit or stopped answering).</summary>
    public bool HasEnded => _hasEnded;

    public async Task AnalyseAsync(int requestId, IReadOnlyList<string> moves, Position position)
    {
        await StopSearchAsync();
        var legal = position.LegalMoves().Count;
        if (legal == 0)
        {
            await _publish(new AnalysisUpdateDto(requestId, []));
            return;
        }
        _search = new Search(requestId, position, Math.Min(_multiPv, legal));
        _engine.SetPosition(moves);
        _engine.Send(_secondsLimit is { } seconds ? $"go movetime {seconds * 1000}" : "go infinite");
    }

    private async Task StopSearchAsync()
    {
        if (_search is not { } search) return;
        _engine.Send("stop");
        try
        {
            await search.Finished.Task.WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            _hasEnded = true;
            throw new EngineException("The Engine stopped answering. Turn it on again to restart it.");
        }
        _search = null;
    }

    private async Task ReadLoopAsync()
    {
        Search? unpublished = null;
        try
        {
            while (true)
            {
                var next = _engine.ReadLineAsync(_stopping.Token).AsTask();
                // Lines that are in but not sent yet go out after a quiet spell, even if the set is incomplete.
                if (unpublished is not null && await Task.WhenAny(next, Task.Delay(MaxQuiet, _stopping.Token)) != next)
                {
                    if (unpublished == _search) await PublishAsync(unpublished);
                    unpublished = null;
                }
                var line = await next;
                var search = _search;
                if (search is null) continue;
                if (line.StartsWith("bestmove", StringComparison.Ordinal))
                {
                    if (unpublished == search) await PublishAsync(search);
                    unpublished = null;
                    search.Finished.TrySetResult();
                    continue;
                }
                if (!UciInfo.TryParse(line, out var info) || info.MultiPv > search.ExpectedLines) continue;
                var (uci, san) = info.LegalLine(search.Position);
                if (uci.Count == 0) continue;
                var score = info.Score.ForWhite(search.Position.SideToMove);
                search.Lines[info.MultiPv] = new AnalysisLineDto(
                    info.MultiPv, info.Depth, score.Cp, score.Mate, Math.Round(WinningChances.WhitePercent(score), 1), san, uci);

                if (info.MultiPv == search.ExpectedLines || search.SincePublished.Elapsed >= MaxQuiet)
                {
                    await PublishAsync(search);
                    unpublished = null;
                }
                else unpublished = search;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception e) when (e is ChannelClosedException or IOException)
        {
            await EndAsync("The Engine stopped unexpectedly. Turn it on again to restart it.");
        }
    }

    private async Task EndAsync(string message)
    {
        _hasEnded = true;
        _search?.Finished.TrySetResult();
        if (_disposed) return;
        try { await _fail(message); } catch (Exception) { } // the client may be gone as well
    }

    /// <summary>Sends the lines; a client that has gone away doesn't stop the Engine's output being read.</summary>
    private async Task PublishAsync(Search search)
    {
        search.SincePublished.Restart();
        try
        {
            await _publish(new AnalysisUpdateDto(search.RequestId, [.. search.Lines.Values]));
        }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _engine.DisposeAsync();
        _stopping.Cancel();
        await _readLoop;
    }
}
