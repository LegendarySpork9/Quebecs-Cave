namespace QuebecsCave.Core.Audit;

public static class AuditAction
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
}

public sealed record AuditHistoryRow(
    int AuditHistoryId,
    int? UserId,
    string Entity,
    int EntityId,
    string Action,
    string? Diff,
    DateTimeOffset CreatedAt);

public interface IAuditHistoryRepository
{
    Task<int> InsertAsync(int? userId, string entity, int entityId, string action, string? diff, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IDeletionRepository
{
    Task<int> InsertAsync(string entity, int entityId, int? userId, DateTimeOffset now, CancellationToken cancellationToken);
}
