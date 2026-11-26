using System.Net.Http.Json;
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

    [Fact]
    public async Task Back_up_the_collection_from_settings()
    {
        await Page.GotoAsync($"{_app.BaseUrl}/settings");

        await Page.GetByTestId("back-up-now").ClickAsync();

        await Expect(Page.GetByTestId("backup-status")).ToHaveTextAsync("Backed up");
        await Expect(Page.GetByTestId("last-backup")).Not.ToHaveTextAsync("none yet");
    }

    [Fact]
    public async Task Switch_the_engine_on_follow_the_moves_and_play_a_line_as_a_variation()
    {
        var gameId = await _app.CreateGameAsync("e4", "e5");
        const string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq -";
        const string afterE5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq -";
        await _app.UseFakeEngineAsync(new()
        {
            [afterE4] = (["depth 12 multipv 1 score cp -25 pv c7c5 g1f3", "depth 12 multipv 2 score cp -30 pv e7e5 g1f3",
                "depth 12 multipv 3 score cp -40 pv e7e6 d2d4"], "c7c5"),
            [afterE5] = (["depth 14 multipv 1 score cp 35 pv g1f3 b8c6", "depth 14 multipv 2 score cp 30 pv f1c4",
                "depth 14 multipv 3 score cp 20 pv b1c3"], "g1f3"),
        });
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");

        await Page.GetByTestId("engine-toggle").ClickAsync();
        await Expect(Page.GetByTestId("engine-line")).ToHaveCountAsync(3);
        await Expect(Page.GetByTestId("engine-line").First).ToContainTextAsync("+0.35");
        await Expect(Page.GetByTestId("engine-line").First).ToContainTextAsync("2.Nf3 Nc6");
        await Expect(Page.GetByTestId("engine-depth")).ToHaveTextAsync("depth 14");

        // The Engine follows the board.
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(Page.GetByTestId("engine-line").First).ToContainTextAsync("1...c5 2.Nf3");
        await Expect(Page.GetByTestId("engine-eval")).ToHaveTextAsync("+0.25");

        // Clicking a line adds it as a Variation at the board position.
        await Page.GetByTestId("engine-line").First.ClickAsync();
        await Expect(Page.Locator(".variation")).ToContainTextAsync("1...c5 2.Nf3");

        await Page.GetByTestId("engine-toggle").ClickAsync();
        await Expect(Page.GetByTestId("engine-line")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Set_the_engine_path_in_settings()
    {
        await Page.GotoAsync($"{_app.BaseUrl}/settings");

        await Page.GetByTestId("engine-path").FillAsync(@"C:
owhere\stockfish.exe");
        await Page.GetByTestId("engine-save").ClickAsync();
        await Expect(Page.GetByTestId("engine-status")).ToContainTextAsync("There is no file at");

        await Page.GetByTestId("engine-path").FillAsync(Path.Combine(AppContext.BaseDirectory, "FakeEngine.exe"));
        await Page.GetByTestId("engine-save").ClickAsync();
        await Expect(Page.GetByTestId("engine-status")).ToHaveTextAsync("Saved. Found Fake Engine 1.0.");
    }

    [Fact]
    public async Task Run_game_analysis_and_jump_to_the_blunder()
    {
        string[] moves = ["e4", "e5", "Qh5", "Nc6", "Bc4", "Nf6", "Qxf7#"];
        var gameId = await _app.CreateGameAsync(moves);
        await _app.UseFakeEngineAsync(new()
        {
            [Key()] = (["depth 20 multipv 1 score cp 30 pv e2e4 e7e5"], "e2e4"),
            [Key(moves[..1])] = (["depth 20 multipv 1 score cp -30 pv e7e5 g1f3"], "e7e5"),
            [Key(moves[..2])] = (["depth 20 multipv 1 score cp 30 pv g1f3 b8c6"], "g1f3"),
            [Key(moves[..3])] = (["depth 20 multipv 1 score cp 40 pv b8c6 f1c4"], "b8c6"),
            [Key(moves[..4])] = (["depth 20 multipv 1 score cp -40 pv f1c4 g7g6"], "f1c4"),
            [Key(moves[..5])] = (["depth 20 multipv 1 score cp 30 pv g7g6 h5f3 g8f6"], "g7g6"),
            [Key(moves[..6])] = (["depth 20 multipv 1 score mate 1 pv h5f7"], "h5f7"),
        });
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");

        await Page.GetByTestId("analyse-game").ClickAsync();

        await Expect(Page.GetByTestId("accuracy-white")).ToHaveTextAsync("88.0%");
        await Expect(Page.GetByTestId("accuracy-black")).ToHaveTextAsync("20.0%");
        await Expect(Page.GetByTestId("classified-move")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("classified-move").Nth(1)).ToContainTextAsync("3...Nf6");
        await Expect(Page.GetByTestId("classified-move").Nth(1)).ToContainTextAsync("blunder");
        await Expect(Page.GetByTestId("best-move").Nth(1)).ToHaveTextAsync("3...g6");
        await Expect(Page.Locator("[data-testid=move][data-mark=blunder]")).ToHaveTextAsync("Nf6");

        await Page.GetByTestId("classified-move").Nth(1).ClickAsync();
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("Nf6");
        await Expect(Page.GetByTestId("best-line")).ToContainTextAsync("3...g6 4.Qf3 Nf6");

        // Clicking the start of the graph goes back to the starting position.
        var graph = await Page.GetByTestId("eval-graph").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(graph!.X + 2, graph.Y + graph.Height / 2);
        await Expect(Page.Locator("button.move.current")).ToHaveCountAsync(0);

        // Reopening shows the saved Engine Assessment without analysing again.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("accuracy-white")).ToHaveTextAsync("88.0%");
        await Expect(Page.GetByTestId("analyse-game")).ToHaveTextAsync("Analyse again");
        await Expect(Page.GetByTestId("move").Filter(new() { HasText = "Nf6" })).ToHaveTextAsync("Nf6"); // no symbol yet

        // Copying mistakes and blunders writes the symbol and the Engine line into the Game.
        await Page.GetByTestId("copy-all").ClickAsync();
        await Expect(Page.GetByTestId("copy-status")).ToHaveTextAsync("Copied 1 move into the Game.");
        await Expect(Page.GetByTestId("move").Filter(new() { HasText = "Nf6??" })).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("variation")).ToContainTextAsync("3...g6 4.Qf3 Nf6");
    }

    [Fact]
    public async Task Queue_several_games_for_analysis_from_the_game_list()
    {
        await _app.CreateGameAsync("e4", "e5");
        await _app.CreateGameAsync("d4", "d5");
        await _app.CreateGameAsync("c4", "c5");
        await _app.UseFakeEngineAsync([], lineDelayMs: 150);
        await Page.GotoAsync(_app.BaseUrl);
        await Expect(Page.GetByTestId("game-row")).ToHaveCountAsync(3);

        await Page.GetByTestId("select-game").Nth(0).CheckAsync();
        await Page.GetByTestId("select-game").Nth(1).CheckAsync();
        await Page.GetByTestId("analyse-selected").ClickAsync();

        await Expect(Page.GetByTestId("analysis-strip")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("strip-game")).ToContainTextAsync("of 2");
        await Expect(Page.Locator(".analysed-mark")).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(Page.GetByTestId("analysis-strip")).ToBeHiddenAsync();

        await Page.GetByTestId("filter-analysed").SelectOptionAsync("false");
        await Expect(Page.GetByTestId("game-row")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Walk_the_opening_tree_and_open_another_game_at_the_shared_position()
    {
        var first = await _app.CreateGameAsync("e4", "e5", "Nf3");
        var second = await _app.CreateGameAsync("e4", "c5");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{first}?ply=0");

        await Expect(Page.GetByTestId("tree-move")).ToHaveTextAsync(["e4"]);
        await Expect(Page.GetByTestId("tree-games")).ToHaveTextAsync(["2"]);

        await Page.GetByTestId("tree-move").First.ClickAsync(); // the Game has 1.e4: it steps into it
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("e4");
        await Expect(Page.GetByTestId("tree-move")).ToHaveTextAsync(["c5", "e5"]);

        // 1...c5 is not in this Game: the board shows it, nothing is saved.
        await Page.GetByTestId("tree-move").Filter(new() { HasText = "c5" }).ClickAsync();
        await Expect(Page.GetByTestId("explore-line")).ToHaveTextAsync("1...c5");
        await Expect(Page.GetByTestId("variation")).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("ArrowLeft"); // back out of the exploration
        await Expect(Page.GetByTestId("explore-bar")).ToHaveCountAsync(0);
        await Page.GetByTestId("tree-move").Filter(new() { HasText = "c5" }).ClickAsync();
        await Page.GetByTestId("explore-add").ClickAsync(); // now it becomes a Variation
        await Expect(Page.GetByTestId("variation")).ToContainTextAsync("1...c5");
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("1...c5");
        await Page.GetByTestId("move").Filter(new() { HasText = "e4" }).ClickAsync();

        // Both Games reached 1.e4; the newest (the second) is listed first. Opening it lands on 1.e4.
        await Expect(Page.GetByTestId("tree-game")).ToHaveCountAsync(2);
        await Page.GetByTestId("tree-game").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($@"/games/{second}\?ply=1$"));
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("e4");
        await Expect(Page.GetByTestId("move")).ToHaveTextAsync(["e4", "c5"]);
    }

    [Fact]
    public async Task Search_the_board_position_from_a_game_and_open_a_transposed_game_there()
    {
        var first = await _app.CreateGameAsync("e4", "e5", "Nf3", "Nc6", "Bb5");
        var second = await _app.CreateGameAsync("Nf3", "Nc6", "e4", "e5", "d4");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{first}?ply=4");

        await Page.GetByTestId("search-this-position").ClickAsync();

        await Expect(Page.GetByTestId("search-result")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("search-move")).ToHaveTextAsync(["3", "3"]);
        await Page.GetByTestId("search-result").First.ClickAsync(); // newest first: the second Game
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($@"/games/{second}\?ply=4$"));
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("e5");
    }

    [Fact]
    public async Task Set_up_a_position_in_the_editor_and_search_it()
    {
        await _app.CreateGameAsync("e4", "e5");
        await Page.GotoAsync($"{_app.BaseUrl}/search");

        await Page.GetByTestId("editor-start").ClickAsync();
        await Page.GetByTestId("search-position").ClickAsync();
        await Expect(Page.GetByTestId("search-result")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("search-move")).ToHaveTextAsync("1");

        // Two kings and a pawn on the first rank: refused with the reason.
        await Page.GetByTestId("editor-clear").ClickAsync();
        await PlaceAsync("palette-white-king", "e1");
        await PlaceAsync("palette-black-king", "e8");
        await PlaceAsync("palette-white-pawn", "a1");
        await Page.GetByTestId("search-position").ClickAsync();
        await Expect(Page.GetByTestId("search-error")).ToContainTextAsync("Pawns can't stand on the first or the last rank.");

        // Erase the pawn: a legal position nobody reached.
        await PlaceAsync("palette-erase", "a1");
        await Page.GetByTestId("search-position").ClickAsync();
        await Expect(Page.GetByTestId("search-empty")).ToBeVisibleAsync();
    }

    /// <summary>Chooses a palette tool and clicks a square on the editor board (White at the bottom).</summary>
    private async Task PlaceAsync(string tool, string square)
    {
        var button = Page.GetByTestId(tool);
        if (await button.GetAttributeAsync("class") is not { } css || !css.Split(' ').Contains("on")) await button.ClickAsync();
        var box = (await Page.GetByTestId("editor-board").BoundingBoxAsync())!;
        var file = square[0] - 'a';
        var rank = square[1] - '1';
        await Page.Mouse.ClickAsync(box.X + (file + 0.5f) * box.Width / 8, box.Y + (7 - rank + 0.5f) * box.Height / 8);
    }

    [Fact]
    public async Task See_what_masters_play_and_replay_a_master_game_read_only()
    {
        _app.MastersJson = fen => fen.StartsWith("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w") ? """
            {"white":958331,"draws":1346565,"black":692516,
             "moves":[{"uci":"e2e4","san":"e4","averageRating":2390,"white":431551,"draws":610338,"black":326236,"game":null},
                      {"uci":"d2d4","san":"d4","averageRating":2405,"white":342556,"draws":488326,"black":239633,"game":null}],
             "topGames":[{"uci":"e2e4","id":"dfYKaW8J","winner":null,"white":{"name":"Carlsen, M.","rating":2839},
                          "black":{"name":"Nakamura, Hi","rating":2816},"year":2025,"month":"2025-10"}],"opening":null}
            """ : null;
        _app.MasterPgns["dfYKaW8J"] = """
            [Event "Clutch Chess: Champions"]
            [White "Carlsen, M."]
            [Black "Nakamura, Hi"]
            [Result "1/2-1/2"]

            1. e4 c5 2. Nf3 g6 1/2-1/2
            """;
        var gameId = await _app.CreateGameAsync();
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");

        await Page.GetByTestId("tree-source-masters").ClickAsync();
        await Expect(Page.GetByTestId("masters-move")).ToHaveTextAsync(["e4", "d4"]);
        await Expect(Page.GetByTestId("masters-percent").First).ToHaveTextAsync("32 · 45 · 24");

        // A Master Game opens read-only and is not added to the Collection.
        await Page.GetByTestId("master-game").ClickAsync();
        await Expect(Page.GetByTestId("master-white")).ToHaveTextAsync("Carlsen, M.");
        await Expect(Page.GetByTestId("master-move")).ToHaveTextAsync(["e4", "c5", "Nf3", "g6"]);
        await Page.GotoAsync(_app.BaseUrl);
        await Expect(Page.GetByTestId("game-row")).ToHaveCountAsync(1);

        // A position Lichess can't answer: the tab says so, the rest of the view works on.
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");
        await Page.GetByTestId("tree-source-masters").ClickAsync();
        await Page.GetByTestId("masters-move").First.ClickAsync(); // explores 1.e4, nothing saved
        await Expect(Page.GetByTestId("masters-unavailable")).ToContainTextAsync("Masters unavailable");
        await Expect(Page.GetByTestId("explore-line")).ToHaveTextAsync("1.e4");
        await Page.GetByTestId("tree-source-mine").ClickAsync();
        await Expect(Page.GetByTestId("tree-count")).ToHaveTextAsync("0 Games");
    }

    [Fact]
    public async Task An_endgame_shows_the_tablebase_verdict_and_ranked_moves()
    {
        _app.TablebaseJson = _ => """
            {"dtz":1,"dtm":17,"category":"win","moves":[
              {"uci":"h7h8q","category":"loss","dtz":-2,"dtm":-16,"zeroing":true},
              {"uci":"g7g8","category":"draw","dtz":0,"dtm":0,"zeroing":false}]}
            """;
        var gameId = await _app.CreateGameAsync("e4");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}");
        await Expect(Page.GetByTestId("engine-toggle")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("tablebase")).ToHaveCountAsync(0); // 32 pieces: nothing to look up

        await Page.GotoAsync($"{_app.BaseUrl}/search?fen={Uri.EscapeDataString("4k3/6KP/8/8/8/8/7p/8 w - - 0 1")}");
        await Expect(Page.GetByTestId("tablebase-verdict")).ToHaveTextAsync("White wins");
        await Expect(Page.GetByTestId("tablebase-move")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("tablebase-move").First).ToContainTextAsync("h8=Q+");
        await Expect(Page.GetByTestId("tablebase-move").First).ToContainTextAsync("Win");

        // Lichess unreachable for the next position: said quietly, the page carries on.
        _app.TablebaseJson = _ => null;
        await Page.GetByTestId("tablebase-move").First.ClickAsync();
        await Expect(Page.GetByTestId("tablebase-unavailable")).ToContainTextAsync("Tablebase unavailable");
        await Page.GetByTestId("search-position").ClickAsync();
        await Expect(Page.GetByTestId("search-empty")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Open_a_players_dossier_from_the_list_and_record_a_club()
    {
        var first = await _app.CreateGameAsync("e4", "e5");
        var second = await _app.CreateGameAsync("d4", "d5");
        using (var http = new HttpClient { BaseAddress = new Uri(_app.BaseUrl) })
        {
            foreach (var (id, date, elo) in new[] { (first, "2024-01-10", 1850), (second, "2024-05-02", 1910) })
            {
                var game = await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/games/{id}");
                (await http.PutAsJsonAsync($"/api/games/{id}", new
                {
                    ownerColour = "white", opponentId = game.GetProperty("opponent").GetProperty("id").GetInt32(),
                    result = "1-0", date, blackElo = elo,
                })).EnsureSuccessStatusCode();
            }
        }

        await Page.GotoAsync(_app.BaseUrl);
        await Page.GetByTestId("game-opponent").First.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(Page.GetByTestId("dossier-name")).ToHaveTextAsync("Fixture Opponent");
        await Expect(Page.GetByTestId("dossier-score")).ToHaveTextAsync("100%");
        await Expect(Page.GetByTestId("dossier-game")).ToHaveCountAsync(2);
        await Expect(Page.Locator("[data-testid=time-chart] circle")).ToHaveCountAsync(2);

        await Page.GetByTestId("player-club").FillAsync("SK Neustadt");
        await Page.GetByTestId("player-club").PressAsync("Tab");
        await Expect(Page.GetByTestId("player-status")).ToHaveTextAsync("Saved");

        await Page.GetByTestId("tab-players").ClickAsync();
        await Expect(Page.GetByTestId("player-row")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("player-row")).ToContainTextAsync("SK Neustadt");
        await Page.GetByTestId("player-search").FillAsync("nobody");
        await Expect(Page.GetByTestId("player-row")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Merge_two_players_after_confirming_which_name_to_keep()
    {
        await _app.CreateGameAsync("e4"); // against "Fixture Opponent"
        using (var http = new HttpClient { BaseAddress = new Uri(_app.BaseUrl) })
        {
            var player = await (await http.PostAsJsonAsync("/api/players", new { name = "Opponent, Fixture" }))
                .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            (await http.PostAsJsonAsync("/api/games", new
            {
                ownerColour = "black", opponentId = player.GetProperty("id").GetInt32(), result = "1/2-1/2",
            })).EnsureSuccessStatusCode();
        }
        await Page.GotoAsync($"{_app.BaseUrl}/players");
        await Expect(Page.GetByTestId("player-row")).ToHaveCountAsync(2);

        await Page.GetByTestId("player-row").Filter(new() { HasText = "Fixture Opponent" }).GetByTestId("player-merge").ClickAsync();
        await Page.GetByTestId("merge-search").FillAsync("Opponent, F");
        await Page.GetByTestId("merge-candidate").ClickAsync();
        await Page.GetByTestId("merge-name").Filter(new() { HasText = "Fixture Opponent" }).ClickAsync();
        await Page.GetByTestId("merge-confirm").ClickAsync();

        await Expect(Page.GetByTestId("player-status")).ToHaveTextAsync("Merged");
        await Expect(Page.GetByTestId("dossier-game")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("dossier-score")).ToHaveTextAsync("75%");
        await Page.GetByTestId("tab-players").ClickAsync();
        await Expect(Page.GetByTestId("player-row")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task See_my_statistics_and_limit_them_by_time_control()
    {
        var rapid = await _app.CreateGameAsync("e4", "e5");
        var classical = await _app.CreateGameAsync("d4", "d5");
        using (var http = new HttpClient { BaseAddress = new Uri(_app.BaseUrl) })
        {
            foreach (var (id, date, mine, theirs, tc, result) in new[]
                     { (rapid, "2024-01-10", 1990, 1850, "15+10", "0-1"), (classical, "2024-05-02", 2010, 1910, "90+30", "1-0") })
            {
                var game = await http.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/games/{id}");
                (await http.PutAsJsonAsync($"/api/games/{id}", new
                {
                    ownerColour = "white", opponentId = game.GetProperty("opponent").GetProperty("id").GetInt32(),
                    result, date, whiteElo = mine, blackElo = theirs, timeControl = tc,
                })).EnsureSuccessStatusCode();
            }
        }

        await Page.GotoAsync(_app.BaseUrl);
        await Page.GetByTestId("tab-statistics").ClickAsync();
        await Expect(Page.GetByTestId("stats-Overall")).ToHaveTextAsync("50%");
        await Expect(Page.GetByTestId("stats-opponent-elo")).ToHaveTextAsync("1880");
        await Expect(Page.Locator("app-time-chart").First.Locator("circle")).ToHaveCountAsync(2);

        await Page.GetByTestId("stats-time-control").FillAsync("90+30");
        await Page.GetByTestId("stats-time-control").PressAsync("Enter");
        await Page.GetByTestId("stats-time-control").PressAsync("Tab");
        await Expect(Page.GetByTestId("stats-Overall")).ToHaveTextAsync("100%");
        await Expect(Page.GetByTestId("stats-opponent-elo")).ToHaveTextAsync("1910");
        await Expect(Page.GetByTestId("stats-accuracy")).ToContainTextAsync("No analysed Games");
    }

    [Fact]
    public async Task Build_a_repertoire_from_a_game_and_on_the_board()
    {
        var gameId = await _app.CreateGameAsync("e4", "e5", "Nf3", "Nc6");
        await Page.GotoAsync($"{_app.BaseUrl}/games/{gameId}?ply=3"); // after 2.Nf3

        await Page.GetByTestId("add-to-repertoire").ClickAsync();
        await Expect(Page.GetByTestId("repertoire-message")).ToHaveTextAsync("Added to your White Repertoire.");

        await Page.GetByTestId("tab-repertoire").ClickAsync();
        await Expect(Page.GetByTestId("move")).ToHaveTextAsync(["e4", "e5", "Nf3"]);

        // A different answer to 1.e4 becomes a Variation; the comment saves.
        await Page.GetByTestId("move").Filter(new() { HasText = "e4" }).ClickAsync();
        await Page.PlayAsync("c7c5");
        await Expect(Page.GetByTestId("variation")).ToContainTextAsync("1...c5");
        await Page.GetByTestId("repertoire-comment").FillAsync("Meet the Sicilian with 2.Nf3.");
        await Expect(Page.GetByTestId("repertoire-comment-status")).ToHaveTextAsync("Saved");

        await Page.GetByTestId("repertoire-black").ClickAsync();
        await Expect(Page.GetByTestId("move")).ToHaveCountAsync(0);
        await Page.GetByTestId("repertoire-white").ClickAsync();
        await Expect(Page.GetByTestId("move")).ToHaveCountAsync(4);
    }

    private static string Key(params string[] san)
    {
        var position = ChessDatabase.Rules.Position.Initial;
        foreach (var move in san) position = position.Play(position.ParseSan(move));
        return string.Join(' ', position.ToFen().Split(' ').Take(4));
    }
}
