using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using WinAdmin.Api;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Network;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Core.Modules;
using WinAdmin.Api.Modules;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Hardening;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

// ── CLI mode ────────────────────────────────────────────────────
if (args.Length > 0 && args[0] is "user" or "network" or "db" or "keys" or "role")
{
    var cliConfig = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    return await WinAdmin.Api.Cli.CliRunner.RunAsync(args, cliConfig);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Для запуска как Windows Service рабочий каталог службы — System32,
    // поэтому фиксируем content root на папке приложения (wwwroot, appsettings, БД).
    ContentRootPath = AppContext.BaseDirectory,
});

// Включает интеграцию с SCM (отклик на старт/стоп). No-op при запуске как консоль или под IIS.
builder.Host.UseWindowsService();

// ── Конфигурация ────────────────────────────────────────────────
// Машинная переменная — только для службы (до перезагрузки SCM её не передаёт); при
// `dotnet run` разработчика она указала бы на рабочую БД установленной службы.
string dbPath = WinAdminPaths.DatabasePath(builder.Configuration["WinAdmin:DatabasePath"],
    WindowsServiceHelpers.IsWindowsService() ? WinAdminPaths.MachineDatabasePath : () => null);
string dataDirectory = WinAdminPaths.DataDirectory(dbPath);

// Ключ шифрования секретов (keys\master.key под DPAPI машины).
string keysDirectory = Path.Combine(dataDirectory, "keys");
// Новый ключ выпускается только на чистой установке (см. SecretProtectorFactory).
ISecretProtector secretProtector = SecretProtectorFactory.Open(new MasterKeyStore(keysDirectory), dataDirectory, out var keyProblem);
if (keyProblem is not null)
    StartupDiagnostics.Warn(keyProblem);

// Провайдер БД: database.json (sqlite | postgresql), по умолчанию — SQLite по WinAdmin:DatabasePath.
// Без базы прав нет — при ошибке служба не стартует, но с понятным сообщением (и в журнале событий).
var databaseStore = new DatabaseSettingsStore(dataDirectory, secretProtector);
DatabaseSettings database;
try
{
    var storedDatabase = databaseStore.Read(dbPath);
    if (!databaseStore.Exists)
    {
        try { databaseStore.Write(storedDatabase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecretUnavailableException)
        {
            StartupDiagnostics.Warn($"Не удалось создать {databaseStore.FilePath}: {ex.Message}");
        }
    }
    database = databaseStore.ResolveForUse(storedDatabase);
}
catch (Exception ex) when (ex is SecretUnavailableException or InvalidOperationException or ArgumentException)
{
    return StartupDiagnostics.Fail($"WinAdmin не запущен: база данных не настроена. {ex.Message}");
}

// Сетевые настройки: network.json рядом с БД → Kestrel:Endpoints (перепривязка на лету).
// Порт из старого --urls используется только при первом создании файла.
int? legacyUrlsPort = NetworkEndpoints.PortFromUrls(builder.Configuration["urls"]);
var networkStore = new NetworkSettingsStore(dataDirectory);
try
{
    networkStore.EnsureCreated(legacyUrlsPort);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    StartupDiagnostics.Warn($"Не удалось создать {networkStore.FilePath}: {ex.Message}. Используется 127.0.0.1:{NetworkEndpoints.DefaultPort}.");
}
var networkSource = new NetworkConfigurationSource(networkStore);
builder.Configuration.Sources.Add(networkSource);
string? bootstrapKey = builder.Configuration["WinAdmin:BootstrapKey"];
var corsOrigins = builder.Configuration.GetSection("WinAdmin:CorsOrigins").Get<string[]>() ?? [];

var jwtOptions = builder.Configuration.GetSection("WinAdmin:Jwt").Get<JwtOptions>() ?? new JwtOptions();
try
{
    jwtOptions.Secret = JwtSecretStore.Resolve(jwtOptions.Secret,
        () => new JwtSecretStore(keysDirectory, secretProtector).GetOrCreate());
}
catch (Exception ex) when (ex is SecretUnavailableException or IOException or UnauthorizedAccessException)
{
    jwtOptions.Secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    StartupDiagnostics.Warn($"JWT-секрет не сохранён ({ex.Message}) — используется временный, сессии сбросятся при перезапуске.");
}

// ── Сервисы ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddWinAdminInfrastructure(database, jwtOptions);
builder.Services.AddWinAdminNetwork(networkStore);
builder.Services.AddSingleton(secretProtector);
builder.Services.AddSingleton<NetworkApplyWatchdog>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Auto";
        options.DefaultChallengeScheme = "Auto";
    })
    .AddPolicyScheme("Auto", "ApiKey or Bearer", options =>
    {
        options.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.ContainsKey("Authorization")
                ? JwtBearerDefaults.AuthenticationScheme
                : ApiKeyDefaults.Scheme;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { })
    .AddNegotiate()
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = DirectorySessionValidator.OnTokenValidated };
    });

var authzBuilder = builder.Services.AddAuthorizationBuilder();
authzBuilder.SetDefaultPolicy(new AuthorizationPolicyBuilder("Auto")
    .RequireAuthenticatedUser()
    .Build());
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<AccessContextFactory>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<IWindowsSignInReader, NegotiateWindowsSignInReader>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        string partition = ctx.Request.Headers[ApiKeyDefaults.HeaderName].FirstOrDefault()
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WinAdmin API",
        Version = "v1",
        Description = "API мониторинга и управления Windows-машиной.",
    });
    c.AddSecurityDefinition(ApiKeyDefaults.Scheme, new OpenApiSecurityScheme
    {
        Name = ApiKeyDefaults.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API-ключ в заголовке X-API-Key",
    });
    c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT token из POST /api/v1/auth/login",
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(ApiKeyDefaults.Scheme, doc)] = [],
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "WinAdmin.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

var app = builder.Build();

// ── База данных + bootstrap ──────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
    db.Database.Migrate();

    var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
    var raw = await apiKeys.EnsureBootstrapAsync(bootstrapKey);
    if (raw is not null)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "bootstrap-key.txt");
        await File.WriteAllTextAsync(file, raw);
        // Папка приложения читается всеми пользователями — admin-ключ закрываем отдельно.
        InstallationHardening.ProtectFile(file,
            [.. AclPlan.ForDataDirectory(),
             new AclRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value,
                 System.Security.AccessControl.FileSystemRights.FullControl)],
            app.Logger);
        app.Logger.LogWarning("Создан стартовый admin API-ключ: {Key}", raw);
        app.Logger.LogWarning("Сохранён в {File}. Удалите файл после копирования.", file);
    }

    await PlatformBootstrapper.RunAsync(db, scope.ServiceProvider.GetRequiredService<PermissionCatalog>());

    var users = scope.ServiceProvider.GetRequiredService<IUserService>();
    var advice = StartupAdvice.For(await users.AnyAsync(),
        await scope.ServiceProvider.GetRequiredService<IRoleService>().CountActiveAdministratorsAsync());
    if (advice.Count > 0)
    {
        app.Logger.LogWarning("════════════════════════════════════════════════════");
        foreach (var line in advice) app.Logger.LogWarning("{Advice}", line);
        app.Logger.LogWarning("════════════════════════════════════════════════════");
    }
}

StartupDiagnostics.Flush(app.Logger);

// ── Сеть ─────────────────────────────────────────────────────────
if (networkSource.Provider?.LastError is { } networkError)
    app.Logger.LogError("{File}: {Error}. Используются настройки по умолчанию (127.0.0.1:{Port}).",
        networkStore.FilePath, networkError, NetworkEndpoints.DefaultPort);

if (WindowsServiceHelpers.IsWindowsService())
{
    try
    {
        StartupHardening.Run(AppContext.BaseDirectory, dataDirectory, new MasterKeyStore(keysDirectory),
            Path.Combine(AppContext.BaseDirectory, "bootstrap-key.txt"), app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Не удалось исправить права на папки WinAdmin.");
    }
    try
    {
        var network = app.Services.GetRequiredService<INetworkSettingsService>();
        network.ApplyFirewall(network.Current, legacyUrlsPort is int p ? [p] : []);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Не удалось привести правило брандмауэра к сетевым настройкам.");
    }
}

// ── Конвейер ────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WinAdmin API v1"));

app.UseDefaultFiles();
app.UseStaticFiles();

if (corsOrigins.Length > 0)
    app.UseCors();

app.UseRouting();
app.UseMiddleware<ModuleAvailabilityMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", machine = Environment.MachineName, time = DateTimeOffset.UtcNow }))
    .AllowAnonymous();

app.MapFallbackToFile("index.html");

app.Run();
return 0;

/// <summary>Точка входа (public для интеграционных тестов).</summary>
public partial class Program { }
