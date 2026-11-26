using System.Text;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Pgn;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>PGN downloads: one Game, or the whole Collection.</summary>
public static class ExportEndpoints
{
    private const string PgnContentType = "application/x-chess-pgn";

    public static void MapExportEndpoints(this RouteGroupBuilder games)
    {
        games.MapGet("/{id:int}/pgn", async (int id, CollectionDbContext db) =>
        {
            var game = await db.Games.WithDetails(withMoves: true).SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var pgn = PgnWriter.Write(game, await GameQueries.OwnerNameAsync(db));
            return File(pgn, $"{FileNamePart(game.Date ?? "")} {FileNamePart(game.Opponent.Name)}.pgn".Trim());
        });

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

    private static IResult File(string pgn, string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(pgn), PgnContentType, fileName);

    /// <summary>Keeps a name usable as part of a Windows file name.</summary>
    private static string FileNamePart(string text) =>
        string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
