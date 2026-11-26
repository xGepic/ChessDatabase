using System.Collections.Concurrent;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Research;

/// <summary>The Lichess masters explorer's answer for a position, in its own JSON shape.</summary>
public sealed record LichessMasters(long White, long Draws, long Black, List<LichessMastersMove> Moves, List<LichessTopGame> TopGames);

/// <summary>A move in <see cref="LichessMasters"/>: how often each side won after it.</summary>
public sealed record LichessMastersMove(string Uci, long White, long Draws, long Black, int? AverageRating);

/// <summary>A top game in <see cref="LichessMasters"/>. Winner is "white", "black" or null for a draw.</summary>
public sealed record LichessTopGame(string Id, string? Winner, LichessPlayer White, LichessPlayer Black, int? Year);

/// <summary>A player in <see cref="LichessTopGame"/>.</summary>
public sealed record LichessPlayer(string Name, int? Rating);

/// <summary>
/// The Masters source of the Opening Tree. Answers never throw: an unavailable source comes back
/// as <see cref="OnlineAnswer{T}"/> with a message, and nothing else in the app is affected.
/// </summary>
public interface IMastersSource
{
    /// <summary>Move statistics and top Master Games for a position.</summary>
    Task<OnlineAnswer<LichessMasters>> PositionAsync(Position position);

    /// <summary>A Master Game's PGN, by its Lichess id.</summary>
    Task<OnlineAnswer<string>> GamePgnAsync(string id);
}

/// <summary>What the Masters source keeps for the app's run: its request state and its answers.</summary>
public sealed class MastersState
{
    public LichessService Service { get; } =
        new("Masters", "Masters", "https://explorer.lichess.org/", new OnlineSourceState());

    /// <summary>Answers by position; move counters don't matter (see <see cref="Positions.Key"/>).</summary>
    public ConcurrentDictionary<string, LichessMasters> Positions { get; } = new();

    /// <summary>Master Game PGNs by Lichess id.</summary>
    public ConcurrentDictionary<string, string> GamePgns { get; } = new();
}

/// <summary>
/// The Masters source: the Lichess masters opening explorer (explorer.lichess.org), asked through
/// <see cref="LichessFetch"/>. The Owner's optional Lichess token is sent when stored.
/// </summary>
public sealed class MastersClient(
    HttpClient http, MastersState state, LichessTokenStore tokens, IConfiguration configuration, TimeProvider clock) : IMastersSource
{
    private readonly LichessFetch fetch = new(http, configuration, clock);

    private const int MovesShown = 12, TopGamesShown = 15;

    public Task<OnlineAnswer<LichessMasters>> PositionAsync(Position position)
    {
        var key = Positions.Key(position);
        return fetch.GetAsync(state.Service, state.Positions, key,
            $"masters?fen={Uri.EscapeDataString(key)}&moves={MovesShown}&topGames={TopGamesShown}", tokens.Read(),
            LichessFetch.ReadJsonAsync<LichessMasters>);
    }

    public Task<OnlineAnswer<string>> GamePgnAsync(string id) =>
        fetch.GetAsync(state.Service, state.GamePgns, id, $"masters/pgn/{id}", tokens.Read(),
            async content => await content.ReadAsStringAsync());
}

/// <summary>Positions as online sources see them.</summary>
public static class Positions
{
    /// <summary>Placement, side to move, castling and en passant: what Lichess's statistics depend on (no move counters).</summary>
    public static string Key(Position position) => string.Join(' ', position.ToFen().Split(' ').Take(4));
}
