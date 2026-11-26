using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class MoveEntryTests
{
    [Fact]
    public async Task Moves_played_one_after_another_form_the_main_line()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var last = await client.PlayMainLineAsync(game.Id, "e2e4", "e7e5", "g1f3", "b8c6", "f1b5");

        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5"], last.MainLine().Select(n => n.San));
        Assert.Equal("r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3", last.MainLine().Last().Fen);
        Assert.Equal([1, 2, 3, 4, 5], last.MainLine().Select(n => n.Ply));
    }

    [Fact]
    public async Task Moves_may_be_sent_as_san_including_german_piece_letters()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var last = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Sf3");

        Assert.Equal("Nf3", last.MainLine().Last().San);
        Assert.Equal("g1f3", last.MainLine().Last().Uci);
    }

    [Fact]
    public async Task An_illegal_move_is_refused_and_the_game_is_unchanged()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var afterE4 = await client.PlayMainLineAsync(game.Id, "e2e4");
        var e4 = afterE4.MainLine().Single();

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/moves", new { parentId = e4.Id, move = "e2e4" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["e4"], (await client.GetGameAsync(game.Id)).MainLine().Select(n => n.San));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("Ke2")]
    public async Task Unreadable_or_illegal_first_moves_are_refused(string move)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/moves", new { parentId = (int?)null, move });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetGameAsync(game.Id)).Nodes);
    }

    [Fact]
    public async Task A_move_after_a_node_of_another_game_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "A");
        var second = await client.CreateGameAsync(opponent: "B");
        var node = (await client.PlayMainLineAsync(first.Id, "e2e4")).MainLine().Single();

        var response = await client.PostAsJsonAsync($"/api/games/{second.Id}/moves", new { parentId = node.Id, move = "e7e5" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Moves_persist_and_read_back_unchanged()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        await client.PlayMainLineAsync(game.Id, "d2d4", "d7d5", "c2c4");

        var reloaded = await factory.CreateClient().GetGameAsync(game.Id);

        Assert.Equal(["d4", "d5", "c4"], reloaded.MainLine().Select(n => n.San));
    }
}
