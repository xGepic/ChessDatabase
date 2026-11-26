using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Collection;

/// <summary>The Collection: the single set of all the Owner's Games, stored in SQLite.</summary>
public sealed class CollectionDbContext(DbContextOptions<CollectionDbContext> options) : DbContext(options)
{
    public DbSet<GameRecord> Games => Set<GameRecord>();
    public DbSet<MoveNodeRecord> MoveNodes => Set<MoveNodeRecord>();
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    public DbSet<SettingsRecord> Settings => Set<SettingsRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SettingsRecord>().Property(s => s.OwnerName).HasMaxLength(200);
        model.Entity<PlayerRecord>().Property(p => p.Name).HasMaxLength(200);

        model.Entity<GameRecord>(game =>
        {
            game.Property(g => g.OwnerColor).HasConversion<string>().HasMaxLength(5);
            game.Property(g => g.Result).HasMaxLength(7);
            game.Property(g => g.Date).HasMaxLength(10);
            game.HasOne(g => g.Opponent).WithMany().HasForeignKey(g => g.OpponentId).OnDelete(DeleteBehavior.Restrict);
            game.HasMany(g => g.MoveNodes).WithOne().HasForeignKey(n => n.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MoveNodeRecord>(node =>
        {
            node.Property(n => n.Uci).HasMaxLength(5);
            node.Property(n => n.San).HasMaxLength(10);
            node.Property(n => n.Nags).HasMaxLength(100);
            node.HasOne<MoveNodeRecord>().WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.Cascade);
            node.HasIndex(n => new { n.GameId, n.ParentId, n.Order });
        });
    }
}

public enum OwnerColor
{
    White,
    Black,
}

/// <summary>A Game the Owner played. The Owner's side is implied by <see cref="OwnerColor"/>.</summary>
public sealed class GameRecord
{
    public int Id { get; set; }
    public OwnerColor OwnerColor { get; set; }
    public int OpponentId { get; set; }
    public PlayerRecord Opponent { get; set; } = null!;

    /// <summary>Null means unrated.</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Null means unrated.</summary>
    public int? BlackElo { get; set; }

    /// <summary>"1-0", "0-1" or "1/2-1/2", as in PGN. Never empty: every Game in the Collection is complete.</summary>
    public string Result { get; set; } = "";

    /// <summary>"2024", "2024-03" or "2024-03-15"; partial dates are allowed, as in PGN.</summary>
    public string? Date { get; set; }

    public DateTime CreatedAt { get; set; }
    public List<MoveNodeRecord> MoveNodes { get; set; } = [];
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

    public int Order { get; set; }
    public string Uci { get; set; } = "";
    public string San { get; set; } = "";
    public string? CommentBefore { get; set; }
    public string? CommentAfter { get; set; }

    /// <summary>Numeric Annotation Glyphs, space separated (e.g. "1 14").</summary>
    public string Nags { get; set; } = "";
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
}
