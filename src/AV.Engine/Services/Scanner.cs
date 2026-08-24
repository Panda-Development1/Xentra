using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AV.Engine.Interfaces;
using AV.Engine.Models;

namespace AV.Engine.Services;

public class Scanner : IScanner
{
    private readonly ISignatureStore _signatureStore;
    private readonly ILogger _logger;

    private static readonly string[] DefaultExclusions = new[]
    {
        @"^C:\\Windows\\",
        @"^C:\\Program Files\\",
        @"^C:\\Program Files \(x86\)\\",
        @"^C:\\Users\\[^\\]+\\AppData\\"
    };

    private readonly Regex[] _exclusionPatterns;

    public Scanner(ISignatureStore signatureStore, ILogger logger, string[]? customExclusions = null)
    {
        _signatureStore = signatureStore;
        _logger = logger;
        var patterns = customExclusions ?? DefaultExclusions;
        _exclusionPatterns = patterns.Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase)).ToArray();
    }

    public async Task<ScanResult> ScanFileAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return new ScanResult
            {
                FilePath = filePath,
                Status = "Error",
                Error = "File not found.",
                ScannedAt = DateTimeOffset.UtcNow
            };
        }

        try
        {
            var fi = new FileInfo(filePath);
            const long maxSize = 100 * 1024 * 1024;
            if (fi.Length > maxSize)
            {
                return new ScanResult
                {
                    FilePath = filePath,
                    Status = "Skipped",
                    Error = "File exceeds maximum scan size.",
                    ScannedAt = DateTimeOffset.UtcNow
                };
            }

            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return new ScanResult
                {
                    FilePath = filePath,
                    Status = "Skipped",
                    Error = "Reparse point skipped.",
                    ScannedAt = DateTimeOffset.UtcNow
                };
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(filePath, ct);
            byte[] hash = SHA256.HashData(fileBytes);
            string hashHex = Convert.ToHexString(hash);

            var detection = await _signatureStore.DetectAsync(hash, ct);

            if (detection is { IsMalicious: true })
            {
                await _logger.LogAsync("WARN", $"Threat detected: {detection.ThreatName} in {filePath}", "Scanner", ct);
                return new ScanResult
                {
                    FilePath = filePath,
                    Status = "Threat",
                    Detection = detection,
                    ScannedAt = DateTimeOffset.UtcNow
                };
            }

            return new ScanResult
            {
                FilePath = filePath,
                Status = "Clean",
                ScannedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception)
        {
            return new ScanResult
            {
                FilePath = filePath,
                Status = "Error",
                Error = "An error occurred during scanning.",
                ScannedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<IReadOnlyList<ScanResult>> ScanDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return Array.Empty<ScanResult>();

        var results = new List<ScanResult>();
        var files = Directory.EnumerateFiles(directoryPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        });

        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = new List<Task>();

        foreach (var file in files)
        {
            if (IsExcluded(file)) continue;
            await semaphore.WaitAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await ScanFileAsync(file, ct);
                    lock (results) { results.Add(result); }
                }
                finally { semaphore.Release(); }
            }, ct));
        }

        await Task.WhenAll(tasks);
        return results.AsReadOnly();
    }

    private bool IsExcluded(string filePath)
    {
        return _exclusionPatterns.Any(p => p.IsMatch(filePath));
    }
}
