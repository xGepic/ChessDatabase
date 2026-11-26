using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

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

/// <summary>
/// Per online source, for the app's run: Lichess's terms (one request at a time, a minute's quiet
/// after "too many requests").
/// </summary>
public sealed class OnlineSourceState
{
    /// <summary>Beyond this many answers a cache starts over, so a long run can't grow without bound.</summary>
    public const int MaxCachedAnswers = 5000;

    private readonly Lock _lock = new();
    private DateTimeOffset _quietUntil = DateTimeOffset.MinValue;

    /// <summary>One request at a time.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Whether Lichess asked to be left alone and that time hasn't passed.</summary>
    public bool IsQuiet(DateTimeOffset now)
    {
        lock (_lock) return now < _quietUntil;
    }

    public void KeepQuietUntil(DateTimeOffset until)
    {
        lock (_lock) _quietUntil = until;
    }

    /// <summary>Adds an answer to a cache, starting it over when it is full.</summary>
    public static void Remember<T>(ConcurrentDictionary<string, T> cache, string key, T value)
    {
        if (cache.Count >= MaxCachedAnswers) cache.Clear();
        cache[key] = value;
    }
}

/// <summary>Where and how to ask one Lichess service. Label starts every unavailable message ("Masters", "Tablebase").</summary>
/// <param name="ConfigSection">Configuration section with BaseUrl, TimeoutSeconds (default 5) and QueueSeconds (default 15).</param>
public sealed record LichessService(string Label, string ConfigSection, string DefaultBaseUrl, OnlineSourceState State);

/// <summary>
/// One GET to a Lichess service, by its rules: one request at a time, a short timeout for the
/// answer (waiting behind an earlier request has its own, longer limit), a minute's quiet after
/// "too many requests". Successful answers go into the given cache. Every failure becomes
/// "{Label} unavailable: reason" rather than an exception.
/// </summary>
public sealed class LichessFetch(HttpClient http, IConfiguration configuration, TimeProvider clock)
{
    private static readonly TimeSpan QuietAfterTooManyRequests = TimeSpan.FromMinutes(1);

    public async Task<OnlineAnswer<T>> GetAsync<T>(
        LichessService service, ConcurrentDictionary<string, T> cache, string key, string path, string? token,
        Func<HttpContent, Task<T?>> read) where T : class
    {
        OnlineAnswer<T> Unavailable(string reason) => OnlineAnswer<T>.Unavailable($"{service.Label} unavailable: {reason}");

        if (cache.TryGetValue(key, out var cached)) return OnlineAnswer<T>.Of(cached);
        var state = service.State;
        if (state.IsQuiet(clock.GetUtcNow())) return Unavailable("Lichess asked to wait a minute.");

        var settings = configuration.GetSection(service.ConfigSection);
        if (!await state.Gate.WaitAsync(TimeSpan.FromSeconds(settings.GetValue("QueueSeconds", 15.0))))
            return Unavailable("still busy with earlier requests. Try again in a moment.");
        try
        {
            if (cache.TryGetValue(key, out cached)) return OnlineAnswer<T>.Of(cached); // answered while waiting
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.GetValue("TimeoutSeconds", 5.0)));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl(service, settings), path));
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, timeout.Token);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    if (await read(response.Content) is not { } value) return Unavailable("Lichess sent something unexpected.");
                    OnlineSourceState.Remember(cache, key, value);
                    return OnlineAnswer<T>.Of(value);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return Unavailable("Lichess wants a Lichess API token. Add one in Settings.");
                case HttpStatusCode.TooManyRequests:
                    state.KeepQuietUntil(clock.GetUtcNow() + QuietAfterTooManyRequests);
                    return Unavailable("Lichess asked to wait a minute.");
                default:
                    return Unavailable($"Lichess answered {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            return Unavailable("Lichess did not answer in time.");
        }
        catch (HttpRequestException)
        {
            return Unavailable("Lichess can't be reached. Are you offline?");
        }
        catch (JsonException)
        {
            return Unavailable("Lichess sent something unexpected.");
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>Reads Lichess's JSON; property names match case-insensitively.</summary>
    public static async Task<T?> ReadJsonAsync<T>(HttpContent content) =>
        JsonSerializer.Deserialize<T>(await content.ReadAsStringAsync(), Json);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static Uri BaseUrl(LichessService service, IConfigurationSection settings)
    {
        var url = settings["BaseUrl"];
        return new Uri(string.IsNullOrWhiteSpace(url) ? service.DefaultBaseUrl : url.EndsWith('/') ? url : url + "/");
    }
}
