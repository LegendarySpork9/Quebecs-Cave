namespace QuebecsCave.Downloader;

public sealed class DownloaderOptions
{
    public const string SectionName = "Downloader";

    public int PollSeconds { get; set; } = 600;
    public string YtDlpPath { get; set; } = "yt-dlp";
    public int VodTake { get; set; } = 50;
    public string DefaultGameSlug { get; set; } = "variety";

    /// <summary>
    /// When true: skip yt-dlp, fabricate placeholder file paths. Lets the rest
    /// of the loop (diff, ingest, event tracking) run without actually
    /// downloading hours of video. Useful for smoke tests and CI.
    /// </summary>
    public bool DryRun { get; set; }
}

public sealed class CaveApiOptions
{
    public const string SectionName = "Api";
    public string BaseUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string VideoRoot { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
}

public sealed class TwitchBroadcasterOptions
{
    public const string SectionName = "Twitch";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string BroadcasterUserId { get; set; } = "";
}
