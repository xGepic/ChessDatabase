using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Settings;

/// <param name="Aliases">Other spellings of the Owner's name, used to recognise the Owner on import.</param>
public sealed record SettingsDto(string OwnerName, IReadOnlyList<string> Aliases);

/// <summary>Leaving Aliases out keeps the stored ones.</summary>
public sealed record SaveSettingsRequest(string? OwnerName, IReadOnlyList<string>? Aliases);

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings");

        settings.MapGet("/", async (CollectionDbContext db) =>
        {
            var record = await db.Settings.AsNoTracking().SingleOrDefaultAsync();
            return new SettingsDto(record?.OwnerName ?? "", record?.Aliases ?? []);
        });

        settings.MapPut("/", async (SaveSettingsRequest body, CollectionDbContext db) =>
        {
            var record = await LoadAsync(db);
            record.OwnerName = (body.OwnerName ?? "").Trim();
            if (body.Aliases is not null)
                record.Aliases = [.. body.Aliases.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct()];
            await db.SaveChangesAsync();
            return new SettingsDto(record.OwnerName, record.Aliases);
        });
    }

    public static async Task<SettingsRecord> LoadAsync(CollectionDbContext db)
    {
        var record = await db.Settings.SingleOrDefaultAsync();
        if (record is null)
        {
            record = new SettingsRecord();
            db.Settings.Add(record);
        }
        return record;
    }
}
