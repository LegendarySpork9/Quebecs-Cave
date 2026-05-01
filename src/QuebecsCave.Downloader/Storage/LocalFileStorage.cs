using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuebecsCave.Downloader.Storage;

public interface IFileStorage
{
    string BuildVideoPath(string gameSlug, int year, string vodId);
    string BuildThumbnailPath(string gameSlug, int year, string vodId);
    string ToPublicVideoUrl(string gameSlug, int year, string vodId);
    string ToPublicThumbnailUrl(string gameSlug, int year, string vodId);
    Task<bool> DownloadFileAsync(string url, string outputPath, CancellationToken cancellationToken);
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly StorageOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<StorageOptions> options,
        HttpClient http,
        ILogger<LocalFileStorage> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_options.VideoRoot))
        {
            throw new InvalidOperationException("Storage:VideoRoot is required.");
        }
    }

    public string BuildVideoPath(string gameSlug, int year, string vodId) =>
        Path.Combine(_options.VideoRoot, SanitizeSegment(gameSlug), year.ToString(), $"{SanitizeSegment(vodId)}.mp4");

    public string BuildThumbnailPath(string gameSlug, int year, string vodId) =>
        Path.Combine(_options.VideoRoot, SanitizeSegment(gameSlug), year.ToString(), $"{SanitizeSegment(vodId)}.jpg");

    public string ToPublicVideoUrl(string gameSlug, int year, string vodId) =>
        BuildPublicUrl($"{Uri.EscapeDataString(SanitizeSegment(gameSlug))}/{year}/{Uri.EscapeDataString(SanitizeSegment(vodId))}.mp4");

    public string ToPublicThumbnailUrl(string gameSlug, int year, string vodId) =>
        BuildPublicUrl($"{Uri.EscapeDataString(SanitizeSegment(gameSlug))}/{year}/{Uri.EscapeDataString(SanitizeSegment(vodId))}.jpg");

    public async Task<bool> DownloadFileAsync(string url, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Thumbnail download {Url} returned {Status}", url, (int)resp.StatusCode);
                return false;
            }
            await using var dst = File.Create(outputPath);
            await resp.Content.CopyToAsync(dst, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail download {Url} failed.", url);
            return false;
        }
    }

    private string BuildPublicUrl(string suffix)
    {
        if (string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            // Fall back to the local file path expressed as a file:// URL —
            // useful for smoke tests when the IIS video site isn't configured yet.
            return new Uri(Path.Combine(_options.VideoRoot, suffix.Replace('/', Path.DirectorySeparatorChar)))
                .ToString();
        }
        return _options.PublicBaseUrl.TrimEnd('/') + "/" + suffix;
    }

    private static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment)) return "_";
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[segment.Length];
        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            buffer[i] = invalid.Contains(c) || c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar
                ? '-'
                : c;
        }
        return new string(buffer);
    }
}
