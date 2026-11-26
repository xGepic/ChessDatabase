using ChessDatabase.Api.Collection;

namespace ChessDatabase.Api.Training;

/// <summary>
/// The spaced-repetition scheduler (SM-2 style) shared by Repertoire Training and Mistake
/// Training. A right answer pushes the next review out: 1 day, then 6, then each interval times
/// the item's ease. A wrong answer starts the item over, lowers its ease and brings it back in a
/// minute, so it returns in the same session.
/// </summary>
public static class SpacedRepetition
{
    public const double StartEase = 2.5;
    public const double MinimumEase = 1.3;

    /// <summary>How soon a wrong answer comes back.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    // SM-2 grades: a right answer counts as 4 (ease unchanged), a wrong one as 2.
    private const int RightGrade = 4;
    private const int WrongGrade = 2;

    /// <summary>Records an answer on the item and schedules its next review.</summary>
    public static void Review(TrainingItemRecord item, bool right, DateTime now)
    {
        var grade = right ? RightGrade : WrongGrade;
        item.Ease = Math.Max(MinimumEase, item.Ease + 0.1 - (5 - grade) * (0.08 + (5 - grade) * 0.02));
        item.ReviewedAt = now;
        if (!right)
        {
            item.Repetitions = 0;
            item.IntervalDays = 0;
            item.DueAt = now + Retry;
            return;
        }
        item.Repetitions++;
        item.IntervalDays = item.Repetitions switch
        {
            1 => 1,
            2 => 6,
            _ => (int)Math.Round(item.IntervalDays * item.Ease, MidpointRounding.AwayFromZero),
        };
        item.DueAt = now.AddDays(item.IntervalDays);
    }

    /// <summary>The end of the local day <paramref name="clock"/> is in, in UTC: items due before it are due today.</summary>
    public static DateTime EndOfToday(TimeProvider clock)
    {
        var local = clock.GetLocalNow();
        var midnight = new DateTimeOffset(local.Date.AddDays(1), local.Offset);
        return midnight.UtcDateTime;
    }
}
