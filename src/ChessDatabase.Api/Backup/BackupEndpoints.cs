using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Settings;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Backup;

/// <param name="Folder">An absolute folder; empty means the default next to the Collection.</param>
public sealed record BackupFolderRequest(string? Folder);

public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var backups = app.MapGroup("/api/backups");

        backups.MapGet("/", (CollectionBackup backup) => backup.StatusAsync());

        // Back up now.
        backups.MapPost("/", async (CollectionBackup backup) =>
        {
            try
            {
                return Results.Ok(await backup.BackUpAsync());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                return Invalid("folder", $"The backup could not be written: {e.Message}");
            }
        });

        backups.MapPut("/folder", async (BackupFolderRequest body, CollectionDbContext db, CollectionBackup backup) =>
        {
            var folder = body.Folder?.Trim() ?? "";
            if (folder.Length > 0 && !Path.IsPathFullyQualified(folder))
                return Invalid("folder", "Give the full path of a folder, such as D:\\Backups\\Chess.");
            var settings = await SettingsEndpoints.LoadAsync(db);
            settings.BackupFolder = folder.Length == 0 ? null : Path.GetFullPath(folder);
            await db.SaveChangesAsync();
            return Results.Ok(await backup.StatusAsync());
        });
    }
}
