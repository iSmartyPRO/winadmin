# 3a: общий слой AD и проверка окружения — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Общий слой для модулей «Пользователи AD» и «Папки»: структура каталога (корневая OU, проекты, скрытые OU, учётка записи), чтение и запись AD, охрана зоны/области, пошаговые сценарии, механизм проверки окружения с платформенными проверками AD и UI для настройки и проверки.

**Architecture:** Чистая логика (DN, область «Проекты», охрана, сценарии, генератор паролей) — в `WinAdmin.Core/ActiveDirectory`; LDAP-чтение/запись — в `WinAdmin.Infrastructure/ActiveDirectory` поверх общего `LdapConnections` (вынесен из `LdapDirectoryService`). Проверки окружения — интерфейс `IEnvironmentCheck` в Core, сервис-агрегатор и фоновый монитор в Infrastructure, API и страница в UI.

**Tech Stack:** .NET 10, `System.DirectoryServices.Protocols`, EF Core (SQLite/PostgreSQL, таблица `PlatformSettings`), xUnit + Moq + WebApplicationFactory, React 19 + AntD 6.

**Spec:** `docs/superpowers/specs/2026-10-10-ad-users-folders-design.md` — §1 (общий слой), §4 (проверка окружения: механизм и платформенные проверки), §5–6, §8 строка 3a.

## Global Constraints

- Пароль учётки записи — `[Secret]`: AES-256-GCM через `ISecretProtector`, AAD (purpose) = `ad-structure.WritePassword`; в API только «задан/не задан».
- Запись в AD — только по зашифрованному каналу: Negotiate + Signing + Sealing (389) или LDAPS (636).
- Перед любой записью — охрана: объект в `RootOu`, проект не скрыт, проект в области действующего лица; нарушение → 403 до обращения к writer.
- Сгенерированный пароль: 20 символов, CSPRNG, есть верхний/нижний регистр, цифра, спецсимвол; не пишется в журнал/лог.
- Ошибки LDAP — русские тексты из спец. §1.5; детали — в лог службы; клиенту не отдаются.
- `CheckResult(Code, Title, Status, Message, Fix)`; статусы `Ok | Warning | Failed | Skipped`; глубина `Quick | Full`.
- Фон: `Quick` раз в час; в аудит — только смена итогового статуса (`environment.status`).
- Новое право `platform.environment.check` («Проверка окружения», не опасное).
- Сообщения коммитов заканчиваются `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- После `npm run build` — `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.
- Запись в AD боевого домена в тестах — только `[AdFact]` и только внутри `WINADMIN_TEST_AD_ROOT` (тестовый OU).

## Review Focus

1. DN с экранированной запятой (`OU=Иванов\, Пётр,OU=Проект,…`) и разный регистр/пробелы (`ou=Проект, dc=pcs`) — проект определяется верно, сравнение не ломается (Task 1: `DnUtilsTests.Escaped_comma_and_case`).
2. DN-«соседи» с общим суффиксом строки (`OU=Pro,…` vs `OU=Project,…`) — `IsUnderOrSame` не путает по `EndsWith` (Task 1: `Sibling_with_common_suffix_is_not_under`).
3. Сохранение настроек AD без пароля не стирает ранее сохранённый пароль; пустая строка — стирает (Task 3: `Null_password_keeps_existing_and_empty_clears`).
4. Ключ шифрования недоступен/пароль повреждён — проверка окружения сообщает «пароль учётки записи не расшифровывается», а не падает (Task 7: `Undecryptable_password_is_reported`).
5. Одна проверка бросает исключение или зависает — остальные модули всё равно проверяются, зависшая получает `Failed` по таймауту (Task 6: `Throwing_and_hanging_checks_do_not_break_others`).

---

## Карта файлов

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/ActiveDirectory/DnUtils.cs` | Разбор/нормализация DN, проект объекта, путь OU |
| `src/backend/WinAdmin.Core/ActiveDirectory/AdStructure.cs` | `AdStructureSettings`, `AdWriteMode`, `AdWriteCredential`, `AdProject`, `EffectiveRights`, `WriterStatus` |
| `src/backend/WinAdmin.Core/ActiveDirectory/ProjectScopeProvider.cs` | Область «Проекты (OU)» |
| `src/backend/WinAdmin.Core/ActiveDirectory/AdGuard.cs` | Охрана зоны и области |
| `src/backend/WinAdmin.Core/ActiveDirectory/AdWriteException.cs` | Ошибка записи с кодом LDAP |
| `src/backend/WinAdmin.Core/Operations/ScenarioRunner.cs` | Пошаговые сценарии |
| `src/backend/WinAdmin.Core/Security/PasswordGenerator.cs` | Генератор паролей |
| `src/backend/WinAdmin.Core/EnvironmentChecks/EnvironmentChecks.cs` | `IEnvironmentCheck`, `CheckResult`, `CheckStatus`, `CheckDepth`, `EnvironmentReport` |
| `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs` | `IAdStructureStore`, `IAdReader`, `IAdWriter`, `IEnvironmentService` |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapConnections.cs` | Подключение, корень каталога, поиск с лимитом и постранично |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdStructureStore.cs` | Настройки в `PlatformSettings` (ключ `ad-structure`), шифрование пароля |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdCredentials.cs` | `AdWriteCredential` → `NetworkCredential` |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapAdReader.cs` | Проекты, существование, эффективные права, статус учётки записи |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdWriteRequests.cs` | Чистые построители значений LDAP и перевод ошибок |
| `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapAdWriter.cs` | Запись в AD |
| `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/EnvironmentService.cs` | Запуск проверок, последние результаты, аудит смены статуса |
| `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/EnvironmentMonitor.cs` | Фон: `Quick` раз в час |
| `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/PlatformAdCheck.cs` | `ad.directory`, `ad.root`, `ad.writer`, `ad.channel` |
| `src/backend/WinAdmin.Api/Controllers/AdStructureController.cs` | `GET/PUT /api/v1/settings/ad` |
| `src/backend/WinAdmin.Api/Controllers/EnvironmentController.cs` | `GET /api/v1/environment`, `/environment/latest` |
| `src/frontend/src/components/AdStructureCard.tsx` | Карточка «Active Directory: управление» |
| `src/frontend/src/pages/Environment.tsx` | Страница «Проверка окружения» |
| `src/tests/WinAdmin.Tests/Fakes/FakeAdReader.cs` | Фейк `IAdReader` |

## Отложено в 3b/3c (решения плана)

- Проверки `ad.rights.users` (нужен список атрибутов модуля «Пользователи») — в 3b; `ad.rights.groups`, `ad.rights.create` (нужен префикс модуля «Папки») — в 3c. Проверки фактических прав реализуются в модулях, используя `IAdReader.ReadEffectiveAsync` из этого плана.
- `GET /api/v1/ad/projects` и подключение `ProjectScopeProvider` к модулям/редактору ролей — в 3b (модулей ещё нет; эндпоинт по спецификации зависит от их прав).
- Списки пользователей/групп в `IAdReader` — в 3b/3c; здесь — постраничный поиск в `LdapConnections`, на котором они строятся.
- Значок итогового статуса проверки на карточке модуля и в `/me` — в 3b/3c вместе с модулями; в 3a результаты видны на странице «Проверка окружения» (`/environment/latest`).

---

### Task 1: DN, структура каталога, область «Проекты», генератор паролей

**Files:**
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/DnUtils.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/AdStructure.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/ProjectScopeProvider.cs`
- Create: `src/backend/WinAdmin.Core/Security/PasswordGenerator.cs`
- Test: `src/tests/WinAdmin.Tests/AdStructureTests.cs`

**Interfaces:**
- Produces:
  - `DnUtils.Split(string dn) → IReadOnlyList<string>`; `Normalize(string) → string`; `IsUnderOrSame(string dn, string ancestor) → bool`; `RelativeOuPath(string dn, string root) → IReadOnlyList<string>`; `ProjectDn(string dn, string root) → string?`; `FirstValue(string dn) → string`
  - `enum AdWriteMode { ServiceAccount, ProcessAccount }`
  - `record AdStructureSettings(string? RootOu, string UsersOuName, IReadOnlyList<string> HiddenOus, AdWriteMode WriteMode, string? WriteLogin, bool HasWritePassword = false)` с `Default`, `Normalize()`
  - `record AdWriteCredential(AdWriteMode Mode, string? Login, string? Password)` (`ToString` без пароля)
  - `record AdProject(string Dn, string Name)`
  - `record EffectiveRights(IReadOnlySet<string> Attributes, IReadOnlySet<string> ChildClasses)`
  - `record WriterStatus(string Account, bool Bound, string? Error, bool? Enabled, bool? Locked, DateTimeOffset? PasswordExpires, bool Encrypted)`
  - `ProjectScopeProvider : IScopeProvider`
  - `PasswordGenerator.Generate(int length = 20) → string`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdStructureTests.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class DnUtilsTests
{
    private const string Root = "OU=Accounts,DC=pcs-msk,DC=com";

    [Fact]
    public void Splits_and_normalizes_with_case_and_spaces()
    {
        Assert.Equal(["OU=Проект", "DC=pcs"], DnUtils.Split("OU=Проект,DC=pcs"));
        Assert.Equal("OU=Проект,DC=pcs-msk,DC=com", DnUtils.Normalize("ou=Проект , dc=pcs-msk,  DC = com"));
    }

    [Fact]
    public void Escaped_comma_and_case()
    {
        const string dn = @"CN=Иванов\, Пётр,OU=Users,OU=Проект А,OU=Accounts,DC=PCS-MSK,DC=COM";
        Assert.Equal(5 + 1, DnUtils.Split(dn).Count);
        Assert.Equal("OU=Проект А,OU=Accounts,DC=PCS-MSK,DC=COM", DnUtils.ProjectDn(dn, Root));
        Assert.Equal(["Проект А", "Users"], DnUtils.RelativeOuPath(dn, Root));
        Assert.Equal("Иванов, Пётр", DnUtils.FirstValue(dn));
    }

    [Fact]
    public void Sibling_with_common_suffix_is_not_under()
    {
        Assert.False(DnUtils.IsUnderOrSame("OU=Users,OU=Project,DC=x", "OU=ject,DC=x"));
        Assert.False(DnUtils.IsUnderOrSame("OU=Pro,DC=x", "OU=Project,DC=x"));
        Assert.True(DnUtils.IsUnderOrSame("OU=Users,OU=Project,DC=x", "ou=project, dc=X"));
        Assert.True(DnUtils.IsUnderOrSame("OU=Project,DC=x", "OU=Project,DC=x"));
    }

    [Fact]
    public void Objects_outside_root_have_no_project()
    {
        Assert.Null(DnUtils.ProjectDn("CN=u,OU=Other,DC=pcs-msk,DC=com", Root));
        Assert.Null(DnUtils.ProjectDn(Root, Root));
        Assert.Null(DnUtils.ProjectDn("CN=Group,OU=Accounts,DC=pcs-msk,DC=com", Root)); // прямо в корне, не в проекте
        Assert.Empty(DnUtils.RelativeOuPath("CN=u,DC=other", Root));
    }
}

public sealed class AdStructureSettingsTests
{
    [Fact]
    public void Normalize_trims_and_defaults()
    {
        var s = new AdStructureSettings(" ou=Accounts, dc=pcs ", " ", [" IT ", "", "it"], AdWriteMode.ServiceAccount, "  ", false).Normalize();
        Assert.Equal("OU=Accounts,DC=pcs", s.RootOu);
        Assert.Equal("Users", s.UsersOuName);
        Assert.Equal(["IT"], s.HiddenOus);
        Assert.Null(s.WriteLogin);
    }

    [Theory]
    [InlineData("DC=pcs,DC=com")]
    [InlineData("CN=Users,DC=pcs")]
    [InlineData("не DN")]
    public void Root_must_be_an_ou(string root)
        => Assert.Throws<ArgumentException>(() =>
            (AdStructureSettings.Default with { RootOu = root }).Normalize());

    [Fact]
    public void Credential_does_not_print_password()
        => Assert.DoesNotContain("secret-pw", new AdWriteCredential(AdWriteMode.ServiceAccount, "PCS\\svc", "secret-pw").ToString());
}

public sealed class ProjectScopeProviderTests
{
    private readonly ProjectScopeProvider _p = new();

    [Fact]
    public void Normalizes_and_deduplicates()
        => Assert.Equal(["OU=Проект,OU=Accounts,DC=pcs"],
            _p.Normalize(new ScopeDefinition([" ou=Проект, ou=Accounts,dc=pcs", "OU=Проект,OU=Accounts,DC=pcs"])).Items);

    [Theory]
    [InlineData("Проект")]
    [InlineData("CN=x,DC=pcs")]
    public void Rejects_non_ou_items(string item)
        => Assert.Throws<ArgumentException>(() => _p.Normalize(new ScopeDefinition([item])));

    [Fact]
    public void Empty_scope_is_rejected()
        => Assert.Throws<ArgumentException>(() => _p.Normalize(new ScopeDefinition([" "])));

    [Fact]
    public void Nested_ou_is_subset_of_project()
    {
        var project = new ScopeDefinition(["OU=Проект,OU=Accounts,DC=pcs"]);
        Assert.True(_p.IsSubsetOf(new ScopeDefinition(["OU=Users,OU=Проект,OU=Accounts,DC=pcs"]), project));
        Assert.False(_p.IsSubsetOf(new ScopeDefinition(["OU=Другой,OU=Accounts,DC=pcs"]), project));
    }
}

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generates_complex_unique_passwords()
    {
        var all = Enumerable.Range(0, 200).Select(_ => PasswordGenerator.Generate()).ToList();
        Assert.All(all, p =>
        {
            Assert.Equal(20, p.Length);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => !char.IsLetterOrDigit(c));
        });
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Too_short_length_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(7));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DnUtilsTests|FullyQualifiedName~AdStructureSettingsTests|FullyQualifiedName~ProjectScopeProviderTests|FullyQualifiedName~PasswordGeneratorTests"`
Expected: FAIL — компиляция: нет `DnUtils`, `AdStructureSettings`, `ProjectScopeProvider`, `PasswordGenerator`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/DnUtils.cs
using System.Text;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Distinguished Name: разбор с учётом экранирования (\,), сравнение без учёта регистра.</summary>
public static class DnUtils
{
    public static IReadOnlyList<string> Split(string dn)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < dn.Length; i++)
        {
            char c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                current.Append(c).Append(dn[++i]);
                continue;
            }
            if (c == ',')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.ToString().Trim().Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    /// <summary>«ou=Проект , dc=x» → «OU=Проект,DC=x»: тип — в верхнем регистре, пробелы вокруг «=» и «,» убраны.</summary>
    public static string Normalize(string dn)
        => string.Join(",", Split(dn).Select(rdn =>
        {
            int eq = rdn.IndexOf('=');
            return eq < 0 ? rdn : rdn[..eq].Trim().ToUpperInvariant() + "=" + rdn[(eq + 1)..].Trim();
        }));

    public static bool IsUnderOrSame(string dn, string ancestor)
    {
        var a = Split(Normalize(dn));
        var b = Split(Normalize(ancestor));
        if (b.Count == 0 || b.Count > a.Count) return false;
        return a.Skip(a.Count - b.Count).SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Имена OU от корня вниз (без корня): «CN=u,OU=Users,OU=Проект,{root}» → [Проект, Users].</summary>
    public static IReadOnlyList<string> RelativeOuPath(string dn, string root)
    {
        if (!IsUnderOrSame(dn, root)) return [];
        var parts = Split(Normalize(dn));
        int rootCount = Split(Normalize(root)).Count;
        return parts.Take(parts.Count - rootCount).Reverse()
            .Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
            .Select(p => Unescape(p[3..]))
            .ToList();
    }

    /// <summary>DN OU проекта (первого уровня под root), в котором лежит объект; null — вне проектов.</summary>
    public static string? ProjectDn(string dn, string root)
    {
        if (!IsUnderOrSame(dn, root)) return null;
        var parts = Split(Normalize(dn));
        int index = parts.Count - Split(Normalize(root)).Count - 1;
        if (index < 0 || !parts[index].StartsWith("OU=", StringComparison.OrdinalIgnoreCase)) return null;
        return string.Join(",", parts.Skip(index));
    }

    /// <summary>Значение первого RDN без экранирования: «CN=Иванов\, Пётр,…» → «Иванов, Пётр».</summary>
    public static string FirstValue(string dn)
    {
        var first = Split(dn).FirstOrDefault() ?? "";
        int eq = first.IndexOf('=');
        return Unescape(eq < 0 ? first : first[(eq + 1)..].Trim());
    }

    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length) i++;
            sb.Append(value[i]);
        }
        return sb.ToString();
    }
}
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/AdStructure.cs
namespace WinAdmin.Core.ActiveDirectory;

public enum AdWriteMode { ServiceAccount, ProcessAccount }

/// <summary>Структура каталога для модулей AD. Пароль учётки записи сюда не попадает — только флаг.</summary>
public sealed record AdStructureSettings(
    string? RootOu, string UsersOuName, IReadOnlyList<string> HiddenOus,
    AdWriteMode WriteMode, string? WriteLogin, bool HasWritePassword = false)
{
    public static AdStructureSettings Default { get; } = new(null, "Users", [], AdWriteMode.ServiceAccount, null);

    public AdStructureSettings Normalize()
    {
        string? root = string.IsNullOrWhiteSpace(RootOu) ? null : DnUtils.Normalize(RootOu);
        if (root is not null)
        {
            var parts = DnUtils.Split(root);
            if (!parts[0].StartsWith("OU=", StringComparison.OrdinalIgnoreCase)
                || !parts.Any(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                || parts.Any(p => !p.Contains('=')))
                throw new ArgumentException("Корневая OU — DN вида OU=…,DC=…,DC=….");
        }
        return this with
        {
            RootOu = root,
            UsersOuName = string.IsNullOrWhiteSpace(UsersOuName) ? "Users" : UsersOuName.Trim(),
            HiddenOus = (HiddenOus ?? []).Select(h => (h ?? "").Trim()).Where(h => h.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WriteLogin = string.IsNullOrWhiteSpace(WriteLogin) ? null : WriteLogin.Trim(),
        };
    }
}

/// <summary>Учётка записи: служебная (логин/пароль) или процесса службы.</summary>
public sealed record AdWriteCredential(AdWriteMode Mode, string? Login, string? Password)
{
    public override string ToString() => Mode == AdWriteMode.ProcessAccount ? "учётка службы" : Login ?? "(не задана)";
}

public sealed record AdProject(string Dn, string Name);

/// <summary>Что учётке записи разрешено на объекте (allowedAttributesEffective / allowedChildClassesEffective).</summary>
public sealed record EffectiveRights(IReadOnlySet<string> Attributes, IReadOnlySet<string> ChildClasses);

/// <summary>Состояние учётки записи для проверки окружения.</summary>
public sealed record WriterStatus(
    string Account, bool Bound, string? Error, bool? Enabled, bool? Locked, DateTimeOffset? PasswordExpires, bool Encrypted);
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/ProjectScopeProvider.cs
using WinAdmin.Core.Modules;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Область «Проекты (OU)»: элемент — DN OU; право на OU распространяется на всё внутри неё.</summary>
public sealed class ProjectScopeProvider : IScopeProvider
{
    public string Title => "Проекты (OU)";

    public ScopeDefinition Normalize(ScopeDefinition scope)
    {
        var items = scope.Items.Select(i => (i ?? "").Trim()).Where(i => i.Length > 0).Select(i =>
        {
            var parts = DnUtils.Split(i);
            if (parts.Count == 0 || !parts[0].StartsWith("OU=", StringComparison.OrdinalIgnoreCase) || parts.Any(p => !p.Contains('=')))
                throw new ArgumentException($"«{i}» — не DN подразделения (OU=…).");
            return DnUtils.Normalize(i);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) throw new ArgumentException("Укажите хотя бы один проект.");
        return new ScopeDefinition(items);
    }

    public bool IsSubsetOf(ScopeDefinition candidate, ScopeDefinition container)
        => candidate.Items.All(c => container.Items.Any(p => DnUtils.IsUnderOrSame(c, p)));
}
```

```csharp
// src/backend/WinAdmin.Core/Security/PasswordGenerator.cs
using System.Security.Cryptography;

namespace WinAdmin.Core.Security;

/// <summary>Случайный пароль (CSPRNG): есть заглавная, строчная, цифра и спецсимвол.</summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!@#$%^&*-_=+?";

    public static string Generate(int length = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);
        string all = Upper + Lower + Digits + Special;
        var chars = new char[length];
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Special[RandomNumberGenerator.GetInt32(Special.Length)];
        for (int i = 4; i < length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: та же команда, что в Step 2.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core src/tests/WinAdmin.Tests/AdStructureTests.cs
git commit -m "feat(ad): DN utilities, AD structure settings, project scope, password generator"
```

---

### Task 2: Охрана записи и пошаговые сценарии

**Files:**
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/AdGuard.cs`
- Create: `src/backend/WinAdmin.Core/ActiveDirectory/AdWriteException.cs`
- Create: `src/backend/WinAdmin.Core/Operations/ScenarioRunner.cs`
- Test: `src/tests/WinAdmin.Tests/AdGuardTests.cs`

**Interfaces:**
- Consumes: `DnUtils`, `AdStructureSettings` (Task 1); `IAccessContext`, `EffectivePermissions`, `AccessDeniedException` (существующие)
- Produces:
  - `AdGuard.EnsureManaged(string dn, AdStructureSettings s) → string` (DN проекта)
  - `AdGuard.EnsureInScope(IAccessContext actor, string permissionId, string projectDn)`
  - `AdGuard.InScopeProjects(IAccessContext actor, string permissionId, IEnumerable<AdProject> projects) → IReadOnlyList<AdProject>`
  - `class AdWriteException(int code, string message) : Exception` (`Code`)
  - `enum StepStatus { Ok, Skipped, Failed }`, `record ScenarioStep(string Name, StepStatus Status, string? Message)`, `record StepResult(StepStatus Status, string? Message)` с `Done(...)`, `Skip(...)`
  - `ScenarioRunner(Action<Exception>? log = null)`: `Task<bool> RunAsync(string name, Func<Task<StepResult>> action)`, `Steps`, `Failed`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdGuardTests.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class AdGuardTests
{
    private const string Root = "OU=Accounts,DC=pcs";
    private static readonly AdStructureSettings S = AdStructureSettings.Default with { RootOu = Root, HiddenOus = ["IT"] };
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);

    private static IAccessContext Actor(params string[] projects) => new AccessContext(
        new PrincipalRef(PrincipalType.LocalUser, "u", []), "u",
        PermissionEvaluator.Evaluate([new RoleSnapshot("r", false,
            [new RoleGrant(ScopedTestModule.Read, projects.Length == 0 ? null : new ScopeDefinition(projects))])], Catalog));

    [Fact]
    public void Managed_object_returns_its_project()
        => Assert.Equal("OU=Проект,OU=Accounts,DC=pcs", AdGuard.EnsureManaged("CN=u,OU=Users,OU=Проект,OU=Accounts,DC=pcs", S));

    [Theory]
    [InlineData("CN=u,OU=Other,DC=pcs")]
    [InlineData("CN=u,OU=IT,OU=Accounts,DC=pcs")]
    [InlineData("CN=g,OU=Accounts,DC=pcs")]
    public void Outside_root_hidden_or_not_in_project_is_denied(string dn)
        => Assert.Throws<AccessDeniedException>(() => AdGuard.EnsureManaged(dn, S));

    [Fact]
    public void Missing_root_is_a_configuration_error()
        => Assert.Throws<InvalidOperationException>(() => AdGuard.EnsureManaged("CN=u,DC=pcs", AdStructureSettings.Default));

    [Fact]
    public void Scope_limits_projects()
    {
        var actor = Actor("OU=A,OU=Accounts,DC=pcs");
        AdGuard.EnsureInScope(actor, ScopedTestModule.Read, "OU=A,OU=Accounts,DC=pcs");
        Assert.Throws<AccessDeniedException>(() => AdGuard.EnsureInScope(actor, ScopedTestModule.Read, "OU=B,OU=Accounts,DC=pcs"));
        Assert.Throws<AccessDeniedException>(() => AdGuard.EnsureInScope(actor, PermissionIds.ServicesRead, "OU=A,OU=Accounts,DC=pcs"));
        AdGuard.EnsureInScope(Actor(), ScopedTestModule.Read, "OU=B,OU=Accounts,DC=pcs"); // без области — всё
    }

    [Fact]
    public void In_scope_projects_are_filtered()
    {
        var projects = new[] { new AdProject("OU=A,OU=Accounts,DC=pcs", "A"), new AdProject("OU=B,OU=Accounts,DC=pcs", "B") };
        Assert.Equal(["A"], AdGuard.InScopeProjects(Actor("ou=a, ou=accounts, dc=pcs"), ScopedTestModule.Read, projects).Select(p => p.Name));
        Assert.Empty(AdGuard.InScopeProjects(Actor("OU=A,OU=Accounts,DC=pcs"), PermissionIds.ServicesRead, projects));
    }
}

public sealed class ScenarioRunnerTests
{
    [Fact]
    public async Task Stops_after_failure_and_marks_rest_not_run()
    {
        var logged = new List<Exception>();
        var run = new ScenarioRunner(logged.Add);
        int executed = 0;
        Assert.True(await run.RunAsync("Шаг 1", () => { executed++; return Task.FromResult(StepResult.Done("ok")); }));
        Assert.True(await run.RunAsync("Шаг 2", () => Task.FromResult(StepResult.Skip("уже сделано"))));
        Assert.False(await run.RunAsync("Шаг 3", () => throw new AdWriteException(50, "Нет прав")));
        Assert.False(await run.RunAsync("Шаг 4", () => { executed++; return Task.FromResult(StepResult.Done()); }));

        Assert.Equal(1, executed);
        Assert.True(run.Failed);
        Assert.Equal([StepStatus.Ok, StepStatus.Skipped, StepStatus.Failed, StepStatus.Skipped], run.Steps.Select(s => s.Status));
        Assert.Equal("Нет прав", run.Steps[2].Message);
        Assert.Contains("не выполнялся", run.Steps[3].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(logged); // ожидаемые ошибки не логируются как сбои
    }

    [Fact]
    public async Task Unexpected_exception_is_hidden_from_user_and_logged()
    {
        var logged = new List<Exception>();
        var run = new ScenarioRunner(logged.Add);
        await run.RunAsync("Шаг", () => throw new NullReferenceException("секретная деталь"));
        Assert.DoesNotContain("секретная", run.Steps[0].Message);
        Assert.Single(logged);
    }
}
```

`AccessContext` и `ScopedTestModule` — существующие (в тестах 1b). Если конструктор `AccessContext` в Core называется иначе — взять из `RoleServiceTests.Admin`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdGuardTests|FullyQualifiedName~ScenarioRunnerTests"`
Expected: FAIL — компиляция: нет `AdGuard`, `ScenarioRunner`, `AdWriteException`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/AdWriteException.cs
namespace WinAdmin.Core.ActiveDirectory;

/// <summary>AD отклонил запись; Code — код результата LDAP, Message — текст для оператора.</summary>
public sealed class AdWriteException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
```

```csharp
// src/backend/WinAdmin.Core/ActiveDirectory/AdGuard.cs
using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Охрана записи: объект в зоне управления и проект в области действующего лица.</summary>
public static class AdGuard
{
    /// <summary>Возвращает DN проекта объекта; вне RootOu, вне проекта или в скрытой OU — 403.</summary>
    public static string EnsureManaged(string dn, AdStructureSettings settings)
    {
        if (settings.RootOu is null)
            throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        var project = DnUtils.ProjectDn(dn, settings.RootOu)
                      ?? throw new AccessDeniedException("Объект вне управляемых проектов AD.", []);
        string name = DnUtils.RelativeOuPath(project, settings.RootOu)[0];
        if (settings.HiddenOus.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new AccessDeniedException($"Проект «{name}» скрыт настройками.", []);
        return project;
    }

    public static void EnsureInScope(IAccessContext actor, string permissionId, string projectDn)
    {
        var scope = actor.Permissions.ScopeFor(permissionId)
                    ?? throw new AccessDeniedException("Недостаточно прав.", [permissionId]);
        if (scope.IsUnrestricted) return;
        if (!scope.Items!.Any(p => DnUtils.IsUnderOrSame(projectDn, p)))
            throw new AccessDeniedException("Проект вне вашей области.", [permissionId]);
    }

    public static IReadOnlyList<AdProject> InScopeProjects(IAccessContext actor, string permissionId, IEnumerable<AdProject> projects)
    {
        var scope = actor.Permissions.ScopeFor(permissionId);
        if (scope is null) return [];
        return scope.IsUnrestricted
            ? projects.ToList()
            : projects.Where(p => scope.Items!.Any(s => DnUtils.IsUnderOrSame(p.Dn, s))).ToList();
    }
}
```

```csharp
// src/backend/WinAdmin.Core/Operations/ScenarioRunner.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Operations;

public enum StepStatus { Ok, Skipped, Failed }

public sealed record ScenarioStep(string Name, StepStatus Status, string? Message);

public sealed record StepResult(StepStatus Status, string? Message)
{
    public static StepResult Done(string? message = null) => new(StepStatus.Ok, message);
    public static StepResult Skip(string message) => new(StepStatus.Skipped, message);
}

/// <summary>
/// Сценарий из шагов: ошибка останавливает выполнение, следующие шаги помечаются «не выполнялся».
/// Ожидаемые ошибки (AD, доступ, проверки) показываются как есть; прочие — общим текстом и в лог.
/// </summary>
public sealed class ScenarioRunner(Action<Exception>? log = null)
{
    private readonly List<ScenarioStep> _steps = [];

    public IReadOnlyList<ScenarioStep> Steps => _steps;
    public bool Failed { get; private set; }

    public async Task<bool> RunAsync(string name, Func<Task<StepResult>> action)
    {
        if (Failed)
        {
            _steps.Add(new(name, StepStatus.Skipped, "Не выполнялся: предыдущий шаг завершился ошибкой"));
            return false;
        }
        try
        {
            var result = await action();
            _steps.Add(new(name, result.Status, result.Message));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed = true;
            bool expected = ex is AdWriteException or DirectoryUnavailableException or AccessDeniedException
                or ArgumentException or InvalidOperationException;
            if (!expected) log?.Invoke(ex);
            _steps.Add(new(name, StepStatus.Failed, expected ? ex.Message : "Внутренняя ошибка — подробности в журнале службы"));
            return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: та же команда, что в Step 2.
Expected: PASS (7 тестов + теория).

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core src/tests/WinAdmin.Tests/AdGuardTests.cs
git commit -m "feat(ad): write guard (zone and scope) and step scenario runner"
```

---

### Task 3: Хранилище настроек структуры AD (с шифрованием пароля)

**Files:**
- Create: `src/backend/WinAdmin.Core/Abstractions/IAdServices.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdStructureStore.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/AdStructureStoreTests.cs`

**Interfaces:**
- Consumes: `AdStructureSettings`, `AdWriteCredential` (Task 1); `ISecretProtector`, `PlatformSettingEntity` (существующие)
- Produces:
  - `IAdStructureStore`: `Task<AdStructureSettings> GetAsync(ct)`, `Task SaveAsync(AdStructureSettings settings, string? newPassword, ct)` (`null` — не менять, `""` — удалить), `Task<AdWriteCredential> GetWriteCredentialAsync(ct)` (бросает `SecretUnavailableException`, если пароль не расшифровывается)
  - `IAdReader`, `IAdWriter`, `IEnvironmentService` — объявления (реализации в Task 4–6)
  - `AdStructureStore(IServiceScopeFactory scopes, ISecretProtector protector) : IAdStructureStore` (singleton, кэш)

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/AdStructureStoreTests.cs
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AdStructureStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly ISecretProtector _protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));

    public AdStructureStoreTests()
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

    private AdStructureStore Store(ISecretProtector? protector = null)
        => new(_sp.GetRequiredService<IServiceScopeFactory>(), protector ?? _protector);

    private string RawJson()
    {
        using var scope = _sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().PlatformSettings.Single(s => s.Key == AdStructureStore.Key).Json;
    }

    private static AdStructureSettings Sample => new("OU=Accounts,DC=pcs", "Users", ["IT"], AdWriteMode.ServiceAccount, "PCS\\svc-winadmin");

    [Fact]
    public async Task Defaults_without_saved_settings()
        => Assert.Null((await Store().GetAsync()).RootOu);

    [Fact]
    public async Task Password_is_encrypted_at_rest_and_never_returned()
    {
        await Store().SaveAsync(Sample, "S3cret-Pass!");
        Assert.DoesNotContain("S3cret-Pass!", RawJson());
        Assert.Contains("enc:v1:", RawJson());

        var fresh = Store();
        var settings = await fresh.GetAsync();
        Assert.True(settings.HasWritePassword);
        Assert.Equal("OU=Accounts,DC=pcs", settings.RootOu);
        Assert.Equal("S3cret-Pass!", (await fresh.GetWriteCredentialAsync()).Password);
    }

    [Fact]
    public async Task Null_password_keeps_existing_and_empty_clears()
    {
        var store = Store();
        await store.SaveAsync(Sample, "First-Pass1!");
        await store.SaveAsync(Sample with { UsersOuName = "Staff" }, null);
        Assert.Equal("First-Pass1!", (await Store().GetWriteCredentialAsync()).Password);
        Assert.Equal("Staff", (await Store().GetAsync()).UsersOuName);

        await store.SaveAsync(Sample, "");
        Assert.False((await Store().GetAsync()).HasWritePassword);
        Assert.Null((await Store().GetWriteCredentialAsync()).Password);
    }

    [Fact]
    public async Task Process_account_needs_no_password()
    {
        await Store().SaveAsync(Sample with { WriteMode = AdWriteMode.ProcessAccount, WriteLogin = null }, null);
        var cred = await Store().GetWriteCredentialAsync();
        Assert.Equal(AdWriteMode.ProcessAccount, cred.Mode);
        Assert.Null(cred.Password);
    }

    [Fact]
    public async Task Wrong_key_reports_secret_unavailable()
    {
        await Store().SaveAsync(Sample, "S3cret-Pass!");
        var other = Store(new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)));
        Assert.True((await other.GetAsync()).HasWritePassword);
        await Assert.ThrowsAsync<SecretUnavailableException>(() => other.GetWriteCredentialAsync());
    }

    [Fact]
    public async Task Invalid_root_is_rejected_and_nothing_saved()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().SaveAsync(Sample with { RootOu = "DC=pcs" }, null));
        Assert.Null((await Store().GetAsync()).RootOu);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdStructureStoreTests"`
Expected: FAIL — компиляция: нет `AdStructureStore`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Core/Abstractions/IAdServices.cs
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Core.Abstractions;

/// <summary>Структура каталога и учётка записи (PlatformSettings, ключ «ad-structure»).</summary>
public interface IAdStructureStore
{
    Task<AdStructureSettings> GetAsync(CancellationToken ct = default);

    /// <summary>newPassword: null — не менять, "" — удалить, иначе — зашифровать и сохранить.</summary>
    Task SaveAsync(AdStructureSettings settings, string? newPassword, CancellationToken ct = default);

    /// <summary>Учётка записи с расшифрованным паролем; SecretUnavailableException — пароль не расшифровывается.</summary>
    Task<AdWriteCredential> GetWriteCredentialAsync(CancellationToken ct = default);
}

/// <summary>Чтение AD для модулей (учётка компьютера; эффективные права — от имени учётки записи).</summary>
public interface IAdReader
{
    /// <summary>OU первого уровня под RootOu; includeHidden=false — без скрытых.</summary>
    Task<IReadOnlyList<AdProject>> ListProjectsAsync(bool includeHidden = false, CancellationToken ct = default);
    Task<bool> ExistsAsync(string dn, CancellationToken ct = default);
    Task<EffectiveRights> ReadEffectiveAsync(string dn, CancellationToken ct = default);
    Task<WriterStatus> GetWriterStatusAsync(CancellationToken ct = default);
}

/// <summary>Запись в AD учёткой записи. Ошибки — AdWriteException / DirectoryUnavailableException.</summary>
public interface IAdWriter
{
    Task ModifyAttributesAsync(string dn, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default);
    /// <summary>false — уже был участником.</summary>
    Task<bool> AddMemberAsync(string groupDn, string memberDn, CancellationToken ct = default);
    /// <summary>false — не был участником.</summary>
    Task<bool> RemoveMemberAsync(string groupDn, string memberDn, CancellationToken ct = default);
    Task SetPrimaryGroupAsync(string userDn, string groupSid, CancellationToken ct = default);
    Task SetEnabledAsync(string userDn, bool enabled, CancellationToken ct = default);
    Task ResetPasswordAsync(string userDn, string password, bool mustChange, CancellationToken ct = default);
    /// <summary>Возвращает новый DN.</summary>
    Task<string> MoveAsync(string dn, string targetOuDn, CancellationToken ct = default);
    /// <summary>Глобальная группа безопасности; возвращает DN.</summary>
    Task<string> CreateGroupAsync(string ouDn, string cn, string description, CancellationToken ct = default);
    Task SetPhotoAsync(string userDn, byte[]? photo, CancellationToken ct = default);
}

/// <summary>Проверка окружения: запуск, последние результаты.</summary>
public interface IEnvironmentService
{
    Task<IReadOnlyList<EnvironmentReport>> RunAsync(string? moduleId, CheckDepth depth, CancellationToken ct = default);
    IReadOnlyList<EnvironmentReport> Latest { get; }
}
```

(`WinAdmin.Core.EnvironmentChecks` создаётся в Task 6; чтобы этот файл компилировался сейчас, в Task 3 создать `src/backend/WinAdmin.Core/EnvironmentChecks/EnvironmentChecks.cs` целиком — код в Task 6, Step 3 — и тесты Task 6 добавить в Task 6.)

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdStructureStore.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Настройки структуры AD в PlatformSettings; пароль учётки записи — зашифрован.</summary>
public sealed class AdStructureStore(IServiceScopeFactory scopes, ISecretProtector protector) : IAdStructureStore
{
    public const string Key = "ad-structure";
    private const string Purpose = "ad-structure.WritePassword";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Stored(
        string? RootOu, string UsersOuName, List<string> HiddenOus, AdWriteMode WriteMode, string? WriteLogin, string? WritePassword);

    private Stored? _cached;

    public async Task<AdStructureSettings> GetAsync(CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        return s is null
            ? AdStructureSettings.Default
            : new AdStructureSettings(s.RootOu, s.UsersOuName, s.HiddenOus, s.WriteMode, s.WriteLogin, s.WritePassword is not null);
    }

    public async Task SaveAsync(AdStructureSettings settings, string? newPassword, CancellationToken ct = default)
    {
        var clean = settings.Normalize();
        var previous = await LoadAsync(ct);
        string? password = newPassword switch
        {
            null => previous?.WritePassword,
            "" => null,
            _ => protector.Protect(newPassword, Purpose),
        };
        var stored = new Stored(clean.RootOu, clean.UsersOuName, [.. clean.HiddenOus], clean.WriteMode, clean.WriteLogin, password);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.FirstOrDefaultAsync(r => r.Key == Key, ct);
        if (row is null) db.PlatformSettings.Add(row = new PlatformSettingEntity { Key = Key });
        row.Json = JsonSerializer.Serialize(stored, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        _cached = stored;
    }

    public async Task<AdWriteCredential> GetWriteCredentialAsync(CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        if (s is null) return new AdWriteCredential(AdWriteMode.ServiceAccount, null, null);
        string? password = s.WritePassword is null ? null : protector.Unprotect(s.WritePassword, Purpose);
        return new AdWriteCredential(s.WriteMode, s.WriteLogin, password);
    }

    private async Task<Stored?> LoadAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Key == Key, ct);
        return _cached = row is null ? null : JsonSerializer.Deserialize<Stored>(row.Json, Json);
    }
}
```

`DependencyInjection.AddWinAdminInfrastructure` — после `IDirectorySettingsStore`:

```csharp
        services.AddSingleton<IAdStructureStore, AdStructureStore>();
```

(`ISecretProtector` регистрируется в `Program.cs` до `AddWinAdminInfrastructure`? Проверить порядок: если регистрируется позже — DI всё равно разрешит singleton при первом запросе; порядок регистрации не важен.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdStructureStoreTests"`
Expected: PASS (6 тестов).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/AdStructureStoreTests.cs
git commit -m "feat(ad): AD structure settings store with encrypted write password"
```

---

### Task 4: LdapConnections и LdapAdReader

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapConnections.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdCredentials.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapAdReader.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapDirectoryService.cs` (использует `LdapConnections`)
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/LdapAdReaderTests.cs`

**Interfaces:**
- Consumes: `IDirectorySettingsStore`, `DirectorySettings`, `DirectoryUnavailableException`, `DirectoryLogin` (1c); `IAdStructureStore`, `AdWriteCredential` (Task 3)
- Produces:
  - `LdapConnections.Open(DirectorySettings s, NetworkCredential? credential) → LdapConnection`; `NamingContext(LdapConnection) → string`; `Search(LdapConnection, SearchRequest) → SearchResponse` (частичный ответ при превышении лимита); `SearchPaged(LdapConnection, string baseDn, string filter, SearchScope scope, params string[] attributes) → IEnumerable<SearchResultEntry>`; `Unavailable(Exception) → DirectoryUnavailableException`
  - `AdCredentials.ToNetwork(AdWriteCredential c, string? domain) → NetworkCredential?` (`null` для `ProcessAccount`; `ArgumentException` — служебная учётка без логина/пароля)
  - `LdapAdReader(IDirectorySettingsStore directory, IAdStructureStore structure) : IAdReader`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/LdapAdReaderTests.cs
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class AdCredentialsTests
{
    [Theory]
    [InlineData("PCS\\svc", "svc", "PCS")]
    [InlineData("svc@pcs-msk.com", "svc@pcs-msk.com", "")]
    [InlineData("svc", "svc", "pcs-msk.com")]
    public void Service_account_login_forms(string login, string user, string domain)
    {
        var c = AdCredentials.ToNetwork(new AdWriteCredential(AdWriteMode.ServiceAccount, login, "pw"), "pcs-msk.com")!;
        Assert.Equal(user, c.UserName);
        Assert.Equal(domain, c.Domain);
        Assert.Equal("pw", c.Password);
    }

    [Fact]
    public void Process_account_has_no_credential()
        => Assert.Null(AdCredentials.ToNetwork(new AdWriteCredential(AdWriteMode.ProcessAccount, null, null), "pcs"));

    [Theory]
    [InlineData(null, "pw")]
    [InlineData("svc", null)]
    public void Incomplete_service_account_is_rejected(string? login, string? password)
        => Assert.Throws<ArgumentException>(() =>
            AdCredentials.ToNetwork(new AdWriteCredential(AdWriteMode.ServiceAccount, login, password), "pcs"));
}

public sealed class LdapAdReaderTests
{
    private static LdapAdReader Reader(DirectorySettings directory, AdStructureSettings structure)
    {
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(directory));
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(structure);
        st.Setup(s => s.GetWriteCredentialAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdWriteCredential(AdWriteMode.ServiceAccount, "svc", "pw"));
        return new LdapAdReader(dir, st.Object);
    }

    [Fact]
    public async Task Directory_off_means_unavailable()
        => await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Reader(DirectorySettings.Disabled, AdStructureSettings.Default with { RootOu = "OU=A,DC=x" }).ListProjectsAsync());

    [Fact]
    public async Task Missing_root_is_a_configuration_error()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Reader(new DirectorySettings(true, "x.local", "127.0.0.1", null, false), AdStructureSettings.Default).ListProjectsAsync());

    [Fact]
    public async Task Unreachable_dc_is_reported_in_writer_status_not_thrown()
    {
        var status = await Reader(new DirectorySettings(true, "x.local", "127.0.0.1", "DC=x", false),
            AdStructureSettings.Default with { RootOu = "OU=A,DC=x" }).GetWriterStatusAsync();
        Assert.False(status.Bound);
        Assert.False(string.IsNullOrEmpty(status.Error));
    }

    private static LdapAdReader Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdStructureSettings.Default with { RootOu = E("WINADMIN_TEST_AD_ROOT") });
        st.Setup(s => s.GetWriteCredentialAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdWriteCredential(AdWriteMode.ServiceAccount, E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD")));
        return new LdapAdReader(dir, st.Object, new System.Net.NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
    }

    [AdFact]
    public async Task Real_directory_projects_rights_and_writer()
    {
        var ad = Real();
        var projects = await ad.ListProjectsAsync();
        Assert.True(await ad.ExistsAsync(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_ROOT")!));
        Assert.False(await ad.ExistsAsync("OU=nope-" + Guid.NewGuid().ToString("N") + "," + Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_ROOT")));
        var status = await ad.GetWriterStatusAsync();
        Assert.True(status.Bound, status.Error);
        Assert.True(status.Encrypted);
        if (projects.Count > 0)
            Assert.NotNull(await ad.ReadEffectiveAsync(projects[0].Dn));
    }
}
```

`LdapAdReader` получает третий необязательный параметр `NetworkCredential? readCredential = null` (как `LdapDirectoryService`) — для интеграционных тестов с машины вне домена.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapAdReaderTests|FullyQualifiedName~AdCredentialsTests"`
Expected: FAIL — компиляция: нет `AdCredentials`, `LdapAdReader`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapConnections.cs
using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Общие операции LDAP: подключение с подписью/шифрованием или LDAPS, поиск с лимитом и постранично.</summary>
public static class LdapConnections
{
    public static LdapConnection Open(DirectorySettings s, NetworkCredential? credential)
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

    public static string NamingContext(LdapConnection connection)
    {
        var response = (SearchResponse)connection.SendRequest(
            new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
        return response.Entries[0].Attributes["defaultNamingContext"]?[0] as string
               ?? throw new DirectoryUnavailableException("Не удалось прочитать корень каталога");
    }

    /// <summary>Поиск с лимитом: «size limit exceeded» — не ошибка, берём частичный ответ.</summary>
    public static SearchResponse Search(LdapConnection connection, SearchRequest request)
    {
        try
        {
            return (SearchResponse)connection.SendRequest(request);
        }
        catch (DirectoryOperationException ex) when (ex.Response is SearchResponse { ResultCode: ResultCode.SizeLimitExceeded } partial)
        {
            return partial;
        }
    }

    /// <summary>Постраничный поиск (по 500): для списков больше лимита сервера (1000).</summary>
    public static IEnumerable<SearchResultEntry> SearchPaged(
        LdapConnection connection, string baseDn, string filter, SearchScope scope, params string[] attributes)
    {
        var page = new PageResultRequestControl(500);
        var request = new SearchRequest(baseDn, filter, scope, attributes);
        request.Controls.Add(page);
        while (true)
        {
            var response = (SearchResponse)connection.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries) yield return entry;
            var cookie = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault()?.Cookie;
            if (cookie is null || cookie.Length == 0) yield break;
            page.Cookie = cookie;
        }
    }

    public static DirectoryUnavailableException Unavailable(Exception ex)
        => new("Контроллер домена недоступен", ex);
}
```

`LdapDirectoryService` — удалить приватные `Connect`, `ReadNamingContext`, `Search` и заменить вызовы на `LdapConnections.Open`, `LdapConnections.NamingContext`, `LdapConnections.Search` (поведение не меняется; существующие тесты `LdapDirectoryServiceTests` — регрессия).

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdCredentials.cs
using System.Net;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

public static class AdCredentials
{
    /// <summary>Служебная учётка → NetworkCredential; учётка службы → null (bind от имени процесса).</summary>
    public static NetworkCredential? ToNetwork(AdWriteCredential credential, string? domain)
    {
        if (credential.Mode == AdWriteMode.ProcessAccount) return null;
        if (string.IsNullOrWhiteSpace(credential.Login) || string.IsNullOrEmpty(credential.Password))
            throw new ArgumentException("Служебная учётка записи не задана: укажите логин и пароль (Настройки → Active Directory).");
        string login = credential.Login.Trim();
        int slash = login.IndexOf('\\');
        if (slash > 0) return new NetworkCredential(login[(slash + 1)..], credential.Password, login[..slash]);
        if (login.Contains('@')) return new NetworkCredential(login, credential.Password);
        return new NetworkCredential(login, credential.Password, domain);
    }
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapAdReader.cs
using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Чтение AD для модулей: учётка компьютера (readCredential — только для тестов вне домена).</summary>
public sealed class LdapAdReader(
    IDirectorySettingsStore directory, IAdStructureStore structure, NetworkCredential? readCredential = null) : IAdReader
{
    public async Task<IReadOnlyList<AdProject>> ListProjectsAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        var (dir, st) = await SettingsAsync(ct);
        return Run(dir, readCredential, connection =>
        {
            var projects = LdapConnections.SearchPaged(connection, st.RootOu!, "(objectClass=organizationalUnit)", SearchScope.OneLevel, "ou")
                .Select(e => new AdProject(DnUtils.Normalize(e.DistinguishedName), DnUtils.FirstValue(e.DistinguishedName)))
                .Where(p => includeHidden || !st.HiddenOus.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return (IReadOnlyList<AdProject>)projects;
        });
    }

    public async Task<bool> ExistsAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledDirectoryAsync(ct);
        return Run(dir, readCredential, connection =>
        {
            try
            {
                connection.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "distinguishedName"));
                return true;
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject)
            {
                return false;
            }
        });
    }

    public async Task<EffectiveRights> ReadEffectiveAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledDirectoryAsync(ct);
        var credential = AdCredentials.ToNetwork(await structure.GetWriteCredentialAsync(ct), dir.Domain);
        return Run(dir, credential, connection =>
        {
            var response = (SearchResponse)connection.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base,
                "allowedAttributesEffective", "allowedChildClassesEffective"));
            var entry = response.Entries[0];
            return new EffectiveRights(Values(entry, "allowedAttributesEffective"), Values(entry, "allowedChildClassesEffective"));
        });
    }

    public async Task<WriterStatus> GetWriterStatusAsync(CancellationToken ct = default)
    {
        DirectorySettings dir;
        AdWriteCredential credential;
        try
        {
            dir = await EnabledDirectoryAsync(ct);
            credential = await structure.GetWriteCredentialAsync(ct);
        }
        catch (Exception ex) when (ex is DirectoryUnavailableException or SecretUnavailableException)
        {
            return new WriterStatus("?", false, ex is SecretUnavailableException
                ? "Пароль учётки записи не расшифровывается (ключ шифрования изменён или повреждён) — задайте его заново"
                : ex.Message, null, null, null, false);
        }

        string account = credential.Mode == AdWriteMode.ProcessAccount
            ? $"учётка службы ({System.Environment.MachineName}$)"
            : credential.Login ?? "(не задана)";
        NetworkCredential? network;
        try { network = AdCredentials.ToNetwork(credential, dir.Domain); }
        catch (ArgumentException ex) { return new WriterStatus(account, false, ex.Message, null, null, null, false); }

        try
        {
            using var connection = LdapConnections.Open(dir, network);
            bool encrypted = dir.UseLdaps || connection.SessionOptions.Sealing;
            if (credential.Mode == AdWriteMode.ProcessAccount)
                return new WriterStatus(account, true, null, null, null, null, encrypted);

            var (sam, upn) = DirectoryLogin.Parse(credential.Login!);
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(connection);
            string filter = upn is null
                ? $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={LdapFilter.Escape(sam)}))"
                : $"(&(objectCategory=person)(objectClass=user)(userPrincipalName={LdapFilter.Escape(upn)}))";
            var response = LdapConnections.Search(connection, new SearchRequest(baseDn, filter, SearchScope.Subtree,
                "userAccountControl", "lockoutTime", "msDS-UserPasswordExpiryTimeComputed") { SizeLimit = 1 });
            if (response.Entries.Count == 0)
                return new WriterStatus(account, true, "Учётка найдена при входе, но не найдена поиском", null, null, null, encrypted);
            var e = response.Entries[0];
            bool enabled = LdapMapping.IsEnabled(e.Attributes["userAccountControl"]?[0] as string);
            bool locked = long.TryParse(e.Attributes["lockoutTime"]?[0] as string, out long lockout) && lockout > 0;
            DateTimeOffset? expires = long.TryParse(e.Attributes["msDS-UserPasswordExpiryTimeComputed"]?[0] as string, out long ft)
                                      && ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc()
                ? DateTimeOffset.FromFileTime(ft) : null;
            return new WriterStatus(account, true, null, enabled, locked, expires, encrypted);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            return new WriterStatus(account, false, "Неверный логин или пароль учётки записи (или учётка отключена/заблокирована)", null, null, null, false);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
        {
            return new WriterStatus(account, false, "Контроллер домена недоступен: " + ex.Message, null, null, null, false);
        }
    }

    private async Task<(DirectorySettings, AdStructureSettings)> SettingsAsync(CancellationToken ct)
    {
        var dir = await EnabledDirectoryAsync(ct);
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null) throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        return (dir, st);
    }

    private async Task<DirectorySettings> EnabledDirectoryAsync(CancellationToken ct)
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

    private static IReadOnlySet<string> Values(SearchResultEntry entry, string attribute)
        => entry.Attributes[attribute]?.GetValues(typeof(string)).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
           ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
```

`Missing_root_is_a_configuration_error`: `SettingsAsync` проверяет корень **после** подключения к домену — тест задаёт включённый домен с недоступным сервером, но до подключения дело не доходит (`InvalidOperationException` раньше `Run`). 

`DependencyInjection`:

```csharp
        services.AddSingleton<IAdReader>(sp => new LdapAdReader(
            sp.GetRequiredService<IDirectorySettingsStore>(), sp.GetRequiredService<IAdStructureStore>()));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~LdapAdReaderTests|FullyQualifiedName~AdCredentialsTests|FullyQualifiedName~LdapDirectoryServiceTests"`
Expected: PASS (интеграционные `[AdFact]` — Skip без переменных).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/LdapAdReaderTests.cs
git commit -m "feat(ad): shared LDAP connections, paged search, AD reader for projects/rights/writer status"
```

---

### Task 5: LdapAdWriter

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdWriteRequests.cs`
- Create: `src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapAdWriter.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/LdapAdWriterTests.cs`

**Interfaces:**
- Consumes: `IAdWriter` (Task 3), `LdapConnections`, `AdCredentials` (Task 4), `AdWriteException` (Task 2)
- Produces:
  - `AdWriteRequests.PasswordValue(string) → byte[]`; `ToggleDisabled(int uac, bool enabled) → int`; `GlobalSecurityGroupType` (`"-2147483646"`); `Rid(string sid) → string`; `Parent(string dn) → string`; `Rdn(string dn) → string`; `Translate(ResultCode code, string account, string operation, string target, string? serverMessage) → Exception` (`AdWriteException` или `DirectoryUnavailableException`)
  - `LdapAdWriter(IDirectorySettingsStore directory, IAdStructureStore structure, ILogger<LdapAdWriter> logger) : IAdWriter`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/LdapAdWriterTests.cs
using System.DirectoryServices.Protocols;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class AdWriteRequestsTests
{
    [Fact]
    public void Password_is_quoted_utf16le()
        => Assert.Equal(Encoding.Unicode.GetBytes("\"Пароль1!\""), AdWriteRequests.PasswordValue("Пароль1!"));

    [Theory]
    [InlineData(512, false, 514)]
    [InlineData(514, true, 512)]
    [InlineData(66050, true, 66048)]
    [InlineData(512, true, 512)]
    public void Toggles_only_disable_bit(int uac, bool enabled, int expected)
        => Assert.Equal(expected, AdWriteRequests.ToggleDisabled(uac, enabled));

    [Fact]
    public void Rid_parent_and_rdn()
    {
        Assert.Equal("513", AdWriteRequests.Rid("S-1-5-21-1-2-3-513"));
        Assert.Equal(@"OU=Users,OU=Проект,DC=x", AdWriteRequests.Parent(@"CN=Иванов\, Пётр,OU=Users,OU=Проект,DC=x"));
        Assert.Equal(@"CN=Иванов\, Пётр", AdWriteRequests.Rdn(@"CN=Иванов\, Пётр,OU=Users,OU=Проект,DC=x"));
    }

    [Fact]
    public void Global_security_group_type()
        => Assert.Equal(unchecked((int)0x80000002).ToString(), AdWriteRequests.GlobalSecurityGroupType);

    [Theory]
    [InlineData(ResultCode.InsufficientAccessRights, "не хватает прав")]
    [InlineData(ResultCode.NoSuchObject, "не найден")]
    [InlineData(ResultCode.EntryAlreadyExists, "уже существует")]
    [InlineData(ResultCode.ConstraintViolation, "политике")]
    [InlineData(ResultCode.UnwillingToPerform, "отклонил")]
    public void Translates_ldap_errors_to_russian(ResultCode code, string fragment)
    {
        var ex = AdWriteRequests.Translate(code, "PCS\\svc", "изменение членства", "CN=g,DC=x", "00002098: SecErr: DSID-03150F94");
        var write = Assert.IsType<AdWriteException>(ex);
        Assert.Contains(fragment, write.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DSID", write.Message);
        Assert.Equal((int)code, write.Code);
    }

    [Theory]
    [InlineData(ResultCode.Busy)]
    [InlineData(ResultCode.Unavailable)]
    public void Server_trouble_is_unavailable(ResultCode code)
        => Assert.IsType<DirectoryUnavailableException>(AdWriteRequests.Translate(code, "svc", "op", "dn", null));
}

public sealed class LdapAdWriterTests
{
    private static LdapAdWriter Writer(DirectorySettings dir, AdWriteCredential cred)
    {
        var d = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(dir));
        var s = new Mock<IAdStructureStore>();
        s.Setup(x => x.GetWriteCredentialAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cred);
        return new LdapAdWriter(d, s.Object, NullLogger<LdapAdWriter>.Instance);
    }

    [Fact]
    public async Task Unconfigured_service_account_fails_before_network()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            Writer(new DirectorySettings(true, "x.local", "127.0.0.1", null, false), new AdWriteCredential(AdWriteMode.ServiceAccount, null, null))
                .SetEnabledAsync("CN=u,DC=x", true));

    [Fact]
    public async Task Unreachable_dc_is_unavailable()
        => await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Writer(new DirectorySettings(true, "x.local", "127.0.0.1", null, false), new AdWriteCredential(AdWriteMode.ServiceAccount, "svc", "pw"))
                .SetEnabledAsync("CN=u,DC=x", true));

    private static (LdapAdWriter Writer, string Root) Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        return (Writer(new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false),
            new AdWriteCredential(AdWriteMode.ServiceAccount, E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"))), E("WINADMIN_TEST_AD_ROOT"));
    }

    [AdFact]
    public async Task Real_directory_group_lifecycle_inside_test_ou()
    {
        var (w, root) = Real();
        string cn = "sg_winadmin_test_" + Guid.NewGuid().ToString("N")[..8];
        string groupDn = await w.CreateGroupAsync(root, cn, "A:\\WinAdmin-Test;Full Access");
        Assert.StartsWith("CN=" + cn, groupDn);
        await w.ModifyAttributesAsync(groupDn, new Dictionary<string, string?> { ["info"] = "created by test" });
        var ex = await Assert.ThrowsAsync<AdWriteException>(() => w.CreateGroupAsync(root, cn, "dup"));
        Assert.Equal((int)ResultCode.EntryAlreadyExists, ex.Code);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdWriteRequestsTests|FullyQualifiedName~LdapAdWriterTests"`
Expected: FAIL — компиляция: нет `AdWriteRequests`, `LdapAdWriter`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/AdWriteRequests.cs
using System.DirectoryServices.Protocols;
using System.Text;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Значения LDAP для записи и перевод ошибок в тексты для оператора.</summary>
public static class AdWriteRequests
{
    private const int AccountDisable = 0x2;

    /// <summary>Глобальная группа безопасности (ADS_GROUP_TYPE_GLOBAL_GROUP | ADS_GROUP_TYPE_SECURITY_ENABLED).</summary>
    public static readonly string GlobalSecurityGroupType = unchecked((int)0x80000002).ToString();

    public static byte[] PasswordValue(string password) => Encoding.Unicode.GetBytes("\"" + password + "\"");

    public static int ToggleDisabled(int uac, bool enabled) => enabled ? uac & ~AccountDisable : uac | AccountDisable;

    public static string Rid(string sid) => sid[(sid.LastIndexOf('-') + 1)..];

    public static string Parent(string dn) => string.Join(",", DnUtils.Split(dn).Skip(1));

    public static string Rdn(string dn) => DnUtils.Split(dn)[0];

    public static Exception Translate(ResultCode code, string account, string operation, string target, string? serverMessage) => code switch
    {
        ResultCode.InsufficientAccessRights => new AdWriteException((int)code,
            $"Учётке {account} не хватает прав на {operation} для {target}. См. «Проверка окружения»."),
        ResultCode.NoSuchObject => new AdWriteException((int)code, "Объект не найден в AD."),
        ResultCode.EntryAlreadyExists => new AdWriteException((int)code, "Объект уже существует."),
        ResultCode.ConstraintViolation => new AdWriteException((int)code,
            "Значение не принято: пароль не соответствует политике домена (длина, сложность, история) или нарушено ограничение атрибута."),
        ResultCode.UnwillingToPerform => new AdWriteException((int)code, $"AD отклонил операцию «{operation}» для {target}."),
        ResultCode.Busy or ResultCode.Unavailable => new DirectoryUnavailableException("Контроллер домена недоступен"),
        _ => new AdWriteException((int)code, $"Ошибка AD при операции «{operation}» ({code})."),
    };
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/ActiveDirectory/LdapAdWriter.cs
using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Запись в AD учёткой записи по зашифрованному каналу. Детали ошибок — в лог, оператору — понятный текст.</summary>
public sealed class LdapAdWriter(IDirectorySettingsStore directory, IAdStructureStore structure, ILogger<LdapAdWriter> logger) : IAdWriter
{
    public Task ModifyAttributesAsync(string dn, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
        => RunAsync("изменение атрибутов", dn, c =>
        {
            var request = new ModifyRequest(dn);
            foreach (var (name, value) in changes)
                request.Modifications.Add(string.IsNullOrEmpty(value)
                    ? new DirectoryAttributeModification { Name = name, Operation = DirectoryAttributeOperation.Delete }
                    : Replace(name, value));
            Send(c, request, tolerate: [ResultCode.NoSuchAttribute]);
            return true;
        }, ct);

    public Task<bool> AddMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
        => RunAsync("изменение членства", groupDn, c =>
        {
            var mod = new DirectoryAttributeModification { Name = "member", Operation = DirectoryAttributeOperation.Add };
            mod.Add(memberDn);
            return Send(c, new ModifyRequest(groupDn, mod), tolerate: [ResultCode.EntryAlreadyExists, ResultCode.AttributeOrValueExists]);
        }, ct);

    public Task<bool> RemoveMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
        => RunAsync("изменение членства", groupDn, c =>
        {
            var mod = new DirectoryAttributeModification { Name = "member", Operation = DirectoryAttributeOperation.Delete };
            mod.Add(memberDn);
            return Send(c, new ModifyRequest(groupDn, mod), tolerate: [ResultCode.NoSuchAttribute, ResultCode.UnwillingToPerform]);
        }, ct);

    public Task SetPrimaryGroupAsync(string userDn, string groupSid, CancellationToken ct = default)
        => RunAsync("смену основной группы", userDn, c =>
        {
            Send(c, new ModifyRequest(userDn, DirectoryAttributeOperation.Replace, "primaryGroupID", AdWriteRequests.Rid(groupSid)));
            return true;
        }, ct);

    public Task SetEnabledAsync(string userDn, bool enabled, CancellationToken ct = default)
        => RunAsync(enabled ? "включение учётки" : "отключение учётки", userDn, c =>
        {
            var read = (SearchResponse)c.SendRequest(new SearchRequest(userDn, "(objectClass=*)", SearchScope.Base, "userAccountControl"));
            int uac = int.TryParse(read.Entries[0].Attributes["userAccountControl"]?[0] as string, out int v) ? v : 512;
            Send(c, new ModifyRequest(userDn, DirectoryAttributeOperation.Replace, "userAccountControl",
                AdWriteRequests.ToggleDisabled(uac, enabled).ToString()));
            return true;
        }, ct);

    public Task ResetPasswordAsync(string userDn, string password, bool mustChange, CancellationToken ct = default)
        => RunAsync("сброс пароля", userDn, c =>
        {
            var request = new ModifyRequest(userDn);
            var pwd = new DirectoryAttributeModification { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Replace };
            pwd.Add(AdWriteRequests.PasswordValue(password));
            request.Modifications.Add(pwd);
            if (mustChange) request.Modifications.Add(Replace("pwdLastSet", "0"));
            Send(c, request);
            return true;
        }, ct);

    public Task<string> MoveAsync(string dn, string targetOuDn, CancellationToken ct = default)
        => RunAsync("перенос", dn, c =>
        {
            string rdn = AdWriteRequests.Rdn(dn);
            Send(c, new ModifyDNRequest(dn, targetOuDn, rdn) { DeleteOldRdn = true });
            return rdn + "," + targetOuDn;
        }, ct);

    public Task<string> CreateGroupAsync(string ouDn, string cn, string description, CancellationToken ct = default)
        => RunAsync("создание группы", ouDn, c =>
        {
            string dn = $"CN={EscapeRdn(cn)},{ouDn}";
            var request = new AddRequest(dn,
                new DirectoryAttribute("objectClass", "group"),
                new DirectoryAttribute("sAMAccountName", cn),
                new DirectoryAttribute("groupType", AdWriteRequests.GlobalSecurityGroupType),
                new DirectoryAttribute("description", description));
            Send(c, request);
            return dn;
        }, ct);

    public Task SetPhotoAsync(string userDn, byte[]? photo, CancellationToken ct = default)
        => RunAsync(photo is null ? "удаление фото" : "загрузку фото", userDn, c =>
        {
            var mod = new DirectoryAttributeModification
            {
                Name = "thumbnailPhoto",
                Operation = photo is null ? DirectoryAttributeOperation.Delete : DirectoryAttributeOperation.Replace,
            };
            if (photo is not null) mod.Add(photo);
            Send(c, new ModifyRequest(userDn, mod), tolerate: photo is null ? [ResultCode.NoSuchAttribute] : []);
            return true;
        }, ct);

    private async Task<T> RunAsync<T>(string operation, string target, Func<LdapConnection, T> action, CancellationToken ct)
    {
        var dir = await directory.GetAsync(ct);
        if (!dir.Enabled || dir.Domain is null) throw new DirectoryUnavailableException("Подключение к домену выключено");
        var credential = await structure.GetWriteCredentialAsync(ct);
        var network = AdCredentials.ToNetwork(credential, dir.Domain);
        try
        {
            using var connection = LdapConnections.Open(dir, network);
            if (!dir.UseLdaps && !connection.SessionOptions.Sealing)
                throw new AdWriteException(0, "Канал к контроллеру домена не зашифрован — запись запрещена. См. «Проверка окружения».");
            return action(connection);
        }
        catch (DirectoryOperationException ex) when (ex.Response is { } response)
        {
            logger.LogWarning("AD: {Operation} для {Target} учёткой {Account}: {Code} {Message}",
                operation, target, credential, response.ResultCode, response.ErrorMessage);
            throw AdWriteRequests.Translate(response.ResultCode, credential.ToString(), operation, target, response.ErrorMessage);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            throw new AdWriteException(49, $"Учётка записи {credential} не может войти (неверный пароль, отключена или заблокирована).");
        }
        catch (LdapException ex)
        {
            logger.LogWarning(ex, "AD: {Operation} для {Target}: контроллер недоступен", operation, target);
            throw LdapConnections.Unavailable(ex);
        }
    }

    /// <summary>true — изменение выполнено; false — допустимый «уже так» (tolerate).</summary>
    private static bool Send(LdapConnection connection, DirectoryRequest request, ResultCode[]? tolerate = null)
    {
        try
        {
            connection.SendRequest(request);
            return true;
        }
        catch (DirectoryOperationException ex) when (ex.Response is { } r && tolerate?.Contains(r.ResultCode) == true)
        {
            return false;
        }
    }

    private static DirectoryAttributeModification Replace(string name, string value)
    {
        var mod = new DirectoryAttributeModification { Name = name, Operation = DirectoryAttributeOperation.Replace };
        mod.Add(value);
        return mod;
    }

    private static string EscapeRdn(string value)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=' || (i == 0 && ch is '#' or ' ') || (i == value.Length - 1 && ch == ' '))
                sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
```

Если у `ModifyRequest`-конструктора с `DirectoryAttributeOperation` другой порядок аргументов в .NET 10 — использовать вариант с `DirectoryAttributeModification` (как в `Replace`) и записать Ruling.

`DependencyInjection`:

```csharp
        services.AddSingleton<IAdWriter, LdapAdWriter>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdWriteRequestsTests|FullyQualifiedName~LdapAdWriterTests"`
Expected: PASS (интеграционный — Skip без переменных).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/LdapAdWriterTests.cs
git commit -m "feat(ad): LDAP writer over encrypted channel with translated errors"
```

---

### Task 6: Механизм проверки окружения

**Files:**
- Create: `src/backend/WinAdmin.Core/EnvironmentChecks/EnvironmentChecks.cs`
- Create: `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/EnvironmentService.cs`
- Create: `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/EnvironmentMonitor.cs`
- Modify: `src/backend/WinAdmin.Core/Security/PermissionIds.cs` (+ `PlatformEnvironmentCheck`)
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`, `src/backend/WinAdmin.Api/Program.cs`
- Test: `src/tests/WinAdmin.Tests/EnvironmentServiceTests.cs`

**Interfaces:**
- Consumes: `IModuleRegistry.GetState(id).Enabled`, `IAuditService.WriteAsync` (существующие)
- Produces:
  - `enum CheckStatus { Ok, Warning, Failed, Skipped }`, `enum CheckDepth { Quick, Full }`
  - `record CheckResult(string Code, string Title, CheckStatus Status, string Message, string? Fix = null)` + `Ok(...)`, `Warn(...)`, `Fail(...)`, `Skip(...)`
  - `interface IEnvironmentCheck { string ModuleId { get; } Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct); }`
  - `record EnvironmentReport(string ModuleId, DateTimeOffset At, CheckStatus Overall, IReadOnlyList<CheckResult> Results)`; `EnvironmentReport.Worst(IEnumerable<CheckResult>) → CheckStatus`
  - `EnvironmentService(IEnumerable<IEnvironmentCheck> checks, IModuleRegistry modules, IServiceScopeFactory scopes, TimeProvider? time = null, TimeSpan? timeout = null) : IEnvironmentService` (singleton); платформа — `ModuleId = "platform"`
  - `PermissionIds.PlatformEnvironmentCheck = "platform.environment.check"`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/EnvironmentServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.EnvironmentChecks;

namespace WinAdmin.Tests;

public sealed class EnvironmentServiceTests
{
    private sealed class Check(string module, Func<CheckDepth, CancellationToken, Task<IReadOnlyList<CheckResult>>> run) : IEnvironmentCheck
    {
        public string ModuleId => module;
        public Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct) => run(depth, ct);
    }

    private static Check Returns(string module, params CheckResult[] results)
        => new(module, (_, _) => Task.FromResult<IReadOnlyList<CheckResult>>(results));

    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IModuleRegistry> _modules = new();

    private EnvironmentService Service(params IEnvironmentCheck[] checks)
    {
        var sp = new ServiceCollection().AddSingleton(_audit.Object).BuildServiceProvider();
        _modules.Setup(m => m.GetState(It.IsAny<string>())).Returns((string id) =>
            new ModuleState(id != "off", true, null, new Dictionary<string, object?>()));
        return new EnvironmentService(checks, _modules.Object, sp.GetRequiredService<IServiceScopeFactory>(),
            timeout: TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task Overall_is_the_worst_status_and_disabled_modules_are_skipped()
    {
        var svc = Service(
            Returns("platform", CheckResult.Ok("a", "A", "ok"), CheckResult.Warn("b", "B", "hmm")),
            Returns("ad-users", CheckResult.Fail("c", "C", "bad", "fix it")),
            Returns("off", CheckResult.Ok("d", "D", "ok")));

        var reports = await svc.RunAsync(null, CheckDepth.Quick);

        Assert.Equal(CheckStatus.Warning, reports.Single(r => r.ModuleId == "platform").Overall);
        Assert.Equal(CheckStatus.Failed, reports.Single(r => r.ModuleId == "ad-users").Overall);
        Assert.DoesNotContain(reports, r => r.ModuleId == "off");
        Assert.Equal(reports.Count, svc.Latest.Count);
    }

    [Fact]
    public async Task Explicit_module_runs_even_when_disabled()
        => Assert.Single(await Service(Returns("off", CheckResult.Ok("d", "D", "ok"))).RunAsync("off", CheckDepth.Quick));

    [Fact]
    public async Task Throwing_and_hanging_checks_do_not_break_others()
    {
        var svc = Service(
            new Check("platform", (_, _) => throw new InvalidOperationException("boom")),
            new Check("ad-users", async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return []; }),
            Returns("ad-folders", CheckResult.Ok("x", "X", "ok")));

        var reports = await svc.RunAsync(null, CheckDepth.Quick);

        Assert.Equal(CheckStatus.Failed, reports.Single(r => r.ModuleId == "platform").Overall);
        Assert.Contains("boom", reports.Single(r => r.ModuleId == "platform").Results[0].Message);
        Assert.Contains("время", reports.Single(r => r.ModuleId == "ad-users").Results[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CheckStatus.Ok, reports.Single(r => r.ModuleId == "ad-folders").Overall);
    }

    [Fact]
    public async Task Status_change_is_audited_once()
    {
        var status = CheckStatus.Ok;
        var svc = Service(new Check("platform", (_, _) =>
            Task.FromResult<IReadOnlyList<CheckResult>>([new CheckResult("a", "A", status, "m")])));

        await svc.RunAsync(null, CheckDepth.Quick);   // первый результат: Ok — записываем
        await svc.RunAsync(null, CheckDepth.Quick);   // без изменений — нет
        status = CheckStatus.Failed;
        await svc.RunAsync(null, CheckDepth.Quick);   // Ok → Failed — записываем

        _audit.Verify(a => a.WriteAsync(It.Is<AuditEntryDto>(e => e.Action == "environment.status"), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Worst_ignores_skipped()
        => Assert.Equal(CheckStatus.Ok, EnvironmentReport.Worst([CheckResult.Ok("a", "A", ""), CheckResult.Skip("b", "B", "")]));
}
```

Перед запуском проверить фактическую сигнатуру `ModuleState` (`grep -n "record ModuleState" -r src/backend`) и подставить её конструктор в мок.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~EnvironmentServiceTests"`
Expected: FAIL — компиляция: нет `EnvironmentService` (а `EnvironmentChecks.cs` создан в Task 3 — если нет, создать сейчас).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Core/EnvironmentChecks/EnvironmentChecks.cs
namespace WinAdmin.Core.EnvironmentChecks;

public enum CheckStatus { Ok, Warning, Failed, Skipped }

public enum CheckDepth { Quick, Full }

/// <summary>Результат проверки: Fix — что сделать администратору.</summary>
public sealed record CheckResult(string Code, string Title, CheckStatus Status, string Message, string? Fix = null)
{
    public static CheckResult Ok(string code, string title, string message) => new(code, title, CheckStatus.Ok, message);
    public static CheckResult Warn(string code, string title, string message, string? fix = null) => new(code, title, CheckStatus.Warning, message, fix);
    public static CheckResult Fail(string code, string title, string message, string? fix = null) => new(code, title, CheckStatus.Failed, message, fix);
    public static CheckResult Skip(string code, string title, string reason) => new(code, title, CheckStatus.Skipped, reason);
}

/// <summary>Проверки окружения модуля («platform» — ядро).</summary>
public interface IEnvironmentCheck
{
    string ModuleId { get; }
    Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct);
}

public sealed record EnvironmentReport(string ModuleId, DateTimeOffset At, CheckStatus Overall, IReadOnlyList<CheckResult> Results)
{
    public static CheckStatus Worst(IEnumerable<CheckResult> results)
    {
        var statuses = results.Select(r => r.Status).Where(s => s != CheckStatus.Skipped).ToList();
        if (statuses.Contains(CheckStatus.Failed)) return CheckStatus.Failed;
        if (statuses.Contains(CheckStatus.Warning)) return CheckStatus.Warning;
        return CheckStatus.Ok;
    }
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/EnvironmentChecks/EnvironmentService.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Запуск проверок окружения: «platform» — всегда, модули — если включены (или запрошены явно).</summary>
public sealed class EnvironmentService(
    IEnumerable<IEnvironmentCheck> checks, IModuleRegistry modules, IServiceScopeFactory scopes,
    TimeProvider? time = null, TimeSpan? timeout = null) : IEnvironmentService
{
    public const string Platform = "platform";
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, EnvironmentReport> _latest = new();

    public IReadOnlyList<EnvironmentReport> Latest => _latest.Values.OrderBy(r => r.ModuleId == Platform ? "" : r.ModuleId).ToList();

    public async Task<IReadOnlyList<EnvironmentReport>> RunAsync(string? moduleId, CheckDepth depth, CancellationToken ct = default)
    {
        var selected = checks.Where(c => moduleId is null
                ? c.ModuleId == Platform || modules.GetState(c.ModuleId).Enabled
                : c.ModuleId == moduleId)
            .GroupBy(c => c.ModuleId);

        var reports = await Task.WhenAll(selected.Select(group => RunModuleAsync(group.Key, group, depth, ct)));
        foreach (var report in reports) await RememberAsync(report, ct);
        return reports.OrderBy(r => r.ModuleId == Platform ? "" : r.ModuleId).ToList();
    }

    private async Task<EnvironmentReport> RunModuleAsync(string moduleId, IEnumerable<IEnvironmentCheck> group, CheckDepth depth, CancellationToken ct)
    {
        var results = new List<CheckResult>();
        foreach (var check in group)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(depth == CheckDepth.Full ? _timeout * 10 : _timeout);
            try
            {
                results.AddRange(await check.RunAsync(depth, cts.Token).WaitAsync(cts.Token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                results.Add(CheckResult.Fail($"{moduleId}.timeout", "Проверка", "Проверка не уложилась во время ожидания"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(CheckResult.Fail($"{moduleId}.error", "Проверка", "Проверка не выполнилась: " + ex.Message));
            }
        }
        return new EnvironmentReport(moduleId, _time.GetUtcNow(), EnvironmentReport.Worst(results), results);
    }

    private async Task RememberAsync(EnvironmentReport report, CancellationToken ct)
    {
        _latest.TryGetValue(report.ModuleId, out var previous);
        _latest[report.ModuleId] = report;
        if (previous?.Overall == report.Overall) return;
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
        {
            Actor = "system", Action = "environment.status", Target = report.ModuleId, Success = report.Overall != CheckStatus.Failed,
            Details = $"{previous?.Overall.ToString() ?? "—"} → {report.Overall}",
        }, ct);
    }
}
```

```csharp
// src/backend/WinAdmin.Infrastructure/EnvironmentChecks/EnvironmentMonitor.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Фон: быстрые проверки через минуту после старта и далее раз в час.</summary>
public sealed class EnvironmentMonitor(IEnvironmentService environment, ILogger<EnvironmentMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await environment.RunAsync(null, CheckDepth.Quick, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Фоновая проверка окружения не выполнилась"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

`PermissionIds`:

```csharp
    public const string PlatformEnvironmentCheck = "platform.environment.check";
```

и в список `Platform`:

```csharp
        new(PlatformEnvironmentCheck, "Проверка окружения", "Готовность домена, учётки записи и модулей"),
```

`DependencyInjection.AddWinAdminInfrastructure`:

```csharp
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
```

`Program.cs` — только для режима веб-службы (не в CLI-ветке), рядом с прочей регистрацией:

```csharp
builder.Services.AddHostedService<EnvironmentMonitor>();
```

В `NetworkApiFactory` (тесты) удалить монитор, чтобы фон не мешал: `services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>()` — **нельзя** (там могут быть другие); вместо этого `services.Remove(services.Single(d => d.ImplementationType == typeof(EnvironmentMonitor)))`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~EnvironmentServiceTests|FullyQualifiedName~Permission"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(environment): environment check service, hourly monitor, permission"
```

---

### Task 7: Платформенные проверки AD

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/EnvironmentChecks/PlatformAdCheck.cs`
- Create: `src/tests/WinAdmin.Tests/Fakes/FakeAdReader.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Test: `src/tests/WinAdmin.Tests/PlatformAdCheckTests.cs`

**Interfaces:**
- Consumes: `IDirectorySettingsStore`, `IDirectoryService.TestConnectionAsync` (1c); `IAdStructureStore` (Task 3); `IAdReader` (Task 4); `IEnvironmentCheck`, `CheckResult` (Task 6)
- Produces: `PlatformAdCheck(IDirectorySettingsStore directory, IDirectoryService directoryService, IAdStructureStore structure, IAdReader reader, TimeProvider? time = null) : IEnvironmentCheck` (`ModuleId = "platform"`), коды `ad.directory`, `ad.root`, `ad.writer`, `ad.channel`; `FakeAdReader` (тесты): `Projects`, `ExistingDns`, `Writer`, `Effective`, `Down`

- [ ] **Step 1: Write the failing test**

```csharp
// src/tests/WinAdmin.Tests/Fakes/FakeAdReader.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Tests.Fakes;

public sealed class FakeAdReader : IAdReader
{
    public List<AdProject> Projects { get; } = [];
    public HashSet<string> ExistingDns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public WriterStatus Writer { get; set; } = new("PCS\\svc", true, null, true, false, null, true);
    public Dictionary<string, EffectiveRights> Effective { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Down { get; set; }

    private void ThrowIfDown()
    {
        if (Down) throw new DirectoryUnavailableException("Контроллер домена недоступен");
    }

    public Task<IReadOnlyList<AdProject>> ListProjectsAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<AdProject>>(Projects.ToList());
    }

    public Task<bool> ExistsAsync(string dn, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(ExistingDns.Contains(dn));
    }

    public Task<EffectiveRights> ReadEffectiveAsync(string dn, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(Effective.TryGetValue(dn, out var r) ? r
            : new EffectiveRights(new HashSet<string>(), new HashSet<string>()));
    }

    public Task<WriterStatus> GetWriterStatusAsync(CancellationToken ct = default) => Task.FromResult(Writer);
}
```

```csharp
// src/tests/WinAdmin.Tests/PlatformAdCheckTests.cs
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class PlatformAdCheckTests
{
    private const string Root = "OU=Accounts,DC=pcs";
    private readonly FakeDirectory _directory = new();
    private readonly FakeAdReader _reader = new();
    private DirectorySettings _dirSettings = new(true, "pcs", null, null, false);
    private AdStructureSettings _structure = AdStructureSettings.Default with
    {
        RootOu = Root, HiddenOus = ["IT"], WriteLogin = "PCS\\svc", HasWritePassword = true,
    };
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

    public PlatformAdCheckTests()
    {
        _reader.ExistingDns.Add(Root);
        _reader.ExistingDns.Add("OU=IT," + Root);
        _reader.Projects.Add(new AdProject("OU=A," + Root, "A"));
    }

    private async Task<Dictionary<string, CheckResult>> RunAsync()
    {
        var dir = new Mock<IDirectorySettingsStore>();
        dir.Setup(d => d.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _dirSettings);
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _structure);
        var results = await new PlatformAdCheck(dir.Object, _directory, st.Object, _reader, _time).RunAsync(CheckDepth.Quick, default);
        return results.ToDictionary(r => r.Code);
    }

    [Fact]
    public async Task Healthy_environment_is_all_ok()
    {
        var r = await RunAsync();
        Assert.All(r.Values, x => Assert.Equal(CheckStatus.Ok, x.Status));
        Assert.Contains("1", r["ad.root"].Message); // число проектов
    }

    [Fact]
    public async Task Directory_off_fails_and_dependents_are_skipped()
    {
        _dirSettings = DirectorySettings.Disabled;
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.directory"].Status);
        Assert.NotNull(r["ad.directory"].Fix);
        Assert.Equal(CheckStatus.Skipped, r["ad.root"].Status);
        Assert.Equal(CheckStatus.Skipped, r["ad.writer"].Status);
        Assert.Equal(CheckStatus.Skipped, r["ad.channel"].Status);
    }

    [Fact]
    public async Task Missing_root_fails_with_fix()
    {
        _structure = _structure with { RootOu = null };
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.root"].Status);
        Assert.Contains("Active Directory", r["ad.root"].Fix);
    }

    [Fact]
    public async Task Missing_hidden_ou_is_a_warning()
    {
        _reader.ExistingDns.Remove("OU=IT," + Root);
        Assert.Equal(CheckStatus.Warning, (await RunAsync())["ad.root"].Status);
    }

    [Fact]
    public async Task Writer_problems()
    {
        _reader.Writer = new("PCS\\svc", false, "Неверный логин или пароль", null, null, null, false);
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.writer"].Status);
        Assert.Equal(CheckStatus.Skipped, r["ad.channel"].Status);

        _reader.Writer = new("PCS\\svc", true, null, true, false, _time.GetUtcNow().AddDays(5), true);
        Assert.Equal(CheckStatus.Warning, (await RunAsync())["ad.writer"].Status);

        _reader.Writer = new("PCS\\svc", true, null, false, false, null, true);
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["ad.writer"].Status);
    }

    [Fact]
    public async Task Unencrypted_channel_fails()
    {
        _reader.Writer = _reader.Writer with { Encrypted = false };
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["ad.channel"].Status);
    }

    [Fact]
    public async Task Undecryptable_password_is_reported()
    {
        _reader.Writer = new("?", false, "Пароль учётки записи не расшифровывается (ключ шифрования изменён или повреждён) — задайте его заново", null, null, null, false);
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.writer"].Status);
        Assert.Contains("расшифровывается", r["ad.writer"].Message);
    }

    [Fact]
    public async Task Service_account_without_password_fails_without_bind()
    {
        _structure = _structure with { HasWritePassword = false };
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.writer"].Status);
        Assert.Contains("пароль", r["ad.writer"].Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~PlatformAdCheckTests"`
Expected: FAIL — компиляция: нет `PlatformAdCheck`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Infrastructure/EnvironmentChecks/PlatformAdCheck.cs
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Готовность общего слоя AD: домен, корневая OU, учётка записи, шифрование канала.</summary>
public sealed class PlatformAdCheck(
    IDirectorySettingsStore directory, IDirectoryService directoryService, IAdStructureStore structure,
    IAdReader reader, TimeProvider? time = null) : IEnvironmentCheck
{
    private const string SettingsFix = "Настройки → Active Directory";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string ModuleId => EnvironmentService.Platform;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
    {
        var results = new List<CheckResult>();

        // 1. Подключение к домену.
        var dir = await directory.GetAsync(ct);
        CheckResult domain;
        if (!dir.Enabled)
            domain = CheckResult.Fail("ad.directory", "Подключение к домену", "Подключение к домену выключено",
                "Настройки → Подключение к домену: включите и укажите домен");
        else
        {
            var steps = await directoryService.TestConnectionAsync(ct);
            var failed = steps.FirstOrDefault(s => !s.Ok);
            domain = failed is null
                ? CheckResult.Ok("ad.directory", "Подключение к домену", $"Домен {dir.Domain}: контроллер отвечает")
                : CheckResult.Fail("ad.directory", "Подключение к домену", $"{failed.Name}: {failed.Message}",
                    "Настройки → Подключение к домену → «Проверить»");
        }
        results.Add(domain);
        if (domain.Status == CheckStatus.Failed)
        {
            results.Add(CheckResult.Skip("ad.root", "Корневая OU", "Нет подключения к домену"));
            results.Add(CheckResult.Skip("ad.writer", "Учётка записи", "Нет подключения к домену"));
            results.Add(CheckResult.Skip("ad.channel", "Шифрование канала", "Нет подключения к домену"));
            return results;
        }

        // 2. Корневая OU и проекты.
        var st = await structure.GetAsync(ct);
        results.Add(await RootAsync(st, ct));

        // 3. Учётка записи.
        CheckResult writer;
        WriterStatus? status = null;
        if (st.WriteMode == AdWriteMode.ServiceAccount && (st.WriteLogin is null || !st.HasWritePassword))
            writer = CheckResult.Fail("ad.writer", "Учётка записи", "Служебная учётка не задана: нужен логин и пароль",
                SettingsFix + ": укажите служебную учётку или выберите «Учётка службы»");
        else
        {
            status = await reader.GetWriterStatusAsync(ct);
            writer = Writer(status);
        }
        results.Add(writer);

        // 4. Шифрование канала записи.
        results.Add(status is { Bound: true }
            ? status.Encrypted
                ? CheckResult.Ok("ad.channel", "Шифрование канала", dir.UseLdaps ? "LDAPS (636)" : "LDAP 389 с подписью и шифрованием")
                : CheckResult.Fail("ad.channel", "Шифрование канала", "Канал не зашифрован — AD не позволит менять пароли",
                    "Включите LDAPS (Настройки → Подключение к домену) или проверьте Kerberos между сервером и DC")
            : CheckResult.Skip("ad.channel", "Шифрование канала", "Учётка записи не вошла"));
        return results;
    }

    private async Task<CheckResult> RootAsync(AdStructureSettings st, CancellationToken ct)
    {
        if (st.RootOu is null)
            return CheckResult.Fail("ad.root", "Корневая OU", "Корневая OU не задана", SettingsFix + ": укажите DN корневой OU (например OU=Accounts,DC=…)");
        if (!await reader.ExistsAsync(st.RootOu, ct))
            return CheckResult.Fail("ad.root", "Корневая OU", $"OU не найдена: {st.RootOu}", SettingsFix + ": проверьте DN");

        var projects = await reader.ListProjectsAsync(false, ct);
        var missingHidden = new List<string>();
        foreach (var hidden in st.HiddenOus)
            if (!await reader.ExistsAsync($"OU={hidden},{st.RootOu}", ct)) missingHidden.Add(hidden);

        string message = $"{st.RootOu}: проектов {projects.Count}";
        return missingHidden.Count > 0
            ? CheckResult.Warn("ad.root", "Корневая OU", message + $"; скрытые OU не найдены: {string.Join(", ", missingHidden)}",
                SettingsFix + ": уберите лишние скрытые OU")
            : CheckResult.Ok("ad.root", "Корневая OU", message);
    }

    private CheckResult Writer(WriterStatus s)
    {
        if (!s.Bound)
            return CheckResult.Fail("ad.writer", "Учётка записи", s.Error ?? "Вход не выполнен", SettingsFix + ": проверьте логин и пароль");
        if (s.Enabled == false)
            return CheckResult.Fail("ad.writer", "Учётка записи", $"{s.Account}: учётка отключена в AD", "Включите учётку в AD");
        if (s.Locked == true)
            return CheckResult.Fail("ad.writer", "Учётка записи", $"{s.Account}: учётка заблокирована", "Разблокируйте учётку в AD");
        if (s.PasswordExpires is { } expires && expires - _time.GetUtcNow() < TimeSpan.FromDays(14))
            return CheckResult.Warn("ad.writer", "Учётка записи", $"{s.Account}: пароль истекает {expires:dd.MM.yyyy}",
                "Смените пароль в AD и обновите его в " + SettingsFix);
        return CheckResult.Ok("ad.writer", "Учётка записи", $"Вход выполнен: {s.Account}");
    }
}
```

`DependencyInjection`:

```csharp
        services.AddSingleton<IEnvironmentCheck, PlatformAdCheck>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~PlatformAdCheckTests"`
Expected: PASS (8 тестов).

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(environment): platform AD checks (domain, root OU, write account, channel)"
```

---

### Task 8: API — настройки AD и проверка окружения

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/AdStructureController.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/EnvironmentController.cs`
- Modify: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs` (фабрика: `FakeAdReader`, без монитора)
- Test: `src/tests/WinAdmin.Tests/AdStructureApiTests.cs`

**Interfaces:**
- Consumes: `IAdStructureStore`, `IEnvironmentService`, `IAuditService`, `PermissionIds.PlatformDirectoryManage`, `PlatformEnvironmentCheck`
- Produces:
  - `GET /api/v1/settings/ad` → `{ rootOu, usersOuName, hiddenOus, writeMode, writeLogin, hasWritePassword }`
  - `PUT /api/v1/settings/ad` `{ rootOu, usersOuName, hiddenOus, writeMode, writeLogin, writePassword? }` → то же; 400 при неверном DN; аудит `settings.ad` (без пароля, «пароль изменён»)
  - `GET /api/v1/environment?module=&depth=quick|full` → `EnvironmentReport[]`
  - `GET /api/v1/environment/latest` → `EnvironmentReport[]`
  - `NetworkApiFactory.AdReader` (`FakeAdReader`)

- [ ] **Step 1: Write the failing test**

В `NetworkApiFactory`:

```csharp
    public FakeAdReader AdReader { get; } = new();
```

в `ConfigureTestServices`:

```csharp
            services.RemoveAll<IAdReader>();
            services.AddSingleton<IAdReader>(AdReader);
            var monitor = services.SingleOrDefault(d => d.ImplementationType == typeof(WinAdmin.Infrastructure.EnvironmentChecks.EnvironmentMonitor));
            if (monitor is not null) services.Remove(monitor);
```

```csharp
// src/tests/WinAdmin.Tests/AdStructureApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class AdStructureApiTests(NetworkApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Settings_require_directory_permission()
    {
        var other = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/settings/ad")).StatusCode);
    }

    [Fact]
    public async Task Password_is_write_only_and_kept_when_omitted()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
        var put = await client.PutAsJsonAsync("/api/v1/settings/ad", new
        {
            rootOu = "OU=Accounts,DC=test,DC=local", usersOuName = "Users", hiddenOus = new[] { "IT" },
            writeMode = "ServiceAccount", writeLogin = "TEST\\svc", writePassword = "Svc-Pass-123!",
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        string body = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Svc-Pass-123!", body);

        var again = await client.PutAsJsonAsync("/api/v1/settings/ad", new
        {
            rootOu = "OU=Accounts,DC=test,DC=local", usersOuName = "Staff", hiddenOus = Array.Empty<string>(),
            writeMode = "ServiceAccount", writeLogin = "TEST\\svc",
        });
        var json = await again.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.True(json.GetProperty("hasWritePassword").GetBoolean());
        Assert.Equal("Staff", json.GetProperty("usersOuName").GetString());
    }

    [Fact]
    public async Task Invalid_root_is_400()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
        var r = await client.PutAsJsonAsync("/api/v1/settings/ad", new { rootOu = "DC=test", usersOuName = "Users", hiddenOus = Array.Empty<string>(), writeMode = "ProcessAccount" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Environment_check_returns_platform_report()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformEnvironmentCheck);
        var reports = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/environment?depth=quick", Web);
        var platform = Assert.Single(reports!, r => r.GetProperty("moduleId").GetString() == "platform");
        Assert.Contains(platform.GetProperty("results").EnumerateArray(), r => r.GetProperty("code").GetString() == "ad.directory");
        Assert.NotEmpty((await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/environment/latest", Web))!);

        var noRights = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRights.GetAsync("/api/v1/environment")).StatusCode);
    }
}
```

Статусы и режимы сериализуются строками: проверить, что в `Program.cs` для MVC подключён `JsonStringEnumConverter`; если нет — добавить `[JsonConverter(typeof(JsonStringEnumConverter))]` на `CheckStatus`, `CheckDepth`, `AdWriteMode` (в Core) и записать Ruling.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdStructureApiTests"`
Expected: FAIL — 404 на `/api/v1/settings/ad` и `/api/v1/environment`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/backend/WinAdmin.Api/Controllers/AdStructureController.cs
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed record SaveAdStructureRequest(
    string? RootOu, string? UsersOuName, List<string>? HiddenOus, AdWriteMode WriteMode, string? WriteLogin, string? WritePassword);

[PlatformErrors]
[RequirePermission(PermissionIds.PlatformDirectoryManage)]
[Route("/api/v1/settings/ad")]
public sealed class AdStructureController(IAdStructureStore store, IAuditService audit) : WinAdminControllerBase
{
    [HttpGet]
    public Task<AdStructureSettings> Get(CancellationToken ct) => store.GetAsync(ct);

    [HttpPut]
    public async Task<AdStructureSettings> Put([FromBody] SaveAdStructureRequest request, CancellationToken ct)
    {
        var settings = new AdStructureSettings(request.RootOu, request.UsersOuName ?? "Users", request.HiddenOus ?? [],
            request.WriteMode, request.WriteLogin);
        await store.SaveAsync(settings, request.WritePassword, ct);
        var saved = await store.GetAsync(ct);
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "settings.ad", Success = true, SourceIp = SourceIp,
            Details = $"root={saved.RootOu}, users={saved.UsersOuName}, hidden=[{string.Join(", ", saved.HiddenOus)}], " +
                      $"mode={saved.WriteMode}, login={saved.WriteLogin}" + (request.WritePassword is null ? "" : ", пароль изменён"),
        }, ct);
        return saved;
    }
}
```

```csharp
// src/backend/WinAdmin.Api/Controllers/EnvironmentController.cs
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformEnvironmentCheck)]
[Route("/api/v1/environment")]
public sealed class EnvironmentController(IEnvironmentService environment) : WinAdminControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EnvironmentReport>> Run([FromQuery] string? module, [FromQuery] string? depth, CancellationToken ct)
        => environment.RunAsync(string.IsNullOrWhiteSpace(module) ? null : module,
            string.Equals(depth, "full", StringComparison.OrdinalIgnoreCase) ? CheckDepth.Full : CheckDepth.Quick, ct);

    [HttpGet("latest")]
    public IReadOnlyList<EnvironmentReport> Latest() => environment.Latest;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~AdStructureApiTests"` и затем полный набор `dotnet test src/tests/WinAdmin.Tests`
Expected: PASS; полный набор — без регрессий.

- [ ] **Step 5: Commit**

```bash
git add src/backend src/tests
git commit -m "feat(api): AD structure settings and environment check endpoints"
```

---

### Task 9: Интерфейс — «Active Directory: управление» и «Проверка окружения»

**Files:**
- Modify: `src/frontend/src/api/types.ts`, `src/frontend/src/api/client.ts`
- Create: `src/frontend/src/components/AdStructureCard.tsx`
- Modify: `src/frontend/src/pages/Settings.tsx`
- Create: `src/frontend/src/pages/Environment.tsx`
- Modify: `src/frontend/src/App.tsx`, `src/frontend/src/components/AppLayout.tsx`

**Interfaces:**
- Consumes: API Task 8
- Produces: `api.adStructure.{get, save}`, `api.environment.{run, latest}`; маршрут `/cp/environment` (право `platform.environment.check`)

Frontend-тестов в репозитории нет (Ruling 1b) — проверка: `npm run build`, `npm run lint`, браузер.

- [ ] **Step 1: Типы и API**

`types.ts`:

```ts
export type AdWriteMode = 'ServiceAccount' | 'ProcessAccount'

export interface AdStructureSettings {
  rootOu: string | null
  usersOuName: string
  hiddenOus: string[]
  writeMode: AdWriteMode
  writeLogin: string | null
  hasWritePassword: boolean
}

export interface SaveAdStructure extends Omit<AdStructureSettings, 'hasWritePassword'> {
  writePassword?: string | null
}

export type CheckStatus = 'Ok' | 'Warning' | 'Failed' | 'Skipped'

export interface CheckResult { code: string; title: string; status: CheckStatus; message: string; fix: string | null }

export interface EnvironmentReport { moduleId: string; at: string; overall: CheckStatus; results: CheckResult[] }
```

`client.ts` — в `api`:

```ts
  adStructure: {
    get: () => http.get<AdStructureSettings>('/settings/ad').then((r) => r.data),
    save: (s: SaveAdStructure) => http.put<AdStructureSettings>('/settings/ad', s).then((r) => r.data),
  },
  environment: {
    run: (depth: 'quick' | 'full' = 'quick', module?: string) =>
      http.get<EnvironmentReport[]>('/environment', { params: { depth, module } }).then((r) => r.data),
    latest: () => http.get<EnvironmentReport[]>('/environment/latest').then((r) => r.data),
  },
```

(добавить типы в `import type` в начале `client.ts`.)

- [ ] **Step 2: Карточка настроек AD**

```tsx
// src/frontend/src/components/AdStructureCard.tsx
import { useEffect, useState } from 'react'
import { App, Button, Card, Form, Input, Radio, Select, Space, Typography } from 'antd'
import { api } from '../api/client'
import type { AdStructureSettings, SaveAdStructure } from '../api/types'

export default function AdStructureCard() {
  const { message } = App.useApp()
  const [form] = Form.useForm<SaveAdStructure>()
  const [current, setCurrent] = useState<AdStructureSettings>()
  const [saving, setSaving] = useState(false)
  const mode = Form.useWatch('writeMode', form)

  useEffect(() => {
    api.adStructure.get().then((s) => { setCurrent(s); form.setFieldsValue(s) })
      .catch(() => message.error('Не удалось загрузить настройки AD'))
  }, [form, message])

  const save = async (values: SaveAdStructure) => {
    setSaving(true)
    try {
      const payload = { ...values, writePassword: values.writePassword ? values.writePassword : null }
      const saved = await api.adStructure.save(payload)
      setCurrent(saved)
      form.setFieldsValue({ ...saved, writePassword: undefined })
      message.success('Настройки AD сохранены')
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card title="Active Directory: управление" className="sp-glass">
      <Typography.Paragraph type="secondary">
        Структура каталога и учётка, которой модули «Пользователи AD» и «Папки» вносят изменения.
        Проекты — подразделения первого уровня под корневой OU.
      </Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item name="rootOu" label="Корневая OU (DN)"><Input placeholder="OU=Accounts,DC=pcs-msk,DC=com" /></Form.Item>
        <Form.Item name="usersOuName" label="OU пользователей внутри проекта"><Input placeholder="Users" /></Form.Item>
        <Form.Item name="hiddenOus" label="Скрытые OU (первый уровень)">
          <Select mode="tags" tokenSeparators={[',']} placeholder="IT, Others" />
        </Form.Item>
        <Form.Item name="writeMode" label="Учётка для записи в AD">
          <Radio.Group>
            <Radio value="ServiceAccount">Служебная учётка</Radio>
            <Radio value="ProcessAccount">Учётка службы WinAdmin</Radio>
          </Radio.Group>
        </Form.Item>
        {mode !== 'ProcessAccount' && (
          <>
            <Form.Item name="writeLogin" label="Логин"><Input placeholder="PCS\svc-winadmin" /></Form.Item>
            <Form.Item name="writePassword" label={current?.hasWritePassword ? 'Пароль (задан — введите, чтобы заменить)' : 'Пароль'}>
              <Input.Password autoComplete="new-password" />
            </Form.Item>
          </>
        )}
        {mode === 'ProcessAccount' && (
          <Typography.Paragraph type="warning">
            На контроллере домена учётка службы имеет права администратора домена — предпочтительна служебная учётка с делегированием.
          </Typography.Paragraph>
        )}
        <Space><Button type="primary" htmlType="submit" loading={saving}>Сохранить</Button></Space>
      </Form>
    </Card>
  )
}
```

`Settings.tsx` — рядом с `DirectorySettingsCard`:

```tsx
      {can('platform.directory.manage') && <AdStructureCard />}
```

(+ `import AdStructureCard from '../components/AdStructureCard'`.)

- [ ] **Step 3: Страница «Проверка окружения»**

```tsx
// src/frontend/src/pages/Environment.tsx
import { useEffect, useState } from 'react'
import { App, Button, Card, Empty, Space, Table, Tag, Typography } from 'antd'
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import type { CheckResult, CheckStatus, EnvironmentReport } from '../api/types'
import PageHeader from '../components/PageHeader'

const statusTag: Record<CheckStatus, { color: string; text: string }> = {
  Ok: { color: 'green', text: 'ок' },
  Warning: { color: 'gold', text: 'внимание' },
  Failed: { color: 'red', text: 'ошибка' },
  Skipped: { color: 'default', text: 'пропущено' },
}

const moduleTitle = (id: string) => ({ platform: 'Платформа (AD)', 'ad-users': 'Пользователи AD', 'ad-folders': 'Папки' } as Record<string, string>)[id] ?? id

export default function Environment() {
  const { message } = App.useApp()
  const [reports, setReports] = useState<EnvironmentReport[]>([])
  const [running, setRunning] = useState<'quick' | 'full'>()

  useEffect(() => { api.environment.latest().then(setReports).catch(() => {}) }, [])

  const run = async (depth: 'quick' | 'full') => {
    setRunning(depth)
    try { setReports(await api.environment.run(depth)) }
    catch { message.error('Проверка не выполнена') }
    finally { setRunning(undefined) }
  }

  return (
    <>
      <PageHeader title="Проверка окружения" subtitle="Готовность домена, учётки записи и модулей"
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} loading={running === 'quick'} onClick={() => run('quick')}>Быстрая проверка</Button>
            <Button icon={<SearchOutlined />} loading={running === 'full'} onClick={() => run('full')}>Полная (с NTFS)</Button>
          </Space>
        } />
      {reports.length === 0 ? <Empty description="Проверка ещё не запускалась" /> : reports.map((r) => (
        <Card key={r.moduleId} className="sp-glass" style={{ marginBottom: 16 }}
          title={<Space>{moduleTitle(r.moduleId)}<Tag color={statusTag[r.overall].color}>{statusTag[r.overall].text}</Tag></Space>}
          extra={<Typography.Text type="secondary">{new Date(r.at).toLocaleString('ru-RU')}</Typography.Text>}>
          <Table<CheckResult> rowKey="code" size="small" pagination={false} dataSource={r.results}
            columns={[
              { title: 'Проверка', dataIndex: 'title', width: 200 },
              { title: 'Статус', dataIndex: 'status', width: 110, render: (s: CheckStatus) => <Tag color={statusTag[s].color}>{statusTag[s].text}</Tag> },
              { title: 'Результат', dataIndex: 'message' },
              { title: 'Что сделать', dataIndex: 'fix', render: (f: string | null) => f ?? '' },
            ]} />
        </Card>
      ))}
    </>
  )
}
```

`App.tsx` — маршрут (по образцу `/cp/modules` с `Guard`): `/cp/environment` → `<Environment />` с `perm="platform.environment.check"`. `AppLayout.tsx` — пункт меню «Проверка окружения» в разделе администрирования с `perm: 'platform.environment.check'`, иконка `SafetyOutlined` (по образцу пунктов «Модули»/«Роли»).

- [ ] **Step 4: Сборка и проверка в браузере**

Run: `cd src/frontend && npm run build && npm run lint` → Expected: сборка без ошибок; в изменённых файлах нет предупреждений. Затем `git checkout -- src/backend/WinAdmin.Api/wwwroot/assets/index-nDTDN0K1.css`.

Браузер (локальный экземпляр на временных данных, порт 18181, как в 1c): «Настройки» → карточка AD: сохранить корень `OU=Test,DC=example,DC=com`, служебная учётка с паролем → поле пароля пустое, подпись «задан»; повторное сохранение без пароля — «задан» остаётся. «Проверка окружения» → «Быстрая проверка» → карточка «Платформа (AD)»: `ad.directory` — ошибка (машина не в домене) с подсказкой, остальные — «пропущено».

- [ ] **Step 5: Commit**

```bash
git add src/frontend
git commit -m "feat(ui): AD management settings card and environment check page"
```

---

### Task 10: Документация, выкатка на DC, проверка

**Files:**
- Modify: `docs/02-security.md`, `docs/03-api-reference.md`, `releases/package/README.md`

- [ ] **Step 1: Документация**

- `docs/03-api-reference.md`: `GET/PUT /settings/ad` (поля, пароль только на запись: `null` — не менять, `""` — удалить), `GET /environment?module=&depth=quick|full`, `GET /environment/latest`, право `platform.environment.check`.
- `docs/02-security.md`: учётка записи (служебная с делегированием — рекомендуется; учётка службы на DC = права администратора домена), пароль шифруется AES-256-GCM, запись только по зашифрованному каналу, охрана зоны/области, сгенерированные пароли не журналируются.
- Пакетный README: раздел «Active Directory: управление» — корневая OU, учётка записи, «Проверка окружения».

- [ ] **Step 2: Полный прогон, сборка пакета**

Run: `dotnet test src/tests/WinAdmin.Tests` (с `WINADMIN_TEST_POSTGRES`) → все PASS, `[AdFact]` — Skip.
Run (PowerShell): `& .\releases\build.ps1 -OutputPath C:\dev\winadmin\releases\out-pcs-dc` → «Готово»; упаковать в `releases\pcs-dc-deploy\WinAdmin-pcs-dc.zip`, без `*.db`/`*.key`/`network.json`/`database.json`.

- [ ] **Step 3: Выкатка на DC** — тем же скриптом, что в 1c (`dc9.ps1`: проверка хеша, резервная копия базы и приложения, распаковка, старт, health, откат при неудаче).

- [ ] **Step 4: Проверка на DC (только чтение)**

Под `admin` на сервере (пароль — из файла на сервере, не в чат): `GET /api/v1/environment?depth=quick` → `ad.directory` = Ok; `ad.root` = Failed «Корневая OU не задана» (настройки не трогаем — их задаёт пользователь); `ad.writer` = Failed «Служебная учётка не задана». Это ожидаемое состояние до настройки пользователем.

- [ ] **Step 5: Commit и push**

```bash
git add docs releases/package/README.md
git commit -m "docs: AD management settings and environment checks"
git push
```
