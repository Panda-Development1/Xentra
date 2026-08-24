using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AV.Engine.Interfaces;
using AV.Engine.Models;

namespace AV.Engine.Services;

public class Quarantine : IQuarantine
{
    private readonly string _quarantinePath;
    private readonly ILogger _logger;

    private static readonly Regex ValidIdPattern = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public Quarantine(string quarantinePath, ILogger logger)
    {
        _quarantinePath = quarantinePath;
        _logger = logger;
        Directory.CreateDirectory(_quarantinePath);
    }

    public async Task<QuarantineEntry> QuarantineAsync(string filePath, DetectionResult detection, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File to quarantine not found.", filePath);

        string quarantineId = $"{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        string destPath = Path.Combine(_quarantinePath, quarantineId);
        string metadataPath = Path.Combine(_quarantinePath, $"{quarantineId}.meta.json");

        byte[] beforeHash = SHA256.HashData(await File.ReadAllBytesAsync(filePath, ct));
        string beforeHashHex = Convert.ToHexString(beforeHash);

        await _logger.LogAsync("INFO", $"Quarantining {filePath} -> {destPath}", "Quarantine", ct);

        File.Move(filePath, destPath);

        byte[] afterBytes = await File.ReadAllBytesAsync(destPath, ct);
        byte[] afterHash = SHA256.HashData(afterBytes);
        string afterHashHex = Convert.ToHexString(afterHash);

        if (beforeHashHex != afterHashHex)
        {
            File.Move(destPath, filePath);
            throw new InvalidOperationException("File integrity check failed after move.");
        }

        var entry = new QuarantineEntry
        {
            Id = quarantineId,
            OriginalPath = Path.GetDirectoryName(filePath) ?? "",
            OriginalFileName = Path.GetFileName(filePath),
            Sha256Before = beforeHashHex,
            Sha256After = afterHashHex,
            ThreatName = detection.ThreatName ?? "Unknown",
            QuarantinedAt = DateTimeOffset.UtcNow,
            DetectionDetails = detection.Details
        };

        var json = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json, ct);

        await _logger.LogAsync("INFO", $"Quarantined {filePath} as {quarantineId}", "Quarantine", ct);
        return entry;
    }

    public async Task RestoreAsync(string quarantineId, CancellationToken ct = default)
    {
        ValidateQuarantineId(quarantineId);

        string srcPath = Path.Combine(_quarantinePath, quarantineId);
        string metadataPath = Path.Combine(_quarantinePath, $"{quarantineId}.meta.json");

        if (!File.Exists(srcPath))
            throw new FileNotFoundException("Quarantined file not found.", quarantineId);

        if (!File.Exists(metadataPath))
            throw new FileNotFoundException("Quarantine metadata not found.", quarantineId);

        var json = await File.ReadAllTextAsync(metadataPath, ct);
        var entry = System.Text.Json.JsonSerializer.Deserialize<QuarantineEntry>(json)
            ?? throw new InvalidOperationException("Invalid quarantine metadata.");

        string restorePath = Path.Combine(entry.OriginalPath, entry.OriginalFileName);
        string? dir = Path.GetDirectoryName(restorePath);
        if (dir != null) Directory.CreateDirectory(dir);

        File.Move(srcPath, restorePath);
        File.Delete(metadataPath);

        await _logger.LogAsync("INFO", $"Restored {quarantineId} to {restorePath}", "Quarantine", ct);
    }

    public async Task DeleteAsync(string quarantineId, CancellationToken ct = default)
    {
        ValidateQuarantineId(quarantineId);

        string srcPath = Path.Combine(_quarantinePath, quarantineId);
        string metadataPath = Path.Combine(_quarantinePath, $"{quarantineId}.meta.json");

        if (File.Exists(srcPath))
            File.Delete(srcPath);

        if (File.Exists(metadataPath))
            File.Delete(metadataPath);

        await _logger.LogAsync("INFO", $"Deleted quarantine entry {quarantineId}", "Quarantine", ct);
    }

    public async Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = new List<QuarantineEntry>();
        var metaFiles = Directory.GetFiles(_quarantinePath, "*.meta.json");

        foreach (var metaFile in metaFiles)
        {
            var json = await File.ReadAllTextAsync(metaFile, ct);
            var entry = System.Text.Json.JsonSerializer.Deserialize<QuarantineEntry>(json);
            if (entry != null) entries.Add(entry);
        }

        return entries.AsReadOnly();
    }

    public async Task<QuarantineEntry?> GetEntryAsync(string quarantineId, CancellationToken ct = default)
    {
        ValidateQuarantineId(quarantineId);
        string metadataPath = Path.Combine(_quarantinePath, $"{quarantineId}.meta.json");

        if (!File.Exists(metadataPath))
            return null;

        var json = await File.ReadAllTextAsync(metadataPath, ct);
        return System.Text.Json.JsonSerializer.Deserialize<QuarantineEntry>(json);
    }

    private static void ValidateQuarantineId(string quarantineId)
    {
        if (string.IsNullOrWhiteSpace(quarantineId) || !ValidIdPattern.IsMatch(quarantineId))
            throw new ArgumentException("Invalid quarantine ID format.");
    }
}
