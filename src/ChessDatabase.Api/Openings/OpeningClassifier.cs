using ChessDatabase.Rules;

namespace ChessDatabase.Api.Openings;

public sealed record Opening(string Eco, string Name);

/// <summary>
/// Opening Classification: maps a move sequence to an ECO code and opening name using the bundled
/// Lichess chess-openings dataset (CC0, see Openings/Data/README.md). Positions are matched by
/// hash, so transpositions are recognised; the deepest matching position wins.
/// </summary>
public sealed class OpeningClassifier
{
    // Built once per process: the dataset never changes while the app runs.
    private static readonly Lazy<(Dictionary<ulong, Opening> Named, HashSet<ulong> Book)> Index = new(BuildIndex);

    /// <summary>The positions a dataset line ends in, with that line's name.</summary>
    private static Dictionary<ulong, Opening> ByPosition => Index.Value.Named;

    /// <summary>Every position along any dataset line: known opening theory.</summary>
    private static HashSet<ulong> Book => Index.Value.Book;

    private static (Dictionary<ulong, Opening>, HashSet<ulong>) BuildIndex()
    {
        var index = new Dictionary<ulong, Opening>();
        var book = new HashSet<ulong>();
        var assembly = typeof(OpeningClassifier).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".tsv")).Order())
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            reader.ReadLine(); // header: eco, name, pgn
            while (reader.ReadLine() is { } line)
            {
                var columns = line.Split('\t');
                if (columns.Length != 3) continue;
                var position = Replay(columns[2], book);
                // The first line that reaches a position names it; later transpositions keep that name.
                index.TryAdd(position.Hash, new Opening(columns[0], columns[1]));
            }
        }
        return (index, book);
    }

    /// <summary>The opening of the deepest position along the sequence that the dataset knows, or null.</summary>
    public Opening? Classify(IEnumerable<Position> positionsAfterEachMove)
    {
        Opening? found = null;
        foreach (var position in positionsAfterEachMove)
            if (ByPosition.TryGetValue(position.Hash, out var opening)) found = opening;
        return found;
    }

    /// <summary>
    /// How many moves from the start stay in book: each of them leads to a position on some dataset
    /// line. Counting stops at the first move that leaves it, even if a later one transposes back.
    /// </summary>
    public static int BookMoves(IEnumerable<Position> positionsAfterEachMove) =>
        positionsAfterEachMove.TakeWhile(p => Book.Contains(p.Hash)).Count();

    /// <summary>Plays a dataset line and adds every position along it to <paramref name="book"/>.</summary>
    private static Position Replay(string pgnMoves, HashSet<ulong> book)
    {
        var position = Position.Initial;
        foreach (var token in pgnMoves.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.EndsWith('.')) continue; // move number
            position = position.Play(position.ParseSan(token));
            book.Add(position.Hash);
        }
        return position;
    }
}
