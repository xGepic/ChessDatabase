using System.Threading.Channels;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Analysis;

/// <summary>
/// Game Analysis of one Game: the Engine evaluates every position of the main line for a fixed
/// time (MultiPV 1) and the result replaces the Game's Engine Assessment. The move tree is only read.
/// </summary>
public sealed class GameAnalyzer(EngineHost host, IServiceScopeFactory scopes, TimeProvider clock)
{
    /// <summary>Engine lines are stored up to this many moves.</summary>
    private const int StoredLineLength = 12;

    /// <summary>How much longer than the time per position the Engine may take before it counts as hung.</summary>
    private static readonly TimeSpan AnswerGrace = TimeSpan.FromSeconds(30);

    /// <param name="msPerMove">Overrides the Engine's Game Analysis time per position.</param>
    /// <param name="progress">Called with positions done so far and the total.</param>
    /// <exception cref="EngineException">The Engine could not be started, crashed or stopped answering.</exception>
    public async Task AnalyseAsync(int gameId, int? msPerMove, Func<int, int, Task> progress, CancellationToken cancellationToken)
    {
        EngineRecord definition;
        List<string> mainLine;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            definition = await db.AnalysisEngineAsync(cancellationToken);
            var nodes = await db.MoveNodes.AsNoTracking().Where(n => n.GameId == gameId).ToListAsync(cancellationToken);
            mainLine = [.. new MoveTree(nodes).MainLine().Select(n => n.Uci)];
        }
        var time = msPerMove ?? definition.AnalysisMsPerMove;

        var positions = new List<StoredEvaluation>(mainLine.Count + 1);
        string engineName;
        await using (var engine = await StartEngineAsync(definition, cancellationToken))
        {
            var line = Line.FromUci(mainLine);
            await progress(0, mainLine.Count + 1);
            for (var ply = 0; ply <= mainLine.Count; ply++)
            {
                positions.Add(await EvaluateAsync(engine, mainLine[..ply], line.Positions[ply], time, cancellationToken));
                await progress(ply + 1, mainLine.Count + 1);
            }
            engineName = engine.Name;
        }

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            if (!await db.Games.AnyAsync(g => g.Id == gameId, cancellationToken)) return; // deleted meanwhile
            // One step: nobody ever sees the Game without an Engine Assessment in between.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.EngineAssessments.Where(a => a.GameId == gameId).ExecuteDeleteAsync(cancellationToken);
            db.EngineAssessments.Add(new EngineAssessmentRecord
            {
                GameId = gameId,
                EngineName = engineName,
                EngineId = definition.Id,
                Threads = definition.Threads,
                HashMb = definition.HashMb,
                MsPerMove = time,
                AnalysedAt = clock.GetUtcNow().UtcDateTime,
                MainLine = mainLine,
                Positions = positions,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Evaluates one position the way Game Analysis evaluates each position (MultiPV 1):
    /// <paramref name="position"/>, reached by <paramref name="moves"/> (UCI) from the start.
    /// </summary>
    /// <param name="msPerMove">The time to think: pass an Engine Assessment's own, to compare with its evaluations.</param>
    /// <param name="engineId">
    /// The Engine to ask: pass the one that produced the Engine Assessment, as Engines score on
    /// different scales. When it is null or has been removed, the Game Analysis engine is asked.
    /// </param>
    /// <exception cref="EngineException">The Engine could not be started, crashed or stopped answering.</exception>
    public async Task<StoredEvaluation> EvaluateOnceAsync(
        IReadOnlyList<string> moves, Position position, int msPerMove, int? engineId, CancellationToken cancellationToken = default)
    {
        EngineRecord definition;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            definition = (engineId is { } id ? await db.EngineAsync(id, cancellationToken) : null)
                ?? await db.AnalysisEngineAsync(cancellationToken);
        }
        await using var engine = await StartEngineAsync(definition, cancellationToken);
        return await EvaluateAsync(engine, moves, position, msPerMove, cancellationToken);
    }

    private async Task<UciEngine> StartEngineAsync(EngineRecord definition, CancellationToken cancellationToken)
    {
        var engine = await host.StartAsync(definition, multiPv: 1, cancellationToken);
        engine.Send("ucinewgame");
        return engine;
    }

    private static async Task<StoredEvaluation> EvaluateAsync(
        UciEngine engine, IReadOnlyList<string> moves, Position position, int msPerMove, CancellationToken cancellationToken)
    {
        // The rules settle positions without a legal move; the Engine isn't asked.
        if (position.LegalMoves().Count == 0)
            return position.IsCheck ? new StoredEvaluation(null, 0, []) : new StoredEvaluation(0, null, []);

        engine.SetPosition(moves);
        engine.Send($"go movetime {msPerMove}");

        UciInfo? best = null;
        string? bestmove = null;
        // Cancelling stops the search; the Engine then still answers with "bestmove", which is read.
        using var stop = cancellationToken.Register(() => { try { engine.Send("stop"); } catch (IOException) { } });
        using var answer = new CancellationTokenSource(TimeSpan.FromMilliseconds(msPerMove) + AnswerGrace);
        try
        {
            while (bestmove is null)
            {
                var line = await engine.ReadLineAsync(answer.Token);
                if (line.StartsWith("bestmove", StringComparison.Ordinal))
                    bestmove = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
                else if (UciInfo.TryParse(line, out var info) && info.MultiPv == 1)
                    best = info;
            }
        }
        catch (OperationCanceledException) when (answer.IsCancellationRequested)
        {
            throw new EngineException("The Engine stopped answering.");
        }
        catch (ChannelClosedException e)
        {
            throw new EngineException("The Engine stopped unexpectedly.", e);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var engineLine = best?.Pv is { Count: > 0 } pv ? pv : [bestmove];
        var (legal, _) = position.PlayableLine(engineLine);
        var score = best?.Score ?? new EngineScore(0, null);
        return new StoredEvaluation(score.Cp, score.Mate, [.. legal.Take(StoredLineLength).Select(m => m.ToUci())]);
    }
}
