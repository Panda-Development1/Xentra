using AV.Engine.Services;
using Xunit;

namespace AV.Engine.Tests;

public class LoggerTests : IDisposable
{
    private readonly string _testDir;

    public LoggerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"xentra_logtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task LogAsync_WritesToFile()
    {
        var logger = new Logger(_testDir);

        await logger.LogAsync("INFO", "Test message");

        var logPath = logger.GetCurrentLogPath();
        Assert.True(File.Exists(logPath));
        string content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("Test message", content);
        Assert.Contains("[INFO]", content);
    }

    [Fact]
    public async Task LogAsync_IncludesSource()
    {
        var logger = new Logger(_testDir);

        await logger.LogAsync("WARN", "Warning message", "TestSource");

        string content = await File.ReadAllTextAsync(logger.GetCurrentLogPath());
        Assert.Contains("[TestSource]", content);
    }

    [Fact]
    public void GetCurrentLogPath_ReturnsDailyLogPath()
    {
        var logger = new Logger(_testDir);
        string path = logger.GetCurrentLogPath();

        string expected = $"xentra-{DateTime.UtcNow:yyyy-MM-dd}.log";
        Assert.EndsWith(expected, path);
    }

    [Fact]
    public void GetRecentLogs_ReturnsLogEntries()
    {
        var logger = new Logger(_testDir);

        logger.LogAsync("INFO", "msg1").GetAwaiter().GetResult();
        logger.LogAsync("INFO", "msg2").GetAwaiter().GetResult();

        var logs = logger.GetRecentLogs();
        Assert.True(logs.Count >= 2);
    }

    [Fact]
    public void GetRecentLogs_RespectsCount()
    {
        var logger = new Logger(_testDir);

        for (int i = 0; i < 10; i++)
            logger.LogAsync("INFO", $"msg{i}").GetAwaiter().GetResult();

        var logs = logger.GetRecentLogs(3);
        Assert.Equal(3, logs.Count);
    }
}
