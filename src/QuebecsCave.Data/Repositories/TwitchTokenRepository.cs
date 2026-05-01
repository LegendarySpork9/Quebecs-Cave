using System.Data;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.Repositories;

/// <summary>
/// Wraps token storage with ASP.NET Data Protection so the access/refresh
/// tokens at rest aren't readable without the data protection keys (which
/// live with the running process).
/// </summary>
public sealed class TwitchTokenRepository : ITwitchTokenRepository
{
    private const string ProtectorPurpose = "QuebecsCave.TwitchToken.v1";

    private readonly ISqlConnectionFactory _factory;
    private readonly IDataProtector _protector;

    public TwitchTokenRepository(ISqlConnectionFactory factory, IDataProtectionProvider provider)
    {
        _factory = factory;
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public async Task<TwitchToken?> GetForUserAsync(int userId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TwitchTokenId, UserId, AccessToken, RefreshToken, ExpiresAt, Scopes
            FROM dbo.TwitchToken
            WHERE UserId = @userId;";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@userId", userId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        var accessProtected = (byte[])r["AccessToken"];
        var refreshProtected = (byte[])r["RefreshToken"];

        return new TwitchToken
        {
            TwitchTokenId = r.GetInt32("TwitchTokenId"),
            UserId = r.GetInt32("UserId"),
            AccessToken = Decrypt(accessProtected),
            RefreshToken = Decrypt(refreshProtected),
            ExpiresAt = r.GetDateTimeOffset("ExpiresAt"),
            Scopes = r.GetString("Scopes"),
        };
    }

    public async Task UpsertAsync(int userId, string accessToken, string refreshToken, DateTimeOffset expiresAt, string scopes, CancellationToken cancellationToken)
    {
        const string sql = @"
            MERGE dbo.TwitchToken AS target
            USING (SELECT @userId AS UserId) AS source
                ON target.UserId = source.UserId
            WHEN MATCHED THEN
                UPDATE SET AccessToken = @access, RefreshToken = @refresh,
                           ExpiresAt = @expires, Scopes = @scopes
            WHEN NOT MATCHED THEN
                INSERT (UserId, AccessToken, RefreshToken, ExpiresAt, Scopes)
                VALUES (@userId, @access, @refresh, @expires, @scopes);";
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@access", Encrypt(accessToken));
        cmd.Parameters.AddWithValue("@refresh", Encrypt(refreshToken));
        cmd.Parameters.AddWithValue("@expires", expiresAt);
        cmd.Parameters.AddWithValue("@scopes", scopes);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private byte[] Encrypt(string plaintext) =>
        _protector.Protect(Encoding.UTF8.GetBytes(plaintext));

    private string Decrypt(byte[] ciphertext) =>
        Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
}
