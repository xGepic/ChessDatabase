using System.Buffers.Binary;
using System.Net;

namespace ChessDatabase.Api.Tests;

public class DiagramTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

    private static async Task<byte[]> DiagramAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/diagram?{query}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string Fen(string fen) => "fen=" + Uri.EscapeDataString(fen);

    /// <summary>Width and height from the PNG header (IHDR follows the 8-byte signature).</summary>
    private static (int Width, int Height) Size(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Fact]
    public async Task A_position_renders_as_a_square_png_board()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var png = await DiagramAsync(client, Fen(StartFen));

        Assert.Equal((480, 480), Size(png));
    }

    [Fact]
    public async Task Coordinates_add_a_margin_for_the_files_and_ranks()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var png = await DiagramAsync(client, Fen(StartFen) + "&coordinates=true");

        Assert.Equal((504, 504), Size(png));
    }

    [Fact]
    public async Task Rendering_is_deterministic()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var query = Fen(AfterE4) + "&orientation=black&coordinates=true&lastMove=e2e4";

        var first = await DiagramAsync(client, query);
        var second = await DiagramAsync(client, query);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Orientation_coordinates_and_last_move_each_change_the_image()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var plain = await DiagramAsync(client, Fen(AfterE4));

        var variants = new[]
        {
            await DiagramAsync(client, Fen(AfterE4) + "&orientation=black"),
            await DiagramAsync(client, Fen(AfterE4) + "&lastMove=e2e4"),
            await DiagramAsync(client, Fen(AfterE4) + "&coordinates=true"),
            await DiagramAsync(client, Fen(StartFen)),
        };

        Assert.All(variants, v => Assert.NotEqual(plain, v));
    }

    [Theory]
    [InlineData("fen=not-a-fen")]
    [InlineData("fen=rnbqkbnr%2Fpppppppp%2F8%2F8%2F8%2F8%2FPPPPPPPP%2FRNBQKBNR%20w%20KQkq%20-%200%201&orientation=sideways")]
    [InlineData("fen=rnbqkbnr%2Fpppppppp%2F8%2F8%2F8%2F8%2FPPPPPPPP%2FRNBQKBNR%20w%20KQkq%20-%200%201&lastMove=e2")]
    public async Task Bad_input_is_refused(string query)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/diagram?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
