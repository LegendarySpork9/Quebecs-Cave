using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuebecsCave.Data.DependencyInjection;
using QuebecsCave.Data.Migrations;
using QuebecsCave.Services.DependencyInjection;
using QuebecsCave.Services.Seed;
using QuebecsCave.Services.Twitch;
using QuebecsCave.Web.Audit;
using QuebecsCave.Web.Auth;
using QuebecsCave.Web.Components;
using QuebecsCave.Web.Endpoints;
using Scalar.AspNetCore;

namespace QuebecsCave.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        // Ferries the cookie principal across the static-prerender →
        // interactive-Server boundary via PersistentComponentState. Without
        // this, [CascadingParameter] AuthState resolves to anonymous on the
        // second (interactive) render of any @rendermode InteractiveServer
        // page and role-gated UI disables itself.
        builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
            QuebecsCave.Web.Auth.PersistingAuthenticationStateProvider>();

        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required");
        builder.Services.AddQuebecsCaveData(connectionString);
        builder.Services.AddQuebecsCaveServices(builder.Configuration);

        builder.Services.Configure<DevDataSeederOptions>(opts =>
        {
            opts.EmojiFolderPath = Path.Combine(builder.Environment.WebRootPath ?? "", "emojis");
        });

        var apiSection = builder.Configuration.GetSection("Api");
        var serviceKeys = apiSection.GetSection("ServiceKeys").Get<string[]>() ?? Array.Empty<string>();

        var twitchSection = builder.Configuration.GetSection(TwitchOptions.SectionName);
        var twitchClientId = twitchSection["ClientId"] ?? "";
        var twitchClientSecret = twitchSection["ClientSecret"] ?? "";

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(opts =>
            {
                opts.Cookie.Name = "QuebecsCave.Auth";
                opts.LoginPath = "/auth/twitch/login";
                opts.LogoutPath = "/auth/logout";
                opts.ExpireTimeSpan = TimeSpan.FromDays(30);
                opts.SlidingExpiration = true;
            })
            .AddApiKey(opts => opts.ServiceKeys = serviceKeys)
            .AddOAuth(TwitchAuthDefaults.Scheme, opts =>
            {
                opts.ClientId = twitchClientId;
                opts.ClientSecret = twitchClientSecret;
                opts.CallbackPath = TwitchAuthDefaults.CallbackPath;
                opts.AuthorizationEndpoint = TwitchAuthDefaults.AuthorizationEndpoint;
                opts.TokenEndpoint = TwitchAuthDefaults.TokenEndpoint;
                opts.UserInformationEndpoint = TwitchAuthDefaults.UserInformationEndpoint;
                opts.SaveTokens = true;
                opts.UsePkce = true;
                opts.Scope.Add("user:read:email");
                opts.Scope.Add("user:read:broadcast");
                opts.Scope.Add("moderation:read");
                opts.Events = TwitchOAuthEvents.Build();
            });

        builder.Services.AddAuthorization(AuthorizationPolicies.Add);

        builder.Services.AddRateLimiter(RateLimiterPolicies.Configure);

        builder.Services.AddOpenApi("v1", opts =>
        {
            opts.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info.Title = "Quebec's Cave API";
                doc.Info.Version = "v1";
                doc.Info.Description = "Public API for the LongLiveQuebec stream archive. " +
                    "All endpoints require an X-Api-Key header. User-bound endpoints additionally require a Twitch session cookie.";
                return Task.CompletedTask;
            });
        });

        builder.Services.AddExceptionHandler<AuditExceptionHandler>();
        builder.Services.AddProblemDetails();

        var app = builder.Build();

        RunSchemaMigrations(app);

        if (app.Environment.IsDevelopment())
        {
            RunDevSeed(app);
        }

        // Always run UseExceptionHandler so AuditExceptionHandler.TryHandleAsync
        // is invoked on every unhandled exception (audit capture matters in dev too).
        app.UseExceptionHandler("/Error");
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // Status-code pages re-execute /not-found, which is a Razor component
        // and would drag the request through Blazor's antiforgery check —
        // re-running form validation against the API's JSON body. Skip the
        // re-execute for /api/* so anonymous JSON POSTs return a clean 401.
        app.UseWhen(
            ctx => !ctx.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
        app.UseHttpsRedirection();

        // Razor Components require antiforgery middleware — Blazor stamps
        // antiforgery metadata onto every component endpoint. We branch the
        // pipeline so /api/* never sees antiforgery: anonymous JSON callers
        // get a clean 401 from auth instead of a 400 from antiforgery's form
        // reader. UI routes still get full protection.
        app.UseWhen(
            ctx => !ctx.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseAntiforgery());
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Audit middleware AFTER auth so user/key claims are visible.
        app.UseApiCallLogging();

        app.MapStaticAssets();
        app.MapOpenApi("/openapi/{documentName}.json")
            .AllowAnonymous();
        app.MapScalarApiReference("/openapi", opts =>
        {
            opts.WithTitle("Quebec's Cave API")
                .WithTheme(ScalarTheme.Solarized)
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        }).AllowAnonymous();
        app.MapIngestEndpoints();
        app.MapAuthEndpoints();
        app.MapDevAuthEndpoints();
        app.MapUserApiEndpoints();
        app.MapLiveStatusEndpoint();
        app.MapPublicApiEndpoints();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }

    private static void RunSchemaMigrations(WebApplication app)
    {
        var connectionString = app.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            app.Logger.LogWarning("No 'Default' connection string configured — skipping schema migration.");
            return;
        }

        var migratorLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<SchemaMigrator>();
        var migrator = new SchemaMigrator(connectionString, migratorLogger);

        try
        {
            var result = migrator.Run();
            if (!result.Success)
            {
                if (app.Environment.IsDevelopment())
                {
                    app.Logger.LogCritical(result.Error,
                        "Schema migration FAILED in Development — continuing without DB so the UI still serves.");
                    return;
                }
                throw new InvalidOperationException("Schema migration failed — see logs for details.", result.Error);
            }
        }
        catch (Exception ex) when (app.Environment.IsDevelopment())
        {
            app.Logger.LogCritical(ex,
                "Schema migration could not run in Development (SQL Server reachable?). Continuing — DB-backed features won't work.");
        }
    }

    private static void RunDevSeed(WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<IDevDataSeeder>();
            seeder.SeedAsync(app.Lifetime.ApplicationStopping).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Dev data seed failed — continuing.");
        }
    }
}
