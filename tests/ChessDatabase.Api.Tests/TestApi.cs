using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChessDatabase.Api.Tests;

/// <summary>The calls the UI makes, so tests read as user steps.</summary>
public static class TestApi
{
    public static async Task SetOwnerNameAsync(this HttpClient client, string name) =>
        (await client.PutAsJsonAsync("/api/settings", new { ownerName = name })).EnsureSuccessStatusCode();

    public static async Task<PlayerDto> CreatePlayerAsync(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/players", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlayerDto>())!;
    }

    public static async Task<GameDto> CreateGameAsync(
        this HttpClient client, string ownerColour = "white", string opponent = "Huber, Thomas",
        int? whiteElo = null, int? blackElo = null, string? result = "1-0", string? date = null)
    {
        var player = await client.CreatePlayerAsync(opponent);
        var response = await client.PostAsJsonAsync("/api/games",
            new { ownerColour, opponentId = player.Id, whiteElo, blackElo, result, date });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }

    /// <summary>Saves the Game details with the given fields changed and the required ones kept.</summary>
    public static async Task UpdateDetailsAsync(this HttpClient client, GameDto game, object changes)
    {
        var body = new JsonObject
        {
            ["ownerColour"] = game.OwnerColour,
            ["opponentId"] = game.Opponent.Id,
            ["whiteElo"] = game.WhiteElo,
            ["blackElo"] = game.BlackElo,
            ["result"] = game.Result,
            ["date"] = game.Date,
        };
        foreach (var (key, value) in JsonSerializer.SerializeToNode(changes)!.AsObject())
            body[key] = value?.DeepClone();
        (await client.PutAsJsonAsync($"/api/games/{game.Id}", body)).EnsureSuccessStatusCode();
    }

    public static async Task<ImportSummaryDto> ImportTextAsync(this HttpClient client, string pgn)
    {
        var response = await client.PostAsJsonAsync("/api/import/text", new { pgn });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
    }

    public static async Task<List<ReviewItemDto>> ReviewListAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<List<ReviewItemDto>>("/api/import/review"))!;

    public static async Task<GameDto> ResolveAsync(this HttpClient client, int itemId, object resolution)
    {
        var response = await client.PostAsJsonAsync($"/api/import/review/{itemId}/resolve", resolution);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }

    public static async Task<GameDto> GetGameAsync(this HttpClient client, int id) =>
        (await client.GetFromJsonAsync<GameDto>($"/api/games/{id}"))!;

    public static async Task<List<GameSummaryDto>> ListGamesAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<List<GameSummaryDto>>("/api/games"))!;
}

public sealed record PlayerDto(int Id, string Name);

public sealed record NamedDto(int Id, string Name);

public sealed record GameSummaryDto(
    int Id, string? Date, string OwnerColour, PlayerDto Opponent, string White, string Black,
    int? WhiteElo, int? BlackElo, string Result, NamedDto? Event, string? Eco, string? OpeningName);

public sealed record GameDto(
    int Id, string? Date, string OwnerColour, PlayerDto Opponent, string White, string Black,
    int? WhiteElo, int? BlackElo, string Result, NamedDto? Event, string? Eco, string? OpeningName,
    string? Site, string? Round, string? TimeControl, string? Notes, List<string> Tags,
    string StartFen, List<int> FirstMoves, List<MoveNodeDto> Nodes);

public sealed record MoveNodeDto(
    int Id, int? ParentId, List<int> Children, int Ply, string San, string Uci, string Fen,
    string? CommentBefore, string? CommentAfter, List<int> Nags);

public static class MoveTreeTestExtensions
{
    public static async Task<AddMoveResponseDto> PlayAsync(this HttpClient client, int gameId, int? parentId, string move)
    {
        var response = await client.PostAsJsonAsync($"/api/games/{gameId}/moves", new { parentId, move });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddMoveResponseDto>())!;
    }

    /// <summary>Plays each move after the end of the current main line; returns the Game afterwards.</summary>
    public static async Task<GameDto> PlayMainLineAsync(this HttpClient client, int gameId, params string[] moves)
    {
        var game = await client.GetGameAsync(gameId);
        foreach (var move in moves)
        {
            var parent = game.MainLine().LastOrDefault()?.Id;
            game = (await client.PlayAsync(gameId, parent, move)).Game;
        }
        return game;
    }

    public static async Task SetCommentAsync(this HttpClient client, int gameId, int nodeId, string text) =>
        (await client.PutAsJsonAsync($"/api/games/{gameId}/moves/{nodeId}/comment", new { text })).EnsureSuccessStatusCode();

    public static async Task SetNagsAsync(this HttpClient client, int gameId, int nodeId, params int[] nags) =>
        (await client.PutAsJsonAsync($"/api/games/{gameId}/moves/{nodeId}/nags", new { nags })).EnsureSuccessStatusCode();

    public static MoveNodeDto Node(this GameDto game, int id) => game.Nodes.Single(n => n.Id == id);

    /// <summary>Follows first children from the starting position.</summary>
    public static List<MoveNodeDto> MainLine(this GameDto game) => game.LineFrom(game.FirstMoves.FirstOrDefault());

    /// <summary>A node and the first-child continuation after it.</summary>
    public static List<MoveNodeDto> LineFrom(this GameDto game, int nodeId)
    {
        var line = new List<MoveNodeDto>();
        var current = game.Nodes.SingleOrDefault(n => n.Id == nodeId);
        while (current is not null)
        {
            line.Add(current);
            current = current.Children.Count > 0 ? game.Node(current.Children[0]) : null;
        }
        return line;
    }
}

public sealed record AddMoveResponseDto(int NodeId, GameDto Game);
