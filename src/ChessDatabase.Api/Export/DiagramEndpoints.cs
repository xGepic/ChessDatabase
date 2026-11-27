using ChessDatabase.Api.Research;
using ChessDatabase.Rules;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Export;

/// <summary>A board diagram of any position as a PNG, for "Copy diagram".</summary>
public static class DiagramEndpoints
{
    public static void MapDiagramEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diagram", (string? fen, string? orientation, bool? coordinates, string? lastMove) =>
        {
            if (PositionInput.Read(fen, out var position) is { } problem) return problem;
            if (ReadOrientation(orientation) is not { } side)
                return Invalid("orientation", "The orientation is \"white\" or \"black\".");
            Move? last = null;
            if (!string.IsNullOrEmpty(lastMove))
            {
                if (!Move.TryParseUci(lastMove, out var move)) return Invalid("lastMove", "The last move is a UCI move such as e2e4.");
                last = move;
            }
            var png = DiagramRenderer.RenderPng(position, new DiagramOptions(side, coordinates ?? false, last));
            return Results.File(png, "image/png");
        });
    }

    private static Color? ReadOrientation(string? text) => text?.ToLowerInvariant() switch
    {
        null or "" or "white" => Color.White,
        "black" => Color.Black,
        _ => null,
    };
}
