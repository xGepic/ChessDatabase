using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class RepertoireTests
{
    [Fact]
    public async Task White_and_Black_Repertoires_start_empty_and_stay_separate()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3");
        await client.AddRepertoireLineAsync("black", null, "e4", "c5");

        Assert.Equal(["e4", "e5", "Nf3"], (await client.RepertoireAsync("white")).MainLine().Select(n => n.San));
        Assert.Equal(["e4", "c5"], (await client.RepertoireAsync("black")).MainLine().Select(n => n.San));
    }

    [Fact]
    public async Task A_line_that_shares_moves_with_the_Repertoire_merges_into_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3", "Nc6", "Bb5");

        var added = await client.AddRepertoireLineAsync("white", null, "e4", "c5", "Nf3");
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3", "Nc6", "Bb5"); // nothing new

        var tree = added.Repertoire;
        var after = await client.RepertoireAsync("white");
        Assert.Single(after.FirstMoves);
        var e4 = after.Node(after.FirstMoves[0]);
        Assert.Equal(["e5", "c5"], e4.Children.Select(id => after.Node(id).San));
        Assert.Equal(7, after.Nodes.Count);
        Assert.Equal("Nf3", tree.Node(added.NodeId).San);
    }

    [Fact]
    public async Task A_line_from_a_Game_is_added_up_to_the_position_on_the_board()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync();
        game = await client.PlayMainLineAsync(game.Id, "d4", "d5", "c4", "e6", "Nc3");
        await client.AddRepertoireLineAsync("white", null, "d4", "Nf6", "c4");

        // "Add this line up to here": the Game's moves to 3.Nc3, sent as UCI from the start.
        await client.AddRepertoireLineAsync("white", null, [.. game.MainLine().Take(5).Select(n => n.Uci)]);

        var tree = await client.RepertoireAsync("white");
        var d4 = tree.Node(tree.FirstMoves.Single());
        Assert.Equal(["Nf6", "d5"], d4.Children.Select(id => tree.Node(id).San));
        Assert.Equal(["d5", "c4", "e6", "Nc3"], tree.LineFrom(d4.Children[1]).Select(n => n.San));
    }

    [Fact]
    public async Task A_line_can_go_on_from_any_move_of_the_Repertoire()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var start = await client.AddRepertoireLineAsync("black", null, "e4", "c5");

        await client.AddRepertoireLineAsync("black", start.NodeId, "Nf3", "d6");

        Assert.Equal(["e4", "c5", "Nf3", "d6"], (await client.RepertoireAsync("black")).MainLine().Select(n => n.San));
    }

    [Fact]
    public async Task Deleting_from_a_move_removes_it_and_everything_after()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3", "Nc6");
        await client.AddRepertoireLineAsync("white", null, "e4", "c5", "Nf3");
        var tree = await client.RepertoireAsync("white");
        var c5 = tree.Node(tree.Node(tree.FirstMoves[0]).Children[1]);

        var response = await client.DeleteAsync($"/api/repertoire/white/nodes/{c5.Id}");

        response.EnsureSuccessStatusCode();
        var after = await client.RepertoireAsync("white");
        Assert.Equal(4, after.Nodes.Count);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], after.MainLine().Select(n => n.San));
    }

    [Fact]
    public async Task A_Variation_can_be_deleted_and_promoted()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3");
        var sicilian = await client.AddRepertoireLineAsync("white", null, "e4", "c5", "Nf3");
        await client.AddRepertoireLineAsync("white", null, "e4", "e6", "d4");

        (await client.PostAsync($"/api/repertoire/white/nodes/{sicilian.NodeId}/promote", null)).EnsureSuccessStatusCode();
        Assert.Equal(["e4", "c5", "Nf3"], (await client.RepertoireAsync("white")).MainLine().Select(n => n.San));

        var tree = await client.RepertoireAsync("white");
        var french = tree.LineFrom(tree.Node(tree.FirstMoves[0]).Children[2]).Last();
        (await client.DeleteAsync($"/api/repertoire/white/nodes/{french.Id}/variation")).EnsureSuccessStatusCode();
        tree = await client.RepertoireAsync("white");
        Assert.Equal(["c5", "e5"], tree.Node(tree.FirstMoves[0]).Children.Select(id => tree.Node(id).San));
    }

    [Fact]
    public async Task Moves_carry_comments()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var line = await client.AddRepertoireLineAsync("black", null, "e4", "c5");

        (await client.PutAsJsonAsync($"/api/repertoire/black/nodes/{line.NodeId}/comment", new { text = "  The Sicilian.\n\nAlways.  " }))
            .EnsureSuccessStatusCode();
        Assert.Equal("The Sicilian.\n\nAlways.", (await client.RepertoireAsync("black")).Node(line.NodeId).CommentAfter);

        await client.PutAsJsonAsync($"/api/repertoire/black/nodes/{line.NodeId}/comment", new { text = "" });
        Assert.Null((await client.RepertoireAsync("black")).Node(line.NodeId).CommentAfter);
    }

    [Fact]
    public async Task Illegal_moves_unknown_sides_and_moves_of_the_other_Repertoire_are_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var white = await client.AddRepertoireLineAsync("white", null, "e4");

        var illegal = await client.PostAsJsonAsync("/api/repertoire/white/lines", new { parentId = (int?)null, moves = new[] { "e4", "e4" } });
        var side = await client.GetAsync("/api/repertoire/red");
        var other = await client.PostAsJsonAsync("/api/repertoire/black/lines", new { parentId = white.NodeId, moves = new[] { "e5" } });

        Assert.Equal(HttpStatusCode.BadRequest, illegal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, side.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.Single((await client.RepertoireAsync("white")).Nodes);
    }
}

public sealed record RepertoireDto(string Side, List<int> FirstMoves, List<MoveNodeDto> Nodes)
{
    public MoveNodeDto Node(int id) => Nodes.Single(n => n.Id == id);

    public List<MoveNodeDto> MainLine() => FirstMoves.Count == 0 ? [] : LineFrom(FirstMoves[0]);

    public List<MoveNodeDto> LineFrom(int id)
    {
        var line = new List<MoveNodeDto>();
        for (var node = Node(id); ; node = Node(node.Children[0]))
        {
            line.Add(node);
            if (node.Children.Count == 0) return line;
        }
    }
}

public sealed record AddRepertoireLineDto(int NodeId, RepertoireDto Repertoire);

public static class RepertoireTestExtensions
{
    public static async Task<RepertoireDto> RepertoireAsync(this HttpClient client, string side) =>
        (await client.GetFromJsonAsync<RepertoireDto>($"/api/repertoire/{side}"))!;

    public static async Task<AddRepertoireLineDto> AddRepertoireLineAsync(this HttpClient client, string side, int? parentId, params string[] moves)
    {
        var response = await client.PostAsJsonAsync($"/api/repertoire/{side}/lines", new { parentId, moves });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddRepertoireLineDto>())!;
    }
}
