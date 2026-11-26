using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class VariationTests
{
    [Fact]
    public async Task Playing_a_different_move_creates_a_variation_and_leaves_the_main_line_intact()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3", "Nc6");
        var e5 = played.MainLine()[1];

        var (nodeId, after) = await client.PlayAsync(game.Id, e5.Id, "Bc4");

        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], after.MainLine().Select(n => n.San));
        Assert.Equal(["Nf3", "Bc4"], after.Node(e5.Id).Children.Select(id => after.Node(id).San));
        Assert.Equal(nodeId, after.Node(e5.Id).Children[1]);
    }

    [Fact]
    public async Task A_variation_can_start_on_the_first_move()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        await client.PlayMainLineAsync(game.Id, "e4", "e5");

        var (_, after) = await client.PlayAsync(game.Id, null, "d4");

        Assert.Equal(["e4", "d4"], after.FirstMoves.Select(id => after.Node(id).San));
        Assert.Equal(["e4", "e5"], after.MainLine().Select(n => n.San));
    }

    [Fact]
    public async Task Playing_the_recorded_move_again_follows_it_without_a_duplicate()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5");

        var (nodeId, after) = await client.PlayAsync(game.Id, played.MainLine()[0].Id, "e5");

        Assert.Equal(played.MainLine()[1].Id, nodeId);
        Assert.Equal(2, after.Nodes.Count);
    }

    [Fact]
    public async Task Variations_nest_inside_variations_to_any_depth()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3");

        // 2.Bc4 instead of 2.Nf3; inside it 2...Nf6 3.d3, or 3.Nc3 Nxe4 4.Nxe4; inside that 4.Bxf7+
        var e5 = played.MainLine()[1].Id;
        var bc4 = (await client.PlayAsync(game.Id, e5, "Bc4")).NodeId;
        var nf6 = (await client.PlayAsync(game.Id, bc4, "Nf6")).NodeId;
        await client.PlayAsync(game.Id, nf6, "d3");
        var nc3 = (await client.PlayAsync(game.Id, nf6, "Nc3")).NodeId;
        var nxe4 = (await client.PlayAsync(game.Id, nc3, "Nxe4")).NodeId;
        await client.PlayAsync(game.Id, nxe4, "Nxe4");
        var (bxf7, after) = await client.PlayAsync(game.Id, nxe4, "Bxf7+");

        Assert.Equal(["e4", "e5", "Nf3"], after.MainLine().Select(n => n.San));
        Assert.Equal(["Bc4", "Nf6", "d3"], after.LineFrom(bc4).Select(n => n.San));
        Assert.Equal(["Nc3", "Nxe4", "Nxe4"], after.LineFrom(nc3).Select(n => n.San));
        Assert.Equal(["Nxe4", "Bxf7+"], after.Node(nxe4).Children.Select(id => after.Node(id).San));
        Assert.Equal(7, after.Node(bxf7).Ply);
    }

    [Fact]
    public async Task Promoting_a_nested_variation_makes_its_whole_line_the_main_line()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync(result: "0-1");
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3", "Nc6");
        var e5 = played.MainLine()[1].Id;
        var bc4 = (await client.PlayAsync(game.Id, e5, "Bc4")).NodeId;
        await client.PlayAsync(game.Id, bc4, "Nc6");
        var nf6 = (await client.PlayAsync(game.Id, bc4, "Nf6")).NodeId;

        var response = await client.PostAsync($"/api/games/{game.Id}/moves/{nf6}/promote", null);

        response.EnsureSuccessStatusCode();
        var after = await client.GetGameAsync(game.Id);
        Assert.Equal(["e4", "e5", "Bc4", "Nf6"], after.MainLine().Select(n => n.San));
        Assert.Equal(["Nf6", "Nc6"], after.Node(bc4).Children.Select(id => after.Node(id).San));
        Assert.Equal(["Bc4", "Nf3"], after.Node(e5).Children.Select(id => after.Node(id).San));
        Assert.Equal("0-1", after.Result);
    }

    [Fact]
    public async Task Deleting_a_variation_removes_that_whole_variation_only()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3", "Nc6");
        var e5 = played.MainLine()[1].Id;
        var bc4 = (await client.PlayAsync(game.Id, e5, "Bc4")).NodeId;
        var nf6 = (await client.PlayAsync(game.Id, bc4, "Nf6")).NodeId;
        await client.PlayAsync(game.Id, nf6, "d3");
        var d4 = (await client.PlayAsync(game.Id, e5, "d4")).NodeId;

        // Right-clicking any move inside the Bc4 line deletes the Bc4 line
        var response = await client.DeleteAsync($"/api/games/{game.Id}/moves/{nf6}/variation");

        response.EnsureSuccessStatusCode();
        var after = await client.GetGameAsync(game.Id);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], after.MainLine().Select(n => n.San));
        Assert.Equal(["Nf3", "d4"], after.Node(e5).Children.Select(id => after.Node(id).San));
        Assert.Equal(5, after.Nodes.Count);
        Assert.Contains(after.Nodes, n => n.Id == d4);
    }

    [Fact]
    public async Task Deleting_a_nested_variation_keeps_the_variation_around_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5");
        var e4 = played.MainLine()[0].Id;
        var c5 = (await client.PlayAsync(game.Id, e4, "c5")).NodeId;
        await client.PlayAsync(game.Id, c5, "Nf3");
        var c3 = (await client.PlayAsync(game.Id, c5, "c3")).NodeId;

        (await client.DeleteAsync($"/api/games/{game.Id}/moves/{c3}/variation")).EnsureSuccessStatusCode();

        var after = await client.GetGameAsync(game.Id);
        Assert.Equal(["c5", "Nf3"], after.LineFrom(c5).Select(n => n.San));
        Assert.Equal(["e5", "c5"], after.Node(e4).Children.Select(id => after.Node(id).San));
    }

    [Fact]
    public async Task Deleting_the_main_line_as_a_variation_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5");

        var response = await client.DeleteAsync($"/api/games/{game.Id}/moves/{played.MainLine()[1].Id}/variation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, (await client.GetGameAsync(game.Id)).Nodes.Count);
    }

    [Fact]
    public async Task Deleting_from_a_main_line_move_removes_it_and_everything_after_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3", "Nc6", "Bb5");
        var nf3 = played.MainLine()[2].Id;
        await client.PlayAsync(game.Id, played.MainLine()[3].Id, "Bc4");

        var response = await client.DeleteAsync($"/api/games/{game.Id}/moves/{nf3}");

        response.EnsureSuccessStatusCode();
        var after = await client.GetGameAsync(game.Id);
        Assert.Equal(["e4", "e5"], after.MainLine().Select(n => n.San));
        Assert.Equal(2, after.Nodes.Count);
    }

    [Fact]
    public async Task Deleting_from_the_main_continuation_lets_the_next_variation_take_its_place()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3");
        var e5 = played.MainLine()[1].Id;
        await client.PlayAsync(game.Id, e5, "Bc4");

        (await client.DeleteAsync($"/api/games/{game.Id}/moves/{played.MainLine()[2].Id}")).EnsureSuccessStatusCode();

        Assert.Equal(["e4", "e5", "Bc4"], (await client.GetGameAsync(game.Id)).MainLine().Select(n => n.San));
    }

    [Fact]
    public async Task Tree_operations_on_a_move_of_another_game_are_not_found()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "A");
        var second = await client.CreateGameAsync(opponent: "B");
        var node = (await client.PlayMainLineAsync(first.Id, "e4")).MainLine()[0].Id;

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/games/{second.Id}/moves/{node}/promote", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/games/{second.Id}/moves/{node}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/games/{second.Id}/moves/{node}/variation")).StatusCode);
    }
}
