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
            var game = await GamesForExport(db).SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var pgn = PgnWriter.Write(game, await GameEndpoints.OwnerNameAsync(db));
            return File(pgn, $"{FileNamePart(game.Date ?? "")} {FileNamePart(game.Opponent.Name)}.pgn".Trim());
        });

        games.MapGet("/pgn", async (CollectionDbContext db) =>
        {
            var all = await GamesForExport(db).ToListAsync();
            var ordered = all.OrderBy(g => g.Date ?? "").ThenBy(g => g.CreatedAt);
            var pgn = PgnWriter.WriteMany(ordered, await GameEndpoints.OwnerNameAsync(db));
            return File(pgn, $"Chess Database {DateTime.Now:yyyy-MM-dd}.pgn");
        });
    }

    private static IQueryable<GameRecord> GamesForExport(CollectionDbContext db) =>
        db.Games.AsNoTracking()
            .Include(g => g.Opponent).Include(g => g.Event).Include(g => g.Tags).Include(g => g.MoveNodes)
            .AsSplitQuery();

    private static IResult File(string pgn, string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(pgn), PgnContentType, fileName);

    /// <summary>Keeps a name usable as part of a Windows file name.</summary>
    private static string FileNamePart(string text) =>
        string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
