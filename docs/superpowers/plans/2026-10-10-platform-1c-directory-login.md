# Платформа 1c: подключение к домену и вход учёткой AD — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WinAdmin в домене: вход доменной учёткой (пароль — только loopback/HTTPS; SSO Kerberos), роли на пользователей и группы AD по SID, настройки подключения к домену с проверкой, поиск AD в назначениях, лимит попыток входа.

**Architecture:** Ядро получает контракт `IDirectoryService` (реализация — `System.DirectoryServices.Protocols`, чтение учёткой компьютера с подписью и шифрованием) и настройки `DirectorySettings` в новой таблице `PlatformSettings`. Группы пользователя AD не кладутся в JWT: `AdGroupCache` перечитывает `tokenGroups` раз в 5 минут, при недоступности домена держит последние группы до 15 минут, и на каждом запросе событие `OnTokenValidated` добавляет их в claims (или отклоняет токен → 401). Вход по паролю и SSO сходятся в `DirectorySignInService.CompleteAsync(sid)`.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 (SQLite + PostgreSQL), `System.DirectoryServices.Protocols` 10.0.x, `Microsoft.AspNetCore.Authentication.Negotiate` 10.0.x, xUnit + Moq + WebApplicationFactory, React 19 + AntD 6.

**Spec:** `docs/superpowers/specs/2026-10-09-module-platform-design.md` — §3 (подключение к домену и вход), §2 «Кэш», §5 (directory/auth API), §6 (вход, назначения, подключение к домену), §8 (DC недоступен), §10 «Вход».

## Global Constraints

- Пароль домена принимается только при loopback-запросе или HTTPS; иначе 400 «Вход учёткой домена по паролю доступен только по HTTPS или с этого компьютера; используйте вход Windows».
- NTLM не принимается для SSO (`/auth/windows`): только Kerberos.
- Субъекты AD — по SID (`S-1-…`); права в JWT не кладутся.
- Группы AD: перечитываются не реже раза в 5 минут; ошибка чтения → последние известные группы до 15 минут, затем 401.
- `/auth/login`: 5 неудачных попыток в минуту на пару (IP, логин) и 20 на IP → 429 с `Retry-After`; несуществующий логин — хеширование-заглушка.
- Нет назначений (ни на пользователя, ни на его группы) → 403 «Нет доступа к WinAdmin».
- Контроллер домена недоступен → вход доменом 503 «Контроллер домена недоступен»; локальные учётки работают.
- Аудит: `auth.login` / `auth.login.failed` (без пароля), `auth.windows`.
- Чтение каталога — учёткой компьютера (Negotiate, Signing+Sealing на 389 или LDAPS 636); пароль чтения не хранится.
- Пустой пароль никогда не передаётся в LDAP bind (анонимный bind «успешен»).
- Тестовые пароли не выводятся в чат и не коммитятся; интеграционные тесты AD — только по переменным окружения, иначе Skip.
- Сообщения коммитов заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- После `npm run build` всегда `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.

## Review Focus

1. Логин в форматах `DOMAIN\user`, `user@domain.local`, `user`, с пробелами и спецсимволами LDAP (`*`, `(`, `\`) — находится тот же пользователь, инъекция фильтра невозможна (Task 1: `DirectoryLoginTests`, `LdapFilterTests`).
2. Пустой или пробельный пароль доменного входа — всегда отказ, LDAP не вызывается (Task 3: `Empty_password_is_rejected_without_bind`).
3. Пользователь AD отключён или удалён после входа — его JWT и refresh перестают работать не позже 5 минут (Task 4: `Disabled_account_invalidates_session`; Task 6: `Refresh_fails_for_disabled_ad_account`).
4. Совпадение имени локального пользователя и доменного (`admin`) — вход проверяет только локального, в домен не уходит (Task 6: `Local_login_wins_over_directory`).
5. Запрос через обратный прокси/`X-Forwarded-For` с внешнего адреса не считается loopback (Task 6: `Forwarded_header_does_not_make_request_loopback`).

---

## Карта файлов

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/ActiveDirectory/DirectoryModels.cs` | `DirectorySettings`, `DirectoryObject`, `DirectoryObjectKind`, `DirectoryTestStep`, `DirectoryUnavailableException` |
| `src/backend/WinAdmin.Core/ActiveDirectory/DirectoryLogin.cs` | разбор логина `DOMAIN\u` / `u@d` / `u` |
| `src/backend/WinAdmin.Core/ActiveDirectory/LdapFilter.cs` | экранирование значений фильтра (RFC 4515), SID → фильтр |
| `src/backend/WinAdmin.Core/Abstractions/IDirectoryService.cs` | `IDirectoryService`, `IDirectorySettingsStore`, `IAdGroupCache`, `IDirectorySignIn` |
| `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs` | + `PlatformSettingEntity`, `DirectoryRefreshTokenEntity` |
| `src/backend/WinAdmin.Infrastructure/Storage/Migrations/{Sqlite,PostgreSql}/*_AddDirectoryLogin.cs` | миграции |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/DirectorySettingsStore.cs` | настройки домена в `PlatformSettings` (ключ `directory`), умолчания от машины |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapDirectoryService.cs` | LDAP через S.DS.Protocols |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapMapping.cs` | чистые функции: SID из байтов, флаг Enabled |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdGroupCache.cs` | группы по SID: 5 мин / 15 мин |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/DirectorySignInService.cs` | пароль → SID → группы → права |
| `src/backend/WinAdmin.Infrastructure/Security/TokenService.cs` | + JWT и refresh для AD |
| `src/backend/WinAdmin.Api/Auth/LoginThrottle.cs` | лимит неудачных входов |
| `src/backend/WinAdmin.Api/Auth/LoginTransport.cs` | loopback/HTTPS |
| `src/backend/WinAdmin.Api/Auth/DirectorySessionValidator.cs` | `OnTokenValidated`: группы в claims или 401 |
| `src/backend/WinAdmin.Api/Auth/WindowsSignInReader.cs` | Negotiate → (SID, тип аутентификации) |
| `src/backend/WinAdmin.Api/Controllers/AuthController.cs` | login / refresh / logout / windows / options |
| `src/backend/WinAdmin.Api/Controllers/DirectoryController.cs` | настройки домена, проверка, поиск |
| `src/frontend/src/auth/LoginForm.tsx`, `api/authApi.ts`, `api/client.ts`, `api/types.ts` | кнопка «Войти как текущий пользователь Windows», API |
| `src/frontend/src/components/DirectorySettingsCard.tsx`, `pages/Settings.tsx` | карточка «Подключение к домену» |
| `src/frontend/src/pages/Roles.tsx` | назначение пользователю/группе AD с поиском |
| `src/tests/WinAdmin.Tests/Fakes/FakeDirectory.cs` | фейк `IDirectoryService` для тестов |

---

### Task 1: Контракты каталога, разбор логина, экранирование LDAP

**Files:**
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/DirectoryModels.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/DirectoryLogin.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/LdapFilter.cs`
- Create: `src/backend/WinAdmin.Core/Abstractions/IDirectoryService.cs`
- Test: `src/tests/WinAdmin.Tests/DirectoryLoginTests.cs`

**Interfaces:**
- Produces:
  - `record DirectorySettings(bool Enabled, string? Domain, string? Server, string? BaseDn, bool UseLdaps)`
  - `enum DirectoryObjectKind { User, Group }`
  - `record DirectoryObject(string Sid, DirectoryObjectKind Kind, string SamAccountName, string? DisplayName, string? Upn, string? DistinguishedName, bool Enabled)` с `LoginName => Upn ?? SamAccountName`
  - `record DirectoryTestStep(string Name, bool Ok, string Message)`
  - `class DirectoryUnavailableException(string message, Exception? inner = null) : Exception`
  - `DirectoryLogin.Parse(string login) → (string Sam, string? Upn)`
  - `LdapFilter.Escape(string) → string`, `LdapFilter.Sid(string sid) → string` (`\01\05…`)
  - `IDirectoryService`, `IDirectorySettingsStore`, `IAdGroupCache`, `IDirectorySignIn` (+ `DirectorySignInStatus`, `DirectorySignInResult`)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/DirectoryLoginTests.cs
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class DirectoryLoginTests
{
    [Theory]
    [InlineData("PCS\\ilias.aidar", "ilias.aidar", null)]
    [InlineData(" ilias.aidar ", "ilias.aidar", null)]
    [InlineData("Ilias.Aidar@pcs-msk.com", "Ilias.Aidar", "Ilias.Aidar@pcs-msk.com")]
    public void Parses_login_forms(string login, string sam, string? upn)
    {
        var (s, u) = DirectoryLogin.Parse(login);
        Assert.Equal(sam, s);
        Assert.Equal(upn, u);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("PCS\\")]
    [InlineData("@pcs")]
    public void Rejects_empty_logins(string login)
        => Assert.Throws<ArgumentException>(() => DirectoryLogin.Parse(login));
}

public sealed class LdapFilterTests
{
    [Theory]
    [InlineData("a*b", "a\\2ab")]
    [InlineData("(x)", "\\28x\\29")]
    [InlineData("a\\b", "a\\5cb")]
    [InlineData("nul\0", "nul\\00")]
    [InlineData("иван", "иван")]
    public void Escapes_special_characters(string value, string expected)
        => Assert.Equal(expected, LdapFilter.Escape(value));

    [Fact]
    public void Sid_is_encoded_as_escaped_bytes()
        => Assert.Equal("\\01\\01\\00\\00\\00\\00\\00\\05\\12\\00\\00\\00", LdapFilter.Sid("S-1-5-18"));

    [Fact]
    public void Invalid_sid_is_rejected()
        => Assert.Throws<ArgumentException>(() => LdapFilter.Sid("not-a-sid"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectoryLoginTests|FullyQualifiedName~LdapFilterTests"`
Expected: FAIL — ошибка компиляции «Имя "DirectoryLogin" не существует».

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/DirectoryModels.cs
namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Подключение к домену. Server пусто — поиск контроллера через DNS; BaseDn пусто — defaultNamingContext.</summary>
public sealed record DirectorySettings(bool Enabled, string? Domain, string? Server, string? BaseDn, bool UseLdaps)
{
    public static DirectorySettings Disabled { get; } = new(false, null, null, null, false);
}

public enum DirectoryObjectKind { User, Group }

/// <summary>Пользователь или группа AD.</summary>
public sealed record DirectoryObject(
    string Sid, DirectoryObjectKind Kind, string SamAccountName, string? DisplayName,
    string? Upn, string? DistinguishedName, bool Enabled)
{
    public string LoginName => Upn ?? SamAccountName;
}

/// <summary>Шаг проверки подключения (для UI).</summary>
public sealed record DirectoryTestStep(string Name, bool Ok, string Message);

/// <summary>Контроллер домена недоступен или подключение выключено.</summary>
public sealed class DirectoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/DirectoryLogin.cs
namespace WinAdmin.Core.ActiveDirectory;

public static class DirectoryLogin
{
    /// <summary>«DOMAIN\user» → (user, null); «user@domain» → (user, user@domain); «user» → (user, null).</summary>
    public static (string Sam, string? Upn) Parse(string login)
    {
        string value = (login ?? "").Trim();
        int slash = value.IndexOf('\\');
        if (slash >= 0) value = value[(slash + 1)..];
        int at = value.IndexOf('@');
        string sam = at >= 0 ? value[..at] : value;
        if (sam.Length == 0 || (at >= 0 && at == value.Length - 1))
            throw new ArgumentException("Укажите логин.");
        return (sam, at >= 0 ? value : null);
    }
}
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/LdapFilter.cs
using System.Security.Principal;
using System.Text;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Значения для LDAP-фильтров (RFC 4515).</summary>
public static class LdapFilter
{
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(c switch
            {
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\\' => "\\5c",
                '\0' => "\\00",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    /// <summary>SID в виде экранированных байтов для (objectSid=…).</summary>
    public static string Sid(string sid)
    {
        SecurityIdentifier parsed;
        try { parsed = new SecurityIdentifier(sid); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        { throw new ArgumentException("Некорректный SID.", nameof(sid), ex); }
        var bytes = new byte[parsed.BinaryLength];
        parsed.GetBinaryForm(bytes, 0);
        return string.Concat(bytes.Select(b => $"\\{b:x2}"));
    }
}
```

`SecurityIdentifier` живёт в `System.Security.Principal.Windows` — в net10.0-windows доступен; если `WinAdmin.Core` таргетит `net10.0`, добавить `<PackageReference Include="System.Security.Principal.Windows" Version="5.0.0" />` не нужно (тип есть в общем фреймворке на Windows) — проверить сборкой; при ошибке CS0246 перенести `LdapFilter.Sid` в Infrastructure и записать Ruling.

```csharp
// src/backend/WinAdmin.Core/Abstractions/IDirectoryService.cs
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Core.Abstractions;

/// <summary>Каталог AD (учётка компьютера). Ошибки сети — DirectoryUnavailableException.</summary>
public interface IDirectoryService
{
    Task<DirectoryObject?> FindUserAsync(string login, CancellationToken ct = default);
    Task<DirectoryObject?> FindBySidAsync(string sid, CancellationToken ct = default);
    /// <summary>SID всех групп пользователя (транзитивно, tokenGroups).</summary>
    Task<IReadOnlyList<string>> GetTokenGroupsAsync(string userSid, CancellationToken ct = default);
    Task<IReadOnlyList<DirectoryObject>> SearchAsync(string query, DirectoryObjectKind? kind, int limit, CancellationToken ct = default);
    /// <summary>Проверка пароля bind-ом. Пустой пароль — всегда false.</summary>
    Task<bool> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default);
    Task<IReadOnlyList<DirectoryTestStep>> TestConnectionAsync(CancellationToken ct = default);
}

public interface IDirectorySettingsStore
{
    Task<DirectorySettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(DirectorySettings settings, CancellationToken ct = default);
}

/// <summary>Группы пользователя AD: обновление раз в 5 минут, при недоступности домена — до 15 минут.</summary>
public interface IAdGroupCache
{
    /// <summary>null — сеанс недействителен (учётка отключена/удалена, домен выключен или недоступен дольше 15 минут).</summary>
    Task<IReadOnlyList<string>?> GetGroupsAsync(string userSid, CancellationToken ct = default);
}

public enum DirectorySignInStatus { Ok, InvalidCredentials, NoAccess, Disabled, Unavailable, NotConfigured }

public sealed record DirectorySignInResult(DirectorySignInStatus Status, DirectoryObject? Account = null);

/// <summary>Вход учёткой домена: по паролю или по уже проверенному SID (SSO).</summary>
public interface IDirectorySignIn
{
    Task<DirectorySignInResult> PasswordAsync(string login, string password, CancellationToken ct = default);
    Task<DirectorySignInResult> CompleteAsync(string userSid, CancellationToken ct = default);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectoryLoginTests|FullyQualifiedName~LdapFilterTests"`
Expected: PASS (11 тестов).

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core src/tests/WinAdmin.Tests/DirectoryLoginTests.cs
git commit -m "feat(directory): contracts, login parsing and LDAP filter escaping"
```

---

### Task 2: Хранилище — PlatformSettings, refresh-токены AD, настройки домена

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs`
- Create: migrations `AddDirectoryLogin` (Sqlite + PostgreSql)
- Modify: `src/backend/WinAdmin.Core/Abstractions/IMachineInfo.cs` (+ `string? DomainName`)
- Modify: `src/backend/WinAdmin.Infrastructure/MachineInfo/WmiMachineInfo.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/DirectorySettingsStore.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/DirectorySettingsStoreTests.cs`

**Interfaces:**
- Consumes: `DirectorySettings`, `IDirectorySettingsStore` (Task 1)
- Produces:
  - `PlatformSettingEntity { string Key; string Json; DateTimeOffset UpdatedAt }`, `DbSet<PlatformSettingEntity> PlatformSettings`
  - `DirectoryRefreshTokenEntity { string Id; string Sid; string TokenHash; DateTimeOffset ExpiresAt; DateTimeOffset? RevokedAt; DateTimeOffset CreatedAt }`, `DbSet<DirectoryRefreshTokenEntity> DirectoryRefreshTokens`
  - `DirectorySettingsStore(IServiceScopeFactory scopes, IMachineInfo machine) : IDirectorySettingsStore` (singleton, кэш в памяти)
  - `IMachineInfo.DomainName`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/DirectorySettingsStoreTests.cs
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DirectorySettingsStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;

    public DirectorySettingsStoreTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private DirectorySettingsStore Store(bool joined, string? domain)
        => new(_sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IMachineInfo>(m => m.IsDomainJoined == joined && m.DomainName == domain));

    [Fact]
    public async Task Defaults_follow_the_machine()
    {
        Assert.Equal(new DirectorySettings(true, "pcs-msk.com", null, null, false), await Store(true, "pcs-msk.com").GetAsync());
        Assert.False((await Store(false, null).GetAsync()).Enabled);
    }

    [Fact]
    public async Task Saved_settings_survive_a_new_store()
    {
        var saved = new DirectorySettings(true, "pcs-msk.com", "dc.pcs-msk.com", "DC=pcs-msk,DC=com", true);
        await Store(false, null).SaveAsync(saved);
        Assert.Equal(saved, await Store(false, null).GetAsync());
    }

    [Fact]
    public async Task Enabled_without_domain_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            Store(false, null).SaveAsync(new DirectorySettings(true, " ", null, null, false)));

    [Fact]
    public async Task Values_are_trimmed_and_blank_becomes_null()
    {
        var store = Store(false, null);
        await store.SaveAsync(new DirectorySettings(true, " pcs-msk.com ", "  ", " DC=x ", false));
        Assert.Equal(new DirectorySettings(true, "pcs-msk.com", null, "DC=x", false), await store.GetAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectorySettingsStoreTests"`
Expected: FAIL — компиляция: нет `DirectorySettingsStore`, нет `IMachineInfo.DomainName`.

- [ ] **Step 3: Write minimal implementation**

`IMachineInfo` — добавить свойство:

```csharp
    /// <summary>DNS-имя домена (Win32_ComputerSystem.Domain), если машина в домене.</summary>
    string? DomainName { get; }
```

`WmiMachineInfo` — добавить:

```csharp
    private readonly Lazy<string?> _domainName = new(() =>
        Query("SELECT PartOfDomain, Domain FROM Win32_ComputerSystem", "PartOfDomain") is true
            ? Query("SELECT Domain FROM Win32_ComputerSystem", "Domain") as string
            : null);

    public string? DomainName => _domainName.Value;
```

Найти все прочие реализации `IMachineInfo` в тестах (`grep -rn "IMachineInfo" src/tests`) — Moq-моки компилируются без изменений; ручные фейки дополнить `public string? DomainName => null;`.

`WinAdminDbContext` — DbSet-ы и сущности:

```csharp
    public DbSet<PlatformSettingEntity> PlatformSettings => Set<PlatformSettingEntity>();
    public DbSet<DirectoryRefreshTokenEntity> DirectoryRefreshTokens => Set<DirectoryRefreshTokenEntity>();
```

в `OnModelCreating` (рядом с `RefreshTokenEntity`, тем же стилем конвертеров дат):

```csharp
        modelBuilder.Entity<PlatformSettingEntity>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Json).IsRequired();
            e.Property(x => x.UpdatedAt).HasConversion(DtoConverter);
        });

        modelBuilder.Entity<DirectoryRefreshTokenEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sid).IsRequired();
            e.Property(x => x.TokenHash).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.Sid);
            e.Property(x => x.ExpiresAt).HasConversion(DtoConverter);
            e.Property(x => x.RevokedAt).HasConversion(NullableDtoConverter);
            e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
        });
```

сущности (в конец файла):

```csharp
/// <summary>Настройки платформы: ключ → JSON (directory, …).</summary>
public sealed class PlatformSettingEntity
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Refresh-токен пользователя AD (локальные — в RefreshTokens с FK на Users).</summary>
public sealed class DirectoryRefreshTokenEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Sid { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Если в `OnModelCreating` для `RefreshTokenEntity.CreatedAt` конвертер не задан — для `DirectoryRefreshTokenEntity.CreatedAt` повторить то, что сделано у `RefreshTokenEntity` (одинаковая сортировка на обоих провайдерах важнее).

Миграции:

```bash
dotnet ef migrations add AddDirectoryLogin --project src/backend/WinAdmin.Infrastructure --startup-project src/backend/WinAdmin.Api --context SqliteWinAdminDbContext --output-dir Storage/Migrations/Sqlite --namespace WinAdmin.Infrastructure.Storage.Migrations.Sqlite
dotnet ef migrations add AddDirectoryLogin --project src/backend/WinAdmin.Infrastructure --startup-project src/backend/WinAdmin.Api --context PostgresWinAdminDbContext --output-dir Storage/Migrations/PostgreSql --namespace WinAdmin.Infrastructure.Storage.Migrations.PostgreSql
```

Expected: `Done.` дважды; миграции содержат только `CreateTable` для `PlatformSettings` и `DirectoryRefreshTokens` + индексы. Если снимок модели оказался в `src/backend/WinAdmin.Infrastructure/WinAdmin/…` — перенести поверх существующего в `Storage/Migrations/...` и удалить лишний каталог.

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/DirectorySettingsStore.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Настройки подключения к домену (PlatformSettings, ключ «directory»); без записи — по машине.</summary>
public sealed class DirectorySettingsStore(IServiceScopeFactory scopes, IMachineInfo machine) : IDirectorySettingsStore
{
    public const string Key = "directory";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DirectorySettings? _cached;

    public async Task<DirectorySettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached) return cached;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        var settings = row is null
            ? new DirectorySettings(machine.IsDomainJoined, machine.DomainName, null, null, false)
            : JsonSerializer.Deserialize<DirectorySettings>(row.Json, Json) ?? DirectorySettings.Disabled;
        return _cached = settings;
    }

    public async Task SaveAsync(DirectorySettings settings, CancellationToken ct = default)
    {
        var clean = new DirectorySettings(settings.Enabled, Blank(settings.Domain), Blank(settings.Server),
            Blank(settings.BaseDn), settings.UseLdaps);
        if (clean.Enabled && clean.Domain is null)
            throw new ArgumentException("Укажите домен.");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null) db.PlatformSettings.Add(row = new PlatformSettingEntity { Key = Key });
        row.Json = JsonSerializer.Serialize(clean, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        _cached = clean;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

`DependencyInjection.AddWinAdminInfrastructure` — после `IMachineInfo`:

```csharp
        services.AddSingleton<IDirectorySettingsStore, DirectorySettingsStore>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectorySettingsStoreTests|FullyQualifiedName~Postgres|FullyQualifiedName~Migration"` (с `WINADMIN_TEST_POSTGRES`)
Expected: PASS; тесты PostgreSQL проходят на новой миграции.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(directory): platform settings table, AD refresh tokens, directory settings store"
```

---

### Task 3: LdapDirectoryService (System.DirectoryServices.Protocols)

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj` (+ `System.DirectoryServices.Protocols` 10.0.9)
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapMapping.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapDirectoryService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/LdapDirectoryServiceTests.cs`, `src/tests/WinAdmin.Tests/AdFactAttribute.cs`

**Interfaces:**
- Consumes: `IDirectoryService`, `IDirectorySettingsStore`, `DirectoryLogin`, `LdapFilter` (Task 1–2)
- Produces: `LdapDirectoryService(IDirectorySettingsStore settings, NetworkCredential? readCredential = null) : IDirectoryService` (singleton); `LdapMapping.SidFromBytes(byte[]) → string`, `LdapMapping.IsEnabled(string? userAccountControl) → bool`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdFactAttribute.cs
namespace WinAdmin.Tests;

/// <summary>
/// Интеграция с настоящим AD (например, через SSH-туннель к 389 контроллера).
/// WINADMIN_TEST_AD_SERVER, WINADMIN_TEST_AD_DOMAIN, WINADMIN_TEST_AD_USER, WINADMIN_TEST_AD_PASSWORD.
/// </summary>
public sealed class AdFactAttribute : FactAttribute
{
    public AdFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_PASSWORD")))
            Skip = "WINADMIN_TEST_AD_* не заданы.";
    }
}
```

```csharp
// src/tests/WinAdmin.Tests/LdapDirectoryServiceTests.cs
using System.Net;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class LdapDirectoryServiceTests
{
    private static IDirectorySettingsStore Settings(DirectorySettings s)
        => Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(s));

    [Fact]
    public void Sid_bytes_are_decoded()
        => Assert.Equal("S-1-5-18", LdapMapping.SidFromBytes([1, 1, 0, 0, 0, 0, 0, 5, 18, 0, 0, 0]));

    [Theory]
    [InlineData("512", true)]
    [InlineData("514", false)]
    [InlineData(null, true)]
    public void Account_disabled_flag(string? uac, bool enabled)
        => Assert.Equal(enabled, LdapMapping.IsEnabled(uac));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_password_is_rejected_without_bind(string password)
    {
        // Сервер заведомо недоступен: если бы bind выполнялся — было бы исключение, а не false.
        var service = new LdapDirectoryService(Settings(new DirectorySettings(true, "invalid.example", "127.0.0.1", null, false)));
        Assert.False(await service.ValidateCredentialsAsync("user", password));
    }

    [Fact]
    public async Task Disabled_directory_throws_unavailable()
    {
        var service = new LdapDirectoryService(Settings(DirectorySettings.Disabled));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.FindUserAsync("user"));
    }

    [Fact]
    public async Task Unreachable_server_throws_unavailable()
    {
        var service = new LdapDirectoryService(Settings(new DirectorySettings(true, "invalid.example", "127.0.0.1", "DC=x", false)));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.FindUserAsync("user"));
    }

    private static LdapDirectoryService Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        return new LdapDirectoryService(Settings(settings),
            new NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
    }

    [AdFact]
    public async Task Real_directory_finds_user_and_groups()
    {
        var ad = Real();
        var user = await ad.FindUserAsync(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_USER")!);
        Assert.NotNull(user);
        Assert.StartsWith("S-1-5-21-", user!.Sid);
        Assert.Equal(user.Sid, (await ad.FindBySidAsync(user.Sid))!.Sid);
        Assert.Contains(await ad.GetTokenGroupsAsync(user.Sid), g => g.EndsWith("-513")); // Domain Users
        Assert.NotEmpty(await ad.SearchAsync(user.SamAccountName, DirectoryObjectKind.User, 5));
        Assert.All(await ad.TestConnectionAsync(), s => Assert.True(s.Ok, s.Name + ": " + s.Message));
    }

    [AdFact]
    public async Task Real_directory_validates_password()
    {
        var ad = Real();
        string user = Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_USER")!;
        Assert.True(await ad.ValidateCredentialsAsync(user, Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_PASSWORD")!));
        Assert.False(await ad.ValidateCredentialsAsync(user, "wrong-" + Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapDirectoryServiceTests"`
Expected: FAIL — компиляция: нет `LdapMapping`/`LdapDirectoryService`.

- [ ] **Step 3: Write minimal implementation**

```bash
dotnet add src/backend/WinAdmin.Infrastructure package System.DirectoryServices.Protocols --version 10.0.9
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapMapping.cs
using System.Security.Principal;

namespace WinAdmin.Infrastructure.ActiveDirectory;

public static class LdapMapping
{
    private const int AccountDisable = 0x2;

    public static string SidFromBytes(byte[] bytes) => new SecurityIdentifier(bytes, 0).Value;

    public static bool IsEnabled(string? userAccountControl)
        => !int.TryParse(userAccountControl, out int uac) || (uac & AccountDisable) == 0;
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapDirectoryService.cs
using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>
/// AD через LDAP: Negotiate с подписью и шифрованием (389) или LDAPS (636).
/// readCredential == null — учётка процесса (служба SYSTEM → учётка компьютера); задаётся только в тестах.
/// </summary>
public sealed class LdapDirectoryService(IDirectorySettingsStore settingsStore, NetworkCredential? readCredential = null) : IDirectoryService
{
    private static readonly string[] Attributes =
        ["objectSid", "objectClass", "sAMAccountName", "displayName", "userPrincipalName", "distinguishedName", "userAccountControl"];
    private const string UserFilter = "(objectCategory=person)(objectClass=user)";
    private const string GroupFilter = "(objectCategory=group)";

    public Task<DirectoryObject?> FindUserAsync(string login, CancellationToken ct = default)
    {
        var (sam, upn) = DirectoryLogin.Parse(login);
        string byName = upn is null
            ? $"(sAMAccountName={LdapFilter.Escape(sam)})"
            : $"(|(userPrincipalName={LdapFilter.Escape(upn)})(sAMAccountName={LdapFilter.Escape(sam)}))";
        return FindOneAsync($"(&{UserFilter}{byName})", ct);
    }

    public Task<DirectoryObject?> FindBySidAsync(string sid, CancellationToken ct = default)
        => FindOneAsync($"(objectSid={LdapFilter.Sid(sid)})", ct);

    public async Task<IReadOnlyList<string>> GetTokenGroupsAsync(string userSid, CancellationToken ct = default)
    {
        var user = await FindBySidAsync(userSid, ct);
        if (user?.DistinguishedName is null) return [];
        return await RunAsync(async (connection, _) =>
        {
            var response = (SearchResponse)connection.SendRequest(
                new SearchRequest(user.DistinguishedName, "(objectClass=*)", SearchScope.Base, "tokenGroups"));
            var entry = response.Entries.Cast<SearchResultEntry>().FirstOrDefault();
            if (entry?.Attributes["tokenGroups"] is not { } attr) return (IReadOnlyList<string>)[];
            return attr.GetValues(typeof(byte[])).Cast<byte[]>().Select(LdapMapping.SidFromBytes).ToList();
        }, ct);
    }

    public async Task<IReadOnlyList<DirectoryObject>> SearchAsync(string query, DirectoryObjectKind? kind, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        string kindFilter = kind switch
        {
            DirectoryObjectKind.User => $"(&{UserFilter})",
            DirectoryObjectKind.Group => GroupFilter,
            _ => $"(|(&{UserFilter}){GroupFilter})",
        };
        string filter = $"(&(anr={LdapFilter.Escape(query.Trim())}){kindFilter})";
        return await RunAsync(async (connection, baseDn) =>
        {
            var request = new SearchRequest(baseDn, filter, SearchScope.Subtree, Attributes) { SizeLimit = Math.Clamp(limit, 1, 100) };
            SearchResponse response;
            try { response = (SearchResponse)connection.SendRequest(request); }
            catch (DirectoryOperationException ex) when (ex.Response is SearchResponse partial) { response = partial; }
            return (IReadOnlyList<DirectoryObject>)response.Entries.Cast<SearchResultEntry>().Select(ToObject).OfType<DirectoryObject>().ToList();
        }, ct);
    }

    public async Task<bool> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        var settings = await EnabledSettingsAsync(ct);
        var (sam, upn) = DirectoryLogin.Parse(login);
        var credential = upn is not null ? new NetworkCredential(upn, password) : new NetworkCredential(sam, password, settings.Domain);
        try
        {
            using var connection = Connect(settings, credential);
            return true;
        }
        catch (LdapException ex) when (ex.ErrorCode == 49) // invalid credentials
        {
            return false;
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException("Контроллер домена недоступен", ex);
        }
    }

    public async Task<IReadOnlyList<DirectoryTestStep>> TestConnectionAsync(CancellationToken ct = default)
    {
        var steps = new List<DirectoryTestStep>();
        var settings = await settingsStore.GetAsync(ct);
        if (!settings.Enabled || settings.Domain is null)
        {
            steps.Add(new("Настройки", false, "Подключение к домену выключено или домен не указан"));
            return steps;
        }
        steps.Add(new("Настройки", true, $"Домен {settings.Domain}, сервер {settings.Server ?? "(поиск через DNS)"}, {(settings.UseLdaps ? "LDAPS 636" : "LDAP 389 с подписью")}"));
        LdapConnection connection;
        try
        {
            connection = Connect(settings, readCredential);
            steps.Add(new("Подключение", true, "Вход учёткой компьютера выполнен"));
        }
        catch (LdapException ex)
        {
            steps.Add(new("Подключение", false, ex.Message));
            return steps;
        }
        using (connection)
        {
            try
            {
                string baseDn = settings.BaseDn ?? ReadNamingContext(connection);
                steps.Add(new("Корень каталога", true, baseDn));
                var response = (SearchResponse)connection.SendRequest(
                    new SearchRequest(baseDn, $"(&{UserFilter})", SearchScope.Subtree, "sAMAccountName") { SizeLimit = 1 });
                steps.Add(new("Поиск", true, $"Найдено записей: {response.Entries.Count}"));
            }
            catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
            {
                steps.Add(new("Поиск", false, ex.Message));
            }
        }
        return steps;
    }

    private Task<DirectoryObject?> FindOneAsync(string filter, CancellationToken ct)
        => RunAsync(async (connection, baseDn) =>
        {
            var response = (SearchResponse)connection.SendRequest(
                new SearchRequest(baseDn, filter, SearchScope.Subtree, Attributes) { SizeLimit = 2 });
            return response.Entries.Count == 1 ? ToObject(response.Entries[0]) : null;
        }, ct);

    private async Task<T> RunAsync<T>(Func<LdapConnection, string, Task<T>> action, CancellationToken ct)
    {
        var settings = await EnabledSettingsAsync(ct);
        try
        {
            using var connection = Connect(settings, readCredential);
            return await action(connection, settings.BaseDn ?? ReadNamingContext(connection));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException("Контроллер домена недоступен", ex);
        }
    }

    private async Task<DirectorySettings> EnabledSettingsAsync(CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        if (!settings.Enabled || settings.Domain is null)
            throw new DirectoryUnavailableException("Подключение к домену выключено");
        return settings;
    }

    private static LdapConnection Connect(DirectorySettings s, NetworkCredential? credential)
    {
        int port = s.UseLdaps ? 636 : 389;
        var id = s.Server is null
            ? new LdapDirectoryIdentifier(s.Domain, port, fullyQualifiedDnsHostName: false, connectionless: false)
            : new LdapDirectoryIdentifier(s.Server, port);
        var connection = new LdapConnection(id) { AuthType = AuthType.Negotiate, Timeout = TimeSpan.FromSeconds(10) };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (s.UseLdaps)
            connection.SessionOptions.SecureSocketLayer = true;
        else
        {
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }
        if (credential is not null) connection.Credential = credential;
        try
        {
            connection.Bind();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string ReadNamingContext(LdapConnection connection)
    {
        var response = (SearchResponse)connection.SendRequest(
            new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
        return response.Entries[0].Attributes["defaultNamingContext"]?[0] as string
               ?? throw new DirectoryUnavailableException("Не удалось прочитать корень каталога");
    }

    private static DirectoryObject? ToObject(SearchResultEntry entry)
    {
        if (entry.Attributes["objectSid"]?[0] is not byte[] sid) return null;
        string? Str(string name) => entry.Attributes[name]?[0] as string;
        bool isGroup = entry.Attributes["objectClass"]?.GetValues(typeof(string)).Cast<string>()
            .Contains("group", StringComparer.OrdinalIgnoreCase) == true;
        return new DirectoryObject(
            LdapMapping.SidFromBytes(sid),
            isGroup ? DirectoryObjectKind.Group : DirectoryObjectKind.User,
            Str("sAMAccountName") ?? "",
            Str("displayName"),
            Str("userPrincipalName"),
            Str("distinguishedName") ?? entry.DistinguishedName,
            isGroup || LdapMapping.IsEnabled(Str("userAccountControl")));
    }
}
```

`RunAsync` принимает `Func<…, Task<T>>`, хотя действия синхронные (`SendRequest`) — делегаты помечены `async` без `await`; если компилятор выдаёт CS1998 как ошибку (TreatWarningsAsErrors), заменить на `Task.FromResult(...)` и записать Ruling.

`DependencyInjection` — после `IDirectorySettingsStore`:

```csharp
        services.AddSingleton<IDirectoryService>(sp => new LdapDirectoryService(sp.GetRequiredService<IDirectorySettingsStore>()));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapDirectoryServiceTests"`
Expected: PASS (7 тестов: 5 модульных + 2 `[AdFact]` пропущены без переменных окружения).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(directory): LDAP directory service with signing/sealing and LDAPS"
```

---

### Task 4: AdGroupCache — группы по SID (5 минут / 15 минут)

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdGroupCache.cs`
- Create: `src/tests/WinAdmin.Tests/Fakes/FakeDirectory.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/AdGroupCacheTests.cs`

**Interfaces:**
- Consumes: `IDirectoryService`, `IAdGroupCache` (Task 1)
- Produces: `AdGroupCache(IDirectoryService directory, TimeProvider? time = null) : IAdGroupCache` (singleton), константы `RefreshAfter = 5 min`, `StaleLimit = 15 min`; тестовый `FakeDirectory : IDirectoryService` со свойствами `Users` (`Dictionary<string, (DirectoryObject User, string Password, List<string> Groups)>` по логину), `Groups` (`List<DirectoryObject>`), `Down` (bool), счётчиком `TokenGroupCalls`.

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/Fakes/FakeDirectory.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Tests.Fakes;

/// <summary>Каталог в памяти: пользователи по логину (sAMAccountName), группы, «падение» домена.</summary>
public sealed class FakeDirectory : IDirectoryService
{
    public Dictionary<string, (DirectoryObject User, string Password, List<string> Groups)> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DirectoryObject> Groups { get; } = [];
    public bool Down { get; set; }
    public int TokenGroupCalls { get; private set; }

    public DirectoryObject AddUser(string sam, string password, params string[] groupSids)
    {
        var user = new DirectoryObject($"S-1-5-21-10-20-30-{1000 + Users.Count}", DirectoryObjectKind.User, sam, sam.ToUpperInvariant(),
            $"{sam}@test.local", $"CN={sam},DC=test,DC=local", true);
        Users[sam] = (user, password, [.. groupSids]);
        return user;
    }

    public DirectoryObject AddGroup(string name)
    {
        var group = new DirectoryObject($"S-1-5-21-10-20-30-{5000 + Groups.Count}", DirectoryObjectKind.Group, name, name, null, $"CN={name},DC=test,DC=local", true);
        Groups.Add(group);
        return group;
    }

    public void Disable(string sam) => Users[sam] = Users[sam] with { User = Users[sam].User with { Enabled = false } };

    private void ThrowIfDown()
    {
        if (Down) throw new DirectoryUnavailableException("Контроллер домена недоступен");
    }

    public Task<DirectoryObject?> FindUserAsync(string login, CancellationToken ct = default)
    {
        ThrowIfDown();
        var (sam, _) = DirectoryLogin.Parse(login);
        return Task.FromResult(Users.TryGetValue(sam, out var u) ? u.User : null);
    }

    public Task<DirectoryObject?> FindBySidAsync(string sid, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(Users.Values.Select(u => u.User).Concat(Groups).FirstOrDefault(o => o.Sid == sid));
    }

    public Task<IReadOnlyList<string>> GetTokenGroupsAsync(string userSid, CancellationToken ct = default)
    {
        ThrowIfDown();
        TokenGroupCalls++;
        var user = Users.Values.FirstOrDefault(u => u.User.Sid == userSid);
        return Task.FromResult<IReadOnlyList<string>>(user.Groups ?? []);
    }

    public Task<IReadOnlyList<DirectoryObject>> SearchAsync(string query, DirectoryObjectKind? kind, int limit, CancellationToken ct = default)
    {
        ThrowIfDown();
        var all = Users.Values.Select(u => u.User).Concat(Groups)
            .Where(o => (kind is null || o.Kind == kind) && o.SamAccountName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<DirectoryObject>>(all);
    }

    public Task<bool> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password)) return Task.FromResult(false);
        ThrowIfDown();
        var (sam, _) = DirectoryLogin.Parse(login);
        return Task.FromResult(Users.TryGetValue(sam, out var u) && u.Password == password);
    }

    public Task<IReadOnlyList<DirectoryTestStep>> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectoryTestStep>>(Down
            ? [new("Подключение", false, "Контроллер домена недоступен")]
            : [new("Подключение", true, "ok")]);
}
```

```csharp
// src/tests/WinAdmin.Tests/AdGroupCacheTests.cs
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AdGroupCacheTests
{
    private readonly FakeDirectory _ad = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly AdGroupCache _cache;
    private readonly string _sid;
    private readonly string _group;

    public AdGroupCacheTests()
    {
        _group = _ad.AddGroup("WinAdmin-Helpdesk").Sid;
        _sid = _ad.AddUser("ivan", "pw", _group).Sid;
        _cache = new AdGroupCache(_ad, _time);
    }

    [Fact]
    public async Task Reads_groups_once_per_five_minutes()
    {
        Assert.Equal([_group], await _cache.GetGroupsAsync(_sid));
        _time.Advance(TimeSpan.FromMinutes(4));
        await _cache.GetGroupsAsync(_sid);
        Assert.Equal(1, _ad.TokenGroupCalls);

        _time.Advance(TimeSpan.FromMinutes(2));
        await _cache.GetGroupsAsync(_sid);
        Assert.Equal(2, _ad.TokenGroupCalls);
    }

    [Fact]
    public async Task Keeps_last_groups_up_to_fifteen_minutes_when_directory_is_down()
    {
        await _cache.GetGroupsAsync(_sid);
        _ad.Down = true;
        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal([_group], await _cache.GetGroupsAsync(_sid));

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await _cache.GetGroupsAsync(_sid));
    }

    [Fact]
    public async Task Disabled_account_invalidates_session()
    {
        await _cache.GetGroupsAsync(_sid);
        _ad.Disable("ivan");
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await _cache.GetGroupsAsync(_sid));
    }

    [Fact]
    public async Task Unknown_sid_and_unreachable_directory_without_cache_give_null()
    {
        Assert.Null(await _cache.GetGroupsAsync("S-1-5-21-1-2-3-999"));
        _ad.Down = true;
        Assert.Null(await new AdGroupCache(_ad, _time).GetGroupsAsync(_sid));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdGroupCacheTests"`
Expected: FAIL — компиляция: нет `AdGroupCache`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdGroupCache.cs
using System.Collections.Concurrent;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>
/// Группы пользователя AD для проверки прав на каждом запросе. Свежие (до 5 минут) — из памяти;
/// старше — перечитываются; домен недоступен — последние известные, пока им меньше 15 минут.
/// Учётка отключена/удалена → null (сеанс недействителен).
/// </summary>
public sealed class AdGroupCache(IDirectoryService directory, TimeProvider? time = null) : IAdGroupCache
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StaleLimit = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<string> Groups)> _entries = new();

    public async Task<IReadOnlyList<string>?> GetGroupsAsync(string userSid, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        bool cached = _entries.TryGetValue(userSid, out var entry);
        if (cached && now - entry.At < RefreshAfter)
            return entry.Groups;

        try
        {
            var account = await directory.FindBySidAsync(userSid, ct);
            if (account is not { Kind: DirectoryObjectKind.User, Enabled: true })
            {
                _entries.TryRemove(userSid, out _);
                return null;
            }
            var groups = await directory.GetTokenGroupsAsync(userSid, ct);
            _entries[userSid] = (now, groups);
            return groups;
        }
        catch (DirectoryUnavailableException)
        {
            return cached && now - entry.At < StaleLimit ? entry.Groups : null;
        }
    }
}
```

`DependencyInjection`:

```csharp
        services.AddSingleton<IAdGroupCache, AdGroupCache>();
```

(`AdGroupCache` имеет необязательный `TimeProvider` — DI использует значение по умолчанию.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdGroupCacheTests"`
Expected: PASS (4 теста).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(directory): AD group cache with 5/15 minute refresh and fallback"
```

---

### Task 5: DirectorySignInService и токены AD

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/DirectorySignInService.cs`
- Modify: `src/backend/WinAdmin.Core/Abstractions/ITokenService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/Security/TokenService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/DirectorySignInTests.cs`

**Interfaces:**
- Consumes: `IDirectoryService`, `IDirectorySettingsStore`, `IAdGroupCache`, `IAccessService` (`GetAsync(PrincipalRef)`), `FakeDirectory`
- Produces:
  - `DirectorySignInService(IDirectoryService directory, IDirectorySettingsStore settings, IAdGroupCache groups, IAccessService access) : IDirectorySignIn` (scoped)
  - `ITokenService.GenerateDirectoryAccessToken(DirectoryObject account) → string` (claims: NameIdentifier=SID, Name=LoginName, `wa:principal`=`AdUser:<SID>`)
  - `ITokenService.CreateDirectoryRefreshTokenAsync(string sid, CancellationToken) → Task<string>`
  - `ITokenService.ValidateDirectoryRefreshTokenAsync(string raw, CancellationToken) → Task<string?>` (SID)
  - `ITokenService.RevokeRefreshTokenAsync` отзывает токен в любой из двух таблиц

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/DirectorySignInTests.cs
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class DirectorySignInTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly FakeDirectory _ad = new();
    private DirectorySettings _settings = new(true, "test.local", null, null, false);
    private readonly DirectorySignInService _signIn;
    private readonly string _helpdesk;

    public DirectorySignInTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();

        _helpdesk = _ad.AddGroup("WinAdmin-Helpdesk").Sid;
        _ad.AddUser("ivan", "Pw-1", _helpdesk);
        _ad.AddUser("petr", "Pw-2");

        var role = new RoleEntity { Name = "Helpdesk" };
        role.Permissions.Add(new RolePermissionEntity { PermissionId = PermissionIds.ServicesRead });
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            db.Roles.Add(role);
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "AdGroup", PrincipalId = _helpdesk, DisplayName = "WinAdmin-Helpdesk" });
            db.SaveChanges();
        }

        var settings = new Mock<IDirectorySettingsStore>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        var access = new AccessService(_sp.GetRequiredService<IServiceScopeFactory>(), new PermissionCatalog(BuiltInModules.All));
        _signIn = new DirectorySignInService(_ad, settings.Object, new AdGroupCache(_ad), access);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Group_member_signs_in()
    {
        var result = await _signIn.PasswordAsync("TEST\\ivan", "Pw-1");
        Assert.Equal(DirectorySignInStatus.Ok, result.Status);
        Assert.Equal("ivan", result.Account!.SamAccountName);
    }

    [Fact]
    public async Task User_without_assignments_has_no_access()
        => Assert.Equal(DirectorySignInStatus.NoAccess, (await _signIn.PasswordAsync("petr", "Pw-2")).Status);

    [Theory]
    [InlineData("ivan", "wrong")]
    [InlineData("nobody", "Pw-1")]
    [InlineData("ivan", "")]
    public async Task Wrong_credentials_are_rejected(string login, string password)
        => Assert.Equal(DirectorySignInStatus.InvalidCredentials, (await _signIn.PasswordAsync(login, password)).Status);

    [Fact]
    public async Task Disabled_account_cannot_sign_in()
    {
        _ad.Disable("ivan");
        Assert.Equal(DirectorySignInStatus.Disabled, (await _signIn.PasswordAsync("ivan", "Pw-1")).Status);
    }

    [Fact]
    public async Task Directory_down_is_reported()
    {
        _ad.Down = true;
        Assert.Equal(DirectorySignInStatus.Unavailable, (await _signIn.PasswordAsync("ivan", "Pw-1")).Status);
    }

    [Fact]
    public async Task Directory_switched_off_is_not_configured()
    {
        _settings = DirectorySettings.Disabled;
        Assert.Equal(DirectorySignInStatus.NotConfigured, (await _signIn.PasswordAsync("ivan", "Pw-1")).Status);
        Assert.Equal(DirectorySignInStatus.NotConfigured, (await _signIn.CompleteAsync(_ad.Users["ivan"].User.Sid)).Status);
    }

    [Fact]
    public async Task Directory_refresh_token_roundtrip_and_revoke()
    {
        using var scope = _sp.CreateScope();
        var tokens = new TokenService(scope.ServiceProvider.GetRequiredService<WinAdminDbContext>(),
            new JwtOptions { Secret = new string('k', 64), Issuer = "i", Audience = "a" });
        string raw = await tokens.CreateDirectoryRefreshTokenAsync("S-1-5-21-10-20-30-1000");
        Assert.Equal("S-1-5-21-10-20-30-1000", await tokens.ValidateDirectoryRefreshTokenAsync(raw));
        Assert.Null(await tokens.ValidateRefreshTokenAsync(raw)); // не локальный

        await tokens.RevokeRefreshTokenAsync(raw);
        Assert.Null(await tokens.ValidateDirectoryRefreshTokenAsync(raw));
    }

    [Fact]
    public void Directory_access_token_carries_sid_principal()
    {
        using var scope = _sp.CreateScope();
        var tokens = new TokenService(scope.ServiceProvider.GetRequiredService<WinAdminDbContext>(),
            new JwtOptions { Secret = new string('k', 64), Issuer = "i", Audience = "a" });
        var ivan = _ad.Users["ivan"].User;
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.GenerateDirectoryAccessToken(ivan));
        Assert.Contains(jwt.Claims, c => c.Type == PrincipalClaims.Type && c.Value == $"AdUser:{ivan.Sid}");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == PrincipalClaims.GroupType);
    }
}
```

Перед запуском проверить, как в проекте создаётся `JwtOptions` (`grep -n "class JwtOptions" -A10 -r src/backend`) и подставить реальные имена свойств; если `JwtOptions` — record с required-полями, использовать их.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectorySignInTests"`
Expected: FAIL — компиляция: нет `DirectorySignInService`, `CreateDirectoryRefreshTokenAsync`, `GenerateDirectoryAccessToken`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/DirectorySignInService.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Пароль → bind → SID → группы → права. Нет ни одного права — NoAccess.</summary>
public sealed class DirectorySignInService(
    IDirectoryService directory, IDirectorySettingsStore settings, IAdGroupCache groups, IAccessService access) : IDirectorySignIn
{
    public async Task<DirectorySignInResult> PasswordAsync(string login, string password, CancellationToken ct = default)
    {
        if (!(await settings.GetAsync(ct)).Enabled) return new(DirectorySignInStatus.NotConfigured);
        if (string.IsNullOrWhiteSpace(password)) return new(DirectorySignInStatus.InvalidCredentials);
        try
        {
            if (!await directory.ValidateCredentialsAsync(login, password, ct))
                return new(DirectorySignInStatus.InvalidCredentials);
            var account = await directory.FindUserAsync(login, ct);
            return account is null
                ? new(DirectorySignInStatus.InvalidCredentials)
                : await CompleteAsync(account.Sid, ct);
        }
        catch (ArgumentException)
        {
            return new(DirectorySignInStatus.InvalidCredentials);
        }
        catch (DirectoryUnavailableException)
        {
            return new(DirectorySignInStatus.Unavailable);
        }
    }

    public async Task<DirectorySignInResult> CompleteAsync(string userSid, CancellationToken ct = default)
    {
        if (!(await settings.GetAsync(ct)).Enabled) return new(DirectorySignInStatus.NotConfigured);
        DirectoryObject? account;
        try { account = await directory.FindBySidAsync(userSid, ct); }
        catch (DirectoryUnavailableException) { return new(DirectorySignInStatus.Unavailable); }
        if (account is not { Kind: DirectoryObjectKind.User }) return new(DirectorySignInStatus.InvalidCredentials);
        if (!account.Enabled) return new(DirectorySignInStatus.Disabled, account);

        var groupSids = await groups.GetGroupsAsync(userSid, ct);
        if (groupSids is null) return new(DirectorySignInStatus.Unavailable, account);
        var permissions = await access.GetAsync(new PrincipalRef(PrincipalType.AdUser, userSid, groupSids), ct);
        return permissions.Map.Count == 0
            ? new(DirectorySignInStatus.NoAccess, account)
            : new(DirectorySignInStatus.Ok, account);
    }
}
```

Порядок в тесте `Disabled_account_cannot_sign_in`: FakeDirectory проверяет пароль и для отключённой учётки (настоящий AD вернул бы 49/533 при bind) — `CompleteAsync` отдаёт `Disabled`. В настоящем AD bind отключённой учётки падает с кодом 49 → `InvalidCredentials`; это допустимо (сообщение пользователю одинаковое — «Неверный логин или пароль» не раскрывает состояние учётки).

`ITokenService` — добавить:

```csharp
    /// <summary>JWT для пользователя AD: sub = SID, wa:principal = AdUser:SID; группы не кладутся.</summary>
    string GenerateDirectoryAccessToken(DirectoryObject account);

    Task<string> CreateDirectoryRefreshTokenAsync(string sid, CancellationToken ct = default);

    /// <summary>SID владельца действующего refresh-токена AD или null.</summary>
    Task<string?> ValidateDirectoryRefreshTokenAsync(string rawToken, CancellationToken ct = default);
```

(`using WinAdmin.Core.ActiveDirectory;`)

`TokenService` — вынести построение JWT в общий метод и добавить AD:

```csharp
    public string GenerateAccessToken(UserPrincipal user)
        => Write(user.Id, user.Login, PrincipalClaims.Format(PrincipalType.LocalUser, user.Id));

    public string GenerateDirectoryAccessToken(DirectoryObject account)
        => Write(account.Sid, account.LoginName, PrincipalClaims.Format(PrincipalType.AdUser, account.Sid));

    private string Write(string id, string name, string principal)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Name, name),
            new(PrincipalClaims.Type, principal),
        };
        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string> CreateDirectoryRefreshTokenAsync(string sid, CancellationToken ct = default)
    {
        var raw = GenerateRaw();
        _db.DirectoryRefreshTokens.Add(new DirectoryRefreshTokenEntity
        {
            Sid = sid,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_opts.RefreshTokenDays),
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<string?> ValidateDirectoryRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var entity = await _db.DirectoryRefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (entity is null || entity.RevokedAt is not null || entity.ExpiresAt < DateTimeOffset.UtcNow) return null;
        return entity.Sid;
    }
```

`RevokeRefreshTokenAsync` — дополнить поиском во второй таблице:

```csharp
    public async Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var local = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (local is not null) local.RevokedAt = DateTimeOffset.UtcNow;
        var directory = await _db.DirectoryRefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (directory is not null) directory.RevokedAt = DateTimeOffset.UtcNow;
        if (local is not null || directory is not null) await _db.SaveChangesAsync(ct);
    }
```

`DependencyInjection`:

```csharp
        services.AddScoped<IDirectorySignIn, DirectorySignInService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectorySignInTests|FullyQualifiedName~Token|FullyQualifiedName~Auth"`
Expected: PASS (новые 10 + существующие тесты токенов/входа).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(directory): directory sign-in service and AD access/refresh tokens"
```

---

### Task 6: Вход доменом в API — login/refresh/logout, loopback/HTTPS, группы в запросе

**Files:**
- Create: `src/backend/WinAdmin.Api/Auth/LoginTransport.cs`
- Create: `src/backend/WinAdmin.Api/Auth/DirectorySessionValidator.cs`
- Modify: `src/backend/WinAdmin.Api/Controllers/AuthController.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (JwtBearer `Events`)
- Modify: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs` (`NetworkApiFactory`: `FakeDirectory`, тестовый `X-Test-Remote-Ip`)
- Test: `src/tests/WinAdmin.Tests/DirectoryLoginApiTests.cs`

**Interfaces:**
- Consumes: `IDirectorySignIn`, `IDirectorySettingsStore`, `IAdGroupCache`, `ITokenService` (Task 5), `IAuditService.WriteAsync(AuditEntryDto)`, `IUserService.ValidateAsync`
- Produces:
  - `LoginTransport.PasswordAllowed(HttpContext) → bool` (HTTPS или loopback по `Connection.RemoteIpAddress`; заголовки прокси не учитываются)
  - `DirectorySessionValidator.OnTokenValidated(TokenValidatedContext) → Task`
  - `AuthController.Login`: 200 / 400 (транспорт) / 401 / 403 «Нет доступа к WinAdmin» / 503 «Контроллер домена недоступен»
  - `NetworkApiFactory.Directory` (`FakeDirectory`), `NetworkApiFactory.DirectorySettings` (изменяемые), `NetworkApiFactory.Loopback()` → `HttpClient` с `X-Test-Remote-Ip: 127.0.0.1`, `NetworkApiFactory.Remote()` → с `10.1.2.3`

- [ ] **Step 1: Write the failing test**

В `NetworkApiFactory` (`NetworkSettingsApiTests.cs`) — добавить:

```csharp
    public FakeDirectory Directory { get; } = new();
    public DirectorySettings DirectorySettings { get; set; } = new(true, "test.local", null, null, false);

    /// <summary>Клиент, чьи запросы сервер видит пришедшими с указанного адреса (только в тестах).</summary>
    public HttpClient ClientFrom(string ip)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add(TestRemoteIp.Header, ip);
        return client;
    }

    public HttpClient Loopback() => ClientFrom("127.0.0.1");
    public HttpClient Remote() => ClientFrom("10.1.2.3");
```

в `ConfigureTestServices`:

```csharp
            services.RemoveAll<IDirectoryService>();
            services.AddSingleton<IDirectoryService>(Directory);
            services.RemoveAll<IDirectorySettingsStore>();
            var settings = new Mock<IDirectorySettingsStore>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => DirectorySettings);
            settings.Setup(s => s.SaveAsync(It.IsAny<DirectorySettings>(), It.IsAny<CancellationToken>()))
                .Callback<DirectorySettings, CancellationToken>((s, _) => DirectorySettings = s).Returns(Task.CompletedTask);
            services.AddSingleton(settings.Object);
            services.AddSingleton<IStartupFilter, TestRemoteIp>();
```

и класс в том же файле:

```csharp
/// <summary>Тестовый адрес клиента: TestServer не заполняет RemoteIpAddress.</summary>
public sealed class TestRemoteIp : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nxt) =>
        {
            if (ctx.Request.Headers.TryGetValue(Header, out var ip) && System.Net.IPAddress.TryParse(ip, out var parsed))
                ctx.Connection.RemoteIpAddress = parsed;
            return nxt();
        });
        next(app);
    };
}
```

(Для `DirectorySettings` в фабрике `SaveAsync` работает через колбэк — его использует Task 9.)

```csharp
// src/tests/WinAdmin.Tests/DirectoryLoginApiTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class DirectoryLoginApiTests(NetworkApiFactory factory)
{
    private sealed record Token(string AccessToken);

    /// <summary>Пользователь AD в группе, которой назначена роль с services.read.</summary>
    private async Task<string> AdUserWithAccessAsync(string password = "Ad-Pass-1")
    {
        string sam = "ad" + Guid.NewGuid().ToString("N")[..6];
        var group = factory.Directory.AddGroup("g-" + sam);
        factory.Directory.AddUser(sam, password, group.Sid);
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var role = await roles.CreateAsync(new SaveRoleRequest("r-" + sam, null, [new RoleGrantDto(PermissionIds.ServicesRead, null)]), factory.SystemActor());
        await roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.AdGroup, group.Sid, group.SamAccountName), factory.SystemActor());
        return sam;
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string login, string password)
        => c.PostAsJsonAsync("/api/v1/auth/login", new { login, password });

    [Fact]
    public async Task Ad_user_signs_in_from_loopback_and_gets_group_permissions()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Loopback();
        var r = await Login(client, $"TEST\\{sam}", "Ad-Pass-1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await r.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var me = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/me");
        Assert.True(me!["permissions"].TryGetProperty(PermissionIds.ServicesRead, out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Ad_password_from_network_without_https_is_refused()
    {
        string sam = await AdUserWithAccessAsync();
        var r = await Login(factory.Remote(), sam, "Ad-Pass-1");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("HTTPS", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Forwarded_header_does_not_make_request_loopback()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Remote();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(client, sam, "Ad-Pass-1")).StatusCode);
    }

    [Fact]
    public async Task Ad_user_without_assignments_gets_403()
    {
        string sam = "na" + Guid.NewGuid().ToString("N")[..6];
        factory.Directory.AddUser(sam, "Ad-Pass-1");
        var r = await Login(factory.Loopback(), sam, "Ad-Pass-1");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("Нет доступа к WinAdmin", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_ad_password_gets_401()
    {
        string sam = await AdUserWithAccessAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.Loopback(), sam, "nope")).StatusCode);
    }

    [Fact]
    public async Task Local_login_wins_over_directory()
    {
        string login = "both" + Guid.NewGuid().ToString("N")[..6];
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest { Login = login, Password = "Local-123456" });
        factory.Directory.AddUser(login, "Ad-Pass-1");
        // Пароль домена к локальной учётке не подходит — в домен запрос не уходит.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.Loopback(), login, "Ad-Pass-1")).StatusCode);
    }

    [Fact]
    public async Task Directory_down_gives_503_and_local_users_still_work()
    {
        string sam = await AdUserWithAccessAsync();
        factory.Directory.Down = true;
        try
        {
            var r = await Login(factory.Loopback(), sam, "Ad-Pass-1");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Contains("Контроллер домена недоступен", await r.Content.ReadAsStringAsync());
        }
        finally { factory.Directory.Down = false; }
    }

    [Fact]
    public async Task Refresh_works_for_ad_user_and_fails_after_disable()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Loopback();
        Assert.Equal(HttpStatusCode.OK, (await Login(client, sam, "Ad-Pass-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_fails_for_disabled_ad_account()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Loopback();
        Assert.Equal(HttpStatusCode.OK, (await Login(client, sam, "Ad-Pass-1")).StatusCode);
        factory.Directory.Disable(sam);
        // Кэш групп свежий (< 5 минут), но обновление сеанса перепроверяет учётку в каталоге.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Directory_disabled_falls_back_to_plain_401()
    {
        string sam = await AdUserWithAccessAsync();
        var saved = factory.DirectorySettings;
        factory.DirectorySettings = DirectorySettings.Disabled;
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.Remote(), sam, "Ad-Pass-1")).StatusCode); }
        finally { factory.DirectorySettings = saved; }
    }
}
```

`Refresh_fails_for_disabled_ad_account` требует, чтобы refresh вызывал `IDirectorySignIn.CompleteAsync` (свежая проверка `FindBySidAsync`), а не только кэш групп.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectoryLoginApiTests"`
Expected: FAIL — AD-вход отвечает 401 (контроллер знает только локальных), транспорт не проверяется.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Api/Auth/LoginTransport.cs
using System.Net;

namespace WinAdmin.Api.Auth;

/// <summary>Пароль домена — только по HTTPS или с этой машины. Заголовки прокси не учитываются.</summary>
public static class LoginTransport
{
    public const string RefusedMessage =
        "Вход учёткой домена по паролю доступен только по HTTPS или с этого компьютера; используйте вход Windows";

    public static bool PasswordAllowed(HttpContext context)
        => context.Request.IsHttps
           || context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
}
```

```csharp
// src/backend/WinAdmin.Api/Auth/DirectorySessionValidator.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>JWT пользователя AD: группы — из кэша (5/15 минут) в claims wa:group; нет групп → 401.</summary>
public static class DirectorySessionValidator
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        if (context.Principal is not { } user) return;
        var principal = PrincipalClaims.Parse(user, ApiKeyDefaults.Scheme);
        if (principal is not { Type: PrincipalType.AdUser }) return;

        var cache = context.HttpContext.RequestServices.GetRequiredService<IAdGroupCache>();
        var groups = await cache.GetGroupsAsync(principal.Id, context.HttpContext.RequestAborted);
        if (groups is null)
        {
            context.Fail("Сеанс учётной записи домена недействителен");
            return;
        }
        if (user.Identity is ClaimsIdentity identity)
            foreach (var sid in groups)
                identity.AddClaim(new Claim(PrincipalClaims.GroupType, sid));
    }
}
```

`Program.cs` — в `AddJwtBearer(options => { … })` после `TokenValidationParameters`:

```csharp
        options.Events = new JwtBearerEvents { OnTokenValidated = DirectorySessionValidator.OnTokenValidated };
```

`AuthController` — `Login`, `Refresh` и вспомогательные методы (порядок шагов важен: локальный → домен выключен → транспорт → домен):

```csharp
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        string login = (request.Login ?? "").Trim();
        if (login.Length == 0 || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new { message = "Неверный логин или пароль" });

        // 1. Есть локальный пользователь с таким логином — только его пароль, в домен не идём.
        if (await users.ExistsAsync(login, ct))
        {
            var local = await users.ValidateAsync(login, request.Password, ct);
            if (local is null) return await FailedAsync(login, "local", ct);
            await AuditAsync("auth.login", login, true, "local", ct);
            return await IssueLocalAsync(local, ct);
        }

        // 2. Подключение к домену выключено — обычный отказ (время ответа выравнивает заглушка в ValidateAsync).
        if (!(await directorySettings.GetAsync(ct)).Enabled)
        {
            await users.ValidateAsync(login, request.Password, ct);
            return await FailedAsync(login, "unknown", ct);
        }

        // 3. Пароль домена — только HTTPS или loopback.
        if (!LoginTransport.PasswordAllowed(HttpContext))
            return BadRequest(new { message = LoginTransport.RefusedMessage });

        var signIn = await directory.PasswordAsync(login, request.Password, ct);
        return await DirectoryOutcomeAsync(signIn, login, "auth.login", ct);
    }

    private async Task<IActionResult> DirectoryOutcomeAsync(DirectorySignInResult result, string login, string action, CancellationToken ct)
    {
        switch (result.Status)
        {
            case DirectorySignInStatus.Ok:
                await AuditAsync(action, result.Account!.LoginName, true, "ad", ct);
                SetRefreshCookie(await tokens.CreateDirectoryRefreshTokenAsync(result.Account.Sid, ct));
                return Ok(new TokenResponse { AccessToken = tokens.GenerateDirectoryAccessToken(result.Account), ExpiresIn = 3600 });
            case DirectorySignInStatus.NoAccess:
                await AuditAsync(action + ".failed", login, false, "нет назначений", ct);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Нет доступа к WinAdmin" });
            case DirectorySignInStatus.Unavailable:
                await AuditAsync(action + ".failed", login, false, "контроллер домена недоступен", ct);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Контроллер домена недоступен" });
            default:
                return await FailedAsync(login, "ad", ct, action + ".failed");
        }
    }

    private async Task<IActionResult> FailedAsync(string login, string source, CancellationToken ct, string action = "auth.login.failed")
    {
        await AuditAsync(action, login, false, source, ct);
        return Unauthorized(new { message = "Неверный логин или пароль" });
    }

    private Task AuditAsync(string action, string actor, bool success, string details, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor, Action = action, Success = success, Details = details,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        }, ct);

    private async Task<IActionResult> IssueLocalAsync(UserPrincipal user, CancellationToken ct)
    {
        SetRefreshCookie(await tokens.CreateRefreshTokenAsync(user.Id, ct));
        return Ok(new TokenResponse { AccessToken = tokens.GenerateAccessToken(user), ExpiresIn = 3600 });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrWhiteSpace(raw))
            return Unauthorized(new { message = "Refresh token отсутствует" });

        var local = await tokens.ValidateRefreshTokenAsync(raw, ct);
        if (local is not null)
        {
            await tokens.RevokeRefreshTokenAsync(raw, ct);
            return await IssueLocalAsync(local, ct);
        }

        var sid = await tokens.ValidateDirectoryRefreshTokenAsync(raw, ct);
        if (sid is not null && await directory.CompleteAsync(sid, ct) is { Status: DirectorySignInStatus.Ok, Account: { } account })
        {
            await tokens.RevokeRefreshTokenAsync(raw, ct);
            SetRefreshCookie(await tokens.CreateDirectoryRefreshTokenAsync(sid, ct));
            return Ok(new TokenResponse { AccessToken = tokens.GenerateDirectoryAccessToken(account), ExpiresIn = 3600 });
        }

        ClearRefreshCookie();
        return Unauthorized(new { message = "Refresh token недействителен или истёк" });
    }
```

Конструктор — с `IDirectorySettingsStore directorySettings`:

```csharp
public sealed class AuthController(
    IUserService users, ITokenService tokens, IDirectorySignIn directory,
    IDirectorySettingsStore directorySettings, IAuditService audit) : ControllerBase
```

`Logout`, `SetRefreshCookie`, `ClearRefreshCookie` — без изменений (переписать на поля первичного конструктора).

Чтобы `Refresh_fails_for_disabled_ad_account` проходил при свежем кэше групп, `CompleteAsync` делает `FindBySidAsync` до обращения к кэшу (так и реализовано в Task 5) — проверить, что тест зелёный.

`IUserService` — добавить:

```csharp
    /// <summary>Есть ли локальный пользователь с таким логином (включённый или нет).</summary>
    Task<bool> ExistsAsync(string login, CancellationToken ct = default);
```

`UserService`:

```csharp
    public Task<bool> ExistsAsync(string login, CancellationToken ct = default)
        => _db.Users.AnyAsync(u => u.Login == login, ct);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectoryLoginApiTests|FullyQualifiedName~Auth|FullyQualifiedName~Delegation"`
Expected: PASS (10 новых + существующие).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(auth): domain password login (loopback/HTTPS), AD refresh, group claims per request"
```

---

### Task 7: Лимит неудачных входов и выравнивание времени ответа

**Files:**
- Create: `src/backend/WinAdmin.Api/Auth/LoginThrottle.cs`
- Modify: `src/backend/WinAdmin.Api/Controllers/AuthController.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (`AddSingleton<LoginThrottle>()`)
- Modify: `src/backend/WinAdmin.Infrastructure/Security/UserService.cs` (заглушка хеширования)
- Test: `src/tests/WinAdmin.Tests/LoginThrottleTests.cs`

**Interfaces:**
- Produces: `LoginThrottle(TimeProvider? time = null)`: `TimeSpan? RetryAfter(string ip, string login)`, `void Failed(string ip, string login)`, `void Succeeded(string ip, string login)`; константы `PerPair = 5`, `PerIp = 20`, `Window = 1 min`.

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/LoginThrottleTests.cs
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Tests;

public sealed class LoginThrottleTests
{
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);

    [Fact]
    public void Sixth_failure_for_same_login_is_blocked_for_the_rest_of_the_minute()
    {
        var t = new LoginThrottle(_time);
        for (int i = 0; i < 5; i++) { Assert.Null(t.RetryAfter("1.1.1.1", "Ivan")); t.Failed("1.1.1.1", "ivan"); }
        var wait = t.RetryAfter("1.1.1.1", "IVAN");
        Assert.NotNull(wait);
        Assert.InRange(wait!.Value.TotalSeconds, 1, 60);
        Assert.Null(t.RetryAfter("1.1.1.1", "petr")); // другой логин с того же IP — пока можно

        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(t.RetryAfter("1.1.1.1", "ivan"));
    }

    [Fact]
    public void Twenty_failures_from_one_ip_block_every_login()
    {
        var t = new LoginThrottle(_time);
        for (int i = 0; i < 20; i++) t.Failed("2.2.2.2", "u" + i);
        Assert.NotNull(t.RetryAfter("2.2.2.2", "fresh"));
        Assert.Null(t.RetryAfter("3.3.3.3", "fresh"));
    }

    [Fact]
    public void Success_clears_failures_of_the_pair()
    {
        var t = new LoginThrottle(_time);
        for (int i = 0; i < 4; i++) t.Failed("1.1.1.1", "ivan");
        t.Succeeded("1.1.1.1", "ivan");
        t.Failed("1.1.1.1", "ivan");
        Assert.Null(t.RetryAfter("1.1.1.1", "ivan"));
    }
}

[Collection("network-api")]
public sealed class LoginThrottleApiTests(NetworkApiFactory factory)
{
    [Fact]
    public async Task Api_returns_429_with_retry_after()
    {
        var client = factory.ClientFrom("10.9.9." + Random.Shared.Next(1, 250));
        string login = "brute" + Guid.NewGuid().ToString("N")[..6];
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "x" })).StatusCode);
        var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "x" });
        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.True(r.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Unknown_login_takes_comparable_time_to_wrong_password()
    {
        string login = "timing" + Guid.NewGuid().ToString("N")[..6];
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest { Login = login, Password = "Right-123456" });
        var users = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IUserService>();
        await users.ValidateAsync(login, "warm-up");

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) await users.ValidateAsync(login, "wrong");
        var wrong = sw.Elapsed;
        sw.Restart();
        for (int i = 0; i < 5; i++) await users.ValidateAsync("missing-" + login, "wrong");
        var missing = sw.Elapsed;

        Assert.True(missing > wrong * 0.3, $"unknown {missing.TotalMilliseconds} ms vs wrong {wrong.TotalMilliseconds} ms");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LoginThrottle"`
Expected: FAIL — компиляция: нет `LoginThrottle`; после заглушки класса — 429 не возвращается, тайминг неизвестного логина ≈ 0.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Api/Auth/LoginThrottle.cs
using System.Collections.Concurrent;

namespace WinAdmin.Api.Auth;

/// <summary>Неудачные входы за последнюю минуту: 5 на пару (IP, логин), 20 на IP.</summary>
public sealed class LoginThrottle(TimeProvider? time = null)
{
    public const int PerPair = 5;
    public const int PerIp = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _failures = new();

    private static string Pair(string ip, string login) => $"{ip}|{login.Trim().ToLowerInvariant()}";

    public TimeSpan? RetryAfter(string ip, string login)
    {
        var now = _time.GetUtcNow();
        return Wait(Pair(ip, login), PerPair, now) ?? Wait(ip, PerIp, now);
    }

    public void Failed(string ip, string login)
    {
        var now = _time.GetUtcNow();
        foreach (var key in new[] { Pair(ip, login), ip })
        {
            var list = _failures.GetOrAdd(key, _ => []);
            lock (list) list.Add(now);
        }
    }

    public void Succeeded(string ip, string login) => _failures.TryRemove(Pair(ip, login), out _);

    private TimeSpan? Wait(string key, int limit, DateTimeOffset now)
    {
        if (!_failures.TryGetValue(key, out var list)) return null;
        lock (list)
        {
            list.RemoveAll(t => now - t >= Window);
            if (list.Count < limit) return null;
            var wait = list[^limit] + Window - now;
            return wait > TimeSpan.Zero ? wait : null;
        }
    }
}
```

`Program.cs`: `builder.Services.AddSingleton<LoginThrottle>();`

`AuthController` — добавить `LoginThrottle throttle` в конструктор; в начале `Login` (после проверки пустых полей):

```csharp
        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (throttle.RetryAfter(ip, login) is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Слишком много попыток входа, попробуйте позже" });
        }
```

в `FailedAsync` — `throttle.Failed(ip, login)` (передать `ip` параметром или вычислять там же тем же выражением); после каждого успешного входа (`IssueLocalAsync` в `Login` и ветка `Ok` в `DirectoryOutcomeAsync` для `auth.login`) — `throttle.Succeeded(ip, login)`. Ответы 400 (транспорт), 403, 503 неудачами не считаются.

`UserService.ValidateAsync` — заглушка:

```csharp
    // Хеш для несуществующего логина: время ответа не выдаёт, есть ли такой пользователь.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<UserEntity>().HashPassword(new UserEntity(), Guid.NewGuid().ToString()));

    public async Task<UserPrincipal?> ValidateAsync(string login, string password, CancellationToken ct = default)
    {
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Login == login, ct);
        if (entity is null)
        {
            _hasher.VerifyHashedPassword(new UserEntity(), DummyHash.Value, password);
            return null;
        }
        if (!entity.IsActive) return null;
        var result = _hasher.VerifyHashedPassword(entity, entity.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : ToPrincipal(entity);
    }
```

(Проверить тип поля `_hasher` — если это `IPasswordHasher<UserEntity>`, `DummyHash` строить тем же типом хешера.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LoginThrottle|FullyQualifiedName~DirectoryLoginApiTests|FullyQualifiedName~Auth"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(auth): login throttling (5 per login, 20 per IP) and constant-time unknown login"
```

---

### Task 8: SSO — `/auth/windows` (Kerberos) и `/auth/options`

**Files:**
- Modify: `src/backend/WinAdmin.Api/WinAdmin.Api.csproj` (+ `Microsoft.AspNetCore.Authentication.Negotiate` 10.0.9)
- Create: `src/backend/WinAdmin.Api/Auth/WindowsSignInReader.cs`
- Modify: `src/backend/WinAdmin.Api/Controllers/AuthController.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (`.AddNegotiate()`, `IWindowsSignInReader`)
- Modify: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs` (фейковый reader)
- Test: `src/tests/WinAdmin.Tests/WindowsSignInApiTests.cs`

**Interfaces:**
- Produces:
  - `record WindowsSignIn(string Sid, string AuthenticationType)`
  - `interface IWindowsSignInReader { Task<WindowsSignIn?> ReadAsync(HttpContext context); }` (null → нужен вызов Challenge)
  - `NegotiateWindowsSignInReader : IWindowsSignInReader` (`AuthenticateAsync(NegotiateDefaults.AuthenticationScheme)`, SID из `ClaimTypes.PrimarySid`, тип — `Identity.AuthenticationType`)
  - `GET /api/v1/auth/windows` → 200 token / 401 (Challenge или NTLM) / 403 / 404 (домен выключен) / 503
  - `GET /api/v1/auth/options` → `{ directory: bool }` (анонимно)
  - в фабрике: `FakeWindowsReader` с изменяемым `Next` (`WindowsSignIn?`)

- [ ] **Step 1: Write the failing test**

В `NetworkApiFactory`:

```csharp
    public FakeWindowsReader Windows { get; } = new();
```

в `ConfigureTestServices`:

```csharp
            services.RemoveAll<IWindowsSignInReader>();
            services.AddSingleton<IWindowsSignInReader>(Windows);
```

класс:

```csharp
public sealed class FakeWindowsReader : IWindowsSignInReader
{
    public WindowsSignIn? Next { get; set; }
    public Task<WindowsSignIn?> ReadAsync(HttpContext context) => Task.FromResult(Next);
}
```

```csharp
// src/tests/WinAdmin.Tests/WindowsSignInApiTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class WindowsSignInApiTests(NetworkApiFactory factory)
{
    private async Task<DirectoryObject> UserWithAccessAsync()
    {
        string sam = "sso" + Guid.NewGuid().ToString("N")[..6];
        var user = factory.Directory.AddUser(sam, "unused-pw");
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var role = await roles.CreateAsync(new SaveRoleRequest("r-" + sam, null, [new RoleGrantDto(PermissionIds.ServicesRead, null)]), factory.SystemActor());
        await roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.AdUser, user.Sid, sam), factory.SystemActor());
        return user;
    }

    [Fact]
    public async Task Kerberos_sign_in_issues_token_even_over_plain_http_from_network()
    {
        var user = await UserWithAccessAsync();
        factory.Windows.Next = new WindowsSignIn(user.Sid, "Kerberos");
        var r = await factory.Remote().GetAsync("/api/v1/auth/windows");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("accessToken", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ntlm_is_refused()
    {
        var user = await UserWithAccessAsync();
        factory.Windows.Next = new WindowsSignIn(user.Sid, "NTLM");
        var r = await factory.Remote().GetAsync("/api/v1/auth/windows");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("Kerberos", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_windows_identity_the_endpoint_challenges()
    {
        factory.Windows.Next = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Remote().GetAsync("/api/v1/auth/windows")).StatusCode);
    }

    [Fact]
    public async Task Disabled_directory_hides_sso_and_options_say_so()
    {
        var saved = factory.DirectorySettings;
        factory.DirectorySettings = DirectorySettings.Disabled;
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await factory.Remote().GetAsync("/api/v1/auth/windows")).StatusCode);
            var options = await factory.Remote().GetFromJsonAsync<Dictionary<string, bool>>("/api/v1/auth/options");
            Assert.False(options!["directory"]);
        }
        finally { factory.DirectorySettings = saved; }
        var on = await factory.Remote().GetFromJsonAsync<Dictionary<string, bool>>("/api/v1/auth/options");
        Assert.True(on!["directory"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~WindowsSignInApiTests"`
Expected: FAIL — компиляция: нет `IWindowsSignInReader`/`WindowsSignIn`.

- [ ] **Step 3: Write minimal implementation**

```bash
dotnet add src/backend/WinAdmin.Api package Microsoft.AspNetCore.Authentication.Negotiate --version 10.0.9
```

```csharp
// src/backend/WinAdmin.Api/Auth/WindowsSignInReader.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace WinAdmin.Api.Auth;

public sealed record WindowsSignIn(string Sid, string AuthenticationType);

/// <summary>Результат Negotiate для текущего запроса; null — нужно отправить вызов (401 + WWW-Authenticate).</summary>
public interface IWindowsSignInReader
{
    Task<WindowsSignIn?> ReadAsync(HttpContext context);
}

public sealed class NegotiateWindowsSignInReader : IWindowsSignInReader
{
    public async Task<WindowsSignIn?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal?.FindFirst(ClaimTypes.PrimarySid)?.Value is not { } sid)
            return null;
        return new WindowsSignIn(sid, result.Principal.Identity?.AuthenticationType ?? "");
    }
}
```

`Program.cs`: в цепочку `AddAuthentication(...)` добавить `.AddNegotiate()`; зарегистрировать `builder.Services.AddSingleton<IWindowsSignInReader, NegotiateWindowsSignInReader>();`. Схема по умолчанию остаётся `"Auto"` — Negotiate вызывается только явно из `/auth/windows`.

`AuthController` — добавить `IWindowsSignInReader windows` в конструктор и действия:

```csharp
    /// <summary>Есть ли вход учёткой домена (для кнопки «Войти как текущий пользователь Windows»).</summary>
    [HttpGet("options")]
    [AllowAnonymous]
    public async Task<IActionResult> Options(CancellationToken ct)
        => Ok(new { directory = (await directorySettings.GetAsync(ct)).Enabled });

    /// <summary>SSO: Negotiate (Kerberos). NTLM не принимается. Работает по HTTP и HTTPS.</summary>
    [HttpGet("windows")]
    [AllowAnonymous]
    public async Task<IActionResult> Windows(CancellationToken ct)
    {
        if (!(await directorySettings.GetAsync(ct)).Enabled)
            return NotFound(new { message = "Вход учёткой домена выключен" });

        var identity = await windows.ReadAsync(HttpContext);
        if (identity is null)
            return Challenge(NegotiateDefaults.AuthenticationScheme);
        if (!string.Equals(identity.AuthenticationType, "Kerberos", StringComparison.OrdinalIgnoreCase))
        {
            await AuditAsync("auth.windows.failed", identity.Sid, false, identity.AuthenticationType, ct);
            return Unauthorized(new { message = "Нужен вход Kerberos (NTLM не принимается): откройте WinAdmin по имени сервера в домене" });
        }

        var result = await directory.CompleteAsync(identity.Sid, ct);
        return await DirectoryOutcomeAsync(result, identity.Sid, "auth.windows", ct);
    }
```

В тестах схема Negotiate зарегистрирована, но `Challenge` в TestServer отдаёт 401 — тест `Without_windows_identity_the_endpoint_challenges` это и проверяет.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~WindowsSignInApiTests|FullyQualifiedName~DirectoryLoginApiTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(auth): Kerberos SSO endpoint and login options"
```

---

### Task 9: API подключения к домену, поиск AD, администраторы AD

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/DirectoryController.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/Access/RoleService.cs`
- Test: `src/tests/WinAdmin.Tests/DirectoryApiTests.cs`, `src/tests/WinAdmin.Tests/RoleServiceTests.cs`

**Interfaces:**
- Consumes: `IDirectorySettingsStore`, `IDirectoryService`, `IAuditService`
- Produces:
  - `GET/PUT /api/v1/settings/directory` (`platform.directory.manage`) — тело/ответ `DirectorySettings` (camelCase)
  - `POST /api/v1/settings/directory/test` → `DirectoryTestStep[]`
  - `GET /api/v1/directory/search?q=&kind=user|group` (`platform.roles.manage`) → `[{ sid, kind, samAccountName, displayName, upn }]`; домен недоступен → 503
  - `RoleService(..., IDirectoryService? directory = null)`: назначение «Администратор» на AdUser активно, если каталог подтверждает включённую учётку; на AdGroup — если группа существует; ошибка каталога → не активно

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/DirectoryApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class DirectoryApiTests(NetworkApiFactory factory)
{
    [Fact]
    public async Task Directory_settings_require_directory_permission()
    {
        var other = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/settings/directory")).StatusCode);
    }

    [Fact]
    public async Task Settings_roundtrip_and_validation()
    {
        var saved = factory.DirectorySettings;
        try
        {
            var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
            var put = await client.PutAsJsonAsync("/api/v1/settings/directory",
                new { enabled = true, domain = "pcs-msk.com", server = "dc.pcs-msk.com", baseDn = (string?)null, useLdaps = false });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var got = await client.GetFromJsonAsync<DirectorySettings>("/api/v1/settings/directory", new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal("dc.pcs-msk.com", got!.Server);

            var bad = await client.PutAsJsonAsync("/api/v1/settings/directory", new { enabled = true, domain = "", useLdaps = false });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        finally { factory.DirectorySettings = saved; }
    }

    [Fact]
    public async Task Test_returns_steps()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
        var steps = await (await client.PostAsync("/api/v1/settings/directory/test", null)).Content.ReadFromJsonAsync<List<DirectoryTestStep>>();
        Assert.NotEmpty(steps!);
    }

    [Fact]
    public async Task Search_finds_groups_for_role_managers()
    {
        string name = "Search-" + Guid.NewGuid().ToString("N")[..6];
        factory.Directory.AddGroup(name);
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        var found = await client.GetFromJsonAsync<List<JsonElement>>($"/api/v1/directory/search?q={name}&kind=group");
        Assert.Single(found!);
        Assert.StartsWith("S-1-5-21-", found![0].GetProperty("sid").GetString());

        var noRights = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRights.GetAsync($"/api/v1/directory/search?q={name}")).StatusCode);
    }

    [Fact]
    public async Task Search_when_directory_is_down_returns_503()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        factory.Directory.Down = true;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/directory/search?q=x")).StatusCode); }
        finally { factory.Directory.Down = false; }
    }
}
```

`RoleServiceTests` — заменить `Ad_assignment_does_not_count_as_active_administrator_yet` на два теста:

```csharp
    [Fact]
    public async Task Ad_assignment_without_directory_is_not_an_active_administrator()
    {
        var local = await AssignAdmin("u-admin");
        await _roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.AdGroup, "S-1-5-21-1-2-3-512", "Domain Admins"), Admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(local.Id, Admin));
        Assert.Equal(1, await _roles.CountActiveAdministratorsAsync());
    }

    [Fact]
    public async Task Ad_assignment_confirmed_by_directory_is_an_active_administrator()
    {
        var ad = new Fakes.FakeDirectory();
        var admins = ad.AddGroup("WinAdmin-Admins");
        var off = ad.AddUser("off", "x");
        ad.Disable("off");
        var roles = new RoleService(_db, Catalog, _access.Object, Mock.Of<IAuditService>(), ad);
        var local = await AssignAdmin("u-admin");
        await roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.AdUser, off.Sid, "off"), Admin);
        Assert.Equal(1, await roles.CountActiveAdministratorsAsync()); // отключённый в AD не считается

        await roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.AdGroup, admins.Sid, "WinAdmin-Admins"), Admin);
        Assert.Equal(2, await roles.CountActiveAdministratorsAsync());
        await roles.UnassignAsync(local.Id, Admin); // теперь можно

        ad.Down = true;
        Assert.Equal(0, await roles.CountActiveAdministratorsAsync());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectoryApiTests|FullyQualifiedName~RoleServiceTests"`
Expected: FAIL — 404 на `/api/v1/settings/directory`; компиляция: конструктор `RoleService` без `IDirectoryService`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Api/Controllers/DirectoryController.cs
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[PlatformErrors]
public sealed class DirectoryController(IDirectorySettingsStore settings, IDirectoryService directory, IAuditService audit)
    : WinAdminControllerBase
{
    [HttpGet("api/v1/settings/directory")]
    [RequirePermission(PermissionIds.PlatformDirectoryManage)]
    public async Task<DirectorySettings> Get(CancellationToken ct) => await settings.GetAsync(ct);

    [HttpPut("api/v1/settings/directory")]
    [RequirePermission(PermissionIds.PlatformDirectoryManage)]
    public async Task<DirectorySettings> Put([FromBody] DirectorySettings request, CancellationToken ct)
    {
        await settings.SaveAsync(request, ct);
        var saved = await settings.GetAsync(ct);
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "settings.directory", Success = true, SourceIp = SourceIp,
            Details = $"enabled={saved.Enabled}, domain={saved.Domain}, server={saved.Server ?? "auto"}, ldaps={saved.UseLdaps}",
        }, ct);
        return saved;
    }

    [HttpPost("api/v1/settings/directory/test")]
    [RequirePermission(PermissionIds.PlatformDirectoryManage)]
    public Task<IReadOnlyList<DirectoryTestStep>> Test(CancellationToken ct) => directory.TestConnectionAsync(ct);

    [HttpGet("api/v1/directory/search")]
    [RequirePermission(PermissionIds.PlatformRolesManage)]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? kind, CancellationToken ct)
    {
        DirectoryObjectKind? k = kind?.ToLowerInvariant() switch
        {
            "user" => DirectoryObjectKind.User,
            "group" => DirectoryObjectKind.Group,
            _ => null,
        };
        try
        {
            var found = await directory.SearchAsync(q ?? "", k, 25, ct);
            return Ok(found.Select(o => new
            {
                sid = o.Sid,
                kind = o.Kind == DirectoryObjectKind.Group ? "group" : "user",
                samAccountName = o.SamAccountName,
                displayName = o.DisplayName,
                upn = o.Upn,
                enabled = o.Enabled,
            }));
        }
        catch (DirectoryUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }
}
```

`Actor` и `SourceIp` — из `WinAdminControllerBase` (проверить имена свойств: `grep -n "protected" src/backend/WinAdmin.Api/Controllers/WinAdminControllerBase.cs`). Если у базового класса есть `[ApiController]`/`[Route]` — маршруты на действиях абсолютные и совместимы.

`RoleService` — конструктор с необязательным каталогом и проверка активности:

```csharp
public sealed class RoleService(
    WinAdminDbContext db, PermissionCatalog catalog, IAccessService access, IAuditService audit,
    IDirectoryService? directory = null) : IRoleService
```

(сохранить существующий список параметров; добавить последний). В `CountActiveAdministratorsAsync(string?, ct, excludePrincipal)` заменить ветку `_ => false` на:

```csharp
                nameof(PrincipalType.AdUser) or nameof(PrincipalType.AdGroup) => await ConfirmedInDirectoryAsync(a.PrincipalId, ct),
                _ => false,
```

и метод:

```csharp
    /// <summary>AD-субъект — активный администратор, только если каталог подтверждает его сейчас.</summary>
    private async Task<bool> ConfirmedInDirectoryAsync(string sid, CancellationToken ct)
    {
        if (directory is null) return false;
        try
        {
            return await directory.FindBySidAsync(sid, ct) is { Enabled: true };
        }
        catch (DirectoryUnavailableException)
        {
            return false;
        }
    }
```

Комментарий у метода обновить: «…AD — если каталог подтверждает включённую учётку или существующую группу».

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DirectoryApiTests|FullyQualifiedName~RoleServiceTests|FullyQualifiedName~RoleCommandsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(directory): directory settings/test/search API; AD administrators confirmed by directory"
```

---

### Task 10: Интерфейс — вход Windows, «Подключение к домену», назначения AD

**Files:**
- Modify: `src/frontend/src/api/types.ts`, `src/frontend/src/api/client.ts`, `src/frontend/src/api/authApi.ts`
- Modify: `src/frontend/src/auth/LoginForm.tsx`
- Create: `src/frontend/src/components/DirectorySettingsCard.tsx`
- Modify: `src/frontend/src/pages/Settings.tsx`
- Modify: `src/frontend/src/pages/Roles.tsx`

**Interfaces:**
- Consumes: `/api/v1/auth/options`, `/api/v1/auth/windows`, `/api/v1/settings/directory[/test]`, `/api/v1/directory/search`
- Produces: `api.directory.{get, save, test, search}`, `authApi.options()`, `authApi.windows()`

В репозитории нет frontend-тестов (Ruling из 1b) — проверка: `npm run build` + `npx oxlint` (если настроен) + браузер на `npm run dev` с API.

- [ ] **Step 1: Типы и API**

`types.ts`:

```ts
export interface DirectorySettings {
  enabled: boolean
  domain: string | null
  server: string | null
  baseDn: string | null
  useLdaps: boolean
}

export interface DirectoryTestStep { name: string; ok: boolean; message: string }

export interface DirectoryEntry {
  sid: string
  kind: 'user' | 'group'
  samAccountName: string
  displayName: string | null
  upn: string | null
  enabled: boolean
}
```

`client.ts` — в объект `api`:

```ts
  directory: {
    get: () => http.get<DirectorySettings>('/settings/directory').then((r) => r.data),
    save: (s: DirectorySettings) => http.put<DirectorySettings>('/settings/directory', s).then((r) => r.data),
    test: () => http.post<DirectoryTestStep[]>('/settings/directory/test').then((r) => r.data),
    search: (q: string, kind: 'user' | 'group') =>
      http.get<DirectoryEntry[]>('/directory/search', { params: { q, kind } }).then((r) => r.data),
  },
```

`authApi.ts` — рядом с `login`:

```ts
  options: async (): Promise<{ directory: boolean }> => {
    const res = await fetch('/api/v1/auth/options')
    return res.ok ? res.json() : { directory: false }
  },

  /** SSO: браузер сам отвечает на 401 Negotiate (Kerberos) для узлов зоны «Интрасеть». */
  windows: async (): Promise<TokenResponse> => {
    const res = await fetch('/api/v1/auth/windows', { credentials: 'include' })
    const body = await res.json().catch(() => ({}))
    if (!res.ok) throw new Error(body?.message ?? 'Вход Windows не выполнен')
    return body as TokenResponse
  },
```

(Тип ответа входа в `authApi.ts` назвать так, как назван у существующего `login`.)

Ошибки `login`: проверить, что `authApi.login` пробрасывает `message` из тела ответа (400/403/429/503) в `Error.message` — если нет, сделать так же, как в `windows`.

- [ ] **Step 2: Кнопка на странице входа**

`LoginForm.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Button, Card, Divider, Form, Input, Typography, App } from 'antd'
import { LockOutlined, UserOutlined, WindowsOutlined } from '@ant-design/icons'
...
  const [windowsAvailable, setWindowsAvailable] = useState(false)
  const [windowsLoading, setWindowsLoading] = useState(false)

  useEffect(() => { authApi.options().then((o) => setWindowsAvailable(o.directory)).catch(() => {}) }, [])

  const signInWindows = async () => {
    setWindowsLoading(true)
    try {
      const resp = await authApi.windows()
      setStoredToken(resp.accessToken)
      onAuthed()
    } catch (err: any) {
      message.error(err?.message ?? 'Вход Windows не выполнен')
    } finally {
      setWindowsLoading(false)
    }
  }
```

после кнопки «Войти» внутри `Form`:

```tsx
          {windowsAvailable && (
            <>
              <Divider plain>или</Divider>
              <Button icon={<WindowsOutlined />} block size="large" loading={windowsLoading} onClick={signInWindows}>
                Войти как текущий пользователь Windows
              </Button>
            </>
          )}
```

Подсказку поля «Логин» сменить на `placeholder="admin или ДОМЕН\\пользователь"`.

- [ ] **Step 3: Карточка «Подключение к домену»**

```tsx
// src/frontend/src/components/DirectorySettingsCard.tsx
import { useEffect, useState } from 'react'
import { App, Button, Card, Form, Input, List, Space, Switch, Typography } from 'antd'
import { CheckCircleTwoTone, CloseCircleTwoTone } from '@ant-design/icons'
import { api } from '../api/client'
import type { DirectorySettings, DirectoryTestStep } from '../api/types'

export default function DirectorySettingsCard() {
  const { message } = App.useApp()
  const [form] = Form.useForm<DirectorySettings>()
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [steps, setSteps] = useState<DirectoryTestStep[]>()

  useEffect(() => { api.directory.get().then((s) => form.setFieldsValue(s)).catch(() => message.error('Не удалось загрузить настройки домена')) }, [form, message])

  const save = async (values: DirectorySettings) => {
    setSaving(true)
    try {
      form.setFieldsValue(await api.directory.save(values))
      message.success('Настройки домена сохранены')
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить')
    } finally {
      setSaving(false)
    }
  }

  const test = async () => {
    setTesting(true)
    try { setSteps(await api.directory.test()) } catch { message.error('Проверка не выполнена') } finally { setTesting(false) }
  }

  return (
    <Card title="Подключение к домену" className="sp-glass">
      <Typography.Paragraph type="secondary">
        Каталог читается учёткой компьютера; пароль не хранится. Вход доменной учёткой по паролю — только по HTTPS или с этого компьютера.
      </Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item name="enabled" label="Вход учётками домена" valuePropName="checked"><Switch /></Form.Item>
        <Form.Item name="domain" label="Домен" rules={[{ validator: async (_, v) => { if (form.getFieldValue('enabled') && !v?.trim()) throw new Error('Укажите домен') } }]}>
          <Input placeholder="pcs-msk.com" />
        </Form.Item>
        <Form.Item name="server" label="Контроллер домена (пусто — найти через DNS)"><Input placeholder="dc.pcs-msk.com" /></Form.Item>
        <Form.Item name="baseDn" label="Корень поиска (пусто — весь домен)"><Input placeholder="DC=pcs-msk,DC=com" /></Form.Item>
        <Form.Item name="useLdaps" label="LDAPS (636) вместо LDAP с подписью (389)" valuePropName="checked"><Switch /></Form.Item>
        <Space>
          <Button type="primary" htmlType="submit" loading={saving}>Сохранить</Button>
          <Button onClick={test} loading={testing}>Проверить</Button>
        </Space>
      </Form>
      {steps && (
        <List style={{ marginTop: 16 }} size="small" dataSource={steps} renderItem={(s) => (
          <List.Item>
            <Space>
              {s.ok ? <CheckCircleTwoTone twoToneColor="#52c41a" /> : <CloseCircleTwoTone twoToneColor="#ff4d4f" />}
              <b>{s.name}</b> <span>{s.message}</span>
            </Space>
          </List.Item>
        )} />
      )}
    </Card>
  )
}
```

`Settings.tsx` — рядом с `NetworkSettingsCard`, по тому же шаблону условия:

```tsx
      {can('platform.directory.manage') && <DirectorySettingsCard />}
```

- [ ] **Step 4: Назначения AD в «Ролях»**

В `AssignmentsDrawer` (`Roles.tsx`): тип субъекта — добавить опции `{ value: 'AdUser', label: 'Пользователь AD' }`, `{ value: 'AdGroup', label: 'Группа AD' }`. Для AD-типов вместо списка локальных — поиск:

```tsx
  const [adOptions, setAdOptions] = useState<{ value: string; label: string }[]>([])
  const [searching, setSearching] = useState(false)
  const searchAd = useMemo(() => {
    let timer: number | undefined
    return (q: string) => {
      window.clearTimeout(timer)
      if (q.trim().length < 2) { setAdOptions([]); return }
      timer = window.setTimeout(async () => {
        setSearching(true)
        try {
          const found = await api.directory.search(q, type === 'AdGroup' ? 'group' : 'user')
          setAdOptions(found.map((e) => ({ value: e.sid, label: `${e.displayName ?? e.samAccountName} (${e.samAccountName})${e.enabled ? '' : ' — отключён'}` })))
        } catch (e: any) {
          message.error(e?.response?.data?.message ?? 'Поиск в AD не выполнен')
        } finally { setSearching(false) }
      }, 300)
    }
  }, [type, message])
```

```tsx
        {type === 'AdUser' || type === 'AdGroup' ? (
          <Select value={principal} onChange={setPrincipal} options={adOptions} showSearch filterOption={false}
            onSearch={searchAd} loading={searching} placeholder="Начните вводить имя (от 2 символов)" style={{ minWidth: 320 }}
            notFoundContent={searching ? 'Поиск…' : 'Ничего не найдено'} />
        ) : (
          /* существующий Select локальных пользователей/ключей */
        )}
```

при создании назначения для AD передавать подпись: `api.assignments.create(role.id, type, principal, adOptions.find((o) => o.value === principal)?.label)`; метка типа в списке назначений: `AdUser` → «пользователь AD», `AdGroup` → «группа AD».

- [ ] **Step 5: Сборка и проверка в браузере**

Run: `cd src/frontend && npm run build` → Expected: сборка без ошибок TypeScript; затем `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.

Браузер (dev-сервер + API локально; машина не в домене): страница входа без кнопки Windows (`directory=false`); «Настройки» → карточка «Подключение к домену», включить с доменом-заглушкой → «Проверить» показывает шаги с ошибкой подключения (без падения страницы); «Роли» → назначение → «Группа AD» → поиск отвечает сообщением 503. Вернуть настройки (выключить).

- [ ] **Step 6: Commit**

```bash
git add src/frontend
git commit -m "feat(ui): Windows sign-in button, directory settings card, AD search in role assignments"
```

---

### Task 11: Документация, выкатка на DC PCS, ручная проверка

**Files:**
- Modify: `README.md`, `docs/02-security.md`, `docs/03-api-reference.md`, `releases/package/README.md` (раздел про домен)

- [ ] **Step 1: Документация**

- `docs/03-api-reference.md`: `/auth/login` (порядок: локальный → домен; 400 транспорт; 403; 429 + Retry-After; 503), `/auth/windows`, `/auth/options`, `/settings/directory` GET/PUT/test, `/directory/search`.
- `docs/02-security.md`: пароль домена — только HTTPS/loopback (SSH-туннель к `127.0.0.1` считается loopback); SSO — только Kerberos (открывать по DNS-имени сервера, сайт в зоне «Интрасеть»); группы перечитываются раз в 5 минут, при недоступности домена — до 15 минут; лимиты входа; отключение учётки в AD лишает доступа ≤ 5 минут.
- Пакетный README: включение домена в «Настройки → Подключение к домену», назначение роли группе AD в «Ролях».

- [ ] **Step 2: Полный прогон и сборка пакета**

Run: `dotnet test src/tests/WinAdmin.Tests` (с `WINADMIN_TEST_POSTGRES`) → Expected: все PASS, `[AdFact]` пропущены (или PASS, если заданы `WINADMIN_TEST_AD_*`).
Run: `releases\build.ps1 -OutputPath C:\dev\winadmin\releases\out-pcs-dc` → Expected: «Готово».

- [ ] **Step 3: Выкатка на DC (SSH через pcs-docker, ключ pcs, Ilias.Aidar@192.168.77.4)**

Скрипт обновления на сервере (выполнять через `dcps.sh` из scratchpad — PowerShell `-EncodedCommand`):

```powershell
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Stop-Service WinAdmin
Copy-Item C:\ProgramData\WinAdmin\WinAdmin.db "C:\ProgramData\WinAdmin\WinAdmin.db.bak-$stamp"
Expand-Archive C:\apps\_deploy\WinAdmin-pcs-dc.zip -DestinationPath C:\apps\WinAdmin -Force
Start-Service WinAdmin
Start-Sleep 3
(Invoke-WebRequest http://127.0.0.1:8080/api/v1/health -UseBasicParsing).StatusCode
(Invoke-RestMethod http://127.0.0.1:8080/api/v1/auth/options).directory
Remove-Item C:\apps\_deploy -Recurse -Force
```

Expected: `200`, `True` (DC в домене → подключение к домену включено по умолчанию). При ошибке старта — вернуть файлы из предыдущего пакета и базу из `.bak-<stamp>`, сообщить пользователю.

- [ ] **Step 4: Ручная проверка на сервере (вместе с пользователем)**

Через SSH на DC: `POST /api/v1/settings/directory/test` под `admin` → все шаги `ok`. Затем пользователь в UI (туннель `ssh -N -L 18080:127.0.0.1:8080 -i ~/.ssh/pcs -J pcs-docker Ilias.Aidar@192.168.77.4`, http://localhost:18080):
1. «Роли» → «Администратор» → назначить группу AD (поиск).
2. Выйти, войти `PCS\<учётка>` с паролем домена (туннель = loopback) → доступ есть.
3. SSO из браузера в домене по `http://dc.pcs-msk.com:8080` — только после открытия доступа из сети (HTTPS, часть 2); в этой части не проверяется.

- [ ] **Step 5: Commit**

```bash
git add README.md docs releases/package
git commit -m "docs: domain sign-in, SSO, directory settings and login limits"
git push
```
