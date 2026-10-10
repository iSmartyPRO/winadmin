# 3c: модуль «Папки» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Модуль `ad-folders`: каталог сетевых папок из групп безопасности `sg_*` (логика Access), членство Full/Read, мастер «Новая папка» (пара групп + NTFS ACL), проверка и исправление прав NTFS, проверки окружения (группы, сопоставление дисков, шара, права на группы/создание, ACL папок) и вкладка «Папки» в карточке пользователя AD.

**Architecture:** Чистая логика (разбор описаний, каталог, поиск, имена групп, сопоставление дисков) — в `WinAdmin.Core/ActiveDirectory/Folders`; чтение групп/участников — `IAdFolderDirectory` (LDAP, постранично, диапазоны `member`); NTFS — `INtfsAccess` (ACL по SID, имперсонация служебной учёткой); бизнес-логика — `AdFoldersService` с singleton-кэшем каталога на 60 с; проверки — `AdFoldersCheck`.

**Tech Stack:** .NET 10, `System.DirectoryServices.Protocols`, `System.Security.AccessControl` (`FileSystemAclExtensions`), P/Invoke `LogonUser`, xUnit + Moq + WebApplicationFactory, React 19 + AntD 6.

**Spec:** `docs/superpowers/specs/2026-10-10-ad-users-folders-design.md` — §3 (модуль), §4.2 (`ad.rights.groups`, `ad.rights.create`, проверки `ad-folders`), §5–6. Основа — планы 3a и 3b.

## Global Constraints

- Права: `ad-folders.read`, `ad-folders.membership`, `ad-folders.create` (опасное) — с областью «Проекты (OU)» (имена `ad-folders.*` вместо `ad.folders.*` — правило каталога, Ruling 3b).
- Перед записью — `AdGuard.EnsureManaged` + `EnsureInScope`; группа — с префиксом модуля и **security**; иначе 403 до writer.
- Разбор описания и объединение в каталог — как в Access (`parseFolderFromGroup`, `buildFolderCatalog`, `smartFolderSearch`, `getUserFolderMembership`).
- Новые группы: `{Prefix}{orgCode}_{baseName}_full|_read`, глобальные безопасности, описание `{path};Full Access` / `{path};Read Only`; `baseName` — `[A-Za-z0-9_-]{1,40}`, имя ≤ 64.
- NTFS: Allow по SID, `ContainerInherit | ObjectInherit`, Full → `Modify`, Read → `ReadAndExecute`; чужие правила не трогаются; достаточное правило — `Skipped`.
- Каталог кэшируется 60 с; запись сбрасывает кэш.
- Аудит: `group.member.add`, `group.member.remove`, `folder.create`, `folder.acl.fix`.
- Коммиты заканчиваются `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; после `npm run build` — `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.
- Запись в боевой AD/шару в тестах — только `[AdFact]` и только в тестовом OU.

## Review Focus

1. Описание с путём в `info` вместо `description`, лишние `;`, путь с завершающим `\`, разный регистр — группы попадают в одну папку (Task 1: `Parser_handles_access_forms`, `Catalog_merges_by_normalized_path`).
2. Попытка изменить членство в группе с префиксом, но вне корневой OU / в скрытом проекте / в чужом проекте — 403, writer не вызывается (Task 4: `Membership_outside_zone_or_scope_is_denied_without_write`).
3. Мастер с уже существующей группой, у которой описание указывает на **другую** папку, — шаг «Группа Full» `Failed`, NTFS не трогается (Task 5: `Existing_group_for_other_path_fails_wizard`).
4. Путь с `..` или не сопоставленной буквой диска — мастер отказывает до любых изменений (Task 1: `Mapping_rejects_unmapped_and_dot_segments`; Task 5: `Wizard_validates_before_any_write`).
5. Группа с > 1500 участниками (диапазонное чтение `member;range=…`) — все участники учтены (Task 2: интеграционный `[AdFact]` и чистая функция `RangeNext` в `LdapRangeTests`).

---

## Карта файлов

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/Security/PermissionIds.cs` | + `AdFolders*` |
| `src/backend/WinAdmin.Core/ActiveDirectory/Folders/AdFoldersModule.cs` | Модуль, настройки, шаблон роли |
| `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderModels.cs` | Модели каталога |
| `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderDescriptionParser.cs` | Разбор описания группы |
| `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderCatalog.cs` | Объединение, поиск, доступы пользователя |
| `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderNaming.cs` | Имена групп, orgCode, сопоставление дисков |
| `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` | + `IAdFolderDirectory`, `INtfsAccess`, `IAdFoldersService` |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/LdapAdFolderDirectory.cs` | Группы и участники из LDAP |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/LdapRange.cs` | Диапазонное чтение `member` |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/NtfsAccess.cs` | ACL папок, создание каталога |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/Impersonation.cs` | `LogonUser` + `RunImpersonated` |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/FolderCatalogCache.cs` | Кэш каталога 60 с |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/AdFoldersService.cs` | Бизнес-логика |
| `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/AdFoldersCheck.cs` | Проверки модуля |
| `src/backend/WinAdmin.Api/Controllers/AdFoldersController.cs` | `/api/v1/ad/folders…` |
| `src/backend/WinAdmin.Api/Controllers/AdProjectsController.cs` | + источник `ad-folders` |
| `src/frontend/src/pages/AdFolders.tsx` | Страница «Папки» |
| `src/frontend/src/pages/AdUsers.tsx` | + вкладка «Папки» |
| `src/tests/WinAdmin.Tests/Fakes/FakeAdDomain.cs` | + `IAdFolderDirectory`, описание групп |
| `src/tests/WinAdmin.Tests/Fakes/FakeNtfs.cs` | Фейк `INtfsAccess` |

## Решения плана

- **`DriveMappings` — список строк `A=\\fs01\Projects`**: схема настроек модулей поддерживает только строки, числа, флаги и списки строк; список объектов из спец. §3.6 записывается строками и разбирается `FolderNaming.ParseMappings`. Цена: формат нужно знать (подсказка в описании поля).
- **Право менять ACL на корне шары** проверяется по явным Allow-правилам ACL корня для SID учётки записи и её доменных групп. Права, полученные через локальные группы файлового сервера, так не видны — результат «предупреждение: не подтверждено», а не «ошибка». Цена: ложное предупреждение.
- **Участники-группы** в членстве допускаются (как в спец.); вложенные группы в каталоге показываются с пометкой «группа», их состав не раскрывается.
- **Шаблон роли** «AD: администраторы папок» (все `ad-folders.*`), отдельно от 3b.

---

### Task 1: Модуль, чистая логика каталога, имена и сопоставление дисков

**Files:**
- Modify: `src/backend/WinAdmin.Core/Security/PermissionIds.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Folders/AdFoldersModule.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderModels.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderDescriptionParser.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderCatalog.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderNaming.cs`
- Modify: `src/backend/WinAdmin.Core/Modules/BuiltInModules.cs`
- Test: `src/tests/WinAdmin.Tests/FolderCatalogTests.cs`

**Interfaces:**
- Produces:
  - `PermissionIds.AdFoldersRead|AdFoldersMembership|AdFoldersCreate` = `"ad-folders.read|membership|create"`
  - `AdFoldersSettings { string GroupPrefix = "sg_"; string GroupsOuName = ""; List<string> DriveMappings = []; string FullRights = "Modify"; string ReadRights = "ReadAndExecute" }`
  - `AdFoldersModule : IWinAdminModule, IModuleLifecycle` (`ModuleId = "ad-folders"`, `AdminRoleName = "AD: администраторы папок"`)
  - `enum FolderAccess { Full, Read, Other }`
  - `record ParsedFolder(string Path, FolderAccess Access, string? AccessText)`
  - `record AdFolderGroup(string Dn, string Name, string? Sid, string? Description, string? Info, bool IsSecurity, IReadOnlyList<string> MemberDns)`
  - `record AdMember(string Dn, string Name, string? Sam, bool IsGroup, bool Enabled)`
  - `record FolderGroupView(string Dn, string Name, string? Sid, IReadOnlyList<AdMember> Members)`
  - `record Folder(string Path, string? ProjectDn, string? ProjectName, FolderGroupView? Full, FolderGroupView? Read, IReadOnlyList<FolderGroupView> Others, IReadOnlyList<string> Warnings)`
  - `record UnparsedGroup(string Dn, string Name, string? ProjectName, string Reason)`
  - `record FolderCatalogResult(IReadOnlyList<Folder> Folders, IReadOnlyList<UnparsedGroup> Unparsed)`
  - `record UserFolderAccess(string? ProjectName, string Path, bool HasFull, bool HasRead)`
  - `FolderDescriptionParser.Parse(string? description, string? info, string groupName) → ParsedFolder?`
  - `FolderDescriptionParser.NormalizeKey(string path) → string`
  - `FolderCatalog.Build(IEnumerable<AdFolderGroup> groups, IReadOnlyDictionary<string, AdMember> members, string rootOu, IReadOnlyCollection<char> mappedDrives) → FolderCatalogResult`
  - `FolderCatalog.Search(IReadOnlyList<Folder> folders, string? query) → IReadOnlyList<Folder>`
  - `FolderCatalog.UserAccess(IReadOnlyList<Folder> folders, string userDn) → IReadOnlyList<UserFolderAccess>`
  - `FolderNaming.ParseMappings(IEnumerable<string> lines) → IReadOnlyDictionary<char, string>` (`ArgumentException` при неверной строке)
  - `FolderNaming.ToUnc(string path, IReadOnlyDictionary<char, string> mappings) → string` (`ArgumentException`: несопоставленная буква, `..`, не путь)
  - `FolderNaming.DefaultOrgCode(string projectName, IEnumerable<string> groupNames, string prefix) → string?`
  - `FolderNaming.GroupNames(string prefix, string orgCode, string baseName) → (string Full, string Read)` (`ArgumentException`)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/FolderCatalogTests.cs
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class FolderDescriptionParserTests
{
    [Theory]
    [InlineData(@"A:\ТЕХНО-ЦЕНТР\01. Общие ресурсы;Full Access", null, "sg_x", @"A:\ТЕХНО-ЦЕНТР\01. Общие ресурсы", FolderAccess.Full)]
    [InlineData(@"A:\Проект\Docs; Read Only", null, "sg_x", @"A:\Проект\Docs", FolderAccess.Read)]
    [InlineData(@"A:\Проект\Docs;ro", null, "sg_x", @"A:\Проект\Docs", FolderAccess.Read)]
    [InlineData(@"A:\Проект\Docs;F", null, "sg_x", @"A:\Проект\Docs", FolderAccess.Full)]
    [InlineData(@"A:\Проект\Docs", null, "sg_a_docs_full", @"A:\Проект\Docs", FolderAccess.Full)]
    [InlineData(@"A:\Проект\Docs", null, "sg_a_docs_read_only", @"A:\Проект\Docs", FolderAccess.Read)]
    [InlineData(null, @"\\fs01\share\Docs;Full", "sg_x", @"\\fs01\share\Docs", FolderAccess.Full)]
    [InlineData(@"примечание;A:\Docs;;Read", null, "sg_x", @"A:\Docs", FolderAccess.Read)]
    public void Parser_handles_access_forms(string? description, string? info, string name, string path, FolderAccess access)
    {
        var parsed = FolderDescriptionParser.Parse(description, info, name)!;
        Assert.Equal(path, parsed.Path);
        Assert.Equal(access, parsed.Access);
    }

    [Fact]
    public void Unknown_access_is_other_and_no_path_is_null()
    {
        var other = FolderDescriptionParser.Parse(@"A:\Docs;Аудит", null, "sg_x")!;
        Assert.Equal(FolderAccess.Other, other.Access);
        Assert.Equal("Аудит", other.AccessText);
        Assert.Null(FolderDescriptionParser.Parse("просто текст", null, "sg_x_full"));
        Assert.Null(FolderDescriptionParser.Parse(null, null, "sg_x_full"));
    }

    [Fact]
    public void Normalized_key_ignores_case_and_trailing_slash()
        => Assert.Equal(FolderDescriptionParser.NormalizeKey(@"A:\Проект\Docs\"), FolderDescriptionParser.NormalizeKey(@"a:\проект\docs"));
}

public sealed class FolderCatalogTests
{
    private const string Root = "OU=Accounts,DC=pcs";
    private const string A = "OU=A," + Root;

    private static AdFolderGroup G(string name, string? description, params string[] members)
        => new($"CN={name},{A}", name, "S-1-5-21-1-" + Math.Abs(name.GetHashCode()), description, null, true, members);

    private static readonly Dictionary<string, AdMember> Members = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CN=ivan,OU=Users," + A] = new("CN=ivan,OU=Users," + A, "Иван", "ivan", false, true),
        ["CN=petr,OU=Users," + A] = new("CN=petr,OU=Users," + A, "Пётр", "petr", false, true),
    };

    [Fact]
    public void Catalog_merges_by_normalized_path()
    {
        var result = FolderCatalog.Build([
            G("sg_a_docs_full", @"A:\Проект\Docs;Full Access", "CN=ivan,OU=Users," + A),
            G("sg_a_docs_read", @"a:\проект\docs\;Read Only", "CN=petr,OU=Users," + A),
        ], Members, Root, ['A']);

        var folder = Assert.Single(result.Folders);
        Assert.Equal("A", folder.ProjectName);
        Assert.Equal("ivan", Assert.Single(folder.Full!.Members).Sam);
        Assert.Equal("petr", Assert.Single(folder.Read!.Members).Sam);
        Assert.Empty(folder.Warnings);
    }

    [Fact]
    public void Warnings_for_duplicates_missing_pair_and_unmapped_drive()
    {
        var result = FolderCatalog.Build([
            G("sg_a_x_full", @"B:\X;Full Access"),
            G("sg_a_x2_full", @"B:\X;Full Access"),
            G("sg_a_bad", "без пути"),
        ], Members, Root, ['A']);

        var folder = Assert.Single(result.Folders);
        Assert.Contains("две группы Full", folder.Warnings);
        Assert.Contains("нет группы Read", folder.Warnings);
        Assert.Contains("буква диска B: не сопоставлена", folder.Warnings);
        Assert.Equal("sg_a_bad", Assert.Single(result.Unparsed).Name);
    }

    [Fact]
    public void Unknown_members_are_shown_by_dn_name()
    {
        var folder = Assert.Single(FolderCatalog.Build([G("sg_a_y_full", @"A:\Y;Full", "CN=Гость,OU=Other,DC=pcs")], Members, Root, ['A']).Folders);
        Assert.Equal("Гость", Assert.Single(folder.Full!.Members).Name);
    }

    [Fact]
    public void Search_by_path_or_words()
    {
        var folders = FolderCatalog.Build([
            G("sg_a_docs_full", @"A:\Проект\Документы;Full"),
            G("sg_a_photo_full", @"A:\Проект\Фото;Full"),
        ], Members, Root, ['A']).Folders;
        Assert.Equal(@"A:\Проект\Документы", Assert.Single(FolderCatalog.Search(folders, @"a:\проект\документы")).Path);
        Assert.Equal(2, FolderCatalog.Search(folders, @"A:\Проект").Count);
        Assert.Equal(@"A:\Проект\Фото", Assert.Single(FolderCatalog.Search(folders, "фото проект")).Path);
        Assert.Equal(2, FolderCatalog.Search(folders, " ").Count);
    }

    [Fact]
    public void User_access_lists_full_and_read()
    {
        var folders = FolderCatalog.Build([
            G("sg_a_docs_full", @"A:\Docs;Full", "CN=ivan,OU=Users," + A),
            G("sg_a_docs_read", @"A:\Docs;Read", "CN=ivan,OU=Users," + A),
            G("sg_a_x_read", @"A:\X;Read", "CN=petr,OU=Users," + A),
        ], Members, Root, ['A']).Folders;
        var access = Assert.Single(FolderCatalog.UserAccess(folders, "CN=ivan,OU=Users," + A));
        Assert.True(access.HasFull);
        Assert.True(access.HasRead);
    }
}

public sealed class FolderNamingTests
{
    private static readonly IReadOnlyDictionary<char, string> Map = FolderNaming.ParseMappings([@"A=\\fs01\Projects", @"b:=\\fs02\B\"]);

    [Fact]
    public void Maps_drive_paths_and_keeps_unc()
    {
        Assert.Equal(@"\\fs01\Projects\Проект\Docs", FolderNaming.ToUnc(@"A:\Проект\Docs", Map));
        Assert.Equal(@"\\fs02\B\X", FolderNaming.ToUnc(@"B:\X\", Map));
        Assert.Equal(@"\\fs03\s\X", FolderNaming.ToUnc(@"\\fs03\s\X", Map));
    }

    [Theory]
    [InlineData(@"C:\X")]
    [InlineData(@"A:\X\..\Windows")]
    [InlineData("просто текст")]
    [InlineData(@"\\server")]
    public void Mapping_rejects_unmapped_and_dot_segments(string path)
        => Assert.Throws<ArgumentException>(() => FolderNaming.ToUnc(path, Map));

    [Theory]
    [InlineData("A")]
    [InlineData(@"AB=\\fs\x")]
    [InlineData("A=C:\\local")]
    public void Bad_mapping_lines_are_rejected(string line)
        => Assert.Throws<ArgumentException>(() => FolderNaming.ParseMappings([line]));

    [Fact]
    public void Org_code_from_existing_groups_or_project_name()
    {
        Assert.Equal("co", FolderNaming.DefaultOrgCode("Сервисный центр", ["sg_co_a_full", "sg_co_b_read", "sg_x_c_full", "other"], "sg_"));
        Assert.Equal("amur", FolderNaming.DefaultOrgCode("Amur", [], "sg_"));
        Assert.Null(FolderNaming.DefaultOrgCode("Амур", [], "sg_"));
    }

    [Fact]
    public void Group_names_are_validated()
    {
        Assert.Equal(("sg_co_docs_full", "sg_co_docs_read"), FolderNaming.GroupNames("sg_", "co", "docs"));
        Assert.Throws<ArgumentException>(() => FolderNaming.GroupNames("sg_", "co", "доки"));
        Assert.Throws<ArgumentException>(() => FolderNaming.GroupNames("sg_", "co", new string('x', 41)));
        Assert.Throws<ArgumentException>(() => FolderNaming.GroupNames("sg_", "co x", "docs"));
    }
}

public sealed class AdFoldersModuleTests
{
    [Fact]
    public void Module_declares_scoped_permissions_and_domain_requirement()
    {
        var module = Assert.Single(BuiltInModules.All, m => m.Id == AdFoldersModule.ModuleId);
        Assert.Equal(ModuleRequirements.DomainJoined, module.Requirements);
        Assert.All(module.Permissions, p => Assert.True(p.Scopable));
        Assert.True(module.Permissions.Single(p => p.Id == PermissionIds.AdFoldersCreate).Dangerous);
        Assert.Equal("sg_", new AdFoldersSettings().GroupPrefix);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~FolderDescriptionParserTests|FullyQualifiedName~FolderCatalogTests|FullyQualifiedName~FolderNamingTests|FullyQualifiedName~AdFoldersModuleTests"`
Expected: FAIL — компиляция: нет `WinAdmin.Core.ActiveDirectory.Folders`.

- [ ] **Step 3: Write minimal implementation**

`PermissionIds.cs`:

```csharp
    public const string AdFoldersRead = "ad-folders.read";
    public const string AdFoldersMembership = "ad-folders.membership";
    public const string AdFoldersCreate = "ad-folders.create";
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Folders/AdFoldersModule.cs
using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory.Folders;

public sealed class AdFoldersSettings
{
    [Description("Префикс групп доступа к папкам")]
    public string GroupPrefix { get; set; } = "sg_";

    [Description("OU для новых групп внутри проекта (пусто — сама OU проекта)")]
    public string GroupsOuName { get; set; } = "";

    [Description(@"Буквы дисков → UNC, по строке: A=\\fs01\Projects")]
    public List<string> DriveMappings { get; set; } = [];

    [Description("NTFS-права группы Full (Modify, FullControl, …)")]
    public string FullRights { get; set; } = "Modify";

    [Description("NTFS-права группы Read (ReadAndExecute, Read, …)")]
    public string ReadRights { get; set; } = "ReadAndExecute";
}

/// <summary>Доступ к сетевым папкам через группы безопасности (логика Access).</summary>
public sealed class AdFoldersModule : IWinAdminModule, IModuleLifecycle
{
    public const string ModuleId = "ad-folders";
    public const string AdminRoleName = "AD: администраторы папок";

    public string Id => ModuleId;
    public string Title => "Папки";
    public string? Description => "Доступ к сетевым папкам через группы безопасности: участники Full/Read, новые папки, права NTFS";
    public ModuleRequirements Requirements => ModuleRequirements.DomainJoined;
    public bool EnabledByDefault => false;
    public Type? SettingsType => typeof(AdFoldersSettings);
    public IScopeProvider? Scope { get; } = new ProjectScopeProvider();

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(PermissionIds.AdFoldersRead, "Просмотр папок", "Каталог папок, участники, доступы пользователя", Scopable: true),
        new(PermissionIds.AdFoldersMembership, "Участники папок", "Добавить или убрать участника в Full/Read", Scopable: true),
        new(PermissionIds.AdFoldersCreate, "Новые папки и права NTFS", "Создание групп и выставление прав на папке", Scopable: true, Dangerous: true),
    ];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public async Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct)
    {
        var roles = services.GetRequiredService<IRoleService>();
        var catalog = services.GetRequiredService<PermissionCatalog>();
        if ((await roles.ListAsync(ct)).Any(r => string.Equals(r.Name, AdminRoleName, StringComparison.CurrentCultureIgnoreCase)))
            return;
        var system = new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "system", []), "system",
            PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
        await roles.CreateAsync(new SaveRoleRequest(AdminRoleName, "Шаблон: все действия с папками",
            Permissions.Select(p => new RoleGrantDto(p.Id, null)).ToList()), system, ct);
    }
}
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderModels.cs
namespace WinAdmin.Core.ActiveDirectory.Folders;

public enum FolderAccess { Full, Read, Other }

public sealed record ParsedFolder(string Path, FolderAccess Access, string? AccessText);

/// <summary>Группа доступа к папке, как она прочитана из AD.</summary>
public sealed record AdFolderGroup(
    string Dn, string Name, string? Sid, string? Description, string? Info, bool IsSecurity, IReadOnlyList<string> MemberDns);

public sealed record AdMember(string Dn, string Name, string? Sam, bool IsGroup, bool Enabled);

public sealed record FolderGroupView(string Dn, string Name, string? Sid, IReadOnlyList<AdMember> Members);

public sealed record Folder(
    string Path, string? ProjectDn, string? ProjectName, FolderGroupView? Full, FolderGroupView? Read,
    IReadOnlyList<FolderGroupView> Others, IReadOnlyList<string> Warnings);

public sealed record UnparsedGroup(string Dn, string Name, string? ProjectName, string Reason);

public sealed record FolderCatalogResult(IReadOnlyList<Folder> Folders, IReadOnlyList<UnparsedGroup> Unparsed);

public sealed record UserFolderAccess(string? ProjectName, string Path, bool HasFull, bool HasRead);
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderDescriptionParser.cs
namespace WinAdmin.Core.ActiveDirectory.Folders;

/// <summary>Порт parseFolderFromGroup (Access): «путь;тип доступа» в description (или info), тип — иначе по суффиксу имени.</summary>
public static class FolderDescriptionParser
{
    public static ParsedFolder? Parse(string? description, string? info, string groupName)
    {
        string text = (!string.IsNullOrWhiteSpace(description) ? description : info ?? "").Trim();
        if (text.Length == 0) return null;

        var parts = text.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        int index = parts.FindIndex(IsPath);
        if (index < 0) return null;

        string path = parts[index];
        string candidate = string.Join(" ", parts.Skip(index + 1)).Trim();
        var (access, accessText) = Normalize(candidate);
        if (access is null)
        {
            var segments = Key(groupName).Split('_', StringSplitOptions.RemoveEmptyEntries);
            string last = segments.LastOrDefault() ?? "";
            string last2 = string.Join("_", segments.TakeLast(2));
            if (last is "full" or "f" || last2 == "full_access") access = FolderAccess.Full;
            else if (last is "read" or "r" or "ro" || last2 == "read_only") access = FolderAccess.Read;
        }
        return new ParsedFolder(path, access ?? FolderAccess.Other, accessText);
    }

    /// <summary>Ключ папки: без учёта регистра и завершающих «\» «/».</summary>
    public static string NormalizeKey(string path) => Key(path.Trim().TrimEnd('\\', '/'));

    private static bool IsPath(string s) => s.Contains(@":\") || s.StartsWith(@"\\") || s.StartsWith('/');

    private static (FolderAccess?, string?) Normalize(string s)
    {
        if (s.Length == 0) return (null, null);
        string n = Key(s);
        if (n is "full" or "f" || n.Contains("full")) return (FolderAccess.Full, s);
        if (n is "read" or "r" or "ro" || n.Contains("read")) return (FolderAccess.Read, s);
        return (FolderAccess.Other, s);
    }

    private static string Key(string s) => s.Trim().ToLowerInvariant();
}
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderCatalog.cs
namespace WinAdmin.Core.ActiveDirectory.Folders;

/// <summary>Порт buildFolderCatalog / smartFolderSearch / getUserFolderMembership (Access).</summary>
public static class FolderCatalog
{
    public static FolderCatalogResult Build(
        IEnumerable<AdFolderGroup> groups, IReadOnlyDictionary<string, AdMember> members, string rootOu, IReadOnlyCollection<char> mappedDrives)
    {
        var unparsed = new List<UnparsedGroup>();
        var byKey = new Dictionary<string, (string Path, string? ProjectDn, List<(AdFolderGroup G, ParsedFolder P)> Items)>();
        foreach (var g in groups)
        {
            string? project = DnUtils.ProjectDn(g.Dn, rootOu);
            var parsed = FolderDescriptionParser.Parse(g.Description, g.Info, g.Name);
            if (parsed is null)
            {
                unparsed.Add(new UnparsedGroup(g.Dn, g.Name, Name(project, rootOu), "в описании нет пути к папке"));
                continue;
            }
            string key = FolderDescriptionParser.NormalizeKey(parsed.Path);
            if (!byKey.TryGetValue(key, out var entry))
                byKey[key] = entry = (parsed.Path.TrimEnd('\\', '/'), project, []);
            entry.Items.Add((g, parsed));
        }

        var folders = byKey.Values.Select(e =>
        {
            var full = e.Items.Where(i => i.P.Access == FolderAccess.Full).ToList();
            var read = e.Items.Where(i => i.P.Access == FolderAccess.Read).ToList();
            var warnings = new List<string>();
            if (full.Count > 1) warnings.Add("две группы Full");
            if (read.Count > 1) warnings.Add("две группы Read");
            if (full.Count == 0) warnings.Add("нет группы Full");
            if (read.Count == 0) warnings.Add("нет группы Read");
            if (e.Path.Length > 1 && e.Path[1] == ':' && !mappedDrives.Contains(char.ToUpperInvariant(e.Path[0])))
                warnings.Add($"буква диска {char.ToUpperInvariant(e.Path[0])}: не сопоставлена");
            return new Folder(e.Path, e.ProjectDn, Name(e.ProjectDn, rootOu),
                full.Select(i => View(i.G, members)).FirstOrDefault(),
                read.Select(i => View(i.G, members)).FirstOrDefault(),
                e.Items.Where(i => i.P.Access == FolderAccess.Other).Select(i => View(i.G, members)).ToList(),
                warnings);
        })
        .OrderBy(f => f.ProjectName ?? "", StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return new FolderCatalogResult(folders, unparsed);
    }

    public static IReadOnlyList<Folder> Search(IReadOnlyList<Folder> folders, string? query)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0) return folders;
        bool isPath = q.Contains(@":\") || q.StartsWith(@"\\") || q.StartsWith('/');
        if (isPath)
        {
            string nq = FolderDescriptionParser.NormalizeKey(q);
            var exact = folders.Where(f => FolderDescriptionParser.NormalizeKey(f.Path) == nq);
            var partial = folders.Where(f =>
            {
                string np = FolderDescriptionParser.NormalizeKey(f.Path);
                return np != nq && (np.Contains(nq) || nq.Contains(np));
            });
            return exact.Concat(partial).ToList();
        }
        var tokens = q.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return folders.Where(f =>
        {
            string hay = string.Join(" ", f.ProjectName ?? "", f.Path, f.Path.Replace('\\', ' ').Replace('/', ' '),
                f.Full?.Name ?? "", f.Read?.Name ?? "", "full access read only").ToLowerInvariant();
            return tokens.All(hay.Contains);
        }).ToList();
    }

    public static IReadOnlyList<UserFolderAccess> UserAccess(IReadOnlyList<Folder> folders, string userDn)
    {
        bool Has(FolderGroupView? g) => g?.Members.Any(m => string.Equals(m.Dn, userDn, StringComparison.OrdinalIgnoreCase)) == true;
        return folders.Select(f => new UserFolderAccess(f.ProjectName, f.Path, Has(f.Full), Has(f.Read)))
            .Where(a => a.HasFull || a.HasRead)
            .ToList();
    }

    private static FolderGroupView View(AdFolderGroup g, IReadOnlyDictionary<string, AdMember> members)
        => new(g.Dn, g.Name, g.Sid, g.MemberDns
            .Select(dn => members.TryGetValue(dn, out var m) ? m : new AdMember(dn, DnUtils.FirstValue(dn), null, false, true))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

    private static string? Name(string? projectDn, string rootOu)
        => projectDn is null ? null : DnUtils.RelativeOuPath(projectDn, rootOu).FirstOrDefault();
}
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/Folders/FolderNaming.cs
using System.Text.RegularExpressions;

namespace WinAdmin.Core.ActiveDirectory.Folders;

public static partial class FolderNaming
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex NamePart();

    /// <summary>Строки «A=\\fs01\Projects» (или «A:=…») → буква → UNC без завершающего «\».</summary>
    public static IReadOnlyDictionary<char, string> ParseMappings(IEnumerable<string> lines)
    {
        var map = new Dictionary<char, string>();
        foreach (var raw in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            int eq = raw.IndexOf('=');
            string drive = eq < 0 ? "" : raw[..eq].Trim().TrimEnd(':');
            string unc = eq < 0 ? "" : raw[(eq + 1)..].Trim().TrimEnd('\\');
            if (drive.Length != 1 || !char.IsLetter(drive[0]) || !unc.StartsWith(@"\\") || unc.Length < 5)
                throw new ArgumentException($"Сопоставление «{raw}»: ожидается вида A=\\\\сервер\\шара.");
            map[char.ToUpperInvariant(drive[0])] = unc;
        }
        return map;
    }

    /// <summary>Путь из описания группы → UNC. Несопоставленная буква, «..» или не путь — ArgumentException.</summary>
    public static string ToUnc(string path, IReadOnlyDictionary<char, string> mappings)
    {
        string p = path.Trim().TrimEnd('\\');
        if (p.Split('\\', '/').Any(s => s == ".."))
            throw new ArgumentException("Путь не должен содержать «..».");
        if (p.StartsWith(@"\\"))
        {
            if (p.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                throw new ArgumentException(@"UNC-путь должен быть вида \\сервер\шара\….");
            return p;
        }
        if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':')
        {
            char drive = char.ToUpperInvariant(p[0]);
            if (!mappings.TryGetValue(drive, out var unc))
                throw new ArgumentException($"Буква диска {drive}: не сопоставлена с сетевым путём (настройки модуля «Папки»).");
            return unc + p[2..];
        }
        throw new ArgumentException("Путь должен быть вида A:\\… или \\\\сервер\\шара\\….");
    }

    /// <summary>Самая частая вторая часть имён групп проекта «sg_&lt;org&gt;_…»; иначе — имя проекта латиницей; иначе null.</summary>
    public static string? DefaultOrgCode(string projectName, IEnumerable<string> groupNames, string prefix)
    {
        var fromGroups = groupNames
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..].Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(s => !string.IsNullOrEmpty(s))
            .GroupBy(s => s!.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (fromGroups is not null) return fromGroups;
        string fromName = new string(projectName.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        return NamePart().IsMatch(fromName) ? fromName : null;
    }

    public static (string Full, string Read) GroupNames(string prefix, string orgCode, string baseName)
    {
        if (!NamePart().IsMatch(orgCode)) throw new ArgumentException("Код организации — латиница, цифры, «_» или «-».");
        if (!NamePart().IsMatch(baseName)) throw new ArgumentException("Имя папки для групп — латиница, цифры, «_» или «-», до 40 символов.");
        string full = $"{prefix}{orgCode}_{baseName}_full".ToLowerInvariant();
        string read = $"{prefix}{orgCode}_{baseName}_read".ToLowerInvariant();
        if (full.Length > 64) throw new ArgumentException("Имя группы длиннее 64 символов — сократите имя.");
        return (full, read);
    }
}
```

`BuiltInModules.All` — добавить `new ActiveDirectory.Folders.AdFoldersModule(),` после `AdUsersModule`.

- [ ] **Step 4: Run test to verify it passes**

Run: та же команда, что в Step 2; затем `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~Permission|FullyQualifiedName~Module"`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core src/tests/WinAdmin.Tests/FolderCatalogTests.cs
git commit -m "feat(ad-folders): module, catalog parser/merge/search, group naming and drive mappings"
```

---

### Task 2: Чтение групп и участников из AD

**Files:**
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` (+ `IAdFolderDirectory`)
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/LdapRange.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/LdapAdFolderDirectory.cs`
- Modify: `src/tests/WinAdmin.Tests/Fakes/FakeAdDomain.cs` (описание групп, `IAdFolderDirectory`)
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/LdapAdFolderDirectoryTests.cs`

**Interfaces:**
- Consumes: `LdapConnections`, `LdapMapping`, `LdapFilter`, `DnUtils` (3a/1c); модели Task 1
- Produces:
  - `IAdFolderDirectory`:
    - `ListGroupsAsync(string baseDn, string prefix, ct) → IReadOnlyList<AdFolderGroup>`
    - `ResolveMembersAsync(IEnumerable<string> dns, ct) → IReadOnlyDictionary<string, AdMember>`
    - `GetGroupAsync(string dn, ct) → AdFolderGroup?`
    - `FindGroupByNameAsync(string sam, ct) → AdFolderGroup?` (весь домен)
    - `FindMemberAsync(string samOrDn, ct) → AdMember?`
  - `LdapRange.Next(string attributeName) → (int Start, bool Done)?` — для `member;range=0-1499` → `(1500, false)`, `member;range=1500-*` → `(…, true)`
  - `FakeAdDomain.Group.Description`, `.Info`, `.IsSecurity`; `FakeAdDomain : IAdFolderDirectory`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/LdapAdFolderDirectoryTests.cs
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;

namespace WinAdmin.Tests;

public sealed class LdapRangeTests
{
    [Theory]
    [InlineData("member;range=0-1499", 1500, false)]
    [InlineData("member;range=1500-*", -1, true)]
    public void Range_attribute_names(string name, int next, bool done)
    {
        var r = LdapRange.Next(name)!.Value;
        Assert.Equal(done, r.Done);
        if (!done) Assert.Equal(next, r.Start);
    }

    [Fact]
    public void Plain_member_is_not_ranged() => Assert.Null(LdapRange.Next("member"));
}

public sealed class LdapAdFolderDirectoryTests
{
    [Fact]
    public async Task Directory_off_is_unavailable()
    {
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(DirectorySettings.Disabled));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => new LdapAdFolderDirectory(dir).ListGroupsAsync("OU=A,DC=x", "sg_"));
    }

    [AdFact]
    public async Task Real_directory_lists_groups_and_resolves_members()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));
        var folders = new LdapAdFolderDirectory(dir, new System.Net.NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
        var groups = await folders.ListGroupsAsync(E("WINADMIN_TEST_AD_ROOT"), "sg_");
        var members = await folders.ResolveMembersAsync(groups.SelectMany(g => g.MemberDns));
        Assert.All(groups, g => Assert.StartsWith("sg_", g.Name, StringComparison.OrdinalIgnoreCase));
        Assert.All(members.Values, m => Assert.False(string.IsNullOrEmpty(m.Name)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapRangeTests|FullyQualifiedName~LdapAdFolderDirectoryTests"`
Expected: FAIL — компиляция: нет `LdapRange`, `LdapAdFolderDirectory`.

- [ ] **Step 3: Write minimal implementation**

`IAdServices.cs` — добавить (`using WinAdmin.Core.ActiveDirectory.Folders;`):

```csharp
/// <summary>Группы доступа к папкам и их участники (учётка компьютера).</summary>
public interface IAdFolderDirectory
{
    Task<IReadOnlyList<AdFolderGroup>> ListGroupsAsync(string baseDn, string prefix, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, AdMember>> ResolveMembersAsync(IEnumerable<string> dns, CancellationToken ct = default);
    Task<AdFolderGroup?> GetGroupAsync(string dn, CancellationToken ct = default);
    /// <summary>Группа по sAMAccountName во всём домене.</summary>
    Task<AdFolderGroup?> FindGroupByNameAsync(string sam, CancellationToken ct = default);
    /// <summary>Пользователь или группа по sAMAccountName или DN.</summary>
    Task<AdMember?> FindMemberAsync(string samOrDn, CancellationToken ct = default);
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/LdapRange.cs
namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Диапазонное чтение многозначных атрибутов (member;range=0-1499).</summary>
public static class LdapRange
{
    /// <summary>null — атрибут не диапазонный; Done — последний кусок; иначе Start следующего.</summary>
    public static (int Start, bool Done)? Next(string attributeName)
    {
        int i = attributeName.IndexOf(";range=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        string range = attributeName[(i + 7)..];
        int dash = range.IndexOf('-');
        string end = range[(dash + 1)..];
        return end == "*" ? (-1, true) : (int.Parse(end) + 1, false);
    }
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/LdapAdFolderDirectory.cs
using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Группы sg_* и их участники: постранично, с диапазонным чтением member (> 1500).</summary>
public sealed class LdapAdFolderDirectory(IDirectorySettingsStore directory, NetworkCredential? readCredential = null) : IAdFolderDirectory
{
    private const int SecurityEnabled = unchecked((int)0x80000000);
    private static readonly string[] GroupAttributes = ["cn", "sAMAccountName", "objectSid", "description", "info", "groupType", "member"];
    private static readonly string[] MemberAttributes = ["objectClass", "sAMAccountName", "displayName", "cn", "userAccountControl"];

    public async Task<IReadOnlyList<AdFolderGroup>> ListGroupsAsync(string baseDn, string prefix, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        string filter = $"(&(objectCategory=group)(cn={LdapFilter.Escape(prefix)}*))";
        return Run(dir, c => LdapConnections.SearchPaged(c, baseDn, filter, SearchScope.Subtree, GroupAttributes)
            .Select(e => ToGroup(c, e)).ToList());
    }

    public async Task<IReadOnlyDictionary<string, AdMember>> ResolveMembersAsync(IEnumerable<string> dns, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        var all = dns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Run(dir, c =>
        {
            var result = new Dictionary<string, AdMember>(StringComparer.OrdinalIgnoreCase);
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            foreach (var batch in all.Chunk(30))
            {
                var filter = new StringBuilder("(|");
                foreach (var dn in batch) filter.Append("(distinguishedName=").Append(LdapFilter.Escape(dn)).Append(')');
                filter.Append(')');
                foreach (var e in LdapConnections.SearchPaged(c, baseDn, filter.ToString(), SearchScope.Subtree, MemberAttributes))
                    result[e.DistinguishedName] = ToMember(e);
            }
            return (IReadOnlyDictionary<string, AdMember>)result;
        });
    }

    public async Task<AdFolderGroup?> GetGroupAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, c =>
        {
            try
            {
                var e = ((SearchResponse)c.SendRequest(new SearchRequest(dn, "(objectCategory=group)", SearchScope.Base, GroupAttributes))).Entries;
                return e.Count == 1 ? ToGroup(c, e[0]) : null;
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject)
            {
                return null;
            }
        });
    }

    public async Task<AdFolderGroup?> FindGroupByNameAsync(string sam, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            var r = LdapConnections.Search(c, new SearchRequest(baseDn,
                $"(&(objectCategory=group)(sAMAccountName={LdapFilter.Escape(sam)}))", SearchScope.Subtree, GroupAttributes) { SizeLimit = 2 });
            return r.Entries.Count == 1 ? ToGroup(c, r.Entries[0]) : null;
        });
    }

    public async Task<AdMember?> FindMemberAsync(string samOrDn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        string value = samOrDn.Trim();
        return Run(dir, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            string filter = value.Contains('=')
                ? $"(distinguishedName={LdapFilter.Escape(value)})"
                : $"(&(|(objectCategory=person)(objectCategory=group))(sAMAccountName={LdapFilter.Escape(value)}))";
            var r = LdapConnections.Search(c, new SearchRequest(baseDn, filter, SearchScope.Subtree, MemberAttributes) { SizeLimit = 2 });
            return r.Entries.Count == 1 ? ToMember(r.Entries[0]) : null;
        });
    }

    private static AdFolderGroup ToGroup(LdapConnection c, SearchResultEntry e)
    {
        string? Str(string name) => e.Attributes[name]?[0] as string;
        int.TryParse(Str("groupType"), out int groupType);
        return new AdFolderGroup(
            e.DistinguishedName, Str("cn") ?? DnUtils.FirstValue(e.DistinguishedName),
            e.Attributes["objectSid"]?[0] is byte[] sid ? LdapMapping.SidFromBytes(sid) : null,
            Str("description"), Str("info"), (groupType & SecurityEnabled) != 0, Members(c, e));
    }

    /// <summary>member целиком; у больших групп AD отдаёт member;range=0-1499 — дочитываем кусками.</summary>
    private static IReadOnlyList<string> Members(LdapConnection c, SearchResultEntry e)
    {
        var result = new List<string>();
        string? rangedName = null;
        foreach (string name in e.Attributes.AttributeNames)
        {
            if (name.Equals("member", StringComparison.OrdinalIgnoreCase))
                result.AddRange(e.Attributes[name].GetValues(typeof(string)).Cast<string>());
            else if (name.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(e.Attributes[name].GetValues(typeof(string)).Cast<string>());
                rangedName = name;
            }
        }
        while (rangedName is not null && LdapRange.Next(rangedName) is { Done: false } next)
        {
            var part = ((SearchResponse)c.SendRequest(new SearchRequest(e.DistinguishedName, "(objectClass=*)", SearchScope.Base,
                $"member;range={next.Start}-*"))).Entries[0];
            rangedName = part.Attributes.AttributeNames.Cast<string>()
                .FirstOrDefault(n => n.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase));
            if (rangedName is null) break;
            result.AddRange(part.Attributes[rangedName].GetValues(typeof(string)).Cast<string>());
        }
        return result;
    }

    private static AdMember ToMember(SearchResultEntry e)
    {
        string? Str(string name) => e.Attributes[name]?[0] as string;
        bool isGroup = e.Attributes["objectClass"]?.GetValues(typeof(string)).Cast<string>()
            .Contains("group", StringComparer.OrdinalIgnoreCase) == true;
        return new AdMember(e.DistinguishedName, Str("displayName") ?? Str("cn") ?? DnUtils.FirstValue(e.DistinguishedName),
            Str("sAMAccountName"), isGroup, isGroup || LdapMapping.IsEnabled(Str("userAccountControl")));
    }

    private async Task<DirectorySettings> EnabledAsync(CancellationToken ct)
    {
        var dir = await directory.GetAsync(ct);
        if (!dir.Enabled || dir.Domain is null) throw new DirectoryUnavailableException("Подключение к домену выключено");
        return dir;
    }

    private T Run<T>(DirectorySettings dir, Func<LdapConnection, T> action)
    {
        try
        {
            using var connection = LdapConnections.Open(dir, readCredential);
            return action(connection);
        }
        catch (LdapException ex)
        {
            throw LdapConnections.Unavailable(ex);
        }
    }
}
```

`DependencyInjection`:

```csharp
        services.AddSingleton<IAdFolderDirectory>(sp => new LdapAdFolderDirectory(sp.GetRequiredService<IDirectorySettingsStore>()));
```

`FakeAdDomain` — в класс `Group` добавить:

```csharp
        public string? Description { get; set; }
        public string? Info { get; set; }
        public bool IsSecurity { get; set; } = true;
```

в `CreateGroupAsync` после создания — `g.Description = description;` (вернуть `g.Dn`); класс реализует ещё `IAdFolderDirectory`:

```csharp
    private AdFolderGroup ToFolderGroup(Group g)
        => new(g.Dn, g.Name, g.Sid, g.Description, g.Info, g.IsSecurity, g.Members.ToList());

    public Task<IReadOnlyList<AdFolderGroup>> ListGroupsAsync(string baseDn, string prefix, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AdFolderGroup>>(Groups
            .Where(g => g.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && DnUtils.IsUnderOrSame(g.Dn, baseDn))
            .Select(ToFolderGroup).ToList());

    public Task<IReadOnlyDictionary<string, AdMember>> ResolveMembersAsync(IEnumerable<string> dns, CancellationToken ct = default)
    {
        var set = new HashSet<string>(dns, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, AdMember>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in Users.Where(u => set.Contains(u.Dn)))
            result[u.Dn] = new AdMember(u.Dn, u.Attributes.GetValueOrDefault("displayName") ?? u.Sam, u.Sam, false, u.Enabled);
        foreach (var g in Groups.Where(g => set.Contains(g.Dn)))
            result[g.Dn] = new AdMember(g.Dn, g.Name, g.Name, true, true);
        return Task.FromResult<IReadOnlyDictionary<string, AdMember>>(result);
    }

    public Task<AdFolderGroup?> GetGroupAsync(string dn, CancellationToken ct = default)
        => Task.FromResult(Groups.FirstOrDefault(g => g.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase)) is { } g ? ToFolderGroup(g) : null);

    public Task<AdFolderGroup?> FindGroupByNameAsync(string sam, CancellationToken ct = default)
        => Task.FromResult(Groups.FirstOrDefault(g => g.Name.Equals(sam, StringComparison.OrdinalIgnoreCase)) is { } g ? ToFolderGroup(g) : null);

    public Task<AdMember?> FindMemberAsync(string samOrDn, CancellationToken ct = default)
    {
        if (Users.FirstOrDefault(u => u.Sam.Equals(samOrDn, StringComparison.OrdinalIgnoreCase) || u.Dn.Equals(samOrDn, StringComparison.OrdinalIgnoreCase)) is { } u)
            return Task.FromResult<AdMember?>(new AdMember(u.Dn, u.Attributes.GetValueOrDefault("displayName") ?? u.Sam, u.Sam, false, u.Enabled));
        if (Groups.FirstOrDefault(g => g.Name.Equals(samOrDn, StringComparison.OrdinalIgnoreCase) || g.Dn.Equals(samOrDn, StringComparison.OrdinalIgnoreCase)) is { } g)
            return Task.FromResult<AdMember?>(new AdMember(g.Dn, g.Name, g.Name, true, true));
        return Task.FromResult<AdMember?>(null);
    }
```

(`using WinAdmin.Core.ActiveDirectory.Folders;` в начало.)

- [ ] **Step 4: Run test to verify it passes**

Run: та же команда, что в Step 2; затем `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdUsers"` (фейк изменён — регрессия 3b).
Expected: PASS (интеграционный — Skip).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(ad-folders): LDAP folder groups and members (paged, ranged member) and fake domain support"
```

---

### Task 3: NTFS — ACL по SID, создание папки, имперсонация

**Files:**
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` (+ `INtfsAccess`, `AclNeed`, `FolderAclState`)
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/Impersonation.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/NtfsAccess.cs`
- Create: `src/tests/WinAdmin.Tests/Fakes/FakeNtfs.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/NtfsAccessTests.cs`

**Interfaces:**
- Consumes: `IAdStructureStore.GetWriteCredentialAsync`, `AdCredentials` (3a)
- Produces:
  - `record AclNeed(string Sid, string Label, string Rights)` (Rights — имя `FileSystemRights`, «Modify»)
  - `record FolderAclState(bool Exists, IReadOnlyList<string> Missing, IReadOnlyList<string> Warnings)` (`Ok => Exists && Missing.Count == 0`)
  - `INtfsAccess`: `DirectoryExistsAsync(string unc, ct) → bool`; `CreateDirectoryAsync(string unc, ct)`; `InspectAsync(string unc, IReadOnlyList<AclNeed> needs, ct) → FolderAclState`; `GrantAsync(string unc, IReadOnlyList<AclNeed> needs, ct) → int` (сколько правил добавлено); `HasExplicitChangePermissionsAsync(string unc, IReadOnlyCollection<string> sids, ct) → bool`
  - `NtfsAccess(IAdStructureStore structure, IDirectorySettingsStore directory) : INtfsAccess`; статические `NtfsAccess.Inspect(DirectorySecurity, IReadOnlyList<AclNeed>) → (Missing, Warnings)`, `NtfsAccess.ParseRights(string) → FileSystemRights`
  - `Impersonation.Run<T>(AdWriteCredential c, string? domain, Func<T> action) → T` (`ProcessAccount` — без имперсонации)
  - `FakeNtfs : INtfsAccess` (тесты): `Directories`, `Rules` (unc → набор SID), `Calls`, `FailGrant`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/NtfsAccessTests.cs
using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;

namespace WinAdmin.Tests;

public sealed class NtfsAccessTests : IDisposable
{
    private const string Users = "S-1-5-32-545";          // BUILTIN\Users
    private const string Backup = "S-1-5-32-551";         // BUILTIN\Backup Operators
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-ntfs-" + Guid.NewGuid().ToString("N"));
    private readonly NtfsAccess _ntfs;

    public NtfsAccessTests()
    {
        var st = Mock.Of<IAdStructureStore>(m => m.GetWriteCredentialAsync(It.IsAny<CancellationToken>()) ==
            Task.FromResult(new AdWriteCredential(AdWriteMode.ProcessAccount, null, null)));
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) ==
            Task.FromResult(new DirectorySettings(true, "test.local", null, null, false)));
        _ntfs = new NtfsAccess(st, dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static AclNeed[] Needs => [new(Backup, "Full", "Modify"), new(Users, "Read", "ReadAndExecute")];

    [Fact]
    public async Task Create_inspect_grant_and_idempotent_regrant()
    {
        Assert.False(await _ntfs.DirectoryExistsAsync(_dir));
        await _ntfs.CreateDirectoryAsync(_dir);
        Assert.True(await _ntfs.DirectoryExistsAsync(_dir));

        var before = await _ntfs.InspectAsync(_dir, [new(Backup, "Full", "Modify")]);
        Assert.Contains(before.Missing, m => m.Contains("Full"));

        Assert.True(await _ntfs.GrantAsync(_dir, Needs) >= 1);
        var after = await _ntfs.InspectAsync(_dir, Needs);
        Assert.True(after.Ok, string.Join("; ", after.Missing));
        Assert.Equal(0, await _ntfs.GrantAsync(_dir, Needs));

        var rule = new DirectoryInfo(_dir).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Single(r => r.IdentityReference.Value == Backup && !r.IsInherited);
        Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
    }

    [Fact]
    public async Task Grant_keeps_other_rules()
    {
        await _ntfs.CreateDirectoryAsync(_dir);
        int before = new DirectoryInfo(_dir).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Count;
        await _ntfs.GrantAsync(_dir, Needs);
        int after = new DirectoryInfo(_dir).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Count;
        Assert.True(after >= before + 1);
    }

    [Fact]
    public async Task Missing_directory_is_reported()
        => Assert.False((await _ntfs.InspectAsync(_dir, Needs)).Exists);

    [Fact]
    public void Read_group_with_write_rights_is_a_warning()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Users), FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var (missing, warnings) = NtfsAccess.Inspect(security, [new(Users, "Read", "ReadAndExecute")]);
        Assert.Empty(missing);
        Assert.Contains(warnings, w => w.Contains("Read"));
    }

    [Theory]
    [InlineData("Modify", FileSystemRights.Modify)]
    [InlineData("readandexecute", FileSystemRights.ReadAndExecute)]
    public void Rights_names(string name, FileSystemRights expected) => Assert.Equal(expected, NtfsAccess.ParseRights(name));

    [Fact]
    public void Unknown_rights_name_is_rejected() => Assert.Throws<ArgumentException>(() => NtfsAccess.ParseRights("Всё"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~NtfsAccessTests"`
Expected: FAIL — компиляция: нет `NtfsAccess`, `AclNeed`.

- [ ] **Step 3: Write minimal implementation**

`IAdServices.cs` — добавить:

```csharp
/// <summary>Нужное правило NTFS: SID группы, подпись (Full/Read), права (имя FileSystemRights).</summary>
public sealed record AclNeed(string Sid, string Label, string Rights);

public sealed record FolderAclState(bool Exists, IReadOnlyList<string> Missing, IReadOnlyList<string> Warnings)
{
    public bool Ok => Exists && Missing.Count == 0;
}

/// <summary>Папки файлового сервера: под учёткой записи (служебная — имперсонация, учётка службы — как есть).</summary>
public interface INtfsAccess
{
    Task<bool> DirectoryExistsAsync(string unc, CancellationToken ct = default);
    Task CreateDirectoryAsync(string unc, CancellationToken ct = default);
    Task<FolderAclState> InspectAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default);
    /// <summary>Добавить недостающие Allow-правила; возвращает число добавленных.</summary>
    Task<int> GrantAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default);
    /// <summary>Есть ли явное Allow «Изменение разрешений» (или полный доступ) для одного из SID.</summary>
    Task<bool> HasExplicitChangePermissionsAsync(string unc, IReadOnlyCollection<string> sids, CancellationToken ct = default);
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/Impersonation.cs
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Доступ к шаре под служебной учёткой: LogonUser(NEW_CREDENTIALS) — сетевые обращения идут от её имени.</summary>
public static class Impersonation
{
    private const int Logon32LogonNewCredentials = 9;
    private const int Logon32ProviderWinnt50 = 3;

    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain, string password, int logonType, int provider, out SafeAccessTokenHandle token);

    public static T Run<T>(AdWriteCredential credential, string? domain, Func<T> action)
    {
        if (credential.Mode == AdWriteMode.ProcessAccount) return action();
        var network = AdCredentials.ToNetwork(credential, domain)!;
        if (!LogonUser(network.UserName, string.IsNullOrEmpty(network.Domain) ? null : network.Domain, network.Password,
                Logon32LogonNewCredentials, Logon32ProviderWinnt50, out var token))
            throw new UnauthorizedAccessException("Не удалось войти служебной учёткой для доступа к файловому серверу: "
                                                  + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        using (token)
            return WindowsIdentity.RunImpersonated(token, action);
    }
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/NtfsAccess.cs
using System.Security.AccessControl;
using System.Security.Principal;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>ACL сетевых папок: Allow по SID с наследованием на подпапки и файлы; чужие правила не трогаются.</summary>
public sealed class NtfsAccess(IAdStructureStore structure, IDirectorySettingsStore directory) : INtfsAccess
{
    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    private const FileSystemRights WriteBits = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
                                               | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public Task<bool> DirectoryExistsAsync(string unc, CancellationToken ct = default) => RunAsync(() => Directory.Exists(unc), ct);

    public Task CreateDirectoryAsync(string unc, CancellationToken ct = default) => RunAsync(() => Directory.CreateDirectory(unc), ct);

    public Task<FolderAclState> InspectAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
        => RunAsync(() =>
        {
            var info = new DirectoryInfo(unc);
            if (!info.Exists) return new FolderAclState(false, ["папка не найдена"], []);
            var (missing, warnings) = Inspect(info.GetAccessControl(), needs);
            return new FolderAclState(true, missing, warnings);
        }, ct);

    public Task<int> GrantAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
        => RunAsync(() =>
        {
            var info = new DirectoryInfo(unc);
            var security = info.GetAccessControl();
            int added = 0;
            foreach (var need in needs)
            {
                if (Covered(security, need)) continue;
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(need.Sid), ParseRights(need.Rights),
                    Inherit, PropagationFlags.None, AccessControlType.Allow));
                added++;
            }
            if (added > 0) info.SetAccessControl(security);
            return added;
        }, ct);

    public Task<bool> HasExplicitChangePermissionsAsync(string unc, IReadOnlyCollection<string> sids, CancellationToken ct = default)
        => RunAsync(() =>
        {
            var set = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
            return new DirectoryInfo(unc).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Any(r => r.AccessControlType == AccessControlType.Allow && set.Contains(r.IdentityReference.Value)
                          && (r.FileSystemRights & FileSystemRights.ChangePermissions) != 0);
        }, ct);

    public static (IReadOnlyList<string> Missing, IReadOnlyList<string> Warnings) Inspect(DirectorySecurity security, IReadOnlyList<AclNeed> needs)
    {
        var missing = new List<string>();
        var warnings = new List<string>();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        foreach (var need in needs)
        {
            if (!Covered(security, need)) missing.Add($"{need.Label}: нет права «{need.Rights}» для группы {need.Sid}");
            var required = ParseRights(need.Rights);
            var allowed = rules.Where(r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Value == need.Sid)
                .Aggregate((FileSystemRights)0, (acc, r) => acc | r.FileSystemRights);
            if ((required & WriteBits) == 0 && (allowed & WriteBits) != 0)
                warnings.Add($"{need.Label}: у группы есть права на запись — проверьте, должна ли она только читать");
        }
        return (missing, warnings);
    }

    public static FileSystemRights ParseRights(string name)
        => Enum.TryParse<FileSystemRights>(name, true, out var rights) && Enum.IsDefined(rights)
            ? rights
            : throw new ArgumentException($"Неизвестные права NTFS «{name}» (Modify, ReadAndExecute, FullControl…).");

    private static bool Covered(DirectorySecurity security, AclNeed need)
    {
        var required = ParseRights(need.Rights);
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(r => r.AccessControlType == AccessControlType.Allow
                      && r.IdentityReference.Value == need.Sid
                      && (r.FileSystemRights & required) == required
                      && (r.InheritanceFlags & Inherit) == Inherit
                      && (r.PropagationFlags & PropagationFlags.InheritOnly) == 0);
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken ct)
    {
        var credential = await structure.GetWriteCredentialAsync(ct);
        string? domain = (await directory.GetAsync(ct)).Domain;
        return await Task.Run(() => Impersonation.Run(credential, domain, action), ct);
    }

    private async Task RunAsync(Action action, CancellationToken ct)
        => await RunAsync(() => { action(); return true; }, ct);
}
```

Для `Inspect` с `DirectorySecurity`, собранной в памяти (тест), `GetAccessRules(true, true, …)` работает без файла.

```csharp
// src/tests/WinAdmin.Tests/Fakes/FakeNtfs.cs
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Tests.Fakes;

public sealed class FakeNtfs : INtfsAccess
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChangePermissionRoots { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Calls { get; } = [];
    public Exception? FailGrant { get; set; }

    public Task<bool> DirectoryExistsAsync(string unc, CancellationToken ct = default) => Task.FromResult(Directories.Contains(unc));

    public Task CreateDirectoryAsync(string unc, CancellationToken ct = default)
    {
        Calls.Add("Create:" + unc);
        Directories.Add(unc);
        return Task.CompletedTask;
    }

    public Task<FolderAclState> InspectAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
    {
        if (!Directories.Contains(unc)) return Task.FromResult(new FolderAclState(false, ["папка не найдена"], []));
        var have = Rules.GetValueOrDefault(unc) ?? [];
        return Task.FromResult(new FolderAclState(true,
            needs.Where(n => !have.Contains(n.Sid)).Select(n => $"{n.Label}: нет права «{n.Rights}»").ToList(), []));
    }

    public Task<int> GrantAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
    {
        Calls.Add("Grant:" + unc);
        if (FailGrant is not null) throw FailGrant;
        if (!Rules.TryGetValue(unc, out var have)) Rules[unc] = have = new(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(needs.Count(n => have.Add(n.Sid)));
    }

    public Task<bool> HasExplicitChangePermissionsAsync(string unc, IReadOnlyCollection<string> sids, CancellationToken ct = default)
        => Task.FromResult(ChangePermissionRoots.Contains(unc));
}
```

`DependencyInjection`:

```csharp
        services.AddSingleton<INtfsAccess, NtfsAccess>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~NtfsAccessTests"`
Expected: PASS (тесты работают с временной папкой и встроенными SID; права администратора не нужны — владелец папки может менять её ACL).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(ad-folders): NTFS ACL inspect/grant by SID with inheritance and service-account impersonation"
```

---

### Task 4: AdFoldersService — каталог, доступы пользователя, членство

**Files:**
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` (+ `IAdFoldersService`, `MembershipRequest`)
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/FolderCatalogCache.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/AdFoldersService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/AdFoldersServiceTests.cs`

**Interfaces:**
- Consumes: Task 1–3; `IAdReader.ListProjectsAsync`, `IAdWriter.AddMemberAsync/RemoveMemberAsync`, `IAdStructureStore`, `IModuleRegistry.GetSettingsAsync<AdFoldersSettings>`, `IAdUserDirectory.FindUserAsync`, `AdGuard`, `ScenarioRunner`, `IAuditService`
- Produces:
  - `record MembershipRequest(string GroupDn, string Member, bool Add, bool RemoveFromOther = true)`
  - `IAdFoldersService`: `ListAsync(IAccessContext actor, string? projectDn, string? q, ct) → FolderCatalogResult`; `UserAccessAsync(actor, string sam, ct) → IReadOnlyList<UserFolderAccess>`; `ChangeMembershipAsync(actor, MembershipRequest request, ct) → IReadOnlyList<ScenarioStep>`
  - `FolderCatalogCache(TimeProvider? time = null)`: `Task<FolderCatalogResult> GetAsync(Func<Task<FolderCatalogResult>> load)`, `Invalidate()` (singleton, 60 с)
  - `AdFoldersService(IAdFolderDirectory folders, IAdUserDirectory users, IAdWriter writer, IAdReader reader, INtfsAccess ntfs, IAdStructureStore structure, IModuleRegistry modules, FolderCatalogCache cache, IAuditService audit, ILogger<AdFoldersService> logger)` (scoped, `partial`)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdFoldersServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public abstract class AdFoldersTestBase
{
    protected const string Root = "OU=Accounts,DC=test,DC=local";
    protected const string A = "OU=A," + Root;
    protected const string B = "OU=B," + Root;
    protected static readonly PermissionCatalog Catalog = new(BuiltInModules.All);

    protected readonly FakeAdDomain Ad = new();
    protected readonly FakeAdReader Reader = new();
    protected readonly FakeNtfs Ntfs = new();
    protected readonly List<AuditEntryDto> Audited = [];
    protected AdFoldersSettings Settings = new() { DriveMappings = [@"A=\\fs01\Projects"] };
    protected readonly AdFoldersService Service;
    protected readonly FakeAdDomain.Group DocsFull, DocsRead;

    protected AdFoldersTestBase()
    {
        Reader.Projects.AddRange([new AdProject(A, "A"), new AdProject(B, "B")]);
        Reader.ExistingDns.UnionWith([Root, A, B]);
        DocsFull = Ad.AddGroup("sg_a_docs_full", A);
        DocsFull.Description = @"A:\A\Docs;Full Access";
        DocsRead = Ad.AddGroup("sg_a_docs_read", A);
        DocsRead.Description = @"A:\A\Docs;Read Only";
        Ad.AddGroup("sg_b_x_full", B).Description = @"A:\B\X;Full Access";
        Ad.AddGroup("sg_it_full", "OU=IT," + Root).Description = @"A:\IT;Full Access";
        Ad.AddGroup("sg_outside_full", "OU=Other,DC=test,DC=local").Description = @"A:\Out;Full Access";
        Ad.AddUser("ivan", "OU=Users," + A, DocsRead.Dn);

        var structure = Mock.Of<IAdStructureStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) ==
            Task.FromResult(AdStructureSettings.Default with { RootOu = Root, HiddenOus = ["IT"] }));
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Settings);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEntryDto>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntryDto, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Service = new AdFoldersService(Ad, Ad, Ad, Reader, Ntfs, structure, modules.Object, new FolderCatalogCache(),
            audit.Object, NullLogger<AdFoldersService>.Instance);
    }

    protected static IAccessContext Actor(params (string Permission, string[]? Projects)[] grants)
        => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "op", []), "op",
            PermissionEvaluator.Evaluate([new RoleSnapshot("r", false,
                grants.Select(g => new RoleGrant(g.Permission, g.Projects is null ? null : new ScopeDefinition(g.Projects))).ToList())], Catalog));

    protected static IAccessContext All(params string[] permissions) => Actor(permissions.Select(p => (p, (string[]?)null)).ToArray());
}

public sealed class AdFoldersServiceTests : AdFoldersTestBase
{
    [Fact]
    public async Task Catalog_shows_managed_projects_only()
    {
        var result = await Service.ListAsync(All(PermissionIds.AdFoldersRead), null, null, default);
        Assert.Equal([@"A:\A\Docs", @"A:\B\X"], result.Folders.Select(f => f.Path));
        Assert.Equal("ivan", Assert.Single(result.Folders[0].Read!.Members).Sam);
    }

    [Fact]
    public async Task Catalog_is_limited_to_scope_and_searchable()
    {
        var delegateB = Actor((PermissionIds.AdFoldersRead, [B]));
        Assert.Equal([@"A:\B\X"], (await Service.ListAsync(delegateB, null, null, default)).Folders.Select(f => f.Path));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ListAsync(delegateB, A, null, default));
        Assert.Single((await Service.ListAsync(All(PermissionIds.AdFoldersRead), null, "docs", default)).Folders);
    }

    [Fact]
    public async Task User_access_lists_folders()
    {
        var access = await Service.UserAccessAsync(All(PermissionIds.AdFoldersRead), "ivan", default);
        var a = Assert.Single(access);
        Assert.True(a.HasRead);
        Assert.False(a.HasFull);
    }

    [Fact]
    public async Task Adding_to_full_removes_from_read_of_same_folder()
    {
        var steps = await Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsFull.Dn, "ivan", Add: true), default);
        var ivan = Ad.U("ivan").Dn;
        Assert.Contains(ivan, DocsFull.Members);
        Assert.DoesNotContain(ivan, DocsRead.Members);
        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.Equal(StepStatus.Ok, s.Status));
        Assert.Equal(["group.member.add", "group.member.remove"], Audited.Select(a => a.Action));
    }

    [Fact]
    public async Task Add_without_remove_from_other_and_already_member_is_skipped()
    {
        var steps = await Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsRead.Dn, "ivan", Add: true, RemoveFromOther: false), default);
        Assert.Equal(StepStatus.Skipped, Assert.Single(steps).Status);
    }

    [Fact]
    public async Task Membership_outside_zone_or_scope_is_denied_without_write()
    {
        var actor = All(PermissionIds.AdFoldersMembership);
        foreach (var name in new[] { "sg_it_full", "sg_outside_full" })
            await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ChangeMembershipAsync(actor,
                new MembershipRequest(Ad.Groups.Single(g => g.Name == name).Dn, "ivan", true), default));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ChangeMembershipAsync(Actor((PermissionIds.AdFoldersMembership, [B])),
            new MembershipRequest(DocsFull.Dn, "ivan", true), default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Non_prefixed_or_distribution_group_is_rejected()
    {
        var other = Ad.AddGroup("Mail All", A);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(other.Dn, "ivan", true), default));
        DocsFull.IsSecurity = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsFull.Dn, "ivan", true), default));
    }

    [Fact]
    public async Task Unknown_member_is_404()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsFull.Dn, "nobody", true), default));

    [Fact]
    public async Task Catalog_cache_is_invalidated_by_membership_change()
    {
        var actor = All(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersMembership);
        await Service.ListAsync(actor, null, null, default);
        await Service.ChangeMembershipAsync(actor, new MembershipRequest(DocsFull.Dn, "ivan", true), default);
        var docs = (await Service.ListAsync(actor, null, null, default)).Folders.Single(f => f.Path == @"A:\A\Docs");
        Assert.Equal("ivan", Assert.Single(docs.Full!.Members).Sam);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersServiceTests"`
Expected: FAIL — компиляция: нет `AdFoldersService`, `FolderCatalogCache`, `MembershipRequest`.

- [ ] **Step 3: Write minimal implementation**

`IAdServices.cs` — добавить:

```csharp
public sealed record MembershipRequest(string GroupDn, string Member, bool Add, bool RemoveFromOther = true);

/// <summary>Модуль «Папки»: проверки зоны/области/префикса и аудит — внутри.</summary>
public interface IAdFoldersService
{
    Task<FolderCatalogResult> ListAsync(IAccessContext actor, string? projectDn, string? q, CancellationToken ct = default);
    Task<IReadOnlyList<UserFolderAccess>> UserAccessAsync(IAccessContext actor, string sam, CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioStep>> ChangeMembershipAsync(IAccessContext actor, MembershipRequest request, CancellationToken ct = default);
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/FolderCatalogCache.cs
using WinAdmin.Core.ActiveDirectory.Folders;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Каталог папок на 60 с (чтение всех групп и участников — дорогое); запись сбрасывает.</summary>
public sealed class FolderCatalogCache(TimeProvider? time = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, FolderCatalogResult Value)? _entry;

    public async Task<FolderCatalogResult> GetAsync(Func<Task<FolderCatalogResult>> load)
    {
        if (_entry is { } e && _time.GetUtcNow() - e.At < Lifetime) return e.Value;
        await _gate.WaitAsync();
        try
        {
            if (_entry is { } again && _time.GetUtcNow() - again.At < Lifetime) return again.Value;
            var value = await load();
            _entry = (_time.GetUtcNow(), value);
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _entry = null;
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/AdFoldersService.cs
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Models;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Папки: каталог из групп с префиксом в управляемых проектах, участники Full/Read, мастер, NTFS.</summary>
public sealed partial class AdFoldersService(
    IAdFolderDirectory folders, IAdUserDirectory users, IAdWriter writer, IAdReader reader, INtfsAccess ntfs,
    IAdStructureStore structure, IModuleRegistry modules, FolderCatalogCache cache, IAuditService audit,
    ILogger<AdFoldersService> logger) : IAdFoldersService
{
    public async Task<FolderCatalogResult> ListAsync(IAccessContext actor, string? projectDn, string? q, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var catalog = await CatalogAsync(st, settings, ct);
        var projects = AdGuard.InScopeProjects(actor, PermissionIds.AdFoldersRead, await reader.ListProjectsAsync(false, ct));
        if (projectDn is not null)
        {
            var only = projects.FirstOrDefault(p => SameDn(p.Dn, projectDn))
                       ?? throw new AccessDeniedException("Проект вне вашей области.", [PermissionIds.AdFoldersRead]);
            projects = [only];
        }
        bool Visible(string? dn) => dn is not null && projects.Any(p => SameDn(p.Dn, dn));
        var visible = catalog.Folders.Where(f => Visible(f.ProjectDn)).ToList();
        return new FolderCatalogResult(FolderCatalog.Search(visible, q),
            catalog.Unparsed.Where(u => projects.Any(p => DnUtils.IsUnderOrSame(u.Dn, p.Dn))).ToList());
    }

    public async Task<IReadOnlyList<UserFolderAccess>> UserAccessAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var user = await users.FindUserAsync(sam, [], ct) ?? throw new KeyNotFoundException($"Пользователь «{sam}» не найден в AD.");
        var visible = (await ListAsync(actor, null, null, ct)).Folders;
        return FolderCatalog.UserAccess(visible, user.Dn);
    }

    public async Task<IReadOnlyList<ScenarioStep>> ChangeMembershipAsync(IAccessContext actor, MembershipRequest request, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var group = await GuardGroupAsync(actor, request.GroupDn, PermissionIds.AdFoldersMembership, st, settings, ct);
        var member = await folders.FindMemberAsync(request.Member, ct)
                     ?? throw new KeyNotFoundException($"«{request.Member}» не найден в AD.");

        var catalog = await CatalogAsync(st, settings, ct);
        var folder = catalog.Folders.FirstOrDefault(f => f.Full?.Dn == group.Dn || f.Read?.Dn == group.Dn
                                                        || f.Others.Any(o => o.Dn == group.Dn));
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Членство {Group}", group.Dn));

        await run.RunAsync($"{(request.Add ? "Добавление в" : "Удаление из")} «{group.Name}»", async () =>
        {
            bool changed = request.Add ? await writer.AddMemberAsync(group.Dn, member.Dn, ct) : await writer.RemoveMemberAsync(group.Dn, member.Dn, ct);
            await AuditMembershipAsync(actor, request.Add, group, member, folder, !changed, ct);
            return changed ? StepResult.Done() : StepResult.Skip(request.Add ? "уже участник" : "не был участником");
        });

        var opposite = folder is null ? null
            : SameDn(folder.Full?.Dn, group.Dn) ? folder.Read
            : SameDn(folder.Read?.Dn, group.Dn) ? folder.Full : null;
        if (request.Add && request.RemoveFromOther && opposite is not null
            && opposite.Members.Any(m => SameDn(m.Dn, member.Dn)))
        {
            await run.RunAsync($"Удаление из «{opposite.Name}»", async () =>
            {
                bool removed = await writer.RemoveMemberAsync(opposite.Dn, member.Dn, ct);
                var oppositeGroup = new AdFolderGroup(opposite.Dn, opposite.Name, opposite.Sid, null, null, true, []);
                await AuditMembershipAsync(actor, false, oppositeGroup, member, folder, !removed, ct);
                return removed ? StepResult.Done() : StepResult.Skip("не был участником");
            });
        }

        cache.Invalidate();
        return run.Steps;
    }

    // ── помощники ────────────────────────────────────────────────

    private async Task<(AdStructureSettings, AdFoldersSettings)> ConfigAsync(CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null) throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        return (st, await modules.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, ct));
    }

    private Task<FolderCatalogResult> CatalogAsync(AdStructureSettings st, AdFoldersSettings settings, CancellationToken ct)
        => cache.GetAsync(async () =>
        {
            var projects = await reader.ListProjectsAsync(false, ct);
            var groups = (await folders.ListGroupsAsync(st.RootOu!, settings.GroupPrefix, ct))
                .Where(g => DnUtils.ProjectDn(g.Dn, st.RootOu!) is { } p && projects.Any(x => SameDn(x.Dn, p)))
                .ToList();
            var members = await folders.ResolveMembersAsync(groups.SelectMany(g => g.MemberDns), ct);
            var drives = Mappings(settings).Keys.ToList();
            return FolderCatalog.Build(groups, members, st.RootOu!, drives);
        });

    /// <summary>Группа в зоне и области, с префиксом модуля и безопасности.</summary>
    private async Task<AdFolderGroup> GuardGroupAsync(IAccessContext actor, string groupDn, string permission,
        AdStructureSettings st, AdFoldersSettings settings, CancellationToken ct)
    {
        string project = AdGuard.EnsureManaged(groupDn, st);
        AdGuard.EnsureInScope(actor, permission, project);
        var group = await folders.GetGroupAsync(groupDn, ct) ?? throw new KeyNotFoundException("Группа не найдена в AD.");
        if (!group.Name.StartsWith(settings.GroupPrefix, StringComparison.OrdinalIgnoreCase))
            throw new AccessDeniedException($"Модуль «Папки» управляет только группами «{settings.GroupPrefix}*».", [permission]);
        if (!group.IsSecurity)
            throw new InvalidOperationException($"«{group.Name}» — не группа безопасности.");
        return group;
    }

    private static IReadOnlyDictionary<char, string> Mappings(AdFoldersSettings settings)
        => FolderNaming.ParseMappings(settings.DriveMappings);

    private Task AuditMembershipAsync(IAccessContext actor, bool add, AdFolderGroup group, AdMember member, Folder? folder,
        bool alreadyMember, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor.Actor,
            Action = add ? "group.member.add" : "group.member.remove",
            Target = group.Dn,
            Success = true,
            Details = $"участник: {member.Sam ?? member.Name}; группа: {group.Name}; папка: {folder?.Path ?? "—"}; проект: {folder?.ProjectName ?? "—"}"
                      + (alreadyMember ? (add ? "; уже был участником" : "; не был участником") : ""),
        }, ct);

    private static bool SameDn(string? a, string? b)
        => a is not null && b is not null && DnUtils.IsUnderOrSame(a, b) && DnUtils.IsUnderOrSame(b, a);
}
```

`UserAccessAsync` без проверки области пользователя: возвращает только папки, которые видит оператор (`ad-folders.read`), — это и есть его область; пользователь вне корневой OU всё равно найдётся, но увидит оператор только свои папки. (Ruling на ревью, если нужно ограничить.)

`DependencyInjection`:

```csharp
        services.AddSingleton<FolderCatalogCache>();
        services.AddScoped<IAdFoldersService, AdFoldersService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdFoldersServiceTests.cs
git commit -m "feat(ad-folders): catalog with scope and cache, user folder access, membership with full/read switch"
```

---

### Task 5: Мастер «Новая папка», проверка и исправление NTFS

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/AdFoldersService.Create.cs`
- Modify: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs`
- Test: `src/tests/WinAdmin.Tests/AdFoldersCreateTests.cs`

**Interfaces:**
- Consumes: Task 4; `FolderNaming`, `NtfsAccess.ParseRights`, `INtfsAccess`
- Produces (в `IAdFoldersService`):
  - `record CreateFolderRequest(string ProjectDn, string Path, string BaseName, string? OrgCode, bool CreateDirectory)`
  - `record FolderPreview(string FullGroup, string ReadGroup, string Unc, string GroupsOuDn)`
  - `PreviewAsync(IAccessContext actor, CreateFolderRequest request, ct) → FolderPreview`
  - `CreateFolderAsync(actor, CreateFolderRequest request, ct) → IReadOnlyList<ScenarioStep>`
  - `InspectAclAsync(actor, string path, ct) → FolderAclState`
  - `FixAclAsync(actor, string path, ct) → IReadOnlyList<ScenarioStep>`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdFoldersCreateTests.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class AdFoldersCreateTests : AdFoldersTestBase
{
    private const string Unc = @"\\fs01\Projects\A\New";
    private static CreateFolderRequest Request(string path = @"A:\A\New", string baseName = "new", bool create = true)
        => new(A, path, baseName, "a", create);

    [Fact]
    public async Task Preview_shows_names_and_unc()
    {
        var p = await Service.PreviewAsync(All(PermissionIds.AdFoldersCreate), new CreateFolderRequest(A, @"A:\A\New", "new", null, true), default);
        Assert.Equal("sg_a_new_full", p.FullGroup);   // orgCode «a» из существующих групп проекта
        Assert.Equal(Unc, p.Unc);
        Assert.Equal(A, p.GroupsOuDn);
    }

    [Fact]
    public async Task Wizard_creates_groups_directory_and_acl()
    {
        var steps = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        Assert.All(steps, s => Assert.Equal(StepStatus.Ok, s.Status));
        var full = Ad.Groups.Single(g => g.Name == "sg_a_new_full");
        Assert.Equal(@"A:\A\New;Full Access", full.Description);
        Assert.Equal(@"A:\A\New;Read Only", Ad.Groups.Single(g => g.Name == "sg_a_new_read").Description);
        Assert.Contains(Unc, Ntfs.Directories);
        Assert.Contains(full.Sid, Ntfs.Rules[Unc]);
        Assert.Equal("folder.create", Assert.Single(Audited).Action);
    }

    [Fact]
    public async Task Rerun_reuses_groups_and_skips_done_steps()
    {
        await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        var again = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        Assert.All(again, s => Assert.Equal(StepStatus.Skipped, s.Status));
        Assert.Single(Ad.Groups, g => g.Name == "sg_a_new_full");
    }

    [Fact]
    public async Task Existing_group_for_other_path_fails_wizard()
    {
        Ad.AddGroup("sg_a_new_full", A).Description = @"A:\A\Other;Full Access";
        var steps = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        Assert.Equal(StepStatus.Failed, steps[0].Status);
        Assert.Contains("другой папкой", steps[0].Message);
        Assert.DoesNotContain(Ntfs.Calls, c => c.StartsWith("Grant"));
    }

    [Theory]
    [InlineData(@"C:\Local\X", "new")]
    [InlineData(@"A:\A\..\Windows", "new")]
    [InlineData(@"A:\A\New", "новая")]
    public async Task Wizard_validates_before_any_write(string path, string baseName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(path, baseName), default));
        Assert.Empty(Ad.Calls);
        Assert.Empty(Ntfs.Calls);
    }

    [Fact]
    public async Task Missing_directory_without_create_flag_fails_before_acl()
    {
        var steps = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(create: false), default);
        Assert.Equal(StepStatus.Failed, steps[2].Status);
        Assert.DoesNotContain(Ntfs.Calls, c => c.StartsWith("Grant"));
    }

    [Fact]
    public async Task Wizard_requires_create_permission_in_project_scope()
        => await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service.CreateFolderAsync(Actor((PermissionIds.AdFoldersCreate, [B])), Request(), default));

    [Fact]
    public async Task Inspect_and_fix_existing_folder_acl()
    {
        Ntfs.Directories.Add(@"\\fs01\Projects\A\Docs");
        var actor = All(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersCreate);
        Assert.False((await Service.InspectAclAsync(actor, @"A:\A\Docs", default)).Ok);
        var steps = await Service.FixAclAsync(actor, @"A:\A\Docs", default);
        Assert.Equal(StepStatus.Ok, Assert.Single(steps).Status);
        Assert.True((await Service.InspectAclAsync(actor, @"A:\A\Docs", default)).Ok);
        Assert.Equal("folder.acl.fix", Audited.Last().Action);
    }

    [Fact]
    public async Task Fix_for_unknown_folder_is_404()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service.FixAclAsync(All(PermissionIds.AdFoldersCreate), @"A:\A\Nope", default));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersCreateTests"`
Expected: FAIL — компиляция: нет `CreateFolderRequest`, `CreateFolderAsync`, `PreviewAsync`, `InspectAclAsync`, `FixAclAsync`.

- [ ] **Step 3: Write minimal implementation**

`IAdServices.cs` — рядом с `MembershipRequest`:

```csharp
public sealed record CreateFolderRequest(string ProjectDn, string Path, string BaseName, string? OrgCode, bool CreateDirectory);

public sealed record FolderPreview(string FullGroup, string ReadGroup, string Unc, string GroupsOuDn);
```

в `IAdFoldersService`:

```csharp
    Task<FolderPreview> PreviewAsync(IAccessContext actor, CreateFolderRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioStep>> CreateFolderAsync(IAccessContext actor, CreateFolderRequest request, CancellationToken ct = default);
    Task<FolderAclState> InspectAclAsync(IAccessContext actor, string path, CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioStep>> FixAclAsync(IAccessContext actor, string path, CancellationToken ct = default);
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/Folders/AdFoldersService.Create.cs
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Models;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

public sealed partial class AdFoldersService
{
    public async Task<FolderPreview> PreviewAsync(IAccessContext actor, CreateFolderRequest request, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        return await PlanAsync(actor, request, st, settings, ct);
    }

    public async Task<IReadOnlyList<ScenarioStep>> CreateFolderAsync(IAccessContext actor, CreateFolderRequest request, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var plan = await PlanAsync(actor, request, st, settings, ct);
        string path = request.Path.Trim().TrimEnd('\\');
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Новая папка {Path}", path));
        string? fullSid = null, readSid = null;

        await run.RunAsync($"Группа Full «{plan.FullGroup}»", async () =>
        {
            var (sid, result) = await EnsureGroupAsync(plan.FullGroup, plan.GroupsOuDn, $"{path};Full Access", path, ct);
            fullSid = sid;
            return result;
        });
        await run.RunAsync($"Группа Read «{plan.ReadGroup}»", async () =>
        {
            var (sid, result) = await EnsureGroupAsync(plan.ReadGroup, plan.GroupsOuDn, $"{path};Read Only", path, ct);
            readSid = sid;
            return result;
        });
        await run.RunAsync($"Папка {plan.Unc}", async () =>
        {
            if (await ntfs.DirectoryExistsAsync(plan.Unc, ct)) return StepResult.Skip("уже есть");
            if (!request.CreateDirectory) throw new InvalidOperationException("Папки нет на файловом сервере — включите «создать папку».");
            await ntfs.CreateDirectoryAsync(plan.Unc, ct);
            return StepResult.Done();
        });
        await run.RunAsync("Права NTFS", async () =>
        {
            int added = await ntfs.GrantAsync(plan.Unc, Needs(fullSid!, readSid!, settings), ct);
            return added > 0 ? StepResult.Done($"добавлено правил: {added}") : StepResult.Skip("права уже настроены");
        });

        cache.Invalidate();
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor.Actor, Action = "folder.create", Target = plan.Unc, Success = !run.Failed,
            Details = $"{path}; группы {plan.FullGroup}, {plan.ReadGroup}; " + string.Join("; ", run.Steps.Select(s => $"{s.Name}: {s.Status}")),
        }, ct);
        return run.Steps;
    }

    public async Task<FolderAclState> InspectAclAsync(IAccessContext actor, string path, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var (folder, unc, needs) = await FolderForAclAsync(actor, path, PermissionIds.AdFoldersRead, st, settings, ct);
        return await ntfs.InspectAsync(unc, needs, ct);
    }

    public async Task<IReadOnlyList<ScenarioStep>> FixAclAsync(IAccessContext actor, string path, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var (folder, unc, needs) = await FolderForAclAsync(actor, path, PermissionIds.AdFoldersCreate, st, settings, ct);
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Права NTFS {Unc}", unc));
        await run.RunAsync("Права NTFS", async () =>
        {
            int added = await ntfs.GrantAsync(unc, needs, ct);
            return added > 0 ? StepResult.Done($"добавлено правил: {added}") : StepResult.Skip("права уже настроены");
        });
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor.Actor, Action = "folder.acl.fix", Target = unc, Success = !run.Failed,
            Details = $"{folder.Path}: {run.Steps[0].Status} {run.Steps[0].Message}",
        }, ct);
        return run.Steps;
    }

    /// <summary>Проверки и имена — до любых изменений.</summary>
    private async Task<FolderPreview> PlanAsync(IAccessContext actor, CreateFolderRequest request, AdStructureSettings st,
        AdFoldersSettings settings, CancellationToken ct)
    {
        string project = AdGuard.EnsureManaged(request.ProjectDn, st);
        AdGuard.EnsureInScope(actor, PermissionIds.AdFoldersCreate, project);
        string unc = FolderNaming.ToUnc(request.Path, Mappings(settings));
        string projectName = DnUtils.RelativeOuPath(project, st.RootOu!)[0];
        string? org = string.IsNullOrWhiteSpace(request.OrgCode) ? null : request.OrgCode.Trim();
        if (org is null)
        {
            var names = (await folders.ListGroupsAsync(project, settings.GroupPrefix, ct)).Select(g => g.Name);
            org = FolderNaming.DefaultOrgCode(projectName, names, settings.GroupPrefix)
                  ?? throw new ArgumentException("Укажите код организации латиницей (по имени проекта его не получить).");
        }
        var (full, read) = FolderNaming.GroupNames(settings.GroupPrefix, org, request.BaseName.Trim());
        string groupsOu = string.IsNullOrWhiteSpace(settings.GroupsOuName) ? project : $"OU={settings.GroupsOuName.Trim()},{project}";
        NtfsAccess.ParseRights(settings.FullRights);
        NtfsAccess.ParseRights(settings.ReadRights);
        return new FolderPreview(full, read, unc, groupsOu);
    }

    /// <summary>Группа есть (с тем же путём) — пропуск; есть с другим путём или вне OU — ошибка; нет — создать.</summary>
    private async Task<(string Sid, StepResult Result)> EnsureGroupAsync(string name, string ouDn, string description, string path, CancellationToken ct)
    {
        var existing = await folders.FindGroupByNameAsync(name, ct);
        if (existing is not null)
        {
            var parsed = FolderDescriptionParser.Parse(existing.Description, existing.Info, existing.Name);
            if (parsed is null || FolderDescriptionParser.NormalizeKey(parsed.Path) != FolderDescriptionParser.NormalizeKey(path))
                throw new InvalidOperationException($"Имя «{name}» занято другой папкой: {parsed?.Path ?? existing.Dn}.");
            return (existing.Sid ?? throw new InvalidOperationException($"У группы «{name}» нет SID."), StepResult.Skip("уже есть"));
        }
        string dn = await writer.CreateGroupAsync(ouDn, name, description, ct);
        var created = await folders.GetGroupAsync(dn, ct) ?? throw new InvalidOperationException($"Группа «{name}» создана, но не читается.");
        return (created.Sid!, StepResult.Done());
    }

    private async Task<(Folder Folder, string Unc, IReadOnlyList<AclNeed> Needs)> FolderForAclAsync(IAccessContext actor, string path,
        string permission, AdStructureSettings st, AdFoldersSettings settings, CancellationToken ct)
    {
        var catalog = await CatalogAsync(st, settings, ct);
        var folder = catalog.Folders.FirstOrDefault(f => FolderDescriptionParser.NormalizeKey(f.Path) == FolderDescriptionParser.NormalizeKey(path))
                     ?? throw new KeyNotFoundException($"Папка «{path}» не найдена среди групп доступа.");
        AdGuard.EnsureInScope(actor, permission, folder.ProjectDn ?? throw new AccessDeniedException("Папка вне проектов.", [permission]));
        if (folder.Full?.Sid is null || folder.Read?.Sid is null)
            throw new InvalidOperationException("У папки нет пары групп Full/Read — права NTFS не настроить.");
        string unc = FolderNaming.ToUnc(folder.Path, Mappings(settings));
        return (folder, unc, Needs(folder.Full.Sid, folder.Read.Sid, settings));
    }

    private static IReadOnlyList<AclNeed> Needs(string fullSid, string readSid, AdFoldersSettings settings)
        => [new AclNeed(fullSid, "Full", settings.FullRights), new AclNeed(readSid, "Read", settings.ReadRights)];
}
```

`Rerun_reuses_groups_and_skips_done_steps`: группы есть с тем же путём → `Skip`; папка есть → `Skip`; права уже есть (`FakeNtfs` — SID в наборе) → `Skip`.

`Missing_directory_without_create_flag_fails_before_acl`: шаг 3 бросает `InvalidOperationException` → `Failed`, шаг 4 «не выполнялся».

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersCreateTests|FullyQualifiedName~AdFoldersServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdFoldersCreateTests.cs
git commit -m "feat(ad-folders): new-folder wizard (groups, directory, NTFS) and ACL inspect/fix"
```

---

### Task 6: Проверки окружения модуля «Папки»

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/AdFoldersCheck.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/AdFoldersCheckTests.cs`

**Interfaces:**
- Consumes: `IAdReader`, `IAdFolderDirectory`, `IAdUserDirectory.GetWriterSidsAsync`, `INtfsAccess`, `IAdStructureStore`, `IModuleRegistry`, `FolderCatalog`, `FolderNaming`
- Produces: `AdFoldersCheck : IEnvironmentCheck` (`ModuleId = "ad-folders"`), коды `folders.groups`, `folders.mappings`, `ad.rights.groups`, `ad.rights.create`, `folders.share`, `folders.acl` (только `Full`)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdFoldersCheckTests.cs
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AdFoldersCheckTests
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private readonly FakeAdDomain _ad = new();
    private readonly FakeAdReader _reader = new();
    private readonly FakeNtfs _ntfs = new();
    private AdFoldersSettings _settings = new() { DriveMappings = [@"A=\\fs01\Projects"] };
    private readonly FakeAdDomain.Group _full, _read;

    public AdFoldersCheckTests()
    {
        _reader.Projects.Add(new AdProject(A, "A"));
        _reader.ExistingDns.UnionWith([Root, A]);
        _full = _ad.AddGroup("sg_a_docs_full", A);
        _full.Description = @"A:\Docs;Full Access";
        _read = _ad.AddGroup("sg_a_docs_read", A);
        _read.Description = @"A:\Docs;Read Only";
        _reader.Effective[_full.Dn] = new EffectiveRights(new HashSet<string> { "member" }, new HashSet<string>());
        _reader.Effective[A] = new EffectiveRights(new HashSet<string>(), new HashSet<string> { "group" });
        _ntfs.Directories.UnionWith([@"\\fs01\Projects", @"\\fs01\Projects\Docs"]);
        _ntfs.ChangePermissionRoots.Add(@"\\fs01\Projects");
        _ntfs.Rules[@"\\fs01\Projects\Docs"] = new(StringComparer.OrdinalIgnoreCase) { _full.Sid, _read.Sid };
    }

    private async Task<Dictionary<string, CheckResult>> RunAsync(CheckDepth depth = CheckDepth.Quick)
    {
        var st = Mock.Of<IAdStructureStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) ==
            Task.FromResult(AdStructureSettings.Default with { RootOu = Root }));
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        return (await new AdFoldersCheck(_reader, _ad, _ad, _ntfs, st, modules.Object).RunAsync(depth, default)).ToDictionary(r => r.Code);
    }

    [Fact]
    public async Task Healthy_quick_is_ok_and_acl_scan_only_in_full()
    {
        var quick = await RunAsync();
        Assert.All(quick.Values, r => Assert.Equal(CheckStatus.Ok, r.Status));
        Assert.False(quick.ContainsKey("folders.acl"));
        Assert.Equal(CheckStatus.Ok, (await RunAsync(CheckDepth.Full))["folders.acl"].Status);
    }

    [Fact]
    public async Task Unparsed_groups_and_missing_pairs_are_warnings()
    {
        _ad.AddGroup("sg_a_bad", A).Description = "без пути";
        _ad.AddGroup("sg_a_solo_full", A).Description = @"A:\Solo;Full";
        var r = (await RunAsync())["folders.groups"];
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("sg_a_bad", r.Message);
        Assert.Contains(@"A:\Solo", r.Message);
    }

    [Fact]
    public async Task Unmapped_drive_fails()
    {
        _ad.AddGroup("sg_a_z_full", A).Description = @"Z:\Z;Full";
        var r = (await RunAsync())["folders.mappings"];
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains("Z:", r.Message);
    }

    [Fact]
    public async Task Bad_mapping_setting_fails_with_fix()
    {
        _settings = new AdFoldersSettings { DriveMappings = ["A"] };
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["folders.mappings"].Status);
    }

    [Fact]
    public async Task Rights_on_groups_and_create()
    {
        _reader.Effective[_full.Dn] = new EffectiveRights(new HashSet<string>(), new HashSet<string>());
        _reader.Effective[A] = new EffectiveRights(new HashSet<string>(), new HashSet<string>());
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.rights.groups"].Status);
        Assert.Equal(CheckStatus.Failed, r["ad.rights.create"].Status);
    }

    [Fact]
    public async Task Share_without_explicit_change_permissions_is_a_warning_and_missing_share_fails()
    {
        _ntfs.ChangePermissionRoots.Clear();
        Assert.Equal(CheckStatus.Warning, (await RunAsync())["folders.share"].Status);
        _ntfs.Directories.Remove(@"\\fs01\Projects");
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["folders.share"].Status);
    }

    [Fact]
    public async Task Full_scan_lists_folders_with_wrong_acl()
    {
        _ntfs.Rules[@"\\fs01\Projects\Docs"].Remove(_read.Sid);
        var r = (await RunAsync(CheckDepth.Full))["folders.acl"];
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains(@"A:\Docs", r.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersCheckTests"`
Expected: FAIL — компиляция: нет `AdFoldersCheck`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/EnvironmentChecks/AdFoldersCheck.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Готовность модуля «Папки»: группы, сопоставление дисков, права на группы и создание, шара, ACL папок (полная).</summary>
public sealed class AdFoldersCheck(
    IAdReader reader, IAdFolderDirectory folders, IAdUserDirectory users, INtfsAccess ntfs,
    IAdStructureStore structure, IModuleRegistry modules) : IEnvironmentCheck
{
    private const int MaxFolders = 500;
    private const int MaxListed = 15;

    public string ModuleId => AdFoldersModule.ModuleId;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        string[] codes = ["folders.groups", "folders.mappings", "ad.rights.groups", "ad.rights.create", "folders.share"];
        if (st.RootOu is null)
            return codes.Select(c => CheckResult.Skip(c, Title(c), "Корневая OU не задана (см. «Платформа»)")).ToList();

        var settings = await modules.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, ct);
        var results = new List<CheckResult>();
        var projects = await reader.ListProjectsAsync(false, ct);
        var groups = (await folders.ListGroupsAsync(st.RootOu, settings.GroupPrefix, ct))
            .Where(g => DnUtils.ProjectDn(g.Dn, st.RootOu) is { } p && projects.Any(x => DnUtils.IsUnderOrSame(p, x.Dn)))
            .ToList();

        IReadOnlyDictionary<char, string>? map = null;
        string? mapError = null;
        try { map = FolderNaming.ParseMappings(settings.DriveMappings); }
        catch (ArgumentException ex) { mapError = ex.Message; }

        var catalog = FolderCatalog.Build(groups, new Dictionary<string, AdMember>(), st.RootOu, map?.Keys.ToList() ?? []);

        // 1. Группы.
        var noPair = catalog.Folders.Where(f => f.Full is null || f.Read is null).Select(f => f.Path).ToList();
        var dupes = catalog.Folders.Where(f => f.Warnings.Any(w => w.StartsWith("две группы"))).Select(f => f.Path).ToList();
        string summary = $"Групп: {groups.Count}, папок: {catalog.Folders.Count}";
        var problems = new List<string>();
        if (catalog.Unparsed.Count > 0) problems.Add("без пути в описании: " + List(catalog.Unparsed.Select(u => u.Name)));
        if (noPair.Count > 0) problems.Add("без пары Full/Read: " + List(noPair));
        if (dupes.Count > 0) problems.Add("дубли групп: " + List(dupes));
        results.Add(problems.Count == 0
            ? CheckResult.Ok("folders.groups", Title("folders.groups"), summary)
            : CheckResult.Warn("folders.groups", Title("folders.groups"), summary + "; " + string.Join("; ", problems),
                $"Исправьте описание групп: «путь;Full Access» / «путь;Read Only»"));

        // 2. Сопоставление дисков.
        var drives = catalog.Folders.Where(f => f.Path.Length > 1 && f.Path[1] == ':').Select(f => char.ToUpperInvariant(f.Path[0])).Distinct().ToList();
        if (mapError is not null)
            results.Add(CheckResult.Fail("folders.mappings", Title("folders.mappings"), mapError, @"Модули → «Папки» → «Буквы дисков → UNC»: строки вида A=\\fs01\Projects"));
        else
        {
            var unmapped = drives.Where(d => !map!.ContainsKey(d)).ToList();
            results.Add(unmapped.Count == 0
                ? CheckResult.Ok("folders.mappings", Title("folders.mappings"), drives.Count == 0 ? "Букв дисков в описаниях нет" : "Сопоставлены: " + string.Join(", ", drives.Select(d => $"{d}:")))
                : CheckResult.Fail("folders.mappings", Title("folders.mappings"), "Не сопоставлены: " + string.Join(", ", unmapped.Select(d => $"{d}:")),
                    @"Модули → «Папки» → «Буквы дисков → UNC»: добавьте строки вида A=\\fs01\Projects"));
        }

        // 3–4. Права учётки записи на группы и создание групп.
        var writer = await reader.GetWriterStatusAsync(ct);
        if (!writer.Bound)
        {
            results.Add(CheckResult.Skip("ad.rights.groups", Title("ad.rights.groups"), "Учётка записи не вошла (см. «Платформа»)"));
            results.Add(CheckResult.Skip("ad.rights.create", Title("ad.rights.create"), "Учётка записи не вошла (см. «Платформа»)"));
        }
        else
        {
            var noMember = new List<string>();
            foreach (var p in projects)
            {
                var sample = groups.FirstOrDefault(g => DnUtils.IsUnderOrSame(g.Dn, p.Dn));
                if (sample is not null && !(await reader.ReadEffectiveAsync(sample.Dn, ct)).Attributes.Contains("member")) noMember.Add(p.Name);
            }
            results.Add(noMember.Count == 0
                ? CheckResult.Ok("ad.rights.groups", Title("ad.rights.groups"), "Изменение участников групп разрешено")
                : CheckResult.Fail("ad.rights.groups", Title("ad.rights.groups"), "Нет права менять участников групп в проектах: " + List(noMember),
                    $"Делегируйте {writer.Account} «Write members» на группы {settings.GroupPrefix}* в этих проектах"));

            var noCreate = new List<string>();
            foreach (var p in projects)
            {
                string ou = string.IsNullOrWhiteSpace(settings.GroupsOuName) ? p.Dn : $"OU={settings.GroupsOuName.Trim()},{p.Dn}";
                if (!await reader.ExistsAsync(ou, ct) || !(await reader.ReadEffectiveAsync(ou, ct)).ChildClasses.Contains("group")) noCreate.Add(p.Name);
            }
            results.Add(noCreate.Count == 0
                ? CheckResult.Ok("ad.rights.create", Title("ad.rights.create"), "Создание групп разрешено")
                : CheckResult.Fail("ad.rights.create", Title("ad.rights.create"), "Нельзя создавать группы в проектах: " + List(noCreate),
                    $"Делегируйте {writer.Account} «Create Group objects» на OU групп этих проектов"));
        }

        // 5. Шары.
        if (map is null || map.Count == 0)
            results.Add(CheckResult.Skip("folders.share", Title("folders.share"), "Сопоставления дисков не заданы"));
        else
        {
            var sids = writer.Bound ? await users.GetWriterSidsAsync(ct) : [];
            var missing = new List<string>();
            var unconfirmed = new List<string>();
            foreach (var unc in map.Values)
            {
                try
                {
                    if (!await ntfs.DirectoryExistsAsync(unc, ct)) { missing.Add(unc); continue; }
                    if (!await ntfs.HasExplicitChangePermissionsAsync(unc, sids, ct)) unconfirmed.Add(unc);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    missing.Add($"{unc} ({ex.Message})");
                }
            }
            results.Add(missing.Count > 0
                ? CheckResult.Fail("folders.share", Title("folders.share"), "Недоступны: " + string.Join("; ", missing),
                    "Проверьте путь, доступ по SMB (445) и права учётки записи на шару")
                : unconfirmed.Count > 0
                    ? CheckResult.Warn("folders.share", Title("folders.share"), "Не подтверждено явное право «Изменение разрешений»: " + string.Join("; ", unconfirmed),
                        $"Дайте {writer.Account} право «Изменение разрешений» (или «Полный доступ») на корне шары; права через локальные группы сервера здесь не видны")
                    : CheckResult.Ok("folders.share", Title("folders.share"), "Шары доступны: " + string.Join("; ", map.Values)));
        }

        // 6. ACL папок — только полная проверка.
        if (depth == CheckDepth.Full)
            results.Add(await AclScanAsync(catalog, map, settings, ct));
        return results;
    }

    private async Task<CheckResult> AclScanAsync(FolderCatalogResult catalog, IReadOnlyDictionary<char, string>? map, AdFoldersSettings settings, CancellationToken ct)
    {
        if (map is null) return CheckResult.Skip("folders.acl", Title("folders.acl"), "Сопоставления дисков не заданы");
        var bad = new List<string>();
        int checkedCount = 0;
        foreach (var f in catalog.Folders.Where(f => f.Full?.Sid is not null && f.Read?.Sid is not null).Take(MaxFolders))
        {
            string unc;
            try { unc = FolderNaming.ToUnc(f.Path, map); }
            catch (ArgumentException) { continue; }
            checkedCount++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var state = await ntfs.InspectAsync(unc, [new AclNeed(f.Full!.Sid!, "Full", settings.FullRights), new AclNeed(f.Read!.Sid!, "Read", settings.ReadRights)], timeout.Token);
                if (!state.Ok) bad.Add($"{f.Path} ({string.Join(", ", state.Missing)})");
            }
            catch (Exception ex) when (ex is OperationCanceledException or UnauthorizedAccessException or IOException)
            {
                bad.Add($"{f.Path} (не прочитать: {ex.Message})");
            }
        }
        return bad.Count == 0
            ? CheckResult.Ok("folders.acl", Title("folders.acl"), $"Права в порядке у {checkedCount} папок")
            : CheckResult.Fail("folders.acl", Title("folders.acl"), $"Расхождения ({bad.Count} из {checkedCount}): " + List(bad),
                "Откройте папку в разделе «Папки» → «Исправить права NTFS»");
    }

    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return string.Join(", ", list.Take(MaxListed)) + (list.Count > MaxListed ? $" и ещё {list.Count - MaxListed}" : "");
    }

    private static string Title(string code) => code switch
    {
        "folders.groups" => "Группы доступа к папкам",
        "folders.mappings" => "Сопоставление дисков",
        "ad.rights.groups" => "Права на участников групп",
        "ad.rights.create" => "Права на создание групп",
        "folders.share" => "Файловый сервер",
        "folders.acl" => "Права NTFS на папках",
        _ => code,
    };
}
```

`DependencyInjection`:

```csharp
        services.AddSingleton<IEnvironmentCheck, AdFoldersCheck>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersCheckTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdFoldersCheckTests.cs
git commit -m "feat(ad-folders): environment checks (groups, mappings, rights, share, NTFS scan)"
```

---

### Task 7: API — папки и источник проектов

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/AdFoldersController.cs`
- Modify: `src/backend/WinAdmin.Api/Controllers/AdProjectsController.cs`
- Modify: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs` (фабрика: `FakeAdDomain` как `IAdFolderDirectory`, `FakeNtfs`)
- Test: `src/tests/WinAdmin.Tests/AdFoldersApiTests.cs`

**Interfaces:**
- Consumes: `IAdFoldersService` (Task 4–5), `AdErrorsAttribute` (3b), `WinAdminModuleAttribute`
- Produces:
  - `GET /api/v1/ad/folders?project=&q=` → `FolderCatalogResult`
  - `GET /api/v1/ad/folders/user/{sam}` → `UserFolderAccess[]`
  - `POST /api/v1/ad/folders/membership` `{ groupDn, member, add, removeFromOther }` → шаги
  - `POST /api/v1/ad/folders/preview` `CreateFolderRequest` → `FolderPreview`
  - `POST /api/v1/ad/folders` `CreateFolderRequest` → шаги
  - `GET /api/v1/ad/folders/acl?path=` → `FolderAclState`; `POST /api/v1/ad/folders/acl-fix` `{ path }` → шаги
  - `/ad/projects` — источник `(ad-folders, ad-folders.read)`
  - `NetworkApiFactory.Ntfs` (`FakeNtfs`)

- [ ] **Step 1: Write the failing test**

В `NetworkApiFactory`:

```csharp
    public FakeNtfs Ntfs { get; } = new();
```

в `ConfigureTestServices`:

```csharp
            services.RemoveAll<IAdFolderDirectory>();
            services.AddSingleton<IAdFolderDirectory>(AdDomain);
            services.RemoveAll<INtfsAccess>();
            services.AddSingleton<INtfsAccess>(Ntfs);
```

```csharp
// src/tests/WinAdmin.Tests/AdFoldersApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class AdFoldersApiTests : IAsyncLifetime
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly NetworkApiFactory _factory;
    private readonly string _base = "f" + Guid.NewGuid().ToString("N")[..6];

    public AdFoldersApiTests(NetworkApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdStructureStore>().SaveAsync(
            AdStructureSettings.Default with { RootOu = Root, WriteMode = AdWriteMode.ProcessAccount }, null);
        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
        await registry.SetEnabledAsync(AdFoldersModule.ModuleId, true, "test");
        await registry.SaveSettingsAsync(AdFoldersModule.ModuleId, new System.Text.Json.Nodes.JsonObject
        {
            ["driveMappings"] = new System.Text.Json.Nodes.JsonArray(@"A=\\fs01\Projects"),
        }, "test");
        if (!_factory.AdReader.Projects.Any(p => p.Dn == A)) _factory.AdReader.Projects.Add(new AdProject(A, "A"));
        _factory.AdReader.ExistingDns.UnionWith([Root, A]);
        _factory.AdDomain.AddGroup($"sg_a_{_base}_full", A).Description = $@"A:\A\{_base};Full Access";
        _factory.AdDomain.AddGroup($"sg_a_{_base}_read", A).Description = $@"A:\A\{_base};Read Only";
        _factory.AdDomain.AddUser("u" + _base, "OU=Users," + A);
        _factory.Services.GetRequiredService<FolderCatalogCache>().Invalidate();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_membership_and_user_access()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersMembership);
        var catalog = await client.GetFromJsonAsync<JsonElement>($"/api/v1/ad/folders?q={_base}", Web);
        var folder = catalog.GetProperty("folders").EnumerateArray().Single();
        string fullDn = folder.GetProperty("full").GetProperty("dn").GetString()!;

        var add = await client.PostAsJsonAsync("/api/v1/ad/folders/membership", new { groupDn = fullDn, member = "u" + _base, add = true, removeFromOther = true });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var access = await client.GetFromJsonAsync<List<JsonElement>>($"/api/v1/ad/folders/user/u{_base}", Web);
        Assert.True(Assert.Single(access!).GetProperty("hasFull").GetBoolean());
    }

    [Fact]
    public async Task Preview_create_and_acl()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersCreate);
        var request = new { projectDn = A, path = $@"A:\A\new{_base}", baseName = "n" + _base, orgCode = "a", createDirectory = true };
        var preview = await (await client.PostAsJsonAsync("/api/v1/ad/folders/preview", request)).Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.Equal($@"\\fs01\Projects\A\new{_base}", preview.GetProperty("unc").GetString());

        var steps = await (await client.PostAsJsonAsync("/api/v1/ad/folders", request)).Content.ReadFromJsonAsync<List<JsonElement>>(Web);
        Assert.All(steps!, s => Assert.Equal("Ok", s.GetProperty("status").GetString()));

        var acl = await client.GetFromJsonAsync<JsonElement>($"/api/v1/ad/folders/acl?path={Uri.EscapeDataString($@"A:\A\new{_base}")}", Web);
        Assert.True(acl.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Permissions_and_validation()
    {
        var reader = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/v1/ad/folders",
            new { projectDn = A, path = @"A:\A\x", baseName = "x", orgCode = "a", createDirectory = true })).StatusCode);
        var creator = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersCreate);
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.PostAsJsonAsync("/api/v1/ad/folders",
            new { projectDn = A, path = @"Q:\x", baseName = "x", orgCode = "a", createDirectory = true })).StatusCode);
        var projects = await reader.GetFromJsonAsync<List<JsonElement>>("/api/v1/ad/projects", Web);
        Assert.Contains(projects!, p => p.GetProperty("name").GetString() == "A");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersApiTests"`
Expected: FAIL — 404 на `/api/v1/ad/folders`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Api/Controllers/AdFoldersController.cs
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed record PathRequest(string Path);

[WinAdminModule(AdFoldersModule.ModuleId)]
[PlatformErrors]
[AdErrors]
[Route("/api/v1/ad/folders")]
public sealed class AdFoldersController(IAdFoldersService folders, AccessContextFactory contexts) : WinAdminControllerBase
{
    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

    [HttpGet]
    [RequirePermission(PermissionIds.AdFoldersRead)]
    public async Task<FolderCatalogResult> List([FromQuery] string? project, [FromQuery] string? q, CancellationToken ct)
        => await folders.ListAsync(await ActorAsync(ct), string.IsNullOrWhiteSpace(project) ? null : project, q, ct);

    [HttpGet("user/{sam}")]
    [RequirePermission(PermissionIds.AdFoldersRead)]
    public async Task<IActionResult> UserAccess(string sam, CancellationToken ct) => Ok(await folders.UserAccessAsync(await ActorAsync(ct), sam, ct));

    [HttpPost("membership")]
    [RequirePermission(PermissionIds.AdFoldersMembership)]
    public async Task<IActionResult> Membership([FromBody] MembershipRequest request, CancellationToken ct)
        => Ok(await folders.ChangeMembershipAsync(await ActorAsync(ct), request, ct));

    [HttpPost("preview")]
    [RequirePermission(PermissionIds.AdFoldersCreate)]
    public async Task<FolderPreview> Preview([FromBody] CreateFolderRequest request, CancellationToken ct)
        => await folders.PreviewAsync(await ActorAsync(ct), request, ct);

    [HttpPost]
    [RequirePermission(PermissionIds.AdFoldersCreate)]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request, CancellationToken ct)
        => Ok(await folders.CreateFolderAsync(await ActorAsync(ct), request, ct));

    [HttpGet("acl")]
    [RequirePermission(PermissionIds.AdFoldersRead)]
    public async Task<FolderAclState> Acl([FromQuery] string path, CancellationToken ct) => await folders.InspectAclAsync(await ActorAsync(ct), path, ct);

    [HttpPost("acl-fix")]
    [RequirePermission(PermissionIds.AdFoldersCreate)]
    public async Task<IActionResult> FixAcl([FromBody] PathRequest request, CancellationToken ct)
        => Ok(await folders.FixAclAsync(await ActorAsync(ct), request.Path, ct));
}
```

`AdProjectsController.Sources` — добавить `(AdFoldersModule.ModuleId, PermissionIds.AdFoldersRead),` (+ `using WinAdmin.Core.ActiveDirectory.Folders;`).

`UnauthorizedAccessException` / `IOException` от файлового сервера в шагах сценария → `ScenarioRunner` показывает «Внутренняя ошибка»; чтобы оператор видел причину, расширить в `ScenarioRunner` (3a) список ожидаемых исключений: `UnauthorizedAccessException`, `IOException` (их `Message` безопасен: путь и «Отказано в доступе»). В `AdErrorsAttribute` — `UnauthorizedAccessException` → 422, `IOException` → 422.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdFoldersApiTests|FullyQualifiedName~AdUsersApiTests|FullyQualifiedName~ScenarioRunnerTests"`, затем полный набор.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(api): folders endpoints (catalog, membership, wizard, ACL) and folders projects source"
```

---

### Task 8: Интерфейс — страница «Папки» и вкладка в карточке пользователя

**Files:**
- Modify: `src/frontend/src/api/types.ts`, `src/frontend/src/api/client.ts`
- Create: `src/frontend/src/pages/AdFolders.tsx`
- Modify: `src/frontend/src/pages/AdUsers.tsx`, `src/frontend/src/App.tsx`, `src/frontend/src/components/AppLayout.tsx`

**Interfaces:**
- Consumes: API Task 7, `api.directory.search` (1c) для добавления участника
- Produces: маршрут `/ad/folders` (модуль `ad-folders`, право `ad-folders.read`)

- [ ] **Step 1: Типы и API**

`types.ts`:

```ts
export interface AdMember { dn: string; name: string; sam: string | null; isGroup: boolean; enabled: boolean }
export interface FolderGroupView { dn: string; name: string; sid: string | null; members: AdMember[] }
export interface AdFolder {
  path: string; projectDn: string | null; projectName: string | null
  full: FolderGroupView | null; read: FolderGroupView | null; others: FolderGroupView[]; warnings: string[]
}
export interface UnparsedGroup { dn: string; name: string; projectName: string | null; reason: string }
export interface FolderCatalogResult { folders: AdFolder[]; unparsed: UnparsedGroup[] }
export interface UserFolderAccess { projectName: string | null; path: string; hasFull: boolean; hasRead: boolean }
export interface CreateFolderRequest { projectDn: string; path: string; baseName: string; orgCode?: string; createDirectory: boolean }
export interface FolderPreview { fullGroup: string; readGroup: string; unc: string; groupsOuDn: string }
export interface FolderAclState { exists: boolean; missing: string[]; warnings: string[]; ok: boolean }
```

`client.ts` — в `api`:

```ts
  folders: {
    list: (params: { project?: string; q?: string }) => http.get<FolderCatalogResult>('/ad/folders', { params }).then((r) => r.data),
    userAccess: (sam: string) => http.get<UserFolderAccess[]>(`/ad/folders/user/${encodeURIComponent(sam)}`).then((r) => r.data),
    membership: (body: { groupDn: string; member: string; add: boolean; removeFromOther: boolean }) =>
      http.post<ScenarioStep[]>('/ad/folders/membership', body).then((r) => r.data),
    preview: (body: CreateFolderRequest) => http.post<FolderPreview>('/ad/folders/preview', body).then((r) => r.data),
    create: (body: CreateFolderRequest) => http.post<ScenarioStep[]>('/ad/folders', body).then((r) => r.data),
    acl: (path: string) => http.get<FolderAclState>('/ad/folders/acl', { params: { path } }).then((r) => r.data),
    fixAcl: (path: string) => http.post<ScenarioStep[]>('/ad/folders/acl-fix', { path }).then((r) => r.data),
  },
```

(типы — в `import type`.)

- [ ] **Step 2: Страница «Папки»**

```tsx
// src/frontend/src/pages/AdFolders.tsx
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, App, Button, Card, Checkbox, Col, Drawer, Empty, Form, Input, List, Modal, Popconfirm, Row, Select, Space, Table, Tag, Typography } from 'antd'
import { DeleteOutlined, FolderAddOutlined, ReloadOutlined, SafetyOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import { api } from '../api/client'
import type { AdFolder, AdProject, FolderAclState, FolderGroupView, FolderPreview, ScenarioStep } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import PageHeader from '../components/PageHeader'

const errorText = (e: any, fallback: string) => e?.response?.data?.message ?? fallback

function Steps({ steps }: { steps: ScenarioStep[] }) {
  const color = { Ok: 'green', Skipped: 'default', Failed: 'red' } as const
  return <List size="small" dataSource={steps} renderItem={(s) => (
    <List.Item><Space><Tag color={color[s.status]}>{s.status === 'Ok' ? 'ок' : s.status === 'Skipped' ? 'пропущен' : 'ошибка'}</Tag>{s.name}<Typography.Text type="secondary">{s.message}</Typography.Text></Space></List.Item>
  )} />
}

function MemberColumn({ title, group, other, onChanged }: { title: string; group: FolderGroupView | null; other: FolderGroupView | null; onChanged: (steps: ScenarioStep[]) => void }) {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [options, setOptions] = useState<{ value: string; label: string }[]>([])
  const [busy, setBusy] = useState(false)
  const search = useMemo(() => {
    let timer: number | undefined
    return (q: string) => {
      window.clearTimeout(timer)
      if (q.trim().length < 2) { setOptions([]); return }
      timer = window.setTimeout(async () => {
        try { setOptions((await api.directory.search(q, 'user')).map((u) => ({ value: u.samAccountName, label: `${u.displayName ?? u.samAccountName} (${u.samAccountName})` }))) }
        catch (e) { message.error(errorText(e, 'Поиск в AD не выполнен')) }
      }, 300)
    }
  }, [message])

  const change = async (member: string, add: boolean) => {
    if (!group) return
    setBusy(true)
    try { onChanged(await api.folders.membership({ groupDn: group.dn, member, add, removeFromOther: true })) }
    catch (e) { message.error(errorText(e, 'Не удалось изменить участников')) }
    finally { setBusy(false) }
  }

  return (
    <Card size="small" title={<Space>{title}<Tag>{group?.members.length ?? 0}</Tag></Space>} extra={group && <Typography.Text type="secondary">{group.name}</Typography.Text>}>
      {!group ? <Empty description={`Нет группы ${title}`} /> : (
        <>
          {can('ad-folders.membership') && (
            <Select showSearch filterOption={false} onSearch={search} options={options} value={null as unknown as string}
              placeholder="Добавить пользователя (от 2 символов)" style={{ width: '100%', marginBottom: 8 }} loading={busy}
              onChange={(v) => change(v, true)} notFoundContent="Ничего не найдено" />
          )}
          <List size="small" dataSource={group.members} locale={{ emptyText: 'Нет участников' }} renderItem={(m) => (
            <List.Item actions={can('ad-folders.membership') ? [
              <Popconfirm key="del" title="Убрать из группы?" onConfirm={() => change(m.sam ?? m.dn, false)}><Button size="small" type="text" danger icon={<DeleteOutlined />} /></Popconfirm>,
            ] : []}>
              <Space>{m.name}{m.isGroup && <Tag>группа</Tag>}{!m.enabled && <Tag>отключён</Tag>}
                {other?.members.some((o) => o.dn === m.dn) && <Tag color="gold">есть и в другой группе</Tag>}</Space>
            </List.Item>
          )} />
        </>
      )}
    </Card>
  )
}

function NewFolder({ projects, open, onClose, onDone }: { projects: AdProject[]; open: boolean; onClose: () => void; onDone: () => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm()
  const [preview, setPreview] = useState<FolderPreview>()
  const [steps, setSteps] = useState<ScenarioStep[]>()
  const [busy, setBusy] = useState(false)

  const values = () => ({ ...form.getFieldsValue(), orgCode: form.getFieldValue('orgCode') || undefined })
  const doPreview = async () => {
    try { await form.validateFields(); setPreview(await api.folders.preview(values())) } catch (e: any) { if (e?.response) message.error(errorText(e, 'Проверка не прошла')) }
  }
  const create = async () => {
    setBusy(true)
    try { setSteps(await api.folders.create(values())); onDone() } catch (e) { message.error(errorText(e, 'Не удалось создать')) } finally { setBusy(false) }
  }

  return (
    <Modal title="Новая папка" open={open} width={640} onCancel={() => { onClose(); setPreview(undefined); setSteps(undefined); form.resetFields() }}
      footer={steps ? <Button onClick={() => { onClose(); setPreview(undefined); setSteps(undefined); form.resetFields() }}>Закрыть</Button> : (
        <Space><Button onClick={doPreview}>Проверить</Button><Button type="primary" disabled={!preview} loading={busy} onClick={create}>Создать</Button></Space>
      )}>
      {steps ? <Steps steps={steps} /> : (
        <Form form={form} layout="vertical" initialValues={{ createDirectory: true }} onValuesChange={() => setPreview(undefined)}>
          <Form.Item name="projectDn" label="Проект" rules={[{ required: true }]}><Select options={projects.map((p) => ({ value: p.dn, label: p.name }))} /></Form.Item>
          <Form.Item name="path" label="Путь к папке (как в описании групп)" rules={[{ required: true }]}><Input placeholder="A:\Проект\Документы" /></Form.Item>
          <Row gutter={12}>
            <Col span={12}><Form.Item name="baseName" label="Имя для групп (латиница)" rules={[{ required: true }]}><Input placeholder="docs" /></Form.Item></Col>
            <Col span={12}><Form.Item name="orgCode" label="Код организации (пусто — авто)"><Input placeholder="co" /></Form.Item></Col>
          </Row>
          <Form.Item name="createDirectory" valuePropName="checked"><Checkbox>Создать папку, если её нет</Checkbox></Form.Item>
          {preview && <Alert type="info" message="Будет создано" description={<>Группы: <b>{preview.fullGroup}</b>, <b>{preview.readGroup}</b> в {preview.groupsOuDn}<br />Папка: {preview.unc}</>} />}
        </Form>
      )}
    </Modal>
  )
}

export default function AdFolders() {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [projects, setProjects] = useState<AdProject[]>([])
  const [project, setProject] = useState<string>()
  const [q, setQ] = useState('')
  const [folders, setFolders] = useState<AdFolder[]>([])
  const [unparsed, setUnparsed] = useState(0)
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<string>()
  const [steps, setSteps] = useState<ScenarioStep[]>()
  const [acl, setAcl] = useState<FolderAclState>()
  const [wizard, setWizard] = useState(false)

  useEffect(() => { api.ad.projects().then(setProjects).catch((e) => message.error(errorText(e, 'Не удалось загрузить проекты'))) }, [message])

  const load = useCallback(async () => {
    setLoading(true)
    try { const r = await api.folders.list({ project, q: q || undefined }); setFolders(r.folders); setUnparsed(r.unparsed.length) }
    catch (e) { message.error(errorText(e, 'Не удалось загрузить папки')) }
    finally { setLoading(false) }
  }, [project, q, message])

  useEffect(() => { load() }, [load])

  const folder = folders.find((f) => f.path === selected)
  const columns: ColumnsType<AdFolder> = [
    { title: 'Папка', dataIndex: 'path', sorter: (a, b) => a.path.localeCompare(b.path, 'ru') },
    { title: 'Проект', dataIndex: 'projectName', width: 140 },
    { title: 'Доступ', width: 230, render: (_, f) => <Space>
      <Tag color={f.full ? 'green' : undefined}>Full ({f.full?.members.length ?? 0})</Tag>
      <Tag color={f.read ? 'blue' : undefined}>Read ({f.read?.members.length ?? 0})</Tag></Space> },
    { title: 'Предупреждения', render: (_, f) => f.warnings.map((w) => <Tag key={w} color="gold">{w}</Tag>) },
  ]

  const checkAcl = async (path: string) => {
    try { setAcl(await api.folders.acl(path)) } catch (e) { message.error(errorText(e, 'Не удалось прочитать права')) }
  }

  return (
    <>
      <PageHeader title="Папки" subtitle="Доступ к сетевым папкам через группы безопасности"
        extra={<Space>
          {can('ad-folders.create') && <Button type="primary" icon={<FolderAddOutlined />} onClick={() => setWizard(true)}>Новая папка</Button>}
          <Button icon={<ReloadOutlined />} onClick={load}>Обновить</Button>
        </Space>} />
      <Card className="sp-glass">
        <Space wrap style={{ marginBottom: 12 }}>
          <Select allowClear placeholder="Все проекты" style={{ minWidth: 220 }} value={project} onChange={setProject}
            options={projects.map((p) => ({ value: p.dn, label: p.name }))} />
          <Input.Search allowClear placeholder="Путь или слова (например: документы проект)" style={{ width: 360 }} onSearch={setQ} />
          {unparsed > 0 && <Tag color="gold">групп без пути в описании: {unparsed}</Tag>}
        </Space>
        <Table<AdFolder> rowKey="path" size="small" loading={loading} dataSource={folders} columns={columns}
          pagination={{ pageSize: 50, showSizeChanger: false }}
          onRow={(f) => ({ onClick: () => { setSelected(f.path); setSteps(undefined); setAcl(undefined) }, style: { cursor: 'pointer' } })} />
      </Card>
      <Drawer title={folder?.path} open={Boolean(folder)} onClose={() => setSelected(undefined)} width={820}>
        {folder && (
          <Space direction="vertical" style={{ width: '100%' }}>
            {folder.warnings.length > 0 && <Alert type="warning" message={folder.warnings.join('; ')} />}
            {steps && <Alert type="info" message="Результат" description={<Steps steps={steps} />} closable onClose={() => setSteps(undefined)} />}
            <Row gutter={12}>
              <Col span={12}><MemberColumn title="Full Access" group={folder.full} other={folder.read} onChanged={(s) => { setSteps(s); load() }} /></Col>
              <Col span={12}><MemberColumn title="Read Only" group={folder.read} other={folder.full} onChanged={(s) => { setSteps(s); load() }} /></Col>
            </Row>
            <Space>
              <Button icon={<SafetyOutlined />} onClick={() => checkAcl(folder.path)}>Проверить права NTFS</Button>
              {can('ad-folders.create') && acl && !acl.ok && acl.exists && (
                <Popconfirm title="Добавить недостающие права NTFS?" onConfirm={async () => {
                  try { setSteps(await api.folders.fixAcl(folder.path)); await checkAcl(folder.path) } catch (e) { message.error(errorText(e, 'Не удалось исправить')) }
                }}><Button type="primary">Исправить права NTFS</Button></Popconfirm>
              )}
            </Space>
            {acl && <Alert type={acl.ok ? (acl.warnings.length ? 'warning' : 'success') : 'error'}
              message={acl.ok ? 'Права NTFS в порядке' : acl.exists ? 'Не хватает прав NTFS' : 'Папка не найдена на файловом сервере'}
              description={[...acl.missing, ...acl.warnings].join('; ') || undefined} />}
          </Space>
        )}
      </Drawer>
      <NewFolder projects={projects} open={wizard} onClose={() => setWizard(false)} onDone={load} />
    </>
  )
}
```

`Empty` в импорте используется в `MemberColumn`; убрать неиспользуемые импорты, если `tsc`/линтер укажет.

`App.tsx`: `<Route path="/ad/folders" element={<Guard perm="ad-folders.read" module="ad-folders"><AdFolders /></Guard>} />` (+ импорт). `AppLayout.tsx`: пункт `{ key: '/ad/folders', icon: <FolderOpenOutlined />, label: 'Папки', perm: 'ad-folders.read', module: 'ad-folders' }` после «Пользователи AD» (+ `FolderOpenOutlined` в импорт).

- [ ] **Step 3: Вкладка «Папки» в карточке пользователя AD**

`AdUsers.tsx`, `UserCard`: состояние и загрузка:

```tsx
  const { can, moduleOn } = useAuth()
  const [folderAccess, setFolderAccess] = useState<UserFolderAccess[]>()
```

в `load` после истории:

```tsx
      if (moduleOn('ad-folders') && can('ad-folders.read')) setFolderAccess(await api.folders.userAccess(sam).catch(() => []))
```

в `Tabs.items` перед «История»:

```tsx
            ...(folderAccess ? [{ key: 'folders', label: `Папки (${folderAccess.length})`, children: (
              <List size="small" dataSource={folderAccess} locale={{ emptyText: 'Нет доступа к папкам' }} renderItem={(f) => (
                <List.Item><Space>{f.path}<Typography.Text type="secondary">{f.projectName}</Typography.Text>
                  {f.hasFull && <Tag color="green">Full</Tag>}{f.hasRead && <Tag color="blue">Read</Tag>}</Space></List.Item>
              )} />
            ) }] : []),
```

(`useAuth()` в `UserCard` уже есть — расширить деструктуризацию; `UserFolderAccess` — в импорт типов.)

- [ ] **Step 4: Сборка**

Run: `cd src/frontend && npm run build && npm run lint` → Expected: без ошибок, нет предупреждений в изменённых файлах; `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.

- [ ] **Step 5: Commit**

```bash
git add src/frontend
git commit -m "feat(ui): folders page (catalog, members, wizard, NTFS check/fix) and folders tab in AD user card"
```

---

### Task 9: Документация, выкатка на DC, проверка

**Files:**
- Modify: `docs/03-api-reference.md`, `docs/02-security.md`, `releases/package/README.md`

- [ ] **Step 1: Документация**

- API: маршруты Task 7, права `ad-folders.*`, коды 403/404/409/422/503.
- Безопасность: что делегировать (Write members на `sg_*`, Create Group objects на OU групп, «Изменение разрешений» на корне шары), имперсонация служебной учёткой для файлового сервера.
- Пакетный README: «Модули → Папки»: включить, настройки (префикс, `A=\\сервер\шара`, права NTFS), «Проверка окружения» (быстрая и полная).

- [ ] **Step 2: Полный прогон и пакет** — `dotnet test` (с `WINADMIN_TEST_POSTGRES`) → PASS; `releases\build.ps1` (PowerShell), zip без данных.

- [ ] **Step 3: Выкатка на DC** — `dc9.ps1` (хеш, резервные копии, распаковка, health, откат).

- [ ] **Step 4: Проверка на DC (только чтение)** — под `admin`: включить модуль `ad-folders`; `GET /ad/folders` → папки PCS (число групп `sg_*` и папок, предупреждения); `GET /ad/folders/user/{sam}` для пользователя с `sg_*`; `GET /environment?module=ad-folders` → `folders.groups`, `folders.mappings` (буквы дисков из описаний — сообщить пользователю для настройки). Записи в AD и на файловый сервер не выполнять.

- [ ] **Step 5: Commit и push**

```bash
git add docs releases/package/README.md
git commit -m "docs: folders module"
git push
```
