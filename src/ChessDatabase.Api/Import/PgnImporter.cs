using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Api.Openings;
using ChessDatabase.Api.Pgn;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Import;

public enum ImportStatus
{
    Imported,
    NeedsReview,
    Failed,
}

public sealed record ImportedGameDto(int Index, ImportStatus Status, string? Reason, string? White, string? Black, int? GameId);

public sealed record ImportSummaryDto(int Imported, int NeedsReview, int Failed, IReadOnlyList<ImportedGameDto> Games);

/// <summary>
/// Brings PGN Games into the Collection. Moves are replayed through Chess Rules (German piece
/// letters become English), the Owner is recognised by name or Alias, and Games that cannot be
/// assigned to the Owner or have no result are held back for review instead of imported.
/// </summary>
public sealed class PgnImporter(CollectionDbContext db, OpeningClassifier openings)
{
    private List<PlayerRecord> _players = [];
    private List<PersonName> _ownerNames = [];

    public async Task<ImportSummaryDto> ImportAsync(string text)
    {
        _players = await db.Players.ToListAsync();
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync();
        _ownerNames = [.. new[] { settings?.OwnerName ?? "" }.Concat(settings?.Aliases ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(PersonName.Parse)];

        var results = new List<ImportedGameDto>();
        foreach (var read in PgnReader.Read(text))
            results.Add(await ImportOneAsync(read));

        return new ImportSummaryDto(
            results.Count(r => r.Status == ImportStatus.Imported),
            results.Count(r => r.Status == ImportStatus.NeedsReview),
            results.Count(r => r.Status == ImportStatus.Failed),
            results);
    }

    private async Task<ImportedGameDto> ImportOneAsync(PgnReadResult read)
    {
        var pgn = read.Game;
        string? white = pgn?.Tag("White"), black = pgn?.Tag("Black");
        ImportedGameDto Result(ImportStatus status, string? reason, int? gameId = null) =>
            new(read.Index, status, reason, white, black, gameId);

        if (pgn is null) return Result(ImportStatus.Failed, read.Error);
        if (pgn.Tag("SetUp") == "1" || pgn.Tag("FEN") is not null)
            return Result(ImportStatus.Failed, "Games from a custom starting position are not supported.");

        var tree = new TreeBuilder();
        if (tree.Build(pgn.Moves) is { } moveError) return Result(ImportStatus.Failed, moveError);

        var problems = new List<string>();
        var hasResult = GameResults.TryParsePgn(pgn.Tag("Result") ?? pgn.Termination, out var result);
        if (!hasResult) problems.Add("The Game has no result.");
        var whiteIsOwner = IsOwner(white);
        var blackIsOwner = IsOwner(black);
        if (whiteIsOwner == blackIsOwner)
            problems.Add(whiteIsOwner
                ? "Both sides match your name or an Alias."
                : "Neither side matches your name or an Alias.");

        if (problems.Count > 0)
        {
            db.ImportReviewItems.Add(new ImportReviewItemRecord
            {
                Pgn = pgn.Source,
                Reason = string.Join(' ', problems),
                White = white,
                Black = black,
                Date = PgnValues.ToGameDate(pgn.Tag("Date")),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Result(ImportStatus.NeedsReview, string.Join(' ', problems));
        }

        var game = new GameRecord
        {
            OwnerColour = whiteIsOwner ? OwnerColour.White : OwnerColour.Black,
            Opponent = FindOrCreatePlayer((whiteIsOwner ? black : white) ?? "Unknown"),
            WhiteElo = PgnValues.Elo(pgn.Tag("WhiteElo")),
            BlackElo = PgnValues.Elo(pgn.Tag("BlackElo")),
            Result = result,
            Date = PgnValues.ToGameDate(pgn.Tag("Date")),
            Event = await NamedRecords.FindOrCreateEventAsync(db, PgnValues.Text(pgn.Tag("Event"))),
            Site = PgnValues.Text(pgn.Tag("Site")),
            Round = PgnValues.Text(pgn.Tag("Round")),
            TimeControl = PgnValues.Text(pgn.Tag("TimeControl")),
            CreatedAt = DateTime.UtcNow,
            MoveNodes = tree.Nodes,
        };
        var opening = openings.Classify(tree.MainLinePositions);
        game.Eco = opening?.Eco;
        game.OpeningName = opening?.Name;

        db.Games.Add(game);
        await db.SaveChangesAsync();
        return Result(ImportStatus.Imported, null, game.Id);
    }

    private bool IsOwner(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var parsed = PersonName.Parse(name);
        return _ownerNames.Any(owner => owner.Matches(parsed));
    }

    /// <summary>An existing Player only if the name is exactly the same (ignoring case); otherwise a new one.</summary>
    private PlayerRecord FindOrCreatePlayer(string name)
    {
        name = name.Trim();
        var player = _players.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (player is null)
        {
            player = new PlayerRecord { Name = name };
            _players.Add(player);
        }
        return player;
    }

    /// <summary>Turns PGN moves into move tree nodes, checking every move against Chess Rules.</summary>
    private sealed class TreeBuilder
    {
        private readonly Dictionary<MoveNodeRecord, int> _childCounts = [];
        private int _rootCount;

        public List<MoveNodeRecord> Nodes { get; } = [];
        public List<Position> MainLinePositions { get; } = [];

        /// <summary>Null when every move is legal; otherwise which move is not.</summary>
        public string? Build(List<PgnMove> moves) => AddLine(Position.Initial, null, moves, isMainLine: true);

        private string? AddLine(Position start, MoveNodeRecord? parent, List<PgnMove> line, bool isMainLine)
        {
            var position = start;
            foreach (var pgnMove in line)
            {
                if (!position.TryParseSan(pgnMove.San, out var move))
                    return $"Move {MoveLabel(position, pgnMove.San)} is not legal here.";

                var node = new MoveNodeRecord
                {
                    Parent = parent,
                    Order = NextOrder(parent),
                    Uci = move.ToUci(),
                    San = position.ToSan(move),
                    CommentBefore = pgnMove.CommentBefore,
                    CommentAfter = pgnMove.CommentAfter,
                    Nags = [.. pgnMove.Nags.Where(n => n is >= 1 and <= 255).Distinct()],
                };
                Nodes.Add(node);

                // Variations replace this move: they start from the same position, after the same parent.
                foreach (var variation in pgnMove.Variations)
                    if (AddLine(position, parent, variation, isMainLine: false) is { } error) return error;

                position = position.Play(move);
                if (isMainLine) MainLinePositions.Add(position);
                parent = node;
            }
            return null;
        }

        private int NextOrder(MoveNodeRecord? parent)
        {
            if (parent is null) return _rootCount++;
            _childCounts.TryGetValue(parent, out var count);
            _childCounts[parent] = count + 1;
            return count;
        }

        private static string MoveLabel(Position position, string san) =>
            $"{position.FullmoveNumber}{(position.SideToMove == Color.White ? "." : "...")} {san}";
    }
}
