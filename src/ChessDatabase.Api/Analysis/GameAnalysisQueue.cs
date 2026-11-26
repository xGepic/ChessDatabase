using ChessDatabase.Api.Engines;
using Microsoft.AspNetCore.SignalR;

namespace ChessDatabase.Api.Analysis;

/// <param name="Done">Positions evaluated so far.</param>
/// <param name="Total">Positions in the main line, the starting one included.</param>
public sealed record RunningAnalysisDto(int GameId, int Done, int Total);

/// <param name="Queued">Games waiting, in the order they will run.</param>
/// <param name="BatchDone">Games finished since the queue was last empty.</param>
/// <param name="BatchTotal">Games queued since the queue was last empty, running and waiting ones included.</param>
public sealed record AnalysisQueueDto(RunningAnalysisDto? Running, IReadOnlyList<int> Queued, int BatchDone, int BatchTotal);

/// <param name="Error">Why it did not finish (cancelled, Engine missing, ...); null on success.</param>
public sealed record AnalysisFinishedDto(int GameId, string? Error);

/// <summary>
/// Runs Game Analysis in the background, one Game at a time, while the Owner keeps working.
/// Changes go to every connected UI over the analysis hub: "analysisQueue" with the whole queue,
/// and "analysisFinished" when a Game is done.
/// </summary>
public sealed class GameAnalysisQueue(GameAnalyzer analyzer, IHubContext<AnalysisHub> hub, ILogger<GameAnalysisQueue> log)
    : BackgroundService
{
    private readonly Lock _lock = new();
    private readonly List<(int GameId, int? MsPerMove)> _pending = [];
    private readonly SemaphoreSlim _work = new(0);
    /// <summary>One status message at a time, so the UI gets them in the order the queue changed.</summary>
    private readonly SemaphoreSlim _sending = new(1, 1);
    private RunningAnalysisDto? _running;
    private CancellationTokenSource? _current;
    private int _batchDone, _batchTotal;

    public AnalysisQueueDto Status()
    {
        lock (_lock) return new AnalysisQueueDto(_running, [.. _pending.Select(p => p.GameId)], _batchDone, _batchTotal);
    }

    /// <summary>Adds Games to the end of the queue. A Game already waiting or running is not added twice.</summary>
    public Task<AnalysisQueueDto> EnqueueAsync(IEnumerable<int> gameIds, int? msPerMove)
    {
        var added = 0;
        lock (_lock)
        {
            foreach (var id in gameIds.Distinct())
            {
                if (_running?.GameId == id || _pending.Any(p => p.GameId == id)) continue;
                _pending.Add((id, msPerMove));
                _batchTotal++;
                added++;
            }
        }
        if (added > 0) _work.Release(added);
        return PublishAsync();
    }

    /// <summary>Empties the queue and stops the running analysis. Finished Engine Assessments stay.</summary>
    public Task<AnalysisQueueDto> CancelAsync()
    {
        lock (_lock)
        {
            _batchTotal -= _pending.Count;
            _pending.Clear();
            _current?.Cancel();
        }
        return PublishAsync();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _work.WaitAsync(stoppingToken);
            (int GameId, int? MsPerMove) job;
            CancellationTokenSource cancel;
            lock (_lock)
            {
                if (_pending.Count == 0) continue; // cancelled while waiting
                job = _pending[0];
                _pending.RemoveAt(0);
                _running = new RunningAnalysisDto(job.GameId, 0, 0);
                cancel = _current = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            }
            await PublishAsync();

            string? error = null;
            try
            {
                await analyzer.AnalyseAsync(job.GameId, job.MsPerMove, (done, total) =>
                {
                    lock (_lock) _running = new RunningAnalysisDto(job.GameId, done, total);
                    return PublishAsync();
                }, cancel.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                error = "Cancelled.";
            }
            catch (EngineException e)
            {
                error = e.Message;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Game Analysis of Game {GameId} failed", job.GameId);
                error = "Game Analysis failed: " + e.Message;
            }
            finally
            {
                lock (_lock)
                {
                    _running = null;
                    _current = null;
                    _batchDone++;
                    if (_pending.Count == 0) _batchDone = _batchTotal = 0;
                }
                cancel.Dispose();
            }
            await SendAsync("analysisFinished", new AnalysisFinishedDto(job.GameId, error));
            await PublishAsync();
        }
    }

    /// <summary>Sends the current queue to every UI and returns it.</summary>
    private async Task<AnalysisQueueDto> PublishAsync()
    {
        await _sending.WaitAsync();
        try
        {
            var status = Status();
            await SendAsync("analysisQueue", status);
            return status;
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>A UI that can't be reached must not stop the queue.</summary>
    private async Task SendAsync(string method, object message)
    {
        try
        {
            await hub.Clients.All.SendAsync(method, message, CancellationToken.None);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Could not send {Method} to the UI", method);
        }
    }
}
