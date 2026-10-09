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
using WinAdmin.Api.Auth;
using WinAdmin.Api.Network;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Storage;

// ── CLI mode ────────────────────────────────────────────────────
if (args.Length > 0 && args[0] == "user")
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
string dbPath = WinAdminPaths.DatabasePath(builder.Configuration["WinAdmin:DatabasePath"]);
string dataDirectory = WinAdminPaths.DataDirectory(dbPath);
string connectionString = $"Data Source={dbPath}";

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
    Console.WriteLine($"Не удалось создать {networkStore.FilePath}: {ex.Message}. Используется 127.0.0.1:{NetworkEndpoints.DefaultPort}.");
}
var networkSource = new NetworkConfigurationSource(networkStore);
builder.Configuration.Sources.Add(networkSource);
string? bootstrapKey = builder.Configuration["WinAdmin:BootstrapKey"];
var corsOrigins = builder.Configuration.GetSection("WinAdmin:CorsOrigins").Get<string[]>() ?? [];

var jwtOptions = builder.Configuration.GetSection("WinAdmin:Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
{
    var bytes = new byte[32];
    System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
    jwtOptions.Secret = Convert.ToBase64String(bytes);
    Console.WriteLine("════════════════════════════════════════════════════");
    Console.WriteLine("JWT Secret не задан — используется временный ключ.");
    Console.WriteLine("При перезапуске все сессии будут сброшены.");
    Console.WriteLine($"Задайте переменную окружения: WinAdmin__Jwt__Secret={jwtOptions.Secret}");
    Console.WriteLine("════════════════════════════════════════════════════");
}

// ── Сервисы ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddWinAdminInfrastructure(connectionString, jwtOptions);
builder.Services.AddWinAdminNetwork(networkStore);
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
    });

builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
var authzBuilder = builder.Services.AddAuthorizationBuilder();
authzBuilder.SetDefaultPolicy(new AuthorizationPolicyBuilder("Auto")
    .RequireAuthenticatedUser()
    .Build());
foreach (var scope in Scopes.All)
    authzBuilder.AddPolicy(ScopePolicy.Name(scope), p =>
    {
        p.AddAuthenticationSchemes("Auto");
        p.RequireAuthenticatedUser();
        p.AddRequirements(new ScopeRequirement(scope));
    });

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
        app.Logger.LogWarning("Создан стартовый admin API-ключ: {Key}", raw);
        app.Logger.LogWarning("Сохранён в {File}. Удалите файл после копирования.", file);
    }

    var users = scope.ServiceProvider.GetRequiredService<IUserService>();
    if (!await users.AnyAsync())
    {
        app.Logger.LogWarning("════════════════════════════════════════════════════");
        app.Logger.LogWarning("Пользователи не созданы. Создайте первого пользователя:");
        app.Logger.LogWarning("WinAdmin.exe user add --login admin --password <пароль> --scopes admin");
        app.Logger.LogWarning("════════════════════════════════════════════════════");
    }
}

// ── Сеть ─────────────────────────────────────────────────────────
if (networkSource.Provider?.LastError is { } networkError)
    app.Logger.LogError("{File}: {Error}. Используются настройки по умолчанию (127.0.0.1:{Port}).",
        networkStore.FilePath, networkError, NetworkEndpoints.DefaultPort);

if (WindowsServiceHelpers.IsWindowsService())
{
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
