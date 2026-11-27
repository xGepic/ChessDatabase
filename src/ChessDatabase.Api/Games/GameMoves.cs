using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Openings;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChessDatabase.Api.Games;

/// <summary>Why a change to a Game's moves could not be made. Nothing is changed when there is one.</summary>
public enum MoveChangeProblem
{
    /// <summary>The move given is not part of this Game.</summary>
    NotInGame,

    /// <summary>A line needs at least one move.</summary>
    NoMoves,

    /// <summary>A move is not legal where it would be played (see <see cref="MoveChange.IllegalMove"/>).</summary>
    IllegalMove,

    /// <summary>The move is on the main line, where only a Variation was asked for.</summary>
    OnMainLine,
}

/// <param name="Problem">Null when the change was made.</param>
/// <param name="Last">For a line: its last move. A new move gets its id when the edit is saved.</param>
/// <param name="IllegalMove">The first move that is not legal, as it was given.</param>
/// <param name="Changed">Whether anything changed: a line that is already in the Game is only followed.</param>
public sealed record MoveChange(MoveChangeProblem? Problem, MoveNodeRecord? Last = null, string? IllegalMove = null, bool Changed = false)
{
    public static MoveChange Failed(MoveChangeProblem problem, string? illegalMove = null) => new(problem, IllegalMove: illegalMove);
}

/// <summary>
/// Game moves: the one way a Game's moves change. Every change keeps what is derived from the
/// main line (ECO code, opening name, position index) in step, in the same all-or-nothing save,
/// so no caller can forget it. Open a Game's moves, make one or more changes, save once
/// (<c>await using</c> the edit, so unsaved changes are dropped).
/// Comments and symbols don't touch the main line and are set on the moves directly.
/// </summary>
public sealed class GameMoves(CollectionDbContext db, OpeningClassifier openings)
{
    /// <summary>A Game's moves, ready to change; null when there is no such Game.</summary>
    public async Task<GameMovesEdit?> OpenAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var game = await db.Games.Include(g => g.MoveNodes).Include(g => g.Positions).AsSplitQuery()
            .SingleOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        return game is null ? null : new GameMovesEdit(db, openings, game);
    }

    /// <summary>
    /// Derives the data of a Game that is about to be added (created by hand, or imported with its
    /// moves), from its main line as UCI moves. It is saved with the Game.
    /// </summary>
    public void DeriveForNew(GameRecord game, IReadOnlyList<string> mainLine) => Derive(game, mainLine, openings);

    /// <summary>
    /// Derives the data of every Game that has none yet: Games stored before the position index
    /// existed. Runs at start-up after the migrations.
    /// </summary>
    public static void DeriveMissing(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var missing = db.Games.Where(g => !g.Positions.Any()).Select(g => g.Id).ToList();
        if (missing.Count == 0) return;

        var nodesByGame = db.MoveNodes.AsNoTracking().Where(n => missing.Contains(n.GameId)).ToList().ToLookup(n => n.GameId);
        foreach (var id in missing)
            foreach (var row in Walk(MainLineUcis(nodesByGame[id])).Rows)
            {
                row.GameId = id;
                db.PositionIndex.Add(row);
            }
        db.SaveChanges();
    }

    /// <summary>Sets the ECO code, opening name and position index for a main line. The Game's old index rows, if loaded, go.</summary>
    internal static void Derive(GameRecord game, IReadOnlyList<string> mainLine, OpeningClassifier openings)
    {
        var (positions, rows) = Walk(mainLine);
        var opening = openings.Classify(positions.Skip(1).ToList()); // the positions after each move
        game.Eco = opening?.Eco;
        game.OpeningName = opening?.Name;
        game.Positions = rows;
    }

    internal static List<string> MainLineUcis(IEnumerable<MoveNodeRecord> nodes) => [.. new MoveTree(nodes).MainLine().Select(n => n.Uci)];

    /// <summary>
    /// Plays a main line from the start: every position on it (the start first) and its position
    /// index rows, one per position.
    /// </summary>
    private static (List<Position> Positions, List<PositionIndexRecord> Rows) Walk(IReadOnlyList<string> mainLine)
    {
        var positions = new List<Position>(mainLine.Count + 1);
        var rows = new List<PositionIndexRecord>(mainLine.Count + 1);
        var position = Position.Initial;
        for (var ply = 0; ; ply++)
        {
            var uci = ply < mainLine.Count ? mainLine[ply] : null;
            positions.Add(position);
            rows.Add(new PositionIndexRecord { Ply = ply, Hash = PositionIndex.Key(position), Uci = uci });
            if (uci is null) return (positions, rows);
            position = position.Play(StoredMove(uci));
        }
    }

    private static Move StoredMove(string uci) =>
        Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException($"Stored move '{uci}' is not UCI.");
}

/// <summary>
/// One Game's moves, open for changes (see <see cref="GameMoves"/>). Every change is checked by
/// Chess Rules first and either made completely or not at all, and the next change sees the moves
/// as this one left them. Changes are written inside one transaction as they are made; only
/// <see cref="SaveAsync"/> keeps them, together with what follows from the new main line. Disposing
/// without saving drops them.
/// </summary>
public sealed class GameMovesEdit : IAsyncDisposable
{
    private readonly CollectionDbContext _db;
    private readonly OpeningClassifier _openings;
    private readonly GameRecord _game;
    private MoveTree _tree;
    private IDbContextTransaction? _transaction;

    internal GameMovesEdit(CollectionDbContext db, OpeningClassifier openings, GameRecord game)
    {
        _db = db;
        _openings = openings;
        _game = game;
        _tree = new MoveTree(game.MoveNodes);
    }

    /// <summary>A move of the Game, e.g. to set its symbols; null when it isn't part of it.</summary>
    public MoveNodeRecord? Node(int nodeId) => _tree.Contains(nodeId) ? _tree.Node(nodeId) : null;

    /// <summary>
    /// Plays moves (UCI or SAN, German piece letters too) one after another from the parent (null:
    /// the start). Where a move is already there, it is followed; the first new move starts a new
    /// line, a Variation if the position already had a continuation.
    /// </summary>
    public async Task<MoveChange> AddLineAsync(int? parentId, IReadOnlyList<string?> moves, CancellationToken cancellationToken = default)
    {
        if (parentId is { } id && !_tree.Contains(id)) return MoveChange.Failed(MoveChangeProblem.NotInGame);
        if (moves.Count == 0) return MoveChange.Failed(MoveChangeProblem.NoMoves);
        if (MoveReading.ReadLine(_tree.PositionAfter(parentId), moves, out var line) is { } illegal)
            return MoveChange.Failed(MoveChangeProblem.IllegalMove, illegal);

        var (last, added) = _tree.AddLine(parentId, line, m =>
        {
            var node = new MoveNodeRecord { GameId = _game.Id, ParentId = m.ParentId, Parent = m.Parent, Order = m.Order, Uci = m.Uci, San = m.San };
            _game.MoveNodes.Add(node);
            return node;
        });
        if (added) await WriteAsync(cancellationToken);
        return new MoveChange(null, last, Changed: added);
    }

    /// <summary>Makes the move's line the main line. A move already on it changes nothing.</summary>
    public async Task<MoveChange> PromoteAsync(int nodeId, CancellationToken cancellationToken = default)
    {
        if (!_tree.Contains(nodeId)) return MoveChange.Failed(MoveChangeProblem.NotInGame);
        if (_tree.VariationStart(nodeId) is null) return new MoveChange(null);
        _tree.PromoteToMainLine(nodeId);
        await WriteAsync(cancellationToken);
        return new MoveChange(null, Changed: true);
    }

    /// <summary>Deletes the whole Variation the move is in (from its first move on).</summary>
    public async Task<MoveChange> DeleteVariationAsync(int nodeId, CancellationToken cancellationToken = default)
    {
        if (!_tree.Contains(nodeId)) return MoveChange.Failed(MoveChangeProblem.NotInGame);
        if (_tree.VariationStart(nodeId) is not { } start) return MoveChange.Failed(MoveChangeProblem.OnMainLine);
        return await DeleteFromAsync(start, cancellationToken);
    }

    /// <summary>
    /// Deletes the move and everything after it. If it was the main continuation, the next
    /// Variation at that point (if any) becomes the main line.
    /// </summary>
    public async Task<MoveChange> DeleteFromAsync(int nodeId, CancellationToken cancellationToken = default)
    {
        if (!_tree.Contains(nodeId)) return MoveChange.Failed(MoveChangeProblem.NotInGame);
        foreach (var node in _tree.Subtree(nodeId).Select(_tree.Node).ToList())
        {
            _db.MoveNodes.Remove(node);
            _game.MoveNodes.Remove(node);
        }
        await WriteAsync(cancellationToken);
        return new MoveChange(null, Changed: true);
    }

    /// <summary>
    /// Keeps the changes and stores what follows from the new main line with them, all or nothing.
    /// Comments and symbols set on moves of this edit are saved too.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            await _db.SaveChangesAsync(cancellationToken); // symbols or comments only: the main line is untouched
            return;
        }
        GameMoves.Derive(_game, GameMoves.MainLineUcis(_game.MoveNodes), _openings);
        await _db.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    /// <summary>Writes the change inside the edit's transaction, so new moves get their ids, and reads the tree afresh.</summary>
    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        _transaction ??= await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _tree = new MoveTree(_game.MoveNodes);
    }

    /// <summary>Drops whatever was not saved.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_transaction is null) return;
        await _transaction.DisposeAsync(); // rolls back
        _transaction = null;
        _db.ChangeTracker.Clear();
    }
}
