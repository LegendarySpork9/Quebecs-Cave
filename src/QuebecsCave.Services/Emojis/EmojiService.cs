using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;

namespace QuebecsCave.Services.Emojis;

public interface IEmojiService
{
    Task<IReadOnlyList<Emoji>> ListActiveAsync(CancellationToken cancellationToken);
}

public sealed class EmojiService : IEmojiService
{
    private readonly IEmojiRepository _emojis;
    public EmojiService(IEmojiRepository emojis) => _emojis = emojis;

    public Task<IReadOnlyList<Emoji>> ListActiveAsync(CancellationToken cancellationToken) =>
        _emojis.ListActiveAsync(cancellationToken);
}
