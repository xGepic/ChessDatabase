using System.Text.RegularExpressions;
using ChessDatabase.Api.Games;
using ChessDatabase.Api.Pgn;
using ChessDatabase.Rules;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Research;

/// <summary>A move played from the position in Master Games.</summary>
/// <param name="Fen">The position after the move (Chess Rules), so the UI can show it without saving anything.</param>
/// <param name="WhitePercent">Share of those games White won; with DrawPercent and BlackPercent about 100.</param>
public sealed record MastersMoveDto(
    string Uci, string San, string Fen, long GameCount, double WhitePercent, double DrawPercent, double BlackPercent, int? AverageRating);

/// <summary>A top Master Game for the position. Not a Game: it is never stored in the Collection.</summary>
/// <param name="Result">"1-0", "0-1" or "1/2-1/2".</param>
public sealed record MasterGameRefDto(string Id, string White, int? WhiteRating, string Black, int? BlackRating, int? Year, string Result);

/// <summary>The Masters source for one position.</summary>
/// <param name="Available">False when Lichess could not be asked; Message says why.</param>
/// <param name="GameCount">Master Games that reached the position.</param>
public sealed record MastersDto(bool Available, string? Message, long GameCount, IReadOnlyList<MastersMoveDto> Moves, IReadOnlyList<MasterGameRefDto> TopGames)
{
    public static MastersDto Unavailable(string message) => new(false, message, 0, [], []);
}

public sealed record MasterMoveDto(string San, string Uci, string Fen);

/// <summary>A Master Game to replay read-only. It is never stored in the Collection.</summary>
/// <param name="Date">"2025-10-29", or as much of it as is known.</param>
/// <param name="Ply">Where the position asked about first occurs (0 = the start), or null if it doesn't.</param>
public sealed record MasterGameDto(
    string Id, string White, int? WhiteElo, string Black, int? BlackElo, string Result, string? Event, string? Site, string? Date,
    IReadOnlyList<MasterMoveDto> Moves, int? Ply);

/// <summary>The Opening Tree's Masters source, from the Lichess masters explorer through <see cref="MastersClient"/>.</summary>
public static partial class MastersEndpoints
{
    /// <summary>Maps GET /api/research/masters?fen= and GET /api/research/masters/games/{id}?fen=.</summary>
    public static void MapMastersEndpoints(this IEndpointRouteBuilder app)
    {
        var masters = app.MapGroup("/api/research/masters");

        masters.MapGet("/", async (string? fen, MastersClient lichess) =>
        {
            if (ResearchEndpoints.ReadPosition(fen, out var position) is { } problem) return problem;
            var answer = await lichess.PositionAsync(position.ToFen());
            return Results.Ok(answer.Value is { } value ? ToDto(position, value) : MastersDto.Unavailable(answer.Problem!));
        });

        masters.MapGet("/games/{id}", async (string id, string? fen, MastersClient lichess) =>
        {
            if (!GameId().IsMatch(id)) return Invalid("id", "That is not a Lichess game id.");
            Position? asked = null;
            if (fen is not null)
            {
                if (ResearchEndpoints.ReadPosition(fen, out var position) is { } problem) return problem;
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
            var games = m.White + m.Draws + m.Black;
            moves.Add(new MastersMoveDto(m.Uci, position.ToSan(move), position.Play(move).ToFen(), games,
                Percent(m.White, games), Percent(m.Draws, games), Percent(m.Black, games), m.AverageRating));
        }
        var top = masters.TopGames.Select(g => new MasterGameRefDto(
            g.Id, g.White.Name, g.White.Rating, g.Black.Name, g.Black.Rating, g.Year,
            g.Winner switch { "white" => "1-0", "black" => "0-1", _ => "1/2-1/2" })).ToList();
        return new MastersDto(true, null, masters.White + masters.Draws + masters.Black, moves, top);
    }

    private static double Percent(long part, long whole) => whole == 0 ? 0 : Math.Round(part * 100.0 / whole, 1);

    /// <summary>The game's main line replayed by Chess Rules, or null if Lichess's PGN can't be read.</summary>
    private static MasterGameDto? ToGame(string id, string pgn, Position? asked)
    {
        if (PgnReader.Read(pgn).FirstOrDefault()?.Entry is not { } entry) return null;
        var position = Position.Initial;
        var moves = new List<MasterMoveDto>();
        int? ply = asked is not null && position.Hash == asked.Hash ? 0 : null;
        foreach (var written in entry.Moves)
        {
            if (!position.TryParseSan(written.San, out var move)) return null;
            var san = position.ToSan(move);
            position = position.Play(move);
            moves.Add(new MasterMoveDto(san, move.ToUci(), position.ToFen()));
            if (ply is null && asked is not null && position.Hash == asked.Hash) ply = moves.Count;
        }
        return new MasterGameDto(
            id, entry.Tag("White") ?? "?", PgnValues.Elo(entry.Tag("WhiteElo")), entry.Tag("Black") ?? "?", PgnValues.Elo(entry.Tag("BlackElo")),
            entry.Tag("Result") ?? entry.Termination ?? "*", PgnValues.OptionalTag(entry.Tag("Event")), PgnValues.OptionalTag(entry.Tag("Site")),
            GameDates.FromPgn(entry.Tag("Date")), moves, ply);
    }

    [GeneratedRegex("^[A-Za-z0-9]{8}$")]
    private static partial Regex GameId();
}
