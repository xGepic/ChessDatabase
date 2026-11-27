using System.Text.RegularExpressions;
using ChessDatabase.Api.Games;
using ChessDatabase.Api.Pgn;
using ChessDatabase.Rules;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Research;

/// <summary>A player of a Master Game. Elo is the rating Lichess has for them, if any.</summary>
public sealed record MasterPlayerDto(string Name, int? Elo);

/// <summary>How a set of Master Games ended, in percent: White won, drawn, Black won.</summary>
public sealed record ResultShareDto(double White, double Draw, double Black)
{
    public static ResultShareDto Of(long white, long draws, long black)
    {
        var games = white + draws + black;
        return games == 0 ? new(0, 0, 0) : new(Percent(white, games), Percent(draws, games), Percent(black, games));
    }

    private static double Percent(long part, long whole) => Math.Round(part * 100.0 / whole, 1);
}

/// <summary>A move played from the position in Master Games.</summary>
/// <param name="Fen">The position after the move (Chess Rules), so the UI can show it without saving anything.</param>
/// <param name="AverageElo">Average Elo of the players who chose it.</param>
public sealed record MastersMoveDto(string Uci, string San, string Fen, long GameCount, ResultShareDto Results, int? AverageElo);

/// <summary>A top Master Game for the position. Not a Game: it is never stored in the Collection.</summary>
/// <param name="Result">"1-0", "0-1" or "1/2-1/2".</param>
public sealed record MasterGameRefDto(string Id, MasterPlayerDto White, MasterPlayerDto Black, int? Year, string Result);

/// <summary>The Masters source for one position.</summary>
/// <param name="Available">False when Lichess could not be asked; Message says why.</param>
/// <param name="GameCount">Master Games that reached the position.</param>
/// <param name="Moves">The most played moves (at most 12), most played first.</param>
/// <param name="OtherMovesGameCount">Master Games that went on with a move not in <paramref name="Moves"/>.</param>
public sealed record MastersDto(
    bool Available, string? Message, long GameCount, IReadOnlyList<MastersMoveDto> Moves, long OtherMovesGameCount,
    IReadOnlyList<MasterGameRefDto> TopGames)
{
    public static MastersDto Unavailable(string message) => new(false, message, 0, [], 0, []);
}

/// <summary>A move of a Master Game, with the position after it (Chess Rules).</summary>
public sealed record MasterMoveDto(string San, string Uci, string Fen);

/// <summary>A Master Game to replay read-only. It is never stored in the Collection.</summary>
/// <param name="Date">"2025-10-29", or as much of it as is known.</param>
/// <param name="Ply">Where the position asked about first occurs (0 = the start), or null if it doesn't.</param>
public sealed record MasterGameDto(
    string Id, MasterPlayerDto White, MasterPlayerDto Black, string Result, string? Event, string? Site, string? Date,
    IReadOnlyList<MasterMoveDto> Moves, int? Ply);

/// <summary>The Opening Tree's Masters source, through <see cref="IMastersSource"/>.</summary>
public static partial class MastersEndpoints
{
    /// <summary>Maps GET /api/research/masters?fen= and GET /api/research/masters/games/{id}?fen=.</summary>
    public static void MapMastersEndpoints(this IEndpointRouteBuilder app)
    {
        var masters = app.MapGroup("/api/research/masters");

        masters.MapGet("/", async (string? fen, IMastersSource lichess) =>
        {
            if (PositionInput.Read(fen, out var position) is { } problem) return problem;
            var answer = await lichess.PositionAsync(position);
            return Results.Ok(answer.Value is { } value ? ToDto(position, value) : MastersDto.Unavailable(answer.Problem!));
        });

        masters.MapGet("/games/{id}", async (string id, string? fen, IMastersSource lichess) =>
        {
            if (!GameId().IsMatch(id)) return Invalid("id", "That is not a Lichess game id.");
            Position? asked = null;
            if (fen is not null)
            {
                if (PositionInput.Read(fen, out var position) is { } problem) return problem;
                asked = position;
            }
            var answer = await lichess.GamePgnAsync(id);
            if (answer.Value is null) return Results.Problem(answer.Problem, statusCode: StatusCodes.Status503ServiceUnavailable);
            return ToGame(id, answer.Value, asked) is { } game
                ? Results.Ok(game)
                : Results.Problem("Masters unavailable: Lichess sent a game that could not be read.", statusCode: StatusCodes.Status502BadGateway);
        });
    }

    private static MastersDto ToDto(Position position, LichessMasters masters)
    {
        var moves = new List<MastersMoveDto>();
        foreach (var m in masters.Moves)
        {
            if (!Move.TryParseUci(m.Uci, out var move) || !position.IsLegal(move)) continue;
            moves.Add(new MastersMoveDto(m.Uci, position.ToSan(move), position.Play(move).ToFen(), m.White + m.Draws + m.Black,
                ResultShareDto.Of(m.White, m.Draws, m.Black), m.AverageRating));
        }
        var total = masters.White + masters.Draws + masters.Black;
        var top = masters.TopGames.Select(g => new MasterGameRefDto(
            g.Id, new MasterPlayerDto(g.White.Name, g.White.Rating), new MasterPlayerDto(g.Black.Name, g.Black.Rating), g.Year,
            g.Winner switch { "white" => "1-0", "black" => "0-1", _ => "1/2-1/2" })).ToList();
        return new MastersDto(true, null, total, moves, Math.Max(0, total - moves.Sum(m => m.GameCount)), top);
    }

    /// <summary>The game's main line replayed by Chess Rules, or null if Lichess's PGN can't be read.</summary>
    private static MasterGameDto? ToGame(string id, string pgn, Position? asked)
    {
        if (PgnReader.Read(pgn) is not [{ Entry: { } entry }, ..]) return null;
        if (Line.Read(Position.Initial, entry.Moves.Select(w => w.San), out _) is not { } line) return null;
        List<MasterMoveDto> moves = [.. Enumerable.Range(0, line.Count).Select(i => new MasterMoveDto(line.San[i], line.Uci[i], line.Positions[i + 1].ToFen()))];
        var reached = asked is null ? -1 : line.Positions.ToList().FindIndex(p => p.Hash == asked.Hash);
        int? ply = reached < 0 ? null : reached;
        return new MasterGameDto(
            id,
            new MasterPlayerDto(entry.Tag("White") ?? "?", PgnValues.Elo(entry.Tag("WhiteElo"))),
            new MasterPlayerDto(entry.Tag("Black") ?? "?", PgnValues.Elo(entry.Tag("BlackElo"))),
            entry.Tag("Result") ?? entry.Termination ?? "*", PgnValues.OptionalTag(entry.Tag("Event")), PgnValues.OptionalTag(entry.Tag("Site")),
            GameDates.FromPgn(entry.Tag("Date")), moves, ply);
    }

    [GeneratedRegex("^[A-Za-z0-9]{8}$")]
    private static partial Regex GameId();
}
