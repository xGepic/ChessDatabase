using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Research;

/// <summary>An answer from an online source, or why there is none. Never an exception.</summary>
public sealed record OnlineAnswer<T>(T? Value, string? Problem) where T : class
{
    public static OnlineAnswer<T> Of(T value) => new(value, null);
    public static OnlineAnswer<T> Unavailable(string problem) => new(null, problem);
}

/// <summary>The Lichess masters explorer's answer for a position (its own JSON shape).</summary>
public sealed record LichessMasters(long White, long Draws, long Black, List<LichessMastersMove> Moves, List<LichessTopGame> TopGames);

public sealed record LichessMastersMove(string Uci, long White, long Draws, long Black, int? AverageRating);

public sealed record LichessTopGame(string Id, string? Winner, LichessPlayer White, LichessPlayer Black, int? Year);

public sealed record LichessPlayer(string Name, int? Rating);

/// <summary>
/// What the Masters source remembers for the app's run: answers by position and game, and when
/// Lichess last asked to be left alone. Shared by every request (the HTTP client itself is not).
/// </summary>
public sealed class MastersState
{
    /// <summary>Beyond this many cached answers the cache starts over, so a long run can't grow without bound.</summary>
    private const int MaxEntries = 5000;

    private readonly ConcurrentDictionary<string, object> _cache = new();

    /// <summary>Lichess's terms: one request at a time.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>After a 429, no request before this time.</summary>
    public DateTimeOffset? QuietUntil { get; set; }

    public bool TryGet<T>(string key, out T value) where T : class
    {
        value = null!;
        if (!_cache.TryGetValue(key, out var cached)) return false;
        value = (T)cached;
        return true;
    }

    public void Remember(string key, object value)
    {
        if (_cache.Count >= MaxEntries) _cache.Clear();
        _cache[key] = value;
    }
}

/// <summary>
/// The Masters source: the Lichess masters opening explorer (explorer.lichess.org). Answers come
/// back as <see cref="OnlineAnswer{T}"/>: offline, slow (short timeout), refused or rate-limited
/// Lichess becomes a "Masters unavailable" message and nothing else is affected. Successful answers
/// are cached for the app's run; the Owner's optional Lichess token is sent when set.
/// </summary>
public sealed class MastersClient(HttpClient http, MastersState state, IConfiguration configuration, IServiceScopeFactory scopes, TimeProvider clock)
{
    public const string DefaultBaseUrl = "https://explorer.lichess.org/";
    private static readonly TimeSpan QuietAfterTooManyRequests = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Move statistics and top games for a position (FEN), straight from Lichess.</summary>
    public Task<OnlineAnswer<LichessMasters>> PositionAsync(string fen) =>
        FetchAsync($"masters:{fen}", $"masters?fen={Uri.EscapeDataString(fen)}&moves=12&topGames=15",
            async content => JsonSerializer.Deserialize<LichessMasters>(await content.ReadAsStringAsync(), Json)!);

    /// <summary>A Master Game's PGN.</summary>
    public Task<OnlineAnswer<string>> GamePgnAsync(string id) =>
        FetchAsync($"pgn:{id}", $"masters/pgn/{id}", content => content.ReadAsStringAsync());

    private async Task<OnlineAnswer<T>> FetchAsync<T>(string key, string path, Func<HttpContent, Task<T>> read) where T : class
    {
        if (state.TryGet<T>(key, out var cached)) return OnlineAnswer<T>.Of(cached);
        if (state.QuietUntil is { } until && clock.GetUtcNow() < until)
            return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess asked to wait a minute.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(configuration.GetValue("Masters:TimeoutSeconds", 5.0)));
        try
        {
            await state.Gate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess did not answer in time.");
        }
        try
        {
            if (state.TryGet(key, out cached)) return OnlineAnswer<T>.Of(cached); // fetched while waiting
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl(), path));
            if (await TokenAsync() is { } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, timeout.Token);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var value = await read(response.Content);
                    state.Remember(key, value);
                    return OnlineAnswer<T>.Of(value);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess wants a Lichess API token. Add one in Settings.");
                case HttpStatusCode.TooManyRequests:
                    state.QuietUntil = clock.GetUtcNow() + QuietAfterTooManyRequests;
                    return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess asked to wait a minute.");
                default:
                    return OnlineAnswer<T>.Unavailable($"Masters unavailable: Lichess answered {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess did not answer in time.");
        }
        catch (HttpRequestException)
        {
            return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess can't be reached. Are you offline?");
        }
        catch (JsonException)
        {
            return OnlineAnswer<T>.Unavailable("Masters unavailable: Lichess sent something unexpected.");
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

    private async Task<string?> TokenAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        return await db.Settings.AsNoTracking().Select(s => s.LichessToken).SingleOrDefaultAsync();
    }
}
