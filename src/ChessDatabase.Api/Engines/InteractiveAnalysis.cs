using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Engines;

/// <summary>One Engine line: score from White's side, moves in SAN and UCI.</summary>
/// <param name="Rank">1 for the Engine's best line.</param>
public sealed record AnalysisLineDto(int Rank, int Depth, int? Cp, int? Mate, IReadOnlyList<string> San, IReadOnlyList<string> Uci);

/// <param name="RequestId">Echoes the request, so the UI can drop updates for a position it has left.</param>
/// <param name="Lines">Best first. Empty when the position has no legal moves.</param>
public sealed record AnalysisUpdateDto(int RequestId, IReadOnlyList<AnalysisLineDto> Lines);

/// <summary>
/// Interactive Analysis over SignalR. The UI calls Analyse with the moves from the start to the
/// position on the board; the Engine searches until the next Analyse or Stop and streams its lines
/// back as "lines" messages. Closing the connection (leaving the Game) stops the Engine too.
/// </summary>
public sealed class AnalysisHub(InteractiveAnalysis analysis) : Hub
{
    public const string Path = "/hubs/analysis";

    public Task Analyse(int requestId, string[] moves) => analysis.AnalyseAsync(Context.ConnectionId, requestId, moves);

    public Task Stop() => analysis.StopAsync(Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception) => analysis.StopAsync(Context.ConnectionId);
}

/// <summary>Keeps one Engine session per connection.</summary>
public sealed class InteractiveAnalysis(EngineHost host, IServiceScopeFactory scopes, IHubContext<AnalysisHub> hub) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, InteractiveSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task AnalyseAsync(string connectionId, int requestId, IReadOnlyList<string> moves)
    {
        var position = Position.Initial;
        foreach (var text in moves)
        {
            if (!Move.TryParseUci(text, out var move) || !position.IsLegal(move))
                throw new HubException($"'{text}' is not a legal move in that position.");
            position = position.Play(move);
        }

        var gate = _locks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(connectionId, out var session) || session.HasEnded)
            {
                if (session is not null) await session.DisposeAsync();
                session = await StartSessionAsync(connectionId);
                _sessions[connectionId] = session;
            }
            await session.AnalyseAsync(requestId, moves, position);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync(string connectionId)
    {
        var gate = _locks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_sessions.TryRemove(connectionId, out var session)) await session.DisposeAsync();
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(connectionId, out _);
        }
    }

    private async Task<InteractiveSession> StartSessionAsync(string connectionId)
    {
        EngineRecord definition;
        using (var scope = scopes.CreateScope())
            definition = await scope.ServiceProvider.GetRequiredService<CollectionDbContext>()
                .Engines.AsNoTracking().SingleAsync(e => e.Id == EngineRecord.StockfishId);
        try
        {
            var engine = await host.StartAsync(definition, definition.MultiPv);
            var client = hub.Clients.Client(connectionId);
            return new InteractiveSession(engine, definition.MultiPv,
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
        await Task.WhenAll(_sessions.Values.Select(s => s.DisposeAsync().AsTask()));
        _sessions.Clear();
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

    private readonly UciEngine _engine;
    private readonly int _multiPv;
    private readonly Func<AnalysisUpdateDto, Task> _publish;
    private readonly Func<string, Task> _fail;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _readLoop;
    private volatile Search? _search;
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

    public InteractiveSession(UciEngine engine, int multiPv, Func<AnalysisUpdateDto, Task> publish, Func<string, Task> fail)
    {
        _engine = engine;
        _multiPv = multiPv;
        _publish = publish;
        _fail = fail;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>True once the Engine process has gone away (crashed or quit).</summary>
    public bool HasEnded { get; private set; }

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
        _engine.Send(moves.Count == 0 ? "position startpos" : $"position startpos moves {string.Join(' ', moves)}");
        _engine.Send("go infinite");
    }

    private async Task StopSearchAsync()
    {
        if (_search is not { } search) return;
        _engine.Send("stop");
        await search.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
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
                    search.Finished.TrySetResult();
                    continue;
                }
                if (!UciInfo.TryParse(line, out var info) || info.MultiPv > search.ExpectedLines) continue;
                var (uci, san) = info.LegalLine(search.Position);
                if (uci.Count == 0) continue;
                var score = info.Score.ForWhite(search.Position.SideToMove);
                search.Lines[info.MultiPv] = new AnalysisLineDto(info.MultiPv, info.Depth, score.Cp, score.Mate, san, uci);

                if (info.MultiPv == search.ExpectedLines || search.SincePublished.Elapsed >= MaxQuiet)
                {
                    await PublishAsync(search);
                    unpublished = null;
                }
                else unpublished = search;
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException)
        {
            HasEnded = true;
            _search?.Finished.TrySetResult();
            if (!_disposed) await _fail("The Engine stopped unexpectedly. Turn it on again to restart it.");
        }
    }

    private Task PublishAsync(Search search)
    {
        search.SincePublished.Restart();
        return _publish(new AnalysisUpdateDto(search.RequestId, [.. search.Lines.Values]));
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
