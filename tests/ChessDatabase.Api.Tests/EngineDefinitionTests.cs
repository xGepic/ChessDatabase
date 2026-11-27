using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public sealed record UciOptionDto(string Name, string Value);

public sealed record EngineDefinitionDto(
    int Id, string Name, string Path, int Threads, int HashMb, int MultiPv, int? InteractiveSecondsLimit,
    double AnalysisSecondsPerMove, string? ReportedName, List<UciOptionDto> Options);

public sealed record EngineSelectionDto(int InteractiveEngineId, int AnalysisEngineId);

public static class EngineDefinitionExtensions
{
    public static async Task<List<EngineDefinitionDto>> EnginesAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<List<EngineDefinitionDto>>("/api/engines"))!;

    /// <summary>Adds an engine definition running the fake engine; returns it.</summary>
    public static async Task<EngineDefinitionDto> AddEngineAsync(
        this HttpClient client, string name, FakeEngine fake, int threads = 2, int hashMb = 64, int multiPv = 3, object[]? options = null)
    {
        var response = await client.PostAsJsonAsync("/api/engines", new
        {
            name, path = FakeEngine.ExePath, arguments = fake.Arguments(), threads, hashMb, multiPv, options = options ?? [],
        });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<EngineDefinitionDto>())!;
    }

    public static async Task SelectEnginesAsync(this HttpClient client, int interactive, int analysis) =>
        (await client.PutAsJsonAsync("/api/engines/selection", new { interactiveEngineId = interactive, analysisEngineId = analysis }))
            .EnsureSuccessStatusCode();
}

public class EngineDefinitionTests
{
    private static object[] Lc0Options(string weights) =>
        [new { name = "WeightsFile", value = weights }, new { name = "Backend", value = "cuda-fp16" }];

    [Fact]
    public async Task A_second_engine_can_be_added_with_its_own_uci_options()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var lc0 = new FakeEngine { Name = "Lc0 v0.31.2", Options = ["Threads", "MultiPV", "WeightsFile", "Backend"] };

        var added = await client.AddEngineAsync("Lc0", lc0, options: Lc0Options(@"C:\Nets\BT4.pb.gz"));

        Assert.Equal("Lc0 v0.31.2", added.ReportedName);
        var engines = await client.EnginesAsync();
        Assert.Equal(["Stockfish", "Lc0"], engines.Select(e => e.Name));
        Assert.Equal([new UciOptionDto("WeightsFile", @"C:\Nets\BT4.pb.gz"), new UciOptionDto("Backend", "cuda-fp16")], engines[1].Options);
    }

    [Fact]
    public async Task An_engine_can_be_renamed_and_its_options_changed()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        var added = await client.AddEngineAsync("Lc0", fake, options: Lc0Options("a.pb.gz"));

        var response = await client.PutAsJsonAsync($"/api/engines/{added.Id}", new
        {
            name = "Leela", path = FakeEngine.ExePath, threads = 3, hashMb = 64, multiPv = 2,
            options = new[] { new { name = "WeightsFile", value = "b.pb.gz" } },
        });

        response.EnsureSuccessStatusCode();
        var saved = (await client.EnginesAsync()).Single(e => e.Id == added.Id);
        Assert.Equal(("Leela", 3, 2), (saved.Name, saved.Threads, saved.MultiPv));
        Assert.Equal([new UciOptionDto("WeightsFile", "b.pb.gz")], saved.Options);
    }

    [Theory]
    [InlineData("", "x", "name")]                 // no option name
    [InlineData("Threads", "4", "Threads")]       // set with its own field
    [InlineData("Backend", "cuda\nquit", "line")] // would smuggle in a UCI command
    public async Task Bad_options_are_refused(string name, string value, string expected)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/engines", new
        {
            name = "Lc0", path = "", threads = 2, hashMb = 64, multiPv = 1, options = new[] { new { name, value } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Engine_names_are_required_and_unique()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var empty = await client.PostAsJsonAsync("/api/engines", new { name = " ", path = "", threads = 2, hashMb = 64, multiPv = 1 });
        var duplicate = await client.PostAsJsonAsync("/api/engines", new { name = "stockfish", path = "", threads = 2, hashMb = 64, multiPv = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("already", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Each_use_starts_on_the_first_engine_and_is_chosen_separately()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        var lc0 = await client.AddEngineAsync("Lc0", fake);

        Assert.Equal(new EngineSelectionDto(1, 1), await client.GetFromJsonAsync<EngineSelectionDto>("/api/engines/selection"));
        await client.SelectEnginesAsync(interactive: lc0.Id, analysis: 1);

        Assert.Equal(new EngineSelectionDto(lc0.Id, 1), await client.GetFromJsonAsync<EngineSelectionDto>("/api/engines/selection"));
        var unknown = await client.PutAsJsonAsync("/api/engines/selection", new { interactiveEngineId = 99, analysisEngineId = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Removing_an_engine_moves_its_uses_to_the_first_remaining_one()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        var lc0 = await client.AddEngineAsync("Lc0", fake);
        await client.SelectEnginesAsync(interactive: lc0.Id, analysis: lc0.Id);

        (await client.DeleteAsync($"/api/engines/{lc0.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(["Stockfish"], (await client.EnginesAsync()).Select(e => e.Name));
        Assert.Equal(new EngineSelectionDto(1, 1), await client.GetFromJsonAsync<EngineSelectionDto>("/api/engines/selection"));
    }

    [Fact]
    public async Task The_last_engine_cannot_be_removed()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/engines/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
