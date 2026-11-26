using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class GameDetailsTests
{
    [Fact]
    public async Task All_optional_details_save_and_read_back()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        await client.UpdateDetailsAsync(game, new
        {
            @event = "Club Championship 2026", site = "Vienna", date = "2026-03", round = "4.2",
            timeControl = "90+30", notes = "Played after a long week.", tags = new[] { "club league", "classical" },
        });

        var saved = await client.GetGameAsync(game.Id);
        Assert.Equal("Club Championship 2026", saved.Event!.Name);
        Assert.Equal("Vienna", saved.Site);
        Assert.Equal("2026-03", saved.Date);
        Assert.Equal("4.2", saved.Round);
        Assert.Equal("90+30", saved.TimeControl);
        Assert.Equal("Played after a long week.", saved.Notes);
        Assert.Equal(["classical", "club league"], saved.Tags);
    }

    [Theory]
    [InlineData("2024")]
    [InlineData("2024-03")]
    [InlineData("2024-03-15")]
    public async Task Partial_dates_are_kept_as_entered(string date)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        await client.UpdateDetailsAsync(game, new { date });

        Assert.Equal(date, (await client.GetGameAsync(game.Id)).Date);
    }

    [Fact]
    public async Task Details_left_empty_read_back_as_absent()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        await client.UpdateDetailsAsync(game, new { @event = " ", site = "", round = (string?)null, tags = Array.Empty<string>() });

        var saved = await client.GetGameAsync(game.Id);
        Assert.Null(saved.Event);
        Assert.Null(saved.Site);
        Assert.Null(saved.Round);
        Assert.Empty(saved.Tags);
    }

    [Fact]
    public async Task Choosing_an_existing_event_links_the_game_to_it_and_events_autocomplete()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "A");
        var second = await client.CreateGameAsync(opponent: "B");
        var third = await client.CreateGameAsync(opponent: "C");
        await client.UpdateDetailsAsync(first, new { @event = "Club Championship 2026" });
        await client.UpdateDetailsAsync(second, new { @event = "club championship 2026" });
        await client.UpdateDetailsAsync(third, new { @event = "Vienna Open" });

        var suggestions = await client.GetFromJsonAsync<List<NamedDto>>("/api/events?search=champ");

        var championship = Assert.Single(suggestions!);
        Assert.Equal("Club Championship 2026", championship.Name);
        Assert.Equal(championship.Id, (await client.GetGameAsync(first.Id)).Event!.Id);
        Assert.Equal(championship.Id, (await client.GetGameAsync(second.Id)).Event!.Id);
    }

    [Fact]
    public async Task Sites_autocomplete_from_earlier_games()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "A");
        var second = await client.CreateGameAsync(opponent: "B");
        await client.UpdateDetailsAsync(first, new { site = "Vienna" });
        await client.UpdateDetailsAsync(second, new { site = "Graz" });

        var suggestions = await client.GetFromJsonAsync<List<string>>("/api/sites?search=vie");

        Assert.Equal(["Vienna"], suggestions);
    }

    [Fact]
    public async Task Tags_can_be_added_and_removed_and_autocomplete()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "A");
        var second = await client.CreateGameAsync(opponent: "B");
        await client.UpdateDetailsAsync(first, new { tags = new[] { "blitz", "club league" } });
        await client.UpdateDetailsAsync(second, new { tags = new[] { "Club League" } });

        await client.UpdateDetailsAsync(first, new { tags = new[] { "club league" } });

        Assert.Equal(["club league"], (await client.GetGameAsync(first.Id)).Tags);
        Assert.Equal(["club league"], (await client.GetGameAsync(second.Id)).Tags);
        var suggestions = await client.GetFromJsonAsync<List<NamedDto>>("/api/tags?search=CLUB");
        Assert.Equal(["club league"], suggestions!.Select(t => t.Name));
    }

    [Theory]
    [InlineData("C60", "Ruy Lopez", "e4", "e5", "Nf3", "Nc6", "Bb5")]
    [InlineData("B90", "Sicilian Defense: Najdorf Variation", "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6")]
    [InlineData("C00", "French Defense", "e4", "e6")]
    // Reached by a different move order than the dataset's: the deepest matching position decides.
    [InlineData("E20", "Nimzo-Indian Defense", "c4", "e6", "d4", "Nf6", "Nc3", "Bb4")]
    public async Task Eco_and_opening_name_are_derived_from_the_main_line(string eco, string name, params string[] moves)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var played = await client.PlayMainLineAsync(game.Id, moves);

        Assert.Equal(eco, played.Eco);
        Assert.Equal(name, played.OpeningName);
    }

    [Fact]
    public async Task Eco_follows_the_main_line_when_a_variation_is_promoted_or_moves_are_deleted()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var played = await client.PlayMainLineAsync(game.Id, "e4", "e6");
        Assert.Equal("C00", played.Eco);
        var c5 = (await client.PlayAsync(game.Id, played.MainLine()[0].Id, "c5")).NodeId;

        await client.PostAsync($"/api/games/{game.Id}/moves/{c5}/promote", null);
        var promoted = await client.GetGameAsync(game.Id);
        Assert.Equal(("B20", "Sicilian Defense"), (promoted.Eco, promoted.OpeningName));

        await client.DeleteAsync($"/api/games/{game.Id}/moves/{promoted.MainLine()[0].Id}");
        var empty = await client.GetGameAsync(game.Id);
        Assert.Null(empty.Eco);
        Assert.Null(empty.OpeningName);
    }

    [Fact]
    public async Task The_game_list_shows_event_and_eco()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        await client.UpdateDetailsAsync(game, new { @event = "Vienna Open" });
        await client.PlayMainLineAsync(game.Id, "e4", "e6");

        var row = Assert.Single(await client.ListGamesAsync());

        Assert.Equal("Vienna Open", row.Event!.Name);
        Assert.Equal("C00", row.Eco);
    }

    [Fact]
    public async Task Overlong_details_are_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}", new
        {
            ownerColour = game.OwnerColour, opponentId = game.Opponent.Id, result = game.Result, site = new string('x', 300),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
