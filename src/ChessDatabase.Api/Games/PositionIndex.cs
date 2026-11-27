using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Games;

/// <summary>
/// The position index (<see cref="PositionIndexRecord"/>): a Game's main line position by position,
/// for Position Search and the Opening Tree. Its rows are written by <see cref="GameMoves"/>.
/// </summary>
public static class PositionIndex
{
    /// <summary>A position as the index stores it.</summary>
    public static long Key(Position position) => unchecked((long)position.Hash);
}
