using LustsDepotDownloaderPro.Models;
using LustsDepotDownloaderPro.Steam;
using LustsDepotDownloaderPro.Utils;

namespace LustsDepotDownloaderPro.Core;

public class DownloadEngine : IDisposable
{
    private readonly DownloadSession _session;
    private readonly SteamSession _steamSession;   // FIX: added so workers can use it
    private readonly CancellationToken _ct;

    private readonly ChunkScheduler _scheduler;
    private readonly GlobalProgress _progress;
    private readonly Checkpoint _checkpoint;
    private readonly List<DownloadWorker> _workers = new();
    private FileAssembler? _fileAssembler;

    public event EventHandler<ProgressEventArgs>? ProgressChanged;

    // FIX: Constructor now requires SteamSession so workers receive it
    public DownloadEngine(DownloadSession session, SteamSession steamSession, CancellationToken ct)
    {
        _session      = session;
        _steamSession = steamSession;
        _ct           = ct;

        _progress   = new GlobalProgress();
        _scheduler  = new ChunkScheduler();
        _checkpoint = Checkpoint.Load(_session.CheckpointPath);

        _fileAssembler = new FileAssembler(session.OutputDir);
        Logger.Info($"Download engine initialised for AppID {session.AppId}");
    }

    public async Task RunAsync()
    {
        try
        {
            Logger.Info($"Starting download with {_session.MaxDownloads} concurrent workers");

            // Create workers — FIX: pass _steamSession
            for (int i = 0; i < _session.MaxDownloads; i++)
            {
                var worker = new DownloadWorker(
                    i, _scheduler, _progress, _session, _checkpoint, _ct, _steamSession, _fileAssembler!);
                worker.ProgressChanged += (s, e) => ProgressChanged?.Invoke(this, e);
                _workers.Add(worker);
            }

            var workerTasks = _workers.Select(w => w.RunAsync()).ToList();

            // Schedule all chunks lazily (avoids gigabytes sitting in RAM)
            await ScheduleChunksAsync();

            Logger.Info("All chunks scheduled, waiting for workers...");
            await Task.WhenAll(workerTasks);

            if (_ct.IsCancellationRequested)
            {
                Logger.Warn("Download paused by user");
                _checkpoint.Save();
            }
            else
            {
                Logger.Info("Finalizing files...");
                await FinalizeAllFilesAsync();
                Logger.Info("Download completed successfully");
                if (_session.ValidateChecksums)
                    await VerifyFilesAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Download engine error: {ex.Message}");
            throw;
        }
    }

    private async Task ScheduleChunksAsync()
    {
        foreach (var depot in _session.Depots)
        {
            Logger.Info($"Scheduling chunks for depot {depot.DepotId}");

            foreach (var file in depot.Files)
            {
                // Apply file-name filters if any were requested
                if (_session.FileFilters.Count > 0)
                {
                    bool matches = _session.FileFilters.Any(
                        filter => FilterMatcher.Matches(file.FileName, filter));
                    if (!matches) continue;
                }

                foreach (var chunk in file.Chunks)
                {
                    if (_ct.IsCancellationRequested) return;

                    if (_checkpoint.IsChunkComplete(chunk.ChunkIdHex)) continue;

                    // Back-pressure: don't let the queue grow unbounded for huge games
                    while (_scheduler.PendingCount > 5000 && !_ct.IsCancellationRequested)
                        await Task.Delay(100, _ct);

                    _scheduler.Enqueue(new ChunkTask
                    {
                        DepotId = depot.DepotId,
                        Chunk   = chunk,
                        File    = file,
                        Size    = chunk.CompressedLength
                    });
                    _progress.AddTotal(chunk.CompressedLength);
                }
            }
        }

        _scheduler.MarkSchedulingComplete();
    }

    private async Task VerifyFilesAsync()
    {
        Logger.Info("Verifying downloaded files...");
        int total = _session.Depots.Sum(d => d.Files.Count);
        int done  = 0;

        foreach (var depot in _session.Depots)
        {
            foreach (var file in depot.Files)
            {
                string path = Path.Combine(
                    _session.OutputDir,
                    file.FileName.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    if (fi.Length != (long)file.Size)
                        Logger.Warn(
                            $"Size mismatch: {file.FileName} " +
                            $"(expected {file.Size}, got {fi.Length})");
                }
                else
                {
                    Logger.Warn($"Missing: {file.FileName}");
                }

                if (++done % 100 == 0)
                    Logger.Info($"Verified {done}/{total} files");
            }
        }

        Logger.Info($"Verification done: {done}/{total}");
        await Task.CompletedTask;
    }

    public DownloadStatistics GetStatistics()
    {
        var snap = _progress.GetSnapshot();
        return new DownloadStatistics
        {
            DownloadedMB    = snap.DownloadedMB,
            TotalMB         = snap.TotalMB,
            Percent         = snap.Percent,
            SpeedMBps       = snap.SpeedMBps,
            CompletedChunks = _checkpoint.CompletedChunks.Count,
            TotalChunks     = _scheduler.TotalScheduled,
            IsCompleted     = _scheduler.IsComplete,
            IsPaused        = _ct.IsCancellationRequested
        };
    }

    public void Pause()
    {
        Logger.Info("Pausing download...");
        _checkpoint.Save();
    }
    private async Task FinalizeAllFilesAsync()
    {
        if (_fileAssembler == null) return;
        foreach (var depot in _session.Depots)
            foreach (var file in depot.Files)
            {
                try { await _fileAssembler.FinalizeFileAsync(file); }
                catch (Exception ex) { Logger.Warn($"Finalize {file.FileName}: {ex.Message}"); }
            }
    }

    public void Dispose()
    {
        _fileAssembler?.Dispose();
    }
}

public class ProgressEventArgs : EventArgs
{
    public long   BytesDownloaded { get; set; }
    public double PercentComplete  { get; set; }
    public double SpeedMBps        { get; set; }
    public int    WorkerId         { get; set; }
    public string? CurrentFile     { get; set; }
}