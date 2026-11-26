using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>
/// Filters and sort order for the game list (and the PGN export of it). Every filter is optional
/// and they combine. Dates may be partial ("2025", "2025-03"); a Game counts as inside a date range
/// when its (possibly partial) date overlaps it. Result is from the Owner's point of view.
/// </summary>
public sealed record GameListQuery(
    int? OpponentId = null,
    int? EventId = null,
    string? From = null,
    string? To = null,
    string? Result = null,
    string? Colour = null,
    string? EcoFrom = null,
    string? EcoTo = null,
    string? Tag = null,
    string? TimeControl = null,
    string? Sort = null,
    string? Direction = null)
{
    public static readonly string[] SortColumns =
        ["date", "white", "whiteElo", "black", "blackElo", "result", "opponent", "colour", "event", "eco"];

    private static readonly string[] OwnerResults = ["win", "draw", "loss"];

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (From is not null && !GameEndpoints.IsValidDate(From)) errors["from"] = ["Write the date as YYYY, YYYY-MM or YYYY-MM-DD."];
        if (To is not null && !GameEndpoints.IsValidDate(To)) errors["to"] = ["Write the date as YYYY, YYYY-MM or YYYY-MM-DD."];
        if (Result is not null && !OwnerResults.Contains(Result)) errors["result"] = ["Result is win, draw or loss."];
        if (Colour is not null && Colour is not ("white" or "black")) errors["colour"] = ["Colour is white or black."];
        if (Sort is not null && !SortColumns.Contains(Sort)) errors["sort"] = [$"Sort by one of: {string.Join(", ", SortColumns)}."];
        if (Direction is not null && Direction is not ("asc" or "desc")) errors["direction"] = ["Direction is asc or desc."];
        return errors;
    }

    /// <summary>The matching Games in list order, each with its list row.</summary>
    public async Task<List<(GameRecord Game, GameSummaryDto Row)>> RunAsync(CollectionDbContext db, bool withMoves = false)
    {
        var query = db.Games.AsNoTracking().Include(g => g.Opponent).Include(g => g.Event).Include(g => g.Tags).AsSplitQuery();
        if (withMoves) query = query.Include(g => g.MoveNodes);
        var games = await query.ToListAsync();
        var ownerName = await GameEndpoints.OwnerNameAsync(db);

        // Up to 10,000 Games: filtering and sorting in memory keeps partial dates and names simple.
        var rows = games
            .Where(Matches)
            .OrderByDescending(g => g.Date ?? "").ThenByDescending(g => g.CreatedAt)
            .Select(g => (Game: g, Row: GameEndpoints.ToSummary(g, ownerName)))
            .ToList();
        return Sorted(rows);
    }

    private bool Matches(GameRecord game)
    {
        if (OpponentId is { } opponentId && game.OpponentId != opponentId) return false;
        if (EventId is { } eventId && game.EventId != eventId) return false;
        if (Colour is not null && !string.Equals(game.OwnerColour.ToString(), Colour, StringComparison.OrdinalIgnoreCase)) return false;
        if (Result is not null && OwnerResult(game) != Result) return false;
        if (TimeControl is not null && !string.Equals(game.TimeControl, TimeControl.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (Tag is not null && !game.Tags.Any(t => string.Equals(t.Name, Tag.Trim(), StringComparison.CurrentCultureIgnoreCase))) return false;
        if (EcoFrom is not null && (game.Eco is null || string.CompareOrdinal(game.Eco, EcoFrom.ToUpperInvariant()) < 0)) return false;
        if (EcoTo is not null && (game.Eco is null || string.CompareOrdinal(game.Eco, EcoTo.ToUpperInvariant()) > 0)) return false;
        if (From is not null || To is not null)
        {
            if (game.Date is null) return false;
            var (start, end) = DateRange(game.Date);
            if (From is not null && string.CompareOrdinal(end, DateRange(From).Start) < 0) return false;
            if (To is not null && string.CompareOrdinal(start, DateRange(To).End) > 0) return false;
        }
        return true;
    }

    private List<(GameRecord Game, GameSummaryDto Row)> Sorted(List<(GameRecord Game, GameSummaryDto Row)> rows)
    {
        if (Sort is null) return Direction == "asc" ? [.. Enumerable.Reverse(rows)] : rows;

        Func<GameSummaryDto, IComparable?> key = Sort switch
        {
            "date" => r => r.Date,
            "white" => r => r.White.ToLowerInvariant(),
            "black" => r => r.Black.ToLowerInvariant(),
            "whiteElo" => r => r.WhiteElo,
            "blackElo" => r => r.BlackElo,
            "result" => r => r.Result,
            "opponent" => r => r.Opponent.Name.ToLowerInvariant(),
            "colour" => r => r.OwnerColour.ToString(),
            "event" => r => r.Event?.Name.ToLowerInvariant(),
            _ => r => r.Eco,
        };
        // Empty values go last in both directions; ties keep the default (newest first) order.
        var withValues = rows.Where(r => key(r.Row) is not null);
        var ordered = Direction == "desc"
            ? withValues.OrderByDescending(r => key(r.Row))
            : withValues.OrderBy(r => key(r.Row));
        return [.. ordered, .. rows.Where(r => key(r.Row) is null)];
    }

    private static string OwnerResult(GameRecord game) => game.Result switch
    {
        GameResult.Draw => "draw",
        GameResult.WhiteWins => game.OwnerColour == OwnerColour.White ? "win" : "loss",
        _ => game.OwnerColour == OwnerColour.Black ? "win" : "loss",
    };

    /// <summary>The first and last day a (partial) date can stand for: "2025-03" → 2025-03-01 … 2025-03-31.</summary>
    private static (string Start, string End) DateRange(string date)
    {
        var parts = date.Split('-');
        return parts.Length switch
        {
            1 => ($"{parts[0]}-01-01", $"{parts[0]}-12-31"),
            2 => ($"{date}-01", $"{date}-31"),
            _ => (date, date),
        };
    }
}
