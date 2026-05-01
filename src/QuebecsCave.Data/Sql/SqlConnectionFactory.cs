using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace QuebecsCave.Data.Sql;

/// <summary>
/// Single source of SQL Server connections for the data layer. Repos call
/// <see cref="OpenAsync"/>, run their commands, and dispose. There's no
/// shared/pooled state beyond what SqlClient does for us.
/// </summary>
public interface ISqlConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }
        _connectionString = connectionString;
    }

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
