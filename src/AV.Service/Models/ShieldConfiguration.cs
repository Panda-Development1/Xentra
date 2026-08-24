namespace AV.Service.Models;

public class ShieldConfiguration
{
    public static string DefaultWatchPath { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string WatchPath { get; set; } = DefaultWatchPath;
    public int DebounceMs { get; set; } = 100;
    public int MaxRetries { get; set; } = 3;
    public bool AutoQuarantine { get; set; } = true;
}
