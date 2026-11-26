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
/// The Assessment module: turns the Engine's evaluations of a Game's positions into an Engine
/// Assessment (winning chance per position, Move Classification and Accuracy per move, Accuracy per
/// side). Lichess's formulas throughout. Pure functions, no I/O.
/// </summary>
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
    /// Assesses the moves of a Game from the initial position: <paramref name="positions"/> has one
    /// evaluation per position, the starting one included, so one more than <paramref name="played"/>.
    /// A move that is the Engine's own choice is never classified.
    /// </summary>
    public static GameAssessment Assess(IReadOnlyList<PositionEvaluation> positions, IReadOnlyList<string> played)
    {
        if (positions.Count != played.Count + 1)
            throw new ArgumentException("There must be one evaluation per position, including the starting one.");

        var whitePercents = positions
            .Select((p, i) => WinningChances.WhitePercent(p.Score, WinningChances.WhiteMoved(i) ? Color.White : Color.Black))
            .ToList();

        var moves = new List<AssessedMove>(played.Count);
        for (var i = 0; i < played.Count; i++)
        {
            var whiteMoved = WinningChances.WhiteMoved(i);
            var before = whiteMoved ? whitePercents[i] : 100 - whitePercents[i];
            var after = whiteMoved ? whitePercents[i + 1] : 100 - whitePercents[i + 1];
            var loss = Math.Max(0, (before - after) * 2); // percent of winning chance → points on the −100…+100 scale
            var playedBest = positions[i].Line is [var best, ..] && best == played[i];
            moves.Add(new AssessedMove(i + 1, loss, playedBest ? null : Classify(loss), Accuracy.OfMove(before, after)));
        }

        var (white, black) = Accuracy.OfGame(whitePercents);
        return new GameAssessment(whitePercents, moves, white, black);
    }
}
