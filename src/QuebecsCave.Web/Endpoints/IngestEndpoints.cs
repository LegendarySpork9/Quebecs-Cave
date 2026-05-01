using Microsoft.Extensions.Options;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Services.Admin;
using QuebecsCave.Web.Auth;
using DomainStream = QuebecsCave.Core.Domain.Stream;

namespace QuebecsCave.Web.Endpoints;

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

public sealed record StreamNeedingThumbnailDto(int StreamId, string TwitchVodId, string GameSlug, int StreamedAtYear);

public sealed record UpdateThumbnailRequest(string ThumbnailUrl);

public sealed record GameForTimeResponse(int GameId, string Slug, string Name);

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

public static class IngestEndpoints
{
    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingest")
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly)
            .DisableAntiforgery()
            .WithTags("Ingest");

        group.MapPost("/downloader-events", async (
            DownloaderEventDto[] events,
            IDownloaderEventLogger logger,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (events is null || events.Length == 0)
            {
                return Results.BadRequest(new { error = "No events provided." });
            }

            foreach (var dto in events)
            {
                if (string.IsNullOrEmpty(dto.Stage)) continue;
                logger.Enqueue(new DownloaderEventEntry(
                    Stage: dto.Stage,
                    TwitchVodId: dto.TwitchVodId,
                    Success: dto.Success,
                    DurationMs: dto.DurationMs,
                    Payload: dto.Payload,
                    Message: dto.Message,
                    OccurredAt: dto.OccurredAt ?? clock.Now));
            }
            await Task.CompletedTask;
            return Results.Accepted();
        }).DisableAntiforgery();

        group.MapPost("/errors", async (
            ErrorEventDto[] errors,
            IErrorLogger logger,
            IErrorStatusLookup statusLookup,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (errors is null || errors.Length == 0)
            {
                return Results.BadRequest(new { error = "No errors provided." });
            }

            var openId = await statusLookup.GetIdAsync(QuebecsCave.Core.Domain.ErrorStatusName.Open, cancellationToken);

            foreach (var dto in errors)
            {
                if (string.IsNullOrEmpty(dto.ExceptionType) || string.IsNullOrEmpty(dto.Message)) continue;
                logger.Enqueue(new ErrorLogEntry(
                    Source: AuditSource.Downloader,
                    ExceptionType: dto.ExceptionType,
                    Message: dto.Message.Length > 2000 ? dto.Message.Substring(0, 2000) : dto.Message,
                    StackTrace: dto.StackTrace,
                    Context: dto.Context,
                    StatusId: openId,
                    GitHubIssueUrl: null,
                    AddressedByUserId: null,
                    AddressedAt: null,
                    Notes: null,
                    OccurredAt: dto.OccurredAt ?? clock.Now));
            }
            return Results.Accepted();
        }).DisableAntiforgery();

        group.MapPost("/streams", async (
            IngestStreamRequest body,
            IStreamRepository streams,
            ILiveSessionRepository sessions,
            IGameRepository games,
            IAuditHistoryWriter audit,
            IOptions<QuebecsCave.Services.Twitch.TwitchOptions> twitchOpts,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title) ||
                string.IsNullOrWhiteSpace(body.VideoUrl))
            {
                return Results.BadRequest(new { error = "Title and VideoUrl are required." });
            }

            // Game attribution: prefer the live session that overlaps the VOD's
            // streamed-at window. Fall back to the GameId the caller provided.
            var resolvedGameId = body.GameId;
            var streamedAt = body.StreamedAt == default ? clock.Now : body.StreamedAt;
            var broadcasterId = twitchOpts.Value.StreamerUserId;
            if (!string.IsNullOrEmpty(broadcasterId))
            {
                var session = await sessions.FindForTimeAsync(broadcasterId, streamedAt, cancellationToken);
                if (session?.ResolvedGameId is int matched)
                {
                    resolvedGameId = matched;
                }
            }
            if (resolvedGameId <= 0)
            {
                return Results.BadRequest(new { error = "No game could be resolved (no matching live session and GameId was not supplied)." });
            }

            var entity = new DomainStream
            {
                Title = body.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description,
                GameId = resolvedGameId,
                StreamedAt = streamedAt,
                DurationSeconds = Math.Max(0, body.DurationSeconds),
                VideoUrl = body.VideoUrl.Trim(),
                ThumbnailUrl = string.IsNullOrWhiteSpace(body.ThumbnailUrl) ? null : body.ThumbnailUrl,
                TwitchVodId = string.IsNullOrWhiteSpace(body.TwitchVodId) ? null : body.TwitchVodId,
                CreatedAt = clock.Now,
            };
            var id = await streams.CreateAsync(entity, cancellationToken);
            await audit.RecordCreateAsync("Stream", id, entity, userId: null, cancellationToken);
            return Results.Created($"/api/streams/{id}", new IngestStreamResponse(id, resolvedGameId));
        }).DisableAntiforgery()
          .WithName("IngestStream")
          .WithSummary("Create a stream (used by the console downloader after a VOD lands). Game is auto-attributed from the live-session overlap; falls back to GameId in the body.");

        group.MapGet("/known-vod-ids", async (
            IStreamRepository streams,
            CancellationToken cancellationToken) =>
        {
            var ids = await streams.GetKnownTwitchVodIdsAsync(cancellationToken);
            return Results.Ok(ids);
        }).WithName("ListKnownVodIds")
          .WithSummary("Return the set of Twitch VOD IDs already archived. Used by the downloader to diff.");

        group.MapGet("/game-for-time", async (
            string broadcasterId,
            DateTimeOffset at,
            ILiveSessionRepository sessions,
            IGameRepository games,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(broadcasterId)) return Results.BadRequest();
            var session = await sessions.FindForTimeAsync(broadcasterId, at, cancellationToken);
            if (session?.ResolvedGameId is not int gameId) return Results.NotFound();

            var game = await games.GetByIdAsync(gameId, cancellationToken);
            if (game is null) return Results.NotFound();

            return Results.Ok(new GameForTimeResponse(game.GameId, game.Slug, game.Name));
        }).WithName("GameForTime")
          .WithSummary("Find the game the broadcaster was playing at the given timestamp, based on tracked live sessions.");

        group.MapGet("/streams-needing-thumbnails", async (
            IStreamRepository streams,
            IGameRepository games,
            int? take,
            CancellationToken cancellationToken) =>
        {
            var rows = await streams.ListStreamsNeedingThumbnailsAsync(Math.Clamp(take ?? 50, 1, 200), cancellationToken);
            var pending = rows.Where(s => !string.IsNullOrEmpty(s.TwitchVodId)).ToList();
            if (pending.Count == 0) return Results.Ok(Array.Empty<StreamNeedingThumbnailDto>());

            var gamesById = (await games.ListAsync(cancellationToken)).ToDictionary(g => g.GameId);
            var dtos = pending
                .Select(s => new StreamNeedingThumbnailDto(
                    s.StreamId,
                    s.TwitchVodId!,
                    gamesById.TryGetValue(s.GameId, out var g) ? g.Slug : "",
                    s.StreamedAt.Year))
                .ToArray();
            return Results.Ok(dtos);
        }).WithName("StreamsNeedingThumbnails")
          .WithSummary("Streams that don't have a thumbnail yet but do have a TwitchVodId — the downloader retries these each sweep.");

        group.MapPut("/streams/{id:int}/thumbnail", async (
            int id,
            UpdateThumbnailRequest body,
            IStreamRepository streams,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.ThumbnailUrl)) return Results.BadRequest();
            await streams.UpdateThumbnailAsync(id, body.ThumbnailUrl.Trim(), cancellationToken);
            return Results.NoContent();
        }).WithName("UpdateStreamThumbnail")
          .WithSummary("Patch only the thumbnail URL of an existing stream. Idempotent.");

        // Diagnostic-only: forces an exception path so we can verify ErrorLog
        // capture without needing the downloader.
        group.MapGet("/throw", (string? message) =>
        {
            throw new InvalidOperationException(message ?? "Audit infra smoke test.");
        }).WithSummary("Diagnostic — throws to exercise the ErrorLog pipeline.");

        return app;
    }
}
