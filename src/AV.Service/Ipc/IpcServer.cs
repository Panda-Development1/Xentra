using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using AV.Engine.Interfaces;
using AV.Engine.Models;
using AV.Service.Models;

namespace AV.Service.Ipc;

public class IpcServer : IDisposable
{
    private readonly IScanner _scanner;
    private readonly IQuarantine _quarantine;
    private readonly AV.Engine.Interfaces.ILogger _logger;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;

    public IpcServer(IScanner scanner, IQuarantine quarantine, AV.Engine.Interfaces.ILogger logger, string pipeName = "XentraAV")
    {
        _scanner = scanner;
        _quarantine = quarantine;
        _logger = logger;
        _pipeName = pipeName;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ListenAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(ct);
                _ = HandleClientAsync(server, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { server.Dispose(); }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync($"AUTH:{token}");
            var authLine = await reader.ReadLineAsync(ct);
            if (authLine != $"TOKEN:{token}")
            {
                await writer.WriteLineAsync("ERR:Unauthorized");
                return;
            }

            await writer.WriteLineAsync("OK:Authenticated");

            while (!ct.IsCancellationRequested && server.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                var response = await ProcessCommandAsync(line, writer, ct);
                if (response != null)
                    await writer.WriteLineAsync(response);
            }
        }
        catch { }
        finally
        {
            server.Dispose();
        }
    }

    private async Task<string?> ProcessCommandAsync(string command, StreamWriter writer, CancellationToken ct)
    {
        var parts = command.Split(' ', 2);
        var cmd = parts[0].ToUpperInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";

        return cmd switch
        {
            "GET_STATUS" => await HandleGetStatusAsync(ct),
            "START_SCAN" => await HandleStartScanAsync(arg, writer, ct),
            "GET_QUARANTINE_LIST" => await HandleGetQuarantineListAsync(ct),
            "RESTORE_FILE" => await HandleRestoreFileAsync(arg, ct),
            "DELETE_FILE" => await HandleDeleteFileAsync(arg, ct),
            "PING" => "PONG",
            _ => "ERR:Unknown command"
        };
    }

    private async Task<string> HandleGetStatusAsync(CancellationToken ct)
    {
        var quarantineEntries = await _quarantine.ListAsync(ct);
        return $"STATUS:Running|Quarantined:{quarantineEntries.Count}";
    }

    private async Task<string> HandleStartScanAsync(string path, StreamWriter writer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "ERR:No path specified";

        if (File.Exists(path))
        {
            var result = await _scanner.ScanFileAsync(path, ct);
            await writer.WriteLineAsync($"SCAN_RESULT:{result.Status}|{result.FilePath}");
            if (result.Detection != null)
                await writer.WriteLineAsync($"SCAN_DETECTION:{result.Detection.ThreatName}|{result.FilePath}");
            await writer.WriteLineAsync("SCAN_COMPLETE:1|" + (result.Status == "Threat" ? "1" : "0") + "|0");
            return null;
        }

        if (Directory.Exists(path))
        {
            int total = 0, threats = 0, errors = 0;

            await writer.WriteLineAsync("SCAN_STARTING");

            var results = await _scanner.ScanDirectoryAsync(path, ct);

            foreach (var r in results)
            {
                total++;
                if (r.Status == "Threat") threats++;
                if (r.Status == "Error") errors++;

                await writer.WriteLineAsync($"SCAN_RESULT:{r.Status}|{r.FilePath}");
                if (r.Detection != null)
                    await writer.WriteLineAsync($"SCAN_DETECTION:{r.Detection.ThreatName}|{r.FilePath}");

                if (total % 10 == 0)
                    await writer.WriteLineAsync($"SCAN_PROGRESS:{total}|{r.FilePath}");
            }

            await writer.WriteLineAsync($"SCAN_COMPLETE:{total}|{threats}|{errors}");
            return null;
        }

        return "ERR:Path not found";
    }

    private async Task<string> HandleGetQuarantineListAsync(CancellationToken ct)
    {
        var entries = await _quarantine.ListAsync(ct);
        if (entries.Count == 0)
            return "QUARANTINE_LIST:EMPTY";

        var lines = entries.Select(e =>
            $"QUARANTINE_ENTRY:{e.Id}|{e.OriginalFileName}|{e.ThreatName}|{e.QuarantinedAt:O}|{e.OriginalPath}");

        return string.Join("\n", lines);
    }

    private async Task<string> HandleRestoreFileAsync(string quarantineId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(quarantineId))
            return "ERR:No quarantine ID specified";

        await _quarantine.RestoreAsync(quarantineId, ct);
        return "OK:Restored";
    }

    private async Task<string> HandleDeleteFileAsync(string quarantineId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(quarantineId))
            return "ERR:No quarantine ID specified";

        await _quarantine.DeleteAsync(quarantineId, ct);
        return "OK:Deleted";
    }
}
