using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class ExportTests
{
    private static async Task<GameDto> RichGameAsync(HttpClient client)
    {
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync(ownerColour: "black", opponent: "Huber, Thomas", whiteElo: 1880, blackElo: 0, result: "0-1");
        await client.UpdateDetailsAsync(game, new
        {
            @event = "Club Championship", site = "Vienna", date = "2025-03", round = "4",
            timeControl = "90+30", notes = "Before the break.", tags = new[] { "club league", "classical" },
        });
        var played = await client.PlayMainLineAsync(game.Id, "e4", "c5", "Nf3", "d6", "d4", "cxd4");
        var main = played.MainLine();
        await client.SetCommentAsync(game.Id, main[1].Id, "The Sicilian.");
        await client.SetNagsAsync(game.Id, main[3].Id, 1, 10);
        await client.PutAsJsonAsync($"/api/games/{game.Id}/moves/{main[0].Id}/comment", new { text = "First game of the season.", placement = "before" });
        var bc4 = (await client.PlayAsync(game.Id, main[1].Id, "Bc4")).NodeId;
        await client.SetCommentAsync(game.Id, bc4, "Bowdler Attack");
        var e6 = (await client.PlayAsync(game.Id, bc4, "e6")).NodeId;
        await client.PlayAsync(game.Id, bc4, "Nc6");
        await client.SetNagsAsync(game.Id, e6, 6);
        return await client.GetGameAsync(game.Id);
    }

    [Fact]
    public async Task A_single_game_exports_as_standard_pgn()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await RichGameAsync(client);

        var response = await client.GetAsync($"/api/games/{game.Id}/pgn");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-chess-pgn", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(".pgn", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        var pgn = await response.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "[Event \"Club Championship\"]\n[Site \"Vienna\"]\n[Date \"2025.03.??\"]\n[Round \"4\"]\n" +
            "[White \"Huber, Thomas\"]\n[Black \"Max Mustermann\"]\n[Result \"0-1\"]\n[WhiteElo \"1880\"]\n",
            pgn.Replace("\r\n", "\n"));
        Assert.DoesNotContain("BlackElo", pgn);
        var movetext = string.Join(' ', pgn.Replace("\r\n", "\n").Split("\n\n", 2)[1].Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(
            "{First game of the season.} 1. e4 c5 {The Sicilian.} 2. Nf3 (2. Bc4 {Bowdler Attack} 2... e6 $6 (2... Nc6)) " +
            "2... d6 $1 $10 3. d4 cxd4 0-1",
            movetext);
    }

    [Fact]
    public async Task Exported_pgn_reimports_into_an_empty_collection_unchanged()
    {
        using var source = new CollectionFactory();
        var sourceClient = source.CreateClient();
        var original = await RichGameAsync(sourceClient);
        var pgn = await sourceClient.GetStringAsync($"/api/games/{original.Id}/pgn");

        using var target = new CollectionFactory();
        var targetClient = target.CreateClient();
        await targetClient.SetOwnerNameAsync("Max Mustermann");
        var summary = await targetClient.ImportTextAsync(pgn);

        var copy = await targetClient.GetGameAsync(Assert.Single(summary.Games).GameId!.Value);
        Assert.Equal(Details(original), Details(copy));
        Assert.Equal(Tree(original), Tree(copy));
    }

    [Fact]
    public async Task Unrated_sides_and_unknown_details_follow_pgn_conventions()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync(result: "1/2-1/2");

        var pgn = (await client.GetStringAsync($"/api/games/{game.Id}/pgn")).Replace("\r\n", "\n");

        Assert.Contains("[Event \"?\"]\n[Site \"?\"]\n[Date \"????.??.??\"]\n[Round \"?\"]\n", pgn);
        Assert.DoesNotContain("Elo", pgn);
        Assert.EndsWith("1/2-1/2\n", pgn);
    }

    [Fact]
    public async Task The_whole_collection_exports_to_one_file()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var a = await client.CreateGameAsync(opponent: "A");
        var b = await client.CreateGameAsync(opponent: "B");
        await client.PlayMainLineAsync(a.Id, "e4");
        await client.PlayMainLineAsync(b.Id, "d4");

        var pgn = await client.GetStringAsync("/api/games/pgn");

        using var target = new CollectionFactory();
        var targetClient = target.CreateClient();
        await targetClient.SetOwnerNameAsync("Max Mustermann");
        var summary = await targetClient.ImportTextAsync(pgn);
        Assert.Equal(2, summary.Imported);
    }

    [Fact]
    public async Task Exporting_a_missing_game_is_not_found()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/games/42/pgn")).StatusCode);
    }

    private static object Details(GameDto g) => new
    {
        g.OwnerColour, Opponent = g.Opponent.Name, g.White, g.Black, g.WhiteElo, g.BlackElo, g.Result,
        Event = g.Event?.Name, g.Site, g.Date, g.Round, g.TimeControl, g.Notes, Tags = string.Join("|", g.Tags), g.Eco, g.OpeningName,
    };

    /// <summary>The move tree as text: every node with its line structure and Annotations.</summary>
    private static string Tree(GameDto game)
    {
        var lines = new List<string>();
        void Walk(IReadOnlyList<int> ids, string indent)
        {
            foreach (var id in ids)
            {
                var n = game.Node(id);
                lines.Add($"{indent}{n.Ply}:{n.San} [{string.Join(',', n.Nags)}] <{n.CommentBefore}> <{n.CommentAfter}>");
                Walk(n.Children, indent + " ");
            }
        }
        Walk(game.FirstMoves, "");
        return string.Join('\n', lines);
    }
}
