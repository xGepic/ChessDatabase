using ChessDatabase.Api.Engines;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Assessment;

/// <summary>
/// Converts Engine scores to winning chance with the Lichess formula. Pure functions, no I/O.
/// </summary>
public static class WinningChances
{
    private const double Multiplier = -0.00368208;
    private const int CpCeiling = 1000;

    /// <summary>Lichess's winning chance: −1 (Black wins) to +1 (White wins). Mate counts as the ±1000 cp ceiling.</summary>
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

    /// <summary>Whether White made the move at this index of a Game (it always starts from the initial position).</summary>
    public static bool WhiteMoved(int moveIndex) => moveIndex % 2 == 0;
}
