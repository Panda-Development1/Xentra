using AV.Engine.Interfaces;
using AV.Engine.Services;
using AV.Service.Ipc;
using AV.Service.Services;
using Xunit;

namespace AV.Service.Tests;

public class WorkerTests : IDisposable
{
    private readonly string _testDir;

    public WorkerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"xentra_workertest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void ConstructorInitializesCorrectly()
    {
        var sigStore = new SignatureStore();
        sigStore.LoadSignaturesAsync().GetAwaiter().GetResult();
        var logger = new Logger(Path.Combine(_testDir, "logs"));
        var scanner = new Scanner(sigStore, logger);
        var quarantine = new Quarantine(Path.Combine(_testDir, "quarantine"), logger);
        var shield = new RealTimeShield(scanner, quarantine, logger);
        var ipc = new IpcServer(scanner, quarantine, logger);

        var worker = new AV.Service.Worker(logger, scanner, quarantine, shield, ipc);

        Assert.NotNull(worker);
    }
}
