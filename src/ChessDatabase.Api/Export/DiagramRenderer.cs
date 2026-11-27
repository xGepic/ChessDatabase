using System.Collections.Frozen;
using ChessDatabase.Rules;
using SkiaSharp;
using Svg.Skia;

namespace ChessDatabase.Api.Export;

/// <summary>How a board diagram is drawn.</summary>
/// <param name="Orientation">The side shown at the bottom.</param>
/// <param name="Coordinates">Files and ranks in a margin left of and below the board.</param>
/// <param name="LastMove">Highlights its from and to squares.</param>
public sealed record DiagramOptions(Color Orientation = Color.White, bool Coordinates = false, Move? LastMove = null);

/// <summary>
/// Draws a position as a PNG board diagram: the app's boxwood and rosewood squares with the
/// cburnett pieces the board uses. Used for "Copy diagram" and for the diagrams in PDF export.
/// The same input always gives the same bytes.
/// </summary>
public static class DiagramRenderer
{
    public const int SquareSize = 60;
    public const int CoordinateMargin = 24;

    private static readonly SKColor Light = SKColor.Parse("#e8cfa0");
    private static readonly SKColor Dark = SKColor.Parse("#a8693d");
    private static readonly SKColor LastMoveTint = new(214, 170, 60, 107);
    private static readonly SKColor CoordinateInk = SKColor.Parse("#3b2a1d");

    /// <summary>The cburnett piece drawings (45 by 45 units), keyed like "wK".</summary>
    private static readonly Lazy<FrozenDictionary<string, SKPicture>> Pieces = new(LoadPieces);

    public static byte[] RenderPng(Position position, DiagramOptions options)
    {
        var margin = options.Coordinates ? CoordinateMargin : 0;
        var boardSize = 8 * SquareSize;
        var info = new SKImageInfo(boardSize + margin, boardSize + margin, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var fill = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = Square.At(file, rank);
            var rect = SquareRect(square, options.Orientation, margin);
            fill.Color = (file + rank) % 2 == 0 ? Dark : Light;
            canvas.DrawRect(rect, fill);
            if (options.LastMove is { } last && (last.From == square || last.To == square))
            {
                fill.Color = LastMoveTint;
                canvas.DrawRect(rect, fill);
            }
            if (position[square] is { } piece) DrawPiece(canvas, piece, rect);
        }

        if (options.Coordinates) DrawCoordinates(canvas, options.Orientation, margin);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKRect SquareRect(Square square, Color orientation, int margin)
    {
        var (column, row) = ToScreen(square.File, square.Rank, orientation);
        return SKRect.Create(margin + column * SquareSize, row * SquareSize, SquareSize, SquareSize);
    }

    /// <summary>Board file and rank to drawn column (left to right) and row (top to bottom). The mapping is its own inverse.</summary>
    private static (int Column, int Row) ToScreen(int file, int rank, Color orientation) =>
        orientation == Color.White ? (file, 7 - rank) : (7 - file, rank);

    private static void DrawPiece(SKCanvas canvas, Piece piece, SKRect rect)
    {
        var picture = Pieces.Value[$"{(piece.Color == Color.White ? 'w' : 'b')}{char.ToUpperInvariant(piece.ToFenChar())}"];
        canvas.Save();
        canvas.Translate(rect.Left, rect.Top);
        canvas.Scale(rect.Width / picture.CullRect.Width, rect.Height / picture.CullRect.Height);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static void DrawCoordinates(SKCanvas canvas, Color orientation, int margin)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 14);
        using var ink = new SKPaint { Color = CoordinateInk, IsAntialias = true };
        var boardSize = 8 * SquareSize;
        for (var i = 0; i < 8; i++)
        {
            var (file, rank) = ToScreen(i, i, orientation);
            var fileLabel = ((char)('a' + file)).ToString();
            var rankLabel = ((char)('1' + rank)).ToString();
            canvas.DrawText(fileLabel, margin + i * SquareSize + SquareSize / 2f, boardSize + margin - 7, SKTextAlign.Center, font, ink);
            canvas.DrawText(rankLabel, margin / 2f, i * SquareSize + SquareSize / 2f + 5, SKTextAlign.Center, font, ink);
        }
    }

    private static FrozenDictionary<string, SKPicture> LoadPieces()
    {
        var assembly = typeof(DiagramRenderer).Assembly;
        var pieces = new Dictionary<string, SKPicture>();
        foreach (var colour in "wb")
        foreach (var type in "KQRBNP")
        {
            var key = $"{colour}{type}";
            using var stream = assembly.GetManifestResourceStream($"ChessDatabase.Api.Export.Pieces.{key}.svg")
                ?? throw new InvalidOperationException($"The piece drawing {key}.svg is missing.");
            var svg = new SKSvg();
            pieces[key] = svg.Load(stream) ?? throw new InvalidDataException($"The piece drawing {key}.svg could not be read.");
        }
        return pieces.ToFrozenDictionary();
    }
}
