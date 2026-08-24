namespace AV.Engine.Models;

public class QuarantineEntry
{
    public string Id { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string Sha256Before { get; set; } = string.Empty;
    public string Sha256After { get; set; } = string.Empty;
    public string ThreatName { get; set; } = string.Empty;
    public DateTimeOffset QuarantinedAt { get; set; }
    public string? DetectionDetails { get; set; }
}
