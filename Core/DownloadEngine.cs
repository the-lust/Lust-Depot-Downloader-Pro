using LustsDepotDownloaderPro.Models;
using LustsDepotDownloaderPro.Steam;
using LustsDepotDownloaderPro.Utils;

namespace LustsDepotDownloaderPro.Core;

public class DownloadEngine : IDisposable
{
    private readonly DownloadSession _session;
    private readonly SteamSession    _steamSession;
    private readonly CancellationToken _ct;

    private readonly ChunkScheduler  _scheduler;
    private readonly GlobalProgress  _progress;
    private readonly Checkpoint      _checkpoint;
    private readonly List<DownloadWorker> _workers = new();
    private FileAssembler? _fileAssembler;

    public event EventHandler<ProgressEventArgs>? ProgressChanged;

    public DownloadEngine(
        DownloadSession session,
        SteamSession steamSession,
        CancellationToken ct)
    {
        _session      = session;
        _steamSession = steamSession;
        _ct           = ct;
        _progress     = new GlobalProgress();
        _scheduler    = new ChunkScheduler();
        _checkpoint   = Checkpoint.Load(_session.CheckpointPath);
        _fileAssembler = new FileAssembler(session.OutputDir);
        Logger.Debug($"Download engine initialised for AppID {session.AppId}");
    }

    public async Task RunAsync()
    {
        try
        {
            Logger.Info($"Starting download with {_session.MaxDownloads} workers");

            // ── KEY FIX: pre-warm CDN server cache before starting workers.
            //    Without this, each worker independently fetches the server list
            //    on startup. While they do that, the chunk scheduler fills the
            //    5 000-chunk throttle window and then STALLS waiting for workers
            //    that are still fetching CDN servers — causing the 2–7 min hang.
            Logger.Debug("Pre-fetching CDN server list...");
            await _steamSession.GetCdnServersAsync();

            for (int i = 0; i < _session.MaxDownloads; i++)
            {
                var w = new DownloadWorker(
                    i, _scheduler, _progress, _session, _checkpoint, _ct,
                    _steamSession, _fileAssembler!);
                w.ProgressChanged += OnWorkerProgress;
                _workers.Add(w);
            }

            var workerTasks = _workers.Select(w => w.RunAsync()).ToList();
            await ScheduleChunksAsync();
            Logger.Debug("All chunks scheduled — waiting for workers...");
            await Task.WhenAll(workerTasks);

            if (_ct.IsCancellationRequested)
            {
                Logger.Info("Download paused — saving checkpoint...");
                _checkpoint.Save();
                _session.WasCancelled = true;
            }
            else
            {
                Logger.Debug("All chunks downloaded — finalizing files...");
                await FinalizeAllFilesAsync();
                if (_session.ValidateChecksums) await VerifyFilesAsync();
                Logger.Success("Download completed successfully!");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Download paused — saving checkpoint...");
            _checkpoint.Save();
            _session.WasCancelled = true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Download engine error: {ex.Message}");
            throw;
        }
    }

    private void OnWorkerProgress(object? sender, ProgressEventArgs e)
    {
        var snap = _progress.GetSnapshot();
        var args = new ProgressEventArgs
        {
            BytesDownloaded = (long)(snap.DownloadedMB * 1_048_576),
            PercentComplete  = snap.Percent,
            SpeedMBps        = snap.SpeedMBps,
            EtaSeconds       = snap.EtaSeconds,
            WorkerId         = e.WorkerId,
            CurrentFile      = e.CurrentFile
        };
        ProgressChanged?.Invoke(this, args);
    }

    private async Task ScheduleChunksAsync()
    {
        foreach (var depot in _session.Depots)
        {
            Logger.Debug($"Scheduling chunks for depot {depot.DepotId}");
            foreach (var file in depot.Files)
            {
                if (_session.FileFilters.Count > 0)
                {
                    bool match = _session.FileFilters.Any(f => FilterMatcher.Matches(file.FileName, f));
                    if (!match) continue;
                }
                foreach (var chunk in file.Chunks)
                {
                    if (_ct.IsCancellationRequested) return;
                    if (_checkpoint.IsChunkComplete(chunk.ChunkIdHex)) continue;

                    // Back-pressure: don't queue more than 10k chunks ahead of workers
                    while (_scheduler.PendingCount > 10_000 && !_ct.IsCancellationRequested)
                        await Task.Delay(50, _ct);

                    _scheduler.Enqueue(new ChunkTask
                        { DepotId = depot.DepotId, Chunk = chunk, File = file,
                          Size = chunk.CompressedLength });
                    _progress.AddTotal(chunk.CompressedLength);
                }
            }
        }
        _scheduler.MarkSchedulingComplete();
    }

    private async Task FinalizeAllFilesAsync()
    {
        if (_fileAssembler == null) return;
        foreach (var depot in _session.Depots)
            foreach (var file in depot.Files)
            {
                try   { await _fileAssembler.FinalizeFileAsync(file); }
                catch (Exception ex) { Logger.Warn($"Finalize {file.FileName}: {ex.Message}"); }
            }
    }

    private async Task VerifyFilesAsync()
    {
        Logger.Info("Verifying files...");
        int total = _session.Depots.Sum(d => d.Files.Count);
        int done  = 0;
        foreach (var depot in _session.Depots)
            foreach (var file in depot.Files)
            {
                string path = System.IO.Path.Combine(
                    _session.OutputDir,
                    file.FileName.Replace('/', System.IO.Path.DirectorySeparatorChar));

                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length != (long)file.Size)
                        Logger.Warn($"Size mismatch: {file.FileName}");
                }
                else Logger.Warn($"Missing: {file.FileName}");

                if (++done % 500 == 0) Logger.Info($"Verified {done}/{total}");
            }
        Logger.Info($"Verification complete: {done}/{total} files");
        await Task.CompletedTask;
    }

    public DownloadStatistics GetStatistics()
    {
        var s = _progress.GetSnapshot();
        return new DownloadStatistics
        {
            DownloadedMB    = s.DownloadedMB,
            TotalMB         = s.TotalMB,
            Percent         = s.Percent,
            SpeedMBps       = s.SpeedMBps,
            CompletedChunks = _checkpoint.CompletedChunks.Count,
            TotalChunks     = _scheduler.TotalScheduled,
            IsCompleted     = _scheduler.IsComplete,
            IsPaused        = _ct.IsCancellationRequested
        };
    }

    public void Dispose() => _fileAssembler?.Dispose();
}

public class ProgressEventArgs : EventArgs
{
    public long    BytesDownloaded  { get; set; }
    public double  PercentComplete   { get; set; }
    public double  SpeedMBps         { get; set; }
    public double  EtaSeconds        { get; set; }
    public int     WorkerId          { get; set; }
    public string? CurrentFile       { get; set; }
}
