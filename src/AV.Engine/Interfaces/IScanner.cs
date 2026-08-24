using AV.Engine.Models;

namespace AV.Engine.Interfaces;

public interface IScanner
{
    Task<ScanResult> ScanFileAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<ScanResult>> ScanDirectoryAsync(string directoryPath, CancellationToken ct = default);
}
