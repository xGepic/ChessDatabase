using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Players;

namespace ChessDatabase.Api.Games;

/// <summary>
/// One row of the game list. White and Black are display names: the Owner's name or the Opponent's.
/// Result is PGN text: "1-0", "0-1" or "1/2-1/2".
/// </summary>
public record GameSummaryDto(
    int Id,
    string? Date,
    OwnerColour OwnerColour,
    PlayerDto Opponent,
    string White,
    string Black,
    int? WhiteElo,
    int? BlackElo,
    string Result,
    EventDto? Event,
    string? Eco,
    string? OpeningName);

/// <summary>
/// A Game with its move tree as a flat node list. <see cref="FirstMoves"/> are the children of the
/// starting position; every node lists its own children. The first child is the main continuation.
/// </summary>
public sealed record GameDto : GameSummaryDto
{
    public GameDto(GameSummaryDto summary, GameRecord game, string startFen, IReadOnlyList<int> firstMoves, IReadOnlyList<MoveNodeDto> nodes)
        : base(summary)
    {
        Site = game.Site;
        Round = game.Round;
        TimeControl = game.TimeControl;
        Notes = game.Notes;
        Tags = [.. game.Tags.Select(t => t.Name).Order(StringComparer.CurrentCultureIgnoreCase)];
        StartFen = startFen;
        FirstMoves = firstMoves;
        Nodes = nodes;
    }

    public string? Site { get; }
    public string? Round { get; }
    public string? TimeControl { get; }
    public string? Notes { get; }
    public IReadOnlyList<string> Tags { get; }
    public string StartFen { get; }
    public IReadOnlyList<int> FirstMoves { get; }
    public IReadOnlyList<MoveNodeDto> Nodes { get; }
}

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

/// <summary>
/// Create or update a Game's details (the whole set at once). Elo 0 or null means unrated. Event is
/// a name: an existing Event with that name (ignoring case) is linked, otherwise one is created.
/// Tags work the same way. Empty text means "not set".
/// </summary>
public sealed record GameDetailsRequest(
    OwnerColour? OwnerColour,
    int? OpponentId,
    int? WhiteElo,
    int? BlackElo,
    string? Result,
    string? Date,
    string? Event = null,
    string? Site = null,
    string? Round = null,
    string? TimeControl = null,
    string? Notes = null,
    IReadOnlyList<string>? Tags = null);

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
