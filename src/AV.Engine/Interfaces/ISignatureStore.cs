using AV.Engine.Models;

namespace AV.Engine.Interfaces;

public interface ISignatureStore
{
    Task LoadSignaturesAsync(CancellationToken ct = default);
    Task<bool> ValidateIntegrityAsync(CancellationToken ct = default);
    int GetSignatureCount();
    Task<DetectionResult?> DetectAsync(byte[] fileHash, CancellationToken ct = default);
}
