using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace AV.Installer.Services;

public class UpdateInfo
{
    public string LatestVersion { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
}

public class UpdateService
{
    // Raw file in the repo — readable without auth or special token scopes.
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/Panda-Development1/Xentra/main/update.json";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XentraAV-Installer", "1.0"));
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var currentStr = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";

        string json;
        try
        {
            json = await _http.GetStringAsync(UpdateManifestUrl);
        }
        catch
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var latestVersion = root.GetProperty("version").GetString() ?? "";
        var downloadUrl = root.GetProperty("downloadUrl").GetString() ?? "";
        var notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";

        if (!Version.TryParse(latestVersion, out var latest) || !Version.TryParse(currentStr, out var current))
            return null;

        if (latest <= current)
            return null;

        return new UpdateInfo
        {
            CurrentVersion = currentStr,
            LatestVersion = latestVersion,
            DownloadUrl = downloadUrl,
            ReleaseNotes = notes
        };
    }

    // Downloads the new installer exe into a temp folder and returns that folder.
    public async Task<string> DownloadUpdateAsync(IProgress<double>? progress = null)
    {
        var updateInfo = await CheckForUpdateAsync();
        if (updateInfo == null)
            throw new InvalidOperationException("No update available");

        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            throw new InvalidOperationException("Update manifest has no download URL");

        var tempDir = Path.Combine(Path.GetTempPath(), "XentraAV_Update_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var exePath = Path.Combine(tempDir, "AV.Installer.exe");

        using (var response = await _http.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes * 100);
            }
        }

        progress?.Report(100);
        return tempDir;
    }
}
