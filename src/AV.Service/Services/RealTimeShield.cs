using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using AV.Engine.Interfaces;
using AV.Engine.Models;

namespace AV.Service.Services;

public class RealTimeShield : IDisposable
{
    private readonly IScanner _scanner;
    private readonly IQuarantine _quarantine;
    private readonly AV.Engine.Interfaces.ILogger _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, int> _retryCounts = new();
    private readonly ConcurrentQueue<string> _scanQueue = new();
    private readonly SemaphoreSlim _concurrency = new(10);
    private CancellationTokenSource? _cts;
    private Task? _processTask;

    private readonly int _maxRetries;
    private readonly int _debounceMs;
    // Paths the user just restored — skip auto-quarantine briefly so a restore isn't
    // instantly undone by the shield re-scanning the same file (ponytail: 2-min grace).
    private readonly ConcurrentDictionary<string, long> _restoredGrace = new(StringComparer.OrdinalIgnoreCase);

    public RealTimeShield(IScanner scanner, IQuarantine quarantine, AV.Engine.Interfaces.ILogger logger, Models.ShieldConfiguration? config = null)
    {
        _scanner = scanner;
        _quarantine = quarantine;
        _logger = logger;
        _maxRetries = config?.MaxRetries ?? 3;
        _debounceMs = config?.DebounceMs ?? 100;
    }

    public void StartWatching(string? watchPath = null)
    {
        _cts = new CancellationTokenSource();

        foreach (var (folder, recursive) in GetWatchFolders(watchPath))
        {
            if (!Directory.Exists(folder)) continue;

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = false
            };

            watcher.Created += OnFileEvent;
            watcher.Changed += OnFileEvent;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        _ = _logger.LogAsync("INFO", $"Real-time shield watching {_watchers.Count} location(s).", "Shield");
        _processTask = ProcessQueueAsync(_cts.Token);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _ = _logger.LogAsync("ERROR", $"Shield watcher error: {e.GetException().Message}", "Shield");
    }

    // The service runs as LocalSystem, so UserProfile resolves to the service's own
    // (empty) profile. Watch the real users' risky folders instead.
    // recursive=false avoids descending into AppData (reparse-point junctions) which
    // breaks a recursive FileSystemWatcher.
    private static IEnumerable<(string Path, bool Recursive)> GetWatchFolders(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && Directory.Exists(explicitPath))
        {
            yield return (explicitPath, true);
            yield break;
        }

        var usersRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users");
        if (!Directory.Exists(usersRoot))
            yield break;

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Public", "Default", "Default User", "All Users" };

        foreach (var profile in Directory.GetDirectories(usersRoot))
        {
            var name = Path.GetFileName(profile);
            if (string.IsNullOrEmpty(name) || skip.Contains(name)) continue;

            try
            {
                if ((File.GetAttributes(profile) & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { continue; }

            foreach (var sub in new[] { "Desktop", "Downloads", "Documents", "Pictures", "Music", "Videos" })
            {
                var f = Path.Combine(profile, sub);
                if (Directory.Exists(f)) yield return (f, true);
            }

            // Non-recursive catch for files dropped straight into the profile root.
            yield return (profile, false);
        }
    }

    public void StopWatching()
    {
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();

        _cts?.Cancel();
        try { _processTask?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        _scanQueue.Enqueue(e.FullPath);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            while (_scanQueue.TryDequeue(out var filePath))
            {
                await _concurrency.WaitAsync(ct);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ScanWithRetryAsync(filePath, ct);
                    }
                    catch (Exception ex)
                    {
                        await _logger.LogAsync("ERROR", $"Shield scan crashed for {filePath}: {ex.Message}", "Shield", ct);
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, ct);
            }
            await Task.Delay(_debounceMs, ct);
        }
    }

    private async Task ScanWithRetryAsync(string filePath, CancellationToken ct)
    {
        for (int attempt = 0; attempt < _maxRetries; attempt++)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var result = await _scanner.ScanFileAsync(filePath, ct);

                if (_restoredGrace.TryGetValue(filePath, out var expiry) &&
                    expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    _retryCounts.TryRemove(filePath, out _);
                    return;
                }

                if (result.Status == "Threat" && result.Detection != null)
                {
                    await _logger.LogAsync("WARN", $"Auto-quarantining threat: {result.Detection.ThreatName} in {filePath}", "Shield", ct);
                    await _quarantine.QuarantineAsync(filePath, result.Detection, ct);
                    _retryCounts.TryRemove(filePath, out _);
                    return;
                }

                _retryCounts.TryRemove(filePath, out _);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < _maxRetries - 1)
                {
                    _retryCounts.AddOrUpdate(filePath, 1, (_, c) => c + 1);
                    await Task.Delay(200 * (attempt + 1), ct);
                }
                else
                {
                    await _logger.LogAsync("ERROR", $"Gave up scanning {filePath}: {ex.Message}", "Shield", ct);
                }
            }
        }
    }

    public void Dispose()
    {
        StopWatching();
        _concurrency.Dispose();
        _cts?.Dispose();
    }

    public void AllowRestoredFile(string path)
    {
        if (!string.IsNullOrEmpty(path))
            _restoredGrace[path] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120;
    }
}
