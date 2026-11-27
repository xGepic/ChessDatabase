using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// The app runs as a website on localhost without logins, so other sites open in the same
/// browser must not be able to change the Collection.
/// </summary>
public class LocalOnlyTests
{
    private static HttpRequestMessage SetOwner(string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = JsonContent.Create(new { ownerName = "Max Mustermann" }) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        return request;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:4200")]
    [InlineData("http://127.0.0.1:5080")]
    public async Task Changes_from_the_app_itself_are_accepted(string? origin)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(SetOwner(origin));

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("null")]
    public async Task Changes_from_another_site_are_refused(string origin)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(SetOwner(origin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requests_for_another_host_name_are_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/games");
        request.Headers.Host = "rebound.evil.example";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
