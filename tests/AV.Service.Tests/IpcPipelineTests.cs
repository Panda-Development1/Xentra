using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using AV.Engine.Models;
using AV.Engine.Services;
using AV.Service.Ipc;
using Xunit;

namespace AV.Service.Tests;

public class IpcPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"xentra_pipe_{Guid.NewGuid():N}");
    private readonly CancellationTokenSource _cts = new();
    private readonly List<IDisposable> _disposables = new();
    private string _pipeName = "XentraAVTest_" + Guid.NewGuid().ToString("N");

    public IpcPipelineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { }
        }
        _cts.Cancel();
        _cts.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private IpcServer StartServer()
    {
        _pipeName = "XentraAVTest_" + Guid.NewGuid().ToString("N");
        var sigStore = new SignatureStore();
        sigStore.LoadSignaturesAsync().GetAwaiter().GetResult();
        var logger = new AV.Engine.Services.Logger(Path.Combine(_root, "logs"));
        var scanner = new Scanner(sigStore, logger, customExclusions: Array.Empty<string>());
        var quarantine = new AV.Engine.Services.Quarantine(Path.Combine(_root, "q"), logger);
        var server = new IpcServer(scanner, quarantine, logger, _pipeName);
        server.Start(_cts.Token);
        _disposables.Add(server);
        return server;
    }

    private (NamedPipeClientStream client, StreamReader reader, StreamWriter writer) Connect()
    {
        var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        client.Connect(3000);
        var reader = new StreamReader(client, new System.Text.UTF8Encoding(false));
        var writer = new StreamWriter(client, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        _disposables.Add(client);
        _disposables.Add(reader);
        _disposables.Add(writer);
        var auth = reader.ReadLine()!;
        Assert.StartsWith("AUTH:", auth);
        writer.WriteLine("TOKEN:" + auth[5..]);
        Assert.Equal("OK:Authenticated", reader.ReadLine());
        return (client, reader, writer);
    }

    [Fact(Timeout = 20000)]
    public void Status_And_Empty_Quarantine()
    {
        StartServer();
        var (_, reader, writer) = Connect();

        writer.WriteLine("GET_STATUS");
        Assert.Equal("STATUS:Running|Quarantined:0", reader.ReadLine());

        writer.WriteLine("GET_QUARANTINE_LIST");
        Assert.Equal("QUARANTINE_LIST:EMPTY", reader.ReadLine());
    }

    [Fact(Timeout = 20000)]
    public void Scan_Directory_Reports_Clean_And_Complete()
    {
        StartServer();
        var (_, reader, writer) = Connect();

        var dir = Path.Combine(_root, "scan");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "clean");

        writer.WriteLine($"START_SCAN {dir}");
        Assert.Equal("SCAN_STARTING", reader.ReadLine());
        Assert.StartsWith("SCAN_RESULT:Clean|", reader.ReadLine()!);
        Assert.Equal("SCAN_COMPLETE:1|0|0", reader.ReadLine());
    }

    [Fact(Timeout = 20000)]
    public void Scan_Detects_Threat_And_Restore_Roundtrips()
    {
        var server = StartServer();
        var (_, reader, writer) = Connect();

        var dir = Path.Combine(_root, "incoming");
        Directory.CreateDirectory(dir);
        var threatPath = Path.Combine(dir, "evil.bin");
        File.WriteAllText(threatPath, "payload");

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(threatPath))).ToUpperInvariant();
        var scannerField = typeof(IpcServer).GetField("_scanner", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var scanner = (Scanner)scannerField.GetValue(server)!;
        var sigField = typeof(Scanner).GetField("_signatureStore", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var sigStore = (SignatureStore)sigField.GetValue(scanner)!;
        var liveIndex = (System.Collections.Concurrent.ConcurrentDictionary<string, DetectionResult>)
            typeof(SignatureStore).GetField("_hashIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sigStore)!;
        var added = liveIndex.TryAdd(hash, new DetectionResult { IsMalicious = true, ThreatName = "Test-Threat", SignatureId = "TEST" });

        writer.WriteLine($"START_SCAN {dir}");
        Assert.Equal("SCAN_STARTING", reader.ReadLine());
        Assert.StartsWith("SCAN_RESULT:Threat|", reader.ReadLine()!);
        Assert.StartsWith("SCAN_DETECTION:", reader.ReadLine()!);
        Assert.Equal("SCAN_COMPLETE:1|1|0", reader.ReadLine());

        // The scan only reports a threat; nothing is quarantined yet. Create a
        // quarantine entry directly through the server's store, then verify the
        // list / restore / delete IPC commands round-trip.
        var qField = typeof(IpcServer).GetField("_quarantine", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var quarantine = (AV.Engine.Interfaces.IQuarantine)qField.GetValue(server)!;
        quarantine.QuarantineAsync(threatPath, new DetectionResult { IsMalicious = true, ThreatName = "Test-Threat", SignatureId = "TEST" }).GetAwaiter().GetResult();
        Assert.False(File.Exists(threatPath), "file should have been moved into quarantine");

        writer.WriteLine("GET_QUARANTINE_LIST");
        var listLine = reader.ReadLine()!;
        Assert.StartsWith("QUARANTINE_LIST:QUARANTINE_ENTRY:", listLine);
        var id = listLine["QUARANTINE_LIST:QUARANTINE_ENTRY:".Length..].Split('|')[0];

        writer.WriteLine($"RESTORE_FILE {id}");
        Assert.Equal("OK:Restored", reader.ReadLine());
        Assert.True(File.Exists(threatPath), "file should be restored to original path");

        writer.WriteLine($"DELETE_FILE {id}");
        Assert.Equal("OK:Deleted", reader.ReadLine());
    }
}
