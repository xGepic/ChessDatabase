using System.Text.RegularExpressions;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Players;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

public static partial class GameEndpoints
{
    public const int MaxElo = 3500;
    private static readonly string EloOutOfRange = $"An Elo is between 1 and {MaxElo}, or empty/0 for unrated.";

    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var games = app.MapGroup("/api/games");

        games.MapGet("/", async ([AsParameters] GameListQuery query, CollectionDbContext db) =>
        {
            var errors = query.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok((await query.RunAsync(db)).Select(r => r.Row));
        });

        games.MapGet("/{id:int}", async (int id, CollectionDbContext db) =>
            await LoadGameDtoAsync(db, id) is { } game ? Results.Ok(game) : Results.NotFound());

        games.MapPost("/", async (GameDetailsRequest body, CollectionDbContext db) =>
        {
            var errors = await ValidateAsync(body, db);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var game = new GameRecord { CreatedAt = DateTime.UtcNow };
            await ApplyAsync(body, game, db);
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return Results.Created($"/api/games/{game.Id}", await LoadGameDtoAsync(db, game.Id));
        });

        games.MapPut("/{id:int}", async (int id, GameDetailsRequest body, CollectionDbContext db) =>
        {
            var game = await db.Games.Include(g => g.Tags).SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var errors = await ValidateAsync(body, db);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            await ApplyAsync(body, game, db);
            await db.SaveChangesAsync();
            return Results.Ok(await LoadGameDtoAsync(db, id));
        });

        games.MapDelete("/{id:int}", async (int id, CollectionDbContext db) =>
        {
            var deleted = await db.Games.Where(g => g.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        games.MapMoveTreeEndpoints();
        games.MapAnnotationEndpoints();
        games.MapExportEndpoints();
    }

    public static async Task<GameDto?> LoadGameDtoAsync(CollectionDbContext db, int id)
    {
        var game = await db.Games.AsNoTracking()
            .Include(g => g.Opponent).Include(g => g.Event).Include(g => g.Tags).Include(g => g.MoveNodes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(g => g.Id == id);
        if (game is null) return null;

        var summary = ToSummary(game, await OwnerNameAsync(db));
        var (firstMoves, nodes) = new MoveTree(game.MoveNodes).ToDtos();
        return new GameDto(summary, game, MoveTree.StartPosition.ToFen(), firstMoves, nodes);
    }

    public static GameSummaryDto ToSummary(GameRecord game, string ownerName)
    {
        var opponent = new PlayerDto(game.Opponent.Id, game.Opponent.Name);
        var white = game.OwnerColour == OwnerColour.White ? ownerName : opponent.Name;
        var black = game.OwnerColour == OwnerColour.Black ? ownerName : opponent.Name;
        var gameEvent = game.Event is null ? null : new NamedDto(game.Event.Id, game.Event.Name);
        return new GameSummaryDto(game.Id, game.Date, game.OwnerColour, opponent, white, black,
            game.WhiteElo, game.BlackElo, game.Result.ToPgn(), gameEvent, game.Eco, game.OpeningName);
    }

    public static async Task<string> OwnerNameAsync(CollectionDbContext db)
    {
        var name = (await db.Settings.AsNoTracking().SingleOrDefaultAsync())?.OwnerName;
        return string.IsNullOrWhiteSpace(name) ? "Owner" : name;
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(GameDetailsRequest body, CollectionDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        if (body.OwnerColour is null) errors["ownerColour"] = ["Choose the colour you played."];
        if (body.OpponentId is not { } opponentId || !await db.Players.AnyAsync(p => p.Id == opponentId))
            errors["opponentId"] = ["Choose an existing Opponent."];
        if (!GameResults.TryParsePgn(body.Result, out _))
            errors["result"] = ["A Game needs a result: 1-0, 0-1 or 1/2-1/2."];
        if (body.WhiteElo is < 0 or > MaxElo) errors["whiteElo"] = [EloOutOfRange];
        if (body.BlackElo is < 0 or > MaxElo) errors["blackElo"] = [EloOutOfRange];
        if (!string.IsNullOrWhiteSpace(body.Date) && !IsValidDate(body.Date))
            errors["date"] = ["Write the date as YYYY, YYYY-MM or YYYY-MM-DD."];

        CheckLength("event", body.Event, GameDetailLimits.Event);
        CheckLength("site", body.Site, GameDetailLimits.Site);
        CheckLength("round", body.Round, GameDetailLimits.Round);
        CheckLength("timeControl", body.TimeControl, GameDetailLimits.TimeControl);
        CheckLength("notes", body.Notes, GameDetailLimits.Notes);
        foreach (var tag in body.Tags ?? []) CheckLength("tags", tag, GameDetailLimits.Tag);
        return errors;

        void CheckLength(string field, string? value, int max)
        {
            if (value?.Trim().Length > max) errors[field] = [$"At most {max} characters."];
        }
    }

    private static async Task ApplyAsync(GameDetailsRequest body, GameRecord game, CollectionDbContext db)
    {
        game.OwnerColour = body.OwnerColour!.Value;
        game.OpponentId = body.OpponentId!.Value;
        game.WhiteElo = Unrated(body.WhiteElo);
        game.BlackElo = Unrated(body.BlackElo);
        game.Result = GameResults.ParsePgn(body.Result!);
        game.Date = Text(body.Date);
        game.Site = Text(body.Site);
        game.Round = Text(body.Round);
        game.TimeControl = Text(body.TimeControl);
        game.Notes = Text(body.Notes);
        game.Event = await NamedRecords.FindOrCreateEventAsync(db, body.Event);
        game.Tags = await NamedRecords.FindOrCreateTagsAsync(db, body.Tags ?? []);
    }

    /// <summary>"YYYY", "YYYY-MM" or "YYYY-MM-DD".</summary>
    public static bool IsValidDate(string date) => DatePattern().IsMatch(date.Trim());

    /// <summary>Trimmed text, or null when empty.</summary>
    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An Elo left empty or entered as 0 means unrated.</summary>
    public static int? Unrated(int? elo) => elo is null or 0 ? null : elo;

    [GeneratedRegex(@"^\d{4}(-(0[1-9]|1[0-2])(-(0[1-9]|[12]\d|3[01]))?)?$")]
    private static partial Regex DatePattern();
}
