using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuebecsCave.Downloader.Api;

public sealed record IngestStreamRequest(
    string Title,
    string? Description,
    int GameId,
    DateTimeOffset StreamedAt,
    int DurationSeconds,
    string VideoUrl,
    string? ThumbnailUrl,
    string? TwitchVodId);

public sealed record IngestStreamResponse(int StreamId, int GameId);

public sealed record GameDto(
    int Id, string Name, string Slug, string? IconUrl, string? TwitchGameId, DateTimeOffset CreatedAt);

public sealed record GameForTimeDto(int GameId, string Slug, string Name);

public sealed record StreamNeedingThumbnailDto(int StreamId, string TwitchVodId, string GameSlug, int StreamedAtYear);

public sealed record UpdateThumbnailRequest(string ThumbnailUrl);

public sealed record DownloaderEventDto(
    string Stage,
    string? TwitchVodId,
    bool Success,
    int? DurationMs,
    string? Payload,
    string? Message,
    DateTimeOffset? OccurredAt);

public sealed record ErrorEventDto(
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? Context,
    DateTimeOffset? OccurredAt);

public interface ICaveApiClient
{
    Task<IReadOnlyList<string>> GetKnownVodIdsAsync(CancellationToken cancellationToken);
    Task<IngestStreamResponse> CreateStreamAsync(IngestStreamRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<GameDto>> ListGamesAsync(CancellationToken cancellationToken);
    Task<GameDto?> GetGameBySlugAsync(string slug, CancellationToken cancellationToken);
    Task<GameForTimeDto?> GetGameForTimeAsync(string broadcasterId, DateTimeOffset at, CancellationToken cancellationToken);
    Task<IReadOnlyList<StreamNeedingThumbnailDto>> ListStreamsNeedingThumbnailsAsync(CancellationToken cancellationToken);
    Task UpdateThumbnailAsync(int streamId, string thumbnailUrl, CancellationToken cancellationToken);
    Task PostDownloaderEventsAsync(IReadOnlyList<DownloaderEventDto> events, CancellationToken cancellationToken);
    Task PostErrorsAsync(IReadOnlyList<ErrorEventDto> errors, CancellationToken cancellationToken);
}

public sealed class CaveApiClient : ICaveApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CaveApiClient> _logger;

    public CaveApiClient(
        HttpClient http,
        IOptions<CaveApiOptions> options,
        ILogger<CaveApiClient> logger)
    {
        _http = http;
        _logger = logger;
        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.BaseUrl))
        {
            throw new InvalidOperationException("Api:BaseUrl is required.");
        }
        _http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Add("X-Api-Key", opts.ServiceKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<string>> GetKnownVodIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await _http.GetFromJsonAsync<List<string>>("api/ingest/known-vod-ids", cancellationToken);
        return ids ?? new List<string>();
    }

    public async Task<IngestStreamResponse> CreateStreamAsync(IngestStreamRequest request, CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsJsonAsync("api/ingest/streams", request, cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IngestStreamResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("API returned no body for ingest/streams");
    }

    public async Task<GameForTimeDto?> GetGameForTimeAsync(string broadcasterId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var url = $"api/ingest/game-for-time?broadcasterId={Uri.EscapeDataString(broadcasterId)}" +
                  $"&at={Uri.EscapeDataString(at.ToString("o"))}";
        try
        {
            return await _http.GetFromJsonAsync<GameForTimeDto>(url, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<StreamNeedingThumbnailDto>> ListStreamsNeedingThumbnailsAsync(CancellationToken cancellationToken)
    {
        var list = await _http.GetFromJsonAsync<List<StreamNeedingThumbnailDto>>(
            "api/ingest/streams-needing-thumbnails", cancellationToken);
        return list ?? new List<StreamNeedingThumbnailDto>();
    }

    public async Task UpdateThumbnailAsync(int streamId, string thumbnailUrl, CancellationToken cancellationToken)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/ingest/streams/{streamId}/thumbnail",
            new UpdateThumbnailRequest(thumbnailUrl),
            cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<GameDto>> ListGamesAsync(CancellationToken cancellationToken)
    {
        var games = await _http.GetFromJsonAsync<List<GameDto>>("api/games", cancellationToken);
        return games ?? new List<GameDto>();
    }

    public async Task<GameDto?> GetGameBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.GetFromJsonAsync<GameDto>($"api/games/{Uri.EscapeDataString(slug)}", cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task PostDownloaderEventsAsync(IReadOnlyList<DownloaderEventDto> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;
        try
        {
            using var resp = await _http.PostAsJsonAsync("api/ingest/downloader-events", events, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            // Never let event-emit failures break the actual work loop.
            _logger.LogWarning(ex, "Failed to post {Count} downloader event(s); continuing.", events.Count);
        }
    }

    public async Task PostErrorsAsync(IReadOnlyList<ErrorEventDto> errors, CancellationToken cancellationToken)
    {
        if (errors.Count == 0) return;
        try
        {
            using var resp = await _http.PostAsJsonAsync("api/ingest/errors", errors, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post {Count} error(s); continuing.", errors.Count);
        }
    }
}
