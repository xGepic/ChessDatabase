using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Collection;

/// <summary>The Collection: the single set of all the Owner's Games, stored in SQLite.</summary>
public sealed class CollectionDbContext(DbContextOptions<CollectionDbContext> options) : DbContext(options)
{
    public DbSet<GameRecord> Games => Set<GameRecord>();
    public DbSet<MoveNodeRecord> MoveNodes => Set<MoveNodeRecord>();
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<TagRecord> Tags => Set<TagRecord>();
    public DbSet<SettingsRecord> Settings => Set<SettingsRecord>();
    public DbSet<ImportReviewItemRecord> ImportReviewItems => Set<ImportReviewItemRecord>();
    public DbSet<EngineRecord> Engines => Set<EngineRecord>();
    public DbSet<EngineAssessmentRecord> EngineAssessments => Set<EngineAssessmentRecord>();
    public DbSet<PositionIndexRecord> PositionIndex => Set<PositionIndexRecord>();
    public DbSet<RepertoireNodeRecord> RepertoireNodes => Set<RepertoireNodeRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SettingsRecord>().Property(s => s.OwnerName).HasMaxLength(200);
        model.Entity<PlayerRecord>(player =>
        {
            player.Property(p => p.Name).HasMaxLength(200);
            player.Property(p => p.FideId).HasMaxLength(PlayerRecord.MaxFideIdLength);
            player.Property(p => p.Club).HasMaxLength(PlayerRecord.MaxClubLength);
        });
        model.Entity<EventRecord>().Property(e => e.Name).HasMaxLength(GameDetailLimits.Event);
        model.Entity<TagRecord>().Property(t => t.Name).HasMaxLength(GameDetailLimits.Tag);

        model.Entity<GameRecord>(game =>
        {
            game.Property(g => g.OwnerColour).HasConversion<string>().HasMaxLength(5);
            game.Property(g => g.Result).HasConversion(r => r.ToPgn(), text => GameResults.ParsePgn(text)).HasMaxLength(7);
            game.Property(g => g.Date).HasMaxLength(10);
            game.Property(g => g.Site).HasMaxLength(GameDetailLimits.Site);
            game.Property(g => g.Round).HasMaxLength(GameDetailLimits.Round);
            game.Property(g => g.TimeControl).HasMaxLength(GameDetailLimits.TimeControl);
            game.Property(g => g.Notes).HasMaxLength(GameDetailLimits.Notes);
            game.Property(g => g.Eco).HasMaxLength(3);
            game.Property(g => g.OpeningName).HasMaxLength(200);
            game.HasOne(g => g.Event).WithMany().HasForeignKey(g => g.EventId).OnDelete(DeleteBehavior.SetNull);
            game.HasMany(g => g.Tags).WithMany();
            game.HasOne(g => g.Opponent).WithMany().HasForeignKey(g => g.OpponentId).OnDelete(DeleteBehavior.Restrict);
            game.HasMany(g => g.MoveNodes).WithOne().HasForeignKey(n => n.GameId).OnDelete(DeleteBehavior.Cascade);
            game.HasMany(g => g.Positions).WithOne().HasForeignKey(p => p.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<EngineRecord>(engine =>
        {
            engine.Property(e => e.Name).HasMaxLength(100);
            engine.Property(e => e.Path).HasMaxLength(1000);
            engine.Property(e => e.Arguments).HasMaxLength(2000);
            engine.Property(e => e.ReportedName).HasMaxLength(200);
            engine.HasData(EngineRecord.Stockfish());
        });

        model.Entity<RepertoireNodeRecord>(node =>
        {
            node.Property(n => n.Side).HasConversion<string>().HasMaxLength(5);
            node.Property(n => n.Uci).HasMaxLength(5);
            node.Property(n => n.San).HasMaxLength(10);
            node.Property(n => n.Comment).HasMaxLength(GameDetailLimits.Notes);
            node.HasOne(n => n.Parent).WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.Cascade);
            node.HasIndex(n => new { n.Side, n.ParentId, n.Order });
        });

        model.Entity<PositionIndexRecord>(position =>
        {
            position.Property(p => p.Uci).HasMaxLength(5);
            position.HasIndex(p => p.Hash);
            position.HasIndex(p => new { p.GameId, p.Ply }).IsUnique();
        });

        model.Entity<EngineAssessmentRecord>(assessment =>
        {
            assessment.HasOne<GameRecord>().WithOne().HasForeignKey<EngineAssessmentRecord>(a => a.GameId).OnDelete(DeleteBehavior.Cascade);
            assessment.Property(a => a.EngineName).HasMaxLength(200);
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            assessment.Property(a => a.Positions).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, json),
                v => System.Text.Json.JsonSerializer.Deserialize<List<StoredEvaluation>>(v, json) ?? new List<StoredEvaluation>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<StoredEvaluation>>(
                    (a, b) => a!.SequenceEqual(b!), v => v.Count, v => v.ToList()));
        });

        model.Entity<MoveNodeRecord>(node =>
        {
            node.Property(n => n.Uci).HasMaxLength(5);
            node.Property(n => n.San).HasMaxLength(10);
            node.HasOne(n => n.Parent).WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.Cascade);
            node.HasIndex(n => new { n.GameId, n.ParentId, n.Order });
        });
    }
}

public enum OwnerColour
{
    White,
    Black,
}

/// <summary>A Game the Owner played. The Owner's side is implied by <see cref="OwnerColour"/>.</summary>
public sealed class GameRecord
{
    public int Id { get; set; }
    public OwnerColour OwnerColour { get; set; }
    public int OpponentId { get; set; }
    public PlayerRecord Opponent { get; set; } = null!;

    /// <summary>Null means unrated.</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Null means unrated.</summary>
    public int? BlackElo { get; set; }

    /// <summary>Every Game in the Collection is complete, so it always has a result. Stored as PGN text.</summary>
    public GameResult Result { get; set; }

    /// <summary>"2024", "2024-03" or "2024-03-15"; partial dates are allowed, as in PGN.</summary>
    public string? Date { get; set; }

    public int? EventId { get; set; }
    public EventRecord? Event { get; set; }
    public string? Site { get; set; }
    public string? Round { get; set; }
    public string? TimeControl { get; set; }

    /// <summary>The Owner's private notes on the Game (not move commentary).</summary>
    public string? Notes { get; set; }

    /// <summary>Derived from the main line by Opening Classification; never entered by hand.</summary>
    public string? Eco { get; set; }

    public string? OpeningName { get; set; }

    public DateTime CreatedAt { get; set; }
    public List<MoveNodeRecord> MoveNodes { get; set; } = [];
    public List<TagRecord> Tags { get; set; } = [];

    /// <summary>The Game's main-line positions, kept in step with the move tree (see <see cref="PositionIndexRecord"/>).</summary>
    public List<PositionIndexRecord> Positions { get; set; } = [];

    /// <summary>The Owner's own Elo in this Game (null when unrated).</summary>
    public int? OwnerElo() => OwnerColour == OwnerColour.White ? WhiteElo : BlackElo;

    /// <summary>The Opponent's Elo in this Game (null when unrated).</summary>
    public int? OpponentElo() => OwnerColour == OwnerColour.White ? BlackElo : WhiteElo;
}

/// <summary>
/// One move of the Owner's Repertoire for one colour: what the Owner intends to play, as a move
/// tree with the same Variation behaviour as a Game's (the first child is the main continuation).
/// </summary>
public sealed class RepertoireNodeRecord : ITreeNode
{
    public int Id { get; set; }

    /// <summary>Whose Repertoire: the lines the Owner plays with White, or with Black.</summary>
    public OwnerColour Side { get; set; }

    public int? ParentId { get; set; }

    /// <summary>Set when a new line is added in one go, so ids are assigned on save.</summary>
    public RepertoireNodeRecord? Parent { get; set; }

    public int Order { get; set; }
    public string Uci { get; set; } = "";
    public string San { get; set; } = "";
    public string? Comment { get; set; }
}

/// <summary>
/// The position index: one row per main-line position of a Game, the starting position and the
/// final one included. Variations are never indexed. It drives the Opening Tree and Position Search,
/// and is rebuilt whenever a Game's main line changes.
/// </summary>
public sealed class PositionIndexRecord
{
    public int Id { get; set; }
    public int GameId { get; set; }

    /// <summary>Half-moves played to reach the position: 0 is the starting position.</summary>
    public int Ply { get; set; }

    /// <summary>The position's Zobrist hash (Chess Rules), stored as a signed 64-bit number.</summary>
    public long Hash { get; set; }

    /// <summary>The move played from the position in UCI; null at the end of the main line.</summary>
    public string? Uci { get; set; }
}

/// <summary>The tournament, league, match or occasion a Game was played in.</summary>
public sealed class EventRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A free-form label the Owner attaches to Games.</summary>
public sealed class TagRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Maximum lengths of the free-text Game details.</summary>
public static class GameDetailLimits
{
    public const int Event = 200;
    public const int Site = 200;
    public const int Round = 20;
    public const int TimeControl = 50;
    public const int Notes = 10_000;
    public const int Tag = 100;
}

/// <summary>
/// One move in a Game's move tree. Children are ordered by <see cref="Order"/>; the first child is
/// the main continuation and later children are Variations.
/// </summary>
public sealed class MoveNodeRecord : ITreeNode
{
    public int Id { get; set; }
    public int GameId { get; set; }

    /// <summary>Null for the first move of the Game (or of a Variation on the first move).</summary>
    public int? ParentId { get; set; }

    /// <summary>Set when building a tree in one go (PGN import), so ids are assigned on save.</summary>
    public MoveNodeRecord? Parent { get; set; }

    public int Order { get; set; }
    public string Uci { get; set; } = "";
    public string San { get; set; } = "";
    public string? CommentBefore { get; set; }
    public string? CommentAfter { get; set; }

    /// <summary>Numeric Annotation Glyphs, e.g. [1, 14] for "!" and "⩲". Stored as a JSON array.</summary>
    public List<int> Nags { get; set; } = [];
}

/// <summary>A person the Owner has played against. The Owner is never a Player row.</summary>
public sealed class PlayerRecord
{
    public const int MaxFideIdLength = 10, MaxClubLength = 100;

    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The Player's FIDE ID (digits), if the Owner recorded it.</summary>
    public string? FideId { get; set; }

    public string? Club { get; set; }
}

/// <summary>The Owner's settings. There is exactly one row.</summary>
public sealed class SettingsRecord
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public string OwnerName { get; set; } = "";

    /// <summary>Other spellings of the Owner's name (stored as a JSON array).</summary>
    public List<string> Aliases { get; set; } = [];

    /// <summary>Where daily backups go; null means "Backups" beside the Collection's file.</summary>
    public string? BackupFolder { get; set; }

}

/// <summary>
/// An Engine definition: a UCI program and the options it runs with. Several can exist; only
/// Stockfish ships, and it starts without a path until the Owner sets one.
/// </summary>
public sealed class EngineRecord
{
    public const int StockfishId = 1;
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The executable; empty until set.</summary>
    public string Path { get; set; } = "";

    /// <summary>Command-line arguments, if the program needs any (the tests' fake engine does).</summary>
    public string? Arguments { get; set; }

    public int Threads { get; set; }
    public int HashMb { get; set; }

    /// <summary>Lines shown in Interactive Analysis. Game Analysis always uses one.</summary>
    public int MultiPv { get; set; }

    /// <summary>Interactive Analysis stops searching after this many seconds; null means no limit.</summary>
    public int? InteractiveSecondsLimit { get; set; }

    /// <summary>Game Analysis time per position, in milliseconds.</summary>
    public int AnalysisMsPerMove { get; set; }

    /// <summary>What the program called itself when the path was saved ("id name"), e.g. "Stockfish 17.1".</summary>
    public string? ReportedName { get; set; }

    /// <summary>The shipped definition, with the Interactive Analysis defaults.</summary>
    public static EngineRecord Stockfish() =>
        new() { Id = StockfishId, Name = "Stockfish", Path = "", Threads = 16, HashMb = 8192, MultiPv = 3, AnalysisMsPerMove = 2000 };
}

/// <summary>
/// The Engine's verdict on a Game's main line (ADR 0003: kept apart from the Owner's Annotations).
/// Only the raw evaluations are stored; classifications and Accuracy are worked out from them when
/// read. Re-running Game Analysis replaces it.
/// </summary>
public sealed class EngineAssessmentRecord
{
    public int Id { get; set; }
    public int GameId { get; set; }

    /// <summary>What the Engine called itself, version included, e.g. "Stockfish 17.1".</summary>
    public string EngineName { get; set; } = "";
    public int Threads { get; set; }
    public int HashMb { get; set; }
    public int MsPerMove { get; set; }
    public DateTime AnalysedAt { get; set; }

    /// <summary>The main line that was analysed, in UCI (the Game may have changed since).</summary>
    public List<string> MainLine { get; set; } = [];

    /// <summary>One per position of that line, the starting position first.</summary>
    public List<StoredEvaluation> Positions { get; set; } = [];
}

/// <summary>The Engine's score (from the side to move, as UCI reports it) and best line (UCI).</summary>
public sealed record StoredEvaluation(int? Cp, int? Mate, List<string> Line);

/// <summary>
/// A PGN entry held back from the Collection until the Owner resolves it: no side matched the Owner,
/// the result was missing, or the Opponent only resembles an existing Player. Kept as its PGN text;
/// it becomes a Game when resolved.
/// </summary>
public sealed class ImportReviewItemRecord
{
    public int Id { get; set; }
    public string Pgn { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Date { get; set; }
    public DateTime CreatedAt { get; set; }
}
