using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AV.Installer.Services;

public class InstallerService
{
    private readonly IInstallerLogger _logger;

    public InstallerService(IInstallerLogger logger)
    {
        _logger = logger;
    }

    public async Task InstallAsync(string installPath, bool pinToTaskbar, bool createDesktopShortcut, IProgress<double>? progress = null)
    {
        _logger.Log("Starting installation...");

        try
        {
            _logger.Log($"Creating directory: {installPath}");
            Directory.CreateDirectory(installPath);
            progress?.Report(10);

            string programData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "XentraAV");

            _logger.Log($"Creating program data directories: {programData}");
            Directory.CreateDirectory(Path.Combine(programData, "Quarantine"));
            Directory.CreateDirectory(Path.Combine(programData, "Logs"));
            Directory.CreateDirectory(Path.Combine(programData, "Signatures"));
            progress?.Report(20);

            _logger.Log("Setting quarantine ACLs (SYSTEM + Admins only)...");
            SetQuarantineAcl(Path.Combine(programData, "Quarantine"));
            progress?.Report(30);

            _logger.Log("Copying files...");
            await CopyFilesAsync(installPath, progress);

            _logger.Log("Registering Windows service...");
            RegisterService(installPath);
            progress?.Report(80);

            if (createDesktopShortcut)
            {
                _logger.Log("Creating desktop shortcut...");
                CreateShortcut(installPath, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            }

            if (pinToTaskbar)
            {
                _logger.Log("Pinning to taskbar...");
                CreateShortcut(installPath, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"));
            }

            progress?.Report(100);
            _logger.Log("Installation complete!");
        }
        catch (Exception ex)
        {
            _logger.Log($"Installation failed: {ex.Message}");
            throw;
        }
    }

    private async Task CopyFilesAsync(string installPath, IProgress<double>? progress = null)
    {
        string sourceDir = AppDomain.CurrentDomain.BaseDirectory;
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        int total = files.Length;
        int copied = 0;

        foreach (var file in files)
        {
            string relativePath = Path.GetRelativePath(sourceDir, file);
            string destPath = Path.Combine(installPath, relativePath);

            string? dir = Path.GetDirectoryName(destPath);
            if (dir != null) Directory.CreateDirectory(dir);

            File.Copy(file, destPath, true);
            copied++;
            _logger.Log($"  Copied: {relativePath}");

            if (total > 0)
                progress?.Report(30 + (double)copied / total * 50);

            await Task.Yield();
        }
    }

    private void RegisterService(string installPath)
    {
        string serviceExe = Path.Combine(installPath, "AV.Service.exe");
        var psi = new ProcessStartInfo("sc.exe", $"create XentraAV binPath= \"{serviceExe}\" start= auto")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        var process = Process.Start(psi);
        process?.WaitForExit(30000);

        if (process?.ExitCode == 0)
        {
            _logger.Log("Service registered successfully.");
            psi = new ProcessStartInfo("sc.exe", "start XentraAV")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            process = Process.Start(psi);
            process?.WaitForExit(30000);
        }
        else
        {
            _logger.Log($"Service registration returned exit code: {process?.ExitCode}");
        }
    }

    private void SetQuarantineAcl(string path)
    {
        var di = new DirectoryInfo(path);
        var acl = di.GetAccessControl();
        acl.SetAccessRuleProtection(true, false);

        var systemRule = new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow);
        acl.AddAccessRule(systemRule);

        var adminRule = new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow);
        acl.AddAccessRule(adminRule);

        di.SetAccessControl(acl);
    }

    private void CreateShortcut(string installPath, string shortcutFolder)
    {
        try
        {
            var shell = new IWshRuntimeLibrary.WshShell();
            string shortcutPath = Path.Combine(shortcutFolder, "Xentra AV.lnk");
            var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.Combine(installPath, "AV.App.exe");
            shortcut.WorkingDirectory = installPath;
            shortcut.Description = "Xentra AV Antivirus";
            shortcut.Save();
        }
        catch { }
    }
}
