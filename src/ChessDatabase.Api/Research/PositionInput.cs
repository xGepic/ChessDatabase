using ChessDatabase.Rules;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Research;

/// <summary>Reads a position sent as a FEN (Research endpoints, diagrams).</summary>
public static class PositionInput
{
    /// <summary>
    /// Reads the FEN. Returns a 400 problem when it is not a FEN or not a position that can occur
    /// (Chess Rules decide), otherwise null.
    /// </summary>
    public static IResult? Read(string? fen, out Position position)
    {
        position = Position.Initial;
        try
        {
            position = Position.FromFen(fen ?? "");
        }
        catch (FormatException e)
        {
            return Invalid("fen", e.Message);
        }
        return position.SetupProblem() is { } reason ? Invalid("fen", reason) : null;
    }
}
