using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ChessDatabase.Smoke.Tests;

public class SmokeTests : IAsyncLifetime
{
    private readonly SmokeApp _app = new();
    private IPage Page => _app.Page;

    public Task InitializeAsync() => _app.InitializeAsync();
    public Task DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Opens_on_an_empty_game_list()
    {
        await Page.GotoAsync(_app.BaseUrl);

        await Expect(Page.GetByTestId("empty-game-list")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Enter_a_short_game_and_find_it_in_the_game_list()
    {
        await Page.GotoAsync(_app.BaseUrl);

        // Settings: the Owner's name
        await Page.GetByTestId("tab-settings").ClickAsync();
        await Page.GetByTestId("owner-name").FillAsync("Smoke Owner");
        await Page.GetByTestId("owner-name").BlurAsync();
        await Expect(Page.GetByTestId("settings-status")).ToHaveTextAsync("Saved");

        // New Game: colour, Opponent created inline, Elos, result
        await Page.GetByTestId("tab-games").ClickAsync();
        await Page.GetByTestId("new-game").ClickAsync();
        await Page.GetByTestId("colour-white").ClickAsync();
        await Page.GetByTestId("opponent-input").FillAsync("Smoke Opponent");
        await Page.GetByTestId("opponent-create").ClickAsync();
        await Page.GetByTestId("owner-elo").FillAsync("1650");
        await Page.GetByTestId("opponent-elo").FillAsync("0");
        await Page.GetByTestId("result-1-0").ClickAsync();
        await Page.GetByTestId("start-game").ClickAsync();

        // Moves on the board
        await Expect(Page.GetByTestId("board")).ToBeVisibleAsync();
        await Page.PlayAsync("e2e4");
        await Expect(Page.GetByTestId("move")).ToHaveCountAsync(1);
        await Page.PlayAsync("e7e5");
        await Expect(Page.GetByTestId("move")).ToHaveCountAsync(2);
        await Page.PlayAsync("g1f3");
        await Expect(Page.GetByTestId("move")).ToHaveTextAsync(["e4", "e5", "Nf3"]);

        // An illegal move is not recorded
        await Page.PlayAsync("f3f5");
        await Expect(Page.GetByTestId("move")).ToHaveCountAsync(3);

        // The game list shows it
        await Page.GetByTestId("tab-games").ClickAsync();
        var row = Page.GetByTestId("game-row");
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Smoke Owner");
        await Expect(row).ToContainTextAsync("1650");
        await Expect(row).ToContainTextAsync("unrated");
        await Expect(row).ToContainTextAsync("1-0");
        await Expect(row.GetByTestId("game-opponent")).ToHaveTextAsync("Smoke Opponent");

        // Reopening shows the Game unchanged
        await row.ClickAsync();
        await Expect(Page.GetByTestId("move")).ToHaveTextAsync(["e4", "e5", "Nf3"]);
    }
}
