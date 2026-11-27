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
/// <param name="EngineId">The Engine definition the lines come from (two can run side by side).</param>
public sealed record AnalysisUpdateDto(int RequestId, IReadOnlyList<AnalysisLineDto> Lines, int EngineId);

/// <summary>
/// Interactive Analysis over SignalR. The UI calls Analyse with the moves from the start to the
/// position on the board, and optionally a second Engine to run beside the Interactive Analysis
/// one. Each Engine searches until the next Analyse or Stop (or its time limit) and streams its
/// lines back as "lines" messages; "engineError" says what went wrong with which Engine. Closing
/// the connection (leaving the Game) stops every Engine too.
/// </summary>
public sealed class AnalysisHub(InteractiveAnalysis analysis) : Hub
{
    public const string Path = "/hubs/analysis";

    /// <param name="secondEngineId">An Engine to run side by side; null for the Interactive Analysis engine alone.</param>
    /// <returns>The Interactive Analysis engine's id: the main lines are the ones with it.</returns>
    public Task<int> Analyse(int requestId, string[] moves, int? secondEngineId) =>
        analysis.AnalyseAsync(Context.ConnectionId, requestId, moves, secondEngineId);

    public Task Stop() => analysis.StopAsync(Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception) => analysis.CloseAsync(Context.ConnectionId);
}

/// <summary>How two Engines side by side share the CPU.</summary>
public static class ThreadShare
{
    /// <summary>
    /// The threads of two Engines running at once. Together they use no more than the busier one
    /// alone: the one with fewer threads keeps them, up to half, and the other gets the rest. Lc0,
    /// which mostly runs on the GPU, keeps its few threads and Stockfish gives up as many. (Each
    /// keeps at least one, so two single-threaded Engines run with one each.)
    /// </summary>
    public static (int A, int B) SideBySide(int a, int b)
    {
        var budget = Math.Max(a, b);
        var fewer = Math.Max(1, Math.Min(Math.Min(a, b), budget / 2));
        var rest = Math.Max(1, budget - fewer);
        return b < a ? (rest, fewer) : (fewer, rest);
    }
}

/// <summary>Keeps the Engine sessions of each connection: the Interactive Analysis engine and maybe a second one.</summary>
public sealed class InteractiveAnalysis(EngineHost host, IServiceScopeFactory scopes, IHubContext<AnalysisHub> hub) : IAsyncDisposable
{
    /// <summary>
    /// An Engine as it is to run: its definition and its share of the threads. Two runs with the
    /// same <see cref="Settings"/> can share a process; any change (path, options, threads) needs a new one.
    /// </summary>
    private sealed record EngineRun(EngineRecord Definition, int Threads)
    {
        public string Settings { get; } = JsonSerializer.Serialize(new
        {
            Definition.Path, Definition.Arguments, Threads, Definition.HashMb, Definition.MultiPv,
            Definition.InteractiveSecondsLimit, Definition.Options,
        });
    }

    /// <summary>A running Engine of a connection and the settings it was started with.</summary>
    private sealed record EngineSession(InteractiveSession Session, string Settings);

    /// <summary>
    /// Per connection: a gate so its calls run one at a time, its sessions by Engine id, and
    /// whether it has closed. A call still waiting at the gate when the connection closes finds it
    /// closed and starts nothing, so no Engine outlives its Game view.
    /// </summary>
    private sealed class Connection
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<int, EngineSession> Sessions { get; } = [];
        public bool Closed { get; set; }

        /// <summary>The Engine last asked for side by side.</summary>
        public int? SecondId { get; set; }

        /// <summary>
        /// A second Engine that failed. It isn't tried again on every move (one that hangs would
        /// hold up the main Engine each time), only once it is asked for afresh.
        /// </summary>
        public int? FailedSecondId { get; set; }
    }

    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    /// <param name="secondEngineId">Runs this Engine too, side by side; null for the Interactive Analysis engine alone.</param>
    /// <returns>The Interactive Analysis engine's id, so the UI knows which lines are the main ones.</returns>
    /// <exception cref="HubException">
    /// The moves are not legal, or the Interactive Analysis engine failed. A second Engine's
    /// failure goes out as "engineError" instead, and the main one keeps running.
    /// </exception>
    public async Task<int> AnalyseAsync(string connectionId, int requestId, IReadOnlyList<string> moves, int? secondEngineId = null)
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
            var (main, second, secondProblem) = await PlanAsync(connection, secondEngineId);
            if (connection.Closed) return main.Definition.Id;
            if (secondProblem is not null && secondEngineId is { } missing)
            {
                connection.FailedSecondId = missing;
                await SendErrorAsync(connectionId, secondProblem, missing);
            }

            // Engines no longer wanted, gone, or due other settings stop first, so the CPU is free for the rest.
            EngineRun[] runs = second is null ? [main] : [main, second];
            foreach (var (id, running) in connection.Sessions.ToList())
            {
                if (runs.Any(r => r.Definition.Id == id && r.Settings == running.Settings) && !running.Session.HasEnded) continue;
                await running.Session.DisposeAsync();
                connection.Sessions.Remove(id);
            }

            try
            {
                await AnalyseWithAsync(connection, connectionId, main, requestId, moves, position);
            }
            catch (EngineException e)
            {
                throw new HubException(e.Message);
            }
            if (second is not null)
            {
                try
                {
                    await AnalyseWithAsync(connection, connectionId, second, requestId, moves, position);
                }
                catch (EngineException e)
                {
                    connection.FailedSecondId = second.Definition.Id;
                    await SendErrorAsync(connectionId, e.Message, second.Definition.Id);
                }
            }
            return main.Definition.Id;
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    /// <summary>
    /// The main Engine and the second one, if it should run, with their shares of the threads.
    /// A second Engine that no longer exists gives a problem for its panel instead.
    /// </summary>
    private async Task<(EngineRun Main, EngineRun? Second, string? SecondProblem)> PlanAsync(Connection connection, int? secondEngineId)
    {
        if (secondEngineId != connection.SecondId) connection.FailedSecondId = null; // asked for afresh: try again
        connection.SecondId = secondEngineId;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var main = await db.InteractiveEngineAsync();
        if (secondEngineId is not { } id || id == main.Id || id == connection.FailedSecondId)
            return (new EngineRun(main, main.Threads), null, null);
        if (await db.EngineAsync(id) is not { } second)
            return (new EngineRun(main, main.Threads), null, "That Engine has been removed. Choose another one to compare with.");
        var (mainThreads, secondThreads) = ThreadShare.SideBySide(main.Threads, second.Threads);
        return (new EngineRun(main, mainThreads), new EngineRun(second, secondThreads), null);
    }

    /// <summary>Starts the Engine's session if it has none, then moves it to the position.</summary>
    /// <exception cref="EngineException">The Engine failed; its session is gone.</exception>
    private async Task AnalyseWithAsync(
        Connection connection, string connectionId, EngineRun run, int requestId, IReadOnlyList<string> moves, Position position)
    {
        var id = run.Definition.Id;
        if (!connection.Sessions.TryGetValue(id, out var running))
        {
            running = new EngineSession(await StartSessionAsync(connectionId, run), run.Settings);
            connection.Sessions[id] = running;
        }
        try
        {
            await running.Session.AnalyseAsync(requestId, moves, position);
        }
        catch (EngineException)
        {
            await running.Session.DisposeAsync();
            connection.Sessions.Remove(id);
            throw;
        }
    }

    /// <summary>The Owner switched the Engines off: their processes end, the connection stays usable.</summary>
    public Task StopAsync(string connectionId) => EndSessionsAsync(connectionId, close: false);

    /// <summary>The connection is gone (Game view left, browser tab closed).</summary>
    public Task CloseAsync(string connectionId) => EndSessionsAsync(connectionId, close: true);

    private async Task EndSessionsAsync(string connectionId, bool close)
    {
        if (!_connections.TryGetValue(connectionId, out var connection)) return;
        await connection.Gate.WaitAsync();
        try
        {
            await Task.WhenAll(connection.Sessions.Values.Select(r => r.Session.DisposeAsync().AsTask()));
            connection.Sessions.Clear();
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

    /// <exception cref="EngineException">The Engine could not be started.</exception>
    private async Task<InteractiveSession> StartSessionAsync(string connectionId, EngineRun run)
    {
        var definition = run.Definition;
        var engine = await host.StartAsync(definition, definition.MultiPv, run.Threads);
        return new InteractiveSession(engine, definition.Id, definition.MultiPv, definition.InteractiveSecondsLimit,
            update => hub.Clients.Client(connectionId).SendAsync("lines", update),
            message => SendErrorAsync(connectionId, message, definition.Id));
    }

    /// <summary>Tells the UI what went wrong with which Engine; a client that has gone away is ignored.</summary>
    private async Task SendErrorAsync(string connectionId, string message, int engineId)
    {
        try
        {
            await hub.Clients.Client(connectionId).SendAsync("engineError", message, engineId);
        }
        catch (Exception) { } // the client may be gone
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_connections.Values.SelectMany(c => c.Sessions.Values).Select(r => r.Session.DisposeAsync().AsTask()));
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
    private readonly int _engineId;
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
    /// <param name="engineId">The Engine definition, so its updates say where they come from.</param>
    public InteractiveSession(
        UciEngine engine, int engineId, int multiPv, int? secondsLimit, Func<AnalysisUpdateDto, Task> publish, Func<string, Task> fail)
    {
        _engine = engine;
        _engineId = engineId;
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
            await _publish(new AnalysisUpdateDto(requestId, [], _engineId));
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
            await _publish(new AnalysisUpdateDto(search.RequestId, [.. search.Lines.Values], _engineId));
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
