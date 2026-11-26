using System.Net.Http.Json;

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
        this HttpClient client, string ownerColor = "white", string opponent = "Huber, Thomas",
        int? whiteElo = null, int? blackElo = null, string? result = "1-0", string? date = null)
    {
        var player = await client.CreatePlayerAsync(opponent);
        var response = await client.PostAsJsonAsync("/api/games",
            new { ownerColor, opponentId = player.Id, whiteElo, blackElo, result, date });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }

    public static async Task<GameDto> GetGameAsync(this HttpClient client, int id) =>
        (await client.GetFromJsonAsync<GameDto>($"/api/games/{id}"))!;

    public static async Task<List<GameSummaryDto>> ListGamesAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<List<GameSummaryDto>>("/api/games"))!;
}

public sealed record PlayerDto(int Id, string Name);

public sealed record GameSummaryDto(
    int Id, string? Date, string OwnerColor, PlayerDto Opponent, string White, string Black,
    int? WhiteElo, int? BlackElo, string Result);

public sealed record GameDto(
    int Id, string? Date, string OwnerColor, PlayerDto Opponent, string White, string Black,
    int? WhiteElo, int? BlackElo, string Result, string StartFen, List<int> FirstMoves, List<MoveNodeDto> Nodes);

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
