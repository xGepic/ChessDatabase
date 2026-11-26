using ChessDatabase.Rules;

namespace ChessDatabase.Api.Research;

/// <summary>A move with its Tablebase verdict, from the side that plays it.</summary>
/// <param name="Category">The verdict for the mover.</param>
/// <param name="Fen">The position after the move (Chess Rules), so the UI can show it without saving anything.</param>
/// <param name="Dtz">Distance to zeroing (a capture or pawn move) in half-moves, for the mover: positive when winning.</param>
/// <param name="Dtm">Distance to mate in half-moves, for the mover: positive when winning; null where unknown.</param>
/// <param name="Zeroing">A capture or pawn move.</param>
public sealed record TablebaseMoveDto(string Uci, string San, string Fen, TablebaseVerdict Category, int? Dtz, int? Dtm, bool Zeroing);

/// <summary>The Tablebase verdict for a position.</summary>
/// <param name="Applicable">False with more than 7 pieces: there is nothing to look up and nobody is asked.</param>
/// <param name="Available">False when the Tablebase could not be asked; Message says why.</param>
/// <param name="Category">The verdict for the side to move (see <see cref="TablebaseMoveDto.Category"/>).</param>
/// <param name="Outcome">"1-0", "0-1" or "1/2-1/2" with best play (a cursed win or blessed loss is a draw); null when unknown.</param>
/// <param name="Moves">Best first, for the side to move.</param>
public sealed record TablebaseDto(
    bool Applicable, bool Available, string? Message, TablebaseVerdict? Category, string? Outcome, int? Dtz, int? Dtm, IReadOnlyList<TablebaseMoveDto> Moves)
{
    public static TablebaseDto NotApplicable { get; } = new(false, false, null, null, null, null, null, []);

    public static TablebaseDto Unavailable(string message) => new(true, false, message, null, null, null, null, []);
}

/// <summary>Tablebase lookups for endgames with up to 7 pieces, through <see cref="ITablebaseSource"/>.</summary>
public static class TablebaseEndpoints
{
    /// <summary>Maps GET /api/research/tablebase?fen=.</summary>
    public static void MapTablebaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/research/tablebase", async (string? fen, ITablebaseSource tablebase) =>
        {
            if (PositionInput.Read(fen, out var position) is { } problem) return problem;
            if (position.PieceCount > ITablebaseSource.MaxPieces) return Results.Ok(TablebaseDto.NotApplicable);
            var answer = await tablebase.LookUpAsync(position);
            return Results.Ok(answer.Value is { } value ? ToDto(position, value) : TablebaseDto.Unavailable(answer.Problem!));
        });
    }

    private static TablebaseDto ToDto(Position position, LichessTablebase answer)
    {
        var moves = new List<TablebaseMoveDto>();
        foreach (var m in answer.Moves)
        {
            if (!Move.TryParseUci(m.Uci, out var move) || !position.IsLegal(move)) continue;
            // Lichess rates a move from the side to move after it; the Owner reads it from the mover's side.
            moves.Add(new TablebaseMoveDto(m.Uci, position.ToSan(move), position.Play(move).ToFen(), TablebaseVerdicts.Parse(m.Category).Opposite(), -m.Dtz, -m.Dtm, m.Zeroing));
        }
        // Best first: by category; a win as fast as possible, a loss as slow as possible.
        var ranked = moves
            .OrderBy(m => m.Category)
            .ThenBy(m => m.Category.Direction() * Math.Abs(m.Dtz ?? 0))
            .ThenBy(m => m.Category.Direction() * Math.Abs(m.Dtm ?? 0))
            .ToList();

        var mover = position.SideToMove;
        var verdict = TablebaseVerdicts.Parse(answer.Category);
        var outcome = verdict switch
        {
            TablebaseVerdict.Win => mover == Color.White ? "1-0" : "0-1",
            TablebaseVerdict.Loss => mover == Color.White ? "0-1" : "1-0",
            TablebaseVerdict.Draw or TablebaseVerdict.CursedWin or TablebaseVerdict.BlessedLoss => "1/2-1/2",
            _ => null,
        };
        return new TablebaseDto(true, true, null, verdict, outcome, answer.Dtz, answer.Dtm, ranked);
    }
}
