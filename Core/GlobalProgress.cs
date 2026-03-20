namespace LustsDepotDownloaderPro.Core;

/// <summary>
/// Thread-safe progress tracker with rolling-window speed and ETA.
/// Speed uses a 5-second sliding window so it reflects current throughput,
/// not the average since boot.
/// </summary>
public class GlobalProgress
{
    private long _downloaded;
    private long _total;
    private readonly DateTime _startTime = DateTime.UtcNow;

    // Rolling window: queue of (timestamp, cumulativeBytes) samples kept for 5 s
    private readonly Queue<(DateTime t, long bytes)> _window = new();
    private readonly object _windowLock = new();

    public void AddTotal(long bytes) => Interlocked.Add(ref _total, bytes);

    public void ReportProgress(long bytes)
    {
        var now       = DateTime.UtcNow;
        var cumul     = Interlocked.Add(ref _downloaded, bytes);
        lock (_windowLock)
        {
            _window.Enqueue((now, cumul));
            // Evict samples older than 5 seconds
            while (_window.Count > 1 && (now - _window.Peek().t).TotalSeconds > 5)
                _window.Dequeue();
        }
    }

    public ProgressSnapshot GetSnapshot()
    {
        var now        = DateTime.UtcNow;
        var downloaded = Interlocked.Read(ref _downloaded);
        var total      = Interlocked.Read(ref _total);
        var elapsed    = (now - _startTime).TotalSeconds;

        // Current speed from rolling window
        double speedMBps = 0;
        lock (_windowLock)
        {
            if (_window.Count >= 2)
            {
                var oldest = _window.Peek();
                double windowSec  = (now - oldest.t).TotalSeconds;
                long   windowBytes = downloaded - oldest.bytes;
                if (windowSec > 0) speedMBps = (windowBytes / 1_048_576.0) / windowSec;
            }
            else if (elapsed > 0)
            {
                // Fallback while window is still warming up
                speedMBps = (downloaded / 1_048_576.0) / elapsed;
            }
        }

        // ETA
        double etaSeconds = 0;
        if (speedMBps > 0 && total > downloaded)
        {
            double remainingMB = (total - downloaded) / 1_048_576.0;
            etaSeconds = remainingMB / speedMBps;
        }

        return new ProgressSnapshot
        {
            DownloadedMB   = downloaded / 1_048_576.0,
            TotalMB        = total      / 1_048_576.0,
            Percent        = total == 0 ? 0 : downloaded * 100.0 / total,
            SpeedMBps      = speedMBps,
            EtaSeconds     = etaSeconds,
            ElapsedSeconds = elapsed
        };
    }
}

public class ProgressSnapshot
{
    public double DownloadedMB   { get; set; }
    public double TotalMB        { get; set; }
    public double Percent        { get; set; }
    public double SpeedMBps      { get; set; }
    public double EtaSeconds     { get; set; }
    public double ElapsedSeconds { get; set; }
}
