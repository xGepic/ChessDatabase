using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class GameEntryTests
{
    [Fact]
    public async Task A_created_game_appears_in_the_game_list_with_the_owner_on_their_side()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");

        var created = await client.CreateGameAsync(
            ownerColor: "black", opponent: "Huber, Thomas", whiteElo: 1850, blackElo: 1720, result: "0-1", date: "2026-11-20");

        var game = Assert.Single(await client.ListGamesAsync());
        Assert.Equal(created.Id, game.Id);
        Assert.Equal("2026-11-20", game.Date);
        Assert.Equal("black", game.OwnerColor);
        Assert.Equal("Huber, Thomas", game.Opponent.Name);
        Assert.Equal("Huber, Thomas", game.White);
        Assert.Equal("Max Mustermann", game.Black);
        Assert.Equal(1850, game.WhiteElo);
        Assert.Equal(1720, game.BlackElo);
        Assert.Equal("0-1", game.Result);
    }

    [Fact]
    public async Task Changing_the_owner_name_changes_the_owner_side_of_existing_games()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max");
        await client.CreateGameAsync(ownerColor: "white");

        await client.SetOwnerNameAsync("Max Mustermann");

        Assert.Equal("Max Mustermann", Assert.Single(await client.ListGamesAsync()).White);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(null, null)]
    [InlineData(0, null)]
    public async Task Elo_zero_or_empty_reads_back_as_unrated_for_either_side(int? whiteElo, int? blackElo)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var created = await client.CreateGameAsync(whiteElo: whiteElo, blackElo: blackElo);

        var game = await client.GetGameAsync(created.Id);
        Assert.Null(game.WhiteElo);
        Assert.Null(game.BlackElo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("2-0")]
    public async Task A_game_without_a_result_is_refused(string? result)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var opponent = await client.CreatePlayerAsync("Huber, Thomas");

        var response = await client.PostAsJsonAsync("/api/games",
            new { ownerColor = "white", opponentId = opponent.Id, result });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await client.ListGamesAsync());
    }

    [Fact]
    public async Task Removing_the_result_of_a_saved_game_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync(result: "1/2-1/2");

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}",
            new { ownerColor = "white", opponentId = game.Opponent.Id, result = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("1/2-1/2", (await client.GetGameAsync(game.Id)).Result);
    }

    [Fact]
    public async Task Game_details_save_immediately()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync(ownerColor: "white", result: "1-0");
        var other = await client.CreatePlayerAsync("Berger, Anna");

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}",
            new { ownerColor = "black", opponentId = other.Id, whiteElo = 2010, blackElo = 0, result = "0-1", date = "2026-11" });

        response.EnsureSuccessStatusCode();
        var saved = await client.GetGameAsync(game.Id);
        Assert.Equal("black", saved.OwnerColor);
        Assert.Equal("Berger, Anna", saved.Opponent.Name);
        Assert.Equal(2010, saved.WhiteElo);
        Assert.Null(saved.BlackElo);
        Assert.Equal("0-1", saved.Result);
        Assert.Equal("2026-11", saved.Date);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(5000)]
    public async Task An_impossible_elo_is_refused(int elo)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var opponent = await client.CreatePlayerAsync("Huber, Thomas");

        var response = await client.PostAsJsonAsync("/api/games",
            new { ownerColor = "white", opponentId = opponent.Id, whiteElo = elo, result = "1-0" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("20.11.2026")]
    [InlineData("2026-13")]
    public async Task An_unreadable_date_is_refused(string date)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var opponent = await client.CreatePlayerAsync("Huber, Thomas");

        var response = await client.PostAsJsonAsync("/api/games",
            new { ownerColor = "white", opponentId = opponent.Id, result = "1-0", date });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_game_needs_an_existing_opponent()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/games",
            new { ownerColor = "white", opponentId = 999, result = "1-0" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_deleted_game_is_gone()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var response = await client.DeleteAsync($"/api/games/{game.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await client.ListGamesAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/games/{game.Id}")).StatusCode);
    }
}
