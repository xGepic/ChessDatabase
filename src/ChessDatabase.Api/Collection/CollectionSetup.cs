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

    public static IServiceCollection AddCollection(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["Collection:DatabasePath"];
        if (string.IsNullOrWhiteSpace(path)) path = DefaultDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        services.AddDbContext<CollectionDbContext>(o => o.UseSqlite($"Data Source={path}"));
        return services;
    }

    /// <summary>Creates the database on first start and applies any pending migrations.</summary>
    public static void MigrateCollection(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Database.Migrate();
    }
}
