using System.Text.Json;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;

namespace QuebecsCave.Services.Reactions;

public sealed record ReactionState(int EmojiId, int Count, bool ByCurrentUser);

public interface IReactionService
{
    Task<IReadOnlyList<ReactionState>> GetStateAsync(int streamId, int? currentUserId, CancellationToken cancellationToken);
    Task AddAsync(int streamId, int userId, int emojiId, byte[] ipHash, CancellationToken cancellationToken);
    Task RemoveAsync(int streamId, int userId, int emojiId, byte[] ipHash, CancellationToken cancellationToken);
}

public sealed class ReactionService : IReactionService
{
    private readonly IReactionRepository _reactions;
    private readonly IEmojiRepository _emojis;
    private readonly IWebsiteEventLogger _websiteEvents;
    private readonly IClock _clock;

    public ReactionService(
        IReactionRepository reactions,
        IEmojiRepository emojis,
        IWebsiteEventLogger websiteEvents,
        IClock clock)
    {
        _reactions = reactions;
        _emojis = emojis;
        _websiteEvents = websiteEvents;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ReactionState>> GetStateAsync(int streamId, int? currentUserId, CancellationToken cancellationToken)
    {
        var counts = await _reactions.GetCountsForStreamAsync(streamId, cancellationToken);
        var byEmoji = counts.ToDictionary(c => c.EmojiId, c => c.Count);

        var ownIds = currentUserId.HasValue
            ? await _reactions.ListEmojiIdsByStreamForUserAsync(streamId, currentUserId.Value, cancellationToken)
            : Array.Empty<int>();
        var owns = new HashSet<int>(ownIds);

        var emojis = await _emojis.ListActiveAsync(cancellationToken);
        return emojis
            .Select(e => new ReactionState(
                e.EmojiId,
                byEmoji.TryGetValue(e.EmojiId, out var c) ? c : 0,
                owns.Contains(e.EmojiId)))
            .ToArray();
    }

    public async Task AddAsync(int streamId, int userId, int emojiId, byte[] ipHash, CancellationToken cancellationToken)
    {
        await _reactions.AddAsync(streamId, userId, emojiId, _clock.Now, cancellationToken);
        EnqueueEvent("add", streamId, userId, emojiId, ipHash);
    }

    public async Task RemoveAsync(int streamId, int userId, int emojiId, byte[] ipHash, CancellationToken cancellationToken)
    {
        await _reactions.RemoveAsync(streamId, userId, emojiId, cancellationToken);
        EnqueueEvent("remove", streamId, userId, emojiId, ipHash);
    }

    private void EnqueueEvent(string verb, int streamId, int userId, int emojiId, byte[] ipHash)
    {
        var detail = JsonSerializer.Serialize(new { verb, streamId, emojiId });
        _websiteEvents.Enqueue(new WebsiteEventEntry(
            Action: WebsiteAction.Reaction,
            Path: $"/streams/{streamId}",
            UserId: userId,
            IpHash: ipHash,
            Detail: detail,
            OccurredAt: _clock.Now));
    }
}
