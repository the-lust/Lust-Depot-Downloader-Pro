using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace LustsDepotDownloaderPro.Core;

public class Checkpoint
{
    private readonly string _checkpointPath;
    private readonly ConcurrentHashSet<string> _completedChunks = new();
    
    public HashSet<string> CompletedChunks => _completedChunks.ToHashSet();

    private Checkpoint(string checkpointPath)
    {
        _checkpointPath = checkpointPath;
    }

    public static Checkpoint Load(string checkpointPath)
    {
        var checkpoint = new Checkpoint(checkpointPath);
        
        if (File.Exists(checkpointPath))
        {
            try
            {
                var json = File.ReadAllText(checkpointPath);
                var data = JsonConvert.DeserializeObject<CheckpointData>(json);
                
                if (data != null && data.CompletedChunks != null)
                {
                    foreach (var chunk in data.CompletedChunks)
                    {
                        checkpoint._completedChunks.Add(chunk);
                    }
                    
                    Utils.Logger.Info($"Loaded checkpoint with {data.CompletedChunks.Count} completed chunks");
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn($"Failed to load checkpoint: {ex.Message}");
            }
        }
        
        return checkpoint;
    }

    public void MarkChunkComplete(string chunkId)
    {
        _completedChunks.Add(chunkId);
        
        // Auto-save every 100 chunks
        if (_completedChunks.Count % 100 == 0)
        {
            Save();
        }
    }

    public bool IsChunkComplete(string chunkId)
    {
        return _completedChunks.Contains(chunkId);
    }

    public void Save()
    {
        try
        {
            var data = new CheckpointData
            {
                CompletedChunks = _completedChunks.ToHashSet(),
                LastSaved = DateTime.UtcNow
            };
            
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_checkpointPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(_checkpointPath, json);
            Utils.Logger.Debug($"Checkpoint saved: {_completedChunks.Count} chunks");
        }
        catch (Exception ex)
        {
            Utils.Logger.Error($"Failed to save checkpoint: {ex.Message}");
        }
    }

    private class CheckpointData
    {
        public HashSet<string> CompletedChunks { get; set; } = new();
        public DateTime LastSaved { get; set; }
    }
}

// Thread-safe HashSet
public class ConcurrentHashSet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();

    public void Add(T item) => _dictionary.TryAdd(item, 0);
    public bool Contains(T item) => _dictionary.ContainsKey(item);
    public HashSet<T> ToHashSet() => _dictionary.Keys.ToHashSet();
    public int Count => _dictionary.Count;
}
