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

    [Fact]
    public async Task Step_through_a_game_branch_into_a_variation_and_promote_it()
    {
        var gameId = await _app.CreateGameAsync("e4", "e5", "Nf3", "Nc6");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");
        var current = Page.Locator("[data-testid=move].current");
        await Expect(current).ToHaveTextAsync("Nc6");

        // Arrow keys step back and forth
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(current).ToHaveTextAsync("e5");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(current).ToHaveTextAsync("Nf3");
        await Page.Keyboard.PressAsync("Home");
        await Expect(current).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("End");
        await Expect(current).ToHaveTextAsync("Nc6");

        // Clicking a move goes there; a different move starts a Variation
        await Page.GetByTestId("move").Filter(new() { HasText = "e5" }).ClickAsync();
        await Page.PlayAsync("f1c4");
        await Expect(Page.GetByTestId("variation")).ToHaveTextAsync("2.Bc4");
        await Expect(current).ToHaveTextAsync("2.Bc4");
        await Page.PlayAsync("g8f6");
        await Expect(Page.GetByTestId("variation")).ToHaveTextAsync("2.Bc4 Nf6");

        // Right-click → Promote to main line
        await Page.GetByTestId("move").Filter(new() { HasText = "Nf6" }).ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByTestId("menu-promote").ClickAsync();
        await Expect(Page.GetByTestId("variation")).ToHaveTextAsync("2.Nf3 Nc6");

        // Right-click → Delete Variation
        await Page.GetByTestId("move").Filter(new() { HasText = "2.Nf3" }).ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByTestId("menu-delete-variation").ClickAsync();
        await Expect(Page.GetByTestId("variation")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("move")).ToHaveTextAsync(["e4", "e5", "Bc4", "Nf6"]);
    }

    [Fact]
    public async Task Fill_in_game_details_and_tags_and_see_event_and_eco_in_the_list()
    {
        var gameId = await _app.CreateGameAsync("e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");
        await Expect(Page.GetByTestId("opening")).ToContainTextAsync("B90");

        await Page.GetByTestId("event-input").FillAsync("Vienna Open");
        await Page.GetByTestId("event-input").PressAsync("Enter");
        await Page.GetByTestId("round-input").FillAsync("5");
        await Page.GetByTestId("round-input").PressAsync("Tab");
        await Expect(Page.GetByTestId("sheet-meta")).ToContainTextAsync("Vienna Open · Round 5");

        await Page.GetByTestId("tag-input").FillAsync("open tournament");
        await Page.GetByTestId("tag-input").PressAsync("Enter");
        await Expect(Page.GetByTestId("tag")).ToHaveTextAsync([new System.Text.RegularExpressions.Regex("open tournament")]);

        await Page.GetByTestId("tab-games").ClickAsync();
        await Expect(Page.GetByTestId("game-event")).ToHaveTextAsync("Vienna Open");
        await Expect(Page.GetByTestId("game-eco")).ToHaveTextAsync("B90");
    }

    [Fact]
    public async Task Import_pasted_pgn_and_open_the_imported_game()
    {
        await _app.SetOwnerNameAsync("Smoke Owner");
        const string pgn = """
            [White "Owner, Smoke"]
            [Black "Huber, Thomas"]
            [Result "1-0"]

            1. e4 e5 2. Sf3 {Knight out} Sc6 (2... d6 3. d4) 3. Lb5 1-0

            [White "Owner, Smoke"]
            [Black "Broken, Bob"]
            [Result "1-0"]

            1. e4 e5 2. Kxe8 1-0
            """;
        await Page.GotoAsync($"{_app.BaseUrl}/import");

        await Page.GetByTestId("pgn-text").FillAsync(pgn);
        await Page.GetByTestId("import-pasted").ClickAsync();

        await Expect(Page.GetByTestId("count-imported")).ToHaveTextAsync("1");
        await Expect(Page.GetByTestId("count-failed")).ToHaveTextAsync("1");
        await Expect(Page.GetByTestId("import-row").Nth(1)).ToContainTextAsync("Kxe8");

        await Page.GetByTestId("import-row").First.GetByRole(AriaRole.Link, new() { Name = "Open" }).ClickAsync();
        await Expect(Page.GetByTestId("comment")).ToHaveTextAsync("Knight out");
        await Expect(Page.GetByTestId("variation")).ToHaveTextAsync("2...d6 3.d4");
        await Expect(Page.GetByTestId("move").Last).ToHaveTextAsync("Bb5");
    }

    [Fact]
    public async Task Resolve_held_back_games_in_the_review_list()
    {
        await _app.SetOwnerNameAsync("Smoke Owner");
        await _app.CreateGameAsync("e4");  // creates the Player "Fixture Opponent"
        const string pgn = """
            [White "Kasparow, Garri"]
            [Black "Fixture Opponent"]
            [Result "1-0"]

            1. d4 1-0

            [White "Owner, Smoke"]
            [Black "Opponent F."]
            [Result "0-1"]

            1. c4 0-1
            """;
        await Page.GotoAsync($"{_app.BaseUrl}/import");
        await Page.GetByTestId("pgn-text").FillAsync(pgn);
        await Page.GetByTestId("import-pasted").ClickAsync();
        await Expect(Page.GetByTestId("count-review")).ToHaveTextAsync("2");

        await Page.GetByTestId("review-link").ClickAsync();
        var items = Page.GetByTestId("review-item");
        await Expect(items).ToHaveCountAsync(2);

        // No side matched: I was White
        await items.First.GetByTestId("review-white").ClickAsync();
        await items.First.GetByTestId("review-resolve").ClickAsync();
        await Expect(items).ToHaveCountAsync(1);

        // "Opponent F." resembles "Fixture Opponent": confirm it is the same Player
        await Expect(items.First).ToContainTextAsync("Fixture Opponent");
        await Expect(items.First.GetByTestId("review-resolve")).ToBeDisabledAsync();
        await items.First.GetByTestId("review-same-player").ClickAsync();
        await items.First.GetByTestId("review-resolve").ClickAsync();
        await Expect(Page.GetByTestId("review-empty")).ToBeVisibleAsync();

        await Page.GetByTestId("tab-games").ClickAsync();
        await Expect(Page.GetByTestId("game-opponent")).ToHaveTextAsync(["Fixture Opponent", "Fixture Opponent", "Fixture Opponent"]);
    }

    [Fact]
    public async Task Export_a_game_to_a_pgn_file()
    {
        await _app.SetOwnerNameAsync("Smoke Owner");
        var gameId = await _app.CreateGameAsync("e4", "e5", "Nf3");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.GetByTestId("export-game").ClickAsync());

        Assert.EndsWith(".pgn", download.SuggestedFilename);
        var pgn = await File.ReadAllTextAsync((await download.PathAsync())!);
        Assert.Contains("[White \"Smoke Owner\"]", pgn);
        Assert.Contains("1. e4 e5 2. Nf3 1-0", pgn);
    }

    [Fact]
    public async Task Filter_and_sort_the_game_list_then_export_what_is_shown()
    {
        await _app.SetOwnerNameAsync("Smoke Owner");
        await _app.ImportAsync("""
            [Date "2025.01.10"]
            [White "Owner, Smoke"]
            [Black "Zeller, Zoe"]
            [Result "0-1"]

            1. e4 0-1

            [Date "2025.02.10"]
            [White "Adler, Anton"]
            [Black "Owner, Smoke"]
            [Result "1-0"]

            1. d4 1-0

            [Date "2025.03.10"]
            [White "Berger, Anna"]
            [Black "Owner, Smoke"]
            [Result "1-0"]

            1. c4 1-0
            """);
        await Page.GotoAsync(_app.BaseUrl);
        await Expect(Page.GetByTestId("game-row")).ToHaveCountAsync(3);

        await Page.GetByTestId("filter-colour").SelectOptionAsync("black");
        await Page.GetByTestId("filter-result").SelectOptionAsync("loss");
        await Expect(Page.GetByTestId("game-opponent")).ToHaveTextAsync(["Berger, Anna", "Adler, Anton"]);

        await Page.GetByTestId("sort-opponent").ClickAsync();
        await Expect(Page.GetByTestId("game-opponent")).ToHaveTextAsync(["Adler, Anton", "Berger, Anna"]);
        await Page.GetByTestId("sort-opponent").ClickAsync();
        await Expect(Page.GetByTestId("game-opponent")).ToHaveTextAsync(["Berger, Anna", "Adler, Anton"]);

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.GetByTestId("export-all").ClickAsync());
        var pgn = await File.ReadAllTextAsync((await download.PathAsync())!);
        Assert.Equal(2, pgn.Split("[Event ").Length - 1);
        Assert.DoesNotContain("Zeller", pgn);

        // Opening a Game and coming back keeps the filters
        await Page.GetByTestId("game-row").First.ClickAsync();
        await Expect(Page.GetByTestId("board")).ToBeVisibleAsync();
        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("game-row")).ToHaveCountAsync(2);

        await Page.GetByTestId("clear-filters").ClickAsync();
        await Expect(Page.GetByTestId("game-row")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task Annotate_a_move_with_a_symbol_and_a_comment()
    {
        var gameId = await _app.CreateGameAsync("e4", "e5", "Qh5", "Nc6");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");

        await Page.GetByTestId("move").Filter(new() { HasText = "Qh5" }).ClickAsync();
        await Page.GetByTestId("nag-6").ClickAsync();
        await Expect(Page.GetByTestId("move").Filter(new() { HasText = "Qh5" })).ToHaveTextAsync("Qh5?!");

        await Page.GetByTestId("comment-input").FillAsync("Too early with the queen.");
        await Expect(Page.GetByTestId("comment-status")).ToHaveTextAsync("Saved");
        await Expect(Page.GetByTestId("comment")).ToHaveTextAsync("Too early with the queen.");

        // Persisted: reload and find both again
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("move").Filter(new() { HasText = "Qh5" })).ToHaveTextAsync("Qh5?!");
        await Expect(Page.GetByTestId("comment")).ToHaveTextAsync("Too early with the queen.");
    }
}
