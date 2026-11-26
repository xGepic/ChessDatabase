using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Research;

/// <summary>An answer from an online source, or why there is none. Never an exception.</summary>
/// <param name="Problem">Set when Value is null: a message for the Owner.</param>
public sealed record OnlineAnswer<T>(T? Value, string? Problem) where T : class
{
    /// <summary>A successful answer.</summary>
    public static OnlineAnswer<T> Of(T value) => new(value, null);

    /// <summary>No answer; the problem is shown to the Owner as it is.</summary>
    public static OnlineAnswer<T> Unavailable(string problem) => new(null, problem);
}

/// <summary>The Lichess masters explorer's answer for a position, in its own JSON shape.</summary>
public sealed record LichessMasters(long White, long Draws, long Black, List<LichessMastersMove> Moves, List<LichessTopGame> TopGames);

/// <summary>A move in <see cref="LichessMasters"/>: how often each side won after it.</summary>
public sealed record LichessMastersMove(string Uci, long White, long Draws, long Black, int? AverageRating);

/// <summary>A top game in <see cref="LichessMasters"/>. Winner is "white", "black" or null for a draw.</summary>
public sealed record LichessTopGame(string Id, string? Winner, LichessPlayer White, LichessPlayer Black, int? Year);

/// <summary>A player in <see cref="LichessTopGame"/>.</summary>
public sealed record LichessPlayer(string Name, int? Rating);

/// <summary>
/// The Masters source of the Opening Tree. Answers never throw: an unavailable source comes back
/// as <see cref="OnlineAnswer{T}"/> with a message, and nothing else in the app is affected.
/// </summary>
public interface IMastersSource
{
    /// <summary>Move statistics and top Master Games for a position.</summary>
    Task<OnlineAnswer<LichessMasters>> PositionAsync(Position position);

    /// <summary>A Master Game's PGN, by its Lichess id.</summary>
    Task<OnlineAnswer<string>> GamePgnAsync(string id);
}

/// <summary>
/// What the Masters source remembers for the app's run: answers by position and by game, and when
/// Lichess last asked to be left alone. Shared by every request (the HTTP client itself is not).
/// </summary>
public sealed class MastersState
{
    /// <summary>Beyond this many answers a cache starts over, so a long run can't grow without bound.</summary>
    private const int MaxEntries = 5000;

    private readonly Lock _lock = new();
    private DateTimeOffset _quietUntil = DateTimeOffset.MinValue;

    /// <summary>Lichess's terms: one request at a time.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Answers by position; transpositions and move counters don't matter (see <see cref="MastersClient.PositionKey"/>).</summary>
    public ConcurrentDictionary<string, LichessMasters> Positions { get; } = new();

    /// <summary>Master Game PGNs by Lichess id.</summary>
    public ConcurrentDictionary<string, string> GamePgns { get; } = new();

    /// <summary>Adds an answer to a cache, starting it over when it is full.</summary>
    public static void Remember<T>(ConcurrentDictionary<string, T> cache, string key, T value)
    {
        if (cache.Count >= MaxEntries) cache.Clear();
        cache[key] = value;
    }

    /// <summary>Whether Lichess asked to be left alone and that time hasn't passed.</summary>
    public bool IsQuiet(DateTimeOffset now)
    {
        lock (_lock) return now < _quietUntil;
    }

    public void KeepQuietUntil(DateTimeOffset until)
    {
        lock (_lock) _quietUntil = until;
    }
}

/// <summary>
/// The Masters source: the Lichess masters opening explorer (explorer.lichess.org). One request at a
/// time, as Lichess asks; a short timeout for Lichess's answer; a minute's quiet after "too many
/// requests". Successful answers are cached for the app's run. The Owner's optional Lichess token is
/// sent when stored.
/// </summary>
public sealed class MastersClient(
    HttpClient http, MastersState state, LichessTokenStore tokens, IConfiguration configuration, TimeProvider clock) : IMastersSource
{
    public const string DefaultBaseUrl = "https://explorer.lichess.org/";
    private const int MovesShown = 12, TopGamesShown = 15;
    private static readonly TimeSpan QuietAfterTooManyRequests = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A position as Lichess's statistics see it: placement, side to move, castling, en passant.</summary>
    public static string PositionKey(Position position) => string.Join(' ', position.ToFen().Split(' ').Take(4));

    public Task<OnlineAnswer<LichessMasters>> PositionAsync(Position position)
    {
        var key = PositionKey(position);
        return FetchAsync(state.Positions, key, $"masters?fen={Uri.EscapeDataString(key)}&moves={MovesShown}&topGames={TopGamesShown}",
            async content => JsonSerializer.Deserialize<LichessMasters>(await content.ReadAsStringAsync(), Json));
    }

    public Task<OnlineAnswer<string>> GamePgnAsync(string id) =>
        FetchAsync(state.GamePgns, id, $"masters/pgn/{id}", async content => await content.ReadAsStringAsync());

    private static OnlineAnswer<T> Unavailable<T>(string reason) where T : class => OnlineAnswer<T>.Unavailable($"Masters unavailable: {reason}");

    private async Task<OnlineAnswer<T>> FetchAsync<T>(
        ConcurrentDictionary<string, T> cache, string key, string path, Func<HttpContent, Task<T?>> read) where T : class
    {
        if (cache.TryGetValue(key, out var cached)) return OnlineAnswer<T>.Of(cached);
        if (state.IsQuiet(clock.GetUtcNow())) return Unavailable<T>("Lichess asked to wait a minute.");
        var token = tokens.Read();

        // Waiting for an earlier request to finish is not Lichess being slow: it has its own, longer limit.
        if (!await state.Gate.WaitAsync(TimeSpan.FromSeconds(configuration.GetValue("Masters:QueueSeconds", 15.0))))
            return Unavailable<T>("still busy with earlier requests. Try again in a moment.");
        try
        {
            if (cache.TryGetValue(key, out cached)) return OnlineAnswer<T>.Of(cached); // answered while waiting
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(configuration.GetValue("Masters:TimeoutSeconds", 5.0)));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl(), path));
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, timeout.Token);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    if (await read(response.Content) is not { } value) return Unavailable<T>("Lichess sent something unexpected.");
                    MastersState.Remember(cache, key, value);
                    return OnlineAnswer<T>.Of(value);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return Unavailable<T>("Lichess wants a Lichess API token. Add one in Settings.");
                case HttpStatusCode.TooManyRequests:
                    state.KeepQuietUntil(clock.GetUtcNow() + QuietAfterTooManyRequests);
                    return Unavailable<T>("Lichess asked to wait a minute.");
                default:
                    return Unavailable<T>($"Lichess answered {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            return Unavailable<T>("Lichess did not answer in time.");
        }
        catch (HttpRequestException)
        {
            return Unavailable<T>("Lichess can't be reached. Are you offline?");
        }
        catch (JsonException)
        {
            return Unavailable<T>("Lichess sent something unexpected.");
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private Uri BaseUrl()
    {
        var url = configuration["Masters:BaseUrl"];
        return new Uri(string.IsNullOrWhiteSpace(url) ? DefaultBaseUrl : url.EndsWith('/') ? url : url + "/");
    }
}
