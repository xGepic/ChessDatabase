using Microsoft.AspNetCore.HostFiltering;

namespace ChessDatabase.Api;

/// <summary>
/// The app is a website on this PC without logins. Other sites open in the same browser can
/// still send requests to localhost, so the backend answers only to the host names localhost
/// and 127.0.0.1 (no DNS rebinding) and refuses changes whose page came from anywhere else
/// (no cross-site form posts).
/// </summary>
public static class LocalOnly
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "[::1]"];

    public static void AddLocalOnly(this IServiceCollection services) =>
        services.PostConfigure<HostFilteringOptions>(options => options.AllowedHosts = [.. LocalHosts]);

    public static void UseLocalOnly(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            var changes = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method);
            if (changes && context.Request.Headers.Origin is [{ } origin, ..] && !IsLocal(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context);
        });

    private static bool IsLocal(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && LocalHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
