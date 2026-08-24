using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    private const string RepoOwner = "Panda-Development1";
    private const string RepoName = "Xentra";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        var token = Environment.GetEnvironmentVariable("XENTRA_GITHUB_TOKEN") ?? "";
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XentraAV-Installer", "1.0"));
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var currentStr = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";

        var response = await _http.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        var latestVersion = tagName.TrimStart('v');
        var releaseNotes = root.GetProperty("body").GetString() ?? "";

        if (!Version.TryParse(latestVersion, out var latest) || !Version.TryParse(currentStr, out var current))
            return null;

        if (latest <= current)
            return null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        return new UpdateInfo
        {
            CurrentVersion = currentStr,
            LatestVersion = latestVersion,
            DownloadUrl = downloadUrl ?? "",
            ReleaseNotes = releaseNotes
        };
    }

    public async Task<string> DownloadUpdateAsync(IProgress<double>? progress = null)
    {
        var updateInfo = await CheckForUpdateAsync();
        if (updateInfo == null)
            throw new InvalidOperationException("No update available");

        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            throw new InvalidOperationException("No download URL found in release assets");

        var tempDir = Path.Combine(Path.GetTempPath(), "XentraAV_Update_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var zipPath = Path.Combine(tempDir, "update.zip");

        using (var response = await _http.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
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

        var extractDir = Path.Combine(tempDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);
        File.Delete(zipPath);

        return extractDir;
    }
}
