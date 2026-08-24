using AV.Engine.Interfaces;
using AV.Engine.Services;
using AV.Service.Ipc;
using AV.Service.Services;

namespace AV.Service;

public class Program
{
    public static async Task Main(string[] args)
    {
        var signatureStore = new SignatureStore();
        await signatureStore.LoadSignaturesAsync();

        var host = Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "XentraAV";
            })
            .ConfigureServices(services =>
            {
                string basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "XentraAV");
                Directory.CreateDirectory(basePath);

                string quarantinePath = Path.Combine(basePath, "Quarantine");
                string logPath = Path.Combine(basePath, "Logs");

                services.AddSingleton<AV.Engine.Interfaces.ILogger>(new Logger(logPath));
                services.AddSingleton<ISignatureStore>(signatureStore);
                services.AddSingleton<IScanner>(sp =>
                    new Scanner(sp.GetRequiredService<ISignatureStore>(), sp.GetRequiredService<AV.Engine.Interfaces.ILogger>()));
                services.AddSingleton<IQuarantine>(sp =>
                    new Quarantine(quarantinePath, sp.GetRequiredService<AV.Engine.Interfaces.ILogger>()));
                services.AddSingleton<RealTimeShield>();
                services.AddSingleton<IpcServer>();
                services.AddHostedService<Worker>();
            })
            .Build();

        await host.RunAsync();
    }
}
