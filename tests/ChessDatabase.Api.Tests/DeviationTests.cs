using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

/// <summary>The Deviation: where a Game first left the Owner's Repertoire for the Owner's colour.</summary>
public class DeviationTests
{
    [Fact]
    public async Task The_Owner_deviates_when_they_play_a_move_outside_their_Repertoire()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3", "Nc6", "Bb5");
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3", "Nc6", "Bc4"); // a second choice at move 3
        var game = await client.SeedGameAsync("1-0", "e4", "e5", "Nf3", "Nc6", "d4", "exd4");

        var deviation = await client.DeviationAsync(game.Id);

        Assert.Equal(("deviation", 5, "owner", "d4"), (deviation.Status, deviation.Ply, deviation.By, deviation.San));
        Assert.Equal(["Bb5", "Bc4"], deviation.Expected);
        Assert.Equal(game.MainLine()[4].Id, deviation.NodeId);
    }

    [Fact]
    public async Task The_Opponent_deviates_when_they_answer_with_a_move_the_Repertoire_does_not_expect()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.AddRepertoireLineAsync("black", null, "e4", "c5", "Nf3", "d6");
        var game = await client.SeedBlackGameAsync("0-1", "e4", "c5", "c3", "d5");

        var deviation = await client.DeviationAsync(game.Id);

        Assert.Equal(("deviation", 3, "opponent", "c3"), (deviation.Status, deviation.Ply, deviation.By, deviation.San));
        Assert.Equal(["Nf3"], deviation.Expected);
    }

    [Fact]
    public async Task A_Game_that_stays_in_the_Repertoire_until_it_runs_out_is_the_end_of_the_Repertoire()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.AddRepertoireLineAsync("white", null, "d4", "d5", "c4");
        var outOfBook = await client.SeedGameAsync("1-0", "d4", "d5", "c4", "e6", "Nc3");
        var shortGame = await client.SeedGameAsync("1-0", "d4", "d5");
        var noRepertoire = await client.SeedBlackGameAsync("1-0", "d4", "d5");

        var ranOut = await client.DeviationAsync(outOfBook.Id);
        var ended = await client.DeviationAsync(shortGame.Id);
        var empty = await client.DeviationAsync(noRepertoire.Id);

        Assert.Equal(("endOfRepertoire", 4, (string?)null, "e6"), (ranOut.Status, ranOut.Ply, ranOut.By, ranOut.San));
        Assert.Equal(outOfBook.MainLine()[3].Id, ranOut.NodeId);
        Assert.Equal(("gameEnded", (int?)null), (ended.Status, ended.Ply));
        Assert.Equal(("endOfRepertoire", 1), (empty.Status, empty.Ply)); // nothing in the Black Repertoire yet
    }

    [Fact]
    public async Task A_transposition_back_into_the_Repertoire_counts_as_in_book()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.AddRepertoireLineAsync("white", null, "d4", "Nf6", "c4", "e6", "Nc3", "Bb4", "e3");
        // Another move order to the same position after 3...Bb4, then a move the Repertoire doesn't have.
        var game = await client.SeedGameAsync("1-0", "c4", "e6", "Nc3", "Nf6", "d4", "Bb4", "Qc2");

        var deviation = await client.DeviationAsync(game.Id);

        Assert.Equal(("deviation", 7, "owner", "Qc2"), (deviation.Status, deviation.Ply, deviation.By, deviation.San));
        Assert.Equal(["e3"], deviation.Expected);
    }

    [Fact]
    public async Task The_list_shows_only_the_Owners_own_Deviations_with_the_expected_move_and_can_be_filtered()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3");
        await client.AddRepertoireLineAsync("black", null, "d4", "Nf6");
        var forgotWhite = await client.SeedGameAsync("1-0", "e4", "e5", "Bc4");
        var forgotBlack = await client.SeedBlackGameAsync("0-1", "d4", "d5");
        await client.SeedGameAsync("1-0", "e4", "c5", "Nf3");          // the Opponent deviated
        await client.SeedGameAsync("1-0", "e4", "e5", "Nf3", "Nc6");   // end of Repertoire

        var all = await client.GetFromJsonAsync<List<OwnerDeviationDto>>("/api/repertoire/deviations");
        var white = await client.GetFromJsonAsync<List<OwnerDeviationDto>>("/api/repertoire/deviations?ownerColour=white");

        Assert.Equal([forgotWhite.Id, forgotBlack.Id], all!.Select(d => d.Game.Id).Order());
        var bc4 = all!.Single(d => d.Game.Id == forgotWhite.Id);
        Assert.Equal((3, "Bc4", "Nf3"), (bc4.Ply, bc4.San, Assert.Single(bc4.Expected)));
        var d5 = all!.Single(d => d.Game.Id == forgotBlack.Id);
        Assert.Equal(("d5", "Nf6"), (d5.San, Assert.Single(d5.Expected)));
        Assert.Equal([forgotWhite.Id], white!.Select(d => d.Game.Id));
    }
}

public sealed record OwnerDeviationDto(GameSummaryRef Game, int Ply, int NodeId, string San, List<string> Expected);

public sealed record GameSummaryRef(int Id);

/// <param name="Status">"deviation", "endOfRepertoire" (the Repertoire ran out first) or "gameEnded" (in book to the end).</param>
/// <param name="Ply">The half-move of the Deviation, or the first one the Repertoire has no move for; null when the Game ended in book.</param>
/// <param name="By">"owner" or "opponent", for a Deviation.</param>
public sealed record DeviationDto(string Status, int? Ply, int? NodeId, string? By, string? San, List<string> Expected);

public static class DeviationTestExtensions
{
    public static async Task<DeviationDto> DeviationAsync(this HttpClient client, int gameId) =>
        (await client.GetFromJsonAsync<DeviationDto>($"/api/games/{gameId}/deviation"))!;
}
