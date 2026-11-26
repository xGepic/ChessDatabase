using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class PositionSearchTests
{
    [Fact]
    public async Task A_position_is_found_in_every_Game_that_reached_it_by_any_move_order()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var queensGambit = await client.SeedGameAsync("1-0", "d4", "d5", "c4", "e6", "Nc3", "Nf6");
        var english = await client.SeedGameAsync("0-1", "c4", "e6", "Nc3", "d5", "d4", "Nf6", "Bg5");
        await client.SeedGameAsync("1-0", "e4", "e5");

        var results = await client.SearchAsync(ResearchTestExtensions.Fen("d4", "d5", "c4", "e6", "Nc3", "Nf6"));

        Assert.Equal(
            [(english.Id, 6, 4), (queensGambit.Id, 6, 4)],
            results.Select(r => (r.Game.Id, r.Ply, r.MoveNumber)).OrderBy(r => r.Id == english.Id ? 0 : 1));
    }

    [Fact]
    public async Task A_position_that_occurs_only_in_a_Variation_is_not_found()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.SeedGameAsync("1-0", "e4", "e5", "Nf3", "Nc6");
        await client.PlayAsync(game.Id, game.MainLine()[2].Id, "Bc5"); // 2...Bc5 only as a Variation

        var variationOnly = await client.SearchAsync(ResearchTestExtensions.Fen("e4", "e5", "Nf3", "Bc5"));
        var mainLine = await client.SearchAsync(ResearchTestExtensions.Fen("e4", "e5", "Nf3", "Nc6"));

        Assert.Empty(variationOnly);
        Assert.Equal([(game.Id, 4, 3)], mainLine.Select(r => (r.Game.Id, r.Ply, r.MoveNumber)));
    }

    [Fact]
    public async Task A_position_set_up_by_hand_finds_the_Games_with_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.SeedGameAsync("1-0", "e4", "e5", "Qh5", "Nc6", "Bc4", "Nf6", "Qxf7#");

        // The final position, typed in with no move counters, as the position editor sends it.
        var mate = await client.SearchAsync("r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 1");

        Assert.Equal([(game.Id, 7, 4)], mate.Select(r => (r.Game.Id, r.Ply, r.MoveNumber)));
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1", "is in check")]
    [InlineData("4k3/8/8/8/8/8/8/P3K3 w - - 0 1", "Pawns")]
    [InlineData("8/8/8/8/8/8/8/4K3 w - - 0 1", "one king")]
    public async Task A_position_that_cannot_occur_is_refused_with_the_reason(string fen, string reason)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/research/search?fen={Uri.EscapeDataString(fen)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync());
    }
}

public static class PositionSearchTestExtensions
{
    public static async Task<List<PositionGameDto>> SearchAsync(this HttpClient client, string fen)
    {
        var result = (await client.GetFromJsonAsync<PositionSearchDto>($"/api/research/search?fen={Uri.EscapeDataString(fen)}"))!;
        Assert.Equal(result.GameCount, result.Games.Count); // every test Collection is far below the limit
        return result.Games;
    }
}
