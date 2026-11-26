namespace ChessDatabase.Api.Assessment;

/// <summary>Lichess's Accuracy: per move from the winning chance kept, per side a volatility-weighted mean.</summary>
public static class Accuracy
{
    /// <summary>Accuracy of one move, from the mover's winning chance (percent) before and after it.</summary>
    public static double OfMove(double before, double after)
    {
        if (after >= before) return 100;
        var raw = 103.1668100711649 * Math.Exp(-0.04354415386753951 * (before - after)) - 3.166924740191411;
        return Math.Clamp(raw + 1, 0, 100);
    }

    /// <summary>
    /// Accuracy per side, from White's winning chance in every position of the Game: the mean of
    /// the weighted mean (weights from how volatile the Game was around each move) and the harmonic
    /// mean of the move accuracies. Null for a side that made no move.
    /// </summary>
    public static (double? White, double? Black) OfGame(IReadOnlyList<double> whitePercents)
    {
        var moves = whitePercents.Count - 1;
        if (moves < 1) return (null, null);
        var windowSize = Math.Clamp(moves / 10, 2, 8);
        var windows = Enumerable.Repeat(whitePercents.Take(windowSize).ToList(), Math.Min(windowSize, whitePercents.Count) - 2)
            .Concat(Sliding(whitePercents, windowSize))
            .ToList();

        List<(double Accuracy, double Weight)> white = [], black = [];
        for (var i = 0; i < moves; i++)
        {
            var weight = Math.Clamp(StandardDeviation(windows[i]), 0.5, 12);
            var (before, after) = (whitePercents[i], whitePercents[i + 1]);
            if (WinningChances.WhiteMoved(i)) white.Add((OfMove(before, after), weight));
            else black.Add((OfMove(100 - before, 100 - after), weight));
        }
        return (Combine(white), Combine(black));
    }

    private static double? Combine(List<(double Accuracy, double Weight)> moves)
    {
        if (moves.Count == 0) return null;
        var weighted = moves.Sum(m => m.Accuracy * m.Weight) / moves.Sum(m => m.Weight);
        var harmonic = moves.Count / moves.Sum(m => 1 / Math.Max(1, m.Accuracy));
        return (weighted + harmonic) / 2;
    }

    private static IEnumerable<List<double>> Sliding(IReadOnlyList<double> values, int size)
    {
        if (values.Count <= size) { yield return [.. values]; yield break; }
        for (var i = 0; i + size <= values.Count; i++) yield return [.. values.Skip(i).Take(size)];
    }

    private static double StandardDeviation(List<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }
}
