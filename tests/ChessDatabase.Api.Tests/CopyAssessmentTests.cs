using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

/// <summary>Copying Engine Assessment into the Game (ADR 0003: the only route from one to the other).</summary>
public class CopyAssessmentTests
{
    private const int Inaccuracy = 6, Mistake = 2, Blunder = 4, Good = 1;

    [Fact]
    public async Task Copying_one_move_adds_its_symbol_and_the_Engine_line_as_a_Variation()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;

        var updated = await client.CopyMoveAsync(game.Id, ply: 3); // 2.Qh5, an inaccuracy

        var qh5 = updated.MainLine()[2];
        Assert.Equal([Inaccuracy], qh5.Nags);
        var afterE5 = updated.Node(qh5.ParentId!.Value);
        Assert.Equal(["Qh5", "Nf3"], afterE5.Children.Select(id => updated.Node(id).San));
        Assert.Equal(["Nf3", "Nc6"], updated.LineFrom(afterE5.Children[1]).Select(n => n.San));
        Assert.Equal(game.MainLine().Select(n => n.Uci), updated.MainLine().Select(n => n.Uci));
    }

    [Fact]
    public async Task Copy_all_takes_every_mistake_and_blunder_and_leaves_inaccuracies()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;

        var response = await client.PostAsync($"/api/games/{game.Id}/assessment/copy", null);

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<CopyResultDto>())!;
        Assert.Equal(1, result.Copied);
        var line = result.Game.MainLine();
        Assert.Empty(line[2].Nags);                // 2.Qh5 was only an inaccuracy
        Assert.Equal([Blunder], line[5].Nags);     // 3...Nf6??
        Assert.Equal(["Nf6", "g6"], line[4].Children.Select(id => result.Game.Node(id).San));
        Assert.Equal(["g6", "Qf3", "Nf6"], result.Game.LineFrom(line[4].Children[1]).Select(n => n.San));
    }

    [Fact]
    public async Task An_Owner_symbol_or_comment_on_the_move_is_never_replaced()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;
        var nf6 = game.MainLine()[5];
        await client.SetNagsAsync(game.Id, nf6.Id, Good, 14);
        await client.SetCommentAsync(game.Id, nf6.Id, "I saw the mate coming.");

        var updated = await client.CopyMoveAsync(game.Id, ply: 6);

        var after = updated.Node(nf6.Id);
        Assert.Equal([Good, 14], after.Nags);
        Assert.Equal("I saw the mate coming.", after.CommentAfter);
        Assert.Equal(2, updated.Node(nf6.ParentId!.Value).Children.Count); // the Engine line is still added
    }

    [Fact]
    public async Task A_position_symbol_alone_does_not_stop_the_move_symbol()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;
        var nf6 = game.MainLine()[5];
        await client.SetNagsAsync(game.Id, nf6.Id, 18); // +-

        var updated = await client.CopyMoveAsync(game.Id, ply: 6);

        Assert.Equal([Blunder, 18], updated.Node(nf6.Id).Nags);
    }

    [Fact]
    public async Task Copying_the_same_move_twice_does_not_double_the_Variation()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;

        await client.CopyMoveAsync(game.Id, ply: 6);
        var twice = await client.CopyMoveAsync(game.Id, ply: 6);
        (await client.PostAsync($"/api/games/{game.Id}/assessment/copy", null)).EnsureSuccessStatusCode();
        var thrice = await client.GetGameAsync(game.Id);

        Assert.Equal(twice.Nodes.Count, thrice.Nodes.Count);
        var nf6 = thrice.MainLine()[5];
        Assert.Equal([Blunder], nf6.Nags);
        Assert.Equal(2, thrice.Node(nf6.ParentId!.Value).Children.Count);
        Assert.Equal(game.Nodes.Count + 3, thrice.Nodes.Count);
    }

    [Fact]
    public async Task Copying_follows_a_Variation_the_Owner_already_entered()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;
        await client.PlayAsync(game.Id, game.MainLine()[4].Id, "g6");

        var updated = await client.CopyMoveAsync(game.Id, ply: 6);

        var bc4 = updated.MainLine()[4];
        Assert.Equal(["Nf6", "g6"], bc4.Children.Select(id => updated.Node(id).San));
        Assert.Equal(["g6", "Qf3", "Nf6"], updated.LineFrom(bc4.Children[1]).Select(n => n.San));
    }

    [Fact]
    public async Task Only_classified_moves_can_be_copied_and_only_with_an_Assessment()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;
        var other = await client.CreateGameAsync(opponent: "Someone Else");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/games/{game.Id}/assessment/moves/1/copy", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/games/{game.Id}/assessment/moves/99/copy", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/games/{other.Id}/assessment/copy", null)).StatusCode);
    }

    [Fact]
    public async Task A_move_no_longer_on_the_main_line_is_not_copied()
    {
        var (factory, client, game) = await AnalysedScholarsMateAsync();
        using var _ = factory;
        var g6 = (await client.PlayAsync(game.Id, game.MainLine()[4].Id, "g6")).NodeId;
        await client.PostAsync($"/api/games/{game.Id}/moves/{g6}/promote", null);

        var response = await client.PostAsync($"/api/games/{game.Id}/assessment/moves/6/copy", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(CollectionFactory, HttpClient, GameDto)> AnalysedScholarsMateAsync()
    {
        var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = GameAnalysisTests.ScholarsMateEngine();
        await client.UseAsync(engine);
        var game = await GameAnalysisTests.CreateScholarsMateAsync(client);
        await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });
        await client.WaitForAssessmentAsync(game.Id);
        return (factory, client, game);
    }
}

public sealed record CopyResultDto(int Copied, GameDto Game);

public static class CopyTestExtensions
{
    public static async Task<GameDto> CopyMoveAsync(this HttpClient client, int gameId, int ply)
    {
        var response = await client.PostAsync($"/api/games/{gameId}/assessment/moves/{ply}/copy", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }
}
