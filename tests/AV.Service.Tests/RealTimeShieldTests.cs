using AV.Engine.Interfaces;
using AV.Engine.Services;
using AV.Service.Services;
using Xunit;

namespace AV.Service.Tests;

public class RealTimeShieldTests : IDisposable
{
    private readonly string _testDir;

    public RealTimeShieldTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"xentra_shieldtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void ConstructorCreatesWatcher()
    {
        var sigStore = new SignatureStore();
        sigStore.LoadSignaturesAsync().GetAwaiter().GetResult();
        var logger = new Logger(Path.Combine(_testDir, "logs"));
        var scanner = new Scanner(sigStore, logger);
        var quarantine = new Quarantine(Path.Combine(_testDir, "quarantine"), logger);

        using var shield = new RealTimeShield(scanner, quarantine, logger);

        Assert.NotNull(shield);
    }

    [Fact]
    public void StartWatchingBeginsMonitoring()
    {
        var sigStore = new SignatureStore();
        sigStore.LoadSignaturesAsync().GetAwaiter().GetResult();
        var logger = new Logger(Path.Combine(_testDir, "logs"));
        var scanner = new Scanner(sigStore, logger);
        var quarantine = new Quarantine(Path.Combine(_testDir, "quarantine"), logger);

        var shield = new RealTimeShield(scanner, quarantine, logger);

        shield.StartWatching(_testDir);

        Thread.Sleep(200);

        shield.StopWatching();
        shield.Dispose();
    }
}
