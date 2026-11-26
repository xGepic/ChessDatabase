using System.Text.RegularExpressions;
using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>A Game's result seen from the Owner's side.</summary>
public enum OwnerResult
{
    Win,
    Draw,
    Loss,
}

public enum GameListColumn
{
    Date,
    White,
    WhiteElo,
    Black,
    BlackElo,
    Result,
    Opponent,
    OwnerColour,
    Event,
    Eco,
}

public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>
/// Filters and sort order for the game list (and the PGN export of it). Every filter is optional
/// and they combine. Dates may be partial ("2025", "2025-03"); a Game counts as inside a date range
/// when its (possibly partial) date overlaps it. ECO bounds may be short: "B" is B00–B99, "B9" is B90–B99.
/// Result, OwnerColour, Sort and Direction are names of the enums above, in any letter case.
/// </summary>
public sealed partial record GameListQuery(
    int? OpponentId = null,
    int? EventId = null,
    string? From = null,
    string? To = null,
    string? Result = null,
    string? OwnerColour = null,
    string? EcoFrom = null,
    string? EcoTo = null,
    string? Tag = null,
    string? TimeControl = null,
    string? Sort = null,
    string? Direction = null)
{
    private OwnerResult? _result;
    private OwnerColour? _ownerColour;
    private GameListColumn? _sort;
    private SortDirection? _direction;

    /// <summary>Checks every filter and reads the enum names. Empty when the query can run.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        _result = Parse<OwnerResult>(Result, "result", errors);
        _ownerColour = Parse<OwnerColour>(OwnerColour, "ownerColour", errors);
        _sort = Parse<GameListColumn>(Sort, "sort", errors);
        _direction = Parse<SortDirection>(Direction, "direction", errors);
        if (From is not null && !GameDates.IsValid(From)) errors["from"] = [GameDates.FormatMessage];
        if (To is not null && !GameDates.IsValid(To)) errors["to"] = [GameDates.FormatMessage];
        if (EcoFrom is not null && !EcoBound().IsMatch(EcoFrom)) errors["ecoFrom"] = ["An ECO code runs from A00 to E99."];
        if (EcoTo is not null && !EcoBound().IsMatch(EcoTo)) errors["ecoTo"] = ["An ECO code runs from A00 to E99."];
        return errors;
    }

    /// <summary>The matching Games in list order, each with its list row.</summary>
    public async Task<List<(GameRecord Game, GameSummaryDto Row)>> RunAsync(CollectionDbContext db, bool withMoves = false)
    {
        if (Validate().Count > 0) throw new InvalidOperationException("Validate the query before running it.");
        var games = await db.Games.WithDetails(withMoves).ToListAsync();
        var ownerName = await GameQueries.OwnerNameAsync(db);

        // Up to 10,000 Games: filtering and sorting in memory keeps partial dates and names simple.
        var rows = games
            .Where(Matches)
            .OrderByDescending(g => g.Date ?? "").ThenByDescending(g => g.CreatedAt)
            .Select(g => (Game: g, Row: GameQueries.ToSummary(g, ownerName)))
            .ToList();
        return Sorted(rows);
    }

    private bool Matches(GameRecord game)
    {
        if (OpponentId is { } opponentId && game.OpponentId != opponentId) return false;
        if (EventId is { } eventId && game.EventId != eventId) return false;
        if (_ownerColour is { } colour && game.OwnerColour != colour) return false;
        if (_result is { } result && ResultForOwner(game) != result) return false;
        if (TimeControl is not null && !Names.Same(game.TimeControl, TimeControl)) return false;
        if (Tag is not null && !game.Tags.Any(t => Names.Same(t.Name, Tag))) return false;
        if (EcoFrom is not null && (game.Eco is null || string.CompareOrdinal(game.Eco, PadEco(EcoFrom, '0')) < 0)) return false;
        if (EcoTo is not null && (game.Eco is null || string.CompareOrdinal(game.Eco, PadEco(EcoTo, '9')) > 0)) return false;
        if (From is not null || To is not null)
        {
            if (game.Date is null) return false;
            var (start, end) = GameDates.Range(game.Date);
            if (From is not null && string.CompareOrdinal(end, GameDates.Range(From).Start) < 0) return false;
            if (To is not null && string.CompareOrdinal(start, GameDates.Range(To).End) > 0) return false;
        }
        return true;
    }

    private List<(GameRecord Game, GameSummaryDto Row)> Sorted(List<(GameRecord Game, GameSummaryDto Row)> rows)
    {
        if (_sort is not { } column) return _direction == SortDirection.Asc ? [.. Enumerable.Reverse(rows)] : rows;

        Func<GameSummaryDto, IComparable?> key = column switch
        {
            GameListColumn.Date => r => r.Date,
            GameListColumn.White => r => r.White.ToLowerInvariant(),
            GameListColumn.Black => r => r.Black.ToLowerInvariant(),
            GameListColumn.WhiteElo => r => r.WhiteElo,
            GameListColumn.BlackElo => r => r.BlackElo,
            GameListColumn.Result => r => r.Result,
            GameListColumn.Opponent => r => r.Opponent.Name.ToLowerInvariant(),
            GameListColumn.OwnerColour => r => r.OwnerColour.ToString(),
            GameListColumn.Event => r => r.Event?.Name.ToLowerInvariant(),
            _ => r => r.Eco,
        };
        // Empty values go last in both directions; ties keep the default (newest first) order.
        var withValues = rows.Where(r => key(r.Row) is not null);
        var ordered = _direction == SortDirection.Desc
            ? withValues.OrderByDescending(r => key(r.Row))
            : withValues.OrderBy(r => key(r.Row));
        return [.. ordered, .. rows.Where(r => key(r.Row) is null)];
    }

    /// <summary>An enum member by name, ignoring case; numbers and unknown names are errors.</summary>
    private static T? Parse<T>(string? text, string field, Dictionary<string, string[]> errors) where T : struct, Enum
    {
        if (text is null) return null;
        if (!char.IsDigit(text.FirstOrDefault()) && Enum.TryParse<T>(text, ignoreCase: true, out var value)) return value;
        errors[field] = [$"Use one of: {string.Join(", ", Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]))}."];
        return null;
    }

    private static OwnerResult ResultForOwner(GameRecord game) => game.Result switch
    {
        GameResult.Draw => OwnerResult.Draw,
        GameResult.WhiteWins => game.OwnerColour == Collection.OwnerColour.White ? OwnerResult.Win : OwnerResult.Loss,
        _ => game.OwnerColour == Collection.OwnerColour.Black ? OwnerResult.Win : OwnerResult.Loss,
    };

    /// <summary>"b" → "B00" (lower bound) or "B99" (upper bound); "B9" → "B90" / "B99".</summary>
    private static string PadEco(string bound, char fill) => bound.ToUpperInvariant().PadRight(3, fill);

    [GeneratedRegex("^[A-Ea-e][0-9]{0,2}$")]
    private static partial Regex EcoBound();
}
