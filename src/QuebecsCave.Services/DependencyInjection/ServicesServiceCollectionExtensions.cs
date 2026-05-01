using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Audit;
using QuebecsCave.Services.Emojis;
using QuebecsCave.Services.Games;
using QuebecsCave.Services.Identity;
using QuebecsCave.Services.Seed;
using QuebecsCave.Services.Streams;
using QuebecsCave.Services.Time;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.DependencyInjection;

public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddQuebecsCaveServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.Configure<TwitchOptions>(configuration.GetSection(TwitchOptions.SectionName));
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<DevAuthOptions>(configuration.GetSection(DevAuthOptions.SectionName));

        services.AddHttpClient<ITwitchClient, TwitchHelixClient>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IStreamService, StreamService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IEmojiService, EmojiService>();
        services.AddScoped<Reactions.IReactionService, Reactions.ReactionService>();
        services.AddScoped<Reports.IReportsService, Reports.ReportsService>();
        services.AddScoped<Admin.IAuditHistoryWriter, Admin.AuditHistoryWriter>();

        services.AddScoped<IRoleResolver, RoleResolver>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddHostedService<ModeratorCacheRefreshService>();

        services.AddSingleton<LiveStatusService>();
        services.AddSingleton<ILiveStatusService>(sp => sp.GetRequiredService<LiveStatusService>());
        services.AddHostedService<LiveStatusBackgroundService>();

        services.AddScoped<IGameIconRefresher, GameIconRefresher>();
        services.AddHostedService<GameIconRefreshBackgroundService>();

        services.AddScoped<IDevDataSeeder, DevDataSeeder>();

        // ---- Audit pipeline ------------------------------------------------
        // One channel per event type, owned by a singleton writer; the
        // matching flusher BackgroundService drains it into SQL in batches.

        services.AddSingleton<BatchedAuditWriter<ApiCallLogEntry>>();
        services.AddSingleton<BatchedAuditWriter<WebsiteEventEntry>>();
        services.AddSingleton<BatchedAuditWriter<DownloaderEventEntry>>();
        services.AddSingleton<BatchedAuditWriter<ErrorLogEntry>>();

        services.AddSingleton<IApiCallLogger, ApiCallLogger>();
        services.AddSingleton<IWebsiteEventLogger, WebsiteEventLogger>();
        services.AddSingleton<IDownloaderEventLogger, DownloaderEventLogger>();
        services.AddSingleton<IErrorLogger, ErrorLogger>();

        services.AddHostedService<ApiCallLogFlusher>();
        services.AddHostedService<WebsiteEventFlusher>();
        services.AddHostedService<DownloaderEventFlusher>();
        services.AddHostedService<ErrorLogFlusher>();

        services.AddHostedService<RetentionBackgroundService>();

        return services;
    }
}
