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

    /// <param name="msPerMove">Overrides the Engine's Game Analysis time per position.</param>
    /// <param name="progress">Positions done so far, out of the total.</param>
    public async Task AnalyseAsync(int gameId, int? msPerMove, Action<int, int> progress, CancellationToken cancellationToken)
    {
        EngineRecord definition;
        List<string> mainLine;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            definition = await db.Engines.AsNoTracking().SingleAsync(e => e.Id == EngineRecord.StockfishId, cancellationToken);
            var nodes = await db.MoveNodes.AsNoTracking().Where(n => n.GameId == gameId).ToListAsync(cancellationToken);
            mainLine = [.. new MoveTree(nodes).MainLine().Select(n => n.Uci)];
        }
        var time = msPerMove ?? definition.AnalysisMsPerMove;

        var positions = new List<StoredEvaluation>(mainLine.Count + 1);
        string engineName;
        await using (var engine = await host.StartAsync(definition, multiPv: 1, cancellationToken))
        {
            engine.Send("ucinewgame");
            var position = Position.Initial;
            progress(0, mainLine.Count + 1);
            for (var ply = 0; ply <= mainLine.Count; ply++)
            {
                if (ply > 0) position = position.Play(ParseUci(mainLine[ply - 1]));
                positions.Add(await EvaluateAsync(engine, mainLine.Take(ply), position, time, cancellationToken));
                progress(ply + 1, mainLine.Count + 1);
            }
            engineName = engine.Name;
        }

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
            if (!await db.Games.AnyAsync(g => g.Id == gameId, cancellationToken)) return; // deleted meanwhile
            await db.EngineAssessments.Where(a => a.GameId == gameId).ExecuteDeleteAsync(cancellationToken);
            db.EngineAssessments.Add(new EngineAssessmentRecord
            {
                GameId = gameId,
                EngineName = engineName,
                Threads = definition.Threads,
                HashMb = definition.HashMb,
                MsPerMove = time,
                AnalysedAt = clock.GetUtcNow().UtcDateTime,
                MainLine = mainLine,
                Positions = positions,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<StoredEvaluation> EvaluateAsync(
        UciEngine engine, IEnumerable<string> moves, Position position, int msPerMove, CancellationToken cancellationToken)
    {
        // The rules settle positions without a legal move; the Engine isn't asked.
        if (position.LegalMoves().Count == 0)
            return position.IsCheck ? new StoredEvaluation(null, 0, []) : new StoredEvaluation(0, null, []);

        var list = string.Join(' ', moves);
        engine.Send(list.Length == 0 ? "position startpos" : $"position startpos moves {list}");
        engine.Send($"go movetime {msPerMove}");

        UciInfo? best = null;
        string? bestmove = null;
        using var stop = cancellationToken.Register(() => { try { engine.Send("stop"); } catch (IOException) { } });
        while (bestmove is null)
        {
            var line = await engine.ReadLineAsync(CancellationToken.None);
            if (line.StartsWith("bestmove", StringComparison.Ordinal))
                bestmove = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
            else if (UciInfo.TryParse(line, out var info) && info.MultiPv == 1)
                best = info;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var (uci, _) = (best ?? new UciInfo(0, 1, new EngineScore(0, null), [bestmove])).LegalLine(position);
        if (uci.Count == 0 && Move.TryParseUci(bestmove, out var only) && position.IsLegal(only)) uci = [only.ToUci()];
        var score = best?.Score ?? new EngineScore(0, null);
        return new StoredEvaluation(score.Cp, score.Mate, [.. uci.Take(StoredLineLength)]);
    }

    private static Move ParseUci(string uci) =>
        Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException($"Stored move '{uci}' is not UCI.");
}
