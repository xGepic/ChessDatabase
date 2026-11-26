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
/// <param name="Pgn">For a duplicate: its PGN, so it can be imported anyway.</param>
public sealed record ImportedGameDto(
    int Index, ImportStatus Status, string? Reason, string? White, string? Black, int? GameId, string? Pgn = null);

public sealed record ImportSummaryDto(
    int Imported, int NeedsReview, int Duplicates, int Failed, IReadOnlyList<ImportedGameDto> Games);

/// <summary>What makes a PGN entry the Owner's Game: their colour, the result and the Opponent.</summary>
public sealed record GameAssignment(OwnerColour OwnerColour, GameResult Result, PlayerRecord Opponent);

/// <summary>The outcome of resolving a held-back entry from the review list.</summary>
public abstract record ResolveOutcome
{
    public sealed record Added(int GameId) : ResolveOutcome;
    public sealed record NotFound : ResolveOutcome;
    public sealed record Invalid(string Field, string Message) : ResolveOutcome;
    public sealed record Duplicate(int ExistingGameId) : ResolveOutcome;
}

/// <summary>
/// Brings PGN entries into the Collection. Moves are replayed through Chess Rules (German piece
/// letters become English) and the Owner is recognised by name or Alias. An entry goes to the
/// review list instead when it has no result, no side matches the Owner, or its Opponent only
/// resembles an existing Player; it is reported as a duplicate when the Game is already there.
/// </summary>
public sealed class PgnImporter(CollectionDbContext db, OpeningClassifier openings)
{
    private List<PlayerRecord> _players = [];
    private List<PersonName> _ownerNames = [];

    public async Task<ImportSummaryDto> ImportAsync(string text, bool allowDuplicates = false)
    {
        await LoadPlayersAndOwnerAsync();
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

    /// <summary>The held-back entries with what is known about them and the Players they may be.</summary>
    public async Task<List<ReviewItemDto>> ReviewListAsync()
    {
        await LoadPlayersAndOwnerAsync();
        var items = await db.ImportReviewItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync();
        return items.Select(ToReviewItem).ToList();
    }

    /// <summary>Adds a held-back entry to the Collection with the Owner's choices.</summary>
    public async Task<ResolveOutcome> ResolveAsync(int itemId, ResolveReviewRequest choice)
    {
        var item = await db.ImportReviewItems.FindAsync(itemId);
        if (item is null) return new ResolveOutcome.NotFound();
        if (choice.OwnerColour is not { } ownerColour) return new ResolveOutcome.Invalid("ownerColour", "Choose the side you played.");
        if (!GameResults.TryParsePgn(choice.Result, out var result))
            return new ResolveOutcome.Invalid("result", "Choose the result: 1-0, 0-1 or 1/2-1/2.");

        await LoadPlayersAndOwnerAsync();
        var entry = PgnReader.Read(item.Pgn).FirstOrDefault()?.Entry;
        var tree = entry is null ? null : TreeBuilder.Build(entry.Moves, out _);
        if (entry is null || tree is null) return new ResolveOutcome.Invalid("id", "The held-back entry can no longer be read.");

        var opponentName = OpponentName(entry, ownerColour);
        PlayerRecord opponent;
        if (choice.OpponentPlayerId is { } playerId)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is null || !(Names.Same(player.Name, opponentName) || PersonName.SamePerson(player.Name, opponentName)))
                return new ResolveOutcome.Invalid("opponentPlayerId", $"That Player's name does not match \"{opponentName}\".");
            opponent = player;
        }
        else
        {
            opponent = ExactPlayer(opponentName) ?? NewPlayer(opponentName);
        }

        if (!choice.AllowDuplicate && await FindDuplicateAsync(entry, tree, ownerColour) is { } existingId)
            return new ResolveOutcome.Duplicate(existingId);

        var game = await CreateGameAsync(entry, tree, new GameAssignment(ownerColour, result, opponent));
        db.ImportReviewItems.Remove(item);
        await db.SaveChangesAsync();
        return new ResolveOutcome.Added(game.Id);
    }

    private async Task LoadPlayersAndOwnerAsync()
    {
        _players = await db.Players.ToListAsync();
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync();
        _ownerNames = [.. new[] { settings?.OwnerName ?? "" }.Concat(settings?.Aliases ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(PersonName.Parse)];
    }

    private async Task<ImportedGameDto> ImportOneAsync(PgnReadResult read, bool allowDuplicates)
    {
        var entry = read.Entry;
        string? white = entry?.Tag("White"), black = entry?.Tag("Black");
        ImportedGameDto Result(ImportStatus status, string? reason, int? gameId = null, string? source = null) =>
            new(read.Index, status, reason, white, black, gameId, source);

        if (entry is null) return Result(ImportStatus.Failed, read.Error);
        if (UnsupportedReason(entry) is { } unsupported) return Result(ImportStatus.Failed, unsupported);
        var tree = TreeBuilder.Build(entry.Moves, out var moveError);
        if (tree is null) return Result(ImportStatus.Failed, moveError);

        if (!allowDuplicates)
        {
            if (await db.ImportReviewItems.AnyAsync(i => i.Pgn == entry.Source))
                return Result(ImportStatus.Duplicate, "This Game is already waiting in the review list.", source: entry.Source);
            if (await FindDuplicateAsync(entry, tree, OwnerColourOf(entry)) is { } existingId)
                return Result(ImportStatus.Duplicate, "This Game is already in the Collection.", existingId, entry.Source);
        }

        var problems = new List<string>();
        var hasResult = GameResults.TryParsePgn(entry.Tag("Result") ?? entry.Termination, out var result);
        if (!hasResult) problems.Add("The Game has no result.");

        var ownerColour = OwnerColourOf(entry);
        PlayerRecord? opponent = null;
        if (ownerColour is null)
        {
            problems.Add(IsOwner(white) && IsOwner(black)
                ? "Both sides match your name or an Alias."
                : "Neither side matches your name or an Alias.");
        }
        else
        {
            var opponentName = OpponentName(entry, ownerColour.Value);
            opponent = ExactPlayer(opponentName);
            if (opponent is null && SimilarPlayer(opponentName) is { } similar)
                problems.Add($"The Opponent \"{opponentName}\" may be the same Player as \"{similar.Name}\".");
        }

        if (problems.Count > 0)
        {
            db.ImportReviewItems.Add(new ImportReviewItemRecord
            {
                Pgn = entry.Source,
                Reason = string.Join(' ', problems),
                White = white,
                Black = black,
                Date = GameDates.FromPgn(entry.Tag("Date")),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Result(ImportStatus.NeedsReview, string.Join(' ', problems));
        }

        var colour = ownerColour!.Value;
        var game = await CreateGameAsync(entry, tree,
            new GameAssignment(colour, result, opponent ?? NewPlayer(OpponentName(entry, colour))));
        return Result(ImportStatus.Imported, null, game.Id);
    }

    /// <summary>Why an entry can never become a Game, or null.</summary>
    private static string? UnsupportedReason(PgnEntry entry)
    {
        if (entry.Tag("SetUp") == "1" || entry.Tag("FEN") is not null)
            return "Games from a custom starting position are not supported.";
        if (PgnValues.OptionalTag(entry.Tag("White")) is null)
            return "Both players' names are needed, and the White player's name is missing.";
        if (PgnValues.OptionalTag(entry.Tag("Black")) is null)
            return "Both players' names are needed, and the Black player's name is missing.";
        return null;
    }

    /// <summary>Adds the Game to the Collection with its details from the PGN tags.</summary>
    private async Task<GameRecord> CreateGameAsync(PgnEntry entry, TreeBuilder tree, GameAssignment assignment)
    {
        var game = new GameRecord
        {
            OwnerColour = assignment.OwnerColour,
            Opponent = assignment.Opponent,
            Result = assignment.Result,
            WhiteElo = PgnValues.Elo(entry.Tag("WhiteElo")),
            BlackElo = PgnValues.Elo(entry.Tag("BlackElo")),
            Date = GameDates.FromPgn(entry.Tag("Date")),
            Event = await EventsAndTags.FindOrCreateEventAsync(db, PgnValues.OptionalTag(entry.Tag("Event"))),
            Site = PgnValues.OptionalTag(entry.Tag("Site")),
            Round = PgnValues.OptionalTag(entry.Tag("Round")),
            TimeControl = PgnValues.OptionalTag(entry.Tag("TimeControl")),
            Notes = PgnValues.OptionalTag(entry.Tag("Notes")),
            Tags = await EventsAndTags.FindOrCreateTagsAsync(db, (entry.Tag("Tags") ?? "").Split(';')),
            CreatedAt = DateTime.UtcNow,
            MoveNodes = tree.Nodes,
        };
        MainLine.Apply(game, tree.MainLineUcis, openings);
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private ReviewItemDto ToReviewItem(ImportReviewItemRecord item)
    {
        var entry = PgnReader.Read(item.Pgn).FirstOrDefault()?.Entry;
        var result = entry is not null && GameResults.TryParsePgn(entry.Tag("Result") ?? entry.Termination, out var r)
            ? r.ToPgn()
            : null;
        var ownerColour = entry is null ? null : OwnerColourOf(entry);

        // A suggestion is only useful for the Opponent's side; while that is unknown, offer both.
        var whiteSuggestion = ownerColour == OwnerColour.White ? null : Suggestion(item.White);
        var blackSuggestion = ownerColour == OwnerColour.Black ? null : Suggestion(item.Black);
        return new ReviewItemDto(item.Id, item.White, item.Black, item.Date, item.Reason, ownerColour, result,
            whiteSuggestion, blackSuggestion);
    }

    private Players.PlayerDto? Suggestion(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || ExactPlayer(name) is not null) return null;
        return SimilarPlayer(name) is { } player ? new Players.PlayerDto(player.Id, player.Name) : null;
    }

    /// <summary>The Owner's colour when exactly one side matches the Owner's name or an Alias.</summary>
    private OwnerColour? OwnerColourOf(PgnEntry entry)
    {
        bool white = IsOwner(entry.Tag("White")), black = IsOwner(entry.Tag("Black"));
        return white == black ? null : white ? OwnerColour.White : OwnerColour.Black;
    }

    private static string OpponentName(PgnEntry entry, OwnerColour ownerColour) =>
        PlayerSides.ToOwnerAndOpponent(ownerColour, entry.Tag("White"), entry.Tag("Black")).Opponent?.Trim() ?? "";

    /// <summary>The Player with exactly this name. Any other spelling, even in letter case, needs the Owner's confirmation.</summary>
    private PlayerRecord? ExactPlayer(string name) => _players.FirstOrDefault(p => p.Name == name.Trim());

    /// <summary>A Player whose name is not the same but plausibly the same person ("Huber T." / "Huber, Thomas").</summary>
    private PlayerRecord? SimilarPlayer(string name)
    {
        var parsed = PersonName.Parse(name);
        return _players.FirstOrDefault(p => parsed.Matches(PersonName.Parse(p.Name)));
    }

    private PlayerRecord NewPlayer(string name)
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

    /// <summary>
    /// A Game with the same Players, date and moves already in the Collection, if any. When the
    /// Owner's colour is not known yet, either assignment counts.
    /// </summary>
    private async Task<int?> FindDuplicateAsync(PgnEntry entry, TreeBuilder tree, OwnerColour? ownerColour)
    {
        var date = GameDates.FromPgn(entry.Tag("Date"));
        var candidates = await db.Games.AsNoTracking().Include(g => g.Opponent).Where(g => g.Date == date).ToListAsync();
        foreach (var candidate in candidates)
        {
            if (ownerColour is { } known && candidate.OwnerColour != known) continue;
            var opponent = PersonName.Parse(OpponentName(entry, candidate.OwnerColour));
            if (!opponent.Matches(PersonName.Parse(candidate.Opponent.Name))) continue;
            var nodes = await db.MoveNodes.AsNoTracking().Where(n => n.GameId == candidate.Id).ToListAsync();
            if (new MoveTree(nodes).MainLine().Select(n => n.Uci).SequenceEqual(tree.MainLineUcis)) return candidate.Id;
        }
        return null;
    }

    /// <summary>Turns PGN moves into move tree nodes, checking every move against Chess Rules.</summary>
    private sealed class TreeBuilder
    {
        private readonly Dictionary<MoveNodeRecord, int> _childCounts = [];
        private int _rootCount;

        private TreeBuilder() { }

        public List<MoveNodeRecord> Nodes { get; } = [];
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
