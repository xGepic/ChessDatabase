using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;

namespace ChessDatabase.Api;

/// <summary>
/// Composes the backend. Used by the browser-mode entry point (Program), the desktop shell
/// and the tests, so all of them run the same app.
/// </summary>
public static class ChessDatabaseApp
{
    public static WebApplicationBuilder CreateBuilder(string[] args, WebApplicationOptions? options = null)
    {
        var builder = options is null
            ? WebApplication.CreateBuilder(args)
            : WebApplication.CreateBuilder(options);

        builder.Services.AddCollection(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.CamelCase)));
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        app.Services.MigrateCollection();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGameEndpoints();

        // Angular routes (e.g. /games/12) fall back to the UI's index.html when it is present.
        app.MapFallbackToFile("index.html");
        return app;
    }
}
