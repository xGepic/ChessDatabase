using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class SettingsTests
{
    [Fact]
    public async Task Owner_name_is_empty_until_set_and_then_reads_back()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<SettingsDto>("/api/settings");
        Assert.Equal("", initial!.OwnerName);

        var put = await client.PutAsJsonAsync("/api/settings", new { ownerName = "  Max Mustermann " });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var after = await client.GetFromJsonAsync<SettingsDto>("/api/settings");
        Assert.Equal("Max Mustermann", after!.OwnerName);
    }

    private sealed record SettingsDto(string OwnerName);
}
