using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Downloader.Api;
using QuebecsCave.Downloader.Pipeline;
using QuebecsCave.Downloader.Storage;
using QuebecsCave.Downloader.YtDlp;
using QuebecsCave.Services.Time;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Downloader;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = BuildHost(args);

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Downloader");
        var loop = host.Services.GetRequiredService<IDownloaderLoop>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        var mode = args.FirstOrDefault(a => a is "--once" or "--watch" or "--help" or "-h") ?? "--help";

        try
        {
            switch (mode)
            {
                case "--once":
                    logger.LogInformation("Quebec's Cave downloader — run-once mode.");
                    await loop.RunOnceAsync(lifetime.ApplicationStopping);
                    return 0;

                case "--watch":
                    logger.LogInformation("Quebec's Cave downloader — watch mode (Ctrl-C to stop).");
                    await loop.RunWatchAsync(lifetime.ApplicationStopping);
                    return 0;

                default:
                    PrintHelp();
                    return 0;
            }
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            logger.LogInformation("Cancellation requested — shutting down.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Downloader failed.");
            return 1;
        }
    }

    private static IHost BuildHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.SetBasePath(AppContext.BaseDirectory);
                cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                cfg.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                cfg.AddEnvironmentVariables();
                cfg.AddCommandLine(args);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(o =>
                {
                    o.IncludeScopes = false;
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                });
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<DownloaderOptions>(ctx.Configuration.GetSection(DownloaderOptions.SectionName));
                services.Configure<CaveApiOptions>(ctx.Configuration.GetSection(CaveApiOptions.SectionName));
                services.Configure<StorageOptions>(ctx.Configuration.GetSection(StorageOptions.SectionName));
                services.Configure<TwitchBroadcasterOptions>(ctx.Configuration.GetSection(TwitchBroadcasterOptions.SectionName));
                services.Configure<TwitchOptions>(ctx.Configuration.GetSection(TwitchOptions.SectionName));

                services.AddSingleton<IClock, SystemClock>();

                // Twitch Helix (reuses the Web app's client). Allow self-signed
                // dev certs so the downloader can hit https://localhost:7110.
                services.AddHttpClient<ITwitchClient, TwitchHelixClient>(http =>
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                });

                services.AddHttpClient<ICaveApiClient, CaveApiClient>(http =>
                {
                    http.Timeout = TimeSpan.FromSeconds(60);
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                });

                services.AddHttpClient<IFileStorage, LocalFileStorage>(http =>
                {
                    http.Timeout = TimeSpan.FromMinutes(2);
                });

                services.AddSingleton<IYtDlpRunner, YtDlpRunner>();
                services.AddSingleton<IDownloaderLoop, DownloaderLoop>();
            })
            .Build();

    private static void PrintHelp()
    {
        Console.WriteLine("Quebec's Cave — console downloader");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  QuebecsCave.Downloader --once    Run a single sweep and exit.");
        Console.WriteLine("  QuebecsCave.Downloader --watch   Loop forever on the configured poll interval.");
        Console.WriteLine();
        Console.WriteLine("Config (appsettings.json + appsettings.Development.json):");
        Console.WriteLine("  Api:BaseUrl, Api:ServiceKey            — Cave Web API endpoint and key");
        Console.WriteLine("  Twitch:ClientId, Twitch:ClientSecret   — Twitch dev app credentials");
        Console.WriteLine("  Twitch:BroadcasterUserId               — streamer's numeric Twitch user ID");
        Console.WriteLine("  Storage:VideoRoot                      — where mp4s land on disk");
        Console.WriteLine("  Storage:PublicBaseUrl                  — URL the cave maps videos to");
        Console.WriteLine("  Downloader:YtDlpPath                   — yt-dlp executable (default: yt-dlp on PATH)");
        Console.WriteLine("  Downloader:DefaultGameSlug             — game to attribute new VODs to");
        Console.WriteLine("  Downloader:DryRun                      — true: skip yt-dlp, write a placeholder");
    }
}
