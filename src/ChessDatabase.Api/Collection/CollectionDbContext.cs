using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Collection;

/// <summary>The Collection: the single set of all the Owner's Games, stored in SQLite.</summary>
public sealed class CollectionDbContext(DbContextOptions<CollectionDbContext> options) : DbContext(options)
{
    public DbSet<GameRecord> Games => Set<GameRecord>();
}

public sealed class GameRecord
{
    public int Id { get; set; }
}
