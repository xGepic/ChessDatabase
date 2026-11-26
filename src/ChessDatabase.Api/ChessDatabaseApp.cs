using ChessDatabase.Api.Analysis;
using ChessDatabase.Api.Backup;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using ChessDatabase.Api.Games;
using ChessDatabase.Api.Import;
using ChessDatabase.Api.Openings;
using ChessDatabase.Api.Players;
using ChessDatabase.Api.Research;
using ChessDatabase.Api.Settings;
using ChessDatabase.Api.Statistics;
using ChessDatabase.Api.Training;

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

        builder.Services.AddCollection();
        builder.Services.AddSingleton<OpeningClassifier>();
        builder.Services.AddScoped<PgnImporter>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<CollectionBackup>();
        builder.Services.AddHostedService<DailyBackups>();
        builder.Services.AddSingleton<EngineHost>();
        builder.Services.AddSingleton<InteractiveAnalysis>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<MastersState>();
        builder.Services.AddSingleton<LichessTokenStore>();
        builder.Services.AddHttpClient<MastersClient>(http =>
        {
            http.Timeout = Timeout.InfiniteTimeSpan; // MastersClient applies its own short timeout
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ChessDatabase/1.0");
        });
        builder.Services.AddTransient<IMastersSource>(services => services.GetRequiredService<MastersClient>());
        builder.Services.AddSingleton<TablebaseState>();
        builder.Services.AddHttpClient<TablebaseClient>(http =>
        {
            http.Timeout = Timeout.InfiniteTimeSpan; // LichessFetch applies its own short timeout
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ChessDatabase/1.0");
        });
        builder.Services.AddTransient<ITablebaseSource>(services => services.GetRequiredService<TablebaseClient>());
        builder.Services.AddSingleton<GameAnalyzer>();
        builder.Services.AddSingleton<GameAnalysisQueue>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameAnalysisQueue>());
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
        Games.PositionIndex.Backfill(app.Services);

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGameEndpoints();
        app.MapSettingsEndpoints();
        app.MapPlayerEndpoints();
        app.MapDossierEndpoints();
        app.MapOwnerStatisticsEndpoints();
        app.MapRepertoireEndpoints();
        app.MapDeviationEndpoints();
        app.MapRepertoireTrainingEndpoints();
        app.MapLookupEndpoints();
        app.MapImportEndpoints();
        app.MapImportReviewEndpoints();
        app.MapBackupEndpoints();
        app.MapEngineEndpoints();
        app.MapAnalysisEndpoints();
        app.MapCopyAssessmentEndpoints();
        app.MapResearchEndpoints();
        app.MapMastersEndpoints();
        app.MapTablebaseEndpoints();
        app.MapHub<AnalysisHub>(AnalysisHub.Path);

        // Angular routes (e.g. /games/12) fall back to the UI's index.html when it is present.
        app.MapFallbackToFile("index.html");
        return app;
    }
}
