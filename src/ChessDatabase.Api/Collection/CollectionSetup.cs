using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Collection;

public static class CollectionSetup
{
    /// <summary>Default location: a per-user app data folder, e.g. %LOCALAPPDATA%\ChessDatabase\collection.db.</summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChessDatabase",
            "collection.db");

    /// <summary>The configured database file (Collection:DatabasePath), or the per-user default.</summary>
    public static string DatabasePath(IConfiguration configuration)
    {
        var path = configuration["Collection:DatabasePath"];
        return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultDatabasePath() : path);
    }

    public static IServiceCollection AddCollection(this IServiceCollection services)
    {
        // The path is read when the context is built, so hosts and tests can set it after this call.
        services.AddDbContext<CollectionDbContext>((provider, options) =>
            options.UseSqlite($"Data Source={DatabasePath(provider.GetRequiredService<IConfiguration>())}"));
        return services;
    }

    /// <summary>Creates the database on first start and applies any pending migrations.</summary>
    public static void MigrateCollection(this IServiceProvider services)
    {
        var path = DatabasePath(services.GetRequiredService<IConfiguration>());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Database.Migrate();
    }
}
