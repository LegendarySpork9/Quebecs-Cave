using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Support;
using Microsoft.Extensions.Logging;

namespace QuebecsCave.Data.Migrations;

/// <summary>
/// Runs embedded *.sql migrations on application startup. Tracking lives in
/// dbo.SchemaVersion(SchemaVersionId, ScriptName, AppliedAt) — renamed from
/// the DbUp default for consistency with the rest of the schema.
/// </summary>
public sealed class SchemaMigrator
{
    private readonly string _connectionString;
    private readonly ILogger<SchemaMigrator> _logger;

    public SchemaMigrator(string connectionString, ILogger<SchemaMigrator> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MigrationResult Run()
    {
        EnsureDatabase.For.SqlDatabase(_connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                static name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .JournalToSqlTable("dbo", "SchemaVersion")
            .WithTransactionPerScript()
            .LogTo(new MicrosoftLoggerForwarder(_logger))
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            _logger.LogError(result.Error, "Schema migration failed on script {Script}", result.ErrorScript?.Name);
            return new MigrationResult(false, result.Scripts.Select(s => s.Name).ToArray(), result.Error);
        }

        var applied = result.Scripts.Select(s => s.Name).ToArray();
        _logger.LogInformation(
            "Schema migration succeeded — {Count} script(s) applied",
            applied.Length);
        return new MigrationResult(true, applied, null);
    }

    private sealed class MicrosoftLoggerForwarder : IUpgradeLog
    {
        private readonly ILogger _logger;
        public MicrosoftLoggerForwarder(ILogger logger) => _logger = logger;

        public void LogTrace(string format, params object[] args) => _logger.LogTrace(format, args);
        public void LogDebug(string format, params object[] args) => _logger.LogDebug(format, args);
        public void LogInformation(string format, params object[] args) => _logger.LogInformation(format, args);
        public void LogWarning(string format, params object[] args) => _logger.LogWarning(format, args);
        public void LogError(string format, params object[] args) => _logger.LogError(format, args);
        public void LogError(Exception ex, string format, params object[] args) => _logger.LogError(ex, format, args);
    }
}

public sealed record MigrationResult(bool Success, IReadOnlyList<string> AppliedScripts, Exception? Error);
