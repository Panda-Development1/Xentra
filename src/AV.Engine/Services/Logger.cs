using System.Collections.Concurrent;
using AV.Engine.Interfaces;

namespace AV.Engine.Services;

public class Logger : ILogger
{
    private readonly string _logDirectory;
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private const int MaxRecentLogs = 1000;

    public Logger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public string GetCurrentLogPath()
    {
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"xentra-{date}.log");
    }

    public async Task LogAsync(string level, string message, string? source = null, CancellationToken ct = default)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string sourcePart = source != null ? $" [{source}]" : "";
        string line = $"{timestamp} [{level}]{sourcePart} {message}";

        _recentLogs.Enqueue(line);
        while (_recentLogs.Count > MaxRecentLogs)
            _recentLogs.TryDequeue(out _);

        try
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                string logPath = GetCurrentLogPath();
                await File.AppendAllTextAsync(logPath, line + Environment.NewLine, ct);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (IOException)
        {
            try
            {
                Console.Error.WriteLine(line);
            }
            catch { }
        }
    }

    public IReadOnlyList<string> GetRecentLogs(int count = 100)
    {
        var logs = _recentLogs.ToArray();
        int start = Math.Max(0, logs.Length - count);
        return logs.Skip(start).ToArray();
    }
}
