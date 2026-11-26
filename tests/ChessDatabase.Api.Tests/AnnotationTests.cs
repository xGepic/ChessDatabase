using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class AnnotationTests
{
    [Fact]
    public async Task A_comment_on_a_main_line_move_can_be_added_edited_and_removed()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var e5 = (await client.PlayMainLineAsync(game.Id, "e4", "e5")).MainLine()[1].Id;

        await client.SetCommentAsync(game.Id, e5, "The open game.");
        Assert.Equal("The open game.", (await client.GetGameAsync(game.Id)).Node(e5).CommentAfter);

        await client.SetCommentAsync(game.Id, e5, "  The open game, as always.  ");
        Assert.Equal("The open game, as always.", (await client.GetGameAsync(game.Id)).Node(e5).CommentAfter);

        await client.SetCommentAsync(game.Id, e5, "   ");
        Assert.Null((await client.GetGameAsync(game.Id)).Node(e5).CommentAfter);
    }

    [Fact]
    public async Task A_comment_on_a_variation_move_reads_back_on_that_move_only()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3");
        var bc4 = (await client.PlayAsync(game.Id, played.MainLine()[1].Id, "Bc4")).NodeId;

        await client.SetCommentAsync(game.Id, bc4, "The Bishop's Opening.");

        var after = await client.GetGameAsync(game.Id);
        Assert.Equal("The Bishop's Opening.", after.Node(bc4).CommentAfter);
        Assert.All(after.MainLine(), n => Assert.Null(n.CommentAfter));
    }

    [Fact]
    public async Task A_comment_can_also_stand_before_a_move()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var e4 = (await client.PlayMainLineAsync(game.Id, "e4")).MainLine()[0].Id;

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}/moves/{e4}/comment",
            new { text = "Best by test:", placement = "before" });

        response.EnsureSuccessStatusCode();
        var node = (await client.GetGameAsync(game.Id)).Node(e4);
        Assert.Equal("Best by test:", node.CommentBefore);
        Assert.Null(node.CommentAfter);
    }

    [Fact]
    public async Task Move_and_position_symbols_are_set_read_back_and_cleared_on_main_line_and_variation_moves()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Qh5");
        var qh5 = played.MainLine()[2].Id;
        var nf3 = (await client.PlayAsync(game.Id, played.MainLine()[1].Id, "Nf3")).NodeId;

        await client.SetNagsAsync(game.Id, qh5, 6, 14);   // ?! and ⩲
        await client.SetNagsAsync(game.Id, nf3, 1);       // !

        var after = await client.GetGameAsync(game.Id);
        Assert.Equal([6, 14], after.Node(qh5).Nags);
        Assert.Equal([1], after.Node(nf3).Nags);

        await client.SetNagsAsync(game.Id, qh5);
        Assert.Empty((await client.GetGameAsync(game.Id)).Node(qh5).Nags);
    }

    [Fact]
    public async Task Duplicate_symbols_are_stored_once()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var e4 = (await client.PlayMainLineAsync(game.Id, "e4")).MainLine()[0].Id;

        await client.SetNagsAsync(game.Id, e4, 1, 1, 16);

        Assert.Equal([1, 16], (await client.GetGameAsync(game.Id)).Node(e4).Nags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(-1)]
    public async Task Symbols_outside_the_nag_range_are_refused(int nag)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var e4 = (await client.PlayMainLineAsync(game.Id, "e4")).MainLine()[0].Id;

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}/moves/{e4}/nags", new { nags = new[] { nag } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(1, 2)]    // ! and ?
    [InlineData(14, 16)]  // ⩲ and ±
    public async Task A_move_takes_at_most_one_move_symbol_and_one_position_symbol(int first, int second)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var e4 = (await client.PlayMainLineAsync(game.Id, "e4")).MainLine()[0].Id;

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}/moves/{e4}/nags", new { nags = new[] { first, second } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetGameAsync(game.Id)).Node(e4).Nags);
    }

    [Fact]
    public async Task Annotations_stay_with_their_move_when_its_variation_is_promoted()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3");
        var bc4 = (await client.PlayAsync(game.Id, played.MainLine()[1].Id, "Bc4")).NodeId;
        await client.SetCommentAsync(game.Id, bc4, "Better.");
        await client.SetNagsAsync(game.Id, bc4, 1);

        await client.PostAsync($"/api/games/{game.Id}/moves/{bc4}/promote", null);

        var promoted = (await client.GetGameAsync(game.Id)).MainLine()[2];
        Assert.Equal(("Bc4", "Better.", 1), (promoted.San, promoted.CommentAfter, promoted.Nags.Single()));
    }

    [Fact]
    public async Task Annotating_a_move_of_another_game_is_not_found()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "A");
        var second = await client.CreateGameAsync(opponent: "B");
        var node = (await client.PlayMainLineAsync(first.Id, "e4")).MainLine()[0].Id;

        var comment = await client.PutAsJsonAsync($"/api/games/{second.Id}/moves/{node}/comment", new { text = "x" });
        var nags = await client.PutAsJsonAsync($"/api/games/{second.Id}/moves/{node}/nags", new { nags = new[] { 1 } });

        Assert.Equal(HttpStatusCode.NotFound, comment.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nags.StatusCode);
    }
}
