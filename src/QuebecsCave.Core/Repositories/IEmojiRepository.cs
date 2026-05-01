using QuebecsCave.Core.Domain;

namespace QuebecsCave.Core.Repositories;

public interface IEmojiRepository
{
    Task<IReadOnlyList<Emoji>> ListActiveAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Emoji>> ListAllAsync(CancellationToken cancellationToken);
    Task<Emoji?> GetByIdAsync(int emojiId, CancellationToken cancellationToken);
    Task<Emoji?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<int> CreateAsync(Emoji emoji, CancellationToken cancellationToken);
    Task UpdateAsync(Emoji emoji, CancellationToken cancellationToken);
}
