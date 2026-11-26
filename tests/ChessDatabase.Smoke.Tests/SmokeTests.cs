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

        await Page.GetByTestId("tree-move").First.ClickAsync(); // plays 1.e4 on the board
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("e4");
        await Expect(Page.GetByTestId("tree-move")).ToHaveTextAsync(["c5", "e5"]);

        // Both Games reached 1.e4; the newest (the second) is listed first. Opening it lands on 1.e4.
        await Expect(Page.GetByTestId("tree-game")).ToHaveCountAsync(2);
        await Page.GetByTestId("tree-game").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($@"/games/{second}\?ply=1$"));
        await Expect(Page.Locator("button.move.current")).ToHaveTextAsync("e4");
        await Expect(Page.GetByTestId("move")).ToHaveTextAsync(["e4", "c5"]);
    }

    private static string Key(params string[] san)
    {
        var position = ChessDatabase.Rules.Position.Initial;
        foreach (var move in san) position = position.Play(position.ParseSan(move));
        return string.Join(' ', position.ToFen().Split(' ').Take(4));
    }
}
