using System.Text.Json;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;

namespace QuebecsCave.Services.Admin;

public interface IAuditHistoryWriter
{
    Task RecordCreateAsync<T>(string entity, int entityId, T newValue, int? userId, CancellationToken cancellationToken);
    Task RecordUpdateAsync<T>(string entity, int entityId, T before, T after, int? userId, CancellationToken cancellationToken);
    Task RecordDeleteAsync(string entity, int entityId, int? userId, CancellationToken cancellationToken);
}

public sealed class AuditHistoryWriter : IAuditHistoryWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly IAuditHistoryRepository _audit;
    private readonly IDeletionRepository _deletions;
    private readonly IClock _clock;

    public AuditHistoryWriter(IAuditHistoryRepository audit, IDeletionRepository deletions, IClock clock)
    {
        _audit = audit;
        _deletions = deletions;
        _clock = clock;
    }

    public Task RecordCreateAsync<T>(string entity, int entityId, T newValue, int? userId, CancellationToken cancellationToken)
    {
        var diff = JsonSerializer.Serialize(new { after = newValue }, JsonOpts);
        return _audit.InsertAsync(userId, entity, entityId, AuditAction.Create, diff, _clock.Now, cancellationToken);
    }

    public Task RecordUpdateAsync<T>(string entity, int entityId, T before, T after, int? userId, CancellationToken cancellationToken)
    {
        var diff = JsonSerializer.Serialize(new { before, after }, JsonOpts);
        return _audit.InsertAsync(userId, entity, entityId, AuditAction.Update, diff, _clock.Now, cancellationToken);
    }

    public async Task RecordDeleteAsync(string entity, int entityId, int? userId, CancellationToken cancellationToken)
    {
        await _audit.InsertAsync(userId, entity, entityId, AuditAction.Delete, null, _clock.Now, cancellationToken);
        await _deletions.InsertAsync(entity, entityId, userId, _clock.Now, cancellationToken);
    }
}
