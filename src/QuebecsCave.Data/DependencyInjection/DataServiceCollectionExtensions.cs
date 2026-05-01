using Microsoft.Extensions.DependencyInjection;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Data.Repositories;
using QuebecsCave.Data.Sql;

namespace QuebecsCave.Data.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddQuebecsCaveData(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<ISqlConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IStreamRepository, StreamRepository>();
        services.AddScoped<IEmojiRepository, EmojiRepository>();

        services.AddScoped<IDeveloperRepository, DeveloperRepository>();
        services.AddScoped<IModeratorCacheRepository, ModeratorCacheRepository>();
        services.AddScoped<ITwitchTokenRepository, TwitchTokenRepository>();
        services.AddScoped<IReactionRepository, ReactionRepository>();
        services.AddScoped<IStreamViewRepository, StreamViewRepository>();
        services.AddScoped<ILiveSessionRepository, LiveSessionRepository>();

        services.AddScoped<IApiCallLogRepository, ApiCallLogRepository>();
        services.AddScoped<IDownloaderEventRepository, DownloaderEventRepository>();
        services.AddScoped<IWebsiteEventRepository, WebsiteEventRepository>();
        services.AddScoped<IErrorLogRepository, ErrorLogRepository>();
        services.AddScoped<IRetentionRepository, RetentionRepository>();

        services.AddScoped<IApiCallLogQueryRepository, ApiCallLogQueryRepository>();
        services.AddScoped<IDownloaderEventQueryRepository, DownloaderEventQueryRepository>();
        services.AddScoped<IWebsiteEventQueryRepository, WebsiteEventQueryRepository>();
        services.AddScoped<IErrorLogQueryRepository, ErrorLogQueryRepository>();
        services.AddScoped<ISchemaVersionQueryRepository, SchemaVersionQueryRepository>();

        services.AddScoped<IAuditHistoryRepository, AuditHistoryRepository>();
        services.AddScoped<IDeletionRepository, DeletionRepository>();

        services.AddScoped<QuebecsCave.Core.Reports.IReportsRepository, ReportsRepository>();

        // ErrorStatusLookup caches IDs in-process — must be a singleton.
        services.AddSingleton<IErrorStatusLookup, ErrorStatusLookup>();

        return services;
    }
}
