using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.Games.GameValues;

namespace ChessDatabase.Api.Games;

public static class GameEndpoints
{
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
            await GameQueries.LoadGameDtoAsync(db, id) is { } game ? Results.Ok(game) : Results.NotFound());

        games.MapPost("/", async (GameDetailsRequest body, CollectionDbContext db) =>
        {
            var errors = await ValidateAsync(body, db);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var game = new GameRecord { CreatedAt = DateTime.UtcNow };
            await ApplyAsync(body, game, db);
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return Results.Created($"/api/games/{game.Id}", await GameQueries.LoadGameDtoAsync(db, game.Id));
        });

        games.MapPut("/{id:int}", async (int id, GameDetailsRequest body, CollectionDbContext db) =>
        {
            var game = await db.Games.Include(g => g.Tags).SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var errors = await ValidateAsync(body, db);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            await ApplyAsync(body, game, db);
            await db.SaveChangesAsync();
            return Results.Ok(await GameQueries.LoadGameDtoAsync(db, id));
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
        if (!string.IsNullOrWhiteSpace(body.Date) && !GameDates.IsValid(body.Date)) errors["date"] = [GameDates.FormatMessage];

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
        game.Date = OptionalText(body.Date);
        game.Site = OptionalText(body.Site);
        game.Round = OptionalText(body.Round);
        game.TimeControl = OptionalText(body.TimeControl);
        game.Notes = OptionalText(body.Notes);
        game.Event = await EventsAndTags.FindOrCreateEventAsync(db, body.Event);
        game.Tags = await EventsAndTags.FindOrCreateTagsAsync(db, body.Tags ?? []);
    }
}
