using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Audit;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

public sealed class ErrorStatusLookup : IErrorStatusLookup
{
    private readonly ISqlConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, int>? _cache;

    public ErrorStatusLookup(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> GetIdAsync(string name, CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_cache is null)
                {
                    _cache = await LoadAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        return _cache!.TryGetValue(name, out var id)
            ? id
            : throw new InvalidOperationException($"Unknown ErrorStatus: {name}");
    }

    private async Task<Dictionary<string, int>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = "SELECT ErrorStatusId, Name FROM dbo.ErrorStatus;";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await r.ReadAsync(cancellationToken))
        {
            dict[r.GetString(1)] = r.GetInt32(0);
        }
        return dict;
    }
}
