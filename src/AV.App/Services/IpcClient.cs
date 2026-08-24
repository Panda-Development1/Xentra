using System.IO;
using System.IO.Pipes;
using System.Text;

namespace AV.App.Services;

public class IpcClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _authenticated;
    private int _connecting;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsConnected => _client?.IsConnected == true && _authenticated;

    public IpcClient(string pipeName = "XentraAV")
    {
        _pipeName = pipeName;
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        // Prevent the reconnect timer and the initial connect from racing on the same pipe.
        if (Interlocked.Exchange(ref _connecting, 1) == 1)
            return false;

        try
        {
            _client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _client.ConnectAsync(3000, ct);

            _reader = new StreamReader(_client, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(_client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var authLine = await _reader.ReadLineAsync(ct);
            if (authLine != null && authLine.StartsWith("AUTH:"))
            {
                string token = authLine[5..];
                await _writer.WriteLineAsync($"TOKEN:{token}");

                var response = await _reader.ReadLineAsync(ct);
                if (response == "OK:Authenticated")
                {
                    _authenticated = true;
                    return true;
                }
            }

            Disconnect();
            return false;
        }
        catch
        {
            Disconnect();
            return false;
        }
        finally
        {
            _connecting = 0;
        }
    }

    public void Disconnect()
    {
        _authenticated = false;
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }

    public async Task<string?> SendCommandAsync(string command, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_writer == null || _reader == null || !IsConnected)
                return null;

            await _writer.WriteLineAsync(command);
            return await _reader.ReadLineAsync(ct);
        }
        catch
        {
            Disconnect();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<string>> SendScanCommandAsync(string path, Action<string>? onLineReceived, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var responses = new List<string>();

        try
        {
            if (_writer == null || _reader == null || !IsConnected)
                return responses;

            await _writer.WriteLineAsync($"START_SCAN {path}");

            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;

                responses.Add(line);
                onLineReceived?.Invoke(line);

                if (line.StartsWith("SCAN_COMPLETE:") || line.StartsWith("ERR:"))
                    break;
            }
        }
        catch
        {
            Disconnect();
        }
        finally
        {
            _gate.Release();
        }

        return responses;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
