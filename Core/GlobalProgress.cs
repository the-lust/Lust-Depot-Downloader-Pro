namespace LustsDepotDownloaderPro.Core;

public class GlobalProgress
{
    private long _downloaded;
    private long _total;
    private readonly object _lock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    public void AddTotal(long bytes)
    {
        Interlocked.Add(ref _total, bytes);
    }

    public void ReportProgress(long bytes)
    {
        Interlocked.Add(ref _downloaded, bytes);
    }

    public ProgressSnapshot GetSnapshot()
    {
        var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        var downloaded = Interlocked.Read(ref _downloaded);
        var total = Interlocked.Read(ref _total);

        return new ProgressSnapshot
        {
            DownloadedMB = downloaded / 1024.0 / 1024.0,
            TotalMB = total / 1024.0 / 1024.0,
            Percent = total == 0 ? 0 : (downloaded * 100.0) / total,
            SpeedMBps = elapsed > 0 ? (downloaded / 1024.0 / 1024.0) / elapsed : 0,
            ElapsedSeconds = elapsed
        };
    }
}

public class ProgressSnapshot
{
    public double DownloadedMB { get; set; }
    public double TotalMB { get; set; }
    public double Percent { get; set; }
    public double SpeedMBps { get; set; }
    public double ElapsedSeconds { get; set; }
}
