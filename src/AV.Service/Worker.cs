using AV.Engine.Interfaces;
using AV.Engine.Services;
using AV.Service.Ipc;
using AV.Service.Services;

namespace AV.Service;

public class Worker : BackgroundService
{
    private readonly AV.Engine.Interfaces.ILogger _logger;
    private readonly IScanner _scanner;
    private readonly IQuarantine _quarantine;
    private readonly RealTimeShield _shield;
    private readonly IpcServer _ipcServer;

    public Worker(AV.Engine.Interfaces.ILogger logger, IScanner scanner, IQuarantine quarantine, RealTimeShield shield, IpcServer ipcServer)
    {
        _logger = logger;
        _scanner = scanner;
        _quarantine = quarantine;
        _shield = shield;
        _ipcServer = ipcServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _logger.LogAsync("INFO", "Xentra AV Service starting...", "Worker", stoppingToken);

        _ipcServer.Start(stoppingToken);
        _shield.StartWatching();

        await _logger.LogAsync("INFO", "Xentra AV Service running.", "Worker", stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _logger.LogAsync("INFO", "Xentra AV Service stopping...", "Worker", cancellationToken);
        _shield.StopWatching();
        _ipcServer.Stop();
        await base.StopAsync(cancellationToken);
    }
}
