using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Analysis;

/// <param name="Copied">How many moves were copied.</param>
public sealed record CopyResultDto(int Copied, GameDto Game);

/// <summary>
/// The only route from Engine Assessment to Annotations (ADR 0003): on the Owner's request, a
/// classified move gets its symbol (?!, ?, ??) and the Engine's line becomes a Variation beside it.
/// A move symbol the Owner already set is kept, comments are never touched, and a line already in
/// the Game is followed rather than added again.
/// </summary>
public static class CopyAssessmentEndpoints
{
    public static void MapCopyAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/games/{id:int}/assessment/moves/{ply:int}/copy", async (int id, int ply, CollectionDbContext db) =>
        {
            if (await AnalysisEndpoints.LoadAsync(db, id) is not { } assessment) return Results.NotFound();
            if (assessment.Moves.FirstOrDefault(m => m.Ply == ply) is not { } move) return Results.NotFound();
            if (move.Classification is null) return Invalid("ply", "The Engine had nothing against that move.");
            if (move.NodeId is null) return Invalid("ply", "That move has changed since the analysis. Analyse the Game again.");

            await CopyAsync(db, id, [move]);
            return Results.Ok(await GameQueries.LoadGameDtoAsync(db, id));
        });

        // "Copy all mistakes and blunders": inaccuracies stay out.
        app.MapPost("/api/games/{id:int}/assessment/copy", async (int id, CollectionDbContext db) =>
        {
            if (await AnalysisEndpoints.LoadAsync(db, id) is not { } assessment) return Results.NotFound();
            var moves = assessment.Moves
                .Where(m => m.NodeId is not null && m.Classification is MoveClassification.Mistake or MoveClassification.Blunder)
                .ToList();
            await CopyAsync(db, id, moves);
            return Results.Ok(new CopyResultDto(moves.Count, (await GameQueries.LoadGameDtoAsync(db, id))!));
        });
    }

    private static async Task CopyAsync(CollectionDbContext db, int gameId, IReadOnlyList<AssessedMoveDto> moves)
    {
        if (moves.Count == 0) return;
        var game = await db.Games.Include(g => g.MoveNodes).SingleAsync(g => g.Id == gameId);
        var tree = new MoveTree(game.MoveNodes);
        foreach (var move in moves)
        {
            var node = tree.Node(move.NodeId!.Value);
            if (!node.Nags.Any(Nags.IsMoveSymbol))
                node.Nags = [SymbolFor(move.Classification!.Value), .. node.Nags];
            if (move.BestLineUci.Count > 0 && move.BestLineUci[0] != node.Uci)
                MoveTreeEndpoints.AddLine(game, tree, node.ParentId, [.. move.BestLineUci.Zip(move.BestLineSan)]);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>?! for an inaccuracy, ? for a mistake, ?? for a blunder (NAG 6, 2, 4).</summary>
    private static int SymbolFor(MoveClassification classification) => classification switch
    {
        MoveClassification.Inaccuracy => 6,
        MoveClassification.Mistake => 2,
        _ => 4,
    };
}
