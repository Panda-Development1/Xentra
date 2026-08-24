using AV.Engine.Models;
using AV.Engine.Services;
using Xunit;

namespace AV.Engine.Tests;

public class QuarantineTests : IDisposable
{
    private readonly string _quarantineDir;
    private readonly string _testDir;
    private readonly Logger _logger;

    public QuarantineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"xentra_qtest_{Guid.NewGuid():N}");
        _quarantineDir = Path.Combine(_testDir, "quarantine");
        Directory.CreateDirectory(_quarantineDir);
        _logger = new Logger(Path.Combine(_testDir, "logs"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task QuarantineAsync_CreatesOpaqueFileAndMetadata()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);
        string testFile = Path.Combine(_testDir, "malware.exe");
        await File.WriteAllTextAsync(testFile, "malicious content");

        var detection = new DetectionResult
        {
            IsMalicious = true,
            ThreatName = "TestMalware",
            Details = "Test detection"
        };

        var entry = await quarantine.QuarantineAsync(testFile, detection);

        Assert.False(File.Exists(testFile));
        Assert.True(File.Exists(Path.Combine(_quarantineDir, entry.Id)));
        Assert.True(File.Exists(Path.Combine(_quarantineDir, $"{entry.Id}.meta.json")));
        Assert.Equal("TestMalware", entry.ThreatName);
        Assert.Equal("malware.exe", entry.OriginalFileName);
    }

    [Fact]
    public async Task RestoreAsync_RestoresToOriginalPath()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);
        string testFile = Path.Combine(_testDir, "restoreme.txt");
        await File.WriteAllTextAsync(testFile, "restore this");

        var detection = new DetectionResult { IsMalicious = true, ThreatName = "Test" };
        var entry = await quarantine.QuarantineAsync(testFile, detection);

        await quarantine.RestoreAsync(entry.Id);

        Assert.True(File.Exists(testFile));
        Assert.False(File.Exists(Path.Combine(_quarantineDir, entry.Id)));
        string content = await File.ReadAllTextAsync(testFile);
        Assert.Equal("restore this", content);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndMetadata()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);
        string testFile = Path.Combine(_testDir, "deleteme.txt");
        await File.WriteAllTextAsync(testFile, "delete this");

        var detection = new DetectionResult { IsMalicious = true, ThreatName = "Test" };
        var entry = await quarantine.QuarantineAsync(testFile, detection);

        await quarantine.DeleteAsync(entry.Id);

        Assert.False(File.Exists(Path.Combine(_quarantineDir, entry.Id)));
        Assert.False(File.Exists(Path.Combine(_quarantineDir, $"{entry.Id}.meta.json")));
    }

    [Fact]
    public async Task ListAsync_ReturnsAllEntries()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);

        for (int i = 0; i < 3; i++)
        {
            string testFile = Path.Combine(_testDir, $"file{i}.txt");
            await File.WriteAllTextAsync(testFile, $"content {i}");
            await quarantine.QuarantineAsync(testFile, new DetectionResult { IsMalicious = true, ThreatName = $"Mal{i}" });
        }

        var entries = await quarantine.ListAsync();

        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task QuarantineAsync_NonexistentFile_Throws()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);
        var detection = new DetectionResult { IsMalicious = true, ThreatName = "Test" };

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => quarantine.QuarantineAsync(Path.Combine(_testDir, "nope.txt"), detection));
    }

    [Fact]
    public async Task RestoreAsync_InvalidId_Throws()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);

        await Assert.ThrowsAsync<ArgumentException>(
            () => quarantine.RestoreAsync("../../../etc/passwd"));
    }

    [Fact]
    public async Task DeleteAsync_InvalidId_Throws()
    {
        var quarantine = new Quarantine(_quarantineDir, _logger);

        await Assert.ThrowsAsync<ArgumentException>(
            () => quarantine.DeleteAsync("../escape"));
    }
}
