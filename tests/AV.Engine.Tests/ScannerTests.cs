using AV.Engine.Services;
using Xunit;

namespace AV.Engine.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _testDir;

    public ScannerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"xentra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task ScanFile_CleanFile_ReturnsClean()
    {
        var sigStore = new SignatureStore();
        await sigStore.LoadSignaturesAsync();
        var logger = new Logger(Path.Combine(_testDir, "logs"));
        var scanner = new Scanner(sigStore, logger);

        string filePath = Path.Combine(_testDir, "clean.txt");
        await File.WriteAllTextAsync(filePath, "This is a clean file.");

        var result = await scanner.ScanFileAsync(filePath);

        Assert.Equal("Clean", result.Status);
        Assert.Equal(filePath, result.FilePath);
    }

    [Fact]
    public async Task ScanFile_NonexistentFile_ReturnsError()
    {
        var sigStore = new SignatureStore();
        await sigStore.LoadSignaturesAsync();
        var logger = new Logger(Path.Combine(_testDir, "logs"));
        var scanner = new Scanner(sigStore, logger);

        var result = await scanner.ScanFileAsync(Path.Combine(_testDir, "nonexistent.txt"));

        Assert.Equal("Error", result.Status);
    }

    [Fact]
    public async Task ScanDirectory_NonexistentDirectory_ReturnsEmpty()
    {
        var sigStore = new SignatureStore();
        await sigStore.LoadSignaturesAsync();
        var logger = new Logger(Path.Combine(_testDir, "logs"));
        var scanner = new Scanner(sigStore, logger);

        var results = await scanner.ScanDirectoryAsync(Path.Combine(_testDir, "nonexistent"));

        Assert.Empty(results);
    }
}
