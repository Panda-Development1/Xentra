namespace AV.Engine.Models;

public class DetectionResult
{
    public bool IsMalicious { get; set; }
    public string? ThreatName { get; set; }
    public string? SignatureId { get; set; }
    public string? Details { get; set; }
}
