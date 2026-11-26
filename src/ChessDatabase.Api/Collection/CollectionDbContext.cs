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

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SettingsRecord>().Property(s => s.OwnerName).HasMaxLength(200);
        model.Entity<PlayerRecord>().Property(p => p.Name).HasMaxLength(200);
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
public sealed class MoveNodeRecord
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
    public int Id { get; set; }
    public string Name { get; set; } = "";
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
