using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;

namespace QuebecsCave.Services.Twitch;

public sealed class TwitchHelixClient : ITwitchClient
{
    private const string IdHost = "https://id.twitch.tv";
    private const string ApiHost = "https://api.twitch.tv";

    private readonly HttpClient _http;
    private readonly TwitchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<TwitchHelixClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _appAccessToken;
    private DateTimeOffset _appAccessTokenExpiresAt;

    public TwitchHelixClient(
        HttpClient http,
        IOptions<TwitchOptions> options,
        IClock clock,
        ILogger<TwitchHelixClient> logger)
    {
        _http = http;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TwitchVod>> GetUserVodsAsync(
        string userId,
        int take,
        string type,
        CancellationToken cancellationToken)
    {
        var query = $"?user_id={Uri.EscapeDataString(userId)}&type={Uri.EscapeDataString(type)}&first={take}";
        var payload = await GetAsync<HelixListResponse<HelixVideoDto>>(
            ApiHost + "/helix/videos" + query, cancellationToken);
        if (payload?.Data is null) return Array.Empty<TwitchVod>();

        return payload.Data
            .Select(v => new TwitchVod(
                v.Id ?? "",
                v.UserId ?? "",
                v.UserLogin ?? "",
                v.Title ?? "",
                v.Description ?? "",
                ParseDate(v.CreatedAt),
                ParseDate(v.PublishedAt),
                v.Url ?? "",
                v.ThumbnailUrl ?? "",
                v.ViewCount,
                v.Type ?? "",
                ParseDuration(v.Duration)))
            .ToArray();
    }

    public async Task<IReadOnlyList<TwitchGame>> GetGamesByNameAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0) return Array.Empty<TwitchGame>();
        var query = string.Join("&", names.Select(n => "name=" + Uri.EscapeDataString(n)));
        return await GetGamesAsync("?" + query, cancellationToken);
    }

    public async Task<IReadOnlyList<TwitchGame>> GetGamesByIdAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return Array.Empty<TwitchGame>();
        var query = string.Join("&", ids.Select(i => "id=" + Uri.EscapeDataString(i)));
        return await GetGamesAsync("?" + query, cancellationToken);
    }

    private async Task<IReadOnlyList<TwitchGame>> GetGamesAsync(string query, CancellationToken cancellationToken)
    {
        var payload = await GetAsync<HelixListResponse<HelixGameDto>>(
            ApiHost + "/helix/games" + query, cancellationToken);
        if (payload?.Data is null) return Array.Empty<TwitchGame>();
        return payload.Data
            .Select(g => new TwitchGame(g.Id ?? "", g.Name ?? "", g.BoxArtUrl ?? ""))
            .ToArray();
    }

    public async Task<TwitchUserToken> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
        });
        using var resp = await _http.PostAsync(IdHost + "/oauth2/token", form, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<HelixUserTokenDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Twitch returned no token");
        return MapToken(dto);
    }

    public async Task<TwitchUserToken> RefreshUserTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
        });
        using var resp = await _http.PostAsync(IdHost + "/oauth2/token", form, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<HelixUserTokenDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Twitch returned no token");
        return MapToken(dto);
    }

    public async Task<TwitchUser?> GetUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiHost + "/helix/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("Client-Id", _options.ClientId);
        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;
        var payload = await resp.Content.ReadFromJsonAsync<HelixListResponse<HelixUserDto>>(cancellationToken: cancellationToken);
        var u = payload?.Data?.FirstOrDefault();
        if (u is null) return null;
        return new TwitchUser(
            u.Id ?? "",
            u.Login ?? "",
            u.DisplayName ?? "",
            string.IsNullOrEmpty(u.Email) ? null : u.Email,
            u.ProfileImageUrl ?? "",
            string.IsNullOrEmpty(u.Description) ? null : u.Description);
    }

    public async Task<IReadOnlyList<TwitchModerator>> GetModeratorsAsync(string broadcasterId, string accessToken, CancellationToken cancellationToken)
    {
        var moderators = new List<TwitchModerator>();
        var cursor = "";
        do
        {
            var url = ApiHost + "/helix/moderation/moderators?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) + "&first=100";
            if (!string.IsNullOrEmpty(cursor)) url += "&after=" + Uri.EscapeDataString(cursor);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("Client-Id", _options.ClientId);
            using var resp = await _http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Twitch /moderation/moderators returned {Status}: {Body}",
                    (int)resp.StatusCode, body);
                break;
            }
            var payload = await resp.Content.ReadFromJsonAsync<HelixPagedResponse<HelixModeratorDto>>(cancellationToken: cancellationToken);
            if (payload?.Data is null) break;
            foreach (var m in payload.Data)
            {
                moderators.Add(new TwitchModerator(m.UserId ?? "", m.UserLogin ?? "", m.UserName ?? ""));
            }
            cursor = payload.Pagination?.Cursor ?? "";
        }
        while (!string.IsNullOrEmpty(cursor));

        return moderators;
    }

    private static TwitchUserToken MapToken(HelixUserTokenDto dto) =>
        new(
            dto.AccessToken ?? "",
            dto.RefreshToken ?? "",
            dto.ExpiresIn,
            dto.TokenType ?? "",
            dto.Scope ?? Array.Empty<string>());

    public async Task<TwitchLiveStream?> GetLiveStreamAsync(string userId, CancellationToken cancellationToken)
    {
        var query = "?user_id=" + Uri.EscapeDataString(userId);
        var payload = await GetAsync<HelixListResponse<HelixStreamDto>>(
            ApiHost + "/helix/streams" + query, cancellationToken);
        var s = payload?.Data?.FirstOrDefault();
        if (s is null) return null;
        return new TwitchLiveStream(
            s.UserId ?? "",
            s.UserLogin ?? "",
            s.GameId ?? "",
            s.GameName ?? "",
            s.Title ?? "",
            s.ViewerCount,
            ParseDate(s.StartedAt),
            s.ThumbnailUrl ?? "");
    }

    // ---- HTTP plumbing ----------------------------------------------------

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var req = await BuildRequestAsync(HttpMethod.Get, url, cancellationToken);
        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Twitch Helix request {Method} {Url} returned {Status}: {Body}",
                req.Method, url, (int)resp.StatusCode, body);
            return default;
        }
        return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var token = await GetAppAccessTokenAsync(cancellationToken);
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Client-Id", _options.ClientId);
        return req;
    }

    private async Task<string> GetAppAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_appAccessToken is not null && _appAccessTokenExpiresAt > _clock.Now.AddMinutes(2))
        {
            return _appAccessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_appAccessToken is not null && _appAccessTokenExpiresAt > _clock.Now.AddMinutes(2))
            {
                return _appAccessToken;
            }

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
            });
            using var resp = await _http.PostAsync(IdHost + "/oauth2/token", form, cancellationToken);
            resp.EnsureSuccessStatusCode();
            var payload = await resp.Content.ReadFromJsonAsync<HelixTokenDto>(cancellationToken: cancellationToken);
            if (payload?.AccessToken is null)
            {
                throw new InvalidOperationException("Twitch returned no access_token");
            }

            _appAccessToken = payload.AccessToken;
            _appAccessTokenExpiresAt = _clock.Now.AddSeconds(payload.ExpiresIn);
            return _appAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---- Helpers ---------------------------------------------------------

    private static DateTimeOffset ParseDate(string? iso) =>
        DateTimeOffset.TryParse(iso, out var d) ? d : default;

    private static int ParseDuration(string? text)
    {
        // Twitch returns e.g. "3h8m33s", "45m12s", "2h", "59s".
        if (string.IsNullOrEmpty(text)) return 0;
        var m = Regex.Match(text, @"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$");
        if (!m.Success) return 0;
        int h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        int s = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return h * 3600 + min * 60 + s;
    }

    // ---- DTOs -----------------------------------------------------------

    private class HelixListResponse<T>
    {
        [JsonPropertyName("data")] public List<T>? Data { get; set; }
    }

    private sealed class HelixVideoDto
    {
        [JsonPropertyName("id")]            public string? Id { get; set; }
        [JsonPropertyName("user_id")]       public string? UserId { get; set; }
        [JsonPropertyName("user_login")]    public string? UserLogin { get; set; }
        [JsonPropertyName("title")]         public string? Title { get; set; }
        [JsonPropertyName("description")]   public string? Description { get; set; }
        [JsonPropertyName("created_at")]    public string? CreatedAt { get; set; }
        [JsonPropertyName("published_at")]  public string? PublishedAt { get; set; }
        [JsonPropertyName("url")]           public string? Url { get; set; }
        [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; set; }
        [JsonPropertyName("view_count")]    public int    ViewCount { get; set; }
        [JsonPropertyName("type")]          public string? Type { get; set; }
        [JsonPropertyName("duration")]      public string? Duration { get; set; }
    }

    private sealed class HelixGameDto
    {
        [JsonPropertyName("id")]           public string? Id { get; set; }
        [JsonPropertyName("name")]         public string? Name { get; set; }
        [JsonPropertyName("box_art_url")]  public string? BoxArtUrl { get; set; }
    }

    private sealed class HelixStreamDto
    {
        [JsonPropertyName("user_id")]       public string? UserId { get; set; }
        [JsonPropertyName("user_login")]    public string? UserLogin { get; set; }
        [JsonPropertyName("game_id")]       public string? GameId { get; set; }
        [JsonPropertyName("game_name")]     public string? GameName { get; set; }
        [JsonPropertyName("title")]         public string? Title { get; set; }
        [JsonPropertyName("viewer_count")]  public int    ViewerCount { get; set; }
        [JsonPropertyName("started_at")]    public string? StartedAt { get; set; }
        [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; set; }
    }

    private sealed class HelixTokenDto
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")]   public int    ExpiresIn { get; set; }
        [JsonPropertyName("token_type")]   public string? TokenType { get; set; }
    }

    private sealed class HelixUserTokenDto
    {
        [JsonPropertyName("access_token")]  public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]    public int    ExpiresIn { get; set; }
        [JsonPropertyName("token_type")]    public string? TokenType { get; set; }
        [JsonPropertyName("scope")]         public string[]? Scope { get; set; }
    }

    private sealed class HelixUserDto
    {
        [JsonPropertyName("id")]                public string? Id { get; set; }
        [JsonPropertyName("login")]             public string? Login { get; set; }
        [JsonPropertyName("display_name")]      public string? DisplayName { get; set; }
        [JsonPropertyName("email")]             public string? Email { get; set; }
        [JsonPropertyName("profile_image_url")] public string? ProfileImageUrl { get; set; }
        [JsonPropertyName("description")]       public string? Description { get; set; }
    }

    private sealed class HelixModeratorDto
    {
        [JsonPropertyName("user_id")]    public string? UserId { get; set; }
        [JsonPropertyName("user_login")] public string? UserLogin { get; set; }
        [JsonPropertyName("user_name")]  public string? UserName { get; set; }
    }

    private sealed class HelixPagedResponse<T> : HelixListResponse<T>
    {
        [JsonPropertyName("pagination")] public HelixPagination? Pagination { get; set; }
    }

    private sealed class HelixPagination
    {
        [JsonPropertyName("cursor")] public string? Cursor { get; set; }
    }
}
