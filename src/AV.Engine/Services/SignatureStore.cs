using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AV.Engine.Interfaces;
using AV.Engine.Models;

namespace AV.Engine.Services;

public class SignatureStore : ISignatureStore
{
    private readonly ConcurrentDictionary<string, DetectionResult> _hashIndex = new();
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private bool _loaded;

    public async Task LoadSignaturesAsync(CancellationToken ct = default)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("signatures.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            throw new InvalidOperationException("Signature database not found as embedded resource.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(ct);

        var entries = JsonSerializer.Deserialize<List<SignatureEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (entries is null) return;

        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Hash))
                _hashIndex.TryAdd(entry.Hash.ToUpperInvariant(), entry.ToDetectionResult());

            if (!string.IsNullOrEmpty(entry.Pattern))
            {
                try
                {
                    var regex = new Regex(entry.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    _regexCache.TryAdd(entry.Id, regex);
                }
                catch (RegexParseException) { }
            }
        }

        _loaded = true;
    }

    public Task<bool> ValidateIntegrityAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_loaded);
    }

    public int GetSignatureCount() => _hashIndex.Count + _regexCache.Count;

    public Task<DetectionResult?> DetectAsync(byte[] fileHash, CancellationToken ct = default)
    {
        if (!_loaded)
            return Task.FromResult<DetectionResult?>(null);

        string hashHex = Convert.ToHexString(fileHash).ToUpperInvariant();

        if (_hashIndex.TryGetValue(hashHex, out var hashResult))
            return Task.FromResult<DetectionResult?>(hashResult);

        if (!_regexCache.IsEmpty)
            System.Diagnostics.Debug.WriteLine("WARN: Regex signatures are loaded but cannot match file content — pattern matching requires file content bytes.");

        return Task.FromResult<DetectionResult?>(null);
    }

    private class SignatureEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Hash { get; set; }
        public string? Pattern { get; set; }

        public DetectionResult ToDetectionResult() => new()
        {
            IsMalicious = true,
            ThreatName = Name,
            SignatureId = Id,
            Details = $"Matched signature {Id}."
        };
    }
}
