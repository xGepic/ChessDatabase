using System.Text;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Export;
using ChessDatabase.Api.Pgn;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>Downloads: one Game as PGN or PDF, or the whole Collection as PGN.</summary>
public static class ExportEndpoints
{
    private const string PgnContentType = "application/x-chess-pgn";

    public static void MapExportEndpoints(this RouteGroupBuilder games)
    {
        games.MapGet("/{id:int}/pgn", (int id, CollectionDbContext db) =>
            OneGameAsync(db, id, (game, ownerName) => File(PgnWriter.Write(game, ownerName), GameFileName(game, ".pgn"))));

        games.MapGet("/{id:int}/pdf", (int id, bool? finalDiagram, CollectionDbContext db) =>
            OneGameAsync(db, id, (game, ownerName) =>
                Results.File(GamePdf.Write(game, ownerName, finalDiagram ?? false), "application/pdf", GameFileName(game, ".pdf"))));

        // The game list as shown: the same filters and order (no filters = the whole Collection).
        games.MapGet("/pgn", async ([AsParameters] GameListQuery query, CollectionDbContext db) =>
        {
            var errors = query.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var rows = await query.RunAsync(db, withMoves: true);
            var pgn = PgnWriter.WriteMany(rows.Select(r => r.Game), await GameQueries.OwnerNameAsync(db));
            return File(pgn, $"Chess Database {DateTime.Now:yyyy-MM-dd}.pgn");
        });
    }

    /// <summary>Loads one Game with its moves and details for export; 404 when there is none.</summary>
    private static async Task<IResult> OneGameAsync(CollectionDbContext db, int id, Func<GameRecord, string, IResult> export)
    {
        var game = await db.Games.WithDetails(withMoves: true).SingleOrDefaultAsync(g => g.Id == id);
        return game is null ? Results.NotFound() : export(game, await GameQueries.OwnerNameAsync(db));
    }

    private static IResult File(string pgn, string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(pgn), PgnContentType, fileName);

    private static string GameFileName(GameRecord game, string extension) =>
        $"{FileNamePart(game.Date ?? "")} {FileNamePart(game.Opponent.Name)}".Trim() + extension;

    /// <summary>Keeps a name usable as part of a Windows file name.</summary>
    private static string FileNamePart(string text) =>
        string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
