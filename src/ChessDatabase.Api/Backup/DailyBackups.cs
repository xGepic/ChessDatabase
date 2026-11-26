namespace ChessDatabase.Api.Backup;

/// <summary>
/// Backs the Collection up on the first start of each day, then every 24 hours while the app runs.
/// Turned off with the setting Backup:Daily = false (the tests do this unless they test it).
/// </summary>
public sealed class DailyBackups(CollectionBackup backup, TimeProvider clock, IConfiguration configuration, ILogger<DailyBackups> log)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);
    private ITimer? _timer;

    /// <summary>When the waiting timer fires; null until the first wait. Tests wait for it before moving the clock.</summary>
    internal DateTimeOffset? NextDue { get; private set; }

    /// <summary>The start-of-day backup happens before the app takes requests, so it is never skipped.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Backup:Daily", defaultValue: true)) return;
        var last = (await backup.StatusAsync()).LastBackupAt;
        if (last is null || last.Value.Date != clock.GetLocalNow().Date)
            await TryBackUpAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastAttempt = clock.GetLocalNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            var last = (await backup.StatusAsync()).LastBackupAt;
            var due = last is null ? lastAttempt + RetryAfterFailure : last.Value + Interval;
            var wait = due - clock.GetLocalNow();
            if (wait > TimeSpan.Zero) await DelayAsync(wait, stoppingToken);
            lastAttempt = clock.GetLocalNow();
            if (!await TryBackUpAsync(stoppingToken)) await DelayAsync(RetryAfterFailure, stoppingToken);
        }
    }


    private async Task DelayAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _timer?.Dispose();
        _timer = clock.CreateTimer(_ => elapsed.TrySetResult(), null, wait, Timeout.InfiniteTimeSpan);
        NextDue = clock.GetLocalNow() + wait;
        await elapsed.Task.WaitAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    private async Task<bool> TryBackUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await backup.BackUpAsync(cancellationToken);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // A missing drive must not stop the app; it is tried again later or on "Back up now".
            log.LogWarning(e, "Backup failed");
            return false;
        }
    }
}
