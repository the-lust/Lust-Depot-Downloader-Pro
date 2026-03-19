using System.Collections.Concurrent;

namespace LustsDepotDownloaderPro.Utils;

public static class Logger
{
    private static bool _debugEnabled = false;
    private static readonly object _lock = new();
    private static StreamWriter? _logWriter;
    private static readonly ConcurrentQueue<string> _logQueue = new();

    public static void Initialize(bool debug = false)
    {
        _debugEnabled = debug;

        // Create log file
        string logPath = Path.Combine(Environment.CurrentDirectory, "logs", $"download_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        
        _logWriter = new StreamWriter(logPath, append: true)
        {
            AutoFlush = true
        };

        // Start background log writer
        _ = Task.Run(async () =>
        {
            while (true)
            {
                if (_logQueue.TryDequeue(out var message))
                {
                    await _logWriter.WriteLineAsync(message);
                }
                else
                {
                    await Task.Delay(100);
                }
            }
        });

        Info("Logger initialized");
    }

    public static void Info(string message)
    {
        Log("INFO", message, ConsoleColor.White);
    }

    public static void Warn(string message)
    {
        Log("WARN", message, ConsoleColor.Yellow);
    }

    public static void Error(string message)
    {
        Log("ERROR", message, ConsoleColor.Red);
    }

    public static void Debug(string message)
    {
        if (_debugEnabled)
        {
            Log("DEBUG", message, ConsoleColor.Gray);
        }
    }

    public static void Success(string message)
    {
        Log("SUCCESS", message, ConsoleColor.Green);
    }

    private static void Log(string level, string message, ConsoleColor color)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logMessage = $"[{timestamp}] [{level}] {message}";

        // Queue for file writing
        _logQueue.Enqueue(logMessage);

        // Console output
        lock (_lock)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(logMessage);
            Console.ResetColor();
        }
    }

    public static void Dispose()
    {
        _logWriter?.Dispose();
    }
}
