using System.Collections.Concurrent;
using LustsDepotDownloaderPro.Models;

namespace LustsDepotDownloaderPro.Core;

public class ChunkScheduler
{
    private readonly ConcurrentQueue<ChunkTask> _queue = new();
    private readonly ConcurrentDictionary<string, ChunkTask> _retry = new();
    private int _totalScheduled = 0;
    private bool _schedulingComplete = false;

    public int PendingCount => _queue.Count + _retry.Count;
    public int TotalScheduled => _totalScheduled;
    public bool IsComplete => _schedulingComplete && _queue.IsEmpty && _retry.IsEmpty;

    public void Enqueue(ChunkTask task)
    {
        _queue.Enqueue(task);
        Interlocked.Increment(ref _totalScheduled);
    }

    public bool TryDequeue(out ChunkTask? task)
    {
        // Prioritize retries
        foreach (var kvp in _retry.ToArray())
        {
            if (_retry.TryRemove(kvp.Key, out task))
                return true;
        }

        // Then try regular queue
        return _queue.TryDequeue(out task);
    }

    public void MarkFailed(ChunkTask task)
    {
        task.RetryCount++;

        if (task.RetryCount > 10) // Increased retry limit
        {
            Utils.Logger.Error($"Chunk {task.Chunk.ChunkIdHex} failed after {task.RetryCount} retries");
            return;
        }

        // Exponential backoff
        Task.Delay(Math.Min(1000 * (int)Math.Pow(2, task.RetryCount), 30000)).Wait();
        
        _retry[task.Chunk.ChunkIdHex] = task;
    }

    public void MarkSchedulingComplete()
    {
        _schedulingComplete = true;
    }
}
