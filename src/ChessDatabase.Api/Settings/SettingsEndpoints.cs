using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Research;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Settings;

/// <param name="HasLichessToken">Whether a Lichess API token is stored. The token itself is never sent back.</param>
public sealed record SettingsDto(string OwnerName, bool HasLichessToken);

public sealed record SaveSettingsRequest(string? OwnerName);

/// <param name="Token">A Lichess personal API token; empty removes the stored one.</param>
public sealed record LichessTokenRequest(string? Token);

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings");

        settings.MapGet("/", async (CollectionDbContext db, LichessTokenStore lichessToken) =>
        {
            var record = await db.Settings.AsNoTracking().SingleOrDefaultAsync();
            return new SettingsDto(record?.OwnerName ?? "", lichessToken.HasToken);
        });

        // The token has its own call, so saving it never touches (or depends on) the other settings.
        settings.MapPut("/lichess-token", (LichessTokenRequest body, LichessTokenStore lichessToken) =>
        {
            lichessToken.Save(body.Token);
            return new { HasLichessToken = lichessToken.HasToken };
        });

        settings.MapPut("/", async (SaveSettingsRequest body, CollectionDbContext db, LichessTokenStore lichessToken) =>
        {
            var record = await LoadAsync(db);
            record.OwnerName = (body.OwnerName ?? "").Trim();
            await db.SaveChangesAsync();
            return new SettingsDto(record.OwnerName, lichessToken.HasToken);
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
