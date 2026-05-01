namespace QuebecsCave.Core.Repositories;

public interface IStreamViewRepository
{
    Task<int> CreateAsync(int streamId, int? userId, byte[] ipHash, DateTimeOffset now, CancellationToken cancellationToken);
}
