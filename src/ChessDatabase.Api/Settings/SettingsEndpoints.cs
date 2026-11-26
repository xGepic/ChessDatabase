using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Settings;

public sealed record SettingsDto(string OwnerName);

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings");

        settings.MapGet("/", async (CollectionDbContext db) =>
        {
            var record = await db.Settings.AsNoTracking().SingleOrDefaultAsync();
            return new SettingsDto(record?.OwnerName ?? "");
        });

        settings.MapPut("/", async (SettingsDto body, CollectionDbContext db) =>
        {
            var record = await LoadAsync(db);
            record.OwnerName = (body.OwnerName ?? "").Trim();
            await db.SaveChangesAsync();
            return new SettingsDto(record.OwnerName);
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
