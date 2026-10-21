using System.Collections.Concurrent;
using System.Text;

namespace CycleSwipes;

/// <summary>
/// Log file next to the exe, only in developer mode (--debug); otherwise every call is a no-op and no file is created.
/// Writing happens on a background thread so input handling is never slowed down.
/// </summary>
internal sealed class Logger : IDisposable
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly BlockingCollection<string> _queue = new(16384);
    private readonly Thread? _thread;

    public string FilePath { get; }

    public bool Enabled { get; }

    public Logger(string filePath, bool enabled)
    {
        FilePath = filePath;
        Enabled = enabled;
        if (!enabled) return;
        _thread = new Thread(WriteLoop) { IsBackground = true, Name = "Logger" };
        _thread.Start();
    }

    /// <summary>Detailed per-gesture lines on top of startup, errors and wraps. Has no effect when the log is disabled.</summary>
    public volatile bool DebugEnabled;

    public void Write(string line)
    {
        if (Enabled) _queue.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}");
    }

    public void Debug(string line)
    {
        if (Enabled && DebugEnabled) Write(line);
    }

    private void WriteLoop()
    {
        StreamWriter? writer = null;
        try
        {
            writer = Open();
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                writer.WriteLine(line);
                if (_queue.Count != 0) continue;

                writer.Flush();
                if (writer.BaseStream.Length <= MaxBytes) continue;

                // Rotation: keep one previous file so the log does not grow forever.
                writer.Dispose();
                File.Move(FilePath, FilePath + ".old", overwrite: true);
                writer = Open();
            }
        }
        catch (Exception)
        {
            // Logging must never crash the app (no write access to the folder, disk full, etc.).
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private StreamWriter Open() =>
        new(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread?.Join(2000);
    }
}
