using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Api.Pgn;
using ChessDatabase.Api.Players;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Import;

/// <summary>
/// A held-back Game and what is already known about it. OwnerColour and Result are null when they
/// still have to be chosen. A suggestion is an existing Player whose name resembles that side's name.
/// </summary>
public sealed record ReviewItemDto(
    int Id,
    string? White,
    string? Black,
    string? Date,
    string Reason,
    OwnerColour? OwnerColour,
    string? Result,
    PlayerDto? WhiteSuggestion,
    PlayerDto? BlackSuggestion);

/// <param name="OpponentPlayerId">
/// Link the Opponent to this existing Player (confirming a suggestion). Null: a Player with exactly
/// the Opponent's name, or a new Player.
/// </param>
public sealed record ResolveReviewRequest(OwnerColour? OwnerColour, string? Result, int? OpponentPlayerId);

public static class ImportReviewEndpoints
{
    public static void MapImportReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var review = app.MapGroup("/api/import/review");

        review.MapGet("/", async (CollectionDbContext db, PgnImporter importer) =>
        {
            await importer.LoadContextAsync();
            var items = await db.ImportReviewItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync();
            return items.Select(item => ToDto(item, importer)).ToList();
        });

        review.MapPost("/{id:int}/resolve", async (int id, ResolveReviewRequest body, CollectionDbContext db, PgnImporter importer) =>
        {
            var item = await db.ImportReviewItems.FindAsync(id);
            if (item is null) return Results.NotFound();
            if (body.OwnerColour is not { } ownerColour) return Invalid("ownerColour", "Choose the side you played.");
            if (!GameResults.TryParsePgn(body.Result, out var result))
                return Invalid("result", "Choose the result: 1-0, 0-1 or 1/2-1/2.");

            await importer.LoadContextAsync();
            var pgn = ReadStored(item);
            var tree = pgn is null ? null : PgnImporter.TreeBuilder.Build(pgn.Moves, out _);
            if (pgn is null || tree is null) return Invalid("id", "The stored Game can no longer be read.");

            var opponentName = PgnImporter.OpponentName(pgn, ownerColour);
            PlayerRecord opponent;
            if (body.OpponentPlayerId is { } playerId)
            {
                var player = await db.Players.FindAsync(playerId);
                var plausible = player is not null &&
                    (string.Equals(player.Name, opponentName, StringComparison.CurrentCultureIgnoreCase)
                     || PersonName.SamePerson(player.Name, opponentName));
                if (!plausible) return Invalid("opponentPlayerId", $"That Player's name does not match \"{opponentName}\".");
                opponent = player!;
            }
            else
            {
                opponent = importer.ExactPlayer(opponentName) ?? importer.NewPlayer(opponentName);
            }

            var game = await importer.CreateGameAsync(pgn, tree, ownerColour, result, opponent);
            db.ImportReviewItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, game.Id));
        });

        review.MapDelete("/{id:int}", async (int id, CollectionDbContext db) =>
        {
            var deleted = await db.ImportReviewItems.Where(i => i.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static ReviewItemDto ToDto(ImportReviewItemRecord item, PgnImporter importer)
    {
        var pgn = ReadStored(item);
        var result = pgn is not null && GameResults.TryParsePgn(pgn.Tag("Result") ?? pgn.Termination, out var r)
            ? r.ToPgn()
            : null;
        return new ReviewItemDto(
            item.Id, item.White, item.Black, item.Date, item.Reason,
            pgn is null ? null : importer.OwnerColourOf(pgn),
            result,
            Suggestion(item.White, importer),
            Suggestion(item.Black, importer));
    }

    private static PlayerDto? Suggestion(string? name, PgnImporter importer)
    {
        if (string.IsNullOrWhiteSpace(name) || importer.ExactPlayer(name) is not null) return null;
        return importer.SimilarPlayer(name) is { } player ? new PlayerDto(player.Id, player.Name) : null;
    }

    private static PgnGame? ReadStored(ImportReviewItemRecord item) => PgnReader.Read(item.Pgn).FirstOrDefault()?.Game;
}
