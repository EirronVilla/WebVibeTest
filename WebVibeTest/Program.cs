using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using WebVibeTest.Application.Games;
using WebVibeTest.Hubs;
using WebVibeTest.Infrastructure.Data;
using WebVibeTest.Infrastructure.Files;
using WebVibeTest.Infrastructure.Games;

namespace WebVibeTest;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureRenderPort(builder);

        var connectionString = ResolvePostgresConnectionString(builder.Configuration);
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        var persistentRoot = builder.Configuration["PersistentStorage:Path"];
        var profileRoot = string.IsNullOrWhiteSpace(persistentRoot)
            ? Path.Combine(builder.Environment.WebRootPath, "uploads", "profiles")
            : Path.Combine(persistentRoot, "profile-images");
        var keyRoot = string.IsNullOrWhiteSpace(persistentRoot)
            ? Path.Combine(builder.Environment.ContentRootPath, ".data", "data-protection-keys")
            : Path.Combine(persistentRoot, "data-protection-keys");
        Directory.CreateDirectory(profileRoot);
        Directory.CreateDirectory(keyRoot);

        builder.Services.AddSingleton(new ProfileImageStorage(profileRoot));
        builder.Services.AddDataProtection()
            .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "WebVibeTest")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRoot));

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        builder.Services.AddDefaultIdentity<IdentityUser>(options =>
            options.SignIn.RequireConfirmedAccount = false)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        builder.Services.AddControllersWithViews();
        builder.Services.AddRazorPages();
        builder.Services.AddSignalR(options =>
        {
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        });
        builder.Services.AddScoped<GameService>();
        builder.Services.AddScoped<IGameService, ExecutionStrategyGameService>();
        builder.Services.AddSingleton<IGameActionLog, InMemoryGameActionLog>();
        builder.Services.AddSingleton<InMemoryGameChat>();
        builder.Services.AddHostedService<GameTimeoutWorker>();
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.UseForwardedHeaders();
        if (app.Environment.IsDevelopment()) app.UseMigrationsEndPoint();
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(profileRoot),
            RequestPath = ProfileImageStorage.RequestPath
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapStaticAssets();
        app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
        app.MapRazorPages().WithStaticAssets();
        app.MapHub<GameHub>("/gameHub");

        if (builder.Configuration.GetValue("Database:ApplyMigrations", app.Environment.IsProduction()))
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");
            logger.LogInformation("Applying pending database migrations.");
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
        }

        app.Run();
    }

    private static void ConfigureRenderPort(WebApplicationBuilder builder)
    {
        var portValue = Environment.GetEnvironmentVariable("PORT");
        if (string.IsNullOrWhiteSpace(portValue)) return;
        if (!int.TryParse(portValue, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("PORT must be a valid TCP port number.");
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    }

    private static string ResolvePostgresConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        var value = string.IsNullOrWhiteSpace(databaseUrl) ? configured : databaseUrl;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Set DATABASE_URL or ConnectionStrings__DefaultConnection.");
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)) return value;

        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2) throw new InvalidOperationException("DATABASE_URL contains invalid credentials.");
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            Pooling = true,
            MaxPoolSize = 20,
            Timeout = 15,
            CommandTimeout = 30,
            SslMode = SslMode.Prefer
        }.ConnectionString;
    }
}
