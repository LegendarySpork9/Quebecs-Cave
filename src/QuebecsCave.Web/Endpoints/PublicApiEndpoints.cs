using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Services.Emojis;
using QuebecsCave.Services.Games;
using QuebecsCave.Services.Reactions;
using QuebecsCave.Services.Streams;
using QuebecsCave.Web.Audit;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Endpoints;

public sealed record StreamDto(
    int Id, string Title, string? Description,
    int GameId, string GameName, string GameSlug, string? GameIconUrl,
    DateTimeOffset StreamedAt, int DurationSeconds,
    string VideoUrl, string? ThumbnailUrl, string? TwitchVodId);

public sealed record StreamListDto(IReadOnlyList<StreamDto> Items, int Total, int Skip, int Take);

public sealed record GameDto(
    int Id, string Name, string Slug, string? IconUrl, string? TwitchGameId, DateTimeOffset CreatedAt);

public sealed record EmojiDto(
    int Id, string Code, string Name, string ImageUrl, int SortOrder);

public sealed record ReactionStateDto(int EmojiId, int Count, bool ByCurrentUser);

public static class PublicApiEndpoints
{
    public static IEndpointRouteBuilder MapPublicApiEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Streams ------------------------------------------------------

        var streams = app.MapGroup("/api/streams")
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly)
            .DisableAntiforgery()
            .WithTags("Streams");

        streams.MapGet("", async (
            int? gameId, DateTimeOffset? from, DateTimeOffset? to, string? q,
            int? skip, int? take,
            IStreamService svc,
            CancellationToken ct) =>
        {
            var filter = new StreamFilter(
                gameId, from, to, q,
                Math.Max(0, skip ?? 0),
                Math.Clamp(take ?? 20, 1, 100));
            var page = await svc.SearchAsync(filter, ct);
            return Results.Ok(new StreamListDto(
                page.Items.Select(MapStream).ToArray(),
                page.Total, page.Skip, page.Take));
        }).WithName("ListStreams").WithSummary("List streams (paged, filterable).");

        streams.MapGet("/{id:int}", async (int id, IStreamService svc, CancellationToken ct) =>
        {
            var card = await svc.GetByIdAsync(id, ct);
            return card is null ? Results.NotFound() : Results.Ok(MapStream(card));
        }).WithName("GetStream").WithSummary("Get a single stream by ID.");

        streams.MapPost("/{id:int}/view", async (
            int id,
            IStreamViewRepository views,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var ip = IpHasher.Hash(http.Connection.RemoteIpAddress);
            int? userId = int.TryParse(http.User.FindFirst("user_id")?.Value, out var u) ? u : null;
            await views.CreateAsync(id, userId, ip, clock.Now, ct);
            return Results.Accepted();
        }).RequireRateLimiting(RateLimiterPolicies.PerIpPublic)
          .WithName("RecordStreamView")
          .WithSummary("Record that the calling client viewed a stream.");

        // ---- Games --------------------------------------------------------

        var games = app.MapGroup("/api/games")
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly)
            .DisableAntiforgery()
            .WithTags("Games");

        games.MapGet("", async (IGameService svc, CancellationToken ct) =>
        {
            var list = await svc.ListAsync(ct);
            return Results.Ok(list.Select(MapGame).ToArray());
        }).WithName("ListGames").WithSummary("List all games.");

        games.MapGet("/{slug}", async (string slug, IGameService svc, CancellationToken ct) =>
        {
            var g = await svc.GetBySlugAsync(slug, ct);
            return g is null ? Results.NotFound() : Results.Ok(MapGame(g));
        }).WithName("GetGameBySlug").WithSummary("Get a game by slug.");

        // ---- Emojis -------------------------------------------------------

        app.MapGet("/api/emojis", async (IEmojiService svc, CancellationToken ct) =>
            {
                var list = await svc.ListActiveAsync(ct);
                return Results.Ok(list.Select(e => new EmojiDto(
                    e.EmojiId, e.Code, e.Name, e.ImageUrl, e.SortOrder)).ToArray());
            })
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly)
            .DisableAntiforgery()
            .WithTags("Emojis")
            .WithName("ListActiveEmojis")
            .WithSummary("List active reaction emojis.");

        // ---- Reactions ----------------------------------------------------

        var reactions = app.MapGroup("/api/streams/{streamId:int}/reactions")
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly, AuthorizationPolicies.AuthenticatedViewer)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimiterPolicies.PerIpPublic)
            .WithTags("Reactions");

        reactions.MapGet("", async (
            int streamId,
            IReactionService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            int? userId = int.TryParse(http.User.FindFirst("user_id")?.Value, out var u) ? u : null;
            var states = await svc.GetStateAsync(streamId, userId, ct);
            return Results.Ok(states
                .Select(s => new ReactionStateDto(s.EmojiId, s.Count, s.ByCurrentUser))
                .ToArray());
        }).WithName("ListStreamReactions")
          .WithSummary("List reaction counts (and the caller's own state) for a stream.");

        reactions.MapPost("", async (
            int streamId,
            ReactionAddRequest body,
            IReactionService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            int? userId = int.TryParse(http.User.FindFirst("user_id")?.Value, out var u) ? u : null;
            if (userId is null) return Results.Unauthorized();
            var ip = IpHasher.Hash(http.Connection.RemoteIpAddress);
            await svc.AddAsync(streamId, userId.Value, body.EmojiId, ip, ct);
            return Results.Accepted();
        }).WithName("AddStreamReaction")
          .WithSummary("Add a reaction. Idempotent — re-POSTing the same emoji is a no-op.");

        reactions.MapDelete("/{emojiId:int}", async (
            int streamId,
            int emojiId,
            IReactionService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            int? userId = int.TryParse(http.User.FindFirst("user_id")?.Value, out var u) ? u : null;
            if (userId is null) return Results.Unauthorized();
            var ip = IpHasher.Hash(http.Connection.RemoteIpAddress);
            await svc.RemoveAsync(streamId, userId.Value, emojiId, ip, ct);
            return Results.NoContent();
        }).WithName("RemoveStreamReaction")
          .WithSummary("Remove a reaction.");

        return app;
    }

    private static StreamDto MapStream(StreamCard c) => new(
        c.StreamId, c.Title, c.Description,
        c.GameId, c.GameName, c.GameSlug, c.GameIconUrl,
        c.StreamedAt, c.DurationSeconds,
        c.VideoUrl, c.ThumbnailUrl, c.TwitchVodId);

    private static GameDto MapGame(Game g) => new(
        g.GameId, g.Name, g.Slug, g.IconUrl, g.TwitchGameId, g.CreatedAt);
}

public sealed record ReactionAddRequest(int EmojiId);
