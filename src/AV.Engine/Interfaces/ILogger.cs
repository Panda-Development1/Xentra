namespace AV.Engine.Interfaces;

public interface ILogger
{
    Task LogAsync(string level, string message, string? source = null, CancellationToken ct = default);
    IReadOnlyList<string> GetRecentLogs(int count = 100);
    string GetCurrentLogPath();
}
