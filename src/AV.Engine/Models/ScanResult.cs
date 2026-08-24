namespace AV.Engine.Models;

public class ScanResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DetectionResult? Detection { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
}
