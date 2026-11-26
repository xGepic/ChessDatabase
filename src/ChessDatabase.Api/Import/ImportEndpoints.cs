using System.Text;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Import;

public sealed record ImportTextRequest(string? Pgn);

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var import = app.MapGroup("/api/import");

        import.MapPost("/text", async (ImportTextRequest body, PgnImporter importer) =>
            string.IsNullOrWhiteSpace(body.Pgn)
                ? Invalid("pgn", "Paste the PGN of one or more Games.")
                : Results.Ok(await importer.ImportAsync(body.Pgn)));

        // A local desktop app with no cookies or logins, so there is nothing for antiforgery to protect.
        import.MapPost("/file", async (IFormFile? file, PgnImporter importer) =>
        {
            if (file is null || file.Length == 0) return Invalid("file", "Choose a PGN file.");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            return Results.Ok(await importer.ImportAsync(Decode(buffer.ToArray())));
        }).DisableAntiforgery();
    }

    /// <summary>UTF-8 if the bytes are valid UTF-8; otherwise Latin-1, which older PGN files often use.</summary>
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
