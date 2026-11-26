using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Api.Players;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Import;

/// <summary>
/// A held-back PGN entry and what is already known about it. OwnerColour and Result are null when
/// they still have to be chosen. A suggestion is an existing Player whose name resembles that side's
/// name; it is only given for the Opponent's side once the Owner's colour is known.
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
/// <param name="AllowDuplicate">Add the Game even if the Collection already has the same Game.</param>
public sealed record ResolveReviewRequest(OwnerColour? OwnerColour, string? Result, int? OpponentPlayerId, bool AllowDuplicate = false);

public static class ImportReviewEndpoints
{
    public static void MapImportReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var review = app.MapGroup("/api/import/review");

        review.MapGet("/", (PgnImporter importer) => importer.ReviewListAsync());

        review.MapPost("/{id:int}/resolve", async (int id, ResolveReviewRequest body, PgnImporter importer, CollectionDbContext db) =>
            await importer.ResolveAsync(id, body) switch
            {
                ResolveOutcome.Added added => Results.Ok(await GameQueries.LoadGameDtoAsync(db, added.GameId)),
                ResolveOutcome.Invalid invalid => Invalid(invalid.Field, invalid.Message),
                ResolveOutcome.Duplicate duplicate => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This Game is already in the Collection.",
                    extensions: new Dictionary<string, object?> { ["existingGameId"] = duplicate.ExistingGameId }),
                _ => Results.NotFound(),
            });

        review.MapDelete("/{id:int}", async (int id, CollectionDbContext db) =>
        {
            var deleted = await db.ImportReviewItems.Where(i => i.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
