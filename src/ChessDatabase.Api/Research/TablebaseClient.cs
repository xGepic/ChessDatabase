using System.Collections.Concurrent;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Research;

/// <summary>
/// The Lichess tablebase's answer for a position, in its own JSON shape. Category is from the
/// side to move, as Lichess writes it (read by <see cref="TablebaseVerdicts.Parse"/>).
/// </summary>
public sealed record LichessTablebase(string Category, int? Dtz, int? Dtm, List<LichessTablebaseMove> Moves);

/// <summary>A move in <see cref="LichessTablebase"/>; Category, Dtz and Dtm are from the side to move after it.</summary>
public sealed record LichessTablebaseMove(string Uci, string Category, int? Dtz, int? Dtm, bool Zeroing);

/// <summary>
/// Tablebase lookups for endgames. Answers never throw: an unavailable source comes back as
/// <see cref="OnlineAnswer{T}"/> with a message.
/// </summary>
public interface ITablebaseSource
{
    /// <summary>The largest number of pieces (kings included) the tablebase covers.</summary>
    const int MaxPieces = 7;

    Task<OnlineAnswer<LichessTablebase>> LookUpAsync(Position position);
}

/// <summary>What the Tablebase source keeps for the app's run: its request state and its answers.</summary>
public sealed class TablebaseState
{
    public LichessService Service { get; } =
        new("Tablebase", "Tablebase", "https://tablebase.lichess.org/", new OnlineSourceState());

    public ConcurrentDictionary<string, LichessTablebase> Answers { get; } = new();
}

/// <summary>The Lichess tablebase (tablebase.lichess.org; no token needed), asked through <see cref="LichessFetch"/>.</summary>
public sealed class TablebaseClient(HttpClient http, TablebaseState state, IConfiguration configuration, TimeProvider clock) : ITablebaseSource
{
    private readonly LichessFetch fetch = new(http, configuration, clock);

    public Task<OnlineAnswer<LichessTablebase>> LookUpAsync(Position position)
    {
        var key = LichessPosition.Key(position);
        return fetch.GetAsync(state.Service, state.Answers, key, $"standard?fen={Uri.EscapeDataString(key)}", token: null,
            LichessFetch.ReadJsonAsync<LichessTablebase>);
    }
}
