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
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, int> _retryCounts = new();
    private readonly ConcurrentQueue<string> _scanQueue = new();
    private readonly SemaphoreSlim _concurrency = new(10);
    private CancellationTokenSource? _cts;
    private Task? _processTask;

    private readonly int _maxRetries;
    private readonly int _debounceMs;

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
        var path = watchPath ?? Models.ShieldConfiguration.DefaultWatchPath;

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = false
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;

        _processTask = ProcessQueueAsync(_cts.Token);
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

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
            catch (IOException) when (attempt < _maxRetries - 1)
            {
                _retryCounts.AddOrUpdate(filePath, 1, (_, c) => c + 1);
                await Task.Delay(200 * (attempt + 1), ct);
            }
        }
    }

    public void Dispose()
    {
        StopWatching();
        _concurrency.Dispose();
        _cts?.Dispose();
    }
}
