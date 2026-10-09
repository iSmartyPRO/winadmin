# 3b: модуль «Пользователи AD» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Модуль `ad-users`: список пользователей AD по проектам, карточка с группами и историей, правка атрибутов, фото, перенос между проектами, сброс пароля, увольнение и восстановление — с областью по проектам, аудитом и проверками окружения; плюс выбор проектов в редакторе ролей.

**Architecture:** Определение модуля, настройки, права и шаблоны ролей — в Core; чтение пользователей — `IAdUserDirectory` (LDAP, учётка компьютера); бизнес-логика — `AdUsersService` поверх `IAdUserDirectory` + `IAdWriter` (3a) + `AdGuard` + `ScenarioRunner`; проверки — `AdUsersCheck` с разбором ACL для права Reset Password. Тесты бизнес-логики — на общем фейке `FakeAdDomain` (одновременно каталог и writer, с моделью членства и основной группы).

**Tech Stack:** .NET 10, `System.DirectoryServices.Protocols`, `System.Security.AccessControl`, xUnit + Moq + WebApplicationFactory, React 19 + AntD 6.

**Spec:** `docs/superpowers/specs/2026-10-10-ad-users-folders-design.md` — §1.3 (область в редакторе ролей), §2 (модуль), §4.2 (`ad.rights.users` и проверки `ad-users`), §5–6. Основа — план 3a (`docs/superpowers/plans/2026-10-10-ad-3a-shared-layer-and-environment.md`).

## Global Constraints

- Права: `ad-users.read`, `ad-users.edit`, `ad-users.move`, `ad-users.password` (опасное), `ad-users.offboard` (опасное); все — с областью «Проекты (OU)».
- Перед любой записью — `AdGuard.EnsureManaged` + `AdGuard.EnsureInScope` (3a); 403 до обращения к writer.
- Атрибуты — только из `EditableAttributes` (по умолчанию 10 из Access); длина ≤ 256, `description` ≤ 1024; `mail` — адрес.
- Фото — JPEG/PNG (по сигнатуре) ≤ `PhotoMaxKb` (100).
- Сценарии увольнения/восстановления — порядок шагов как в Access; «Пользователи домена» — по SID домена + RID 513; повторный запуск идемпотентен; остановка на ошибке.
- Сгенерированный пароль — `PasswordGenerator.Generate()` (20 символов), возвращается только оператору, не пишется в аудит/лог.
- Аудит: `user.attributes.update`, `user.photo.update`, `user.photo.remove`, `user.move`, `user.password.reset`, `user.account.deactivate`, `user.account.activate`; `Target` = DN.
- Модуль требует `DomainJoined`; по умолчанию выключен.
- Коммиты заканчиваются `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; после `npm run build` — `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.
- Запись в боевой AD в тестах — только `[AdFact]` внутри `WINADMIN_TEST_AD_ROOT`.

## Review Focus

1. Пользователь с тем же `sAMAccountName` в другой OU вне корня (или в скрытой OU) — действие отклоняется 403, а не применяется к нему (Task 3: `User_outside_root_or_hidden_is_403`).
2. Перенос в проект, где нет `OU=Users`, — 409 с понятным текстом, пользователь не трогается (Task 4: `Move_to_project_without_users_ou_is_409_and_nothing_written`).
3. Сценарий увольнения, где AD отказал на 3-м шаге, — шаги 4–6 «не выполнялся», пользователь не отключён, повторный запуск продолжает (Task 5: `Deactivation_stops_on_failure_and_rerun_completes`).
4. Правка атрибутов без фактических изменений (те же значения, лишние пробелы) — нет записи и нет события аудита (Task 4: `Unchanged_attributes_write_nothing`).
5. Делегат с областью «Проект A» видит в списке только пользователей проекта A, а запрос `?project=` чужого проекта — 403 (Task 3: `List_is_limited_to_scope`).

---

## Карта файлов

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/Modules/ModuleContracts.cs` | + `IModuleLifecycle` |
| `src/backend/WinAdmin.Core/ActiveDirectory/Users/AdUsersModule.cs` | Модуль, настройки, шаблоны ролей |
| `src/backend/WinAdmin.Core/ActiveDirectory/Users/AdUserModels.cs` | `AdUser`, `AdGroupRef`, `AdUserView`, `AdUserCard`, `AdUserStatus`, `ActivationResult` |
| `src/backend/WinAdmin.Core/Security/PermissionIds.cs` | + `AdUsers*` |
| `src/backend/WinAdmin.Core/Modules/BuiltInModules.cs` | + `new AdUsersModule()` в `All` |
| `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` | + `IAdUserDirectory`, `IAdUsersService` |
| `src/backend/WinAdmin.Infrastructure/Modules/ModuleRegistry.cs` | вызов `IModuleLifecycle.OnFirstEnabledAsync` |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/LdapAdUserDirectory.cs` | LDAP-чтение пользователей, групп, фото, SID, ACL |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/LdapValues.cs` | Чистые преобразования значений LDAP |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.cs` | Бизнес-логика модуля |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AclInspector.cs` | Расширенные права по дескриптору безопасности |
| `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/AdUsersCheck.cs` | Проверки `ad-users` + `ad.rights.users` |
| `src/backend/WinAdmin.Infrastructure/Security/AuditService.cs`, `Core/Abstractions/ISecurityServices.cs` | фильтр аудита по `target` |
| `src/backend/WinAdmin.Api/Controllers/AdUsersController.cs` | `/api/v1/ad/users…` |
| `src/backend/WinAdmin.Api/Controllers/AdProjectsController.cs` | `/api/v1/ad/projects` |
| `src/frontend/src/pages/AdUsers.tsx` | Страница «Пользователи AD» |
| `src/frontend/src/pages/Roles.tsx` | Выбор проектов для области «Проекты (OU)» |
| `src/tests/WinAdmin.Tests/Fakes/FakeAdDomain.cs` | Фейк каталога + writer с моделью членства |

## Решения плана

- **Доступность модуля без `RootOu`.** Спец. §1.1 говорит «модуль недоступен, пока не задана корневая OU»; у контракта модулей нет хука доступности по настройкам. Модуль доступен по требованию `DomainJoined`, а операции без корневой OU отвечают 409 «Корневая OU не задана», проверка окружения показывает это первой строкой. Цена ошибки: модуль можно включить заранее — он просто ничего не покажет.
- **Шаблоны ролей:** «AD: отдел кадров» (`read`, `edit`, `move`) и «AD: администраторы пользователей» (все `ad-users.*`). Права папок появятся отдельным шаблоном в 3c (существующую роль 3c не меняет).
- **Уволенные пользователи** (в `TerminatedOuDn`, обычно вне корневой OU) не принадлежат проекту: видеть их и действовать над ними может любой с `ad-users.offboard` (любая область); при восстановлении целевой проект обязан быть в области.
- **История в карточке** — аудит по `Target` = текущий DN (после переноса старые записи с прежним DN в карточке не видны — журнал аудита их содержит).
- **Фото** — без библиотеки обрезки: браузер вписывает изображение в 256×256 и кодирует JPEG (canvas), сервер проверяет сигнатуру и размер.

---

### Task 1: Модуль `ad-users`, права, настройки, шаблоны ролей

**Files:**
- Modify: `src/backend/WinAdmin.Core/Modules/ModuleContracts.cs`
- Modify: `src/backend/WinAdmin.Core/Security/PermissionIds.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Users/AdUsersModule.cs`
- Modify: `src/backend/WinAdmin.Core/Modules/BuiltInModules.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/Modules/ModuleRegistry.cs`
- Test: `src/tests/WinAdmin.Tests/AdUsersModuleTests.cs`

**Interfaces:**
- Consumes: `ProjectScopeProvider` (3a), `IRoleService.ListAsync/CreateAsync`, `PermissionCatalog`, `PermissionEvaluator`, `BuiltInRoles` (1b)
- Produces:
  - `interface IModuleLifecycle { Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct); }`
  - `PermissionIds.AdUsersRead|AdUsersEdit|AdUsersMove|AdUsersPassword|AdUsersOffboard` (`"ad-users.read"` …)
  - `class AdUsersSettings { string FiredGroup; string TerminatedOuDn; List<string> EditableAttributes; int PhotoMaxKb; static IReadOnlyList<string> DefaultAttributes }`
  - `class AdUsersModule : IWinAdminModule, IModuleLifecycle` (`ModuleId = "ad-users"`, `HrRoleName`, `AdminRoleName`)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdUsersModuleTests.cs
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AdUsersModuleTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;

    public AdUsersModuleTests()
    {
        _connection.Open();
        var catalog = new PermissionCatalog(BuiltInModules.All);
        var services = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(catalog)
            .AddSingleton(Mock.Of<IAccessService>())
            .AddSingleton(Mock.Of<IAuditService>())
            .AddScoped<IRoleService, RoleService>()
            .AddSingleton(Mock.Of<IMachineInfo>(m => m.IsDomainJoined == true))
            .AddSingleton(Mock.Of<ISecretProtector>())
            .AddSingleton<IModuleRegistry, ModuleRegistry>();
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Module_declares_scoped_permissions_settings_and_domain_requirement()
    {
        var module = Assert.Single(BuiltInModules.All, m => m.Id == AdUsersModule.ModuleId);
        Assert.Equal(ModuleRequirements.DomainJoined, module.Requirements);
        Assert.False(module.EnabledByDefault);
        Assert.IsType<ProjectScopeProvider>(module.Scope);
        Assert.Equal(typeof(AdUsersSettings), module.SettingsType);
        Assert.All(module.Permissions, p => Assert.True(p.Scopable));
        Assert.True(module.Permissions.Single(p => p.Id == PermissionIds.AdUsersPassword).Dangerous);
        Assert.True(module.Permissions.Single(p => p.Id == PermissionIds.AdUsersOffboard).Dangerous);
        Assert.Equal(10, new AdUsersSettings().EditableAttributes.Count);
    }

    [Fact]
    public async Task First_enable_creates_role_templates_once()
    {
        var registry = _sp.GetRequiredService<IModuleRegistry>();
        await registry.SetEnabledAsync(AdUsersModule.ModuleId, true, "test");
        await registry.SetEnabledAsync(AdUsersModule.ModuleId, false, "test");
        await registry.SetEnabledAsync(AdUsersModule.ModuleId, true, "test");

        using var scope = _sp.CreateScope();
        var roles = await scope.ServiceProvider.GetRequiredService<IRoleService>().ListAsync();
        var hr = Assert.Single(roles, r => r.Name == AdUsersModule.HrRoleName);
        Assert.Equal([PermissionIds.AdUsersEdit, PermissionIds.AdUsersMove, PermissionIds.AdUsersRead],
            hr.Permissions.Select(p => p.PermissionId).Order());
        Assert.Single(roles, r => r.Name == AdUsersModule.AdminRoleName);
    }

    [Fact]
    public async Task Existing_role_with_template_name_is_left_alone()
    {
        using (var scope = _sp.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();
            var system = new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "t", []), "t",
                PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
            await roles.CreateAsync(new SaveRoleRequest(AdUsersModule.HrRoleName, "своя", [new RoleGrantDto(PermissionIds.AdUsersRead, null)]), system);
        }
        await _sp.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, true, "test");

        using var check = _sp.CreateScope();
        var hr = (await check.ServiceProvider.GetRequiredService<IRoleService>().ListAsync()).Single(r => r.Name == AdUsersModule.HrRoleName);
        Assert.Equal("своя", hr.Description);
    }
}
```

Перед запуском сверить конструктор `ModuleRegistry` (3a: `PermissionCatalog, IMachineInfo, IServiceScopeFactory, ISecretProtector, IAccessService`) и `RoleDto`/`SaveRoleRequest`/`RoleGrantDto` (поля `Name`, `Description`, `Permissions[].PermissionId`); при расхождении — подставить фактические имена.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersModuleTests"`
Expected: FAIL — компиляция: нет `AdUsersModule`, `AdUsersSettings`, `PermissionIds.AdUsers*`.

- [ ] **Step 3: Write minimal implementation**

`ModuleContracts.cs` — добавить:

```csharp
/// <summary>Модуль, которому нужно действие при первом включении (например, шаблоны ролей).</summary>
public interface IModuleLifecycle
{
    Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct);
}
```

`PermissionIds.cs`:

```csharp
    public const string AdUsersRead = "ad-users.read";
    public const string AdUsersEdit = "ad-users.edit";
    public const string AdUsersMove = "ad-users.move";
    public const string AdUsersPassword = "ad-users.password";
    public const string AdUsersOffboard = "ad-users.offboard";
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Users/AdUsersModule.cs
using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory.Users;

public sealed class AdUsersSettings
{
    public static IReadOnlyList<string> DefaultAttributes { get; } =
    [
        "displayName", "givenName", "sn", "mail", "department", "title",
        "telephoneNumber", "physicalDeliveryOfficeName", "description", "company",
    ];

    [Description("Группа уволенных (имя или DN)")]
    public string FiredGroup { get; set; } = "";

    [Description("OU уволенных (DN)")]
    public string TerminatedOuDn { get; set; } = "";

    [Description("Редактируемые атрибуты")]
    public List<string> EditableAttributes { get; set; } = [.. DefaultAttributes];

    [Description("Максимальный размер фото, КБ")]
    public int PhotoMaxKb { get; set; } = 100;
}

/// <summary>Пользователи Active Directory: просмотр, атрибуты, фото, перенос, пароль, увольнение.</summary>
public sealed class AdUsersModule : IWinAdminModule, IModuleLifecycle
{
    public const string ModuleId = "ad-users";
    public const string HrRoleName = "AD: отдел кадров";
    public const string AdminRoleName = "AD: администраторы пользователей";

    public string Id => ModuleId;
    public string Title => "Пользователи AD";
    public string? Description => "Пользователи Active Directory по проектам: атрибуты, фото, перенос, пароль, увольнение";
    public ModuleRequirements Requirements => ModuleRequirements.DomainJoined;
    public bool EnabledByDefault => false;
    public Type? SettingsType => typeof(AdUsersSettings);
    public IScopeProvider? Scope { get; } = new ProjectScopeProvider();

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(PermissionIds.AdUsersRead, "Просмотр пользователей", "Список, карточка, группы, история", Scopable: true),
        new(PermissionIds.AdUsersEdit, "Атрибуты и фото", "ФИО, почта, отдел, должность, телефон, офис, описание, компания, фото", Scopable: true),
        new(PermissionIds.AdUsersMove, "Перенос между проектами", Scopable: true),
        new(PermissionIds.AdUsersPassword, "Сброс пароля", Scopable: true, Dangerous: true),
        new(PermissionIds.AdUsersOffboard, "Увольнение и восстановление", Scopable: true, Dangerous: true),
    ];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public async Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct)
    {
        var roles = services.GetRequiredService<IRoleService>();
        var catalog = services.GetRequiredService<PermissionCatalog>();
        var system = new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "system", []), "system",
            PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
        var existing = (await roles.ListAsync(ct)).Select(r => r.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        if (!existing.Contains(HrRoleName))
            await roles.CreateAsync(new SaveRoleRequest(HrRoleName, "Шаблон: просмотр, правка атрибутов и перенос пользователей AD",
                [new RoleGrantDto(PermissionIds.AdUsersRead, null), new RoleGrantDto(PermissionIds.AdUsersEdit, null),
                 new RoleGrantDto(PermissionIds.AdUsersMove, null)]), system, ct);
        if (!existing.Contains(AdminRoleName))
            await roles.CreateAsync(new SaveRoleRequest(AdminRoleName, "Шаблон: все действия с пользователями AD",
                Permissions.Select(p => new RoleGrantDto(p.Id, null)).ToList()), system, ct);
    }
}
```

(Проверить: `AccessContext` и `IRoleService.ListAsync/CreateAsync` находятся в Core и имеют `CancellationToken` последним параметром; `SaveRoleRequest`/`RoleGrantDto` — в `WinAdmin.Core.Models`.)

`BuiltInModules.All` — добавить последним элементом `new ActiveDirectory.Users.AdUsersModule(),` (тип списка — `IReadOnlyList<IWinAdminModule>`).

`ModuleRegistry.SetEnabledAsync` — запомнить, был ли модуль когда-либо сохранён, и после `UpsertAsync`/аудита вызвать хук:

```csharp
    public async Task SetEnabledAsync(string id, bool enabled, string actor, CancellationToken ct = default)
    {
        var module = Find(id);
        var (available, reason) = Check(module);
        if (enabled && !available)
            throw new InvalidOperationException($"Модуль «{module.Title}» недоступен на этой машине: {reason}");

        bool firstEnable = enabled && !Rows().ContainsKey(id);
        await UpsertAsync(id, row => row.Enabled = enabled, actor, ct);
        await AuditAsync(actor, enabled ? "module.enable" : "module.disable", id, null, ct);

        if (firstEnable && module is IModuleLifecycle lifecycle)
        {
            using var scope = scopes.CreateScope();
            await lifecycle.OnFirstEnabledAsync(scope.ServiceProvider, ct);
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersModuleTests|FullyQualifiedName~ModuleRegistry|FullyQualifiedName~Permission"`
Expected: PASS (новые 3 + существующие тесты реестра/прав).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdUsersModuleTests.cs
git commit -m "feat(ad-users): module definition, permissions, settings and role templates"
```

---

### Task 2: Каталог пользователей (`IAdUserDirectory`) и фейк домена

**Files:**
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Users/AdUserModels.cs`
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` (+ `IAdUserDirectory`)
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/LdapValues.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/LdapAdUserDirectory.cs`
- Create: `src/tests/WinAdmin.Tests/Fakes/FakeAdDomain.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/LdapAdUserDirectoryTests.cs`

**Interfaces:**
- Consumes: `LdapConnections`, `AdCredentials` (3a), `IDirectorySettingsStore`, `IAdStructureStore`, `LdapMapping.SidFromBytes/IsEnabled`, `LdapFilter` (1c)
- Produces:
  - `record AdUser(string Sam, string Dn, string Sid, string? DisplayName, bool Enabled, DateTimeOffset? LastLogon, DateTimeOffset? WhenCreated, bool HasPhoto, int PrimaryGroupId, IReadOnlyDictionary<string, string?> Attributes)`
  - `record AdGroupRef(string Dn, string Name, string? Sid, bool IsPrimary)`
  - `IAdUserDirectory`:
    - `ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, ct) → IReadOnlyList<AdUser>` (поддерево)
    - `FindUserAsync(string sam, IReadOnlyCollection<string> attributes, ct) → AdUser?` (весь домен)
    - `GetGroupsAsync(AdUser user, ct) → IReadOnlyList<AdGroupRef>` (прямые `memberOf` + основная)
    - `GetPhotoAsync(string dn, ct) → byte[]?`
    - `FindGroupAsync(string nameOrDn, ct) → AdGroupRef?`
    - `GetDomainUsersGroupAsync(ct) → AdGroupRef` (RID 513)
    - `FindSampleUserAsync(string ouDn, ct) → string?` (DN)
    - `GetWriterSidsAsync(ct) → IReadOnlyList<string>` (SID учётки записи + её группы + `S-1-1-0`, `S-1-5-11`)
    - `ReadSecurityDescriptorAsync(string dn, ct) → byte[]?` (DACL)
  - `LdapValues.FileTime(string?) → DateTimeOffset?`, `GeneralizedTime(string?) → DateTimeOffset?`, `DomainSid(string userSid) → string`
  - `FakeAdDomain : IAdUserDirectory, IAdWriter` (тесты): `AddUser(sam, projectOuDn, params string[] groupDns)`, `AddGroup(name, ouDn, rid?)`, `Users`, `Groups`, `Calls`, `FailOn`, `DomainSid`, `WriterSids`, `SecurityDescriptors`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/LdapAdUserDirectoryTests.cs
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory.Users;

namespace WinAdmin.Tests;

public sealed class LdapValuesTests
{
    [Fact]
    public void File_time_and_never()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
            LdapValues.FileTime(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero).ToFileTime().ToString()));
        Assert.Null(LdapValues.FileTime("0"));
        Assert.Null(LdapValues.FileTime("9223372036854775807"));
        Assert.Null(LdapValues.FileTime(null));
    }

    [Fact]
    public void Generalized_time()
        => Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 30, 5, TimeSpan.Zero), LdapValues.GeneralizedTime("20261009123005.0Z"));

    [Fact]
    public void Domain_sid_from_user_sid()
        => Assert.Equal("S-1-5-21-1-2-3", LdapValues.DomainSid("S-1-5-21-1-2-3-1105"));
}

public sealed class LdapAdUserDirectoryTests
{
    [Fact]
    public async Task Directory_off_is_unavailable()
    {
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(DirectorySettings.Disabled));
        var users = new LdapAdUserDirectory(dir, Mock.Of<IAdStructureStore>());
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => users.FindUserAsync("ivan", []));
    }

    private static LdapAdUserDirectory Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));
        var cred = new AdWriteCredential(AdWriteMode.ServiceAccount, E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"));
        var st = Mock.Of<IAdStructureStore>(m => m.GetWriteCredentialAsync(It.IsAny<CancellationToken>()) == Task.FromResult(cred));
        return new LdapAdUserDirectory(dir, st, new System.Net.NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
    }

    [AdFact]
    public async Task Real_directory_reads_users_groups_and_domain_users()
    {
        var users = Real();
        string root = Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_ROOT")!;
        var list = await users.ListUsersAsync(root, ["mail", "department"]);
        var me = await users.FindUserAsync(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_USER")!, ["mail"]);
        Assert.NotNull(me);
        Assert.Contains(await users.GetGroupsAsync(me!), g => g.IsPrimary);
        Assert.EndsWith("-513", (await users.GetDomainUsersGroupAsync()).Sid);
        Assert.NotEmpty(await users.GetWriterSidsAsync());
        Assert.NotNull(await users.ReadSecurityDescriptorAsync(me!.Dn));
        _ = list;
    }
}
```

`[AdFact]` — из 1c.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapValuesTests|FullyQualifiedName~LdapAdUserDirectoryTests"`
Expected: FAIL — компиляция: нет `LdapValues`, `LdapAdUserDirectory`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Users/AdUserModels.cs
namespace WinAdmin.Core.ActiveDirectory.Users;

/// <summary>Пользователь AD; Attributes — запрошенные атрибуты (ключи — имена LDAP без учёта регистра).</summary>
public sealed record AdUser(
    string Sam, string Dn, string Sid, string? DisplayName, bool Enabled,
    DateTimeOffset? LastLogon, DateTimeOffset? WhenCreated, bool HasPhoto, int PrimaryGroupId,
    IReadOnlyDictionary<string, string?> Attributes)
{
    public string? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;
}

public sealed record AdGroupRef(string Dn, string Name, string? Sid, bool IsPrimary);

public enum AdUserStatus { Active, Disabled, Terminated, All }

/// <summary>Пользователь для UI: проект (или «уволен»), атрибуты.</summary>
public sealed record AdUserView(
    string Sam, string Dn, string? DisplayName, bool Enabled, DateTimeOffset? LastLogon, DateTimeOffset? WhenCreated,
    bool HasPhoto, string? ProjectDn, string? ProjectName, bool Terminated, IReadOnlyDictionary<string, string?> Attributes);

public sealed record AdUserCard(AdUserView User, IReadOnlyList<AdGroupRef> Groups);

public sealed record ActivationResult(IReadOnlyList<Operations.ScenarioStep> Steps, string? Password);
```

`IAdServices.cs` — добавить:

```csharp
/// <summary>Чтение пользователей и групп для модуля «Пользователи AD» (учётка компьютера).</summary>
public interface IAdUserDirectory
{
    Task<IReadOnlyList<AdUser>> ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, CancellationToken ct = default);
    Task<AdUser?> FindUserAsync(string sam, IReadOnlyCollection<string> attributes, CancellationToken ct = default);
    Task<IReadOnlyList<AdGroupRef>> GetGroupsAsync(AdUser user, CancellationToken ct = default);
    Task<byte[]?> GetPhotoAsync(string dn, CancellationToken ct = default);
    Task<AdGroupRef?> FindGroupAsync(string nameOrDn, CancellationToken ct = default);
    Task<AdGroupRef> GetDomainUsersGroupAsync(CancellationToken ct = default);
    Task<string?> FindSampleUserAsync(string ouDn, CancellationToken ct = default);
    /// <summary>SID учётки записи, её групп (tokenGroups) и Everyone / Authenticated Users — для разбора ACL.</summary>
    Task<IReadOnlyList<string>> GetWriterSidsAsync(CancellationToken ct = default);
    Task<byte[]?> ReadSecurityDescriptorAsync(string dn, CancellationToken ct = default);
}
```

(`using WinAdmin.Core.ActiveDirectory.Users;` в начало файла.)

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/LdapValues.cs
using System.Globalization;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public static class LdapValues
{
    /// <summary>FILETIME (lastLogonTimestamp и т.п.); 0 и «никогда» → null.</summary>
    public static DateTimeOffset? FileTime(string? value)
        => long.TryParse(value, out long ft) && ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc()
            ? DateTimeOffset.FromFileTime(ft).ToUniversalTime() : null;

    /// <summary>GeneralizedTime «20261009123005.0Z» → UTC.</summary>
    public static DateTimeOffset? GeneralizedTime(string? value)
        => value is { Length: >= 14 } && DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? new DateTimeOffset(dt, TimeSpan.Zero) : null;

    public static string DomainSid(string userSid) => userSid[..userSid.LastIndexOf('-')];
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/LdapAdUserDirectory.cs
using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

/// <summary>Пользователи и группы AD (учётка компьютера; readCredential — только для тестов вне домена).</summary>
public sealed class LdapAdUserDirectory(
    IDirectorySettingsStore directory, IAdStructureStore structure, NetworkCredential? readCredential = null) : IAdUserDirectory
{
    private const string UserFilter = "(objectCategory=person)(objectClass=user)";
    private static readonly string[] Base =
        ["sAMAccountName", "distinguishedName", "objectSid", "displayName", "userAccountControl", "lastLogonTimestamp",
         "whenCreated", "primaryGroupID", "thumbnailPhoto"];

    public async Task<IReadOnlyList<AdUser>> ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c => LdapConnections
            .SearchPaged(c, baseDn, $"(&{UserFilter})", SearchScope.Subtree, [.. Base, .. attributes])
            .Select(e => ToUser(e, attributes)).OfType<AdUser>().ToList());
    }

    public async Task<AdUser?> FindUserAsync(string sam, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            var response = LdapConnections.Search(c, new SearchRequest(baseDn,
                $"(&{UserFilter}(sAMAccountName={LdapFilter.Escape(sam)}))", SearchScope.Subtree, [.. Base, .. attributes]) { SizeLimit = 2 });
            return response.Entries.Count == 1 ? ToUser(response.Entries[0], attributes) : null;
        });
    }

    public async Task<IReadOnlyList<AdGroupRef>> GetGroupsAsync(AdUser user, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            var groups = new List<AdGroupRef>();
            var entry = ((SearchResponse)c.SendRequest(new SearchRequest(user.Dn, "(objectClass=*)", SearchScope.Base, "memberOf"))).Entries[0];
            foreach (string dn in entry.Attributes["memberOf"]?.GetValues(typeof(string)).Cast<string>() ?? [])
                groups.Add(new AdGroupRef(dn, DnUtils.FirstValue(dn), null, false));
            string primarySid = $"{LdapValues.DomainSid(user.Sid)}-{user.PrimaryGroupId}";
            if (FindBySid(c, dir, primarySid) is { } primary) groups.Insert(0, primary with { IsPrimary = true });
            return (IReadOnlyList<AdGroupRef>)groups.OrderByDescending(g => g.IsPrimary).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });
    }

    public async Task<byte[]?> GetPhotoAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
            ((SearchResponse)c.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "thumbnailPhoto")))
            .Entries[0].Attributes["thumbnailPhoto"]?[0] as byte[]);
    }

    public async Task<AdGroupRef?> FindGroupAsync(string nameOrDn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        string value = nameOrDn.Trim();
        return Run(dir, readCredential, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            string filter = value.Contains('=')
                ? $"(&(objectCategory=group)(distinguishedName={LdapFilter.Escape(value)}))"
                : $"(&(objectCategory=group)(|(sAMAccountName={LdapFilter.Escape(value)})(cn={LdapFilter.Escape(value)})))";
            var response = LdapConnections.Search(c, new SearchRequest(baseDn, filter, SearchScope.Subtree, "objectSid", "cn") { SizeLimit = 2 });
            return response.Entries.Count == 1 ? ToGroup(response.Entries[0]) : null;
        });
    }

    public async Task<AdGroupRef> GetDomainUsersGroupAsync(CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            string nc = LdapConnections.NamingContext(c);
            var domain = ((SearchResponse)c.SendRequest(new SearchRequest(nc, "(objectClass=*)", SearchScope.Base, "objectSid"))).Entries[0];
            string domainSid = LdapMapping.SidFromBytes((byte[])domain.Attributes["objectSid"][0]);
            return FindBySid(c, dir, domainSid + "-513")
                   ?? throw new InvalidOperationException("Группа «Пользователи домена» (RID 513) не найдена.");
        });
    }

    public async Task<string?> FindSampleUserAsync(string ouDn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            var response = LdapConnections.Search(c, new SearchRequest(ouDn, $"(&{UserFilter})", SearchScope.Subtree, "distinguishedName") { SizeLimit = 1 });
            return response.Entries.Count > 0 ? response.Entries[0].DistinguishedName : null;
        });
    }

    public async Task<IReadOnlyList<string>> GetWriterSidsAsync(CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        var credential = await structure.GetWriteCredentialAsync(ct);
        return Run(dir, readCredential, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            string filter = credential.Mode == AdWriteMode.ProcessAccount
                ? $"(&(objectCategory=computer)(sAMAccountName={LdapFilter.Escape(System.Environment.MachineName + "$")}))"
                : $"(&{UserFilter}(sAMAccountName={LdapFilter.Escape(DirectoryLogin.Parse(credential.Login ?? "").Sam)}))";
            var found = LdapConnections.Search(c, new SearchRequest(baseDn, filter, SearchScope.Subtree, "distinguishedName", "objectSid") { SizeLimit = 1 });
            var sids = new List<string> { "S-1-1-0", "S-1-5-11" };
            if (found.Entries.Count == 0) return (IReadOnlyList<string>)sids;
            var account = found.Entries[0];
            sids.Add(LdapMapping.SidFromBytes((byte[])account.Attributes["objectSid"][0]));
            var tokens = ((SearchResponse)c.SendRequest(new SearchRequest(account.DistinguishedName, "(objectClass=*)", SearchScope.Base, "tokenGroups"))).Entries[0];
            sids.AddRange(tokens.Attributes["tokenGroups"]?.GetValues(typeof(byte[])).Cast<byte[]>().Select(LdapMapping.SidFromBytes) ?? []);
            return sids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public async Task<byte[]?> ReadSecurityDescriptorAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            var request = new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "nTSecurityDescriptor");
            request.Controls.Add(new SecurityDescriptorFlagControl(System.DirectoryServices.Protocols.SecurityMasks.Dacl));
            return ((SearchResponse)c.SendRequest(request)).Entries[0].Attributes["nTSecurityDescriptor"]?[0] as byte[];
        });
    }

    private static AdGroupRef? FindBySid(LdapConnection c, DirectorySettings dir, string sid)
    {
        string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
        var response = LdapConnections.Search(c, new SearchRequest(baseDn, $"(objectSid={LdapFilter.Sid(sid)})", SearchScope.Subtree, "objectSid", "cn") { SizeLimit = 1 });
        return response.Entries.Count == 1 ? ToGroup(response.Entries[0]) : null;
    }

    private static AdGroupRef ToGroup(SearchResultEntry e)
        => new(e.DistinguishedName, e.Attributes["cn"]?[0] as string ?? DnUtils.FirstValue(e.DistinguishedName),
            e.Attributes["objectSid"]?[0] is byte[] sid ? LdapMapping.SidFromBytes(sid) : null, false);

    private static AdUser? ToUser(SearchResultEntry e, IReadOnlyCollection<string> attributes)
    {
        if (e.Attributes["objectSid"]?[0] is not byte[] sid) return null;
        string? Str(string name) => e.Attributes[name]?[0] as string;
        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes) attrs[a] = Str(a);
        return new AdUser(
            Str("sAMAccountName") ?? "", e.DistinguishedName, LdapMapping.SidFromBytes(sid), Str("displayName"),
            LdapMapping.IsEnabled(Str("userAccountControl")), LdapValues.FileTime(Str("lastLogonTimestamp")),
            LdapValues.GeneralizedTime(Str("whenCreated")), e.Attributes["thumbnailPhoto"] is { Count: > 0 },
            int.TryParse(Str("primaryGroupID"), out int pg) ? pg : 513, attrs);
    }

    private async Task<DirectorySettings> EnabledAsync(CancellationToken ct)
    {
        var dir = await directory.GetAsync(ct);
        if (!dir.Enabled || dir.Domain is null) throw new DirectoryUnavailableException("Подключение к домену выключено");
        return dir;
    }

    private static T Run<T>(DirectorySettings dir, NetworkCredential? credential, Func<LdapConnection, T> action)
    {
        try
        {
            using var connection = LdapConnections.Open(dir, credential);
            return action(connection);
        }
        catch (LdapException ex)
        {
            throw LdapConnections.Unavailable(ex);
        }
    }
}
```

`ListUsersAsync` запрашивает `[.. Base, .. attributes]` — вместе с `thumbnailPhoto`, чтобы знать `HasPhoto` (до ~100 КБ на пользователя с фото; для нескольких тысяч пользователей допустимо; если на боевом домене медленно — Ruling: `HasPhoto` отдельным запросом).

`DependencyInjection`:

```csharp
        services.AddSingleton<IAdUserDirectory>(sp => new LdapAdUserDirectory(
            sp.GetRequiredService<IDirectorySettingsStore>(), sp.GetRequiredService<IAdStructureStore>()));
```

```csharp
// src/tests/WinAdmin.Tests/Fakes/FakeAdDomain.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;

namespace WinAdmin.Tests.Fakes;

/// <summary>
/// Домен в памяти: каталог (IAdUserDirectory) и запись (IAdWriter) над одним состоянием.
/// Основная группа моделируется как в AD: смена primaryGroupID делает прежнюю основную группу обычным членством.
/// </summary>
public sealed class FakeAdDomain : IAdUserDirectory, IAdWriter
{
    public const string DomainSid = "S-1-5-21-7-8-9";
    private int _rid = 2000;

    public sealed class User
    {
        public required string Sam { get; init; }
        public required string Dn { get; set; }
        public required string Sid { get; init; }
        public bool Enabled { get; set; } = true;
        public int PrimaryGroupId { get; set; } = 513;
        public Dictionary<string, string?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[]? Photo { get; set; }
        public string? Password { get; set; }
        public bool MustChange { get; set; }
    }

    public sealed class Group
    {
        public required string Name { get; init; }
        public required string Dn { get; init; }
        public required string Sid { get; init; }
        public HashSet<string> Members { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public List<User> Users { get; } = [];
    public List<Group> Groups { get; } = [];
    public List<string> Calls { get; } = [];
    /// <summary>Операция (например «RemoveMember:CN=g,…») → исключение при вызове.</summary>
    public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExistingOus { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> WriterSids { get; } = ["S-1-1-0", "S-1-5-11", DomainSid + "-1500"];
    public Dictionary<string, byte[]> SecurityDescriptors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeAdDomain() => AddGroup("Пользователи домена", "CN=Users,DC=test,DC=local", 513);

    public Group DomainUsers => Groups.Single(g => g.Sid.EndsWith("-513"));

    public Group AddGroup(string name, string ouDn, int? rid = null)
    {
        var g = new Group { Name = name, Dn = $"CN={name},{ouDn}", Sid = $"{DomainSid}-{rid ?? ++_rid}" };
        Groups.Add(g);
        return g;
    }

    public User AddUser(string sam, string ouDn, params string[] groupDns)
    {
        var u = new User { Sam = sam, Dn = $"CN={sam},{ouDn}", Sid = $"{DomainSid}-{++_rid}" };
        u.Attributes["displayName"] = sam.ToUpperInvariant();
        Users.Add(u);
        foreach (var dn in groupDns) Groups.Single(g => g.Dn == dn).Members.Add(u.Dn);
        return u;
    }

    public User U(string sam) => Users.Single(u => u.Sam == sam);

    private void Call(string op, string target)
    {
        string key = $"{op}:{target}";
        Calls.Add(key);
        if (FailOn.TryGetValue(key, out var ex)) throw ex;
    }

    private AdUser ToAd(User u, IReadOnlyCollection<string> attributes)
    {
        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes) attrs[a] = u.Attributes.GetValueOrDefault(a);
        return new AdUser(u.Sam, u.Dn, u.Sid, u.Attributes.GetValueOrDefault("displayName"), u.Enabled, null, null,
            u.Photo is not null, u.PrimaryGroupId, attrs);
    }

    // ── IAdUserDirectory ─────────────────────────────────────────
    public Task<IReadOnlyList<AdUser>> ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AdUser>>(Users.Where(u => DnUtils.IsUnderOrSame(u.Dn, baseDn)).Select(u => ToAd(u, attributes)).ToList());

    public Task<AdUser?> FindUserAsync(string sam, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Sam.Equals(sam, StringComparison.OrdinalIgnoreCase)) is { } u ? ToAd(u, attributes) : null);

    public Task<IReadOnlyList<AdGroupRef>> GetGroupsAsync(AdUser user, CancellationToken ct = default)
    {
        var groups = Groups.Where(g => g.Members.Contains(user.Dn)).Select(g => new AdGroupRef(g.Dn, g.Name, g.Sid, false)).ToList();
        var primary = Groups.FirstOrDefault(g => g.Sid == $"{DomainSid}-{user.PrimaryGroupId}");
        if (primary is not null) groups.Insert(0, new AdGroupRef(primary.Dn, primary.Name, primary.Sid, true));
        return Task.FromResult<IReadOnlyList<AdGroupRef>>(groups);
    }

    public Task<byte[]?> GetPhotoAsync(string dn, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Dn == dn)?.Photo);

    public Task<AdGroupRef?> FindGroupAsync(string nameOrDn, CancellationToken ct = default)
        => Task.FromResult(Groups.FirstOrDefault(g => g.Dn.Equals(nameOrDn, StringComparison.OrdinalIgnoreCase)
                                                     || g.Name.Equals(nameOrDn, StringComparison.OrdinalIgnoreCase))
            is { } g ? new AdGroupRef(g.Dn, g.Name, g.Sid, false) : null);

    public Task<AdGroupRef> GetDomainUsersGroupAsync(CancellationToken ct = default)
        => Task.FromResult(new AdGroupRef(DomainUsers.Dn, DomainUsers.Name, DomainUsers.Sid, false));

    public Task<string?> FindSampleUserAsync(string ouDn, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => DnUtils.IsUnderOrSame(u.Dn, ouDn))?.Dn);

    public Task<IReadOnlyList<string>> GetWriterSidsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(WriterSids);

    public Task<byte[]?> ReadSecurityDescriptorAsync(string dn, CancellationToken ct = default)
        => Task.FromResult(SecurityDescriptors.GetValueOrDefault(dn));

    // ── IAdWriter ────────────────────────────────────────────────
    private User ByDn(string dn) => Users.FirstOrDefault(u => u.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase))
                                    ?? throw new AdWriteException(32, "Объект не найден в AD.");

    public Task ModifyAttributesAsync(string dn, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
    {
        Call("Modify", dn);
        var u = ByDn(dn);
        foreach (var (k, v) in changes) u.Attributes[k] = string.IsNullOrEmpty(v) ? null : v;
        return Task.CompletedTask;
    }

    public Task<bool> AddMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
    {
        Call("AddMember", groupDn);
        return Task.FromResult(Groups.Single(g => g.Dn == groupDn).Members.Add(memberDn));
    }

    public Task<bool> RemoveMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
    {
        Call("RemoveMember", groupDn);
        var g = Groups.Single(x => x.Dn == groupDn);
        var u = Users.FirstOrDefault(x => x.Dn == memberDn);
        if (u is not null && g.Sid == $"{DomainSid}-{u.PrimaryGroupId}")
            throw new AdWriteException(53, "AD отклонил операцию: нельзя удалить из основной группы.");
        return Task.FromResult(g.Members.Remove(memberDn));
    }

    public Task SetPrimaryGroupAsync(string userDn, string groupSid, CancellationToken ct = default)
    {
        Call("SetPrimary", userDn);
        var u = ByDn(userDn);
        var target = Groups.Single(g => g.Sid == groupSid);
        if (!target.Members.Contains(userDn)) throw new AdWriteException(53, "Пользователь не состоит в группе.");
        var old = Groups.FirstOrDefault(g => g.Sid == $"{DomainSid}-{u.PrimaryGroupId}");
        old?.Members.Add(userDn);           // прежняя основная — теперь обычное членство
        target.Members.Remove(userDn);      // основная группа не хранится в member
        u.PrimaryGroupId = int.Parse(groupSid[(groupSid.LastIndexOf('-') + 1)..]);
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(string userDn, bool enabled, CancellationToken ct = default)
    {
        Call(enabled ? "Enable" : "Disable", userDn);
        ByDn(userDn).Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task ResetPasswordAsync(string userDn, string password, bool mustChange, CancellationToken ct = default)
    {
        Call("ResetPassword", userDn);
        var u = ByDn(userDn);
        u.Password = password;
        u.MustChange = mustChange;
        return Task.CompletedTask;
    }

    public Task<string> MoveAsync(string dn, string targetOuDn, CancellationToken ct = default)
    {
        Call("Move", dn);
        var u = ByDn(dn);
        string newDn = $"{DnUtils.Split(dn)[0]},{targetOuDn}";
        foreach (var g in Groups.Where(g => g.Members.Remove(dn))) g.Members.Add(newDn);
        u.Dn = newDn;
        return Task.FromResult(newDn);
    }

    public Task<string> CreateGroupAsync(string ouDn, string cn, string description, CancellationToken ct = default)
    {
        Call("CreateGroup", ouDn);
        return Task.FromResult(AddGroup(cn, ouDn).Dn);
    }

    public Task SetPhotoAsync(string userDn, byte[]? photo, CancellationToken ct = default)
    {
        Call(photo is null ? "RemovePhoto" : "SetPhoto", userDn);
        ByDn(userDn).Photo = photo;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapValuesTests|FullyQualifiedName~LdapAdUserDirectoryTests"`
Expected: PASS (интеграционный — Skip).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(ad-users): LDAP user directory (users, groups, photo, domain users, writer SIDs, ACL) and fake domain"
```

---

### Task 3: AdUsersService — чтение (список, карточка, история)

**Files:**
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` (+ `IAdUsersService`)
- Modify: `src/backend/WinAdmin.Core/Abstractions/ISecurityServices.cs`, `src/backend/WinAdmin.Infrastructure/Security/AuditService.cs`, `src/backend/WinAdmin.Api/Controllers/AuditController.cs` (фильтр `target`)
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/AdUsersServiceTests.cs`

**Interfaces:**
- Consumes: `IAdUserDirectory`, `FakeAdDomain` (Task 2); `IAdReader.ListProjectsAsync/ExistsAsync`, `IAdWriter`, `IAdStructureStore`, `AdGuard`, `ScenarioRunner`, `PasswordGenerator` (3a); `IModuleRegistry.GetSettingsAsync<AdUsersSettings>`; `IAuditService`
- Produces:
  - `IAuditService.QueryAsync(int limit = 200, string? actor = null, string? target = null, CancellationToken ct = default)`
  - `IAdUsersService` — в Task 3 только методы чтения; Task 4–5 добавляют методы записи в интерфейс и класс (без заглушек):
    `ListAsync(IAccessContext actor, string? projectDn, AdUserStatus status, string? q, ct) → IReadOnlyList<AdUserView>`; `GetAsync(actor, sam, ct) → AdUserCard`; `GetPhotoAsync(actor, sam, ct) → byte[]?`; `HistoryAsync(actor, sam, ct) → IReadOnlyList<AuditEntryDto>`
  - `AdUsersService(IAdUserDirectory directory, IAdWriter writer, IAdReader reader, IAdStructureStore structure, IModuleRegistry modules, IAuditService audit, ILogger<AdUsersService> logger)` (scoped)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdUsersServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Users;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

/// <summary>Общая подготовка: корень OU=Accounts, проекты A и B, скрытый IT, OU уволенных вне корня.</summary>
public abstract class AdUsersServiceTestBase
{
    protected const string Root = "OU=Accounts,DC=test,DC=local";
    protected const string A = "OU=A," + Root;
    protected const string B = "OU=B," + Root;
    protected const string Fired = "OU=FiredUsers,DC=test,DC=local";
    protected static readonly PermissionCatalog Catalog = new(BuiltInModules.All);

    protected readonly FakeAdDomain Ad = new();
    protected readonly FakeAdReader Reader = new();
    protected readonly Mock<IAuditService> Audit = new();
    protected readonly List<AuditEntryDto> Audited = [];
    protected AdUsersSettings Settings = new() { FiredGroup = "Fired Users", TerminatedOuDn = Fired };
    protected AdStructureSettings Structure = AdStructureSettings.Default with { RootOu = Root, HiddenOus = ["IT"] };
    protected readonly AdUsersService Service;

    protected AdUsersServiceTestBase()
    {
        Reader.Projects.AddRange([new AdProject(A, "A"), new AdProject(B, "B")]);
        Reader.ExistingDns.UnionWith([Root, A, B, "OU=Users," + A, "OU=Users," + B, Fired]);
        Ad.AddGroup("Fired Users", "OU=Groups," + Root);
        Ad.AddGroup("sg_a_docs_full", A);
        Ad.AddUser("ivan", "OU=Users," + A, "CN=sg_a_docs_full," + A);
        Ad.AddUser("petr", "OU=Users," + B);
        Ad.AddUser("admin2", "OU=IT," + Root);
        Ad.AddUser("old", Fired);
        Ad.U("old").Enabled = false;
        Ad.DomainUsers.Members.Remove(Ad.U("old").Dn);

        var structure = new Mock<IAdStructureStore>();
        structure.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Structure);
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Settings);
        Audit.Setup(a => a.WriteAsync(It.IsAny<AuditEntryDto>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntryDto, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Audit.Setup(a => a.QueryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string? _, string? target, CancellationToken _) => Audited.Where(e => e.Target == target).ToList());
        Service = new AdUsersService(Ad, Ad, Reader, structure.Object, modules.Object, Audit.Object, NullLogger<AdUsersService>.Instance);
    }

    protected static IAccessContext Actor(params (string Permission, string[]? Projects)[] grants)
        => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "op", []), "op",
            PermissionEvaluator.Evaluate([new RoleSnapshot("r", false,
                grants.Select(g => new RoleGrant(g.Permission, g.Projects is null ? null : new ScopeDefinition(g.Projects))).ToList())], Catalog));

    protected static IAccessContext All(params string[] permissions)
        => Actor(permissions.Select(p => (p, (string[]?)null)).ToArray());
}

public sealed class AdUsersReadTests : AdUsersServiceTestBase
{
    [Fact]
    public async Task List_shows_managed_users_with_projects_and_hides_hidden_ou_and_terminated()
    {
        var list = await Service.ListAsync(All(PermissionIds.AdUsersRead), null, AdUserStatus.All, null, default);
        Assert.Equal(["ivan", "petr"], list.Select(u => u.Sam).Order());
        Assert.Equal("A", list.Single(u => u.Sam == "ivan").ProjectName);
    }

    [Fact]
    public async Task List_is_limited_to_scope()
    {
        var delegateA = Actor((PermissionIds.AdUsersRead, [A]));
        Assert.Equal(["ivan"], (await Service.ListAsync(delegateA, null, AdUserStatus.All, null, default)).Select(u => u.Sam));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ListAsync(delegateA, B, AdUserStatus.All, null, default));
    }

    [Fact]
    public async Task Status_and_search_filters()
    {
        Ad.U("petr").Enabled = false;
        var actor = All(PermissionIds.AdUsersRead);
        Assert.Equal(["ivan"], (await Service.ListAsync(actor, null, AdUserStatus.Active, null, default)).Select(u => u.Sam));
        Assert.Equal(["petr"], (await Service.ListAsync(actor, null, AdUserStatus.Disabled, null, default)).Select(u => u.Sam));
        Assert.Equal(["ivan"], (await Service.ListAsync(actor, null, AdUserStatus.All, "IVA", default)).Select(u => u.Sam));
    }

    [Fact]
    public async Task Terminated_list_requires_offboard()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service.ListAsync(All(PermissionIds.AdUsersRead), null, AdUserStatus.Terminated, null, default));
        var list = await Service.ListAsync(Actor((PermissionIds.AdUsersRead, [A]), (PermissionIds.AdUsersOffboard, [A])),
            null, AdUserStatus.Terminated, null, default);
        Assert.True(Assert.Single(list).Terminated);
    }

    [Fact]
    public async Task Card_has_groups_with_primary_first()
    {
        var card = await Service.GetAsync(All(PermissionIds.AdUsersRead), "ivan", default);
        Assert.True(card.Groups[0].IsPrimary);
        Assert.Contains(card.Groups, g => g.Name == "sg_a_docs_full");
    }

    [Fact]
    public async Task User_outside_root_or_hidden_is_403_and_unknown_is_404()
    {
        var actor = All(PermissionIds.AdUsersRead);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.GetAsync(actor, "admin2", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.GetAsync(actor, "nobody", default));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.GetAsync(Actor((PermissionIds.AdUsersRead, [B])), "ivan", default));
    }

    [Fact]
    public async Task Missing_root_is_a_configuration_error()
    {
        Structure = Structure with { RootOu = null };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.ListAsync(All(PermissionIds.AdUsersRead), null, AdUserStatus.All, null, default));
    }

    [Fact]
    public async Task History_is_audit_by_user_dn()
    {
        Audited.Add(new AuditEntryDto { Actor = "x", Action = "user.move", Target = Ad.U("ivan").Dn, Success = true });
        Audited.Add(new AuditEntryDto { Actor = "x", Action = "user.move", Target = "CN=other", Success = true });
        Assert.Single(await Service.HistoryAsync(All(PermissionIds.AdUsersRead), "ivan", default));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersReadTests"`
Expected: FAIL — компиляция: нет `AdUsersService`, `QueryAsync` с `target`.

- [ ] **Step 3: Write minimal implementation**

`ISecurityServices.cs` — сигнатура:

```csharp
    Task<IReadOnlyList<AuditEntryDto>> QueryAsync(int limit = 200, string? actor = null, string? target = null, CancellationToken ct = default);
```

`AuditService.QueryAsync` — параметр `string? target = null` и фильтр:

```csharp
        if (!string.IsNullOrWhiteSpace(target))
            q = q.Where(e => e.Target == target);
```

`AuditController`: `_audit.QueryAsync(limit, actor, ct: ct)`.

`IAdServices.cs` — добавить:

```csharp
/// <summary>Модуль «Пользователи AD»: все проверки зоны/области и аудит — внутри.</summary>
public interface IAdUsersService
{
    Task<IReadOnlyList<AdUserView>> ListAsync(IAccessContext actor, string? projectDn, AdUserStatus status, string? q, CancellationToken ct = default);
    Task<AdUserCard> GetAsync(IAccessContext actor, string sam, CancellationToken ct = default);
    Task<byte[]?> GetPhotoAsync(IAccessContext actor, string sam, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntryDto>> HistoryAsync(IAccessContext actor, string sam, CancellationToken ct = default);
}
```

(`using WinAdmin.Core.Models; using WinAdmin.Core.Security;`.)

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.cs
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

/// <summary>
/// Пользователи AD. Управляемый пользователь — в проекте под корневой OU (не скрытом) и в области оператора;
/// уволенный — в OU уволенных: виден и доступен тем, у кого есть ad-users.offboard.
/// </summary>
public sealed partial class AdUsersService(
    IAdUserDirectory directory, IAdWriter writer, IAdReader reader, IAdStructureStore structure,
    IModuleRegistry modules, IAuditService audit, ILogger<AdUsersService> logger) : IAdUsersService
{
    private sealed record Located(AdUser User, string? ProjectDn, bool Terminated);

    public async Task<IReadOnlyList<AdUserView>> ListAsync(IAccessContext actor, string? projectDn, AdUserStatus status, string? q, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var attributes = Attributes(settings);
        var projects = await reader.ListProjectsAsync(false, ct);
        IEnumerable<AdUser> users;

        if (status == AdUserStatus.Terminated)
        {
            if (!actor.Permissions.Has(PermissionIds.AdUsersOffboard))
                throw new AccessDeniedException("Уволенных видят только с правом увольнения.", [PermissionIds.AdUsersOffboard]);
            if (string.IsNullOrWhiteSpace(settings.TerminatedOuDn))
                throw new InvalidOperationException("Не задана OU уволенных (настройки модуля «Пользователи AD»).");
            users = await directory.ListUsersAsync(settings.TerminatedOuDn, attributes, ct);
        }
        else
        {
            var visible = AdGuard.InScopeProjects(actor, PermissionIds.AdUsersRead, projects);
            if (projectDn is not null)
            {
                var project = visible.FirstOrDefault(p => DnUtils.IsUnderOrSame(projectDn, p.Dn) && DnUtils.IsUnderOrSame(p.Dn, projectDn))
                              ?? throw new AccessDeniedException("Проект вне вашей области.", [PermissionIds.AdUsersRead]);
                visible = [project];
            }
            var list = new List<AdUser>();
            foreach (var p in visible) list.AddRange(await directory.ListUsersAsync(p.Dn, attributes, ct));
            users = list;
        }

        string? query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        return users
            .Where(u => status switch { AdUserStatus.Active => u.Enabled, AdUserStatus.Disabled => !u.Enabled, _ => true })
            .Where(u => query is null || Matches(u, query))
            .Select(u => View(u, st, settings))
            .OrderBy(v => v.DisplayName ?? v.Sam, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<AdUserCard> GetAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersRead, st, settings, ct);
        var groups = await directory.GetGroupsAsync(located.User, ct);
        return new AdUserCard(View(located.User, st, settings), groups);
    }

    public async Task<byte[]?> GetPhotoAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersRead, st, settings, ct);
        return located.User.HasPhoto ? await directory.GetPhotoAsync(located.User.Dn, ct) : null;
    }

    public async Task<IReadOnlyList<AuditEntryDto>> HistoryAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersRead, st, settings, ct);
        return await audit.QueryAsync(200, null, located.User.Dn, ct);
    }

    // ── общие помощники ──────────────────────────────────────────

    private async Task<(AdStructureSettings, AdUsersSettings)> ConfigAsync(CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null) throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        return (st, await modules.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, ct));
    }

    private static IReadOnlyCollection<string> Attributes(AdUsersSettings settings)
        => AdUsersSettings.DefaultAttributes.Concat(settings.EditableAttributes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Найти по логину и проверить: уволенный — нужен offboard; иначе зона + область для permission.</summary>
    private async Task<Located> LocateAsync(IAccessContext actor, string sam, string permission,
        AdStructureSettings st, AdUsersSettings settings, CancellationToken ct)
    {
        var user = await directory.FindUserAsync(sam, Attributes(settings), ct)
                   ?? throw new KeyNotFoundException($"Пользователь «{sam}» не найден в AD.");
        if (IsTerminated(user, settings))
        {
            if (!actor.Permissions.Has(PermissionIds.AdUsersOffboard))
                throw new AccessDeniedException("Пользователь уволен: нужен доступ к увольнению.", [PermissionIds.AdUsersOffboard]);
            return new Located(user, null, true);
        }
        string project = AdGuard.EnsureManaged(user.Dn, st);
        AdGuard.EnsureInScope(actor, permission, project);
        return new Located(user, project, false);
    }

    private static bool IsTerminated(AdUser user, AdUsersSettings settings)
        => !string.IsNullOrWhiteSpace(settings.TerminatedOuDn) && DnUtils.IsUnderOrSame(user.Dn, settings.TerminatedOuDn);

    private static bool Matches(AdUser u, string q)
        => u.Sam.Contains(q, StringComparison.OrdinalIgnoreCase)
           || (u.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
           || (u.Attr("mail")?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

    private static AdUserView View(AdUser u, AdStructureSettings st, AdUsersSettings settings)
    {
        bool terminated = IsTerminated(u, settings);
        string? project = terminated ? null : DnUtils.ProjectDn(u.Dn, st.RootOu!);
        return new AdUserView(u.Sam, u.Dn, u.DisplayName, u.Enabled, u.LastLogon, u.WhenCreated, u.HasPhoto,
            project, project is null ? null : DnUtils.RelativeOuPath(project, st.RootOu!)[0], terminated, u.Attributes);
    }

    private Task AuditAsync(IAccessContext actor, string action, string target, bool success, string details, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto { Actor = actor.Actor, Action = action, Target = target, Success = success, Details = details }, ct);
}
```

`partial` — методы записи добавляются в Task 4–5 отдельными файлами (`AdUsersService.Write.cs`, `AdUsersService.Offboarding.cs`), чтобы файлы оставались небольшими.

`DependencyInjection`:

```csharp
        services.AddScoped<IAdUsersService, AdUsersService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersReadTests|FullyQualifiedName~Audit"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(ad-users): list, card, photo and history with zone/scope checks; audit target filter"
```

---

### Task 4: Атрибуты, фото, перенос, пароль

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.Write.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUserValidation.cs`
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` (методы записи в `IAdUsersService`)
- Test: `src/tests/WinAdmin.Tests/AdUsersWriteTests.cs`

**Interfaces:**
- Consumes: база Task 3; `AdWriteException`, `PasswordGenerator`
- Produces (в `IAdUsersService`):
  - `UpdateAttributesAsync(IAccessContext actor, string sam, IReadOnlyDictionary<string, string?> changes, ct) → AdUserView`
  - `SetPhotoAsync(actor, sam, byte[]? photo, ct)`
  - `MoveAsync(actor, sam, string projectDn, ct) → AdUserView`
  - `ResetPasswordAsync(actor, sam, string? password, bool generate, bool mustChange, ct) → string?` (сгенерированный)
  - `AdUserValidation.Clean(IReadOnlyDictionary<string, string?> changes, IReadOnlyCollection<string> editable) → Dictionary<string, string?>` (канонические имена, `ArgumentException`); `IsImage(byte[]) → bool`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdUsersWriteTests.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Users;

namespace WinAdmin.Tests;

public sealed class AdUsersWriteTests : AdUsersServiceTestBase
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    [Fact]
    public async Task Updates_only_changed_editable_attributes_and_audits_before_after()
    {
        Ad.U("ivan").Attributes["title"] = "Инженер";
        var view = await Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit, PermissionIds.AdUsersRead), "ivan",
            new Dictionary<string, string?> { ["Title"] = " Главный инженер ", ["department"] = "ПТО", ["telephoneNumber"] = "" }, default);

        Assert.Equal("Главный инженер", Ad.U("ivan").Attributes["title"]);
        Assert.Equal("ПТО", view.Attributes["department"]);
        var e = Assert.Single(Audited);
        Assert.Equal("user.attributes.update", e.Action);
        Assert.Contains("title: «Инженер» → «Главный инженер»", e.Details);
        Assert.DoesNotContain("telephoneNumber", e.Details); // было пусто — стало пусто
    }

    [Fact]
    public async Task Unchanged_attributes_write_nothing()
    {
        Ad.U("ivan").Attributes["title"] = "Инженер";
        await Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit), "ivan", new Dictionary<string, string?> { ["title"] = " Инженер " }, default);
        Assert.DoesNotContain(Ad.Calls, c => c.StartsWith("Modify"));
        Assert.Empty(Audited);
    }

    [Theory]
    [InlineData("userAccountControl", "512")]
    [InlineData("mail", "не-адрес")]
    public async Task Rejects_foreign_or_invalid_attributes(string name, string value)
        => await Assert.ThrowsAsync<ArgumentException>(() => Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit), "ivan",
            new Dictionary<string, string?> { [name] = value }, default));

    [Fact]
    public async Task Too_long_value_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit), "ivan",
            new Dictionary<string, string?> { ["title"] = new string('x', 257) }, default));

    [Fact]
    public async Task Edit_outside_scope_is_403_without_write()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.UpdateAttributesAsync(Actor((PermissionIds.AdUsersEdit, [B])), "ivan",
            new Dictionary<string, string?> { ["title"] = "x" }, default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Photo_type_and_size_are_checked()
    {
        var actor = All(PermissionIds.AdUsersEdit);
        await Service.SetPhotoAsync(actor, "ivan", Jpeg, default);
        Assert.Equal(Jpeg, Ad.U("ivan").Photo);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SetPhotoAsync(actor, "ivan", [1, 2, 3, 4], default));
        Settings.PhotoMaxKb = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SetPhotoAsync(actor, "ivan", [.. Jpeg, .. new byte[2000]], default));
        await Service.SetPhotoAsync(actor, "ivan", null, default);
        Assert.Null(Ad.U("ivan").Photo);
        Assert.Equal(["user.photo.update", "user.photo.remove"], Audited.Select(a => a.Action));
    }

    [Fact]
    public async Task Move_needs_both_projects_in_scope()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.MoveAsync(Actor((PermissionIds.AdUsersMove, [A])), "ivan", B, default));
        var view = await Service.MoveAsync(Actor((PermissionIds.AdUsersMove, [A, B])), "ivan", B, default);
        Assert.Equal("CN=ivan,OU=Users," + B, Ad.U("ivan").Dn);
        Assert.Equal("B", view.ProjectName);
        Assert.Equal("user.move", Assert.Single(Audited).Action);
    }

    [Fact]
    public async Task Move_to_project_without_users_ou_is_409_and_nothing_written()
    {
        Reader.ExistingDns.Remove("OU=Users," + B);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.MoveAsync(All(PermissionIds.AdUsersMove), "ivan", B, default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Move_to_hidden_project_is_denied()
        => await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service.MoveAsync(All(PermissionIds.AdUsersMove), "ivan", "OU=IT," + Root, default));

    [Fact]
    public async Task Password_generated_is_returned_but_not_audited()
    {
        string? generated = await Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", null, true, true, default);
        Assert.Equal(20, generated!.Length);
        Assert.Equal(generated, Ad.U("ivan").Password);
        Assert.True(Ad.U("ivan").MustChange);
        Assert.DoesNotContain(generated, Assert.Single(Audited).Details);
    }

    [Fact]
    public async Task Manual_password_must_be_long_enough()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", "short", false, false, default));
        Assert.Null(await Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", "Long-Enough-1", false, false, default));
        Assert.Equal("Long-Enough-1", Ad.U("ivan").Password);
    }

    [Fact]
    public async Task Ad_refusal_is_audited_as_failure_and_rethrown()
    {
        Ad.FailOn["ResetPassword:" + Ad.U("ivan").Dn] = new AdWriteException(19, "Пароль не соответствует политике домена");
        await Assert.ThrowsAsync<AdWriteException>(() => Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", "Long-Enough-1", false, false, default));
        Assert.False(Assert.Single(Audited).Success);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersWriteTests"`
Expected: FAIL — компиляция: нет `UpdateAttributesAsync`, `SetPhotoAsync`, `MoveAsync`, `ResetPasswordAsync`.

- [ ] **Step 3: Write minimal implementation**

`IAdUsersService` — добавить:

```csharp
    Task<AdUserView> UpdateAttributesAsync(IAccessContext actor, string sam, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default);
    Task SetPhotoAsync(IAccessContext actor, string sam, byte[]? photo, CancellationToken ct = default);
    Task<AdUserView> MoveAsync(IAccessContext actor, string sam, string projectDn, CancellationToken ct = default);
    /// <summary>generate=true — сгенерировать и вернуть; иначе — задать password, вернуть null.</summary>
    Task<string?> ResetPasswordAsync(IAccessContext actor, string sam, string? password, bool generate, bool mustChange, CancellationToken ct = default);
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUserValidation.cs
using System.Text.RegularExpressions;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public static partial class AdUserValidation
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex MailRegex();

    /// <summary>Только разрешённые атрибуты (имена приводятся к настроенному написанию), обрезка пробелов, длина, формат почты.</summary>
    public static Dictionary<string, string?> Clean(IReadOnlyDictionary<string, string?> changes, IReadOnlyCollection<string> editable)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, rawValue) in changes)
        {
            string name = editable.FirstOrDefault(e => e.Equals(rawName?.Trim(), StringComparison.OrdinalIgnoreCase))
                          ?? throw new ArgumentException($"Атрибут «{rawName}» нельзя изменять.");
            string? value = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
            int max = name.Equals("description", StringComparison.OrdinalIgnoreCase) ? 1024 : 256;
            if (value is not null && value.Length > max)
                throw new ArgumentException($"«{name}»: не длиннее {max} символов.");
            if (value is not null && name.Equals("mail", StringComparison.OrdinalIgnoreCase) && !MailRegex().IsMatch(value))
                throw new ArgumentException("«mail»: неверный адрес почты.");
            result[name] = value;
        }
        return result;
    }

    public static bool IsImage(byte[] data)
        => data.Length >= 4 && ((data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                                || (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47));
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.Write.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public sealed partial class AdUsersService
{
    public async Task<AdUserView> UpdateAttributesAsync(IAccessContext actor, string sam, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var clean = AdUserValidation.Clean(changes, settings.EditableAttributes);
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersEdit, st, settings, ct);
        var user = located.User;

        var diff = clean.Where(kv => !string.Equals(Normalize(user.Attr(kv.Key)), kv.Value, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (diff.Count == 0) return View(user, st, settings);

        string details = string.Join("; ", diff.Select(kv => $"{kv.Key}: «{user.Attr(kv.Key) ?? ""}» → «{kv.Value ?? ""}»"));
        await WriteAuditedAsync(actor, "user.attributes.update", user.Dn, details,
            () => writer.ModifyAttributesAsync(user.Dn, diff, ct), ct);

        var updated = user.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in diff) updated[k] = v;
        return View(user with
        {
            Attributes = updated,
            DisplayName = diff.TryGetValue("displayName", out var dn) ? dn : user.DisplayName,
        }, st, settings);
    }

    public async Task SetPhotoAsync(IAccessContext actor, string sam, byte[]? photo, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        if (photo is not null)
        {
            if (!AdUserValidation.IsImage(photo)) throw new ArgumentException("Фото должно быть в формате JPEG или PNG.");
            if (photo.Length > settings.PhotoMaxKb * 1024) throw new ArgumentException($"Фото больше {settings.PhotoMaxKb} КБ.");
        }
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersEdit, st, settings, ct);
        await WriteAuditedAsync(actor, photo is null ? "user.photo.remove" : "user.photo.update", located.User.Dn,
            photo is null ? "фото удалено" : $"фото {photo.Length / 1024 + 1} КБ",
            () => writer.SetPhotoAsync(located.User.Dn, photo, ct), ct);
    }

    public async Task<AdUserView> MoveAsync(IAccessContext actor, string sam, string projectDn, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersMove, st, settings, ct);
        string target = AdGuard.EnsureManaged(projectDn, st);
        AdGuard.EnsureInScope(actor, PermissionIds.AdUsersMove, target);
        string targetOu = $"OU={st.UsersOuName},{target}";
        if (!await reader.ExistsAsync(targetOu, ct))
            throw new InvalidOperationException($"В проекте нет OU «{st.UsersOuName}».");
        if (DnUtils.IsUnderOrSame(AdWriteRequests.Parent(located.User.Dn), targetOu) && DnUtils.IsUnderOrSame(targetOu, AdWriteRequests.Parent(located.User.Dn)))
            return View(located.User, st, settings);

        string newDn = "";
        string from = DnUtils.RelativeOuPath(located.ProjectDn!, st.RootOu!)[0];
        string to = DnUtils.RelativeOuPath(target, st.RootOu!)[0];
        await WriteAuditedAsync(actor, "user.move", located.User.Dn, $"{from} → {to}",
            async () => newDn = await writer.MoveAsync(located.User.Dn, targetOu, ct), ct);
        return View(located.User with { Dn = newDn }, st, settings);
    }

    public async Task<string?> ResetPasswordAsync(IAccessContext actor, string sam, string? password, bool generate, bool mustChange, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        if (!generate && (password is null || password.Length < 8))
            throw new ArgumentException("Пароль не короче 8 символов (или выберите «сгенерировать»).");
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersPassword, st, settings, ct);
        string value = generate ? PasswordGenerator.Generate() : password!;
        await WriteAuditedAsync(actor, "user.password.reset", located.User.Dn,
            (generate ? "сгенерирован" : "задан оператором") + (mustChange ? ", смена при входе" : ""),
            () => writer.ResetPasswordAsync(located.User.Dn, value, mustChange, ct), ct);
        return generate ? value : null;
    }

    /// <summary>Только управляемые (не уволенные) пользователи: зона + область.</summary>
    private async Task<Located> ManagedAsync(IAccessContext actor, string sam, string permission,
        AdStructureSettings st, AdUsersSettings settings, CancellationToken ct)
    {
        var located = await LocateAsync(actor, sam, permission, st, settings, ct);
        if (located.Terminated)
            throw new InvalidOperationException("Пользователь уволен — сначала восстановите его.");
        return located;
    }

    private async Task WriteAuditedAsync(IAccessContext actor, string action, string target, string details, Func<Task> write, CancellationToken ct)
    {
        try
        {
            await write();
        }
        catch (Exception ex) when (ex is AdWriteException or DirectoryUnavailableException)
        {
            await AuditAsync(actor, action, target, false, details + ": " + ex.Message, ct);
            throw;
        }
        await AuditAsync(actor, action, target, true, details, ct);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

`AdWriteRequests.Parent` — из 3a (`WinAdmin.Infrastructure.ActiveDirectory`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersWriteTests|FullyQualifiedName~AdUsersReadTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdUsersWriteTests.cs
git commit -m "feat(ad-users): attributes, photo, move and password reset with validation and audit"
```

---

### Task 5: Увольнение и восстановление

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.Offboarding.cs`
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs`
- Test: `src/tests/WinAdmin.Tests/AdUsersOffboardingTests.cs`

**Interfaces:**
- Consumes: Task 3–4; `ScenarioRunner`, `StepResult`, `ScenarioStep` (3a); `IAdUserDirectory.FindGroupAsync/GetDomainUsersGroupAsync/GetGroupsAsync`
- Produces (в `IAdUsersService`): `DeactivateAsync(IAccessContext actor, string sam, ct) → IReadOnlyList<ScenarioStep>`; `ActivateAsync(actor, sam, string projectDn, ct) → ActivationResult`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdUsersOffboardingTests.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class AdUsersOffboardingTests : AdUsersServiceTestBase
{
    private string FiredDn => Ad.Groups.Single(g => g.Name == "Fired Users").Dn;

    [Fact]
    public async Task Deactivation_runs_access_steps_in_order()
    {
        var steps = await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        var ivan = Ad.U("ivan");

        Assert.All(steps, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Equal("CN=ivan," + Fired, ivan.Dn);
        Assert.False(ivan.Enabled);
        Assert.Equal(int.Parse(Ad.Groups.Single(g => g.Name == "Fired Users").Sid.Split('-')[^1]), ivan.PrimaryGroupId);
        Assert.DoesNotContain(Ad.Groups, g => g.Members.Contains(ivan.Dn) && g.Name != "Fired Users");
        Assert.Contains("Пользователи домена", string.Join("|", steps.Select(s => s.Name)));
        Assert.NotNull(ivan.Password);
        Assert.Equal(
            ["AddMember", "SetPrimary", "RemoveMember", "RemoveMember", "ResetPassword", "Disable", "Move"],
            Ad.Calls.Select(c => c.Split(':')[0]));
        var e = Assert.Single(Audited);
        Assert.Equal("user.account.deactivate", e.Action);
        Assert.DoesNotContain(ivan.Password!, e.Details);
    }

    [Fact]
    public async Task Deactivation_stops_on_failure_and_rerun_completes()
    {
        Ad.FailOn["RemoveMember:CN=sg_a_docs_full," + A] = new AdWriteException(50, "Нет прав");
        var first = await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Assert.Contains(first, s => s.Status == StepStatus.Failed);
        Assert.True(Ad.U("ivan").Enabled);                         // до отключения не дошли
        Assert.Contains("не выполнялся", first[^1].Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Assert.Single(Audited).Success);

        Ad.FailOn.Clear();
        var second = await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Assert.All(second, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Contains(second, s => s.Status == StepStatus.Skipped);  // уже в Fired Users
        Assert.False(Ad.U("ivan").Enabled);
    }

    [Fact]
    public async Task Repeat_deactivation_of_terminated_user_is_idempotent()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        var again = await Service.DeactivateAsync(Actor((PermissionIds.AdUsersOffboard, [B])), "ivan", default);
        Assert.All(again, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Equal("CN=ivan," + Fired, Ad.U("ivan").Dn);
    }

    [Fact]
    public async Task Deactivation_needs_settings_and_scope()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.DeactivateAsync(Actor((PermissionIds.AdUsersOffboard, [B])), "ivan", default));
        Settings.FiredGroup = "";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Activation_restores_into_project_and_returns_password_once()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Ad.Calls.Clear();
        Audited.Clear();

        var result = await Service.ActivateAsync(Actor((PermissionIds.AdUsersOffboard, [B])), "ivan", B, default);
        var ivan = Ad.U("ivan");

        Assert.All(result.Steps, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Equal("CN=ivan,OU=Users," + B, ivan.Dn);
        Assert.True(ivan.Enabled);
        Assert.Equal(513, ivan.PrimaryGroupId);
        Assert.DoesNotContain(ivan.Dn, Ad.Groups.Single(g => g.Name == "Fired Users").Members);
        Assert.Equal(ivan.Password, result.Password);
        Assert.True(ivan.MustChange);
        Assert.DoesNotContain(result.Password!, Assert.Single(Audited).Details);
        Assert.Equal(["AddMember", "SetPrimary", "RemoveMember", "Move", "ResetPassword", "Enable"], Ad.Calls.Select(c => c.Split(':')[0]));
    }

    [Fact]
    public async Task Activation_target_must_be_in_scope()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ActivateAsync(Actor((PermissionIds.AdUsersOffboard, [A])), "ivan", B, default));
    }

    [Fact]
    public async Task Failed_activation_returns_no_password()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Ad.FailOn["Move:CN=ivan," + Fired] = new AdWriteException(50, "Нет прав на перенос");
        var result = await Service.ActivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", B, default);
        Assert.Null(result.Password);
        Assert.False(Ad.U("ivan").Enabled);
    }
}
```

Порядок вызовов в первом тесте: у `ivan` прямые группы — `sg_a_docs_full`; после смены основной группы «Пользователи домена» становится обычным членством → два `RemoveMember`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersOffboardingTests"`
Expected: FAIL — компиляция: нет `DeactivateAsync`, `ActivateAsync`.

- [ ] **Step 3: Write minimal implementation**

`IAdUsersService` — добавить:

```csharp
    Task<IReadOnlyList<ScenarioStep>> DeactivateAsync(IAccessContext actor, string sam, CancellationToken ct = default);
    Task<ActivationResult> ActivateAsync(IAccessContext actor, string sam, string projectDn, CancellationToken ct = default);
```

(`using WinAdmin.Core.Operations;`.)

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Users/AdUsersService.Offboarding.cs
using Microsoft.Extensions.Logging;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public sealed partial class AdUsersService
{
    /// <summary>Увольнение (порт AdUserDeactivation): Fired Users + основная → снять группы → пароль → отключить → OU уволенных.</summary>
    public async Task<IReadOnlyList<ScenarioStep>> DeactivateAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        RequireOffboardSettings(settings);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersOffboard, st, settings, ct);
        var fired = await directory.FindGroupAsync(settings.FiredGroup, ct)
                    ?? throw new InvalidOperationException($"Группа уволенных «{settings.FiredGroup}» не найдена в AD.");
        var user = located.User;
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Увольнение {Sam}", sam));

        await run.RunAsync($"Добавление в группу «{fired.Name}»", async () =>
            await writer.AddMemberAsync(fired.Dn, user.Dn, ct) ? StepResult.Done() : StepResult.Skip("уже в группе"));
        await run.RunAsync($"Основная группа «{fired.Name}»", async () =>
        {
            if (Rid(fired.Sid) == user.PrimaryGroupId.ToString()) return StepResult.Skip("уже основная");
            await writer.SetPrimaryGroupAsync(user.Dn, fired.Sid!, ct);
            return StepResult.Done();
        });

        if (!run.Failed)
        {
            var current = await directory.FindUserAsync(sam, [], ct) ?? user;
            foreach (var group in (await directory.GetGroupsAsync(current, ct)).Where(g => !g.IsPrimary && !SameDn(g.Dn, fired.Dn)))
                await run.RunAsync($"Удаление из группы «{group.Name}»", async () =>
                    await writer.RemoveMemberAsync(group.Dn, user.Dn, ct) ? StepResult.Done() : StepResult.Skip("не состоял"));
        }

        await run.RunAsync("Сброс пароля", async () =>
        {
            await writer.ResetPasswordAsync(user.Dn, PasswordGenerator.Generate(), false, ct);
            return StepResult.Done("случайный, не показывается");
        });
        await run.RunAsync("Отключение учётной записи", async () =>
        {
            if (!user.Enabled) return StepResult.Skip("уже отключена");
            await writer.SetEnabledAsync(user.Dn, false, ct);
            return StepResult.Done();
        });
        await run.RunAsync("Перенос в OU уволенных", async () =>
        {
            if (located.Terminated) return StepResult.Skip("уже в OU уволенных");
            await writer.MoveAsync(user.Dn, settings.TerminatedOuDn, ct);
            return StepResult.Done();
        });

        await AuditAsync(actor, "user.account.deactivate", user.Dn, !run.Failed, Summary(run.Steps), ct);
        return run.Steps;
    }

    /// <summary>Восстановление (порт AdUserActivation): «Пользователи домена» + основная → снять Fired → перенос → пароль → включить.</summary>
    public async Task<ActivationResult> ActivateAsync(IAccessContext actor, string sam, string projectDn, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersOffboard, st, settings, ct);
        string project = AdGuard.EnsureManaged(projectDn, st);
        AdGuard.EnsureInScope(actor, PermissionIds.AdUsersOffboard, project);
        string targetOu = $"OU={st.UsersOuName},{project}";
        if (!await reader.ExistsAsync(targetOu, ct))
            throw new InvalidOperationException($"В проекте нет OU «{st.UsersOuName}».");

        var domainUsers = await directory.GetDomainUsersGroupAsync(ct);
        var fired = string.IsNullOrWhiteSpace(settings.FiredGroup) ? null : await directory.FindGroupAsync(settings.FiredGroup, ct);
        var user = located.User;
        string dn = user.Dn;
        string? password = null;
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Восстановление {Sam}", sam));

        await run.RunAsync($"Добавление в «{domainUsers.Name}»", async () =>
            user.PrimaryGroupId == 513 || await writer.AddMemberAsync(domainUsers.Dn, dn, ct) ? StepResult.Done() : StepResult.Skip("уже в группе"));
        await run.RunAsync($"Основная группа «{domainUsers.Name}»", async () =>
        {
            if (user.PrimaryGroupId == 513) return StepResult.Skip("уже основная");
            await writer.SetPrimaryGroupAsync(dn, domainUsers.Sid!, ct);
            return StepResult.Done();
        });
        if (fired is not null)
            await run.RunAsync($"Удаление из группы «{fired.Name}»", async () =>
                await writer.RemoveMemberAsync(fired.Dn, dn, ct) ? StepResult.Done() : StepResult.Skip("не состоял"));
        await run.RunAsync($"Перенос в «{DnUtils.RelativeOuPath(project, st.RootOu!)[0]}»", async () =>
        {
            if (SameDn(AdWriteRequests.Parent(dn), targetOu)) return StepResult.Skip("уже там");
            dn = await writer.MoveAsync(dn, targetOu, ct);
            return StepResult.Done();
        });
        await run.RunAsync("Новый пароль", async () =>
        {
            string value = PasswordGenerator.Generate();
            await writer.ResetPasswordAsync(dn, value, true, ct);
            password = value;
            return StepResult.Done("показан оператору, смена при входе");
        });
        await run.RunAsync("Включение учётной записи", async () =>
        {
            await writer.SetEnabledAsync(dn, true, ct);
            return StepResult.Done();
        });

        await AuditAsync(actor, "user.account.activate", user.Dn, !run.Failed, Summary(run.Steps), ct);
        return new ActivationResult(run.Steps, run.Failed ? null : password);
    }

    private static void RequireOffboardSettings(AdUsersSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.FiredGroup))
            throw new InvalidOperationException("Не задана группа уволенных (настройки модуля «Пользователи AD»).");
        if (string.IsNullOrWhiteSpace(settings.TerminatedOuDn))
            throw new InvalidOperationException("Не задана OU уволенных (настройки модуля «Пользователи AD»).");
    }

    private static string? Rid(string? sid) => sid?[(sid.LastIndexOf('-') + 1)..];

    private static bool SameDn(string a, string b) => DnUtils.IsUnderOrSame(a, b) && DnUtils.IsUnderOrSame(b, a);

    private static string Summary(IReadOnlyList<ScenarioStep> steps)
        => string.Join("; ", steps.Select(s => $"{s.Name}: {s.Status}" + (s.Status == StepStatus.Failed ? $" ({s.Message})" : "")));
}
```

`Failed_activation_returns_no_password`: перенос падает до шага пароля → `password` остаётся `null`, а `run.Failed` → `null`.
`Repeat_deactivation_of_terminated_user_is_idempotent`: уволенный — `LocateAsync` разрешает любому с `ad-users.offboard`; шаги: «уже в группе», «уже основная», нет групп для удаления, пароль, «уже отключена», «уже в OU уволенных».

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersOffboardingTests|FullyQualifiedName~AdUsersWriteTests|FullyQualifiedName~AdUsersReadTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdUsersOffboardingTests.cs
git commit -m "feat(ad-users): offboarding and restore scenarios (ported from Access)"
```

---

### Task 6: Проверки окружения модуля и разбор ACL

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AclInspector.cs`
- Create: `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/AdUsersCheck.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/AdUsersCheckTests.cs`

**Interfaces:**
- Consumes: `IAdReader.ListProjectsAsync/ExistsAsync/ReadEffectiveAsync/GetWriterStatusAsync`, `IAdStructureStore`, `IAdUserDirectory`, `IModuleRegistry.GetSettingsAsync<AdUsersSettings>`, `CheckResult` (3a)
- Produces:
  - `AclInspector.ResetPassword` (`Guid 00299570-246d-11d0-a768-00aa006e0529`); `AclInspector.HasExtendedRight(byte[] sd, IReadOnlyCollection<string> sids, Guid right) → bool`
  - `AdUsersCheck : IEnvironmentCheck` (`ModuleId = "ad-users"`), коды `users.usersOu`, `ad.rights.users`, `users.fired`, `users.domainUsers`, `users.terminatedOu`, `users.resetPassword`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdUsersCheckTests.cs
using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AclInspectorTests
{
    private const string Writer = "S-1-5-21-7-8-9-1500";

    private static byte[] Sd(params GenericAce[] aces)
    {
        var acl = new RawAcl(GenericAcl.AclRevisionDS, aces.Length);
        for (int i = 0; i < aces.Length; i++) acl.InsertAce(i, aces[i]);
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, acl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static ObjectAce Right(AceQualifier q, string sid, Guid right, AceFlags flags = AceFlags.None)
        => new(flags, q, 0x100 /* ControlAccess */, new SecurityIdentifier(sid), ObjectAceFlags.ObjectAceTypePresent, right, Guid.Empty, false, null);

    [Fact]
    public void Allow_for_writer_or_its_group_grants()
    {
        Assert.True(AclInspector.HasExtendedRight(Sd(Right(AceQualifier.AccessAllowed, Writer, AclInspector.ResetPassword)), [Writer], AclInspector.ResetPassword));
        Assert.False(AclInspector.HasExtendedRight(Sd(Right(AceQualifier.AccessAllowed, "S-1-5-21-7-8-9-999", AclInspector.ResetPassword)), [Writer], AclInspector.ResetPassword));
        Assert.False(AclInspector.HasExtendedRight(Sd(Right(AceQualifier.AccessAllowed, Writer, Guid.NewGuid())), [Writer], AclInspector.ResetPassword));
    }

    [Fact]
    public void Deny_wins_and_inherit_only_is_ignored()
    {
        Assert.False(AclInspector.HasExtendedRight(Sd(
            Right(AceQualifier.AccessDenied, Writer, AclInspector.ResetPassword),
            Right(AceQualifier.AccessAllowed, Writer, AclInspector.ResetPassword)), [Writer], AclInspector.ResetPassword));
        Assert.False(AclInspector.HasExtendedRight(Sd(
            Right(AceQualifier.AccessAllowed, Writer, AclInspector.ResetPassword, AceFlags.InheritOnly | AceFlags.ContainerInherit)), [Writer], AclInspector.ResetPassword));
    }

    [Fact]
    public void Full_control_grants_everything()
        => Assert.True(AclInspector.HasExtendedRight(Sd(new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x000F01FF,
            new SecurityIdentifier(Writer), false, null)), [Writer], AclInspector.ResetPassword));
}

public sealed class AdUsersCheckTests
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private readonly FakeAdDomain _ad = new();
    private readonly FakeAdReader _reader = new();
    private AdUsersSettings _settings = new() { FiredGroup = "Fired Users", TerminatedOuDn = "OU=Fired,DC=test,DC=local" };
    private AdStructureSettings _structure = AdStructureSettings.Default with { RootOu = Root, WriteLogin = "TEST\\svc", HasWritePassword = true };

    public AdUsersCheckTests()
    {
        _reader.Projects.Add(new AdProject(A, "A"));
        _reader.ExistingDns.UnionWith([Root, A, "OU=Users," + A, "OU=Fired,DC=test,DC=local"]);
        var fired = _ad.AddGroup("Fired Users", Root);
        var ivan = _ad.AddUser("ivan", "OU=Users," + A);
        var all = new HashSet<string>(AdUsersSettings.DefaultAttributes.Append("thumbnailPhoto"), StringComparer.OrdinalIgnoreCase);
        _reader.Effective[ivan.Dn] = new EffectiveRights(all, new HashSet<string>());
        _reader.Effective[fired.Dn] = new EffectiveRights(new HashSet<string> { "member" }, new HashSet<string>());
        _reader.Effective[_ad.DomainUsers.Dn] = new EffectiveRights(new HashSet<string> { "member" }, new HashSet<string>());
        _reader.Effective["OU=Fired,DC=test,DC=local"] = new EffectiveRights(new HashSet<string>(), new HashSet<string> { "user" });
        _ad.SecurityDescriptors[ivan.Dn] = AclSd(_ad.WriterSids[2]);
    }

    private static byte[] AclSd(string sid)
    {
        var acl = new RawAcl(GenericAcl.AclRevisionDS, 1);
        acl.InsertAce(0, new ObjectAce(AceFlags.None, AceQualifier.AccessAllowed, 0x100, new SecurityIdentifier(sid),
            ObjectAceFlags.ObjectAceTypePresent, AclInspector.ResetPassword, Guid.Empty, false, null));
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, acl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private async Task<Dictionary<string, CheckResult>> RunAsync()
    {
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _structure);
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        return (await new AdUsersCheck(_reader, _ad, st.Object, modules.Object).RunAsync(CheckDepth.Quick, default)).ToDictionary(r => r.Code);
    }

    [Fact]
    public async Task Healthy_is_all_ok()
        => Assert.All((await RunAsync()).Values, r => Assert.Equal(CheckStatus.Ok, r.Status));

    [Fact]
    public async Task Missing_attribute_rights_are_listed_with_fix()
    {
        _reader.Effective[_ad.U("ivan").Dn] = new EffectiveRights(new HashSet<string> { "displayName" }, new HashSet<string>());
        var r = (await RunAsync())["ad.rights.users"];
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains("thumbnailPhoto", r.Message);
        Assert.Contains("TEST\\svc", r.Fix);
    }

    [Fact]
    public async Task Missing_fired_group_and_ou_settings()
    {
        _settings = new AdUsersSettings();
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Warning, r["users.fired"].Status);
        Assert.Equal(CheckStatus.Warning, r["users.terminatedOu"].Status);
    }

    [Fact]
    public async Task No_reset_password_right_fails()
    {
        _ad.SecurityDescriptors[_ad.U("ivan").Dn] = AclSd("S-1-5-21-1-1-1-1");
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["users.resetPassword"].Status);
    }

    [Fact]
    public async Task Project_without_users_ou_is_a_warning()
    {
        _reader.Projects.Add(new AdProject("OU=B," + Root, "B"));
        var r = (await RunAsync())["users.usersOu"];
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("B", r.Message);
    }

    [Fact]
    public async Task Writer_not_signed_in_skips_rights()
    {
        _reader.Writer = _reader.Writer with { Bound = false, Error = "нет" };
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Skipped, r["ad.rights.users"].Status);
        Assert.Equal(CheckStatus.Skipped, r["users.resetPassword"].Status);
    }

    [Fact]
    public async Task No_root_skips_module_checks()
    {
        _structure = _structure with { RootOu = null };
        var r = await RunAsync();
        Assert.All(r.Values, x => Assert.Equal(CheckStatus.Skipped, x.Status));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AclInspectorTests|FullyQualifiedName~AdUsersCheckTests"`
Expected: FAIL — компиляция: нет `AclInspector`, `AdUsersCheck`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/AclInspector.cs
using System.Security.AccessControl;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Расширенные права AD по DACL объекта: есть ли Allow без Deny для одного из SID.</summary>
public static class AclInspector
{
    public static readonly Guid ResetPassword = new("00299570-246d-11d0-a768-00aa006e0529");
    private const int ControlAccess = 0x100;
    private const int GenericAll = 0x10000000;
    private const int FullControl = 0x000F01FF;

    public static bool HasExtendedRight(byte[] securityDescriptor, IReadOnlyCollection<string> sids, Guid right)
    {
        var sd = new RawSecurityDescriptor(securityDescriptor, 0);
        if (sd.DiscretionaryAcl is null) return true; // нет DACL — доступ не ограничен
        var set = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
        bool allowed = false;
        foreach (GenericAce ace in sd.DiscretionaryAcl)
        {
            if ((ace.AceFlags & AceFlags.InheritOnly) != 0) continue;
            if (ace is not KnownAce known || !set.Contains(known.SecurityIdentifier.Value)) continue;
            bool full = (known.AccessMask & GenericAll) != 0 || (known.AccessMask & FullControl) == FullControl;
            bool matches = full || ((known.AccessMask & ControlAccess) != 0 && known switch
            {
                ObjectAce oa => (oa.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) == 0 || oa.ObjectAceType == right,
                _ => true,
            });
            if (!matches) continue;
            bool deny = ace.AceType is AceType.AccessDenied or AceType.AccessDeniedObject;
            if (deny) return false;
            allowed = true;
        }
        return allowed;
    }
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/EnvironmentChecks/AdUsersCheck.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Готовность модуля «Пользователи AD»: OU пользователей, права учётки записи, группа и OU уволенных, сброс пароля.</summary>
public sealed class AdUsersCheck(IAdReader reader, IAdUserDirectory users, IAdStructureStore structure, IModuleRegistry modules) : IEnvironmentCheck
{
    private const int MaxProjects = 50;
    private static readonly string[] Codes =
        ["users.usersOu", "ad.rights.users", "users.fired", "users.domainUsers", "users.terminatedOu", "users.resetPassword"];

    public string ModuleId => AdUsersModule.ModuleId;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null)
            return Codes.Select(c => CheckResult.Skip(c, Title(c), "Корневая OU не задана (см. «Платформа»)")).ToList();

        var settings = await modules.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, ct);
        var projects = (await reader.ListProjectsAsync(false, ct)).Take(MaxProjects).ToList();
        var writer = await reader.GetWriterStatusAsync(ct);
        string account = writer.Account;
        var results = new List<CheckResult>();

        // OU пользователей в проектах + образцы пользователей.
        var missingOu = new List<string>();
        var samples = new List<(AdProject Project, string Dn)>();
        foreach (var p in projects)
        {
            string ou = $"OU={st.UsersOuName},{p.Dn}";
            if (!await reader.ExistsAsync(ou, ct)) { missingOu.Add(p.Name); continue; }
            if (await users.FindSampleUserAsync(ou, ct) is { } sample) samples.Add((p, sample));
        }
        results.Add(missingOu.Count == 0
            ? CheckResult.Ok("users.usersOu", Title("users.usersOu"), $"OU «{st.UsersOuName}» есть во всех проектах ({projects.Count})")
            : CheckResult.Warn("users.usersOu", Title("users.usersOu"), $"Нет OU «{st.UsersOuName}» в проектах: {string.Join(", ", missingOu)}",
                "Создайте OU или измените «OU пользователей» в Настройки → Active Directory"));

        if (!writer.Bound)
        {
            string reason = "Учётка записи не вошла (см. «Платформа»)";
            results.Add(CheckResult.Skip("ad.rights.users", Title("ad.rights.users"), reason));
            results.Add(await FiredAsync(settings, null, ct));
            results.Add(CheckResult.Skip("users.domainUsers", Title("users.domainUsers"), reason));
            results.Add(await TerminatedOuAsync(settings, null, ct));
            results.Add(CheckResult.Skip("users.resetPassword", Title("users.resetPassword"), reason));
            return results;
        }

        // Права на атрибуты пользователей.
        var needed = settings.EditableAttributes.Append("thumbnailPhoto").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lacking = new List<string>();
        foreach (var (project, dn) in samples)
        {
            var rights = await reader.ReadEffectiveAsync(dn, ct);
            var missing = needed.Where(a => !rights.Attributes.Contains(a)).ToList();
            if (missing.Count > 0) lacking.Add($"{project.Name}: {string.Join(", ", missing)}");
        }
        results.Add(samples.Count == 0
            ? CheckResult.Warn("ad.rights.users", Title("ad.rights.users"), "В проектах нет пользователей для проверки")
            : lacking.Count == 0
                ? CheckResult.Ok("ad.rights.users", Title("ad.rights.users"), $"Запись атрибутов разрешена ({samples.Count} проектов)")
                : CheckResult.Fail("ad.rights.users", Title("ad.rights.users"), "Нет права записи: " + string.Join("; ", lacking),
                    $"Делегируйте {account} «Write Property» на эти атрибуты для объектов user в OU={st.UsersOuName} проектов"));

        results.Add(await FiredAsync(settings, account, ct));

        var domainUsers = await users.GetDomainUsersGroupAsync(ct);
        results.Add((await reader.ReadEffectiveAsync(domainUsers.Dn, ct)).Attributes.Contains("member")
            ? CheckResult.Ok("users.domainUsers", Title("users.domainUsers"), $"«{domainUsers.Name}»: запись участников разрешена")
            : CheckResult.Fail("users.domainUsers", Title("users.domainUsers"), $"«{domainUsers.Name}»: нет права изменять участников",
                $"Делегируйте {account} «Write members» на группу «{domainUsers.Name}»"));

        results.Add(await TerminatedOuAsync(settings, account, ct));

        if (samples.Count == 0)
            results.Add(CheckResult.Skip("users.resetPassword", Title("users.resetPassword"), "Нет пользователей для проверки"));
        else
        {
            var sids = await users.GetWriterSidsAsync(ct);
            var sd = await users.ReadSecurityDescriptorAsync(samples[0].Dn, ct);
            bool can = sd is not null && AclInspector.HasExtendedRight(sd, sids, AclInspector.ResetPassword);
            results.Add(can
                ? CheckResult.Ok("users.resetPassword", Title("users.resetPassword"), "Сброс пароля разрешён")
                : CheckResult.Fail("users.resetPassword", Title("users.resetPassword"), $"Нет права «Reset Password» (проверено на {samples[0].Project.Name})",
                    $"Делегируйте {account} «Reset Password» на объекты user в OU={st.UsersOuName} проектов"));
        }
        return results;
    }

    private async Task<CheckResult> FiredAsync(AdUsersSettings settings, string? account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.FiredGroup))
            return CheckResult.Warn("users.fired", Title("users.fired"), "Группа уволенных не задана — увольнение недоступно",
                "Модули → «Пользователи AD» → настройки: «Группа уволенных»");
        var group = await users.FindGroupAsync(settings.FiredGroup, ct);
        if (group is null)
            return CheckResult.Fail("users.fired", Title("users.fired"), $"Группа «{settings.FiredGroup}» не найдена", "Проверьте имя или DN группы");
        if (account is null) return CheckResult.Ok("users.fired", Title("users.fired"), $"Группа найдена: {group.Dn}");
        return (await reader.ReadEffectiveAsync(group.Dn, ct)).Attributes.Contains("member")
            ? CheckResult.Ok("users.fired", Title("users.fired"), $"«{group.Name}»: запись участников разрешена")
            : CheckResult.Fail("users.fired", Title("users.fired"), $"«{group.Name}»: нет права изменять участников",
                $"Делегируйте {account} «Write members» на группу «{group.Name}»");
    }

    private async Task<CheckResult> TerminatedOuAsync(AdUsersSettings settings, string? account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.TerminatedOuDn))
            return CheckResult.Warn("users.terminatedOu", Title("users.terminatedOu"), "OU уволенных не задана — увольнение недоступно",
                "Модули → «Пользователи AD» → настройки: «OU уволенных»");
        if (!await reader.ExistsAsync(settings.TerminatedOuDn, ct))
            return CheckResult.Fail("users.terminatedOu", Title("users.terminatedOu"), $"OU не найдена: {settings.TerminatedOuDn}", "Проверьте DN");
        if (account is null) return CheckResult.Ok("users.terminatedOu", Title("users.terminatedOu"), "OU найдена");
        return (await reader.ReadEffectiveAsync(settings.TerminatedOuDn, ct)).ChildClasses.Contains("user")
            ? CheckResult.Ok("users.terminatedOu", Title("users.terminatedOu"), "Перенос в OU уволенных разрешён")
            : CheckResult.Fail("users.terminatedOu", Title("users.terminatedOu"), "Нет права переносить пользователей в OU уволенных",
                $"Делегируйте {account} «Create/Delete User objects» на OU уволенных и OU пользователей проектов");
    }

    private static string Title(string code) => code switch
    {
        "users.usersOu" => "OU пользователей в проектах",
        "ad.rights.users" => "Права на атрибуты пользователей",
        "users.fired" => "Группа уволенных",
        "users.domainUsers" => "Пользователи домена",
        "users.terminatedOu" => "OU уволенных",
        "users.resetPassword" => "Сброс пароля",
        _ => code,
    };
}
```

`DependencyInjection`:

```csharp
        services.AddSingleton<IEnvironmentCheck, AdUsersCheck>();
```

(`AdUsersCheck` — singleton, зависимости `IAdReader`, `IAdUserDirectory`, `IAdStructureStore`, `IModuleRegistry` — singleton.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AclInspectorTests|FullyQualifiedName~AdUsersCheckTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdUsersCheckTests.cs
git commit -m "feat(ad-users): environment checks incl. attribute rights and reset-password ACL"
```

---

### Task 7: API — пользователи AD и проекты

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/AdUsersController.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/AdProjectsController.cs`
- Modify: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs` (фабрика: `FakeAdDomain` как `IAdUserDirectory`+`IAdWriter`, машина «в домене»)
- Test: `src/tests/WinAdmin.Tests/AdUsersApiTests.cs`

**Interfaces:**
- Consumes: `IAdUsersService` (Task 3–5), `IAdReader`, `IModuleRegistry`, `AccessContextFactory`, `WinAdminModuleAttribute`, `PlatformErrorsAttribute`
- Produces:
  - `GET /api/v1/ad/projects` → `[{ dn, name }]` (из области `ad-users.read`; 404, если модуль `ad-users` выключен)
  - `GET /api/v1/ad/users?project=&status=active|disabled|terminated|all&q=` → `AdUserView[]`
  - `GET /api/v1/ad/users/{sam}` → `AdUserCard`; `GET …/{sam}/photo` → `image/jpeg|png` или 404; `GET …/{sam}/history` → `AuditEntryDto[]`
  - `PUT …/{sam}/attributes` `{ attributes: { name: value } }` → `AdUserView`
  - `PUT …/{sam}/photo` (тело — байты, `Content-Type: image/jpeg|image/png`) → 204; `DELETE …/{sam}/photo` → 204
  - `POST …/{sam}/move` `{ projectDn }` → `AdUserView`
  - `POST …/{sam}/password` `{ password?, generate, mustChange }` → `{ password: string|null }`
  - `POST …/{sam}/deactivate` → `ScenarioStep[]`; `POST …/{sam}/activate` `{ projectDn }` → `ActivationResult`
  - Ошибки: `PlatformErrors` (403/404/409/400) + `DirectoryUnavailableException` → 503, `AdWriteException` → 422 `{ message }`

- [ ] **Step 1: Write the failing test**

В `NetworkApiFactory`:

```csharp
    public FakeAdDomain AdDomain { get; } = new();
```

в `ConfigureTestServices`:

```csharp
            services.RemoveAll<IAdUserDirectory>();
            services.AddSingleton<IAdUserDirectory>(AdDomain);
            services.RemoveAll<IAdWriter>();
            services.AddSingleton<IAdWriter>(AdDomain);
            services.RemoveAll<IMachineInfo>();
            services.AddSingleton(Mock.Of<IMachineInfo>(m => m.IsDomainJoined == true && m.IsWindowsServer == true && m.DomainName == "test.local"));
```

```csharp
// src/tests/WinAdmin.Tests/AdUsersApiTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class AdUsersApiTests : IAsyncLifetime
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly NetworkApiFactory _factory;
    private readonly string _sam = "u" + Guid.NewGuid().ToString("N")[..6];

    public AdUsersApiTests(NetworkApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdStructureStore>().SaveAsync(
            AdStructureSettings.Default with { RootOu = Root, WriteMode = AdWriteMode.ProcessAccount }, null);
        await scope.ServiceProvider.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, true, "test");
        if (!_factory.AdReader.Projects.Any(p => p.Dn == A)) _factory.AdReader.Projects.Add(new AdProject(A, "A"));
        _factory.AdReader.ExistingDns.UnionWith([Root, A, "OU=Users," + A]);
        _factory.AdDomain.AddUser(_sam, "OU=Users," + A);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_card_and_projects()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead);
        var list = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/ad/users?status=all", Web);
        Assert.Contains(list!, u => u.GetProperty("sam").GetString() == _sam);
        var card = await client.GetFromJsonAsync<JsonElement>($"/api/v1/ad/users/{_sam}", Web);
        Assert.Equal("A", card.GetProperty("user").GetProperty("projectName").GetString());
        var projects = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/ad/projects", Web);
        Assert.Contains(projects!, p => p.GetProperty("name").GetString() == "A");
    }

    [Fact]
    public async Task Write_requires_permission_and_password_is_returned_once()
    {
        var reader = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reader.PostAsJsonAsync($"/api/v1/ad/users/{_sam}/password", new { generate = true, mustChange = true })).StatusCode);

        var admin = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersPassword);
        var r = await admin.PostAsJsonAsync($"/api/v1/ad/users/{_sam}/password", new { generate = true, mustChange = true });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(20, (await r.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("password").GetString()!.Length);
    }

    [Fact]
    public async Task Photo_roundtrip()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersEdit, PermissionIds.AdUsersRead);
        var content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/v1/ad/users/{_sam}/photo", content)).StatusCode);
        var photo = await client.GetAsync($"/api/v1/ad/users/{_sam}/photo");
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Errors_map_to_status_codes()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead, PermissionIds.AdUsersEdit);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/ad/users/nobody-xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/ad/users/{_sam}/attributes",
            new { attributes = new Dictionary<string, string> { ["userAccountControl"] = "512" } })).StatusCode);

        _factory.AdDomain.FailOn["Modify:" + _factory.AdDomain.U(_sam).Dn] = new AdWriteException(50, "Нет прав");
        try
        {
            var r = await client.PutAsJsonAsync($"/api/v1/ad/users/{_sam}/attributes", new { attributes = new Dictionary<string, string> { ["title"] = "x" } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Contains("Нет прав", await r.Content.ReadAsStringAsync());
        }
        finally { _factory.AdDomain.FailOn.Clear(); }
    }

    [Fact]
    public async Task Disabled_module_is_404()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, false, "test");
        try
        {
            var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/ad/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/ad/projects")).StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, true, "test");
        }
    }
}
```

`ClientWithPermissionsAsync` создаёт роль с правами без области — делегирование из `SystemActor` допускает права модуля `ad-users` (модуль в каталоге).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersApiTests"`
Expected: FAIL — 404 на `/api/v1/ad/*`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Api/Controllers/AdUsersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed record AttributesRequest(Dictionary<string, string?> Attributes);
public sealed record ProjectRequest(string ProjectDn);
public sealed record PasswordRequest(string? Password, bool Generate, bool MustChange = true);

/// <summary>AD: недоступен — 503, AD отклонил — 422 с текстом.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AdErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            DirectoryUnavailableException e => new ObjectResult(new { message = e.Message }) { StatusCode = 503 },
            AdWriteException e => new ObjectResult(new { message = e.Message }) { StatusCode = 422 },
            _ => null,
        };
        if (context.Result is not null) context.ExceptionHandled = true;
    }
}

[WinAdminModule(AdUsersModule.ModuleId)]
[PlatformErrors]
[AdErrors]
[Route("/api/v1/ad/users")]
public sealed class AdUsersController(IAdUsersService users, AccessContextFactory contexts) : WinAdminControllerBase
{
    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

    [HttpGet]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<IReadOnlyList<AdUserView>> List([FromQuery] string? project, [FromQuery] string? status, [FromQuery] string? q, CancellationToken ct)
        => await users.ListAsync(await ActorAsync(ct), string.IsNullOrWhiteSpace(project) ? null : project,
            Enum.TryParse<AdUserStatus>(status, true, out var s) ? s : AdUserStatus.All, q, ct);

    [HttpGet("{sam}")]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<AdUserCard> Get(string sam, CancellationToken ct) => await users.GetAsync(await ActorAsync(ct), sam, ct);

    [HttpGet("{sam}/photo")]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<IActionResult> Photo(string sam, CancellationToken ct)
    {
        var photo = await users.GetPhotoAsync(await ActorAsync(ct), sam, ct);
        if (photo is null) return NotFound();
        return File(photo, photo.Length > 1 && photo[0] == 0x89 ? "image/png" : "image/jpeg");
    }

    [HttpGet("{sam}/history")]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<IActionResult> History(string sam, CancellationToken ct) => Ok(await users.HistoryAsync(await ActorAsync(ct), sam, ct));

    [HttpPut("{sam}/attributes")]
    [RequirePermission(PermissionIds.AdUsersEdit)]
    public async Task<AdUserView> Attributes(string sam, [FromBody] AttributesRequest request, CancellationToken ct)
        => await users.UpdateAttributesAsync(await ActorAsync(ct), sam, request.Attributes ?? [], ct);

    [HttpPut("{sam}/photo")]
    [RequirePermission(PermissionIds.AdUsersEdit)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SetPhoto(string sam, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        await users.SetPhotoAsync(await ActorAsync(ct), sam, ms.ToArray(), ct);
        return NoContent();
    }

    [HttpDelete("{sam}/photo")]
    [RequirePermission(PermissionIds.AdUsersEdit)]
    public async Task<IActionResult> RemovePhoto(string sam, CancellationToken ct)
    {
        await users.SetPhotoAsync(await ActorAsync(ct), sam, null, ct);
        return NoContent();
    }

    [HttpPost("{sam}/move")]
    [RequirePermission(PermissionIds.AdUsersMove)]
    public async Task<AdUserView> Move(string sam, [FromBody] ProjectRequest request, CancellationToken ct)
        => await users.MoveAsync(await ActorAsync(ct), sam, request.ProjectDn, ct);

    [HttpPost("{sam}/password")]
    [RequirePermission(PermissionIds.AdUsersPassword)]
    public async Task<IActionResult> Password(string sam, [FromBody] PasswordRequest request, CancellationToken ct)
        => Ok(new { password = await users.ResetPasswordAsync(await ActorAsync(ct), sam, request.Password, request.Generate, request.MustChange, ct) });

    [HttpPost("{sam}/deactivate")]
    [RequirePermission(PermissionIds.AdUsersOffboard)]
    public async Task<IActionResult> Deactivate(string sam, CancellationToken ct) => Ok(await users.DeactivateAsync(await ActorAsync(ct), sam, ct));

    [HttpPost("{sam}/activate")]
    [RequirePermission(PermissionIds.AdUsersOffboard)]
    public async Task<IActionResult> Activate(string sam, [FromBody] ProjectRequest request, CancellationToken ct)
        => Ok(await users.ActivateAsync(await ActorAsync(ct), sam, request.ProjectDn, ct));
}
```

```csharp
// src/backend/WinAdmin.Api/Controllers/AdProjectsController.cs
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Проекты (OU первого уровня) в пределах области; общий для модулей AD (3c добавит ad-folders.read).</summary>
[PlatformErrors]
[AdErrors]
[Route("/api/v1/ad/projects")]
public sealed class AdProjectsController(IAdReader reader, IModuleRegistry modules, AccessContextFactory contexts) : WinAdminControllerBase
{
    private static readonly (string Module, string Permission)[] Sources =
    [
        (AdUsersModule.ModuleId, PermissionIds.AdUsersRead),
    ];

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var enabled = Sources.Where(s => modules.GetState(s.Module).Enabled).ToList();
        if (enabled.Count == 0) return NotFound();
        var actor = await contexts.CreateAsync(User, ct);
        if (actor is null) return Unauthorized();
        var allowed = enabled.Where(s => actor.Permissions.Has(s.Permission)).ToList();
        if (allowed.Count == 0) return StatusCode(StatusCodes.Status403Forbidden);

        var projects = await reader.ListProjectsAsync(false, ct);
        var visible = allowed.SelectMany(s => AdGuard.InScopeProjects(actor, s.Permission, projects))
            .DistinctBy(p => p.Dn, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new { dn = p.Dn, name = p.Name });
        return Ok(visible);
    }
}
```

`WinAdminControllerBase` имеет `[Authorize]` — анонимный запрос получит 401 до действия.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsersApiTests"`, затем полный `dotnet test src/tests/WinAdmin.Tests`
Expected: PASS, без регрессий (машина «в домене» в фабрике не ломает остальные тесты — проверить `ModuleRegistry`-тесты API).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(api): AD users and projects endpoints with module/scope checks and AD error mapping"
```

---

### Task 8: Интерфейс — страница «Пользователи AD» и проекты в редакторе ролей

**Files:**
- Modify: `src/frontend/src/api/types.ts`, `src/frontend/src/api/client.ts`
- Create: `src/frontend/src/pages/AdUsers.tsx`
- Modify: `src/frontend/src/App.tsx`, `src/frontend/src/components/AppLayout.tsx`
- Modify: `src/frontend/src/pages/Roles.tsx`

**Interfaces:**
- Consumes: API Task 7
- Produces: маршрут `/ad/users` (модуль `ad-users`, право `ad-users.read`); `api.ad.*`

Frontend-тестов нет — `npm run build`, `npm run lint`, браузер.

- [ ] **Step 1: Типы и API**

`types.ts`:

```ts
export interface AdProject { dn: string; name: string }

export interface AdUserView {
  sam: string; dn: string; displayName: string | null; enabled: boolean
  lastLogon: string | null; whenCreated: string | null; hasPhoto: boolean
  projectDn: string | null; projectName: string | null; terminated: boolean
  attributes: Record<string, string | null>
}

export interface AdGroupRef { dn: string; name: string; sid: string | null; isPrimary: boolean }
export interface AdUserCard { user: AdUserView; groups: AdGroupRef[] }
export interface ScenarioStep { name: string; status: 'Ok' | 'Skipped' | 'Failed'; message: string | null }
export interface ActivationResult { steps: ScenarioStep[]; password: string | null }
export type AdUserStatus = 'active' | 'disabled' | 'terminated' | 'all'
```

`client.ts` — в `api`:

```ts
  ad: {
    projects: () => http.get<AdProject[]>('/ad/projects').then((r) => r.data),
    users: (params: { project?: string; status?: AdUserStatus; q?: string }) =>
      http.get<AdUserView[]>('/ad/users', { params }).then((r) => r.data),
    user: (sam: string) => http.get<AdUserCard>(`/ad/users/${encodeURIComponent(sam)}`).then((r) => r.data),
    photo: (sam: string) => http.get(`/ad/users/${encodeURIComponent(sam)}/photo`, { responseType: 'blob' }).then((r) => r.data as Blob),
    history: (sam: string) => http.get<AuditEntryDto[]>(`/ad/users/${encodeURIComponent(sam)}/history`).then((r) => r.data),
    updateAttributes: (sam: string, attributes: Record<string, string | null>) =>
      http.put<AdUserView>(`/ad/users/${encodeURIComponent(sam)}/attributes`, { attributes }).then((r) => r.data),
    setPhoto: (sam: string, blob: Blob) =>
      http.put(`/ad/users/${encodeURIComponent(sam)}/photo`, blob, { headers: { 'Content-Type': blob.type } }),
    removePhoto: (sam: string) => http.delete(`/ad/users/${encodeURIComponent(sam)}/photo`),
    move: (sam: string, projectDn: string) =>
      http.post<AdUserView>(`/ad/users/${encodeURIComponent(sam)}/move`, { projectDn }).then((r) => r.data),
    password: (sam: string, body: { password?: string; generate: boolean; mustChange: boolean }) =>
      http.post<{ password: string | null }>(`/ad/users/${encodeURIComponent(sam)}/password`, body).then((r) => r.data),
    deactivate: (sam: string) => http.post<ScenarioStep[]>(`/ad/users/${encodeURIComponent(sam)}/deactivate`).then((r) => r.data),
    activate: (sam: string, projectDn: string) =>
      http.post<ActivationResult>(`/ad/users/${encodeURIComponent(sam)}/activate`, { projectDn }).then((r) => r.data),
  },
```

(типы — в `import type` в начале файла.)

- [ ] **Step 2: Страница «Пользователи AD»**

```tsx
// src/frontend/src/pages/AdUsers.tsx
import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Alert, App, Avatar, Button, Card, Checkbox, Col, Descriptions, Drawer, Empty, Form, Input, List, Modal,
  Popconfirm, Row, Segmented, Select, Space, Table, Tabs, Tag, Typography, Upload,
} from 'antd'
import { CopyOutlined, ReloadOutlined, UserOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import { api } from '../api/client'
import type { AdProject, AdUserCard, AdUserStatus, AdUserView, AuditEntryDto, ScenarioStep } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import PageHeader from '../components/PageHeader'

const ATTRS: [string, string][] = [
  ['displayName', 'Отображаемое имя'], ['givenName', 'Имя'], ['sn', 'Фамилия'], ['mail', 'Почта'],
  ['department', 'Отдел'], ['title', 'Должность'], ['telephoneNumber', 'Телефон'],
  ['physicalDeliveryOfficeName', 'Офис'], ['company', 'Компания'], ['description', 'Описание'],
]

const errorText = (e: any, fallback: string) => e?.response?.data?.message ?? fallback

function Steps({ steps }: { steps: ScenarioStep[] }) {
  const color = { Ok: 'green', Skipped: 'default', Failed: 'red' } as const
  return (
    <List size="small" dataSource={steps} renderItem={(s) => (
      <List.Item><Space><Tag color={color[s.status]}>{s.status === 'Ok' ? 'ок' : s.status === 'Skipped' ? 'пропущен' : 'ошибка'}</Tag>{s.name}<Typography.Text type="secondary">{s.message}</Typography.Text></Space></List.Item>
    )} />
  )
}

/** Вписать изображение в 256×256 и закодировать JPEG (≈ 20–40 КБ). */
async function toJpeg(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, 256 / Math.max(bitmap.width, bitmap.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  return new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('jpeg'))), 'image/jpeg', 0.85))
}

function UserCard({ sam, projects, onClose, onChanged }: {
  sam?: string; projects: AdProject[]; onClose: () => void; onChanged: () => void
}) {
  const { message, modal } = App.useApp()
  const { can } = useAuth()
  const [card, setCard] = useState<AdUserCard>()
  const [photoUrl, setPhotoUrl] = useState<string>()
  const [history, setHistory] = useState<AuditEntryDto[]>([])
  const [form] = Form.useForm()
  const [busy, setBusy] = useState<string>()
  const [steps, setSteps] = useState<ScenarioStep[]>()
  const [target, setTarget] = useState<string>()

  const load = useCallback(async () => {
    if (!sam) return
    try {
      const c = await api.ad.user(sam)
      setCard(c)
      form.setFieldsValue(c.user.attributes)
      setHistory(await api.ad.history(sam).catch(() => []))
      if (c.user.hasPhoto) setPhotoUrl(URL.createObjectURL(await api.ad.photo(sam)))
      else setPhotoUrl(undefined)
    } catch (e) {
      message.error(errorText(e, 'Не удалось загрузить пользователя'))
    }
  }, [sam, form, message])

  useEffect(() => { setSteps(undefined); setCard(undefined); load() }, [load])

  const run = async (key: string, action: () => Promise<void>) => {
    setBusy(key)
    try { await action(); onChanged(); await load() } catch (e) { message.error(errorText(e, 'Операция не выполнена')) } finally { setBusy(undefined) }
  }

  const showPassword = (password: string) => modal.info({
    title: 'Новый пароль — показывается один раз',
    content: <Space><Typography.Text code copyable={{ icon: <CopyOutlined /> }}>{password}</Typography.Text></Space>,
  })

  const u = card?.user
  return (
    <Drawer title={u?.displayName ?? sam} open={Boolean(sam)} onClose={onClose} width={720}>
      {!u ? <Empty /> : (
        <>
          <Space align="start" style={{ marginBottom: 16 }}>
            <Avatar size={72} src={photoUrl} icon={<UserOutlined />} />
            <Descriptions size="small" column={1} items={[
              { key: 'sam', label: 'Логин', children: u.sam },
              { key: 'p', label: 'Проект', children: u.terminated ? <Tag color="red">уволен</Tag> : u.projectName },
              { key: 's', label: 'Состояние', children: u.enabled ? <Tag color="green">активен</Tag> : <Tag>отключён</Tag> },
              { key: 'l', label: 'Последний вход', children: u.lastLogon ? new Date(u.lastLogon).toLocaleString('ru-RU') : '—' },
            ]} />
          </Space>
          {steps && <Alert type="info" style={{ marginBottom: 12 }} message="Результат" description={<Steps steps={steps} />} closable onClose={() => setSteps(undefined)} />}
          <Tabs items={[
            {
              key: 'attrs', label: 'Атрибуты', children: (
                <Form form={form} layout="vertical" disabled={!can('ad-users.edit') || u.terminated}
                  onFinish={(values) => run('attrs', async () => { await api.ad.updateAttributes(u.sam, values); message.success('Сохранено') })}>
                  <Row gutter={12}>
                    {ATTRS.map(([k, label]) => (
                      <Col span={k === 'description' ? 24 : 12} key={k}><Form.Item name={k} label={label}><Input /></Form.Item></Col>
                    ))}
                  </Row>
                  {can('ad-users.edit') && !u.terminated && (
                    <Space wrap>
                      <Button type="primary" htmlType="submit" loading={busy === 'attrs'}>Сохранить</Button>
                      <Upload accept="image/*" showUploadList={false} beforeUpload={(file) => {
                        run('photo', async () => { await api.ad.setPhoto(u.sam, await toJpeg(file)); message.success('Фото обновлено') })
                        return false
                      }}><Button loading={busy === 'photo'}>Загрузить фото</Button></Upload>
                      {u.hasPhoto && <Popconfirm title="Удалить фото?" onConfirm={() => run('photo', async () => { await api.ad.removePhoto(u.sam) })}><Button danger>Удалить фото</Button></Popconfirm>}
                    </Space>
                  )}
                </Form>
              ),
            },
            { key: 'groups', label: `Группы (${card.groups.length})`, children: (
              <List size="small" dataSource={card.groups} renderItem={(g) => (
                <List.Item><Space>{g.name}{g.isPrimary && <Tag>основная</Tag>}</Space></List.Item>
              )} />
            ) },
            { key: 'actions', label: 'Действия', children: (
              <Space direction="vertical" style={{ width: '100%' }}>
                {can('ad-users.move') && !u.terminated && (
                  <Space.Compact style={{ width: '100%' }}>
                    <Select style={{ flex: 1 }} placeholder="Перенести в проект" value={target} onChange={setTarget}
                      options={projects.filter((p) => p.dn !== u.projectDn).map((p) => ({ value: p.dn, label: p.name }))} />
                    <Button disabled={!target} loading={busy === 'move'} onClick={() => run('move', async () => { await api.ad.move(u.sam, target!); message.success('Перенесён') })}>Перенести</Button>
                  </Space.Compact>
                )}
                {can('ad-users.password') && !u.terminated && (
                  <Space wrap>
                    <Popconfirm title="Сгенерировать новый пароль? Пользователь сменит его при входе."
                      onConfirm={() => run('pwd', async () => { const r = await api.ad.password(u.sam, { generate: true, mustChange: true }); if (r.password) showPassword(r.password) })}>
                      <Button loading={busy === 'pwd'}>Сгенерировать пароль</Button>
                    </Popconfirm>
                    <ManualPassword onSubmit={(password, mustChange) => run('pwd', async () => { await api.ad.password(u.sam, { password, generate: false, mustChange }); message.success('Пароль изменён') })} />
                  </Space>
                )}
                {can('ad-users.offboard') && !u.terminated && (
                  <Popconfirm title="Уволить пользователя?" description="Снять все группы, отключить, перенести в OU уволенных."
                    okButtonProps={{ danger: true }} onConfirm={() => run('off', async () => setSteps(await api.ad.deactivate(u.sam)))}>
                    <Button danger loading={busy === 'off'}>Уволить</Button>
                  </Popconfirm>
                )}
                {can('ad-users.offboard') && (u.terminated || !u.enabled) && (
                  <Space.Compact style={{ width: '100%' }}>
                    <Select style={{ flex: 1 }} placeholder="Восстановить в проект" value={target} onChange={setTarget}
                      options={projects.map((p) => ({ value: p.dn, label: p.name }))} />
                    <Button type="primary" disabled={!target} loading={busy === 'on'}
                      onClick={() => run('on', async () => { const r = await api.ad.activate(u.sam, target!); setSteps(r.steps); if (r.password) showPassword(r.password) })}>Восстановить</Button>
                  </Space.Compact>
                )}
              </Space>
            ) },
            { key: 'history', label: 'История', children: (
              <List size="small" dataSource={history} locale={{ emptyText: 'Нет записей' }} renderItem={(h) => (
                <List.Item><Space direction="vertical" size={0}>
                  <Space><Tag color={h.success ? 'green' : 'red'}>{h.action}</Tag><Typography.Text type="secondary">{new Date(h.timestamp).toLocaleString('ru-RU')} · {h.actor}</Typography.Text></Space>
                  <Typography.Text>{h.details}</Typography.Text>
                </Space></List.Item>
              )} />
            ) },
          ]} />
        </>
      )}
    </Drawer>
  )
}

function ManualPassword({ onSubmit }: { onSubmit: (password: string, mustChange: boolean) => void }) {
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm()
  return (
    <>
      <Button onClick={() => setOpen(true)}>Задать пароль</Button>
      <Modal title="Задать пароль" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} destroyOnHidden>
        <Form form={form} layout="vertical" initialValues={{ mustChange: true }}
          onFinish={(v) => { onSubmit(v.password, v.mustChange); setOpen(false); form.resetFields() }}>
          <Form.Item name="password" label="Пароль" rules={[{ required: true, min: 8, message: 'Не короче 8 символов' }]}><Input.Password autoComplete="new-password" /></Form.Item>
          <Form.Item name="confirm" label="Повтор" dependencies={['password']} rules={[{ required: true }, ({ getFieldValue }) => ({
            validator: (_, v) => (v === getFieldValue('password') ? Promise.resolve() : Promise.reject(new Error('Пароли не совпадают'))),
          })]}><Input.Password autoComplete="new-password" /></Form.Item>
          <Form.Item name="mustChange" valuePropName="checked"><Checkbox>Сменить при следующем входе</Checkbox></Form.Item>
        </Form>
      </Modal>
    </>
  )
}

export default function AdUsers() {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [projects, setProjects] = useState<AdProject[]>([])
  const [project, setProject] = useState<string>()
  const [status, setStatus] = useState<AdUserStatus>('active')
  const [q, setQ] = useState('')
  const [users, setUsers] = useState<AdUserView[]>([])
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<string>()

  useEffect(() => { api.ad.projects().then(setProjects).catch((e) => message.error(errorText(e, 'Не удалось загрузить проекты'))) }, [message])

  const load = useCallback(async () => {
    setLoading(true)
    try { setUsers(await api.ad.users({ project, status, q: q || undefined })) }
    catch (e) { message.error(errorText(e, 'Не удалось загрузить пользователей')) }
    finally { setLoading(false) }
  }, [project, status, q, message])

  useEffect(() => { load() }, [load])

  const columns: ColumnsType<AdUserView> = useMemo(() => [
    { title: 'Имя', dataIndex: 'displayName', render: (v, r) => v ?? r.sam, sorter: (a, b) => (a.displayName ?? a.sam).localeCompare(b.displayName ?? b.sam, 'ru') },
    { title: 'Логин', dataIndex: 'sam', width: 160 },
    { title: 'Должность', render: (_, r) => r.attributes.title },
    { title: 'Отдел', render: (_, r) => r.attributes.department },
    { title: 'Проект', dataIndex: 'projectName', width: 160, render: (v, r) => (r.terminated ? <Tag color="red">уволен</Tag> : v) },
    { title: 'Состояние', dataIndex: 'enabled', width: 110, render: (v) => (v ? <Tag color="green">активен</Tag> : <Tag>отключён</Tag>) },
  ], [])

  const statuses = [
    { value: 'active', label: 'Активные' }, { value: 'disabled', label: 'Отключённые' },
    ...(can('ad-users.offboard') ? [{ value: 'terminated', label: 'Уволенные' }] : []),
    { value: 'all', label: 'Все' },
  ]

  return (
    <>
      <PageHeader title="Пользователи AD" subtitle="Пользователи Active Directory по проектам"
        extra={<Button icon={<ReloadOutlined />} onClick={load}>Обновить</Button>} />
      <Card className="sp-glass">
        <Space wrap style={{ marginBottom: 12 }}>
          <Select allowClear placeholder="Все проекты" style={{ minWidth: 220 }} value={project} onChange={setProject}
            disabled={status === 'terminated'} options={projects.map((p) => ({ value: p.dn, label: p.name }))} />
          <Segmented value={status} onChange={(v) => setStatus(v as AdUserStatus)} options={statuses} />
          <Input.Search allowClear placeholder="ФИО, логин, почта" style={{ width: 280 }} onSearch={setQ} />
        </Space>
        <Table<AdUserView> rowKey="sam" size="small" loading={loading} dataSource={users} columns={columns}
          pagination={{ pageSize: 50, showSizeChanger: false }}
          onRow={(r) => ({ onClick: () => setSelected(r.sam), style: { cursor: 'pointer' } })} />
      </Card>
      <UserCard sam={selected} projects={projects} onClose={() => setSelected(undefined)} onChanged={load} />
    </>
  )
}
```

Если `Modal` в используемой версии AntD не знает `destroyOnHidden` — использовать `destroyOnClose` (сверить с другими страницами проекта).

`App.tsx` — маршрут по образцу существующих модульных: `<Route path="/ad/users" element={<Guard perm="ad-users.read" module="ad-users"><AdUsers /></Guard>} />` (+ `import AdUsers from './pages/AdUsers'`). `AppLayout.tsx` — пункт меню `{ key: '/ad/users', icon: <IdcardOutlined />, label: 'Пользователи AD', perm: 'ad-users.read', module: 'ad-users' }` в основной части меню (после «Журналы Windows»), `IdcardOutlined` — в импорт иконок. Сверить имя свойства `Guard` для модуля (`module`) с `Guard.tsx`.

- [ ] **Step 3: Проекты в редакторе ролей**

`Roles.tsx`, `RoleEditor`: загрузить проекты, если есть области «Проекты (OU)», и показать выбор вместо ввода тегов:

```tsx
  const [projects, setProjects] = useState<{ value: string; label: string }[]>([])
  const needsProjects = groups.some((g) => g.scopeTitle === 'Проекты (OU)')
  useEffect(() => {
    if (open && needsProjects) api.ad.projects().then((p) => setProjects(p.map((x) => ({ value: x.dn, label: x.name })))).catch(() => setProjects([]))
  }, [open, needsProjects])
```

в рендере областей:

```tsx
          {g.scopeTitle === 'Проекты (OU)' ? (
            <Select mode="multiple" allowClear style={{ width: '100%', marginTop: 4 }} placeholder="Пусто — все проекты"
              options={projects} value={scopes[g.id] ?? []} onChange={(v) => setScopes((s) => ({ ...s, [g.id]: v }))}
              notFoundContent="Проекты недоступны: включите модуль и задайте корневую OU" />
          ) : (
            <Select mode="tags" open={false} style={{ width: '100%', marginTop: 4 }} placeholder="Пусто — без ограничений"
              value={scopes[g.id] ?? []} onChange={(v) => setScopes((s) => ({ ...s, [g.id]: v }))} />
          )}
```

(`useEffect` — в импорт React; в списке ролей метка области без изменений.)

- [ ] **Step 4: Сборка и браузер**

Run: `cd src/frontend && npm run build && npm run lint` → Expected: без ошибок; в изменённых файлах нет предупреждений; `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.

Браузер (локальный экземпляр, порт 18181): «Модули» → «Пользователи AD» — «недоступен: компьютер не входит в домен» (машина разработки не в домене) — ожидаемо; страница `/ad/users` открывается только при включённом модуле. Полная проверка интерфейса — на DC (Task 9).

- [ ] **Step 5: Commit**

```bash
git add src/frontend
git commit -m "feat(ui): AD users page (list, card, attributes, photo, move, password, offboarding) and project scope picker"
```

---

### Task 9: Документация, выкатка на DC, проверка

**Files:**
- Modify: `docs/03-api-reference.md`, `docs/02-security.md`, `releases/package/README.md`

- [ ] **Step 1: Документация**

- API: `/ad/projects`, `/ad/users` (все маршруты Task 7, коды 403/404/409/422/503), права `ad-users.*`.
- Безопасность: область «Проекты (OU)»; уволенные доступны с `ad-users.offboard`; пароли один раз; что делегировать учётке записи (атрибуты, `thumbnailPhoto`, Reset Password, Write members на Fired Users и «Пользователи домена», перенос между OU).
- Пакетный README: «Модули → Пользователи AD»: включить, настройки (группа и OU уволенных), шаблоны ролей, «Проверка окружения».

- [ ] **Step 2: Полный прогон и пакет**

Run: `dotnet test src/tests/WinAdmin.Tests` (с `WINADMIN_TEST_POSTGRES`) → все PASS. Сборка пакета `releases\build.ps1` (PowerShell), zip без `*.db`/`*.key`/`network.json`/`database.json`.

- [ ] **Step 3: Выкатка на DC** — `dc9.ps1` (хеш, резервные копии, распаковка, health, откат).

- [ ] **Step 4: Проверка на DC (только чтение)**

Под `admin` (пароль — из файла на сервере): задать `RootOu = OU=Accounts,DC=pcs-msk,DC=com` (только настройка WinAdmin, учётка записи не задаётся), включить модуль `ad-users`; `GET /ad/projects` → проекты PCS; `GET /ad/users?status=active` → пользователи (число > 0); `GET /environment?module=ad-users` → `users.usersOu` с реальным результатом, проверки прав — «пропущено» (учётка записи не задана). Записи в AD не выполнять.

- [ ] **Step 5: Commit и push**

```bash
git add docs releases/package/README.md
git commit -m "docs: AD users module"
git push
```
