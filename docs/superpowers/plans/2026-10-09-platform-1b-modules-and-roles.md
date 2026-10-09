# Platform 1b — Modules, Roles & Delegation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Разделы WinAdmin становятся модулями (включение/выключение, настройки, требования к машине), а доступ — ролями: набор прав `модуль.действие` с областью, назначаемый локальным пользователям и API-ключам, с делегированием без повышения прав и защитой от потери доступа.

**Architecture:** Контракты (`IWinAdminModule`, `PermissionDefinition`, `PermissionCatalog`, `PermissionEvaluator`, `DelegationGuard`) — в Core без зависимостей от Windows. Infrastructure хранит модули/роли/назначения в БД (SQLite и PostgreSQL), считает итоговые права с кэшем (`AccessService`), управляет ролями (`RoleService`) и модулями (`ModuleRegistry`). API проверяет права динамическими политиками `perm:<id>` и прячет выключенные модули (404) middleware'ом до авторизации. Существующие scopes один раз превращаются в роли при старте. UI строит меню из `/api/v1/me`.

**Tech Stack:** .NET 10, ASP.NET Core authorization, EF Core 10 (Sqlite + Npgsql), System.Management (WMI), React 19 + Ant Design 6, xUnit/Moq/WebApplicationFactory.

**Spec:** `docs/superpowers/specs/2026-10-09-module-platform-design.md` (§1, §2, §5 без directory/auth, §6, §7, §9 строка 1b, §10)

## Global Constraints

- Репозиторий `C:\dev\winadmin`, ветка `feat/module-platform`; план 1a выполнен (секреты, SQLite/PostgreSQL).
- Права — `модуль.действие`; права ядра — `platform.*`; id модуля — `^[a-z][a-z0-9-]*$`, не `platform`.
- Встроенная роль: id `administrator`, имя «Администратор», все права (вычисляются, не хранятся), без области, неизменяема, неудаляема.
- Субъекты назначений: `LocalUser`, `AdUser`, `AdGroup`, `ApiKey`; AD — по SID (`S-1-…`); в 1b UI назначает только локальных пользователей и API-ключи.
- Итоговые права: объединение ролей; право без области в любой роли → без ограничений; кэш 60 с, сброс при любом изменении ролей/назначений/модулей.
- Делегирование: роль/назначение можно создать, изменить или удалить, только если каждое её право есть у действующего лица, а область — подмножество его области (для встроенной роли — все права без области).
- Нельзя снять последнее назначение «Администратор» на активного субъекта; нельзя удалить/отключить пользователя или отозвать ключ, если это последний активный администратор (409).
- Выключенный или недоступный модуль: его API → 404 (до проверки прав).
- `disks.read` объединяется с `system.read`; старый scope `admin` → роль «Администратор».
- Пользовательские строки — по-русски.
- Тесты: `dotnet test src/tests/WinAdmin.Tests` (PostgreSQL-тесты — при `WINADMIN_TEST_POSTGRES`, см. `src/tests/start-test-postgres.ps1`).
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Обновление установки, где `admin`, `iadmin`, `iadmin1` имеют scope `admin`: после первого старта все трое — «Администратор», вход и все разделы работают; повторный старт ничего не дублирует (тест в Task 6).
2. JWT, выпущенный до обновления (без claim `wa:principal`), продолжает работать до истечения — права считаются по `NameIdentifier` (тест в Task 8).
3. Делегат с правом `platform.roles.manage` и частью прав пытается выдать себе или другому «Администратор» или право вне своих — 403 с перечнем, ничего не меняется (тест в Task 5).
4. Удаление/отключение единственного активного администратора, в том числе когда второй администратор есть, но отключён или его ключ истёк — 409 (тест в Task 5).
5. Выключение модуля, раздел которого открыт в UI: API отвечает 404, меню скрывает пункт, прямой URL показывает «Нет доступа», а не пустую страницу (тест в Task 8 для API, проверка в Task 13 для UI).

---

## File Structure

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/Modules/ModuleContracts.cs` | `IWinAdminModule`, `PermissionDefinition`, `ModuleRequirements`, `ScopeDefinition`, `IScopeProvider`, `SecretAttribute` |
| `src/backend/WinAdmin.Core/Modules/BuiltInModules.cs` | Существующие разделы как модули |
| `src/backend/WinAdmin.Core/Modules/PermissionCatalog.cs` | Каталог прав и модулей с проверками |
| `src/backend/WinAdmin.Core/Security/PermissionIds.cs` | Константы прав, права ядра |
| `src/backend/WinAdmin.Core/Security/Access.cs` | `PrincipalType`, `PrincipalRef`, `EffectiveScope`, `EffectivePermissions`, `RoleGrant`, `RoleSnapshot`, `PermissionEvaluator`, `IAccessContext`, `AccessContext` |
| `src/backend/WinAdmin.Core/Security/DelegationGuard.cs` | Проверка делегирования, `AccessDeniedException` |
| `src/backend/WinAdmin.Core/Security/BuiltInRoles.cs`, `PrincipalClaims.cs` | Встроенная роль; claim субъекта |
| `src/backend/WinAdmin.Core/Models/RoleModels.cs`, `ModuleModels.cs` | DTO |
| `src/backend/WinAdmin.Core/Abstractions/IAccessService.cs`, `IRoleService.cs`, `IModuleRegistry.cs`, `IMachineInfo.cs` | Интерфейсы |
| `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs` | Сущности модулей, ролей, назначений |
| `src/backend/WinAdmin.Infrastructure/Storage/Migrations/{Sqlite,PostgreSql}/*AddModulesAndRoles*` | Миграции |
| `src/backend/WinAdmin.Infrastructure/Access/*.cs` | `AccessService`, `RoleService`, `RoleMapping`, `ScopeJson`, `PlatformBootstrapper`, `LegacyScopes` |
| `src/backend/WinAdmin.Infrastructure/Modules/*.cs` | `ModuleRegistry`, `ModuleSettingsSchema` |
| `src/backend/WinAdmin.Infrastructure/MachineInfo/WmiMachineInfo.cs` | Требования к машине |
| `src/backend/WinAdmin.Api/Auth/PermissionAuthorization.cs`, `AccessContextFactory.cs` | Политики `perm:*`, контекст доступа |
| `src/backend/WinAdmin.Api/Modules/ModuleAvailability.cs` | `[WinAdminModule]`, middleware 404 |
| `src/backend/WinAdmin.Api/Controllers/{Me,Modules,Permissions,Roles,RoleAssignments}Controller.cs` | Новые API |
| `src/backend/WinAdmin.Api/Controllers/PlatformErrorsAttribute.cs` | Исключения → 400/403/404/409 |
| `src/backend/WinAdmin.Api/Cli/RoleCommands.cs` | CLI `role` |
| `src/frontend/src/api/{types,client,authApi}.ts`, `auth/AuthProvider.tsx` | Права в клиенте |
| `src/frontend/src/components/{AppLayout,Guard,NoAccess}.tsx` | Меню по правам, защита маршрутов |
| `src/frontend/src/pages/{Modules,Roles,Users,ApiKeys,Settings}.tsx` | Страницы |

---

### Task 1: Контракты модулей, каталог прав, встроенные модули

**Files:**
- Modify: `src/backend/WinAdmin.Core/WinAdmin.Core.csproj` (пакеты абстракций DI/конфигурации)
- Create: `src/backend/WinAdmin.Core/Modules/ModuleContracts.cs`
- Create: `src/backend/WinAdmin.Core/Security/PermissionIds.cs`
- Create: `src/backend/WinAdmin.Core/Modules/BuiltInModules.cs`
- Create: `src/backend/WinAdmin.Core/Modules/PermissionCatalog.cs`
- Test: `src/tests/WinAdmin.Tests/PermissionCatalogTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] enum ModuleRequirements { None = 0, WindowsServer = 1, DomainJoined = 2 }`
  - `sealed record PermissionDefinition(string Id, string Title, string? Description = null, bool Scopable = false, bool Dangerous = false)`
  - `sealed record ScopeDefinition(IReadOnlyList<string> Items)`
  - `interface IScopeProvider { string Title { get; } ScopeDefinition Normalize(ScopeDefinition scope); bool IsSubsetOf(ScopeDefinition candidate, ScopeDefinition container); }`
  - `sealed class SecretAttribute : Attribute` (свойства настроек)
  - `interface IWinAdminModule { string Id; string Title; string? Description; ModuleRequirements Requirements; bool EnabledByDefault; IReadOnlyList<PermissionDefinition> Permissions; Type? SettingsType; IScopeProvider? Scope; void ConfigureServices(IServiceCollection, IConfiguration); }`
  - `static class PermissionIds` (константы ниже) с `IReadOnlyList<PermissionDefinition> Platform`
  - `sealed class BuiltInModule(...) : IWinAdminModule`, `static class BuiltInModules { IReadOnlyList<IWinAdminModule> All }`
  - `sealed class PermissionCatalog(IEnumerable<IWinAdminModule>)` с `Modules`, `All`, `Find(string)`, `ModuleOf(string)`, `FindModule(string)`

- [ ] **Step 1: Add DI/configuration abstractions to Core**

```bash
dotnet add src/backend/WinAdmin.Core package Microsoft.Extensions.DependencyInjection.Abstractions --version 10.0.*
dotnet add src/backend/WinAdmin.Core package Microsoft.Extensions.Configuration.Abstractions --version 10.0.*
```

Закрепить обе версии в `WinAdmin.Core.csproj` на фактически восстановленных (как в 1a: взять из `obj/project.assets.json`).

- [ ] **Step 2: Write the failing tests**

`src/tests/WinAdmin.Tests/PermissionCatalogTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class PermissionCatalogTests
{
    private sealed class TestModule(string id, params PermissionDefinition[] permissions) : IWinAdminModule
    {
        public string Id => id;
        public string Title => id;
        public string? Description => null;
        public ModuleRequirements Requirements => ModuleRequirements.None;
        public bool EnabledByDefault => true;
        public IReadOnlyList<PermissionDefinition> Permissions => permissions;
        public Type? SettingsType => null;
        public IScopeProvider? Scope => null;
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }

    [Fact]
    public void Built_in_modules_form_a_valid_catalog_with_platform_permissions()
    {
        var catalog = new PermissionCatalog(BuiltInModules.All);

        Assert.Contains(catalog.All, p => p.Id == PermissionIds.PlatformRolesManage);
        Assert.Contains(catalog.All, p => p.Id == PermissionIds.ServicesManage);
        Assert.Equal("services", catalog.ModuleOf(PermissionIds.ServicesManage)!.Id);
        Assert.Null(catalog.ModuleOf(PermissionIds.PlatformRolesManage));
        Assert.Null(catalog.Find("disks.read")); // объединено с system.read
        Assert.Equal(catalog.All.Count, catalog.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("Services")]
    [InlineData("platform")]
    [InlineData("1abc")]
    public void Rejects_invalid_module_ids(string id)
        => Assert.Throws<InvalidOperationException>(() => new PermissionCatalog([new TestModule(id)]));

    [Fact]
    public void Rejects_permission_outside_module_prefix()
        => Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new TestModule("ad", new PermissionDefinition("users.read", "x"))]));

    [Fact]
    public void Rejects_duplicate_modules_and_permissions()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new TestModule("ad"), new TestModule("ad")]));
        Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new TestModule("ad", new("ad.x", "1"), new("ad.x", "2"))]));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~PermissionCatalogTests`
Expected: build FAIL — `WinAdmin.Core.Modules` не существует.

- [ ] **Step 4: Implement**

`src/backend/WinAdmin.Core/Modules/ModuleContracts.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WinAdmin.Core.Modules;

/// <summary>Требования модуля к машине.</summary>
[Flags]
public enum ModuleRequirements
{
    None = 0,
    WindowsServer = 1,
    DomainJoined = 2,
}

/// <summary>Право модуля: id вида «модуль.действие».</summary>
public sealed record PermissionDefinition(
    string Id, string Title, string? Description = null, bool Scopable = false, bool Dangerous = false);

/// <summary>Область действия права (для AD — список DN OU).</summary>
public sealed record ScopeDefinition(IReadOnlyList<string> Items);

/// <summary>Как модуль понимает область.</summary>
public interface IScopeProvider
{
    /// <summary>Подпись области в UI («Подразделения (OU)»).</summary>
    string Title { get; }

    /// <summary>Нормализует и проверяет область; ArgumentException при ошибке.</summary>
    ScopeDefinition Normalize(ScopeDefinition scope);

    /// <summary>Входит ли candidate в container.</summary>
    bool IsSubsetOf(ScopeDefinition candidate, ScopeDefinition container);
}

/// <summary>Свойство настроек модуля хранится зашифрованным и не возвращается в API.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute;

/// <summary>Модуль WinAdmin: раздел, который включается, настраивается и делегируется.</summary>
public interface IWinAdminModule
{
    string Id { get; }
    string Title { get; }
    string? Description { get; }
    ModuleRequirements Requirements { get; }
    bool EnabledByDefault { get; }
    IReadOnlyList<PermissionDefinition> Permissions { get; }
    Type? SettingsType { get; }
    IScopeProvider? Scope { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
```

`src/backend/WinAdmin.Core/Security/PermissionIds.cs`:

```csharp
using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Security;

/// <summary>Идентификаторы прав встроенных модулей и ядра.</summary>
public static class PermissionIds
{
    public const string SystemRead = "system.read";
    public const string ServicesRead = "services.read";
    public const string ServicesManage = "services.manage";
    public const string ProcessesRead = "processes.read";
    public const string ProcessesManage = "processes.manage";
    public const string PrintersRead = "printers.read";
    public const string PrintersManage = "printers.manage";
    public const string PowerManage = "power.manage";
    public const string EventLogsRead = "eventlogs.read";
    public const string EventLogsManage = "eventlogs.manage";
    public const string SoftwareRead = "software.read";
    public const string SoftwareManage = "software.manage";

    public const string PlatformUsersManage = "platform.users.manage";
    public const string PlatformRolesManage = "platform.roles.manage";
    public const string PlatformModulesManage = "platform.modules.manage";
    public const string PlatformApiKeysManage = "platform.apikeys.manage";
    public const string PlatformAuditRead = "platform.audit.read";
    public const string PlatformNetworkManage = "platform.network.manage";
    public const string PlatformDirectoryManage = "platform.directory.manage";

    /// <summary>Права ядра (не отключаются вместе с модулями).</summary>
    public static IReadOnlyList<PermissionDefinition> Platform { get; } =
    [
        new(PlatformUsersManage, "Пользователи WinAdmin", "Создание, отключение и пароли локальных пользователей", Dangerous: true),
        new(PlatformRolesManage, "Роли и назначения", "Роли и их выдача — в пределах собственных прав", Dangerous: true),
        new(PlatformModulesManage, "Модули", "Включение, отключение и настройка модулей", Dangerous: true),
        new(PlatformApiKeysManage, "API-ключи", "Выпуск и отзыв API-ключей", Dangerous: true),
        new(PlatformAuditRead, "Журнал аудита", "Просмотр журнала действий"),
        new(PlatformNetworkManage, "Сетевой доступ", "Порт, режим доступа, разрешённые подсети", Dangerous: true),
        new(PlatformDirectoryManage, "Подключение к домену", "Настройки подключения к Active Directory", Dangerous: true),
    ];
}
```

`src/backend/WinAdmin.Core/Modules/BuiltInModules.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Modules;

/// <summary>Модуль из существующего раздела: без настроек и областей, сервисы регистрируются в Infrastructure.</summary>
public sealed class BuiltInModule(string id, string title, string description, params PermissionDefinition[] permissions)
    : IWinAdminModule
{
    public string Id => id;
    public string Title => title;
    public string? Description => description;
    public ModuleRequirements Requirements => ModuleRequirements.None;
    public bool EnabledByDefault => true;
    public IReadOnlyList<PermissionDefinition> Permissions => permissions;
    public Type? SettingsType => null;
    public IScopeProvider? Scope => null;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
}

public static class BuiltInModules
{
    public static IReadOnlyList<IWinAdminModule> All { get; } =
    [
        new BuiltInModule("system", "Система", "Дашборд, сведения о системе и диски",
            new(PermissionIds.SystemRead, "Просмотр системы и дисков")),
        new BuiltInModule("services", "Службы", "Службы Windows",
            new(PermissionIds.ServicesRead, "Просмотр служб"),
            new(PermissionIds.ServicesManage, "Запуск, остановка и перезапуск служб", Dangerous: true)),
        new BuiltInModule("processes", "Процессы", "Запущенные процессы",
            new(PermissionIds.ProcessesRead, "Просмотр процессов"),
            new(PermissionIds.ProcessesManage, "Завершение процессов", Dangerous: true)),
        new BuiltInModule("printers", "Принтеры", "Принтеры и очереди печати",
            new(PermissionIds.PrintersRead, "Просмотр принтеров"),
            new(PermissionIds.PrintersManage, "Пауза, возобновление, очистка очереди")),
        new BuiltInModule("power", "Питание", "Перезагрузка и выключение",
            new(PermissionIds.PowerManage, "Перезагрузка и выключение машины", Dangerous: true)),
        new BuiltInModule("eventlogs", "Журналы Windows", "Журналы событий Windows",
            new(PermissionIds.EventLogsRead, "Просмотр журналов"),
            new(PermissionIds.EventLogsManage, "Исключения учётных записей в журналах")),
        new BuiltInModule("software", "Программы", "Установленные программы и обновления",
            new(PermissionIds.SoftwareRead, "Просмотр программ и обновлений"),
            new(PermissionIds.SoftwareManage, "Удаление программ и откат обновлений", Dangerous: true)),
    ];
}
```

`src/backend/WinAdmin.Core/Modules/PermissionCatalog.cs`:

```csharp
using System.Text.RegularExpressions;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Modules;

/// <summary>Все права: ядро + модули. Ошибки описания модулей — ошибка разработчика, служба не стартует.</summary>
public sealed class PermissionCatalog
{
    private static readonly Regex ModuleIdPattern = new("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, PermissionDefinition> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IWinAdminModule> _moduleByPermission = new(StringComparer.Ordinal);
    private readonly List<PermissionDefinition> _all = [];

    public PermissionCatalog(IEnumerable<IWinAdminModule> modules)
    {
        Modules = modules.ToList();
        foreach (var p in PermissionIds.Platform)
            Add(p, null);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Modules)
        {
            if (!ModuleIdPattern.IsMatch(m.Id) || m.Id == "platform")
                throw new InvalidOperationException($"Недопустимый id модуля «{m.Id}».");
            if (!ids.Add(m.Id))
                throw new InvalidOperationException($"Модуль «{m.Id}» зарегистрирован дважды.");
            foreach (var p in m.Permissions)
            {
                if (!p.Id.StartsWith(m.Id + ".", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Право «{p.Id}» модуля «{m.Id}» должно начинаться с «{m.Id}.».");
                Add(p, m);
            }
        }
    }

    public IReadOnlyList<IWinAdminModule> Modules { get; }
    public IReadOnlyList<PermissionDefinition> All => _all;

    public PermissionDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Модуль права; null — право ядра или неизвестное.</summary>
    public IWinAdminModule? ModuleOf(string permissionId) => _moduleByPermission.GetValueOrDefault(permissionId);

    public IWinAdminModule? FindModule(string moduleId) => Modules.FirstOrDefault(m => m.Id == moduleId);

    private void Add(PermissionDefinition p, IWinAdminModule? module)
    {
        if (!_byId.TryAdd(p.Id, p))
            throw new InvalidOperationException($"Право «{p.Id}» объявлено дважды.");
        _all.Add(p);
        if (module is not null)
            _moduleByPermission[p.Id] = module;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~PermissionCatalogTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/backend/WinAdmin.Core src/tests/WinAdmin.Tests/PermissionCatalogTests.cs
git commit -m "feat(core): add module contract, permission catalog and built-in modules"
```

---

### Task 2: Итоговые права и делегирование (чистая логика)

**Files:**
- Create: `src/backend/WinAdmin.Core/Security/Access.cs`
- Create: `src/backend/WinAdmin.Core/Security/DelegationGuard.cs`
- Create: `src/backend/WinAdmin.Core/Security/BuiltInRoles.cs`
- Create: `src/backend/WinAdmin.Core/Security/PrincipalClaims.cs`
- Test: `src/tests/WinAdmin.Tests/PermissionEvaluatorTests.cs`
- Test: `src/tests/WinAdmin.Tests/DelegationGuardTests.cs`

**Interfaces:**
- Consumes: `PermissionCatalog`, `PermissionDefinition`, `ScopeDefinition`, `IScopeProvider`, `PermissionIds` (Task 1).
- Produces:
  - `enum PrincipalType { LocalUser, AdUser, AdGroup, ApiKey }`
  - `sealed record PrincipalRef(PrincipalType Type, string Id, IReadOnlyList<string> GroupSids)` с `string Key`
  - `sealed class EffectiveScope` (`Unrestricted`, `Of(IEnumerable<string>)`, `Items` (null = без ограничений), `IsUnrestricted`)
  - `sealed record RoleGrant(string PermissionId, ScopeDefinition? Scope)`, `sealed record RoleSnapshot(string RoleId, bool IsAdministrator, IReadOnlyList<RoleGrant> Grants)`
  - `sealed class EffectivePermissions` (`None`, `Map`, `Has`, `ScopeFor`)
  - `static class PermissionEvaluator { EffectivePermissions Evaluate(IEnumerable<RoleSnapshot>, PermissionCatalog) }`
  - `interface IAccessContext { PrincipalRef Principal; string Actor; EffectivePermissions Permissions; }`, `sealed record AccessContext(...) : IAccessContext`
  - `static class DelegationGuard { IReadOnlyList<string> Violations(EffectivePermissions actor, IEnumerable<RoleGrant> grants, PermissionCatalog catalog); IEnumerable<RoleGrant> AdministratorGrants(PermissionCatalog) }`
  - `sealed class AccessDeniedException(string message, IReadOnlyList<string> violations) : Exception` с `Violations`
  - `static class BuiltInRoles { const string AdministratorId = "administrator"; const string AdministratorName = "Администратор"; }`
  - `static class PrincipalClaims { const string Type = "wa:principal"; const string GroupType = "wa:group"; string Format(PrincipalType, string id); PrincipalRef? Parse(ClaimsPrincipal user, string apiKeyScheme) }`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/PermissionEvaluatorTests.cs`:

```csharp
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

/// <summary>Модуль с областью (как будущий AD) для тестов прав.</summary>
internal sealed class ScopedTestModule : IWinAdminModule
{
    public const string Read = "ou.users.read";
    public const string Edit = "ou.users.edit";
    public string Id => "ou";
    public string Title => "OU";
    public string? Description => null;
    public ModuleRequirements Requirements => ModuleRequirements.None;
    public bool EnabledByDefault => true;
    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
        [new(Read, "Чтение", Scopable: true), new(Edit, "Правка", Scopable: true)];
    public Type? SettingsType => null;
    public IScopeProvider? Scope { get; } = new SuffixScope();
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    /// <summary>Как DN: «OU=A,OU=B» входит в «OU=B».</summary>
    private sealed class SuffixScope : IScopeProvider
    {
        public string Title => "OU";
        public ScopeDefinition Normalize(ScopeDefinition scope) => scope;
        public bool IsSubsetOf(ScopeDefinition candidate, ScopeDefinition container)
            => candidate.Items.All(c => container.Items.Any(k =>
                c.Equals(k, StringComparison.OrdinalIgnoreCase) || c.EndsWith("," + k, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class PermissionEvaluatorTests
{
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);
    private static RoleSnapshot Role(params RoleGrant[] grants) => new(Guid.NewGuid().ToString(), false, grants);
    private static ScopeDefinition S(params string[] items) => new(items);

    [Fact]
    public void Unions_scopes_of_roles_granting_same_permission()
    {
        var e = PermissionEvaluator.Evaluate(
            [Role(new(ScopedTestModule.Read, S("OU=X"))), Role(new(ScopedTestModule.Read, S("OU=Y", "ou=x")))], Catalog);

        var scope = e.ScopeFor(ScopedTestModule.Read)!;
        Assert.False(scope.IsUnrestricted);
        Assert.Equal(2, scope.Items!.Count);
    }

    [Fact]
    public void Unrestricted_grant_wins()
    {
        var e = PermissionEvaluator.Evaluate(
            [Role(new(ScopedTestModule.Read, S("OU=X"))), Role(new(ScopedTestModule.Read, null))], Catalog);
        Assert.True(e.ScopeFor(ScopedTestModule.Read)!.IsUnrestricted);
    }

    [Fact]
    public void Scope_on_non_scopable_permission_is_ignored()
    {
        var e = PermissionEvaluator.Evaluate([Role(new(PermissionIds.ServicesRead, S("x")))], Catalog);
        Assert.True(e.ScopeFor(PermissionIds.ServicesRead)!.IsUnrestricted);
    }

    [Fact]
    public void Unknown_permissions_are_ignored()
    {
        var e = PermissionEvaluator.Evaluate([Role(new("gone.module.read", null))], Catalog);
        Assert.False(e.Has("gone.module.read"));
        Assert.Empty(e.Map);
    }

    [Fact]
    public void Administrator_gets_every_permission_unrestricted()
    {
        var e = PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], Catalog);
        Assert.All(Catalog.All, p => Assert.True(e.ScopeFor(p.Id)!.IsUnrestricted));
    }

    [Fact]
    public void Principal_claims_round_trip_and_fall_back_to_name_identifier()
    {
        var withClaim = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(PrincipalClaims.Type, PrincipalClaims.Format(PrincipalType.AdUser, "S-1-5-21-1")),
            new Claim(PrincipalClaims.GroupType, "S-1-5-21-512"),
        ], "Bearer"));
        var p = PrincipalClaims.Parse(withClaim, "ApiKey")!;
        Assert.Equal(PrincipalType.AdUser, p.Type);
        Assert.Equal("S-1-5-21-1", p.Id);
        Assert.Equal(["S-1-5-21-512"], p.GroupSids);

        // JWT, выпущенный до обновления: только NameIdentifier.
        var legacy = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "AuthenticationTypes.Federation"));
        Assert.Equal(new PrincipalRef(PrincipalType.LocalUser, "u1", []).Key, PrincipalClaims.Parse(legacy, "ApiKey")!.Key);

        var key = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "k1")], "ApiKey"));
        Assert.Equal(PrincipalType.ApiKey, PrincipalClaims.Parse(key, "ApiKey")!.Type);

        Assert.Null(PrincipalClaims.Parse(new ClaimsPrincipal(new ClaimsIdentity()), "ApiKey"));
    }
}
```

`src/tests/WinAdmin.Tests/DelegationGuardTests.cs`:

```csharp
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class DelegationGuardTests
{
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);

    private static EffectivePermissions Actor(params RoleGrant[] grants)
        => PermissionEvaluator.Evaluate([new RoleSnapshot("r", false, grants)], Catalog);

    [Fact]
    public void Can_grant_subset_of_own_permissions_and_scope()
    {
        var actor = Actor(new(PermissionIds.ServicesRead, null), new(ScopedTestModule.Read, new(["OU=Corp"])));
        var violations = DelegationGuard.Violations(actor,
            [new(PermissionIds.ServicesRead, null), new(ScopedTestModule.Read, new(["OU=HR,OU=Corp"]))], Catalog);
        Assert.Empty(violations);
    }

    [Fact]
    public void Cannot_grant_permission_actor_lacks()
    {
        var actor = Actor(new(PermissionIds.ServicesRead, null));
        var violations = DelegationGuard.Violations(actor, [new(PermissionIds.ServicesManage, null)], Catalog);
        Assert.Single(violations);
        Assert.Contains(PermissionIds.ServicesManage, violations[0]);
    }

    [Fact]
    public void Cannot_widen_scope_or_drop_it()
    {
        var actor = Actor(new(ScopedTestModule.Read, new(["OU=HR,OU=Corp"])));
        Assert.NotEmpty(DelegationGuard.Violations(actor, [new(ScopedTestModule.Read, new(["OU=Corp"]))], Catalog));
        Assert.NotEmpty(DelegationGuard.Violations(actor, [new(ScopedTestModule.Read, null)], Catalog));
    }

    [Fact]
    public void Only_full_administrator_can_hand_out_administrator()
    {
        var partial = Actor(new(PermissionIds.PlatformRolesManage, null), new(PermissionIds.ServicesRead, null));
        Assert.NotEmpty(DelegationGuard.Violations(partial, DelegationGuard.AdministratorGrants(Catalog), Catalog));

        var admin = PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], Catalog);
        Assert.Empty(DelegationGuard.Violations(admin, DelegationGuard.AdministratorGrants(Catalog), Catalog));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~PermissionEvaluatorTests|FullyQualifiedName~DelegationGuardTests"`
Expected: build FAIL — `PermissionEvaluator` не найден.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Core/Security/Access.cs`:

```csharp
using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Security;

public enum PrincipalType { LocalUser, AdUser, AdGroup, ApiKey }

/// <summary>Субъект доступа; GroupSids — группы AD (транзитивно), для остальных — пусто.</summary>
public sealed record PrincipalRef(PrincipalType Type, string Id, IReadOnlyList<string> GroupSids)
{
    public string Key => $"{Type}:{Id}";
}

/// <summary>Область итогового права: Items == null — без ограничений.</summary>
public sealed class EffectiveScope
{
    private EffectiveScope(IReadOnlyList<string>? items) => Items = items;

    public static EffectiveScope Unrestricted { get; } = new(null);

    public static EffectiveScope Of(IEnumerable<string> items)
        => new(items.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    public IReadOnlyList<string>? Items { get; }
    public bool IsUnrestricted => Items is null;
}

public sealed record RoleGrant(string PermissionId, ScopeDefinition? Scope);

public sealed record RoleSnapshot(string RoleId, bool IsAdministrator, IReadOnlyList<RoleGrant> Grants);

/// <summary>Итоговые права субъекта: право → область.</summary>
public sealed class EffectivePermissions(IReadOnlyDictionary<string, EffectiveScope> map)
{
    public static EffectivePermissions None { get; } = new(new Dictionary<string, EffectiveScope>());

    public IReadOnlyDictionary<string, EffectiveScope> Map { get; } = map;
    public bool Has(string permissionId) => Map.ContainsKey(permissionId);
    public EffectiveScope? ScopeFor(string permissionId) => Map.GetValueOrDefault(permissionId);
}

public static class PermissionEvaluator
{
    /// <summary>
    /// Объединение ролей: право без области (или необластное право) в любой роли — без ограничений;
    /// иначе — объединение областей всех ролей, дающих это право. Неизвестные права игнорируются.
    /// </summary>
    public static EffectivePermissions Evaluate(IEnumerable<RoleSnapshot> roles, PermissionCatalog catalog)
    {
        var unrestricted = new HashSet<string>(StringComparer.Ordinal);
        var scoped = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            var grants = role.IsAdministrator ? DelegationGuard.AdministratorGrants(catalog) : role.Grants;
            foreach (var grant in grants)
            {
                var def = catalog.Find(grant.PermissionId);
                if (def is null) continue;
                if (grant.Scope is null || !def.Scopable)
                {
                    unrestricted.Add(def.Id);
                    continue;
                }
                if (!scoped.TryGetValue(def.Id, out var set))
                    scoped[def.Id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.UnionWith(grant.Scope.Items);
            }
        }

        var map = new Dictionary<string, EffectiveScope>(StringComparer.Ordinal);
        foreach (var id in unrestricted)
            map[id] = EffectiveScope.Unrestricted;
        foreach (var (id, set) in scoped)
            map.TryAdd(id, EffectiveScope.Of(set));
        return new EffectivePermissions(map);
    }
}

/// <summary>Кто действует и что ему можно (для сервисов и контроллеров).</summary>
public interface IAccessContext
{
    PrincipalRef Principal { get; }
    string Actor { get; }
    EffectivePermissions Permissions { get; }
}

public sealed record AccessContext(PrincipalRef Principal, string Actor, EffectivePermissions Permissions) : IAccessContext;
```

`src/backend/WinAdmin.Core/Security/DelegationGuard.cs`:

```csharp
using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Security;

/// <summary>Отказ в делегировании: перечень прав/областей, которые действующее лицо не может выдать.</summary>
public sealed class AccessDeniedException(string message, IReadOnlyList<string> violations) : Exception(message)
{
    public IReadOnlyList<string> Violations { get; } = violations;
}

/// <summary>Нельзя выдать больше, чем есть у себя: каждое право — своё, область — не шире своей.</summary>
public static class DelegationGuard
{
    public static IEnumerable<RoleGrant> AdministratorGrants(PermissionCatalog catalog)
        => catalog.All.Select(p => new RoleGrant(p.Id, null));

    public static IReadOnlyList<string> Violations(EffectivePermissions actor, IEnumerable<RoleGrant> grants, PermissionCatalog catalog)
    {
        var violations = new List<string>();
        foreach (var grant in grants)
        {
            var have = actor.ScopeFor(grant.PermissionId);
            if (have is null)
            {
                violations.Add($"нет права «{grant.PermissionId}»");
                continue;
            }
            if (have.IsUnrestricted)
                continue;

            var def = catalog.Find(grant.PermissionId);
            if (grant.Scope is null || def is null || !def.Scopable)
            {
                violations.Add($"«{grant.PermissionId}»: можно выдать только в пределах своей области");
                continue;
            }

            var own = new ScopeDefinition(have.Items!);
            var provider = catalog.ModuleOf(grant.PermissionId)?.Scope;
            bool subset = provider?.IsSubsetOf(grant.Scope, own)
                          ?? grant.Scope.Items.All(i => own.Items.Contains(i, StringComparer.OrdinalIgnoreCase));
            if (!subset)
                violations.Add($"«{grant.PermissionId}»: область шире вашей");
        }
        return violations;
    }
}
```

`src/backend/WinAdmin.Core/Security/BuiltInRoles.cs`:

```csharp
namespace WinAdmin.Core.Security;

public static class BuiltInRoles
{
    public const string AdministratorId = "administrator";
    public const string AdministratorName = "Администратор";
}
```

`src/backend/WinAdmin.Core/Security/PrincipalClaims.cs`:

```csharp
using System.Security.Claims;

namespace WinAdmin.Core.Security;

/// <summary>Субъект в claims: wa:principal = «Тип:id», wa:group = SID группы AD.</summary>
public static class PrincipalClaims
{
    public const string Type = "wa:principal";
    public const string GroupType = "wa:group";

    public static string Format(PrincipalType type, string id) => $"{type}:{id}";

    /// <summary>
    /// Субъект запроса. Без claim wa:principal (JWT до обновления, API-ключ) — по NameIdentifier:
    /// схема API-ключа → ApiKey, иначе — локальный пользователь.
    /// </summary>
    public static PrincipalRef? Parse(ClaimsPrincipal user, string apiKeyScheme)
    {
        var groups = user.FindAll(GroupType).Select(c => c.Value).ToList();
        string? value = user.FindFirst(Type)?.Value;
        if (value is not null)
        {
            int colon = value.IndexOf(':');
            if (colon > 0 && Enum.TryParse<PrincipalType>(value[..colon], out var type) && colon < value.Length - 1)
                return new PrincipalRef(type, value[(colon + 1)..], groups);
            return null;
        }

        string? id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(id))
            return null;
        bool isKey = user.Identities.Any(i => i.AuthenticationType == apiKeyScheme);
        return new PrincipalRef(isKey ? PrincipalType.ApiKey : PrincipalType.LocalUser, id, groups);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~PermissionEvaluatorTests|FullyQualifiedName~DelegationGuardTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core/Security src/tests/WinAdmin.Tests/PermissionEvaluatorTests.cs src/tests/WinAdmin.Tests/DelegationGuardTests.cs
git commit -m "feat(core): evaluate effective permissions and guard delegation"
```

---

### Task 3: Хранение модулей, ролей и назначений

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs`
- Create (генерация): `Storage/Migrations/Sqlite/*_AddModulesAndRoles*`, `Storage/Migrations/PostgreSql/*_AddModulesAndRoles*`
- Test: `src/tests/WinAdmin.Tests/RoleStorageTests.cs`

**Interfaces:**
- Produces (namespace `WinAdmin.Infrastructure.Storage`):
  - `DbSet<ModuleStateEntity> ModuleStates`, `DbSet<RoleEntity> Roles`, `DbSet<RolePermissionEntity> RolePermissions`, `DbSet<RoleAssignmentEntity> RoleAssignments`
  - `ModuleStateEntity { string Id; bool Enabled; string? SettingsJson; DateTimeOffset UpdatedAt; string? UpdatedBy }`
  - `RoleEntity { string Id = Guid; string Name; string? Description; bool IsBuiltin; DateTimeOffset CreatedAt; ICollection<RolePermissionEntity> Permissions; ICollection<RoleAssignmentEntity> Assignments }`
  - `RolePermissionEntity { string RoleId; string PermissionId; string? ScopeJson; RoleEntity Role }` (ключ RoleId+PermissionId)
  - `RoleAssignmentEntity { string Id = Guid; string RoleId; string PrincipalType; string PrincipalId; string DisplayName; DateTimeOffset CreatedAt; string? CreatedBy; RoleEntity Role }` (уникально RoleId+PrincipalType+PrincipalId)

- [ ] **Step 1: Write the failing test**

`src/tests/WinAdmin.Tests/RoleStorageTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class RoleStorageTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;

    public RoleStorageTests()
    {
        _connection.Open();
        _db = new SqliteWinAdminDbContext(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Deleting_role_removes_its_permissions_and_assignments()
    {
        var role = new RoleEntity { Name = "Кадры" };
        role.Permissions.Add(new RolePermissionEntity { PermissionId = "ou.users.read", ScopeJson = "[\"OU=HR\"]" });
        role.Assignments.Add(new RoleAssignmentEntity { PrincipalType = "LocalUser", PrincipalId = "u1", DisplayName = "u1" });
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.RolePermissions.CountAsync());
        Assert.Equal(0, await _db.RoleAssignments.CountAsync());
    }

    [Fact]
    public async Task Same_principal_cannot_get_same_role_twice()
    {
        var role = new RoleEntity { Name = "R" };
        _db.Roles.Add(role);
        _db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "ApiKey", PrincipalId = "k", DisplayName = "k" });
        _db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "ApiKey", PrincipalId = "k", DisplayName = "k" });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Module_state_round_trips()
    {
        _db.ModuleStates.Add(new ModuleStateEntity { Id = "services", Enabled = false, SettingsJson = "{}" });
        await _db.SaveChangesAsync();
        Assert.False((await _db.ModuleStates.SingleAsync()).Enabled);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~RoleStorageTests`
Expected: build FAIL — `RoleEntity` не найден.

- [ ] **Step 3: Add entities and model configuration**

В `WinAdminDbContext.cs`:

1. После `public DbSet<ExcludedUserEntity> ExcludedUsers => Set<ExcludedUserEntity>();` добавить:

```csharp
    public DbSet<ModuleStateEntity> ModuleStates => Set<ModuleStateEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<RoleAssignmentEntity> RoleAssignments => Set<RoleAssignmentEntity>();
```

2. В конец `OnModelCreating` (перед закрывающей `}` метода) добавить:

```csharp
        modelBuilder.Entity<ModuleStateEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.UpdatedAt).HasConversion(DtoConverter);
        });

        modelBuilder.Entity<RoleEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
            e.HasMany(x => x.Permissions).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assignments).WithOne(x => x.Role).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermissionEntity>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.Property(x => x.PermissionId).HasMaxLength(128);
        });

        modelBuilder.Entity<RoleAssignmentEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PrincipalType).IsRequired().HasMaxLength(20);
            e.Property(x => x.PrincipalId).IsRequired().HasMaxLength(200);
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(256);
            e.HasIndex(x => new { x.RoleId, x.PrincipalType, x.PrincipalId }).IsUnique();
            e.HasIndex(x => new { x.PrincipalType, x.PrincipalId });
            e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
        });
```

3. В конец файла добавить классы:

```csharp
/// <summary>Состояние модуля: включён ли, настройки (секреты — enc:v1:…).</summary>
public sealed class ModuleStateEntity
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; }
    public string? SettingsJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}

public sealed class RoleEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsBuiltin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<RolePermissionEntity> Permissions { get; set; } = [];
    public ICollection<RoleAssignmentEntity> Assignments { get; set; } = [];
}

/// <summary>Право в роли; ScopeJson — JSON-массив строк области или null (без области).</summary>
public sealed class RolePermissionEntity
{
    public string RoleId { get; set; } = "";
    public RoleEntity Role { get; set; } = null!;
    public string PermissionId { get; set; } = "";
    public string? ScopeJson { get; set; }
}

/// <summary>Назначение роли субъекту (PrincipalType — имя значения PrincipalType).</summary>
public sealed class RoleAssignmentEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RoleId { get; set; } = "";
    public RoleEntity Role { get; set; } = null!;
    public string PrincipalType { get; set; } = "";
    public string PrincipalId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
}
```

- [ ] **Step 4: Generate migrations for both providers**

```bash
dotnet ef migrations add AddModulesAndRoles --project src/backend/WinAdmin.Infrastructure --startup-project src/backend/WinAdmin.Api --context SqliteWinAdminDbContext --output-dir Storage/Migrations/Sqlite --namespace WinAdmin.Infrastructure.Storage.Migrations.Sqlite
dotnet ef migrations add AddModulesAndRoles --project src/backend/WinAdmin.Infrastructure --startup-project src/backend/WinAdmin.Api --context PostgresWinAdminDbContext --output-dir Storage/Migrations/PostgreSql --namespace WinAdmin.Infrastructure.Storage.Migrations.PostgreSql
```

Expected: `Done.` дважды. Если снимок модели снова окажется в `src/backend/WinAdmin.Infrastructure/WinAdmin/…` (как в 1a), перенести его в нужную папку `Storage/Migrations/...` поверх существующего снимка и удалить лишний каталог.

- [ ] **Step 5: Run tests**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~RoleStorageTests|FullyQualifiedName~DatabaseMigrationTests"`
Expected: PASS (включая `HasPendingModelChanges` для обоих провайдеров).

- [ ] **Step 6: Commit**

```bash
git add -A src/backend/WinAdmin.Infrastructure src/tests/WinAdmin.Tests/RoleStorageTests.cs
git commit -m "feat(storage): store module states, roles and role assignments"
```

---

### Task 4: Сервис итоговых прав с кэшем

**Files:**
- Create: `src/backend/WinAdmin.Core/Abstractions/IAccessService.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Access/ScopeJson.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Access/AccessService.cs`
- Test: `src/tests/WinAdmin.Tests/AccessServiceTests.cs`

**Interfaces:**
- Consumes: `PermissionCatalog`, `PermissionEvaluator`, `RoleSnapshot`, `RoleGrant`, `PrincipalRef`, `EffectivePermissions`, `BuiltInRoles` (Tasks 1–2); сущности Task 3.
- Produces:
  - `interface IAccessService { Task<EffectivePermissions> GetAsync(PrincipalRef principal, CancellationToken ct = default); void Invalidate(); }`
  - `static class ScopeJson { string? Serialize(ScopeDefinition?); ScopeDefinition? Parse(string?) }`
  - `static class RoleMapping { RoleSnapshot ToSnapshot(RoleEntity) }` (в `AccessService.cs`)
  - `sealed class AccessService(IServiceScopeFactory, PermissionCatalog, TimeProvider? = null) : IAccessService`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/AccessServiceTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AccessServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly AccessService _access;
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);

    public AccessServiceTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
        _access = new AccessService(_sp.GetRequiredService<IServiceScopeFactory>(), Catalog, _time);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private void Seed(Action<WinAdminDbContext> seed)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        seed(db);
        db.SaveChanges();
    }

    private static RoleEntity Role(string name, string permission, string? scopeJson = null)
    {
        var r = new RoleEntity { Name = name };
        r.Permissions.Add(new RolePermissionEntity { PermissionId = permission, ScopeJson = scopeJson });
        return r;
    }

    [Fact]
    public async Task Combines_direct_and_group_assignments()
    {
        var direct = Role("D", PermissionIds.ServicesRead);
        var viaGroup = Role("G", ScopedTestModule.Read, "[\"OU=HR\"]");
        Seed(db =>
        {
            db.Roles.AddRange(direct, viaGroup);
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = direct.Id, PrincipalType = "AdUser", PrincipalId = "S-1-5-21-7", DisplayName = "u" });
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = viaGroup.Id, PrincipalType = "AdGroup", PrincipalId = "S-1-5-21-900", DisplayName = "g" });
        });

        var p = await _access.GetAsync(new PrincipalRef(PrincipalType.AdUser, "S-1-5-21-7", ["S-1-5-21-900"]));

        Assert.True(p.Has(PermissionIds.ServicesRead));
        Assert.Equal(["OU=HR"], p.ScopeFor(ScopedTestModule.Read)!.Items);
        Assert.False(p.Has(PermissionIds.ServicesManage));
    }

    [Fact]
    public async Task Administrator_assignment_grants_everything()
    {
        Seed(db =>
        {
            db.Roles.Add(new RoleEntity { Id = BuiltInRoles.AdministratorId, Name = BuiltInRoles.AdministratorName, IsBuiltin = true });
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = BuiltInRoles.AdministratorId, PrincipalType = "LocalUser", PrincipalId = "u1", DisplayName = "u1" });
        });

        var p = await _access.GetAsync(new PrincipalRef(PrincipalType.LocalUser, "u1", []));
        Assert.All(Catalog.All, d => Assert.True(p.Has(d.Id)));
    }

    [Fact]
    public async Task Caches_for_60_seconds_and_invalidates_on_demand()
    {
        var role = Role("R", PermissionIds.ServicesRead);
        Seed(db => db.Roles.Add(role));
        var who = new PrincipalRef(PrincipalType.ApiKey, "k1", []);
        Assert.False((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead));

        Seed(db => db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "ApiKey", PrincipalId = "k1", DisplayName = "k" }));
        Assert.False((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead)); // из кэша

        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead));

        Seed(db => db.RoleAssignments.RemoveRange(db.RoleAssignments));
        _access.Invalidate();
        Assert.False((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~AccessServiceTests`
Expected: build FAIL — `AccessService` не найден.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Core/Abstractions/IAccessService.cs`:

```csharp
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Abstractions;

/// <summary>Итоговые права субъекта (с кэшем на 60 с).</summary>
public interface IAccessService
{
    Task<EffectivePermissions> GetAsync(PrincipalRef principal, CancellationToken ct = default);

    /// <summary>Сбросить кэш (после изменения ролей, назначений, модулей).</summary>
    void Invalidate();
}
```

`src/backend/WinAdmin.Infrastructure/Access/ScopeJson.cs`:

```csharp
using System.Text.Json;
using WinAdmin.Core.Modules;

namespace WinAdmin.Infrastructure.Access;

/// <summary>Область в БД — JSON-массив строк; null — без области.</summary>
public static class ScopeJson
{
    public static string? Serialize(ScopeDefinition? scope)
        => scope is null ? null : JsonSerializer.Serialize(scope.Items);

    public static ScopeDefinition? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var items = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return new ScopeDefinition(items);
    }
}
```

`src/backend/WinAdmin.Infrastructure/Access/AccessService.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

public static class RoleMapping
{
    public static RoleSnapshot ToSnapshot(RoleEntity role) => new(
        role.Id,
        role.Id == BuiltInRoles.AdministratorId,
        role.Permissions.Select(p => new RoleGrant(p.PermissionId, ScopeJson.Parse(p.ScopeJson))).ToList());
}

/// <summary>Права субъекта и его групп AD; кэш 60 с по субъекту и набору групп.</summary>
public sealed class AccessService(IServiceScopeFactory scopes, PermissionCatalog catalog, TimeProvider? time = null) : IAccessService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, EffectivePermissions Value)> _cache = new();

    public async Task<EffectivePermissions> GetAsync(PrincipalRef principal, CancellationToken ct = default)
    {
        string key = principal.Key + "|" + string.Join(",", principal.GroupSids.Order(StringComparer.OrdinalIgnoreCase));
        var now = _time.GetUtcNow();
        if (_cache.TryGetValue(key, out var hit) && now - hit.At < Ttl)
            return hit.Value;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        string type = principal.Type.ToString();
        string groupType = PrincipalType.AdGroup.ToString();
        var groups = principal.GroupSids.ToList();

        var roleIds = await db.RoleAssignments.AsNoTracking()
            .Where(a => (a.PrincipalType == type && a.PrincipalId == principal.Id)
                        || (a.PrincipalType == groupType && groups.Contains(a.PrincipalId)))
            .Select(a => a.RoleId)
            .Distinct()
            .ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking()
            .Include(r => r.Permissions)
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(ct);

        var result = PermissionEvaluator.Evaluate(roles.Select(RoleMapping.ToSnapshot), catalog);
        _cache[key] = (now, result);
        return result;
    }

    public void Invalidate() => _cache.Clear();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~AccessServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core/Abstractions/IAccessService.cs src/backend/WinAdmin.Infrastructure/Access src/tests/WinAdmin.Tests/AccessServiceTests.cs
git commit -m "feat(access): compute effective permissions with 60s cache"
```

---

### Task 5: Роли и назначения — делегирование и защита от потери доступа

**Files:**
- Create: `src/backend/WinAdmin.Core/Models/RoleModels.cs`
- Create: `src/backend/WinAdmin.Core/Abstractions/IRoleService.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Access/RoleService.cs`
- Test: `src/tests/WinAdmin.Tests/RoleServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4; `IAuditService`, `AuditEntryDto` (существующие).
- Produces:
  - `sealed record RoleGrantDto(string PermissionId, IReadOnlyList<string>? Scope)`
  - `sealed record RoleDto(string Id, string Name, string? Description, bool IsBuiltin, IReadOnlyList<RoleGrantDto> Permissions, int AssignmentCount)`
  - `sealed record SaveRoleRequest(string Name, string? Description, List<RoleGrantDto> Permissions)`
  - `sealed record RoleAssignmentDto(string Id, string RoleId, string RoleName, PrincipalType PrincipalType, string PrincipalId, string DisplayName, DateTimeOffset CreatedAt)`
  - `sealed record CreateAssignmentRequest(string RoleId, PrincipalType PrincipalType, string PrincipalId, string? DisplayName)`
  - `interface IRoleService` (методы ниже); исключения: `ArgumentException` (400), `AccessDeniedException` (403), `InvalidOperationException` (409), `KeyNotFoundException` (404)
  - `sealed class RoleService(WinAdminDbContext, PermissionCatalog, IAccessService, IAuditService) : IRoleService`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/RoleServiceTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class RoleServiceTests : IDisposable
{
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;
    private readonly Mock<IAccessService> _access = new();
    private readonly RoleService _roles;

    private static IAccessContext Admin => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "admin", []), "admin",
        PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], Catalog));

    private static IAccessContext Delegate(params RoleGrant[] grants) => new AccessContext(
        new PrincipalRef(PrincipalType.LocalUser, "hrlead", []), "hrlead",
        PermissionEvaluator.Evaluate([new RoleSnapshot("d", false, [new(PermissionIds.PlatformRolesManage, null), .. grants])], Catalog));

    public RoleServiceTests()
    {
        _connection.Open();
        _db = new SqliteWinAdminDbContext(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _db.Roles.Add(new RoleEntity { Id = BuiltInRoles.AdministratorId, Name = BuiltInRoles.AdministratorName, IsBuiltin = true });
        _db.Users.Add(new UserEntity { Id = "u-admin", Login = "admin", PasswordHash = "x" });
        _db.Users.Add(new UserEntity { Id = "u-second", Login = "second", PasswordHash = "x" });
        _db.SaveChanges();
        _roles = new RoleService(_db, Catalog, _access.Object, Mock.Of<IAuditService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task<RoleAssignmentDto> AssignAdmin(string userId)
        => _roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, userId, null), Admin);

    [Fact]
    public async Task Creates_role_and_invalidates_cache()
    {
        var role = await _roles.CreateAsync(new SaveRoleRequest(" Кадры ", "HR",
            [new(ScopedTestModule.Read, [" OU=HR ", "OU=HR"]), new(PermissionIds.ServicesRead, null)]), Admin);

        Assert.Equal("Кадры", role.Name);
        Assert.Equal(["OU=HR"], role.Permissions.Single(p => p.PermissionId == ScopedTestModule.Read).Scope);
        _access.Verify(a => a.Invalidate(), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData("", PermissionIds.ServicesRead)]
    [InlineData("R", "no.such.permission")]
    public async Task Rejects_invalid_roles(string name, string permission)
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            _roles.CreateAsync(new SaveRoleRequest(name, null, [new(permission, null)]), Admin));

    [Fact]
    public async Task Rejects_duplicate_name_case_insensitive()
    {
        await _roles.CreateAsync(new SaveRoleRequest("Кадры", null, [new(PermissionIds.ServicesRead, null)]), Admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _roles.CreateAsync(new SaveRoleRequest("кадры", null, [new(PermissionIds.ServicesRead, null)]), Admin));
    }

    [Fact]
    public async Task Delegate_cannot_create_role_beyond_own_rights_and_nothing_changes()
    {
        var hr = Delegate(new(ScopedTestModule.Read, new(["OU=HR"])));
        var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => _roles.CreateAsync(
            new SaveRoleRequest("Шире", null, [new(ScopedTestModule.Read, ["OU=Corp"]), new(PermissionIds.PowerManage, null)]), hr));

        Assert.Equal(2, ex.Violations.Count);
        Assert.Equal(1, await _db.Roles.CountAsync()); // только «Администратор»
    }

    [Fact]
    public async Task Delegate_cannot_assign_administrator()
    {
        var hr = Delegate(new(PermissionIds.ServicesRead, null));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _roles.AssignAsync(
            new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, "u-second", null), hr));
    }

    [Fact]
    public async Task Builtin_role_is_immutable()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _roles.UpdateAsync(BuiltInRoles.AdministratorId, new SaveRoleRequest("X", null, []), Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.DeleteAsync(BuiltInRoles.AdministratorId, Admin));
    }

    [Fact]
    public async Task Assignment_requires_existing_principal_and_is_unique()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AssignAdmin("nobody"));
        var first = await AssignAdmin("u-admin");
        Assert.Equal("admin", first.DisplayName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AssignAdmin("u-admin"));
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_removed()
    {
        var a = await AssignAdmin("u-admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(a.Id, Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _roles.EnsureNotLastAdministratorAsync(PrincipalType.LocalUser, "u-admin"));

        // Второй администратор отключён — не считается.
        var b = await AssignAdmin("u-second");
        (await _db.Users.FindAsync("u-second"))!.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(a.Id, Admin));

        // Активный второй — можно.
        (await _db.Users.FindAsync("u-second"))!.IsActive = true;
        await _db.SaveChangesAsync();
        await _roles.UnassignAsync(a.Id, Admin);
        Assert.Single(await _roles.ListAssignmentsAsync());
        _ = b;
    }

    [Fact]
    public async Task Expired_api_key_is_not_an_active_administrator()
    {
        _db.ApiKeys.Add(new ApiKeyEntity { Id = "k-old", Name = "old", KeyHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();
        var user = await AssignAdmin("u-admin");
        await _roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.ApiKey, "k-old", null), Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(user.Id, Admin));
    }

    [Fact]
    public async Task Removing_principal_drops_its_assignments()
    {
        await AssignAdmin("u-admin");
        var role = await _roles.CreateAsync(new SaveRoleRequest("R", null, [new(PermissionIds.ServicesRead, null)]), Admin);
        await _roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.LocalUser, "u-second", null), Admin);

        await _roles.RemovePrincipalAsync(PrincipalType.LocalUser, "u-second");

        Assert.DoesNotContain(await _roles.ListAssignmentsAsync(), x => x.PrincipalId == "u-second");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~RoleServiceTests`
Expected: build FAIL — `RoleService` не найден.

- [ ] **Step 3: Implement DTOs and interface**

`src/backend/WinAdmin.Core/Models/RoleModels.cs`:

```csharp
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Models;

public sealed record RoleGrantDto(string PermissionId, IReadOnlyList<string>? Scope);

public sealed record RoleDto(
    string Id, string Name, string? Description, bool IsBuiltin,
    IReadOnlyList<RoleGrantDto> Permissions, int AssignmentCount);

public sealed record SaveRoleRequest(string Name, string? Description, List<RoleGrantDto> Permissions);

public sealed record RoleAssignmentDto(
    string Id, string RoleId, string RoleName, PrincipalType PrincipalType,
    string PrincipalId, string DisplayName, DateTimeOffset CreatedAt);

public sealed record CreateAssignmentRequest(string RoleId, PrincipalType PrincipalType, string PrincipalId, string? DisplayName);
```

`src/backend/WinAdmin.Core/Abstractions/IRoleService.cs`:

```csharp
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Abstractions;

/// <summary>
/// Роли и назначения. Ошибки: ArgumentException — неверные данные; AccessDeniedException —
/// нарушение делегирования; InvalidOperationException — конфликт; KeyNotFoundException — нет объекта.
/// </summary>
public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto> CreateAsync(SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(string id, SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default);
    Task DeleteAsync(string id, IAccessContext actor, CancellationToken ct = default);

    Task<IReadOnlyList<RoleAssignmentDto>> ListAssignmentsAsync(
        PrincipalType? type = null, string? principalId = null, string? roleId = null, CancellationToken ct = default);
    Task<RoleAssignmentDto> AssignAsync(CreateAssignmentRequest request, IAccessContext actor, CancellationToken ct = default);
    Task UnassignAsync(string assignmentId, IAccessContext actor, CancellationToken ct = default);

    /// <summary>409, если субъект — последний активный администратор.</summary>
    Task EnsureNotLastAdministratorAsync(PrincipalType type, string principalId, CancellationToken ct = default);

    /// <summary>Удаляет все назначения субъекта (при удалении пользователя/ключа).</summary>
    Task RemovePrincipalAsync(PrincipalType type, string principalId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement the service**

`src/backend/WinAdmin.Infrastructure/Access/RoleService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

public sealed class RoleService(WinAdminDbContext db, PermissionCatalog catalog, IAccessService access, IAuditService audit) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct);
        var counts = await db.RoleAssignments.AsNoTracking().GroupBy(a => a.RoleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return roles
            .OrderByDescending(r => r.IsBuiltin).ThenBy(r => r.Name)
            .Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDto> CreateAsync(SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default)
    {
        var (name, grants) = Validate(request);
        Demand(actor, grants);
        await EnsureUniqueNameAsync(name, null, ct);

        var role = new RoleEntity { Name = name, Description = request.Description?.Trim() };
        foreach (var g in grants)
            role.Permissions.Add(new RolePermissionEntity { PermissionId = g.PermissionId, ScopeJson = ScopeJson.Serialize(g.Scope) });
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.create", role.Name, ct);
        access.Invalidate();
        return ToDto(role, 0);
    }

    public async Task<RoleDto> UpdateAsync(string id, SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default)
    {
        var role = await LoadRoleAsync(id, ct);
        if (role.IsBuiltin)
            throw new InvalidOperationException("Встроенную роль нельзя изменить.");
        var (name, grants) = Validate(request);
        Demand(actor, GrantsOf(role));
        Demand(actor, grants);
        await EnsureUniqueNameAsync(name, role.Id, ct);

        role.Name = name;
        role.Description = request.Description?.Trim();
        db.RolePermissions.RemoveRange(role.Permissions);
        role.Permissions.Clear();
        foreach (var g in grants)
            role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, PermissionId = g.PermissionId, ScopeJson = ScopeJson.Serialize(g.Scope) });
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.update", role.Name, ct);
        access.Invalidate();
        return ToDto(role, await db.RoleAssignments.CountAsync(a => a.RoleId == role.Id, ct));
    }

    public async Task DeleteAsync(string id, IAccessContext actor, CancellationToken ct = default)
    {
        var role = await LoadRoleAsync(id, ct);
        if (role.IsBuiltin)
            throw new InvalidOperationException("Встроенную роль нельзя удалить.");
        Demand(actor, GrantsOf(role));
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.delete", role.Name, ct);
        access.Invalidate();
    }

    public async Task<IReadOnlyList<RoleAssignmentDto>> ListAssignmentsAsync(
        PrincipalType? type = null, string? principalId = null, string? roleId = null, CancellationToken ct = default)
    {
        var query = db.RoleAssignments.AsNoTracking().Include(a => a.Role).AsQueryable();
        if (type is not null) query = query.Where(a => a.PrincipalType == type.ToString());
        if (principalId is not null) query = query.Where(a => a.PrincipalId == principalId);
        if (roleId is not null) query = query.Where(a => a.RoleId == roleId);
        var list = await query.ToListAsync(ct);
        return list.OrderBy(a => a.Role.Name).ThenBy(a => a.DisplayName).Select(ToDto).ToList();
    }

    public async Task<RoleAssignmentDto> AssignAsync(CreateAssignmentRequest request, IAccessContext actor, CancellationToken ct = default)
    {
        var role = await LoadRoleAsync(request.RoleId, ct);
        Demand(actor, GrantsOf(role));
        string displayName = await ResolvePrincipalAsync(request, ct);
        string type = request.PrincipalType.ToString();
        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == role.Id && a.PrincipalType == type && a.PrincipalId == request.PrincipalId, ct))
            throw new InvalidOperationException($"Роль «{role.Name}» уже назначена «{displayName}».");

        var entity = new RoleAssignmentEntity
        {
            RoleId = role.Id, Role = role, PrincipalType = type, PrincipalId = request.PrincipalId,
            DisplayName = displayName, CreatedBy = actor.Actor,
        };
        db.RoleAssignments.Add(entity);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.assign", $"{role.Name} → {displayName}", ct);
        access.Invalidate();
        return ToDto(entity);
    }

    public async Task UnassignAsync(string assignmentId, IAccessContext actor, CancellationToken ct = default)
    {
        var entity = await db.RoleAssignments.Include(a => a.Role).ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new KeyNotFoundException("Назначение не найдено.");
        Demand(actor, GrantsOf(entity.Role));
        if (entity.RoleId == BuiltInRoles.AdministratorId && await CountActiveAdministratorsAsync(entity.Id, ct) == 0)
            throw new InvalidOperationException("Нельзя снять последнее назначение роли «Администратор».");

        db.RoleAssignments.Remove(entity);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.unassign", $"{entity.Role.Name} → {entity.DisplayName}", ct);
        access.Invalidate();
    }

    public async Task EnsureNotLastAdministratorAsync(PrincipalType type, string principalId, CancellationToken ct = default)
    {
        string t = type.ToString();
        var own = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == BuiltInRoles.AdministratorId && a.PrincipalType == t && a.PrincipalId == principalId)
            .Select(a => a.Id).ToListAsync(ct);
        if (own.Count == 0) return;
        if (await CountActiveAdministratorsAsync(null, ct, excludePrincipal: (t, principalId)) == 0)
            throw new InvalidOperationException("Это последний активный администратор WinAdmin — сначала назначьте роль «Администратор» другому.");
    }

    public async Task RemovePrincipalAsync(PrincipalType type, string principalId, CancellationToken ct = default)
    {
        string t = type.ToString();
        var rows = await db.RoleAssignments.Where(a => a.PrincipalType == t && a.PrincipalId == principalId).ToListAsync(ct);
        db.RoleAssignments.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        access.Invalidate();
    }

    // ── helpers ──────────────────────────────────────────────────

    private (string Name, List<RoleGrant> Grants) Validate(SaveRoleRequest request)
    {
        string name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            throw new ArgumentException("Название роли: от 1 до 100 символов.");

        var grants = new Dictionary<string, RoleGrant>(StringComparer.Ordinal);
        foreach (var p in request.Permissions ?? [])
        {
            var def = catalog.Find(p.PermissionId)
                      ?? throw new ArgumentException($"Неизвестное право «{p.PermissionId}».");
            ScopeDefinition? scope = null;
            if (p.Scope is not null)
            {
                if (!def.Scopable)
                    throw new ArgumentException($"Право «{def.Id}» не поддерживает область.");
                var items = p.Scope.Select(i => i?.Trim() ?? "").Where(i => i.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (items.Count == 0)
                    throw new ArgumentException($"Область права «{def.Id}» пуста (уберите область, чтобы дать право без ограничений).");
                scope = new ScopeDefinition(items);
                var provider = catalog.ModuleOf(def.Id)?.Scope;
                if (provider is not null)
                    scope = provider.Normalize(scope);
            }
            grants[def.Id] = new RoleGrant(def.Id, scope);
        }
        return (name, grants.Values.ToList());
    }

    private void Demand(IAccessContext actor, IEnumerable<RoleGrant> grants)
    {
        var violations = DelegationGuard.Violations(actor.Permissions, grants, catalog);
        if (violations.Count > 0)
            throw new AccessDeniedException("Недостаточно прав для этой роли.", violations);
    }

    private IEnumerable<RoleGrant> GrantsOf(RoleEntity role)
        => role.Id == BuiltInRoles.AdministratorId
            ? DelegationGuard.AdministratorGrants(catalog)
            : RoleMapping.ToSnapshot(role).Grants;

    private async Task<RoleEntity> LoadRoleAsync(string id, CancellationToken ct)
        => await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct)
           ?? throw new KeyNotFoundException("Роль не найдена.");

    private async Task EnsureUniqueNameAsync(string name, string? exceptId, CancellationToken ct)
    {
        string lower = name.ToLower();
        if (await db.Roles.AnyAsync(r => r.Name.ToLower() == lower && r.Id != exceptId, ct)
            || db.Roles.Local.Any(r => r.Id != exceptId && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) && db.Entry(r).State == EntityState.Added))
            throw new InvalidOperationException($"Роль «{name}» уже существует.");
    }

    private async Task<string> ResolvePrincipalAsync(CreateAssignmentRequest request, CancellationToken ct)
    {
        switch (request.PrincipalType)
        {
            case PrincipalType.LocalUser:
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.PrincipalId, ct)
                           ?? throw new ArgumentException("Пользователь не найден.");
                return user.Login;
            case PrincipalType.ApiKey:
                var key = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == request.PrincipalId, ct)
                          ?? throw new ArgumentException("API-ключ не найден.");
                return key.Name;
            default:
                if (!request.PrincipalId.StartsWith("S-1-", StringComparison.Ordinal))
                    throw new ArgumentException("Для учётной записи AD нужен SID (S-1-…).");
                return string.IsNullOrWhiteSpace(request.DisplayName) ? request.PrincipalId : request.DisplayName.Trim();
        }
    }

    /// <summary>Активные назначения «Администратор»: пользователь включён, ключ не отозван и не истёк, AD — всегда.</summary>
    private async Task<int> CountActiveAdministratorsAsync(string? excludeAssignmentId, CancellationToken ct,
        (string Type, string Id)? excludePrincipal = null)
    {
        var rows = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == BuiltInRoles.AdministratorId && a.Id != excludeAssignmentId).ToListAsync(ct);
        int active = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var a in rows)
        {
            if (excludePrincipal is { } ex && a.PrincipalType == ex.Type && a.PrincipalId == ex.Id) continue;
            bool isActive = a.PrincipalType switch
            {
                nameof(PrincipalType.LocalUser) => await db.Users.AnyAsync(u => u.Id == a.PrincipalId && u.IsActive, ct),
                nameof(PrincipalType.ApiKey) => (await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == a.PrincipalId, ct))
                    is { IsRevoked: false } k && (k.ExpiresAt is null || k.ExpiresAt > now),
                _ => true,
            };
            if (isActive) active++;
        }
        return active;
    }

    private Task AuditAsync(IAccessContext actor, string action, string target, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto { Actor = actor.Actor, Action = action, Target = target, Success = true }, ct);

    private RoleDto ToDto(RoleEntity r, int assignments)
    {
        var grants = r.Id == BuiltInRoles.AdministratorId
            ? catalog.All.Select(p => new RoleGrantDto(p.Id, null)).ToList()
            : r.Permissions.OrderBy(p => p.PermissionId)
                .Select(p => new RoleGrantDto(p.PermissionId, ScopeJson.Parse(p.ScopeJson)?.Items)).ToList();
        return new RoleDto(r.Id, r.Name, r.Description, r.IsBuiltin, grants, assignments);
    }

    private static RoleAssignmentDto ToDto(RoleAssignmentEntity a) => new(
        a.Id, a.RoleId, a.Role.Name, Enum.Parse<PrincipalType>(a.PrincipalType), a.PrincipalId, a.DisplayName, a.CreatedAt);
}
```

Примечание: в `ApiKey`-ветке `CountActiveAdministratorsAsync` сравнение `ExpiresAt > now` выполняется в памяти (сущность уже загружена) — так одинаково работает для SQLite (тики) и PostgreSQL.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~RoleServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/backend/WinAdmin.Core src/backend/WinAdmin.Infrastructure/Access src/tests/WinAdmin.Tests/RoleServiceTests.cs
git commit -m "feat(access): manage roles and assignments with delegation and admin lockout guard"
```

---

### Task 6: Перенос scopes в роли при старте

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Access/LegacyScopes.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Access/PlatformBootstrapper.cs`
- Test: `src/tests/WinAdmin.Tests/PlatformBootstrapperTests.cs`

**Interfaces:**
- Consumes: сущности Task 3; `PermissionCatalog`, `PermissionIds`, `BuiltInRoles`, `PrincipalType`.
- Produces:
  - `static class LegacyScopes { Task AssignAsync(WinAdminDbContext db, PermissionCatalog catalog, PrincipalType type, string principalId, string displayName, IEnumerable<string> scopes, string createdBy, CancellationToken ct = default) }` — `admin` → «Администратор»; иначе роль «Импорт: …» (переиспользуется); `disks.read` → `system.read`; не сохраняет (вызывающий делает `SaveChangesAsync`)
  - `static class PlatformBootstrapper { Task RunAsync(WinAdminDbContext db, PermissionCatalog catalog, CancellationToken ct = default) }` — создаёт «Администратор», переносит непустые `Scopes` пользователей и ключей в роли и очищает `Scopes`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/PlatformBootstrapperTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class PlatformBootstrapperTests : IDisposable
{
    private static readonly PermissionCatalog Catalog = new(BuiltInModules.All);
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;

    public PlatformBootstrapperTests()
    {
        _connection.Open();
        _db = new SqliteWinAdminDbContext(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Existing_admins_become_administrators_and_rerun_changes_nothing()
    {
        foreach (var login in new[] { "admin", "iadmin", "iadmin1" })
            _db.Users.Add(new UserEntity { Id = login, Login = login, PasswordHash = "x", Scopes = "admin" });
        _db.ApiKeys.Add(new ApiKeyEntity { Id = "boot", Name = "bootstrap-admin", KeyHash = "h", Scopes = "admin" });
        await _db.SaveChangesAsync();

        await PlatformBootstrapper.RunAsync(_db, Catalog);
        await PlatformBootstrapper.RunAsync(_db, Catalog);

        var admins = await _db.RoleAssignments.Where(a => a.RoleId == BuiltInRoles.AdministratorId).ToListAsync();
        Assert.Equal(4, admins.Count);
        Assert.Equal(1, await _db.Roles.CountAsync(r => r.Id == BuiltInRoles.AdministratorId && r.IsBuiltin));
        Assert.All(await _db.Users.ToListAsync(), u => Assert.Equal("", u.Scopes));
        Assert.Equal("", (await _db.ApiKeys.SingleAsync()).Scopes);
    }

    [Fact]
    public async Task Same_scope_sets_share_one_imported_role()
    {
        _db.Users.Add(new UserEntity { Id = "a", Login = "a", PasswordHash = "x", Scopes = "services.read,disks.read" });
        _db.ApiKeys.Add(new ApiKeyEntity { Id = "k", Name = "mon", KeyHash = "h", Scopes = "disks.read, services.read,bogus.scope" });
        await _db.SaveChangesAsync();

        await PlatformBootstrapper.RunAsync(_db, Catalog);

        var imported = await _db.Roles.Include(r => r.Permissions).SingleAsync(r => !r.IsBuiltin);
        Assert.Equal("Импорт: services.read, system.read", imported.Name);
        Assert.Equal(new[] { "services.read", "system.read" }, imported.Permissions.Select(p => p.PermissionId).Order());
        Assert.Equal(2, await _db.RoleAssignments.CountAsync(a => a.RoleId == imported.Id));
    }

    [Fact]
    public async Task Unknown_only_scopes_assign_nothing_but_are_cleared()
    {
        _db.Users.Add(new UserEntity { Id = "z", Login = "z", PasswordHash = "x", Scopes = "bogus.scope" });
        await _db.SaveChangesAsync();

        await PlatformBootstrapper.RunAsync(_db, Catalog);

        Assert.Empty(await _db.RoleAssignments.ToListAsync());
        Assert.Equal("", (await _db.Users.SingleAsync()).Scopes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~PlatformBootstrapperTests`
Expected: build FAIL — `PlatformBootstrapper` не найден.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Infrastructure/Access/LegacyScopes.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

/// <summary>Старые scopes → роли: admin → «Администратор», остальное → «Импорт: …» (одна роль на набор).</summary>
public static class LegacyScopes
{
    public static async Task AssignAsync(WinAdminDbContext db, PermissionCatalog catalog, PrincipalType type,
        string principalId, string displayName, IEnumerable<string> scopes, string createdBy, CancellationToken ct = default)
    {
        var list = scopes.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        string roleId;
        if (list.Contains("admin", StringComparer.OrdinalIgnoreCase))
        {
            roleId = BuiltInRoles.AdministratorId;
        }
        else
        {
            var permissions = list
                .Select(s => s == "disks.read" ? PermissionIds.SystemRead : s)
                .Where(s => catalog.Find(s) is not null)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            if (permissions.Count == 0)
                return;

            string name = "Импорт: " + string.Join(", ", permissions);
            var role = db.Roles.Local.FirstOrDefault(r => r.Name == name)
                       ?? await db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);
            if (role is null)
            {
                role = new RoleEntity { Name = name, Description = "Создана из прежних scopes при обновлении WinAdmin" };
                foreach (var p in permissions)
                    role.Permissions.Add(new RolePermissionEntity { PermissionId = p });
                db.Roles.Add(role);
            }
            roleId = role.Id;
        }

        string t = type.ToString();
        bool exists = db.RoleAssignments.Local.Any(a => a.RoleId == roleId && a.PrincipalType == t && a.PrincipalId == principalId)
                      || await db.RoleAssignments.AnyAsync(a => a.RoleId == roleId && a.PrincipalType == t && a.PrincipalId == principalId, ct);
        if (!exists)
        {
            db.RoleAssignments.Add(new RoleAssignmentEntity
            {
                RoleId = roleId, PrincipalType = t, PrincipalId = principalId, DisplayName = displayName, CreatedBy = createdBy,
            });
        }
    }
}
```

`src/backend/WinAdmin.Infrastructure/Access/PlatformBootstrapper.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

/// <summary>
/// При каждом старте: встроенная роль «Администратор» и перенос непустых старых scopes
/// пользователей и ключей в роли (после переноса колонка Scopes очищается — повтор ничего не делает).
/// </summary>
public static class PlatformBootstrapper
{
    public static async Task RunAsync(WinAdminDbContext db, PermissionCatalog catalog, CancellationToken ct = default)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == BuiltInRoles.AdministratorId, ct))
        {
            db.Roles.Add(new RoleEntity
            {
                Id = BuiltInRoles.AdministratorId,
                Name = BuiltInRoles.AdministratorName,
                Description = "Все права, включая права будущих модулей",
                IsBuiltin = true,
            });
            await db.SaveChangesAsync(ct);
        }

        foreach (var user in await db.Users.Where(u => u.Scopes != "").ToListAsync(ct))
        {
            await LegacyScopes.AssignAsync(db, catalog, PrincipalType.LocalUser, user.Id, user.Login,
                user.Scopes.Split(','), "migration", ct);
            user.Scopes = "";
        }
        foreach (var key in await db.ApiKeys.Where(k => k.Scopes != "").ToListAsync(ct))
        {
            await LegacyScopes.AssignAsync(db, catalog, PrincipalType.ApiKey, key.Id, key.Name,
                key.Scopes.Split(','), "migration", ct);
            key.Scopes = "";
        }
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~PlatformBootstrapperTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/Access src/tests/WinAdmin.Tests/PlatformBootstrapperTests.cs
git commit -m "feat(access): migrate legacy scopes to roles on startup"
```

---

### Task 7: Реестр модулей — включение, требования, настройки с секретами

**Files:**
- Create: `src/backend/WinAdmin.Core/Abstractions/IMachineInfo.cs`
- Create: `src/backend/WinAdmin.Core/Models/ModuleModels.cs`
- Create: `src/backend/WinAdmin.Core/Abstractions/IModuleRegistry.cs`
- Create: `src/backend/WinAdmin.Infrastructure/MachineInfo/WmiMachineInfo.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Modules/ModuleSettingsSchema.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Modules/ModuleRegistry.cs`
- Test: `src/tests/WinAdmin.Tests/ModuleRegistryTests.cs`

**Interfaces:**
- Consumes: `IWinAdminModule`, `PermissionCatalog`, `SecretAttribute`, `ModuleRequirements` (Task 1); `ModuleStateEntity` (Task 3); `IAccessService` (Task 4); `ISecretProtector` (1a); `IAuditService`.
- Produces:
  - `interface IMachineInfo { bool IsWindowsServer { get; } bool IsDomainJoined { get; } }`, `sealed class WmiMachineInfo : IMachineInfo`
  - `sealed record ModuleState(string Id, bool Enabled, bool Available, string? UnavailableReason)`
  - `sealed record SettingsField(string Name, string Title, string Kind)` (Kind: `string|number|boolean|stringList|secret`)
  - `interface IModuleRegistry { IReadOnlyList<IWinAdminModule> Modules; ModuleState GetState(string id); Task SetEnabledAsync(string id, bool enabled, string actor, CancellationToken ct = default); IReadOnlyList<SettingsField> GetSettingsSchema(string id); Task<JsonObject> GetSettingsViewAsync(string id, CancellationToken ct = default); Task SaveSettingsAsync(string id, JsonObject settings, string actor, CancellationToken ct = default); Task<T> GetSettingsAsync<T>(string id, CancellationToken ct = default) where T : class, new(); }`
  - `static class ModuleSettingsSchema { IReadOnlyList<SettingsField> Build(Type settingsType) }`
  - `sealed class ModuleRegistry(PermissionCatalog, IMachineInfo, IServiceScopeFactory, ISecretProtector, IAccessService) : IModuleRegistry` (singleton)

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/ModuleRegistryTests.cs`:

```csharp
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DirectoryTestSettings
{
    [Description("Служебная учётка")]
    public string ServiceUser { get; set; } = "";

    [Secret, Description("Пароль служебной учётки")]
    public string ServicePassword { get; set; } = "";

    public int Port { get; set; } = 389;
    public bool UseLdaps { get; set; }
    public List<string> HiddenOus { get; set; } = [];
}

internal sealed class ServerOnlyModule(ModuleRequirements requirements, bool enabledByDefault = true) : IWinAdminModule
{
    public string Id => "dir";
    public string Title => "Каталог";
    public string? Description => null;
    public ModuleRequirements Requirements => requirements;
    public bool EnabledByDefault => enabledByDefault;
    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [new("dir.read", "Чтение")];
    public Type? SettingsType => typeof(DirectoryTestSettings);
    public IScopeProvider? Scope => null;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
}

public sealed class ModuleRegistryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly Mock<IMachineInfo> _machine = new();
    private readonly Mock<IAccessService> _access = new();
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));

    public ModuleRegistryTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(Mock.Of<IAuditService>())
            .BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private ModuleRegistry Registry(IWinAdminModule module) => new(
        new PermissionCatalog([module]), _machine.Object, _sp.GetRequiredService<IServiceScopeFactory>(), _protector, _access.Object);

    private string? StoredJson()
    {
        using var scope = _sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().ModuleStates.Single().SettingsJson;
    }

    [Fact]
    public async Task Toggle_persists_and_survives_a_new_registry_instance()
    {
        var module = new ServerOnlyModule(ModuleRequirements.None);
        Assert.True(Registry(module).GetState("dir").Enabled);

        await Registry(module).SetEnabledAsync("dir", false, "admin");

        Assert.False(Registry(module).GetState("dir").Enabled);
        _access.Verify(a => a.Invalidate(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Unmet_requirements_make_module_unavailable_and_not_enableable()
    {
        _machine.SetupGet(m => m.IsWindowsServer).Returns(false);
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.WindowsServer));

        var state = registry.GetState("dir");
        Assert.False(state.Available);
        Assert.False(state.Enabled);
        Assert.Contains("Windows Server", state.UnavailableReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetEnabledAsync("dir", true, "admin"));
    }

    [Fact]
    public void Schema_describes_fields_and_secrets()
    {
        var schema = ModuleSettingsSchema.Build(typeof(DirectoryTestSettings));
        Assert.Equal(
            new[] { "serviceUser:string", "servicePassword:secret", "port:number", "useLdaps:boolean", "hiddenOus:stringList" },
            schema.Select(f => $"{f.Name}:{f.Kind}"));
        Assert.Equal("Пароль служебной учётки", schema[1].Title);
    }

    [Fact]
    public async Task Secret_is_encrypted_masked_and_kept_when_not_resent()
    {
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.None));
        await registry.SaveSettingsAsync("dir", new JsonObject
        {
            ["serviceUser"] = "svc_access", ["servicePassword"] = "P@ss;1", ["port"] = 636,
        }, "admin");

        Assert.DoesNotContain("P@ss;1", StoredJson());
        Assert.Contains("enc:v1:", StoredJson());

        var view = await registry.GetSettingsViewAsync("dir");
        Assert.Equal("svc_access", view["serviceUser"]!.GetValue<string>());
        Assert.True(view["servicePassword"]!["isSet"]!.GetValue<bool>());

        // Пароль не прислан — остаётся прежним.
        await registry.SaveSettingsAsync("dir", new JsonObject { ["serviceUser"] = "svc2", ["servicePassword"] = "" }, "admin");
        var typed = await registry.GetSettingsAsync<DirectoryTestSettings>("dir");
        Assert.Equal("svc2", typed.ServiceUser);
        Assert.Equal("P@ss;1", typed.ServicePassword);
        Assert.Equal(636, typed.Port);
    }

    [Fact]
    public async Task Wrong_types_are_rejected()
    {
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.SaveSettingsAsync("dir", new JsonObject { ["port"] = "not a number" }, "admin"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~ModuleRegistryTests`
Expected: build FAIL — `ModuleRegistry` не найден.

- [ ] **Step 3: Implement contracts**

`src/backend/WinAdmin.Core/Abstractions/IMachineInfo.cs`:

```csharp
namespace WinAdmin.Core.Abstractions;

/// <summary>Свойства машины для требований модулей.</summary>
public interface IMachineInfo
{
    bool IsWindowsServer { get; }
    bool IsDomainJoined { get; }
}
```

`src/backend/WinAdmin.Core/Models/ModuleModels.cs`:

```csharp
namespace WinAdmin.Core.Models;

public sealed record ModuleState(string Id, bool Enabled, bool Available, string? UnavailableReason);

/// <summary>Поле настроек модуля для формы UI. Kind: string | number | boolean | stringList | secret.</summary>
public sealed record SettingsField(string Name, string Title, string Kind);
```

`src/backend/WinAdmin.Core/Abstractions/IModuleRegistry.cs`:

```csharp
using System.Text.Json.Nodes;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Abstractions;

/// <summary>Состояние и настройки модулей (KeyNotFoundException — нет модуля, ArgumentException — неверные настройки).</summary>
public interface IModuleRegistry
{
    IReadOnlyList<IWinAdminModule> Modules { get; }
    ModuleState GetState(string id);
    Task SetEnabledAsync(string id, bool enabled, string actor, CancellationToken ct = default);
    IReadOnlyList<SettingsField> GetSettingsSchema(string id);

    /// <summary>Настройки для UI: секреты — { "isSet": bool }.</summary>
    Task<JsonObject> GetSettingsViewAsync(string id, CancellationToken ct = default);

    /// <summary>Пустой или отсутствующий секрет — оставить прежний.</summary>
    Task SaveSettingsAsync(string id, JsonObject settings, string actor, CancellationToken ct = default);

    /// <summary>Настройки с расшифрованными секретами (недоступный секрет — пустая строка).</summary>
    Task<T> GetSettingsAsync<T>(string id, CancellationToken ct = default) where T : class, new();
}
```

- [ ] **Step 4: Implement machine info and schema**

`src/backend/WinAdmin.Infrastructure/MachineInfo/WmiMachineInfo.cs`:

```csharp
using System.Management;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.MachineInfo;

/// <summary>WMI: Win32_OperatingSystem.ProductType (2 — контроллер домена, 3 — сервер), Win32_ComputerSystem.PartOfDomain.</summary>
public sealed class WmiMachineInfo : IMachineInfo
{
    private readonly Lazy<bool> _server = new(() => Query("SELECT ProductType FROM Win32_OperatingSystem", "ProductType") is uint t && t is 2 or 3);
    private readonly Lazy<bool> _domain = new(() => Query("SELECT PartOfDomain FROM Win32_ComputerSystem", "PartOfDomain") is true);

    public bool IsWindowsServer => _server.Value;
    public bool IsDomainJoined => _domain.Value;

    private static object? Query(string wql, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject mo in searcher.Get())
                using (mo) return mo[property];
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
```

`src/backend/WinAdmin.Infrastructure/Modules/ModuleSettingsSchema.cs`:

```csharp
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;

namespace WinAdmin.Infrastructure.Modules;

public static class ModuleSettingsSchema
{
    public static IReadOnlyList<SettingsField> Build(Type settingsType)
        => settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => new SettingsField(
                JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? p.Name,
                KindOf(p)))
            .ToList();

    public static bool IsSecret(PropertyInfo p) => p.GetCustomAttribute<SecretAttribute>() is not null;

    private static string KindOf(PropertyInfo p)
    {
        if (IsSecret(p)) return "secret";
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(decimal)) return "number";
        if (t != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(t)) return "stringList";
        return "string";
    }
}
```

- [ ] **Step 5: Implement the registry**

`src/backend/WinAdmin.Infrastructure/Modules/ModuleRegistry.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Modules;

/// <summary>Singleton: состояние модулей кэшируется в памяти и перечитывается после изменений.</summary>
public sealed class ModuleRegistry(
    PermissionCatalog catalog, IMachineInfo machine, IServiceScopeFactory scopes,
    ISecretProtector protector, IAccessService access) : IModuleRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private Dictionary<string, ModuleStateEntity>? _rows;

    public IReadOnlyList<IWinAdminModule> Modules => catalog.Modules;

    public ModuleState GetState(string id)
    {
        var module = Find(id);
        var (available, reason) = Check(module);
        bool enabled = available && (Rows().TryGetValue(id, out var row) ? row.Enabled : module.EnabledByDefault);
        return new ModuleState(id, enabled, available, reason);
    }

    public async Task SetEnabledAsync(string id, bool enabled, string actor, CancellationToken ct = default)
    {
        var module = Find(id);
        var (available, reason) = Check(module);
        if (enabled && !available)
            throw new InvalidOperationException($"Модуль «{module.Title}» недоступен на этой машине: {reason}");

        await UpsertAsync(id, row => row.Enabled = enabled, actor, ct);
        await AuditAsync(actor, enabled ? "module.enable" : "module.disable", id, null, ct);
    }

    public IReadOnlyList<SettingsField> GetSettingsSchema(string id)
        => Find(id).SettingsType is { } t ? ModuleSettingsSchema.Build(t) : [];

    public Task<JsonObject> GetSettingsViewAsync(string id, CancellationToken ct = default)
    {
        var module = Find(id);
        var stored = StoredSettings(id);
        var view = new JsonObject();
        if (module.SettingsType is null) return Task.FromResult(view);
        foreach (var p in Properties(module.SettingsType))
        {
            string name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (ModuleSettingsSchema.IsSecret(p))
                view[name] = new JsonObject { ["isSet"] = stored[name] is JsonValue v && v.GetValue<string>().Length > 0 };
            else
                view[name] = stored[name]?.DeepClone();
        }
        return Task.FromResult(view);
    }

    public async Task SaveSettingsAsync(string id, JsonObject settings, string actor, CancellationToken ct = default)
    {
        var module = Find(id);
        if (module.SettingsType is null)
            throw new ArgumentException($"У модуля «{module.Title}» нет настроек.");

        var merged = StoredSettings(id);
        var changed = new List<string>();
        foreach (var p in Properties(module.SettingsType))
        {
            string name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!settings.TryGetPropertyValue(name, out var incoming))
                continue;
            if (ModuleSettingsSchema.IsSecret(p))
            {
                if (incoming is JsonValue sv && sv.TryGetValue<string>(out var secret) && secret.Length > 0)
                {
                    merged[name] = protector.Protect(secret, Purpose(id, name));
                    changed.Add(name);
                }
                continue;
            }
            merged[name] = incoming?.DeepClone();
            changed.Add(name);
        }

        // Проверка типов: секреты подставляем пустыми строками.
        var probe = (JsonObject)merged.DeepClone();
        foreach (var p in Properties(module.SettingsType).Where(ModuleSettingsSchema.IsSecret))
            probe[JsonNamingPolicy.CamelCase.ConvertName(p.Name)] = "";
        try
        {
            probe.Deserialize(module.SettingsType, Json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Неверные настройки модуля «{module.Title}»: {ex.Message}");
        }

        await UpsertAsync(id, row => row.SettingsJson = merged.ToJsonString(), actor, ct);
        await AuditAsync(actor, "module.settings.update", id, string.Join(", ", changed), ct);
    }

    public Task<T> GetSettingsAsync<T>(string id, CancellationToken ct = default) where T : class, new()
    {
        var stored = StoredSettings(id);
        foreach (var p in Properties(typeof(T)).Where(ModuleSettingsSchema.IsSecret))
        {
            string name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            string? value = stored[name] is JsonValue v ? v.GetValue<string>() : null;
            try
            {
                stored[name] = string.IsNullOrEmpty(value) ? "" : protector.Unprotect(value, Purpose(id, name));
            }
            catch (SecretUnavailableException)
            {
                stored[name] = "";
            }
        }
        return Task.FromResult(stored.Deserialize<T>(Json) ?? new T());
    }

    // ── helpers ──────────────────────────────────────────────────

    private IWinAdminModule Find(string id)
        => catalog.FindModule(id) ?? throw new KeyNotFoundException($"Модуль «{id}» не найден.");

    private (bool Available, string? Reason) Check(IWinAdminModule module)
    {
        if (module.Requirements.HasFlag(ModuleRequirements.WindowsServer) && !machine.IsWindowsServer)
            return (false, "нужен Windows Server");
        if (module.Requirements.HasFlag(ModuleRequirements.DomainJoined) && !machine.IsDomainJoined)
            return (false, "компьютер не входит в домен");
        return (true, null);
    }

    private static string Purpose(string moduleId, string field) => $"module:{moduleId}:{field}";

    private static IEnumerable<PropertyInfo> Properties(Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

    private JsonObject StoredSettings(string id)
        => Rows().TryGetValue(id, out var row) && !string.IsNullOrWhiteSpace(row.SettingsJson)
            ? JsonNode.Parse(row.SettingsJson) as JsonObject ?? new JsonObject()
            : new JsonObject();

    private Dictionary<string, ModuleStateEntity> Rows()
    {
        lock (_lock)
        {
            if (_rows is not null) return _rows;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            return _rows = db.ModuleStates.AsNoTracking().ToDictionary(r => r.Id, StringComparer.Ordinal);
        }
    }

    private async Task UpsertAsync(string id, Action<ModuleStateEntity> change, string actor, CancellationToken ct)
    {
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            var row = await db.ModuleStates.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (row is null)
            {
                row = new ModuleStateEntity { Id = id, Enabled = Find(id).EnabledByDefault };
                db.ModuleStates.Add(row);
            }
            change(row);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = actor;
            await db.SaveChangesAsync(ct);
        }
        lock (_lock) _rows = null;
        access.Invalidate();
    }

    private async Task AuditAsync(string actor, string action, string target, string? details, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(
            new AuditEntryDto { Actor = actor, Action = action, Target = target, Success = true, Details = details }, ct);
    }
}
```

(Нужны `using WinAdmin.Core.Abstractions;` — уже есть — для `ISecretProtector`, `SecretUnavailableException`, `IAuditService`; `AuditEntryDto` — `WinAdmin.Core.Models`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~ModuleRegistryTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/backend/WinAdmin.Core src/backend/WinAdmin.Infrastructure/MachineInfo/WmiMachineInfo.cs src/backend/WinAdmin.Infrastructure/Modules src/tests/WinAdmin.Tests/ModuleRegistryTests.cs
git commit -m "feat(modules): module registry with requirements and encrypted settings"
```

---

### Task 8: Авторизация по правам, субъект в токене, скрытие выключенных модулей

**Files:**
- Create: `src/backend/WinAdmin.Api/Auth/PermissionAuthorization.cs`
- Create: `src/backend/WinAdmin.Api/Auth/AccessContextFactory.cs`
- Create: `src/backend/WinAdmin.Api/Modules/ModuleAvailability.cs`
- Modify: `src/backend/WinAdmin.Core/Models/SecurityModels.cs`, `UserModels.cs` (без scopes)
- Modify: `src/backend/WinAdmin.Core/Abstractions/IUserService.cs` (убрать `UpdateScopesAsync`)
- Modify: `src/backend/WinAdmin.Infrastructure/Security/{UserService,ApiKeyService,TokenService}.cs`
- Modify: `src/backend/WinAdmin.Api/Auth/ApiKeyAuthenticationHandler.cs`, `Auth/ScopeAuthorization.cs` (удалить)
- Modify: все контроллеры с `Policy = "scope:…"`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`, `src/backend/WinAdmin.Api/Program.cs`
- Delete: `src/backend/WinAdmin.Core/Security/Scopes.cs`
- Modify tests: `ApiKeyServiceTests.cs`, `UserServiceTests.cs`, `TokenServiceTests.cs`, `NetworkSettingsApiTests.cs`
- Test: `src/tests/WinAdmin.Tests/PermissionApiTests.cs`

**Interfaces:**
- Consumes: Tasks 1–7.
- Produces:
  - `sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute` (policy `perm:<id>`)
  - `sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider`, `sealed class PermissionAuthorizationHandler(IAccessService)`
  - `sealed class AccessContextFactory(IAccessService)` с `Task<IAccessContext?> CreateAsync(ClaimsPrincipal user, CancellationToken ct = default)`
  - `sealed class WinAdminModuleAttribute(string moduleId) : Attribute`, `sealed class ModuleAvailabilityMiddleware`
  - DTO без scopes: `UserDto { Id, Login, CreatedAt, IsActive, Roles }`, `CreateUserRequest { Login, Password, RoleIds }`, `UserPrincipal { Id, Login }`, `ApiKeyDto { …, Roles }`, `CreateApiKeyRequest { Name, RoleIds, ExpiresAt }`, `ApiKeyPrincipal { Id, Name }`
  - `AddWinAdminInfrastructure` регистрирует: `PermissionCatalog` (по `BuiltInModules.All`), `IAccessService`→`AccessService` (singleton), `IRoleService`→`RoleService` (scoped), `IMachineInfo`→`WmiMachineInfo`, `IModuleRegistry`→`ModuleRegistry` (singleton)
  - Тестовый помощник `NetworkApiFactory.ClientWithPermissionsAsync(params string[] permissions)` и `ClientAsAdministratorAsync()`

- [ ] **Step 1: Write the failing API tests**

`src/tests/WinAdmin.Tests/PermissionApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class PermissionApiTests(NetworkApiFactory factory)
{
    [Fact]
    public async Task Permission_from_role_opens_endpoint_and_its_absence_returns_403()
    {
        var reader = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/v1/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/v1/processes")).StatusCode);
    }

    [Fact]
    public async Task Disabled_module_returns_404_even_without_permission()
    {
        var registry = factory.Services.GetRequiredService<IModuleRegistry>();
        await registry.SetEnabledAsync("printers", false, "test");
        try
        {
            var admin = await factory.ClientAsAdministratorAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/printers")).StatusCode);
            var nobody = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
            Assert.Equal(HttpStatusCode.NotFound, (await nobody.GetAsync("/api/v1/printers")).StatusCode);
        }
        finally
        {
            await registry.SetEnabledAsync("printers", true, "test");
        }
    }

    [Fact]
    public async Task Legacy_jwt_without_principal_claim_still_works()
    {
        // Пользователь-администратор и токен в старом формате (только NameIdentifier/Name).
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            var created = await users.CreateAsync(new() { Login = "legacy-" + Guid.NewGuid().ToString("N")[..6], Password = "Legacy-123456" });
            userId = created.Id;
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            await roles.AssignAsync(new(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, userId, null), factory.SystemActor());
        }
        string token = factory.LegacyAccessToken(userId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/services")).StatusCode);
    }
}
```

В `NetworkSettingsApiTests.cs` (класс `NetworkApiFactory`) заменить метод `ClientWithScopesAsync` на:

```csharp
    /// <summary>Клиент с API-ключом, которому назначена роль с перечисленными правами.</summary>
    public async Task<HttpClient> ClientWithPermissionsAsync(params string[] permissions)
    {
        using var scope = Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var created = await keys.CreateAsync(new CreateApiKeyRequest { Name = "t-" + Guid.NewGuid().ToString("N") });
        var role = await roles.CreateAsync(new SaveRoleRequest("r-" + Guid.NewGuid().ToString("N"), null,
            [.. permissions.Select(p => new RoleGrantDto(p, null))]), SystemActor());
        await roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.ApiKey, created.Key.Id, null), SystemActor());
        return WithKey(created.PlaintextKey);
    }

    public async Task<HttpClient> ClientAsAdministratorAsync()
    {
        using var scope = Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var created = await keys.CreateAsync(new CreateApiKeyRequest { Name = "adm-" + Guid.NewGuid().ToString("N") });
        await roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.ApiKey, created.Key.Id, null), SystemActor());
        return WithKey(created.PlaintextKey);
    }

    /// <summary>Действующее лицо с полными правами (для подготовки данных в тестах).</summary>
    public IAccessContext SystemActor()
    {
        var catalog = Services.GetRequiredService<PermissionCatalog>();
        return new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "test", []), "test",
            PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
    }

    /// <summary>JWT в формате до 1b: без claim wa:principal.</summary>
    public string LegacyAccessToken(string userId)
    {
        var jwt = Services.GetRequiredService<JwtOptions>();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.Secret));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            jwt.Issuer, jwt.Audience,
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
             new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "legacy"),
             new System.Security.Claims.Claim("scope", "admin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient WithKey(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        return client;
    }
```

и добавить `using WinAdmin.Core.Models; using WinAdmin.Core.Modules; using WinAdmin.Core.Security;` в начало файла. В тестах класса `NetworkSettingsApiTests` заменить `factory.ClientWithScopesAsync("admin")` на `factory.ClientAsAdministratorAsync()`, а `factory.ClientWithScopesAsync("system.read")` на `factory.ClientWithPermissionsAsync(PermissionIds.SystemRead)`.

Обновить старые юнит-тесты сервисов под DTO без scopes:
- `ApiKeyServiceTests.cs`: удалить `Create_filters_unknown_scopes`; в остальных создавать ключ как `new CreateApiKeyRequest { Name = "…", ExpiresAt = … }` (без `Scopes`); проверки `principal.Scopes` заменить на `Assert.Equal(created.Key.Id, principal!.Id)`; тест bootstrap проверяет, что у созданного ключа `Scopes == "admin"` в БД (`_db.ApiKeys.Single().Scopes`), — это маркер для переноса в роль при старте.
- `UserServiceTests.cs`: создавать `new CreateUserRequest { Login = …, Password = … }`; проверку `dto.Scopes` удалить.
- `TokenServiceTests.cs`: тест `GenerateAccessToken_ContainsLoginAndScopes` переименовать в `GenerateAccessToken_ContainsLoginAndPrincipal` и проверять claim `wa:principal` = `LocalUser:1` и отсутствие claim `scope`; в сущностях `UserEntity` убрать `Scopes = "admin"`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: build FAIL — `IRoleService`/`RoleGrantDto` используются тестами, `CreateApiKeyRequest` ещё содержит `Scopes`… (компиляция падает до реализации).

- [ ] **Step 3: DTOs without scopes**

`src/backend/WinAdmin.Core/Models/SecurityModels.cs`:
- `ApiKeyDto`: заменить `public IReadOnlyList<string> Scopes { get; init; } = [];` на `public IReadOnlyList<string> Roles { get; init; } = [];`
- `ApiKeyPrincipal`: удалить свойство `Scopes`.
- `CreateApiKeyRequest`: заменить `public List<string> Scopes { get; init; } = [];` на `public List<string> RoleIds { get; init; } = [];`

`src/backend/WinAdmin.Core/Models/UserModels.cs`:
- `UserDto`: `Scopes` → `public IReadOnlyList<string> Roles { get; init; } = [];`
- `CreateUserRequest`: `Scopes` → `public List<string> RoleIds { get; init; } = [];`
- удалить `UpdateScopesRequest`;
- `UserPrincipal`: удалить `Scopes`.

`src/backend/WinAdmin.Core/Abstractions/IUserService.cs`: удалить строку `Task<bool> UpdateScopesAsync(...)`.

Удалить `src/backend/WinAdmin.Core/Security/Scopes.cs`.

- [ ] **Step 4: Services without scopes**

`UserService.cs`:
- `CreateAsync`: тело — `var entity = new UserEntity { Login = request.Login.Trim() };` (без scopes), остальное как было.
- удалить `UpdateScopesAsync` и `NormalizeScopes`;
- `ToDto`: убрать строку `Scopes = …`; `ToPrincipal`: убрать строку `Scopes = …`;
- удалить `using WinAdmin.Core.Security;`, если больше не нужен.

`ApiKeyService.cs`:
- `CreateAsync`: убрать `var scopes = NormalizeScopes(request.Scopes);` и строку `Scopes = string.Join(',', scopes),`; лог — `"Создан API-ключ {Name} ({Id})"` с двумя параметрами;
- `ValidateAsync`: в результате убрать `Scopes = SplitScopes(entity.Scopes),`;
- `EnsureBootstrapAsync`: `Scopes = "admin",` (маркер: при старте `PlatformBootstrapper` назначит роль «Администратор» и очистит поле);
- удалить `NormalizeScopes`, `SplitScopes`; в `ToDto` убрать строку `Scopes = …`;
- удалить `using WinAdmin.Core.Security;`, если больше не нужен.

`TokenService.cs`:
- в `GenerateAccessToken` заменить `claims.AddRange(user.Scopes.Select(s => new Claim("scope", s)));` на
  `claims.Add(new Claim(PrincipalClaims.Type, PrincipalClaims.Format(PrincipalType.LocalUser, user.Id)));`
  (добавить `using WinAdmin.Core.Security;`);
- в `ValidateRefreshTokenAsync` убрать строку `Scopes = entity.User.Scopes.Split(...)`.

`ApiKeyAuthenticationHandler.cs`: заменить строку `claims.AddRange(principal.Scopes.Select(...));` на
`claims.Add(new Claim(PrincipalClaims.Type, PrincipalClaims.Format(PrincipalType.ApiKey, principal.Id)));`
(и `using WinAdmin.Core.Security;`); константу `ScopeClaimType` удалить.

Удалить файл `src/backend/WinAdmin.Api/Auth/ScopeAuthorization.cs`.

- [ ] **Step 5: Permission authorization**

`src/backend/WinAdmin.Api/Auth/PermissionAuthorization.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>[RequirePermission("services.manage")] — доступ по итоговым правам субъекта.</summary>
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicy.Prefix + permission)
{
    public string Permission { get; } = permission;
}

public static class PermissionPolicy
{
    public const string Prefix = "perm:";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Политики perm:* создаются на лету — не нужно регистрировать каждое право.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);
        var policy = new AuthorizationPolicyBuilder("Auto")
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

public sealed class PermissionAuthorizationHandler(IAccessService access) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var principal = PrincipalClaims.Parse(context.User, ApiKeyDefaults.Scheme);
        if (principal is null) return;
        var permissions = await access.GetAsync(principal);
        if (permissions.Has(requirement.Permission))
            context.Succeed(requirement);
    }
}
```

`src/backend/WinAdmin.Api/Auth/AccessContextFactory.cs`:

```csharp
using System.Security.Claims;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Auth;

/// <summary>Контекст доступа текущего запроса (кто и с какими итоговыми правами).</summary>
public sealed class AccessContextFactory(IAccessService access)
{
    public async Task<IAccessContext?> CreateAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var principal = PrincipalClaims.Parse(user, ApiKeyDefaults.Scheme);
        if (principal is null) return null;
        var permissions = await access.GetAsync(principal, ct);
        return new AccessContext(principal, user.Identity?.Name ?? principal.Key, permissions);
    }
}
```

`src/backend/WinAdmin.Api/Modules/ModuleAvailability.cs`:

```csharp
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Api.Modules;

/// <summary>Контроллер принадлежит модулю: выключенный или недоступный модуль — 404.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class WinAdminModuleAttribute(string moduleId) : Attribute
{
    public string ModuleId { get; } = moduleId;
}

/// <summary>Стоит после UseRouting и до UseAuthentication: 404 не раскрывает наличие функции.</summary>
public sealed class ModuleAvailabilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IModuleRegistry modules)
    {
        var module = context.GetEndpoint()?.Metadata.GetMetadata<WinAdminModuleAttribute>();
        if (module is not null && !modules.GetState(module.ModuleId).Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    }
}
```

- [ ] **Step 6: Switch controllers**

Во всех контроллерах заменить `[Authorize(Policy = "scope:" + Scopes.X)]` на `[RequirePermission(PermissionIds.Y)]` (`using WinAdmin.Api.Auth;`, `using WinAdmin.Api.Modules;`, `using WinAdmin.Core.Security;` уже есть или добавить) и добавить на класс `[WinAdminModule("…")]`:

| Контроллер | Было | Стало | Модуль |
|---|---|---|---|
| `SystemController` | `SystemRead` | `PermissionIds.SystemRead` | `system` |
| `DisksController` | `DisksRead` | `PermissionIds.SystemRead` | `system` |
| `ServicesController` | `ServicesRead` / `ServicesManage` | `PermissionIds.ServicesRead` / `ServicesManage` | `services` |
| `ProcessesController` | `ProcessesRead` / `ProcessesManage` | `PermissionIds.ProcessesRead` / `ProcessesManage` | `processes` |
| `PrintersController` | `PrintersRead` / `PrintersManage` | `PermissionIds.PrintersRead` / `PrintersManage` | `printers` |
| `PowerController` | `PowerManage` | `PermissionIds.PowerManage` | `power` |
| `EventLogsController` | `EventLogsRead` | `PermissionIds.EventLogsRead` | `eventlogs` |
| `ExcludedUsersController` | `Admin` | `PermissionIds.EventLogsManage` | `eventlogs` |
| `SoftwareController` | `SoftwareRead` / `SoftwareManage` | `PermissionIds.SoftwareRead` / `SoftwareManage` | `software` |
| `AuditController` | `Admin` | `PermissionIds.PlatformAuditRead` | — |
| `NetworkSettingsController` | `Admin` | `PermissionIds.PlatformNetworkManage` | — |
| `UsersController` | `Admin` | `PermissionIds.PlatformUsersManage` | — (Task 9) |
| `ApiKeysController` | `Admin` | `PermissionIds.PlatformApiKeysManage` | — (Task 9) |

В `UsersController` временно удалить действие `UpdateScopes` (полная переработка — Task 9). В `ApiKeysController` удалить действие `AvailableScopes`, а в `Create` в аудит писать `Details = null` (роли — Task 9). В `AuthController.Me` вернуть `{ login = User.Identity?.Name }` (полный `/api/v1/me` — Task 9).

- [ ] **Step 7: Register services and pipeline**

`src/backend/WinAdmin.Infrastructure/DependencyInjection.cs` — в конец `AddWinAdminInfrastructure` перед `return services;`:

```csharp
        services.AddSingleton(new PermissionCatalog(BuiltInModules.All));
        services.AddSingleton<IAccessService, AccessService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddSingleton<IMachineInfo, WmiMachineInfo>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
```

(`using WinAdmin.Core.Modules; using WinAdmin.Infrastructure.Access; using WinAdmin.Infrastructure.Modules;`; `WmiMachineInfo` — в `WinAdmin.Infrastructure.MachineInfo`, уже подключён.)

`Program.cs`:
1. Удалить блок `builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();` и цикл `foreach (var scope in Scopes.All) authzBuilder.AddPolicy(...)`; оставить `var authzBuilder = builder.Services.AddAuthorizationBuilder(); authzBuilder.SetDefaultPolicy(...)`.
2. Добавить после него:

```csharp
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<AccessContextFactory>();
```

3. В блоке «База данных + bootstrap» после `apiKeys.EnsureBootstrapAsync(...)`-логики и до проверки `users.AnyAsync()` добавить:

```csharp
    await PlatformBootstrapper.RunAsync(db, scope.ServiceProvider.GetRequiredService<PermissionCatalog>());
```

4. В конвейере заменить

```csharp
app.UseRateLimiter();
app.UseAuthentication();
```

на

```csharp
app.UseRouting();
app.UseMiddleware<ModuleAvailabilityMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
```

(`using WinAdmin.Api.Modules; using WinAdmin.Core.Modules; using WinAdmin.Infrastructure.Access;`; убрать `using WinAdmin.Core.Security;` только если он больше не используется.)

`Swagger`: `AddSecurityRequirement` не меняется.

- [ ] **Step 8: Run all tests**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS, включая `PermissionApiTests` (3).

- [ ] **Step 9: Commit**

```bash
git add -A src/backend src/tests/WinAdmin.Tests
git commit -m "feat(api): authorize by role permissions and hide disabled modules"
```

---

### Task 9: API ядра — /me, модули, права, роли, назначения, пользователи и ключи

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/PlatformErrorsAttribute.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/MeController.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/ModulesController.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/PermissionsController.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/RolesController.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/RoleAssignmentsController.cs`
- Modify: `src/backend/WinAdmin.Api/Controllers/UsersController.cs`, `ApiKeysController.cs`, `AuthController.cs` (удалить `Me`)
- Test: `src/tests/WinAdmin.Tests/PlatformApiTests.cs`

**Interfaces:**
- Consumes: Tasks 1–8.
- Produces (HTTP):
  - `GET /api/v1/me` → `{ actor, principal, permissions: { id: string[] | null }, modules: [{ id, title }] }`
  - `GET /api/v1/modules` (`platform.modules.manage`) → `[{ id, title, description, enabled, available, unavailableReason, scopable, scopeTitle, permissions: [{ id, title, description, scopable, dangerous }], settingsSchema: [{ name, title, kind }], settings }]`; `PUT /api/v1/modules/{id}` `{ enabled?: bool, settings?: object }` → 204
  - `GET /api/v1/permissions` (`platform.roles.manage`) → `[{ id: "platform"|moduleId, title, scopable, scopeTitle, permissions: [...] }]`
  - `GET/POST /api/v1/roles`, `PUT/DELETE /api/v1/roles/{id}` (`platform.roles.manage`)
  - `GET /api/v1/role-assignments?principalType=&principalId=&roleId=`, `POST`, `DELETE /{id}` (`platform.roles.manage`)
  - `/api/v1/users` — `UserDto.Roles`; `POST { login, password, roleIds }`; деактивация и удаление последнего администратора — 409
  - `/api/v1/apikeys` — `ApiKeyDto.Roles`; `POST { name, roleIds, expiresAt }`; отзыв последнего администратора — 409
  - Ошибки: 400 `{ message }`, 403 `{ message, violations }`, 404 `{ message }`, 409 `{ message }`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/PlatformApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class PlatformApiTests(NetworkApiFactory factory)
{
    private static async Task<JsonNode> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Me_lists_permissions_and_enabled_modules()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        var me = await Json(await client.GetAsync("/api/v1/me"));

        Assert.True(me["permissions"]!.AsObject().ContainsKey(PermissionIds.ServicesRead));
        Assert.Null(me["permissions"]![PermissionIds.ServicesRead]);
        Assert.Contains(me["modules"]!.AsArray(), m => m!["id"]!.GetValue<string>() == "services");
    }

    [Fact]
    public async Task Admin_manages_roles_and_assignments_end_to_end()
    {
        var admin = await factory.ClientAsAdministratorAsync();

        var created = await admin.PostAsJsonAsync("/api/v1/roles",
            new { name = "Наблюдатель-" + Guid.NewGuid().ToString("N")[..4], permissions = new[] { new { permissionId = PermissionIds.ServicesRead } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        string roleId = (await Json(created))["id"]!.GetValue<string>();

        var user = await admin.PostAsJsonAsync("/api/v1/users",
            new { login = "viewer-" + Guid.NewGuid().ToString("N")[..6], password = "Viewer-123456", roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var userJson = await Json(user);
        Assert.Contains(userJson["roles"]!.AsArray(), r => r!.GetValue<string>().StartsWith("Наблюдатель"));

        var assignments = await Json(await admin.GetAsync($"/api/v1/role-assignments?roleId={roleId}"));
        Assert.Single(assignments.AsArray());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/roles/{roleId}")).StatusCode);
    }

    [Fact]
    public async Task Delegate_gets_403_with_violations_when_escalating()
    {
        var delegateClient = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage, PermissionIds.ServicesRead);
        var r = await delegateClient.PostAsJsonAsync("/api/v1/roles",
            new { name = "Эскалация", permissions = new[] { new { permissionId = PermissionIds.PowerManage } } });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.NotEmpty((await Json(r))["violations"]!.AsArray());
    }

    [Fact]
    public async Task Modules_endpoint_lists_and_toggles()
    {
        var admin = await factory.ClientAsAdministratorAsync();
        var list = (await Json(await admin.GetAsync("/api/v1/modules"))).AsArray();
        Assert.Contains(list, m => m!["id"]!.GetValue<string>() == "power" && m["enabled"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/modules/power", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/power/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/modules/power", new { enabled = true })).StatusCode);
    }

    [Fact]
    public async Task Unknown_role_returns_404_and_bad_input_400()
    {
        var admin = await factory.ClientAsAdministratorAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/v1/roles/nope")).StatusCode);
        var bad = await admin.PostAsJsonAsync("/api/v1/roles", new { name = "", permissions = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~PlatformApiTests`
Expected: FAIL — 404 на `/api/v1/me`, `/api/v1/roles`, `/api/v1/modules`.

- [ ] **Step 3: Error mapping filter**

`src/backend/WinAdmin.Api/Controllers/PlatformErrorsAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Исключения сервисов ядра → HTTP: 400 / 403 (+violations) / 404 / 409.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlatformErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            AccessDeniedException e => new ObjectResult(new { message = e.Message, violations = e.Violations }) { StatusCode = 403 },
            KeyNotFoundException e => new NotFoundObjectResult(new { message = e.Message }),
            InvalidOperationException e => new ConflictObjectResult(new { message = e.Message }),
            ArgumentException e => new BadRequestObjectResult(new { message = e.Message }),
            _ => null,
        };
        if (context.Result is not null)
            context.ExceptionHandled = true;
    }
}
```

- [ ] **Step 4: /me, modules, permissions**

`src/backend/WinAdmin.Api/Controllers/MeController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Api.Controllers;

/// <summary>Кто я и что мне можно: права с областями и включённые модули.</summary>
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(AccessContextFactory contexts, IModuleRegistry modules) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var access = await contexts.CreateAsync(User, ct);
        if (access is null) return Unauthorized();
        return Ok(new
        {
            actor = access.Actor,
            principal = access.Principal.Key,
            permissions = access.Permissions.Map.ToDictionary(kv => kv.Key, kv => kv.Value.Items),
            modules = modules.Modules
                .Where(m => modules.GetState(m.Id).Enabled)
                .Select(m => new { id = m.Id, title = m.Title }),
        });
    }
}
```

`src/backend/WinAdmin.Api/Controllers/ModulesController.cs`:

```csharp
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Модули: состояние, включение, настройки.</summary>
[RequirePermission(PermissionIds.PlatformModulesManage)]
[PlatformErrors]
[Route("api/v1/modules")]
public sealed class ModulesController(IModuleRegistry modules) : WinAdminControllerBase
{
    public sealed record UpdateModuleRequest(bool? Enabled, JsonObject? Settings);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = new List<object>();
        foreach (var m in modules.Modules)
        {
            var state = modules.GetState(m.Id);
            result.Add(new
            {
                id = m.Id, title = m.Title, description = m.Description,
                enabled = state.Enabled, available = state.Available, unavailableReason = state.UnavailableReason,
                scopable = m.Scope is not null, scopeTitle = m.Scope?.Title,
                permissions = m.Permissions.Select(PermissionView),
                settingsSchema = modules.GetSettingsSchema(m.Id),
                settings = await modules.GetSettingsViewAsync(m.Id, ct),
            });
        }
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateModuleRequest request, CancellationToken ct)
    {
        if (request.Settings is not null)
            await modules.SaveSettingsAsync(id, request.Settings, Actor, ct);
        if (request.Enabled is bool enabled)
            await modules.SetEnabledAsync(id, enabled, Actor, ct);
        return NoContent();
    }

    internal static object PermissionView(PermissionDefinition p)
        => new { id = p.Id, title = p.Title, description = p.Description, scopable = p.Scopable, dangerous = p.Dangerous };
}
```

`src/backend/WinAdmin.Api/Controllers/PermissionsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Каталог прав для редактора ролей: ядро и модули (в т.ч. выключенные — роль можно подготовить заранее).</summary>
[RequirePermission(PermissionIds.PlatformRolesManage)]
[Route("api/v1/permissions")]
public sealed class PermissionsController(PermissionCatalog catalog) : WinAdminControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var groups = new List<object>
        {
            new { id = "platform", title = "WinAdmin", scopable = false, scopeTitle = (string?)null,
                  permissions = PermissionIds.Platform.Select(ModulesController.PermissionView) },
        };
        groups.AddRange(catalog.Modules.Select(m => (object)new
        {
            id = m.Id, title = m.Title, scopable = m.Scope is not null, scopeTitle = m.Scope?.Title,
            permissions = m.Permissions.Select(ModulesController.PermissionView),
        }));
        return Ok(groups);
    }
}
```

- [ ] **Step 5: Roles and assignments**

`src/backend/WinAdmin.Api/Controllers/RolesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformRolesManage)]
[PlatformErrors]
[Route("api/v1/roles")]
public sealed class RolesController(IRoleService roles, AccessContextFactory contexts) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await roles.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRoleRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await roles.CreateAsync(request, await ActorAsync(ct), ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] SaveRoleRequest request, CancellationToken ct)
        => Ok(await roles.UpdateAsync(id, request, await ActorAsync(ct), ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, await ActorAsync(ct), ct);
        return NoContent();
    }

    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);
}
```

`src/backend/WinAdmin.Api/Controllers/RoleAssignmentsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformRolesManage)]
[PlatformErrors]
[Route("api/v1/role-assignments")]
public sealed class RoleAssignmentsController(IRoleService roles, AccessContextFactory contexts) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PrincipalType? principalType, [FromQuery] string? principalId,
        [FromQuery] string? roleId, CancellationToken ct)
        => Ok(await roles.ListAssignmentsAsync(principalType, principalId, roleId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await roles.AssignAsync(request, await ActorAsync(ct), ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await roles.UnassignAsync(id, await ActorAsync(ct), ct);
        return NoContent();
    }

    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);
}
```

- [ ] **Step 6: Users and API keys with roles**

`src/backend/WinAdmin.Api/Controllers/UsersController.cs` — заменить содержимое класса (сохранив `using`, добавив `WinAdmin.Api.Auth`, `WinAdmin.Core.Security`):

```csharp
[RequirePermission(PermissionIds.PlatformUsersManage)]
[PlatformErrors]
[Route("api/v1/users")]
public sealed class UsersController(IUserService users, IRoleService roles, AccessContextFactory contexts) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct)
    {
        var assignments = await roles.ListAssignmentsAsync(PrincipalType.LocalUser, ct: ct);
        var list = await users.ListAsync(ct);
        return Ok(list.Select(u => u with
        {
            Roles = assignments.Where(a => a.PrincipalId == u.Id).Select(a => a.RoleName).ToList(),
        }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { message = "Укажите логин и пароль." });
        var actor = await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

        UserDto dto;
        try
        {
            dto = await users.CreateAsync(request, ct);
        }
        catch (Exception ex) when (ex.ToString().Contains("UNIQUE") || ex.ToString().Contains("unique"))
        {
            return Conflict(new { message = $"Пользователь «{request.Login}» уже существует" });
        }

        var granted = new List<string>();
        try
        {
            foreach (var roleId in request.RoleIds.Distinct())
                granted.Add((await roles.AssignAsync(new CreateAssignmentRequest(roleId, PrincipalType.LocalUser, dto.Id, null), actor, ct)).RoleName);
        }
        catch
        {
            // Роли выдать нельзя — пользователь без ролей не нужен.
            await roles.RemovePrincipalAsync(PrincipalType.LocalUser, dto.Id, ct);
            await users.DeleteAsync(dto.Id, ct);
            throw;
        }
        return StatusCode(StatusCodes.Status201Created, dto with { Roles = granted });
    }

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordRequest request, CancellationToken ct)
        => await users.ChangePasswordAsync(id, request.NewPassword, ct) ? NoContent() : NotFound();

    [HttpPut("{id}/active")]
    public async Task<IActionResult> SetActive(string id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        if (!request.IsActive)
            await roles.EnsureNotLastAdministratorAsync(PrincipalType.LocalUser, id, ct);
        return await users.SetActiveAsync(id, request.IsActive, ct) ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await roles.EnsureNotLastAdministratorAsync(PrincipalType.LocalUser, id, ct);
        if (!await users.DeleteAsync(id, ct)) return NotFound();
        await roles.RemovePrincipalAsync(PrincipalType.LocalUser, id, ct);
        return NoContent();
    }
}
```

`src/backend/WinAdmin.Api/Controllers/ApiKeysController.cs` — класс:

```csharp
[RequirePermission(PermissionIds.PlatformApiKeysManage)]
[PlatformErrors]
[Route("api/v1/apikeys")]
public sealed class ApiKeysController(IApiKeyService keys, IAuditService audit, IRoleService roles, AccessContextFactory contexts)
    : WinAdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List(CancellationToken ct)
    {
        var assignments = await roles.ListAssignmentsAsync(PrincipalType.ApiKey, ct: ct);
        var list = await keys.ListAsync(ct);
        return Ok(list.Select(k => k with
        {
            Roles = assignments.Where(a => a.PrincipalId == k.Id).Select(a => a.RoleName).ToList(),
        }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(OperationResult.Fail("Укажите имя ключа"));
        if (request.RoleIds.Count == 0)
            return BadRequest(OperationResult.Fail("Выберите хотя бы одну роль"));
        var actor = await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

        var created = await keys.CreateAsync(request, ct);
        var granted = new List<string>();
        try
        {
            foreach (var roleId in request.RoleIds.Distinct())
                granted.Add((await roles.AssignAsync(new CreateAssignmentRequest(roleId, PrincipalType.ApiKey, created.Key.Id, null), actor, ct)).RoleName);
        }
        catch
        {
            await roles.RemovePrincipalAsync(PrincipalType.ApiKey, created.Key.Id, ct);
            await keys.RevokeAsync(created.Key.Id, ct);
            throw;
        }

        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "apikey.create", Target = created.Key.Id, Success = true,
            Details = $"роли: {string.Join(", ", granted)}", SourceIp = SourceIp,
        }, ct);
        return StatusCode(StatusCodes.Status201Created, created with { Key = created.Key with { Roles = granted } });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        await roles.EnsureNotLastAdministratorAsync(PrincipalType.ApiKey, id, ct);
        bool ok = await keys.RevokeAsync(id, ct);
        await audit.WriteAsync(new AuditEntryDto { Actor = Actor, Action = "apikey.revoke", Target = id, Success = ok, SourceIp = SourceIp }, ct);
        return ok ? Ok(OperationResult.Ok("Ключ отозван")) : NotFound();
    }
}
```

`AuthController`: удалить действие `Me` (заменено `/api/v1/me`).

- [ ] **Step 7: Run tests**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS, включая `PlatformApiTests` (5).

- [ ] **Step 8: Commit**

```bash
git add src/backend/WinAdmin.Api src/tests/WinAdmin.Tests/PlatformApiTests.cs
git commit -m "feat(api): me, modules, permissions, roles and assignments endpoints"
```

---

### Task 10: CLI — роли для пользователей и аварийное назначение

**Files:**
- Modify: `src/backend/WinAdmin.Api/Cli/UserCommands.cs` (`add --role`, `--scopes` через перенос; `list` с ролями)
- Create: `src/backend/WinAdmin.Api/Cli/RoleCommands.cs`
- Modify: `src/backend/WinAdmin.Api/Cli/CliRunner.cs` (каталог, БД для `role`, регистрация), `src/backend/WinAdmin.Api/Program.cs` (CLI-режим `role`)
- Test: `src/tests/WinAdmin.Tests/RoleCommandsTests.cs`

**Interfaces:**
- Consumes: `LegacyScopes`, `PlatformBootstrapper`, `PermissionCatalog`, `BuiltInRoles`, сущности.
- Produces:
  - `WinAdmin.exe user add --login x --password y [--role "Имя роли"]… [--scopes admin|a,b]`
  - `WinAdmin.exe role list`, `role assign --role "Имя" (--local login | --apikey id)`, `role unassign --role "Имя" (--local login | --apikey id)`
  - `static void RoleCommands.Register(Command role, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/RoleCommandsTests.cs`:

```csharp
using System.CommandLine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Api.Cli;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class RoleCommandsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly RootCommand _root;

    public RoleCommandsTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(new PermissionCatalog(BuiltInModules.All))
            .AddScoped<IUserService, UserService>()
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            db.Database.Migrate();
            PlatformBootstrapper.RunAsync(db, scope.ServiceProvider.GetRequiredService<PermissionCatalog>()).GetAwaiter().GetResult();
        }
        var user = new Command("user");
        UserCommands.Register(user, _sp, _out, _err);
        var role = new Command("role");
        RoleCommands.Register(role, _sp, _out, _err);
        _root = new RootCommand { user, role };
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private T Db<T>(Func<WinAdminDbContext, T> query)
    {
        using var scope = _sp.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<WinAdminDbContext>());
    }

    [Fact]
    public async Task User_add_with_role_assigns_administrator()
    {
        Assert.Equal(0, await _root.InvokeAsync(["user", "add", "--login", "ops", "--password", "Ops-123456", "--role", "Администратор"]));
        Assert.Equal(1, Db(db => db.RoleAssignments.Count(a => a.RoleId == BuiltInRoles.AdministratorId && a.DisplayName == "ops")));
    }

    [Fact]
    public async Task User_add_with_legacy_scopes_maps_to_roles()
    {
        Assert.Equal(0, await _root.InvokeAsync(["user", "add", "--login", "mon", "--password", "Mon-1234567", "--scopes", "system.read,services.read"]));
        Assert.Equal(1, Db(db => db.RoleAssignments.Count(a => a.DisplayName == "mon")));
        Assert.Equal("", Db(db => db.Users.Single(u => u.Login == "mon").Scopes));
    }

    [Fact]
    public async Task Unknown_role_fails_without_creating_user()
    {
        Assert.Equal(1, await _root.InvokeAsync(["user", "add", "--login", "x", "--password", "X-12345678", "--role", "Нет такой"]));
        Assert.Equal(0, Db(db => db.Users.Count()));
        Assert.Contains("Нет такой", _err.ToString());
    }

    [Fact]
    public async Task Role_assign_is_the_emergency_path()
    {
        await _root.InvokeAsync(["user", "add", "--login", "late", "--password", "Late-123456"]);
        Assert.Equal(0, await _root.InvokeAsync(["role", "assign", "--role", "Администратор", "--local", "late"]));
        Assert.Equal(0, await _root.InvokeAsync(["role", "list"]));
        Assert.Contains("Администратор", _out.ToString());
        Assert.Contains("late", _out.ToString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~RoleCommandsTests`
Expected: build FAIL — `RoleCommands` не найден / у `UserCommands.Register` нет параметров вывода.

- [ ] **Step 3: Implement**

В `UserCommands.cs`:
1. Сигнатуру `Register(Command userCommand, IServiceProvider provider)` заменить на `Register(Command userCommand, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)`; сохранить `o = output ?? Console.Out`, `e = error ?? Console.Error` и передать в `ListCommand` и `AddCommand`; во всех `Console.WriteLine` этих двух команд писать в `o`, ошибки — в `e`.
2. `AddCommand` заменить на:

```csharp
    private static Command AddCommand(IServiceProvider provider, TextWriter output, TextWriter error)
    {
        var loginOpt = new Option<string>("--login", "Логин") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", "Пароль") { IsRequired = true };
        var roleOpt = new Option<string[]>("--role", "Роль (можно несколько): например «Администратор»") { AllowMultipleArgumentsPerToken = false };
        var scopesOpt = new Option<string>("--scopes", () => "", "Совместимость: старые scopes через запятую (admin → «Администратор»)");
        var cmd = new Command("add", "Создать пользователя") { loginOpt, passwordOpt, roleOpt, scopesOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();
            var roleNames = ctx.ParseResult.GetValueForOption(roleOpt) ?? [];

            var roles = new List<RoleEntity>();
            foreach (var name in roleNames)
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
                if (role is null)
                {
                    error.WriteLine($"Роль «{name}» не найдена. Список: WinAdmin.exe role list");
                    ctx.ExitCode = 1;
                    return;
                }
                roles.Add(role);
            }

            var dto = await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest
            {
                Login = ctx.ParseResult.GetValueForOption(loginOpt)!,
                Password = ctx.ParseResult.GetValueForOption(passwordOpt)!,
            });
            foreach (var role in roles)
                db.RoleAssignments.Add(new RoleAssignmentEntity
                {
                    RoleId = role.Id, PrincipalType = nameof(PrincipalType.LocalUser), PrincipalId = dto.Id,
                    DisplayName = dto.Login, CreatedBy = "cli",
                });
            await LegacyScopes.AssignAsync(db, catalog, PrincipalType.LocalUser, dto.Id, dto.Login,
                ctx.ParseResult.GetValueForOption(scopesOpt)!.Split(','), "cli");
            await db.SaveChangesAsync();

            var granted = await db.RoleAssignments.Where(a => a.PrincipalId == dto.Id).Select(a => a.Role.Name).ToListAsync();
            output.WriteLine($"Создан пользователь: {dto.Login} (роли: {(granted.Count == 0 ? "нет" : string.Join(", ", granted))})");
        });
        return cmd;
    }
```

3. `ListCommand`: колонку `SCOPES` заменить на `ROLES`, роли брать из `db.RoleAssignments` (`PrincipalType == "LocalUser"`, `PrincipalId == u.Id`).
4. Команду `scopes` (если есть в `Register`) удалить — scopes больше не существуют.

`using`: `System.CommandLine.Invocation`, `Microsoft.EntityFrameworkCore`, `WinAdmin.Core.Modules`, `WinAdmin.Core.Security`, `WinAdmin.Infrastructure.Access`, `WinAdmin.Infrastructure.Storage`.

`src/backend/WinAdmin.Api/Cli/RoleCommands.cs`:

```csharp
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

/// <summary>Роли из консоли администратора — в том числе аварийная выдача «Администратор».</summary>
public static class RoleCommands
{
    public static void Register(Command role, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        role.AddCommand(List(provider, o));
        role.AddCommand(Change("assign", "Назначить роль", provider, o, e, assign: true));
        role.AddCommand(Change("unassign", "Снять роль", provider, o, e, assign: false));
    }

    private static Command List(IServiceProvider provider, TextWriter output)
    {
        var cmd = new Command("list", "Роли и кому они назначены");
        cmd.SetHandler(async () =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            foreach (var r in await db.Roles.Include(x => x.Assignments).OrderByDescending(x => x.IsBuiltin).ThenBy(x => x.Name).ToListAsync())
            {
                output.WriteLine($"{r.Name}{(r.IsBuiltin ? " (встроенная)" : "")}");
                foreach (var a in r.Assignments.OrderBy(x => x.DisplayName))
                    output.WriteLine($"    {a.PrincipalType,-10} {a.DisplayName}");
            }
        });
        return cmd;
    }

    private static Command Change(string name, string description, IServiceProvider provider, TextWriter output, TextWriter error, bool assign)
    {
        var roleOpt = new Option<string>("--role", "Название роли") { IsRequired = true };
        var localOpt = new Option<string?>("--local", "Логин локального пользователя");
        var keyOpt = new Option<string?>("--apikey", "Id API-ключа");
        var cmd = new Command(name, description) { roleOpt, localOpt, keyOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            string roleName = ctx.ParseResult.GetValueForOption(roleOpt)!;
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role is null)
            {
                error.WriteLine($"Роль «{roleName}» не найдена.");
                ctx.ExitCode = 1;
                return;
            }

            string? login = ctx.ParseResult.GetValueForOption(localOpt);
            string? keyId = ctx.ParseResult.GetValueForOption(keyOpt);
            (PrincipalType Type, string Id, string Display)? principal = null;
            if (login is not null && await db.Users.FirstOrDefaultAsync(u => u.Login == login) is { } user)
                principal = (PrincipalType.LocalUser, user.Id, user.Login);
            else if (keyId is not null && await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId) is { } key)
                principal = (PrincipalType.ApiKey, key.Id, key.Name);
            if (principal is null)
            {
                error.WriteLine("Укажите существующего пользователя (--local) или API-ключ (--apikey).");
                ctx.ExitCode = 1;
                return;
            }

            string type = principal.Value.Type.ToString();
            var existing = await db.RoleAssignments.FirstOrDefaultAsync(a =>
                a.RoleId == role.Id && a.PrincipalType == type && a.PrincipalId == principal.Value.Id);
            if (assign && existing is null)
                db.RoleAssignments.Add(new RoleAssignmentEntity
                {
                    RoleId = role.Id, PrincipalType = type, PrincipalId = principal.Value.Id,
                    DisplayName = principal.Value.Display, CreatedBy = "cli",
                });
            else if (!assign && existing is not null)
                db.RoleAssignments.Remove(existing);
            await db.SaveChangesAsync();
            output.WriteLine($"{(assign ? "Назначено" : "Снято")}: «{role.Name}» — {principal.Value.Display}. Служба применит изменение в течение минуты.");
        });
        return cmd;
    }
}
```

`CliRunner.cs`:
- в DI добавить `services.AddSingleton(new PermissionCatalog(BuiltInModules.All));`;
- регистрацию БД и миграцию выполнять для `args is ["user", ..] or ["role", ..]` (оба места);
- после миграции для этих команд вызвать `await PlatformBootstrapper.RunAsync(db, provider.GetRequiredService<PermissionCatalog>());` (чтобы «Администратор» существовал до запуска службы);
- зарегистрировать `var roleCommand = new Command("role", "Роли WinAdmin"); RoleCommands.Register(roleCommand, provider);` и добавить в `RootCommand`.

`Program.cs`: условие CLI — добавить `or "role"`.

- [ ] **Step 4: Run tests**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Api src/tests/WinAdmin.Tests/RoleCommandsTests.cs
git commit -m "feat(cli): assign roles from the console and map legacy --scopes"
```

---

### Task 11: Фронтенд — права в клиенте, меню, защита маршрутов, пользователи и ключи

**Files:**
- Modify: `src/frontend/src/api/types.ts`, `api/client.ts`, `api/authApi.ts`
- Modify: `src/frontend/src/auth/AuthProvider.tsx`
- Create: `src/frontend/src/components/NoAccess.tsx`, `components/Guard.tsx`
- Modify: `src/frontend/src/components/AppLayout.tsx`, `App.tsx`
- Modify: `src/frontend/src/pages/Users.tsx`, `pages/ApiKeys.tsx`, `pages/Settings.tsx`

**Interfaces:**
- Consumes: HTTP из Task 9.
- Produces:
  - типы `MeResponse { actor; principal; permissions: Record<string, string[] | null>; modules: { id; title }[] }`, `RoleDto`, `RoleGrantDto`, `SaveRoleRequest`, `RoleAssignmentDto`, `PrincipalType`, `PermissionGroupDto`, `PermissionDto`, `ModuleDto`, `SettingsField`
  - `api.me()`, `api.roles.{list,create,update,remove}`, `api.assignments.{list,create,remove}`, `api.permissions()`, `api.modules.{list,update}`
  - `useAuth()` → `{ user, can(perm), moduleOn(id), logout, token }`
  - `<Guard perm="…" module="…">` → дочерний элемент или `<NoAccess />`

- [ ] **Step 1: Types and client**

`src/frontend/src/api/types.ts`:
- `ApiKeyDto.scopes: string[]` → `roles: string[]`; `UserDto.scopes` → `roles: string[]`; `CreateUserRequest.scopes` → `roleIds: string[]`.
- `MeResponse` заменить на:

```ts
export interface MeResponse {
  actor: string
  principal: string
  /** право → область (null — без ограничений) */
  permissions: Record<string, string[] | null>
  modules: { id: string; title: string }[]
}
```

- добавить в конец:

```ts
export type PrincipalType = 'LocalUser' | 'AdUser' | 'AdGroup' | 'ApiKey'

export interface PermissionDto {
  id: string
  title: string
  description?: string
  scopable: boolean
  dangerous: boolean
}

export interface PermissionGroupDto {
  id: string
  title: string
  scopable: boolean
  scopeTitle?: string
  permissions: PermissionDto[]
}

export interface RoleGrantDto {
  permissionId: string
  scope: string[] | null
}

export interface RoleDto {
  id: string
  name: string
  description?: string
  isBuiltin: boolean
  permissions: RoleGrantDto[]
  assignmentCount: number
}

export interface SaveRoleRequest {
  name: string
  description?: string
  permissions: RoleGrantDto[]
}

export interface RoleAssignmentDto {
  id: string
  roleId: string
  roleName: string
  principalType: PrincipalType
  principalId: string
  displayName: string
  createdAt: string
}

export interface SettingsField {
  name: string
  title: string
  kind: 'string' | 'number' | 'boolean' | 'stringList' | 'secret'
}

export interface ModuleDto {
  id: string
  title: string
  description?: string
  enabled: boolean
  available: boolean
  unavailableReason?: string
  scopable: boolean
  scopeTitle?: string
  permissions: PermissionDto[]
  settingsSchema: SettingsField[]
  settings: Record<string, unknown>
}
```

`src/frontend/src/api/client.ts`:
- в импорт типов добавить `ModuleDto, PermissionGroupDto, PrincipalType, RoleAssignmentDto, RoleDto, SaveRoleRequest`;
- удалить `availableScopes` и `updateUserScopes`;
- `createKey` заменить на `createKey: (name: string, roleIds: string[], expiresAt?: string) => http.post<CreatedApiKey>('/apikeys', { name, roleIds, expiresAt }).then((r) => r.data),`;
- в объект `api` добавить:

```ts
  me: () => http.get<MeResponse>('/me').then((r) => r.data),

  permissions: () => http.get<PermissionGroupDto[]>('/permissions').then((r) => r.data),

  roles: {
    list: () => http.get<RoleDto[]>('/roles').then((r) => r.data),
    create: (req: SaveRoleRequest) => http.post<RoleDto>('/roles', req).then((r) => r.data),
    update: (id: string, req: SaveRoleRequest) => http.put<RoleDto>(`/roles/${id}`, req).then((r) => r.data),
    remove: (id: string) => http.delete(`/roles/${id}`),
  },

  assignments: {
    list: (params: { principalType?: PrincipalType; principalId?: string; roleId?: string }) =>
      http.get<RoleAssignmentDto[]>('/role-assignments', { params }).then((r) => r.data),
    create: (roleId: string, principalType: PrincipalType, principalId: string, displayName?: string) =>
      http.post<RoleAssignmentDto>('/role-assignments', { roleId, principalType, principalId, displayName }).then((r) => r.data),
    remove: (id: string) => http.delete(`/role-assignments/${id}`),
  },

  modules: {
    list: () => http.get<ModuleDto[]>('/modules').then((r) => r.data),
    update: (id: string, body: { enabled?: boolean; settings?: Record<string, unknown> }) => http.put(`/modules/${id}`, body),
  },
```

`src/frontend/src/api/authApi.ts`: в `me` заменить URL `'/api/v1/auth/me'` на `'/api/v1/me'`.

- [ ] **Step 2: Auth context with permissions**

`src/frontend/src/auth/AuthProvider.tsx` — интерфейс и значение контекста:

```tsx
interface AuthState {
  user: MeResponse | null
  token: string
  /** Есть ли право (с любой областью). */
  can: (permission: string) => boolean
  /** Включён ли модуль. */
  moduleOn: (moduleId: string) => boolean
  logout: () => Promise<void>
}
```

и перед `return` провайдера:

```tsx
  const can = useCallback((permission: string) => Boolean(user && permission in user.permissions), [user])
  const moduleOn = useCallback((id: string) => Boolean(user?.modules.some((m) => m.id === id)), [user])
```

`value={{ user, token, can, moduleOn, logout }}`.

- [ ] **Step 3: NoAccess and Guard**

`src/frontend/src/components/NoAccess.tsx`:

```tsx
import { Result } from 'antd'

export default function NoAccess() {
  return (
    <Result
      status="403"
      title="Нет доступа"
      subTitle="У вашей учётной записи нет прав на этот раздел, или модуль выключен. Обратитесь к администратору WinAdmin."
    />
  )
}
```

`src/frontend/src/components/Guard.tsx`:

```tsx
import type { ReactNode } from 'react'
import { Spin } from 'antd'
import { useAuth } from '../auth/AuthProvider'
import NoAccess from './NoAccess'

/** Раздел виден, только если модуль включён и есть хотя бы одно из прав. */
export default function Guard({ perm, module, children }: { perm: string | string[]; module?: string; children: ReactNode }) {
  const { user, can, moduleOn } = useAuth()
  if (!user) return <Spin style={{ display: 'block', margin: '80px auto' }} />
  const perms = Array.isArray(perm) ? perm : [perm]
  if ((module && !moduleOn(module)) || !perms.some(can)) return <NoAccess />
  return <>{children}</>
}
```

- [ ] **Step 4: Menu by permissions**

`src/frontend/src/components/AppLayout.tsx`:
- импорт иконок дополнить `SafetyCertificateOutlined, AppstoreAddOutlined`;
- заменить `const { user, logout } = useAuth()` и строку `const isAdmin = …` на `const { can, moduleOn, logout } = useAuth()`;
- заменить массив `items` на:

```tsx
  type Item = { key: string; icon?: React.ReactNode; label: string; perm?: string | string[]; module?: string; children?: Item[] }
  const allowed = (i: Item) =>
    (!i.module || moduleOn(i.module)) && (!i.perm || (Array.isArray(i.perm) ? i.perm.some(can) : can(i.perm)))
  const filter = (list: Item[]): Item[] =>
    list.filter(allowed)
      .map((i) => (i.children ? { ...i, children: filter(i.children) } : i))
      .filter((i) => !i.children || i.children.length > 0)

  const items = filter([
    { key: '/', icon: <DashboardOutlined />, label: 'Дашборд', perm: 'system.read', module: 'system' },
    { key: '/disks', icon: <HddOutlined />, label: 'Диски', perm: 'system.read', module: 'system' },
    { key: '/services', icon: <ApiOutlined />, label: 'Службы', perm: 'services.read', module: 'services' },
    { key: '/processes', icon: <AppstoreOutlined />, label: 'Процессы', perm: 'processes.read', module: 'processes' },
    {
      key: '/software', icon: <CodeOutlined />, label: 'Software', perm: 'software.read', module: 'software',
      children: [
        { key: '/software/apps', label: 'Applications' },
        { key: '/software/updates', label: 'Updates' },
      ],
    },
    { key: '/printers', icon: <PrinterOutlined />, label: 'Принтеры', perm: 'printers.read', module: 'printers' },
    { key: '/power', icon: <PoweroffOutlined />, label: 'Питание', perm: 'power.manage', module: 'power' },
    {
      key: '/logs', icon: <FileTextOutlined />, label: 'Журналы Windows', perm: 'eventlogs.read', module: 'eventlogs',
      children: [
        { key: '/logs/auth', label: 'Авторизация' },
        { key: '/logs/security', label: 'Security (все события)' },
        { key: '/logs/system', label: 'Система' },
        { key: '/logs/application', label: 'Приложения' },
        { key: '/logs/powershell', label: 'PowerShell' },
        { key: '/logs/setup', label: 'Установка ПО' },
        { key: '/logs/custom', label: 'Произвольный журнал' },
      ],
    },
    { key: '/cp/users', icon: <TeamOutlined />, label: 'Пользователи', perm: 'platform.users.manage' },
    { key: '/cp/roles', icon: <SafetyCertificateOutlined />, label: 'Роли', perm: 'platform.roles.manage' },
    { key: '/cp/modules', icon: <AppstoreAddOutlined />, label: 'Модули', perm: 'platform.modules.manage' },
    { key: '/cp/apikeys', icon: <KeyOutlined />, label: 'API-ключи', perm: 'platform.apikeys.manage' },
    { key: '/cp/audit', icon: <FileSearchOutlined />, label: 'Аудит', perm: 'platform.audit.read' },
    { key: '/cp/settings', icon: <SettingOutlined />, label: 'Настройки', perm: ['platform.network.manage', 'eventlogs.manage'] },
    { key: '/docs', icon: <BookOutlined />, label: 'API-документация' },
  ])
```

(Если в исходном массиве между `'/processes'` и `'/software'` или в других местах есть разделители `{ type: 'divider' }` — сохранить их, вставив как элементы без `perm`; фильтр `allowed` их пропускает. Тип `Item` объявить вне компонента рядом с импортами; `import type React from 'react'` при необходимости.)

- [ ] **Step 5: Guarded routes**

`src/frontend/src/App.tsx`: импортировать `Guard`, `Modules`, `Roles` (страницы появятся в Task 12; до этого — добавить маршруты в Task 12), обернуть маршруты:

```tsx
          <Route path="/" element={<Guard perm="system.read" module="system"><Dashboard /></Guard>} />
          <Route path="/disks" element={<Guard perm="system.read" module="system"><Disks /></Guard>} />
          <Route path="/services" element={<Guard perm="services.read" module="services"><Services /></Guard>} />
          <Route path="/processes" element={<Guard perm="processes.read" module="processes"><Processes /></Guard>} />
          <Route path="/printers" element={<Guard perm="printers.read" module="printers"><Printers /></Guard>} />
          <Route path="/power" element={<Guard perm="power.manage" module="power"><Power /></Guard>} />
          <Route path="/software/apps" element={<Guard perm="software.read" module="software"><Applications /></Guard>} />
          <Route path="/software/updates" element={<Guard perm="software.read" module="software"><Updates /></Guard>} />
          <Route path="/logs/:presetKey" element={<Guard perm="eventlogs.read" module="eventlogs"><EventLogs /></Guard>} />
          <Route path="/cp/apikeys" element={<Guard perm="platform.apikeys.manage"><ApiKeys /></Guard>} />
          <Route path="/cp/audit" element={<Guard perm="platform.audit.read"><AuditLog /></Guard>} />
          <Route path="/cp/users" element={<Guard perm="platform.users.manage"><Users /></Guard>} />
          <Route path="/cp/settings" element={<Guard perm={['platform.network.manage', 'eventlogs.manage']}><Settings /></Guard>} />
```

Остальные маршруты (`/docs`, `*`) — без изменений. Вызов `api.system()` для имени машины остаётся (ошибка 403 уже перехватывается `.catch`).

- [ ] **Step 6: Settings page by permission**

`src/frontend/src/pages/Settings.tsx`: получить `const { can, moduleOn } = useAuth()`; `<NetworkSettingsCard />` показывать при `can('platform.network.manage')`; карточку исключённых учётных записей и её загрузку (`load`) — только при `can('eventlogs.manage') && moduleOn('eventlogs')` (в `useEffect` не вызывать `load`, если нет права).

- [ ] **Step 7: Users with roles**

`src/frontend/src/pages/Users.tsx`:
- удалить `ALL_SCOPES`, состояние/форму/модалку `scopes`;
- загрузить роли: `const [roles, setRoles] = useState<RoleDto[]>([])` и в `load` — `const [u, r] = await Promise.all([api.users(), api.roles.list().catch(() => [])]); setUsers(u); setRoles(r)`;
- колонку `scopes` заменить на:

```tsx
    {
      field: 'roles', headerName: 'Роли', flex: 2,
      cellRenderer: ({ value }: { value: string[] }) => (
        <Space wrap size={4}>
          {value.length === 0
            ? <Tag>нет ролей</Tag>
            : value.map((r) => <Tag key={r} color={r === 'Администратор' ? 'red' : 'blue'}>{r}</Tag>)}
        </Space>
      ),
    },
```

- в создании: поле `<Form.Item name="roleIds" label="Роли" initialValue={[]}><Select mode="multiple" options={roles.map((r) => ({ value: r.id, label: r.name }))} placeholder="Выберите роли" /></Form.Item>`; `handleCreate(values: { login: string; password: string; roleIds: string[] })`;
- кнопку «Scopes» заменить на «Роли», открывающую модалку управления ролями пользователя:

```tsx
  const [rolesOpen, setRolesOpen] = useState(false)
  const [userAssignments, setUserAssignments] = useState<RoleAssignmentDto[]>([])
  const [rolesForm] = Form.useForm()

  const openRoles = async (user: UserDto) => {
    setSelected(user)
    const list = await api.assignments.list({ principalType: 'LocalUser', principalId: user.id })
    setUserAssignments(list)
    rolesForm.setFieldsValue({ roleIds: list.map((a) => a.roleId) })
    setRolesOpen(true)
  }

  const saveRoles = async (values: { roleIds: string[] }) => {
    if (!selected) return
    try {
      const current = new Set(userAssignments.map((a) => a.roleId))
      const wanted = new Set(values.roleIds)
      for (const a of userAssignments.filter((a) => !wanted.has(a.roleId))) await api.assignments.remove(a.id)
      for (const id of [...wanted].filter((id) => !current.has(id))) await api.assignments.create(id, 'LocalUser', selected.id)
      message.success('Роли обновлены')
      setRolesOpen(false)
      load()
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось изменить роли')
      load()
    }
  }
```

  и модалку `Роли — {selected?.login}` с `Select mode="multiple"` по `roles`;
- в `handleToggleActive` и `handleDelete` обернуть вызовы в `try/catch` с `message.error(e?.response?.data?.message ?? 'Ошибка')` (409 последнего администратора).

- [ ] **Step 8: API keys with roles**

`src/frontend/src/pages/ApiKeys.tsx`:
- `const { data: scopes } = useApi(api.availableScopes)` → `const { data: roles } = useApi(api.roles.list)`;
- в `submit` — `{ name: string; roleIds: string[]; expiresAt?: Dayjs }` и `api.createKey(v.name, v.roleIds, …)`;
- колонку `Scopes` → `Роли` (`dataIndex: 'roles'`, тег `gold` для «Администратор»);
- поле формы `scopes` → `roleIds` с подписью «Роли» и опциями `(roles ?? []).map((r) => ({ label: r.name, value: r.id }))`, `initialValues={{ roleIds: [] }}`, сообщение «Выберите хотя бы одну роль»;
- в `revoke` показывать `response.data.message` при ошибке (409).

- [ ] **Step 9: Build and lint**

Run: `npm --prefix src/frontend run build && npm --prefix src/frontend run lint`
Expected: сборка без ошибок TypeScript; в изменённых файлах нет новых замечаний oxlint. (Маршруты `/cp/roles`, `/cp/modules` есть в меню, страницы появятся в Task 12 — до этого ссылка ведёт на `*` → `/`.)

- [ ] **Step 10: Commit**

```bash
git add src/frontend/src
git commit -m "feat(ui): permission-based menu and routes, roles on users and API keys"
```

---

### Task 12: Фронтенд — страницы «Модули» и «Роли»

**Files:**
- Create: `src/frontend/src/pages/Modules.tsx`
- Create: `src/frontend/src/pages/Roles.tsx`
- Modify: `src/frontend/src/App.tsx` (маршруты `/cp/modules`, `/cp/roles`)

**Interfaces:**
- Consumes: `api.modules`, `api.permissions`, `api.roles`, `api.assignments`, `api.users`, `api.apiKeys` (Task 11).

- [ ] **Step 1: Modules page**

`src/frontend/src/pages/Modules.tsx`:

```tsx
import { useState } from 'react'
import { Alert, App, Button, Card, Col, Form, Input, InputNumber, Row, Select, Space, Switch, Tag, Tooltip, Typography } from 'antd'
import { useApi } from '../hooks/useApi'
import { api } from '../api/client'
import type { ModuleDto, SettingsField } from '../api/types'
import PageHeader from '../components/PageHeader'

const { Paragraph, Text } = Typography

function SettingsForm({ module, onSaved }: { module: ModuleDto; onSaved: () => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm()
  const [saving, setSaving] = useState(false)

  const initial = Object.fromEntries(module.settingsSchema
    .filter((f) => f.kind !== 'secret')
    .map((f) => [f.name, module.settings[f.name]]))

  const save = async (values: Record<string, unknown>) => {
    setSaving(true)
    try {
      await api.modules.update(module.id, { settings: values })
      message.success('Настройки сохранены')
      form.resetFields(module.settingsSchema.filter((f) => f.kind === 'secret').map((f) => f.name))
      onSaved()
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить настройки')
    } finally {
      setSaving(false)
    }
  }

  const field = (f: SettingsField) => {
    switch (f.kind) {
      case 'boolean': return <Switch />
      case 'number': return <InputNumber style={{ width: 200 }} />
      case 'stringList': return <Select mode="tags" open={false} tokenSeparators={[',']} />
      case 'secret': {
        const isSet = (module.settings[f.name] as { isSet?: boolean } | undefined)?.isSet
        return <Input.Password placeholder={isSet ? 'задано — оставьте пустым, чтобы не менять' : 'не задано'} autoComplete="new-password" />
      }
      default: return <Input />
    }
  }

  return (
    <Form form={form} layout="vertical" initialValues={initial} onFinish={save}>
      {module.settingsSchema.map((f) => (
        <Form.Item key={f.name} name={f.name} label={f.title} valuePropName={f.kind === 'boolean' ? 'checked' : 'value'}>
          {field(f)}
        </Form.Item>
      ))}
      <Button htmlType="submit" type="primary" loading={saving}>Сохранить настройки</Button>
    </Form>
  )
}

export default function Modules() {
  const { data, loading, refresh } = useApi(api.modules.list)
  const { message } = App.useApp()
  const [busy, setBusy] = useState<string>()

  const toggle = async (m: ModuleDto, enabled: boolean) => {
    setBusy(m.id)
    try {
      await api.modules.update(m.id, { enabled })
      message.success(enabled ? `Модуль «${m.title}» включён` : `Модуль «${m.title}» выключен`)
      await refresh(true)
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось изменить модуль')
    } finally {
      setBusy(undefined)
    }
  }

  return (
    <>
      <PageHeader title="Модули" subtitle="Включение, отключение и настройка разделов WinAdmin" onRefresh={refresh} loading={loading} />
      <Alert type="info" showIcon style={{ marginBottom: 16 }}
        message="Выключенный модуль скрыт из меню у всех пользователей, а его API отвечает 404. Права на него можно заранее выдать в ролях." />
      <Row gutter={[16, 16]}>
        {(data ?? []).map((m) => (
          <Col key={m.id} xs={24} lg={12}>
            <Card
              className="sp-glass"
              variant="borderless"
              title={<Space>{m.title}<Text type="secondary" code>{m.id}</Text></Space>}
              extra={
                <Tooltip title={m.available ? undefined : `Недоступен: ${m.unavailableReason}`}>
                  <Switch checked={m.enabled} disabled={!m.available} loading={busy === m.id} onChange={(v) => toggle(m, v)} />
                </Tooltip>
              }
            >
              {m.description && <Paragraph type="secondary" style={{ marginTop: 0 }}>{m.description}</Paragraph>}
              {!m.available && <Alert type="warning" showIcon style={{ marginBottom: 12 }} message={`Недоступен на этой машине: ${m.unavailableReason}`} />}
              <Space size={[4, 4]} wrap style={{ marginBottom: m.settingsSchema.length ? 16 : 0 }}>
                {m.permissions.map((p) => (
                  <Tooltip key={p.id} title={p.description}>
                    <Tag color={p.dangerous ? 'volcano' : 'blue'}>{p.title}</Tag>
                  </Tooltip>
                ))}
              </Space>
              {m.settingsSchema.length > 0 && <SettingsForm module={m} onSaved={() => refresh(true)} />}
            </Card>
          </Col>
        ))}
      </Row>
    </>
  )
}
```

- [ ] **Step 2: Roles page**

`src/frontend/src/pages/Roles.tsx`:

```tsx
import { useMemo, useState } from 'react'
import {
  Alert, App, Button, Card, Drawer, Empty, Form, Input, List, Popconfirm, Select, Space, Table, Tag, Tree, Typography,
} from 'antd'
import type { DataNode } from 'antd/es/tree'
import { DeleteOutlined, EditOutlined, PlusOutlined, TeamOutlined } from '@ant-design/icons'
import { useApi } from '../hooks/useApi'
import { api } from '../api/client'
import type { PermissionGroupDto, PrincipalType, RoleAssignmentDto, RoleDto, RoleGrantDto } from '../api/types'
import PageHeader from '../components/PageHeader'

const { Text } = Typography

interface EditorValues { name: string; description?: string }

function errorText(e: any, fallback: string) {
  const d = e?.response?.data
  return d?.violations?.length ? `${d.message} ${d.violations.join('; ')}` : d?.message ?? fallback
}

function RoleEditor({ role, groups, open, onClose, onSaved }: {
  role?: RoleDto; groups: PermissionGroupDto[]; open: boolean; onClose: () => void; onSaved: () => void
}) {
  const { message } = App.useApp()
  const [form] = Form.useForm<EditorValues>()
  const [checked, setChecked] = useState<string[]>([])
  const [scopes, setScopes] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)
  const moduleOf = (permId: string) => groups.find((g) => g.permissions.some((p) => p.id === permId))

  const reset = () => {
    form.setFieldsValue({ name: role?.name ?? '', description: role?.description })
    setChecked(role?.permissions.map((p) => p.permissionId) ?? [])
    const s: Record<string, string[]> = {}
    for (const p of role?.permissions ?? []) {
      const g = moduleOf(p.permissionId)
      if (g?.scopable && p.scope) s[g.id] = Array.from(new Set([...(s[g.id] ?? []), ...p.scope]))
    }
    setScopes(s)
  }

  const treeData: DataNode[] = groups.map((g) => ({
    key: `group:${g.id}`,
    title: g.title,
    children: g.permissions.map((p) => ({
      key: p.id,
      title: <span>{p.title} {p.dangerous && <Tag color="volcano" style={{ marginLeft: 4 }}>опасное</Tag>}</span>,
    })),
  }))

  const save = async () => {
    const values = await form.validateFields()
    const permissions: RoleGrantDto[] = checked.filter((k) => !k.startsWith('group:')).map((id) => {
      const g = moduleOf(id)
      const p = g?.permissions.find((x) => x.id === id)
      const scope = g?.scopable && p?.scopable && scopes[g.id]?.length ? scopes[g.id] : null
      return { permissionId: id, scope }
    })
    setSaving(true)
    try {
      if (role) await api.roles.update(role.id, { ...values, permissions })
      else await api.roles.create({ ...values, permissions })
      message.success('Роль сохранена')
      onSaved()
      onClose()
    } catch (e) {
      message.error(errorText(e, 'Не удалось сохранить роль'))
    } finally {
      setSaving(false)
    }
  }

  const scopedGroups = groups.filter((g) => g.scopable && checked.some((k) => g.permissions.some((p) => p.id === k)))

  return (
    <Drawer
      title={role ? `Роль «${role.name}»` : 'Новая роль'}
      open={open}
      onClose={onClose}
      width={560}
      afterOpenChange={(v) => v && reset()}
      extra={<Button type="primary" onClick={save} loading={saving}>Сохранить</Button>}
    >
      <Form form={form} layout="vertical">
        <Form.Item name="name" label="Название" rules={[{ required: true, message: 'Укажите название' }, { max: 100 }]}>
          <Input placeholder="Например: Кадры ТЕХНО-ЦЕНТР" />
        </Form.Item>
        <Form.Item name="description" label="Описание">
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
      <Text strong>Права</Text>
      <Tree
        checkable
        defaultExpandAll
        selectable={false}
        treeData={treeData}
        checkedKeys={checked}
        onCheck={(keys) => setChecked((Array.isArray(keys) ? keys : keys.checked).map(String))}
        style={{ margin: '8px 0 16px' }}
      />
      {scopedGroups.map((g) => (
        <div key={g.id} style={{ marginBottom: 12 }}>
          <Text>{g.scopeTitle ?? 'Область'} — {g.title}</Text>
          <Select
            mode="tags"
            open={false}
            style={{ width: '100%', marginTop: 4 }}
            placeholder="Пусто — без ограничений"
            value={scopes[g.id] ?? []}
            onChange={(v) => setScopes((s) => ({ ...s, [g.id]: v }))}
          />
        </div>
      ))}
    </Drawer>
  )
}

function AssignmentsDrawer({ role, open, onClose, onChanged }: {
  role?: RoleDto; open: boolean; onClose: () => void; onChanged: () => void
}) {
  const { message } = App.useApp()
  const [items, setItems] = useState<RoleAssignmentDto[]>([])
  const [type, setType] = useState<PrincipalType>('LocalUser')
  const [principal, setPrincipal] = useState<string>()
  const { data: users } = useApi(api.users)
  const { data: keys } = useApi(api.apiKeys)

  const load = async () => role && setItems(await api.assignments.list({ roleId: role.id }))

  const add = async () => {
    if (!role || !principal) return
    try {
      await api.assignments.create(role.id, type, principal)
      setPrincipal(undefined)
      await load()
      onChanged()
    } catch (e) {
      message.error(errorText(e, 'Не удалось назначить роль'))
    }
  }

  const remove = async (a: RoleAssignmentDto) => {
    try {
      await api.assignments.remove(a.id)
      await load()
      onChanged()
    } catch (e) {
      message.error(errorText(e, 'Не удалось снять роль'))
    }
  }

  const options = type === 'LocalUser'
    ? (users ?? []).map((u) => ({ value: u.id, label: u.login }))
    : (keys ?? []).filter((k) => !k.isRevoked).map((k) => ({ value: k.id, label: k.name }))

  return (
    <Drawer title={`Кому назначена «${role?.name ?? ''}»`} open={open} onClose={onClose} width={520} afterOpenChange={(v) => v && load()}>
      <Space.Compact style={{ width: '100%', marginBottom: 16 }}>
        <Select value={type} style={{ width: 190 }} onChange={(v) => { setType(v); setPrincipal(undefined) }}
          options={[{ value: 'LocalUser', label: 'Пользователь WinAdmin' }, { value: 'ApiKey', label: 'API-ключ' }]} />
        <Select value={principal} onChange={setPrincipal} options={options} showSearch optionFilterProp="label"
          placeholder="Выберите" style={{ flex: 1 }} />
        <Button type="primary" icon={<PlusOutlined />} onClick={add} disabled={!principal}>Назначить</Button>
      </Space.Compact>
      <Alert type="info" showIcon style={{ marginBottom: 12 }} message="Пользователи и группы Active Directory появятся после настройки подключения к домену." />
      {items.length === 0 ? <Empty description="Роль никому не назначена" /> : (
        <List
          dataSource={items}
          renderItem={(a) => (
            <List.Item actions={[
              <Popconfirm key="del" title="Снять роль?" onConfirm={() => remove(a)} okText="Снять" cancelText="Отмена">
                <Button size="small" danger icon={<DeleteOutlined />} />
              </Popconfirm>,
            ]}>
              <Space>
                <Tag>{a.principalType === 'LocalUser' ? 'пользователь' : a.principalType === 'ApiKey' ? 'API-ключ' : a.principalType}</Tag>
                {a.displayName}
              </Space>
            </List.Item>
          )}
        />
      )}
    </Drawer>
  )
}

export default function Roles() {
  const { data, loading, refresh } = useApi(api.roles.list)
  const { data: groups } = useApi(api.permissions)
  const { message } = App.useApp()
  const [editing, setEditing] = useState<RoleDto | undefined>()
  const [editorOpen, setEditorOpen] = useState(false)
  const [assigning, setAssigning] = useState<RoleDto | undefined>()

  const titleOf = useMemo(() => {
    const map = new Map<string, string>()
    for (const g of groups ?? []) for (const p of g.permissions) map.set(p.id, `${g.title}: ${p.title}`)
    return (id: string) => map.get(id) ?? id
  }, [groups])

  const remove = async (r: RoleDto) => {
    try {
      await api.roles.remove(r.id)
      message.success('Роль удалена')
      await refresh(true)
    } catch (e) {
      message.error(errorText(e, 'Не удалось удалить роль'))
    }
  }

  return (
    <>
      <PageHeader
        title="Роли"
        subtitle="Наборы прав по модулям; выдавать можно только права, которые есть у вас самих"
        onRefresh={refresh}
        loading={loading}
        extra={<Button type="primary" icon={<PlusOutlined />} onClick={() => { setEditing(undefined); setEditorOpen(true) }}>Создать роль</Button>}
      />
      <Card variant="borderless" className="sp-glass">
        <Table<RoleDto>
          rowKey="id"
          loading={loading}
          dataSource={data ?? []}
          pagination={false}
          columns={[
            {
              title: 'Роль', dataIndex: 'name',
              render: (name: string, r) => (
                <Space direction="vertical" size={0}>
                  <Space>{name}{r.isBuiltin && <Tag color="gold">встроенная</Tag>}</Space>
                  {r.description && <Text type="secondary" style={{ fontSize: 12 }}>{r.description}</Text>}
                </Space>
              ),
            },
            {
              title: 'Права',
              render: (_, r) => r.isBuiltin ? <Tag color="red">все права</Tag> : (
                <Space size={[4, 4]} wrap>
                  {r.permissions.map((p) => (
                    <Tag key={p.permissionId} color={p.scope ? 'purple' : 'blue'}>
                      {titleOf(p.permissionId)}{p.scope ? ` (${p.scope.length} обл.)` : ''}
                    </Tag>
                  ))}
                </Space>
              ),
            },
            { title: 'Назначена', dataIndex: 'assignmentCount', width: 110 },
            {
              title: '', width: 170,
              render: (_, r) => (
                <Space>
                  <Button size="small" icon={<TeamOutlined />} onClick={() => setAssigning(r)}>Кому</Button>
                  {!r.isBuiltin && <Button size="small" icon={<EditOutlined />} onClick={() => { setEditing(r); setEditorOpen(true) }} />}
                  {!r.isBuiltin && (
                    <Popconfirm title="Удалить роль?" description="Все её назначения будут сняты." onConfirm={() => remove(r)} okText="Удалить" okButtonProps={{ danger: true }} cancelText="Отмена">
                      <Button size="small" danger icon={<DeleteOutlined />} />
                    </Popconfirm>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>
      <RoleEditor role={editing} groups={groups ?? []} open={editorOpen} onClose={() => setEditorOpen(false)} onSaved={() => refresh(true)} />
      <AssignmentsDrawer role={assigning} open={Boolean(assigning)} onClose={() => setAssigning(undefined)} onChanged={() => refresh(true)} />
    </>
  )
}
```

- [ ] **Step 3: Routes**

`src/frontend/src/App.tsx`: `import Modules from './pages/Modules'`, `import Roles from './pages/Roles'` и маршруты:

```tsx
          <Route path="/cp/roles" element={<Guard perm="platform.roles.manage"><Roles /></Guard>} />
          <Route path="/cp/modules" element={<Guard perm="platform.modules.manage"><Modules /></Guard>} />
```

- [ ] **Step 4: Build and lint**

Run: `npm --prefix src/frontend run build && npm --prefix src/frontend run lint`
Expected: без ошибок; в новых файлах нет замечаний oxlint (кроме `no-explicit-any` в `catch (e: any)`, если правило в проекте выключено — как в существующих страницах).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src
git commit -m "feat(ui): modules and roles administration pages"
```

---

### Task 13: Документация и проверка в браузере

**Files:**
- Modify: `releases/package/README.md` (разделы «Первый вход», «CLI», новый «Роли и модули»), `releases/package/docs/security.md` (таблица scopes → права и роли), `README.md`, `releases/install-service.ps1` (подсказка `--role`)

- [ ] **Step 1: Docs**

`releases/package/README.md`:
- в «3. Первый вход» команду заменить на `.\WinAdmin.exe user add --login admin --password "ВашНадёжныйПароль" --role Администратор`;
- в «9. CLI» заменить строки со `--scopes`/`set-password` на:

```powershell
.\WinAdmin.exe user add --login operator --password "..." --role "Наблюдатель"
.\WinAdmin.exe user list
.\WinAdmin.exe user set-password --login admin --password "новый"
.\WinAdmin.exe role list
.\WinAdmin.exe role assign --role Администратор --local admin     # аварийное восстановление доступа
```

- после раздела 5а добавить:

```markdown
## 5б. Роли и модули

Права выдаются **ролями**: роль — набор прав вида `модуль.действие` (например `services.manage`),
для модулей с областью (Active Directory) — ещё и список OU. Роли создаются в **Роли**,
назначаются пользователям WinAdmin и API-ключам (позже — пользователям и группам AD).

- Встроенная роль **«Администратор»** — все права, её нельзя изменить или удалить; последнее назначение
  «Администратор» снять нельзя.
- Выдавать можно только права, которые есть у вас самих, и в пределах вашей области.
- **Модули** включаются и выключаются в разделе «Модули»: выключенный модуль скрыт у всех, его API отвечает 404.

При обновлении со старой версии scopes пользователей и ключей автоматически превращаются в роли:
`admin` → «Администратор», остальные наборы — в роли «Импорт: …».
```

`releases/package/docs/security.md`: заголовок «Scopes (для API-ключей и пользователей)» заменить на «Права (назначаются ролями)», в таблице строку `disks.read` удалить (теперь `system.read`), `admin` заменить на «роль «Администратор» — все права», добавить строки `eventlogs.manage` («Исключения учётных записей в журналах») и права ядра `platform.*` одной строкой («Пользователи, роли, модули, API-ключи, аудит, сеть, домен»).

`README.md` (корень) и `releases/install-service.ps1`: заменить `--scopes admin` на `--role Администратор` в подсказках.

- [ ] **Step 2: Full test run**

Run: `dotnet test src/tests/WinAdmin.Tests` (и с `WINADMIN_TEST_POSTGRES` на временном кластере)
Expected: всё PASS.

- [ ] **Step 3: Browser check on a copy-like setup**

Опубликовать сборку во временный каталог и запустить на `127.0.0.1:8090` (как в 1a/Task 9 прошлой ветки), предварительно создав в этом каталоге «старую» установку: `WinAdmin.exe user add --login legacyadmin --password … --scopes admin` **до** первого запуска веб-приложения.

Проверить в браузере (вход `legacyadmin`):
1. Меню содержит все разделы, включая «Роли» и «Модули» (роль «Администратор» назначена при переносе).
2. «Модули»: выключить «Принтеры» → пункт пропадает из меню; прямой переход на `/printers` показывает «Нет доступа»; включить обратно.
3. «Роли»: создать «Наблюдатель» с `services.read` и `system.read`; «Кому» → назначить новому пользователю `viewer` (создать в «Пользователи» с ролью «Наблюдатель»).
4. Выйти, войти как `viewer`: в меню только «Дашборд», «Диски», «Службы»; кнопки управления службами возвращают ошибку 403 (сообщение в UI); `/cp/users` → «Нет доступа».
5. Войти как `legacyadmin`, попытаться снять у себя «Администратор» (единственный) → сообщение 409.

Остановить экземпляр, удалить временные данные.

- [ ] **Step 4: Commit**

```bash
git add releases README.md
git commit -m "docs: roles, modules and role-based CLI"
```
