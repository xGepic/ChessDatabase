namespace ChessDatabase.Api.Research;

/// <summary>
/// A Tablebase verdict for one side, best first. A cursed win and a blessed loss are draws with
/// the 50-move rule; the "maybe" verdicts are wins or losses the tablebase can't time exactly.
/// </summary>
public enum TablebaseVerdict
{
    Win,
    CursedWin,
    MaybeWin,
    Draw,
    Unknown,
    MaybeLoss,
    BlessedLoss,
    Loss,
}

public static class TablebaseVerdicts
{
    /// <summary>
    /// Reads Lichess's category. "syzygy-win" and "syzygy-loss" are wins and losses known from
    /// Syzygy alone (no distance to mate); categories Lichess may add later read as unknown.
    /// </summary>
    public static TablebaseVerdict Parse(string? category) => category switch
    {
        "win" or "syzygy-win" => TablebaseVerdict.Win,
        "cursed-win" => TablebaseVerdict.CursedWin,
        "maybe-win" => TablebaseVerdict.MaybeWin,
        "draw" => TablebaseVerdict.Draw,
        "maybe-loss" => TablebaseVerdict.MaybeLoss,
        "blessed-loss" => TablebaseVerdict.BlessedLoss,
        "loss" or "syzygy-loss" => TablebaseVerdict.Loss,
        _ => TablebaseVerdict.Unknown,
    };

    /// <summary>The same result seen from the other side.</summary>
    public static TablebaseVerdict Opposite(this TablebaseVerdict verdict) => verdict switch
    {
        TablebaseVerdict.Win => TablebaseVerdict.Loss,
        TablebaseVerdict.CursedWin => TablebaseVerdict.BlessedLoss,
        TablebaseVerdict.MaybeWin => TablebaseVerdict.MaybeLoss,
        TablebaseVerdict.MaybeLoss => TablebaseVerdict.MaybeWin,
        TablebaseVerdict.BlessedLoss => TablebaseVerdict.CursedWin,
        TablebaseVerdict.Loss => TablebaseVerdict.Win,
        _ => verdict,
    };

    /// <summary>1 when shorter distances are better (winning), −1 when longer are (losing), 0 otherwise.</summary>
    public static int Direction(this TablebaseVerdict verdict) => verdict switch
    {
        TablebaseVerdict.Win or TablebaseVerdict.CursedWin or TablebaseVerdict.MaybeWin => 1,
        TablebaseVerdict.Loss or TablebaseVerdict.BlessedLoss or TablebaseVerdict.MaybeLoss => -1,
        _ => 0,
    };
}
