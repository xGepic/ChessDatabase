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
    Duplicate,
    Failed,
}

/// <param name="GameId">The imported Game, or for a duplicate the existing Game it duplicates.</param>
/// <param name="Pgn">For a duplicate: the Game's PGN, so it can be imported anyway.</param>
public sealed record ImportedGameDto(
    int Index, ImportStatus Status, string? Reason, string? White, string? Black, int? GameId, string? Pgn = null);

public sealed record ImportSummaryDto(
    int Imported, int NeedsReview, int Duplicates, int Failed, IReadOnlyList<ImportedGameDto> Games);

/// <summary>
/// Brings PGN Games into the Collection. Moves are replayed through Chess Rules (German piece
/// letters become English), the Owner is recognised by name or Alias, and a Game goes to the
/// review list instead when it has no result, no side matches the Owner, or its Opponent only
/// resembles an existing Player. Games already in the Collection are reported as duplicates.
/// </summary>
public sealed class PgnImporter(CollectionDbContext db, OpeningClassifier openings)
{
    private List<PlayerRecord> _players = [];
    private List<PersonName> _ownerNames = [];

    public async Task<ImportSummaryDto> ImportAsync(string text, bool allowDuplicates = false)
    {
        await LoadContextAsync();
        var results = new List<ImportedGameDto>();
        foreach (var read in PgnReader.Read(text))
            results.Add(await ImportOneAsync(read, allowDuplicates));

        return new ImportSummaryDto(
            results.Count(r => r.Status == ImportStatus.Imported),
            results.Count(r => r.Status == ImportStatus.NeedsReview),
            results.Count(r => r.Status == ImportStatus.Duplicate),
            results.Count(r => r.Status == ImportStatus.Failed),
            results);
    }

    public async Task LoadContextAsync()
    {
        _players = await db.Players.ToListAsync();
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync();
        _ownerNames = [.. new[] { settings?.OwnerName ?? "" }.Concat(settings?.Aliases ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(PersonName.Parse)];
    }

    private async Task<ImportedGameDto> ImportOneAsync(PgnReadResult read, bool allowDuplicates)
    {
        var pgn = read.Game;
        string? white = pgn?.Tag("White"), black = pgn?.Tag("Black");
        ImportedGameDto Result(ImportStatus status, string? reason, int? gameId = null, string? source = null) =>
            new(read.Index, status, reason, white, black, gameId, source);

        if (pgn is null) return Result(ImportStatus.Failed, read.Error);
        if (Problem(pgn) is { } failure) return Result(ImportStatus.Failed, failure);
        var tree = TreeBuilder.Build(pgn.Moves, out var moveError);
        if (tree is null) return Result(ImportStatus.Failed, moveError);

        if (!allowDuplicates && await db.ImportReviewItems.AnyAsync(i => i.Pgn == pgn.Source))
            return Result(ImportStatus.Duplicate, "This Game is already waiting in the review list.", source: pgn.Source);

        var problems = new List<string>();
        var hasResult = GameResults.TryParsePgn(pgn.Tag("Result") ?? pgn.Termination, out var result);
        if (!hasResult) problems.Add("The Game has no result.");

        var ownerColour = OwnerColourOf(pgn);
        PlayerRecord? opponent = null;
        if (ownerColour is null)
        {
            problems.Add(IsOwner(white) && IsOwner(black)
                ? "Both sides match your name or an Alias."
                : "Neither side matches your name or an Alias.");
        }
        else
        {
            var opponentName = OpponentName(pgn, ownerColour.Value);
            opponent = ExactPlayer(opponentName);
            if (opponent is null && SimilarPlayer(opponentName) is { } similar)
                problems.Add($"The Opponent \"{opponentName}\" may be the same Player as \"{similar.Name}\".");

            if (!allowDuplicates && await FindDuplicateAsync(pgn, tree, ownerColour.Value, opponentName) is { } existingId)
                return Result(ImportStatus.Duplicate, "This Game is already in the Collection.", existingId, pgn.Source);
        }

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

        var game = await CreateGameAsync(pgn, tree, ownerColour!.Value, result,
            opponent ?? NewPlayer(OpponentName(pgn, ownerColour.Value)));
        return Result(ImportStatus.Imported, null, game.Id);
    }

    /// <summary>Why a read Game can never be imported, or null.</summary>
    public static string? Problem(PgnGame pgn) =>
        pgn.Tag("SetUp") == "1" || pgn.Tag("FEN") is not null
            ? "Games from a custom starting position are not supported."
            : null;

    /// <summary>Adds the Game to the Collection with its details from the PGN tags.</summary>
    public async Task<GameRecord> CreateGameAsync(
        PgnGame pgn, TreeBuilder tree, OwnerColour ownerColour, GameResult result, PlayerRecord opponent)
    {
        var opening = openings.Classify(tree.MainLinePositions);
        var game = new GameRecord
        {
            OwnerColour = ownerColour,
            Opponent = opponent,
            WhiteElo = PgnValues.Elo(pgn.Tag("WhiteElo")),
            BlackElo = PgnValues.Elo(pgn.Tag("BlackElo")),
            Result = result,
            Date = PgnValues.ToGameDate(pgn.Tag("Date")),
            Event = await NamedRecords.FindOrCreateEventAsync(db, PgnValues.Text(pgn.Tag("Event"))),
            Site = PgnValues.Text(pgn.Tag("Site")),
            Round = PgnValues.Text(pgn.Tag("Round")),
            TimeControl = PgnValues.Text(pgn.Tag("TimeControl")),
            Notes = PgnValues.Text(pgn.Tag("Notes")),
            Tags = await NamedRecords.FindOrCreateTagsAsync(db, (pgn.Tag("Tags") ?? "").Split(';')),
            Eco = opening?.Eco,
            OpeningName = opening?.Name,
            CreatedAt = DateTime.UtcNow,
            MoveNodes = tree.Nodes,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    /// <summary>The Owner's colour when exactly one side matches the Owner's name or an Alias.</summary>
    public OwnerColour? OwnerColourOf(PgnGame pgn)
    {
        bool white = IsOwner(pgn.Tag("White")), black = IsOwner(pgn.Tag("Black"));
        return white == black ? null : white ? OwnerColour.White : OwnerColour.Black;
    }

    public static string OpponentName(PgnGame pgn, OwnerColour ownerColour) =>
        Games.GameEndpoints.Text(pgn.Tag(ownerColour == OwnerColour.White ? "Black" : "White")) ?? "Unknown";

    /// <summary>The Player with exactly this name (ignoring case), if any.</summary>
    public PlayerRecord? ExactPlayer(string name) =>
        _players.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>A Player whose name is not the same but plausibly the same person ("Huber T." / "Huber, Thomas").</summary>
    public PlayerRecord? SimilarPlayer(string name)
    {
        var parsed = PersonName.Parse(name);
        return _players.FirstOrDefault(p => parsed.Matches(PersonName.Parse(p.Name)));
    }

    public PlayerRecord NewPlayer(string name)
    {
        var player = new PlayerRecord { Name = name.Trim() };
        _players.Add(player);
        return player;
    }

    private bool IsOwner(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var parsed = PersonName.Parse(name);
        return _ownerNames.Any(owner => owner.Matches(parsed));
    }

    /// <summary>A Game with the same Players, date and moves already in the Collection, if any.</summary>
    private async Task<int?> FindDuplicateAsync(PgnGame pgn, TreeBuilder tree, OwnerColour ownerColour, string opponentName)
    {
        var date = PgnValues.ToGameDate(pgn.Tag("Date"));
        var opponent = PersonName.Parse(opponentName);
        var candidates = await db.Games.AsNoTracking().Include(g => g.Opponent)
            .Where(g => g.OwnerColour == ownerColour && g.Date == date)
            .ToListAsync();
        foreach (var candidate in candidates.Where(g => opponent.Matches(PersonName.Parse(g.Opponent.Name))))
        {
            var nodes = await db.MoveNodes.AsNoTracking().Where(n => n.GameId == candidate.Id).ToListAsync();
            if (new MoveTree(nodes).MainLine().Select(n => n.Uci).SequenceEqual(tree.MainLineUcis)) return candidate.Id;
        }
        return null;
    }

    /// <summary>Turns PGN moves into move tree nodes, checking every move against Chess Rules.</summary>
    public sealed class TreeBuilder
    {
        private readonly Dictionary<MoveNodeRecord, int> _childCounts = [];
        private int _rootCount;

        private TreeBuilder() { }

        public List<MoveNodeRecord> Nodes { get; } = [];
        public List<Position> MainLinePositions { get; } = [];
        public List<string> MainLineUcis { get; } = [];

        /// <summary>The tree, or null with the move that is not legal.</summary>
        public static TreeBuilder? Build(List<PgnMove> moves, out string? error)
        {
            var builder = new TreeBuilder();
            error = builder.AddLine(Position.Initial, null, moves, isMainLine: true);
            return error is null ? builder : null;
        }

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
                if (isMainLine)
                {
                    MainLinePositions.Add(position);
                    MainLineUcis.Add(node.Uci);
                }
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
