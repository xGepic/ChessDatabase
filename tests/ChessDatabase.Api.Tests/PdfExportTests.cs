using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace ChessDatabase.Api.Tests;

public partial class PdfExportTests
{
    private const int DiagramNag = 220;

    /// <summary>
    /// 1.e4 c5 {The Sicilian.} 2.Nf3 (2.Bc4 {Bowdler Attack} 2...e6?! (2...Nc6)) 2...d6! = 3.d4 cxd4,
    /// with a diagram after 2...d6.
    /// </summary>
    private static async Task<GameDto> FixtureGameAsync(HttpClient client)
    {
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync(ownerColour: "black", opponent: "Huber, Thomas", whiteElo: 1880, blackElo: 0, result: "0-1");
        await client.UpdateDetailsAsync(game, new { @event = "Club Championship", site = "Vienna", date = "2025-03-15", round = "4" });
        var main = (await client.PlayMainLineAsync(game.Id, "e4", "c5", "Nf3", "d6", "d4", "cxd4")).MainLine();
        await client.SetCommentAsync(game.Id, main[1].Id, "The Sicilian.");
        await client.SetNagsAsync(game.Id, main[3].Id, 1, 10, DiagramNag);
        var bc4 = (await client.PlayAsync(game.Id, main[1].Id, "Bc4")).NodeId;
        await client.SetCommentAsync(game.Id, bc4, "Bowdler Attack");
        var e6 = (await client.PlayAsync(game.Id, bc4, "e6")).NodeId;
        await client.PlayAsync(game.Id, bc4, "Nc6");
        await client.SetNagsAsync(game.Id, e6, 6);
        return await client.GetGameAsync(game.Id);
    }

    private static async Task<(string Text, int Images)> PdfAsync(HttpClient client, int gameId, bool finalDiagram)
    {
        var response = await client.GetAsync($"/api/games/{gameId}/pdf?finalDiagram={finalDiagram}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".pdf", (response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName)!.Trim('"'));

        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var pages = pdf.GetPages().ToList();
        var text = string.Join(' ', pages.Select(p => string.Join(' ', p.GetWords().Select(w => w.Text))));
        return (Spaces().Replace(text, " "), pages.Sum(p => p.GetImages().Count()));
    }

    [Fact]
    public async Task The_pdf_has_the_header_and_the_annotated_notation()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await FixtureGameAsync(client);

        var (text, _) = await PdfAsync(client, game.Id, finalDiagram: false);

        Assert.Contains("Huber, Thomas 1880", text);
        Assert.Contains("Max Mustermann unrated", text);
        Assert.Contains("Club Championship", text);
        Assert.Contains("Vienna", text);
        Assert.Contains("2025-03-15", text);
        Assert.Contains("Round 4", text);
        Assert.Contains("1.e4 c5 The Sicilian. 2.Nf3", text);
        Assert.Contains("2.Bc4 Bowdler Attack 2...e6?! (2...Nc6)", text);
        Assert.Contains("2...d6! =", text);
        Assert.Contains("3.d4 cxd4 0-1", text);
    }

    [Fact]
    public async Task Diagrams_appear_at_marked_moves_and_optionally_at_the_end()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await FixtureGameAsync(client);

        var (withoutFinal, oneDiagram) = await PdfAsync(client, game.Id, finalDiagram: false);
        var (_, twoDiagrams) = await PdfAsync(client, game.Id, finalDiagram: true);

        Assert.Equal(1, oneDiagram);
        Assert.Equal(2, twoDiagrams);
        Assert.Contains("After 2...d6", withoutFinal);
    }

    [Fact]
    public async Task A_diagram_inside_a_nested_variation_follows_its_paragraph()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await FixtureGameAsync(client);
        var bc4 = game.Nodes.Single(n => n.San == "Bc4");
        var nc6 = game.Nodes.Single(n => n.San == "Nc6");
        await client.SetNagsAsync(game.Id, nc6.Id, DiagramNag);
        await client.PutAsJsonAsync($"/api/games/{game.Id}/moves/{bc4.Id}/comment", new { text = "Instead:", placement = "before" });
        await client.SetNagsAsync(game.Id, game.Nodes.Single(n => n.San == "cxd4").Id, 22, 146);

        var (text, images) = await PdfAsync(client, game.Id, finalDiagram: false);

        Assert.Equal(2, images);
        Assert.Contains("Instead: 2.Bc4 Bowdler Attack 2...e6?! (2...Nc6) After 2...Nc6 2...d6! =", text);
        Assert.Contains("cxd4 ⨀ N 0-1", text);
    }

    [Fact]
    public async Task Diagram_marks_are_saved_with_the_game_and_travel_in_pgn()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await FixtureGameAsync(client);

        var d6 = game.MainLine()[3];
        Assert.Contains(DiagramNag, d6.Nags);
        Assert.Contains("d6 $1 $10 $220", await client.GetStringAsync($"/api/games/{game.Id}/pgn"));
    }

    [Fact]
    public async Task A_missing_game_has_no_pdf()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/games/999/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
