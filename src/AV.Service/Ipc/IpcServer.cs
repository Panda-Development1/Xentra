using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

            while (!ct.IsCancellationRequested && server.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                var response = await ProcessCommandAsync(line, ct);
                await writer.WriteLineAsync(response);
            }
        }
        catch { }
        finally
        {
            server.Dispose();
        }
    }

    private async Task<string> ProcessCommandAsync(string command, CancellationToken ct)
    {
        var parts = command.Split(' ', 2);
        var cmd = parts[0].ToUpperInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";

        return cmd switch
        {
            "GET_STATUS" => JsonSerializer.Serialize(new { Status = "Running", ScannedCount = 0 }),
            "START_SCAN" => await HandleStartScanAsync(arg, ct),
            "GET_QUARANTINE_LIST" => await HandleGetQuarantineListAsync(ct),
            "RESTORE_FILE" => await HandleRestoreFileAsync(arg, ct),
            "DELETE_FILE" => await HandleDeleteFileAsync(arg, ct),
            _ => "ERR:Unknown command"
        };
    }

    private async Task<string> HandleStartScanAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "ERR:No path specified";

        if (Directory.Exists(path))
        {
            var results = await _scanner.ScanDirectoryAsync(path, ct);
            return JsonSerializer.Serialize(results);
        }
        else if (File.Exists(path))
        {
            var result = await _scanner.ScanFileAsync(path, ct);
            return JsonSerializer.Serialize(result);
        }

        return "ERR:Path not found";
    }

    private async Task<string> HandleGetQuarantineListAsync(CancellationToken ct)
    {
        var entries = await _quarantine.ListAsync(ct);
        return JsonSerializer.Serialize(entries);
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
