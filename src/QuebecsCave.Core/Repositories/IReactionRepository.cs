using QuebecsCave.Core.Domain;

namespace QuebecsCave.Core.Repositories;

public interface IReactionRepository
{
    Task<IReadOnlyList<Reaction>> ListByStreamAsync(int streamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> ListEmojiIdsByStreamForUserAsync(int streamId, int userId, CancellationToken cancellationToken);
    Task AddAsync(int streamId, int userId, int emojiId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RemoveAsync(int streamId, int userId, int emojiId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReactionCount>> GetCountsForStreamAsync(int streamId, CancellationToken cancellationToken);
}
