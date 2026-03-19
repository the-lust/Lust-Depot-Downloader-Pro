using LustsDepotDownloaderPro.Models;
using LustsDepotDownloaderPro.Steam;
using LustsDepotDownloaderPro.Utils;
using SteamKit2;
using SteamKit2.CDN;

namespace LustsDepotDownloaderPro.Core;

public class DownloadWorker
{
    private readonly int _workerId;
    private readonly ChunkScheduler _scheduler;
    private readonly GlobalProgress _progress;
    private readonly DownloadSession _session;
    private readonly Checkpoint _checkpoint;
    private readonly CancellationToken _ct;
    private readonly SteamSession _steam;   // FIX: was a homemade CdnClient with no auth
    private readonly FileAssembler _fileAssembler;

    private IReadOnlyCollection<Server>? _servers;

    public event EventHandler<ProgressEventArgs>? ProgressChanged;

    public DownloadWorker(
        int workerId,
        ChunkScheduler scheduler,
        GlobalProgress progress,
        DownloadSession session,
        Checkpoint checkpoint,
        CancellationToken ct,
        SteamSession steam,
        FileAssembler fileAssembler)  // shared assembler with cached handles
    {
        _workerId      = workerId;
        _scheduler     = scheduler;
        _progress      = progress;
        _session       = session;
        _checkpoint    = checkpoint;
        _ct            = ct;
        _steam         = steam;
        _fileAssembler = fileAssembler;
    }

    public async Task RunAsync()
    {
        Logger.Debug($"Worker {_workerId} started");

        // Fetch the CDN server list once per worker (SteamSession caches it)
        _servers = await _steam.GetCdnServersAsync();

        try
        {
            while (!_ct.IsCancellationRequested)
            {
                if (!_scheduler.TryDequeue(out var task) || task == null)
                {
                    if (_scheduler.IsComplete)
                    {
                        Logger.Debug($"Worker {_workerId} finished — no more chunks");
                        break;
                    }
                    await Task.Delay(100, _ct);
                    continue;
                }

                try
                {
                    await ProcessChunkAsync(task);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Worker {_workerId} chunk error: {ex.Message}");
                    _scheduler.MarkFailed(task);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug($"Worker {_workerId} cancelled");
        }
        catch (Exception ex)
        {
            Logger.Error($"Worker {_workerId} fatal: {ex.Message}");
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task ProcessChunkAsync(ChunkTask task)
    {
        // Look up the depot key for this depot
        var depotInfo = _session.Depots.FirstOrDefault(d => d.DepotId == task.DepotId);
        var depotKey  = depotInfo?.DepotKey;

        // FIX: CDN.Client.DownloadDepotChunkAsync handles
        //        1. Authenticated HTTP download
        //        2. AES-ECB decryption with depot key     ← was missing entirely
        //        3. VZip / zlib decompression             ← was buggy with custom code
        byte[] data = await DownloadChunkWithRetryAsync(task, depotKey);

        // Write to file at the correct byte offset
        await _fileAssembler.WriteChunkAsync(task.File, task.Chunk, data);

        _progress.ReportProgress(task.Size);
        _checkpoint.MarkChunkComplete(task.Chunk.ChunkIdHex);

        var snap = _progress.GetSnapshot();
        ProgressChanged?.Invoke(this, new ProgressEventArgs
        {
            BytesDownloaded = (long)(snap.DownloadedMB * 1024 * 1024),
            PercentComplete  = snap.Percent,
            SpeedMBps        = snap.SpeedMBps,
            WorkerId         = _workerId,
            CurrentFile      = task.File.FileName
        });
    }

    private async Task<byte[]> DownloadChunkWithRetryAsync(ChunkTask task, byte[]? depotKey)
    {
        if (task.Chunk.ChunkId == null || task.Chunk.ChunkId.Length == 0)
            throw new InvalidOperationException($"Chunk {task.Chunk.ChunkIdHex} has no ID bytes");

        // Build the SteamKit2 chunk descriptor
        var chunkInfo = new DepotManifest.ChunkData
        {
            ChunkID            = task.Chunk.ChunkId,
            Checksum           = task.Chunk.Checksum,  // Now uint in SteamKit2 3.x
            Offset             = task.Chunk.Offset,
            CompressedLength   = task.Chunk.CompressedLength,
            UncompressedLength = task.Chunk.UncompressedLength
        };

        // FIX (SteamKit2 3.x): DownloadDepotChunkAsync now writes into a pre-allocated
        // buffer and returns the number of bytes written (uncompressed).
        // The old 2.x API returned a DepotChunk object — that overload no longer exists.
        // Allocate based on uncompressed size (what we'll receive after decrypt+decompress).
        int bufSize = (int)Math.Max(task.Chunk.UncompressedLength, task.Chunk.CompressedLength);
        if (bufSize <= 0) bufSize = 1024 * 1024; // fallback 1 MB
        byte[] buffer = new byte[bufSize];

        var servers  = _servers ?? Array.Empty<Server>();
        Exception? lastEx = null;

        foreach (var server in servers.Take(5))
        {
            try
            {
                // SteamKit2 3.x signature:
                //   Task<int> DownloadDepotChunkAsync(depotId, chunk, server, destination, depotKey,
                //                                    proxyServer = null, cdnAuthToken = null)
                // Returns bytes written into destination[].
                // Decryption (AES) + VZip decompression are handled internally by SteamKit2.
                int written = await _steam.CdnClient.DownloadDepotChunkAsync(
                    task.DepotId, chunkInfo, server, buffer, depotKey);

                // Slice to the actual written length; avoid extra allocation when exact fit
                return written == buffer.Length ? buffer : buffer[..written];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastEx = ex;
                Logger.Debug($"Worker {_workerId}: {server.Host} → {ex.Message}");
            }
        }

        throw new Exception(
            $"All CDN servers failed for chunk {task.Chunk.ChunkIdHex}: {lastEx?.Message}",
            lastEx);
    }
}