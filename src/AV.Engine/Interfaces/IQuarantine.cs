using AV.Engine.Models;

namespace AV.Engine.Interfaces;

public interface IQuarantine
{
    Task<QuarantineEntry> QuarantineAsync(string filePath, DetectionResult detection, CancellationToken ct = default);
    Task RestoreAsync(string quarantineId, CancellationToken ct = default);
    Task DeleteAsync(string quarantineId, CancellationToken ct = default);
    Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken ct = default);
    Task<QuarantineEntry?> GetEntryAsync(string quarantineId, CancellationToken ct = default);
}
