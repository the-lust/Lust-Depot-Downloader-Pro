using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace LustsDepotDownloaderPro.Core;

public class Checkpoint
{
    private readonly string _checkpointPath;
    private readonly ConcurrentHashSet<string> _completedChunks = new();
    private int _chunksSinceLastSave = 0;

    public HashSet<string> CompletedChunks => _completedChunks.ToHashSet();
    public string FilePath => _checkpointPath;

    private Checkpoint(string checkpointPath) => _checkpointPath = checkpointPath;

    public static Checkpoint Load(string checkpointPath)
    {
        var cp = new Checkpoint(checkpointPath);
        if (!File.Exists(checkpointPath)) return cp;
        try
        {
            var data = JsonConvert.DeserializeObject<CheckpointData>(File.ReadAllText(checkpointPath));
            if (data?.CompletedChunks != null)
            {
                foreach (var id in data.CompletedChunks) cp._completedChunks.Add(id);
                Utils.Logger.Info($"Loaded checkpoint: {data.CompletedChunks.Count} chunks (saved {data.LastSaved:u})");
            }
        }
        catch (Exception ex) { Utils.Logger.Warn($"Checkpoint load failed ({ex.Message}) — starting fresh"); }
        return cp;
    }

    public void MarkChunkComplete(string chunkId)
    {
        _completedChunks.Add(chunkId);
        if (Interlocked.Increment(ref _chunksSinceLastSave) % 100 == 0) Save();
    }

    public bool IsChunkComplete(string chunkId) => _completedChunks.Contains(chunkId);

    public void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_checkpointPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = _checkpointPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(new CheckpointData
            {
                CompletedChunks = _completedChunks.ToHashSet(),
                LastSaved       = DateTime.UtcNow
            }, Formatting.Indented));
            File.Move(tmp, _checkpointPath, overwrite: true);

            // MUST be Logger.Info with path — GUI stdout parser reads: "Checkpoint saved: <path>"
            Utils.Logger.Info($"Checkpoint saved: {_checkpointPath}");
        }
        catch (Exception ex) { Utils.Logger.Error($"Failed to save checkpoint: {ex.Message}"); }
    }

    private class CheckpointData
    {
        public HashSet<string> CompletedChunks { get; set; } = new();
        public DateTime LastSaved { get; set; }
    }
}

public class ConcurrentHashSet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _d = new();
    public void Add(T i)       => _d.TryAdd(i, 0);
    public bool Contains(T i)  => _d.ContainsKey(i);
    public HashSet<T> ToHashSet() => _d.Keys.ToHashSet();
    public int Count           => _d.Count;
}
