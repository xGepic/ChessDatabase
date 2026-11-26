using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Players;

namespace ChessDatabase.Api.Games;

/// <summary>One row of the game list. White and Black are display names: the Owner's name or the Opponent's.</summary>
public sealed record GameSummaryDto(
    int Id,
    string? Date,
    OwnerColor OwnerColor,
    PlayerDto Opponent,
    string White,
    string Black,
    int? WhiteElo,
    int? BlackElo,
    string Result);

/// <summary>
/// A Game with its move tree as a flat node list. <see cref="FirstMoves"/> are the children of the
/// starting position; every node lists its own children. The first child is the main continuation.
/// </summary>
public sealed record GameDto(
    int Id,
    string? Date,
    OwnerColor OwnerColor,
    PlayerDto Opponent,
    string White,
    string Black,
    int? WhiteElo,
    int? BlackElo,
    string Result,
    string StartFen,
    IReadOnlyList<int> FirstMoves,
    IReadOnlyList<MoveNodeDto> Nodes);

/// <param name="Fen">The position after this move.</param>
public sealed record MoveNodeDto(
    int Id,
    int? ParentId,
    IReadOnlyList<int> Children,
    int Ply,
    string San,
    string Uci,
    string Fen,
    string? CommentBefore,
    string? CommentAfter,
    IReadOnlyList<int> Nags);

/// <summary>Create or update a Game's details. Elo 0 or null means unrated.</summary>
public sealed record GameDetailsRequest(
    OwnerColor? OwnerColor,
    int? OpponentId,
    int? WhiteElo,
    int? BlackElo,
    string? Result,
    string? Date);

/// <summary>Play a move after <paramref name="ParentId"/> (null = from the starting position), in SAN or UCI.</summary>
public sealed record AddMoveRequest(int? ParentId, string? Move);

public sealed record AddMoveResponse(int NodeId, GameDto Game);

public enum CommentPlacement
{
    After,
    Before,
}

/// <summary>Sets a move's comment; empty text removes it. Placement defaults to after the move.</summary>
public sealed record SetCommentRequest(string? Text, CommentPlacement Placement = CommentPlacement.After);

/// <summary>Replaces a move's symbols (NAGs), e.g. [1, 14] for "!" and "⩲". An empty list clears them.</summary>
public sealed record SetNagsRequest(IReadOnlyList<int>? Nags);
