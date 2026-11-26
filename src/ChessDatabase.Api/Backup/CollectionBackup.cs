using System.Globalization;
using ChessDatabase.Api.Collection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Backup;

/// <param name="Folder">Where backups go now.</param>
/// <param name="IsDefault">True when no folder is configured and the default next to the Collection is used.</param>
/// <param name="LastBackupAt">The newest backup in that folder, or null when it has none.</param>
public sealed record BackupStatusDto(string Folder, bool IsDefault, DateTimeOffset? LastBackupAt);

/// <summary>
/// Copies the Collection's SQLite file into the backup folder with SQLite's online backup, so the
/// copy is consistent while the app runs, and keeps only the newest <see cref="Keep"/> copies.
/// The time of the last backup is read from the file names, so it always matches the folder.
/// </summary>
public sealed class CollectionBackup(IConfiguration configuration, IServiceScopeFactory scopes, TimeProvider clock)
{
    public const int Keep = 10;
    private const string Prefix = "collection-";
    private const string StampFormat = "yyyyMMdd-HHmmss-fff";
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Used when Settings name no folder: "Backups" beside the Collection's database file.</summary>
    public string DefaultFolder =>
        Path.Combine(Path.GetDirectoryName(CollectionSetup.DatabasePath(configuration))!, "Backups");

    public async Task<BackupStatusDto> StatusAsync()
    {
        var configured = await ConfiguredFolderAsync();
        var folder = configured ?? DefaultFolder;
        return new BackupStatusDto(folder, configured is null, Backups(folder).Select(b => (DateTimeOffset?)b.Time).FirstOrDefault());
    }

    /// <summary>Makes a backup now and deletes all but the newest <see cref="Keep"/>.</summary>
    public async Task<BackupStatusDto> BackUpAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var folder = (await ConfiguredFolderAsync()) ?? DefaultFolder;
            Directory.CreateDirectory(folder);
            var now = clock.GetLocalNow();
            var target = Path.Combine(folder, $"{Prefix}{now.ToString(StampFormat, CultureInfo.InvariantCulture)}.db");
            var partial = target + ".partial";

            using (var source = new SqliteConnection($"Data Source={CollectionSetup.DatabasePath(configuration)}"))
            using (var destination = new SqliteConnection($"Data Source={partial};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            File.Move(partial, target, overwrite: true);

            foreach (var old in Backups(folder).Skip(Keep)) File.Delete(old.Path);
            return await StatusAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The backups in a folder, newest first.</summary>
    private IEnumerable<(string Path, DateTimeOffset Time)> Backups(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, Prefix + "*.db")
            .Select(path => (Path: path, Stamp: Path.GetFileNameWithoutExtension(path)[Prefix.Length..]))
            .Where(b => DateTime.TryParseExact(b.Stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Select(b =>
            {
                var local = DateTime.ParseExact(b.Stamp, StampFormat, CultureInfo.InvariantCulture);
                return (b.Path, new DateTimeOffset(local, clock.LocalTimeZone.GetUtcOffset(local)));
            })
            .OrderByDescending(b => b.Item2)
            .ToList();
    }

    private async Task<string?> ConfiguredFolderAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var folder = await db.Settings.AsNoTracking().Select(s => s.BackupFolder).SingleOrDefaultAsync();
        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }
}
