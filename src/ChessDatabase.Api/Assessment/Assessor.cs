using ChessDatabase.Api.Engines;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Assessment;

/// <summary>How much winning chance a move gave away (see <see cref="Assessor.Classify"/>).</summary>
public enum MoveClassification
{
    Inaccuracy,
    Mistake,
    Blunder,
}

/// <summary>The Engine's verdict on one position, as the Engine reports it: score from the side to move.</summary>
/// <param name="Line">The Engine's best line in UCI; its first move is the best move. Empty when no move is legal.</param>
public sealed record PositionEvaluation(EngineScore Score, IReadOnlyList<string> Line);

/// <param name="Loss">Winning chance the move gave away, in points on Lichess's scale (−100 to +100); never negative.</param>
/// <param name="Accuracy">How close the move was to the Engine's, 0–100.</param>
public sealed record AssessedMove(int Ply, double Loss, MoveClassification? Classification, double Accuracy);

/// <param name="WhitePercents">White's winning chance (0–100) in each position, from the start.</param>
/// <param name="WhiteAccuracy">Null when White made no move.</param>
public sealed record GameAssessment(
    IReadOnlyList<double> WhitePercents, IReadOnlyList<AssessedMove> Moves, double? WhiteAccuracy, double? BlackAccuracy);

/// <summary>
/// Turns raw Engine evaluations into Winning chances, Move Classifications and Accuracy.
/// Lichess's formulas throughout. Pure functions, no I/O.
/// </summary>
public static class WinningChances
{
    private const double Multiplier = -0.00368208;
    private const int CpCeiling = 1000;

    /// <summary>Lichess "winning chances": −1 (Black wins) to +1 (White wins). Mate counts as the ±1000 cp ceiling.</summary>
    public static double White(EngineScore whiteScore)
    {
        var cp = whiteScore.Mate is { } mate
            ? (mate >= 0 ? CpCeiling : -CpCeiling)
            : Math.Clamp(whiteScore.Cp ?? 0, -CpCeiling, CpCeiling);
        return Math.Clamp(2 / (1 + Math.Exp(Multiplier * cp)) - 1, -1, 1);
    }

    /// <summary>White's winning chance in percent, for a score from White's side.</summary>
    public static double WhitePercent(EngineScore whiteScore) => 50 + 50 * White(whiteScore);

    /// <summary>
    /// White's winning chance in percent, for a score as the Engine reports it (from the side to
    /// move). "Mate 0" means the side to move is checkmated.
    /// </summary>
    public static double WhitePercent(EngineScore score, Color sideToMove)
    {
        if (score.Mate == 0) return WhitePercent(new EngineScore(null, sideToMove == Color.White ? -1 : 1));
        return WhitePercent(score.ForWhite(sideToMove));
    }
}

/// <summary>Lichess's Accuracy: per move from the winning chance kept, per side a volatility-weighted mean.</summary>
public static class Accuracy
{
    /// <summary>Accuracy of one move, from the mover's winning chance (percent) before and after it.</summary>
    public static double OfMove(double before, double after)
    {
        if (after >= before) return 100;
        var raw = 103.1668100711649 * Math.Exp(-0.04354415386753951 * (before - after)) - 3.166924740191411;
        return Math.Clamp(raw + 1, 0, 100);
    }

    /// <summary>
    /// Accuracy per side: the mean of the weighted mean (weights from how volatile the game was
    /// around each move) and the harmonic mean of the move accuracies.
    /// </summary>
    public static (double? White, double? Black) OfGame(IReadOnlyList<double> whitePercents, bool firstMoveByBlack)
    {
        var moves = whitePercents.Count - 1;
        if (moves < 1) return (null, null);
        var windowSize = Math.Clamp(moves / 10, 2, 8);
        var windows = Enumerable.Repeat(whitePercents.Take(windowSize).ToList(), Math.Min(windowSize, whitePercents.Count) - 2)
            .Concat(Sliding(whitePercents, windowSize))
            .ToList();

        List<(double Accuracy, double Weight)> white = [], black = [];
        for (var i = 0; i < moves; i++)
        {
            var weight = Math.Clamp(StandardDeviation(windows[i]), 0.5, 12);
            var whiteMoved = i % 2 == 0 != firstMoveByBlack;
            var (before, after) = (whitePercents[i], whitePercents[i + 1]);
            if (whiteMoved) white.Add((OfMove(before, after), weight));
            else black.Add((OfMove(100 - before, 100 - after), weight));
        }
        return (Combine(white), Combine(black));
    }

    private static double? Combine(List<(double Accuracy, double Weight)> moves)
    {
        if (moves.Count == 0) return null;
        var weighted = moves.Sum(m => m.Accuracy * m.Weight) / moves.Sum(m => m.Weight);
        var harmonic = moves.Count / moves.Sum(m => 1 / Math.Max(1, m.Accuracy));
        return (weighted + harmonic) / 2;
    }

    private static IEnumerable<List<double>> Sliding(IReadOnlyList<double> values, int size)
    {
        if (values.Count <= size) { yield return [.. values]; yield break; }
        for (var i = 0; i + size <= values.Count; i++) yield return [.. values.Skip(i).Take(size)];
    }

    private static double StandardDeviation(List<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }
}

public static class Assessor
{
    /// <summary>Thresholds in points of winning chance lost, on Lichess's −100…+100 scale (as Lichess classifies).</summary>
    public const double InaccuracyFrom = 10, MistakeFrom = 20, BlunderFrom = 30;

    public static MoveClassification? Classify(double loss) => loss switch
    {
        >= BlunderFrom => MoveClassification.Blunder,
        >= MistakeFrom => MoveClassification.Mistake,
        >= InaccuracyFrom => MoveClassification.Inaccuracy,
        _ => null,
    };

    /// <summary>
    /// Assesses a line of moves from its evaluations: <paramref name="positions"/> has one entry per
    /// position, the one before the first move included, so one more than <paramref name="played"/>.
    /// </summary>
    public static GameAssessment Assess(IReadOnlyList<PositionEvaluation> positions, IReadOnlyList<string> played, bool firstMoveByBlack = false)
    {
        if (positions.Count != played.Count + 1)
            throw new ArgumentException("There must be one evaluation per position, including the starting one.");

        var whitePercents = positions
            .Select((p, i) => WinningChances.WhitePercent(p.Score, i % 2 == 0 != firstMoveByBlack ? Color.White : Color.Black))
            .ToList();

        var moves = new List<AssessedMove>(played.Count);
        for (var i = 0; i < played.Count; i++)
        {
            var whiteMoved = i % 2 == 0 != firstMoveByBlack;
            var before = whiteMoved ? whitePercents[i] : 100 - whitePercents[i];
            var after = whiteMoved ? whitePercents[i + 1] : 100 - whitePercents[i + 1];
            var loss = Math.Max(0, (before - after) * 2); // percent of winning chance → points on the −100…+100 scale
            var playedBest = positions[i].Line.FirstOrDefault() == played[i];
            moves.Add(new AssessedMove(i + 1, loss, playedBest ? null : Classify(loss), Accuracy.OfMove(before, after)));
        }

        var (white, black) = Accuracy.OfGame(whitePercents, firstMoveByBlack);
        return new GameAssessment(whitePercents, moves, white, black);
    }
}
