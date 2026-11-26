using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var games = app.MapGroup("/api/games");

        games.MapGet("/", async (CollectionDbContext db) =>
            await db.Games.AsNoTracking().OrderBy(g => g.Id).Select(g => new { g.Id }).ToListAsync());
    }
}
