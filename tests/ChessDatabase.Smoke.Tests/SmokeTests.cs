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
}
