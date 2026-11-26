using Microsoft.Playwright;

namespace ChessDatabase.Smoke.Tests;

/// <summary>Plays moves on the chessground board by clicking squares, like the Owner would.</summary>
public static class BoardDriver
{
    public static async Task PlayAsync(this IPage page, string uci, bool flipped = false)
    {
        await page.ClickSquareAsync(uci[..2], flipped);
        await page.ClickSquareAsync(uci[2..4], flipped);
    }

    public static async Task ClickSquareAsync(this IPage page, string square, bool flipped = false)
    {
        var board = page.Locator("[data-testid=board] cg-board");
        var box = await board.BoundingBoxAsync() ?? throw new InvalidOperationException("Board not visible.");
        int file = square[0] - 'a', rank = square[1] - '1';
        var column = flipped ? 7 - file : file;
        var row = flipped ? rank : 7 - rank;
        var size = box.Width / 8;
        await page.Mouse.ClickAsync(box.X + (column + 0.5f) * size, box.Y + (row + 0.5f) * size);
    }
}
