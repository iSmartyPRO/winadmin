# Software (Applications + Updates) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Software section to WinAdmin with Applications (installed programs from Registry + Store/AppX, uninstall with progress) and Updates (installed Windows updates, uninstall/rollback with progress) using in-memory jobs and a right-side drawer.

**Architecture:** Backend exposes `ISoftwareCatalogService` (list apps/updates) and singleton `ISoftwareJobService` (one active uninstall/rollback job, polling via REST). Pure helpers classify system apps and build quiet uninstall commands so they are unit-tested without touching the live machine. Frontend adds Software submenu → Applications / Updates pages (AG Grid) plus a shared `SoftwareOperationDrawer` that polls `GET /software/jobs/{id}` every ~1s.

**Tech Stack:** ASP.NET Core 10 (C#) — Registry, WMI `Win32_QuickFixEngineering`, AppX via `Windows.Management.Deployment.PackageManager` where available; xUnit; React 19 + TypeScript + Ant Design + AG Grid (no frontend test framework — verify via `npx tsc -b` / manual UI).

## Global Constraints

- Navigation: Software submenu with Applications (`/software/apps`) and Updates (`/software/updates`); parent icon `CodeOutlined` (spec).
- App sources: Registry Uninstall keys + Store/AppX only — not PackageManagement “everything” (spec).
- System apps hidden by default; client Switch «Показать системные» (spec).
- Progress UI: right Drawer; list stays visible (spec).
- Long operations: in-memory jobs + REST polling; no SignalR; max one active job; 30-minute timeout; jobs lost on process restart (spec).
- Updates: list + uninstall + rollback endpoints; `CanRollback` only when uninstall is possible for that update (v1: `CanRollback == CanUninstall` for removable QFEs — both API actions remain distinct for audit) (spec).
- Scopes: `software.read`, `software.manage` in `Scopes.All`; `admin` already bypasses via `ScopeAuthorization` (spec).
- Audit: one write **on job completion** only — `software.app.uninstall` / `software.update.uninstall` / `software.update.rollback` (spec).
- No install of software/updates, no auto-reboot, no persistent job store (spec).
- DTOs: `sealed record` with `{ get; init; }` (see `ProcessInfo.cs`). OS-facing services: `[SupportedOSPlatform("windows")]`. Controllers inherit `WinAdminControllerBase`, `[Authorize(Policy = "scope:" + Scopes.X)]`.
- Id encoding: prefix + raw key (`reg:`, `store:`, `upd:`); path params use `Uri.EscapeDataString` / `Uri.UnescapeDataString`.

---

## File Structure

**Backend (new):**
- `src/backend/WinAdmin.Core/Models/SoftwareModels.cs` — DTOs, enums, exceptions
- `src/backend/WinAdmin.Infrastructure/Software/SoftwareAppHelpers.cs` — pure helpers (system/canUninstall, quiet command, id parse)
- `src/backend/WinAdmin.Infrastructure/Software/SoftwareCatalogService.cs` — registry + AppX + QFE list
- `src/backend/WinAdmin.Infrastructure/Software/SoftwareJobService.cs` — in-memory jobs + process runner + audit via `IServiceScopeFactory`
- `src/backend/WinAdmin.Api/Controllers/SoftwareController.cs`
- `src/tests/WinAdmin.Tests/SoftwareAppHelpersTests.cs`
- `src/tests/WinAdmin.Tests/SoftwareJobServiceTests.cs`

**Backend (modified):**
- `src/backend/WinAdmin.Core/Security/Scopes.cs`
- `src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs` — add software interfaces (or new file `ISoftwareServices.cs` if preferred; plan uses append to `ISystemServices.cs` for consistency with Event Logs)
- `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`

**Frontend (new):**
- `src/frontend/src/components/SoftwareOperationDrawer.tsx`
- `src/frontend/src/pages/Applications.tsx`
- `src/frontend/src/pages/Updates.tsx`
- `src/frontend/src/hooks/useSoftwareJob.ts` — start + poll + active restore

**Frontend (modified):**
- `src/frontend/src/api/types.ts`
- `src/frontend/src/api/client.ts`
- `src/frontend/src/components/AppLayout.tsx`
- `src/frontend/src/App.tsx`
- `src/frontend/src/pages/Users.tsx` — `ALL_SCOPES`
- `src/frontend/src/pages/ApiDocs.tsx`
- `docs/03-api-reference.md`
- `README.md` (one feature bullet)

---

### Task 1: Models, scopes, interfaces

**Files:**
- Create: `src/backend/WinAdmin.Core/Models/SoftwareModels.cs`
- Modify: `src/backend/WinAdmin.Core/Security/Scopes.cs`
- Modify: `src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs`

**Interfaces:**
- Produces: `InstalledApp`, `InstalledUpdate`, `SoftwareJob`, `SoftwareJobType`, `SoftwareJobStatus`, `SoftwareConflictException`, `SoftwareNotFoundException`, `SoftwareActionNotAllowedException`, `Scopes.SoftwareRead`, `Scopes.SoftwareManage`, `ISoftwareCatalogService`, `ISoftwareJobService`

- [ ] **Step 1: Create models**

```csharp
// src/backend/WinAdmin.Core/Models/SoftwareModels.cs
namespace WinAdmin.Core.Models;

public sealed record InstalledApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public DateTime? InstallDate { get; init; }
    public string? InstallLocation { get; init; }
    public long? SizeBytes { get; init; }
    /// <summary>"Registry" or "Store".</summary>
    public required string Source { get; init; }
    public bool IsSystem { get; init; }
    public bool CanUninstall { get; init; }
    public string? UninstallString { get; init; }
}

public sealed record InstalledUpdate
{
    public required string Id { get; init; }
    public string? KbArticle { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public DateTime? InstalledOn { get; init; }
    public bool CanUninstall { get; init; }
    public bool CanRollback { get; init; }
}

public enum SoftwareJobType { UninstallApp, UninstallUpdate, RollbackUpdate }
public enum SoftwareJobStatus { Queued, Running, Succeeded, Failed }

public sealed record SoftwareJob
{
    public required string Id { get; init; }
    public SoftwareJobType Type { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public SoftwareJobStatus Status { get; init; }
    public int? ProgressPercent { get; init; }
    public required string StatusMessage { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
}

public sealed class SoftwareConflictException : Exception
{
    public string? ActiveJobId { get; }
    public SoftwareConflictException(string message, string? activeJobId = null) : base(message)
        => ActiveJobId = activeJobId;
}

public sealed class SoftwareNotFoundException : Exception
{
    public SoftwareNotFoundException(string message) : base(message) { }
}

public sealed class SoftwareActionNotAllowedException : Exception
{
    public SoftwareActionNotAllowedException(string message) : base(message) { }
}
```

- [ ] **Step 2: Add scopes**

In `Scopes.cs`, after `EventLogsRead`:

```csharp
public const string SoftwareRead = "software.read";
public const string SoftwareManage = "software.manage";
```

In `All`:

```csharp
PowerManage, EventLogsRead, SoftwareRead, SoftwareManage, Admin,
```

- [ ] **Step 3: Add interfaces** at end of `ISystemServices.cs`:

```csharp
public interface ISoftwareCatalogService
{
    IReadOnlyList<InstalledApp> GetApplications();
    IReadOnlyList<InstalledUpdate> GetUpdates();
    InstalledApp? FindApplication(string id);
    InstalledUpdate? FindUpdate(string id);
}

public interface ISoftwareJobService
{
    SoftwareJob StartUninstallApp(string appId);
    SoftwareJob StartUninstallUpdate(string updateId);
    SoftwareJob StartRollbackUpdate(string updateId);
    SoftwareJob? GetJob(string jobId);
    SoftwareJob? GetActiveJob();
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/backend/WinAdmin.Core/WinAdmin.Core.csproj`  
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core/Models/SoftwareModels.cs src/backend/WinAdmin.Core/Security/Scopes.cs src/backend/WinAdmin.Core/Abstractions/ISystemServices.cs
git commit -m "feat: add Software models, scopes, and service interfaces"
```

---

### Task 2: Pure app helpers (TDD)

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Software/SoftwareAppHelpers.cs`
- Test: `src/tests/WinAdmin.Tests/SoftwareAppHelpersTests.cs`

**Interfaces:**
- Consumes: none (pure static)
- Produces: `SoftwareAppHelpers.IsRegistrySystem(...)`, `CanUninstallRegistry(...)`, `BuildQuietUninstallCommand(...)`, `MakeRegistryId`, `MakeStoreId`, `MakeUpdateId`, `TryParseId`, `IsStoreSystemPackage(...)`

- [ ] **Step 1: Write failing tests**

```csharp
// src/tests/WinAdmin.Tests/SoftwareAppHelpersTests.cs
using WinAdmin.Infrastructure.Software;

namespace WinAdmin.Tests;

public sealed class SoftwareAppHelpersTests
{
    [Theory]
    [InlineData(1, null, true)]
    [InlineData(0, "KB123456", true)]
    [InlineData(0, null, false)]
    public void IsRegistrySystem_Heuristics(int systemComponent, string? parentKeyName, bool expected)
    {
        Assert.Equal(expected, SoftwareAppHelpers.IsRegistrySystem(systemComponent, parentKeyName, displayName: "App"));
    }

    [Fact]
    public void CanUninstallRegistry_RequiresUninstallString()
    {
        Assert.False(SoftwareAppHelpers.CanUninstallRegistry(null, null));
        Assert.True(SoftwareAppHelpers.CanUninstallRegistry("msiexec /x {GUID}", null));
        Assert.True(SoftwareAppHelpers.CanUninstallRegistry(null, "setup.exe /S"));
    }

    [Fact]
    public void BuildQuietUninstallCommand_PrefersQuietString()
    {
        var cmd = SoftwareAppHelpers.BuildQuietUninstallCommand(
            uninstallString: "msiexec.exe /x {AAA}",
            quietUninstallString: "msiexec.exe /x {AAA} /qn");
        Assert.Equal("msiexec.exe /x {AAA} /qn", cmd);
    }

    [Fact]
    public void BuildQuietUninstallCommand_AugmentsMsiexec()
    {
        var cmd = SoftwareAppHelpers.BuildQuietUninstallCommand("MsiExec.exe /X{AAA-BBB}", null);
        Assert.Contains("/qn", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{AAA-BBB}", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ids_RoundTrip()
    {
        var reg = SoftwareAppHelpers.MakeRegistryId("{GUID}");
        Assert.True(SoftwareAppHelpers.TryParseId(reg, out var kind, out var key));
        Assert.Equal(SoftwareIdKind.Registry, kind);
        Assert.Equal("{GUID}", key);

        var store = SoftwareAppHelpers.MakeStoreId("Foo_1.0.0.0_x64__publisher");
        Assert.True(SoftwareAppHelpers.TryParseId(store, out kind, out key));
        Assert.Equal(SoftwareIdKind.Store, kind);

        var upd = SoftwareAppHelpers.MakeUpdateId("KB5025221");
        Assert.True(SoftwareAppHelpers.TryParseId(upd, out kind, out key));
        Assert.Equal(SoftwareIdKind.Update, kind);
        Assert.Equal("KB5025221", key);
    }

    [Theory]
    [InlineData("Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy", true)]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe", false)]
    public void IsStoreSystemPackage(string familyOrFull, bool expected)
    {
        Assert.Equal(expected, SoftwareAppHelpers.IsStoreSystemPackage(familyOrFull));
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj --filter FullyQualifiedName~SoftwareAppHelpersTests`  
Expected: FAIL (type/helpers missing)

- [ ] **Step 3: Implement helpers**

```csharp
// src/backend/WinAdmin.Infrastructure/Software/SoftwareAppHelpers.cs
using System.Text.RegularExpressions;

namespace WinAdmin.Infrastructure.Software;

public enum SoftwareIdKind { Registry, Store, Update }

public static class SoftwareAppHelpers
{
    public const string RegistryPrefix = "reg:";
    public const string StorePrefix = "store:";
    public const string UpdatePrefix = "upd:";

    private static readonly HashSet<string> StoreSystemFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.Cortana",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.BioEnrollment",
        "Microsoft.LockApp",
        "Microsoft.Windows.ContentDeliveryManager",
        "windows.immersivecontrolpanel",
        "Microsoft.Windows.SecHealthUI",
        "Microsoft.MicrosoftEdge",
        "Microsoft.Win32WebViewHost",
        "Microsoft.XboxGameCallableUI",
        "Microsoft.XboxIdentityProvider",
    };

    public static string MakeRegistryId(string subKeyName) => RegistryPrefix + subKeyName;
    public static string MakeStoreId(string packageFullName) => StorePrefix + packageFullName;
    public static string MakeUpdateId(string hotfixId) => UpdatePrefix + hotfixId;

    public static bool TryParseId(string id, out SoftwareIdKind kind, out string key)
    {
        kind = default;
        key = "";
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.StartsWith(RegistryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = SoftwareIdKind.Registry;
            key = id[RegistryPrefix.Length..];
            return key.Length > 0;
        }
        if (id.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = SoftwareIdKind.Store;
            key = id[StorePrefix.Length..];
            return key.Length > 0;
        }
        if (id.StartsWith(UpdatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = SoftwareIdKind.Update;
            key = id[UpdatePrefix.Length..];
            return key.Length > 0;
        }
        return false;
    }

    public static bool IsRegistrySystem(int systemComponent, string? parentDisplayNameOrKeyHint, string? displayName)
    {
        if (systemComponent == 1) return true;
        if (!string.IsNullOrEmpty(parentDisplayNameOrKeyHint) &&
            parentDisplayNameOrKeyHint.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(displayName) &&
            Regex.IsMatch(displayName, @"^Update for|Security Update for|Hotfix for", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    public static bool CanUninstallRegistry(string? uninstallString, string? quietUninstallString)
        => !string.IsNullOrWhiteSpace(quietUninstallString) || !string.IsNullOrWhiteSpace(uninstallString);

    public static string? BuildQuietUninstallCommand(string? uninstallString, string? quietUninstallString)
    {
        if (!string.IsNullOrWhiteSpace(quietUninstallString))
            return quietUninstallString.Trim();
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;
        var s = uninstallString.Trim();
        if (s.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            if (!s.Contains("/qn", StringComparison.OrdinalIgnoreCase) &&
                !s.Contains("/quiet", StringComparison.OrdinalIgnoreCase))
                return s + " /qn /norestart";
            return s;
        }
        // Non-MSI: return as-is; many support /S — do not invent flags blindly
        return s;
    }

    public static bool IsStoreSystemPackage(string packageFamilyOrFullName)
    {
        var family = packageFamilyOrFullName;
        var us = family.IndexOf('_');
        // PackageFullName: Name_Version_Arch_Resource_PublisherId — family is Name_PublisherId
        // For simplicity: check if any known family is a prefix of the string or equals Name before first _
        foreach (var sys in StoreSystemFamilies)
        {
            if (family.StartsWith(sys + "_", StringComparison.OrdinalIgnoreCase) ||
                family.Equals(sys, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // Framework / resource packages often contain ".NET.Native" or "Microsoft.NET."
        if (family.Contains("Microsoft.NET.", StringComparison.OrdinalIgnoreCase) ||
            family.Contains(".NET.Native", StringComparison.OrdinalIgnoreCase) ||
            family.Contains("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
```

Adjust `IsStoreSystemPackage` tests if the family-name heuristic needs a PackageFamilyName form (`Name_PublisherId`). For Calculator test use a non-listed family; for ShellExperienceHost use a full name starting with that family.

- [ ] **Step 4: Run tests — expect PASS**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj --filter FullyQualifiedName~SoftwareAppHelpersTests`  
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/Software/SoftwareAppHelpers.cs src/tests/WinAdmin.Tests/SoftwareAppHelpersTests.cs
git commit -m "feat: add Software app id and uninstall helper heuristics"
```

---

### Task 3: Catalog service (applications + updates)

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Software/SoftwareCatalogService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `SoftwareAppHelpers`, `ISoftwareCatalogService`
- Produces: working `GetApplications` / `GetUpdates` / `Find*`

- [ ] **Step 1: Implement `SoftwareCatalogService`**

Key behaviors (full file in implementation — outline must be followed):

1. **Registry apps** — read these hives (skip entries without `DisplayName`):
   - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
   - Id = `MakeRegistryId(subKeyName)` (dedupe by Id)
   - Map: DisplayName→Name, DisplayVersion, Publisher, InstallDate (yyyyMMdd), InstallLocation, EstimatedSize (KB→bytes * 1024), UninstallString / QuietUninstallString
   - `IsSystem` / `CanUninstall` via helpers; `Source = "Registry"`
   - Prefer storing effective quiet command in `UninstallString` field for the job runner

2. **Store apps** — try `Windows.Management.Deployment.PackageManager().FindPackagesForUser("")` (or empty user). On failure (API missing), return registry-only and log warning.
   - Skip frameworks if detectable (`IsFramework` / helpers)
   - Id = `MakeStoreId(package.Id.FullName)`
   - `IsSystem` = `IsStoreSystemPackage(...)` or signature kind system
   - `CanUninstall` = !IsSystem && !IsFramework (best-effort)
   - `Source = "Store"`

3. **Updates** — WMI query `SELECT HotFixID, Description, InstalledOn, InstalledBy FROM Win32_QuickFixEngineering`
   - Id = `MakeUpdateId(HotFixID)`
   - Title = Description or HotFixID; KbArticle = HotFixID
   - `CanUninstall` = HotFixID looks like `KBnnnnnn` (regex `^KB\d+$` ignore case) — wusa can target these; others false
   - `CanRollback` = `CanUninstall` (v1 rule from Global Constraints)

```csharp
[SupportedOSPlatform("windows")]
public sealed class SoftwareCatalogService : ISoftwareCatalogService
{
    // constructor ILogger<SoftwareCatalogService>
    // implement interface as above
}
```

- [ ] **Step 2: Register DI**

```csharp
services.AddScoped<ISoftwareCatalogService, SoftwareCatalogService>();
```

(Job service registered in Task 4 as singleton.)

- [ ] **Step 3: Build**

Run: `dotnet build src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj`  
Expected: `Build succeeded.` (may need `TargetFramework` windows TFM already used by Infrastructure — follow existing project.)

- [ ] **Step 4: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/Software/SoftwareCatalogService.cs src/backend/WinAdmin.Infrastructure/DependencyInjection.cs
git commit -m "feat: add Software catalog service for apps and updates"
```

---

### Task 4: Job service (TDD for concurrency + state)

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Software/SoftwareJobService.cs`
- Test: `src/tests/WinAdmin.Tests/SoftwareJobServiceTests.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `ISoftwareCatalogService`, `IServiceScopeFactory`, `IAuditService` (via scope), `ILogger`
- Produces: `ISoftwareJobService` implementation

Design notes for implementer:
- Singleton holding `ConcurrentDictionary<string, SoftwareJobState>`
- `SoftwareJobState` mutable internal class; public API returns immutable `SoftwareJob` snapshots
- `GetActiveJob()` returns first with Status Queued or Running
- Start methods: if active exists → throw `SoftwareConflictException`
- Resolve target via new scope → catalog `Find*`; if null → `SoftwareNotFoundException`; if !Can* → `SoftwareActionNotAllowedException`
- Fire `Task.Run` with linked `CancellationTokenSource` timeout 30 minutes
- Runner:
  - App registry: `Process.Start` parsed command (file + args); wait for exit
  - App store: `PackageManager.RemovePackageAsync(fullName)` 
  - Update uninstall/rollback: `wusa.exe /uninstall /kb:{number} /quiet /norestart` (strip `KB` prefix for `/kb:`)
- Update status messages: «Запуск…», «Удаление…», «Завершение…»; ProgressPercent null until done then 100
- Exit code 3010 → Succeeded with message about reboot required
- On finish: write audit via scoped `IAuditService` with action name per type
- Completed jobs: prune older than 1 hour on each Get/Start

For **unit tests**, inject a testable seam: make an internal/protected `ISoftwareProcessRunner` interface with `Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)` and `Task RemoveStorePackageAsync(string fullName, CancellationToken ct)` so tests do not launch real processes. Default production runner implements real Process/PackageManager.

- [ ] **Step 1: Write failing job tests** (fake catalog + fake runner)

```csharp
// src/tests/WinAdmin.Tests/SoftwareJobServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Software;

namespace WinAdmin.Tests;

file sealed class FakeCatalog : ISoftwareCatalogService
{
    public List<InstalledApp> Apps { get; } = new();
    public List<InstalledUpdate> Updates { get; } = new();
    public IReadOnlyList<InstalledApp> GetApplications() => Apps;
    public IReadOnlyList<InstalledUpdate> GetUpdates() => Updates;
    public InstalledApp? FindApplication(string id) => Apps.FirstOrDefault(a => a.Id == id);
    public InstalledUpdate? FindUpdate(string id) => Updates.FirstOrDefault(u => u.Id == id);
}

file sealed class FakeRunner : ISoftwareProcessRunner
{
    public int ExitCode { get; set; }
    public Exception? ThrowOnRun { get; set; }
    public TaskCompletionSource? Gate { get; set; }

    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        if (Gate is not null) await Gate.Task.WaitAsync(ct);
        if (ThrowOnRun is not null) throw ThrowOnRun;
        return ExitCode;
    }

    public Task RemoveStorePackageAsync(string fullName, CancellationToken ct) => Task.CompletedTask;
}

file sealed class FakeAudit : IAuditService
{
    public List<AuditEntryDto> Entries { get; } = new();
    public Task WriteAsync(AuditEntryDto entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
    // Implement any other IAuditService members as no-ops / empty — match real interface.
}

public sealed class SoftwareJobServiceTests
{
    private static (SoftwareJobService svc, FakeCatalog catalog, FakeRunner runner, FakeAudit audit) Create()
    {
        var catalog = new FakeCatalog();
        var runner = new FakeRunner();
        var audit = new FakeAudit();
        var services = new ServiceCollection();
        services.AddSingleton<ISoftwareCatalogService>(catalog);
        services.AddSingleton<IAuditService>(audit);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var svc = new SoftwareJobService(scopeFactory, runner, NullLogger<SoftwareJobService>.Instance);
        return (svc, catalog, runner, audit);
    }

    [Fact]
    public async Task StartUninstallApp_Succeeds_AndAudits()
    {
        var (svc, catalog, runner, audit) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1", Name = "App1", Source = "Registry",
            CanUninstall = true, UninstallString = "cmd.exe /c exit 0",
        });
        runner.ExitCode = 0;

        var job = svc.StartUninstallApp("reg:App1");
        Assert.Equal(SoftwareJobStatus.Queued, job.Status); // or Running — accept either immediately

        await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);
        Assert.Contains(audit.Entries, e => e.Action == "software.app.uninstall" && e.Success);
    }

    [Fact]
    public void SecondStart_WhileActive_ThrowsConflict()
    {
        var (svc, catalog, runner, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1", Name = "App1", Source = "Registry",
            CanUninstall = true, UninstallString = "cmd.exe /c exit 0",
        });
        runner.Gate = new TaskCompletionSource();

        var first = svc.StartUninstallApp("reg:App1");
        var ex = Assert.Throws<SoftwareConflictException>(() => svc.StartUninstallApp("reg:App1"));
        Assert.Equal(first.Id, ex.ActiveJobId);
        runner.Gate.SetResult();
    }

    [Fact]
    public void UnknownApp_ThrowsNotFound()
    {
        var (svc, _, _, _) = Create();
        Assert.Throws<SoftwareNotFoundException>(() => svc.StartUninstallApp("reg:missing"));
    }

    [Fact]
    public void CannotUninstall_ThrowsNotAllowed()
    {
        var (svc, catalog, _, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:Locked", Name = "Locked", Source = "Registry", CanUninstall = false,
        });
        Assert.Throws<SoftwareActionNotAllowedException>(() => svc.StartUninstallApp("reg:Locked"));
    }

    [Fact]
    public async Task ExitCode3010_Succeeded_WithRebootMessage()
    {
        var (svc, catalog, runner, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1", Name = "App1", Source = "Registry",
            CanUninstall = true, UninstallString = "cmd.exe /c exit 0",
        });
        runner.ExitCode = 3010;
        var job = svc.StartUninstallApp("reg:App1");
        var done = await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);
        Assert.Contains("перезагруз", done.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SoftwareJob> WaitFor(ISoftwareJobService svc, string id, SoftwareJobStatus status)
    {
        for (var i = 0; i < 100; i++)
        {
            var j = svc.GetJob(id)!;
            if (j.Status == status || j.Status is SoftwareJobStatus.Failed or SoftwareJobStatus.Succeeded)
                return j;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }
}
```

Adjust `FakeAudit` to match the exact `IAuditService` surface in `ISecurityServices.cs` (copy unused members as no-ops). `SoftwareJobService` constructor signature must match: `(IServiceScopeFactory, ISoftwareProcessRunner, ILogger<SoftwareJobService>)`.

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj --filter FullyQualifiedName~SoftwareJobServiceTests`  
Expected: FAIL (types missing)

- [ ] **Step 3: Implement `ISoftwareProcessRunner`, `SoftwareProcessRunner`, `SoftwareJobService`**

```csharp
// ISoftwareProcessRunner — in SoftwareProcessRunner.cs or own file
public interface ISoftwareProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, CancellationToken ct);
    Task RemoveStorePackageAsync(string packageFullName, CancellationToken ct);
}
```

Parse uninstall command into fileName/arguments (handle quoted paths). Register:

```csharp
services.AddSingleton<ISoftwareProcessRunner, SoftwareProcessRunner>();
services.AddSingleton<ISoftwareJobService, SoftwareJobService>();
```

- [ ] **Step 4: Run tests — PASS**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj --filter FullyQualifiedName~SoftwareJobServiceTests`  
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/Software/SoftwareJobService.cs src/backend/WinAdmin.Infrastructure/Software/SoftwareProcessRunner.cs src/backend/WinAdmin.Infrastructure/Software/ISoftwareProcessRunner.cs src/tests/WinAdmin.Tests/SoftwareJobServiceTests.cs src/backend/WinAdmin.Infrastructure/DependencyInjection.cs
git commit -m "feat: add Software job service with single-flight uninstall"
```

---

### Task 5: SoftwareController

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/SoftwareController.cs`

**Interfaces:**
- Consumes: `ISoftwareCatalogService`, `ISoftwareJobService`
- Produces: REST endpoints under `/api/v1/software`

- [ ] **Step 1: Implement controller**

```csharp
// src/backend/WinAdmin.Api/Controllers/SoftwareController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed class SoftwareController : WinAdminControllerBase
{
    private readonly ISoftwareCatalogService _catalog;
    private readonly ISoftwareJobService _jobs;

    public SoftwareController(ISoftwareCatalogService catalog, ISoftwareJobService jobs)
    {
        _catalog = catalog;
        _jobs = jobs;
    }

    [HttpGet("applications")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareRead)]
    [ProducesResponseType(typeof(IReadOnlyList<InstalledApp>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<InstalledApp>> GetApplications()
        => Ok(_catalog.GetApplications());

    [HttpPost("applications/{id}/uninstall")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status202Accepted)]
    public IActionResult UninstallApplication(string id)
        => StartJob(() => _jobs.StartUninstallApp(Uri.UnescapeDataString(id)));

    [HttpGet("updates")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareRead)]
    [ProducesResponseType(typeof(IReadOnlyList<InstalledUpdate>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<InstalledUpdate>> GetUpdates()
        => Ok(_catalog.GetUpdates());

    [HttpPost("updates/{id}/uninstall")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status202Accepted)]
    public IActionResult UninstallUpdate(string id)
        => StartJob(() => _jobs.StartUninstallUpdate(Uri.UnescapeDataString(id)));

    [HttpPost("updates/{id}/rollback")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status202Accepted)]
    public IActionResult RollbackUpdate(string id)
        => StartJob(() => _jobs.StartRollbackUpdate(Uri.UnescapeDataString(id)));

    [HttpGet("jobs/active")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetActiveJob()
    {
        var job = _jobs.GetActiveJob();
        return job is null ? NoContent() : Ok(job);
    }

    [HttpGet("jobs/{jobId}")]
    [Authorize(Policy = "scope:" + Scopes.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status200OK)]
    public ActionResult<SoftwareJob> GetJob(string jobId)
    {
        var job = _jobs.GetJob(jobId);
        return job is null ? NotFound() : Ok(job);
    }

    private IActionResult StartJob(Func<SoftwareJob> start)
    {
        try
        {
            var job = start();
            return Accepted(job);
        }
        catch (SoftwareNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (SoftwareActionNotAllowedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (SoftwareConflictException ex)
        {
            return Conflict(new { message = ex.Message, activeJobId = ex.ActiveJobId });
        }
    }
}
```

Route order: declare `jobs/active` **before** `jobs/{jobId}` so `active` is not captured as an id (already ordered above).

- [ ] **Step 2: Build API project**

Run: `dotnet build src/backend/WinAdmin.Api/WinAdmin.Api.csproj`  
Expected: succeeded

- [ ] **Step 3: Commit**

```bash
git add src/backend/WinAdmin.Api/Controllers/SoftwareController.cs
git commit -m "feat: add Software API controller for apps, updates, and jobs"
```

---

### Task 6: Frontend types + API client

**Files:**
- Modify: `src/frontend/src/api/types.ts`
- Modify: `src/frontend/src/api/client.ts`
- Modify: `src/frontend/src/pages/Users.tsx` (`ALL_SCOPES`)

**Interfaces:**
- Produces: `InstalledApp`, `InstalledUpdate`, `SoftwareJob`, `api.software.*`

- [ ] **Step 1: Add types** (camelCase JSON — ASP.NET default)

```typescript
export type SoftwareJobType = 'UninstallApp' | 'UninstallUpdate' | 'RollbackUpdate'
export type SoftwareJobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface InstalledApp {
  id: string
  name: string
  version?: string
  publisher?: string
  installDate?: string
  installLocation?: string
  sizeBytes?: number
  source: 'Registry' | 'Store' | string
  isSystem: boolean
  canUninstall: boolean
  uninstallString?: string
}

export interface InstalledUpdate {
  id: string
  kbArticle?: string
  title: string
  description?: string
  installedOn?: string
  canUninstall: boolean
  canRollback: boolean
}

export interface SoftwareJob {
  id: string
  type: SoftwareJobType
  targetId: string
  targetName: string
  status: SoftwareJobStatus
  progressPercent?: number | null
  statusMessage: string
  error?: string
  startedAt: string
  finishedAt?: string
}
```

- [ ] **Step 2: Add client methods**

```typescript
software: {
  applications: () => http.get<InstalledApp[]>('/software/applications').then((r) => r.data),
  uninstallApp: (id: string) =>
    http.post<SoftwareJob>(`/software/applications/${encodeURIComponent(id)}/uninstall`).then((r) => r.data),
  updates: () => http.get<InstalledUpdate[]>('/software/updates').then((r) => r.data),
  uninstallUpdate: (id: string) =>
    http.post<SoftwareJob>(`/software/updates/${encodeURIComponent(id)}/uninstall`).then((r) => r.data),
  rollbackUpdate: (id: string) =>
    http.post<SoftwareJob>(`/software/updates/${encodeURIComponent(id)}/rollback`).then((r) => r.data),
  getJob: (jobId: string) => http.get<SoftwareJob>(`/software/jobs/${jobId}`).then((r) => r.data),
  getActiveJob: () =>
    http.get<SoftwareJob>('/software/jobs/active').then((r) => (r.status === 204 ? null : r.data)),
},
```

Note: axios treats 204 as success with empty data — handle `r.status === 204` or `!r.data`.

- [ ] **Step 3: Update `ALL_SCOPES` in Users.tsx**

Add `'eventlogs.read', 'software.read', 'software.manage'` (eventlogs was missing from the hardcoded list).

- [ ] **Step 4: Typecheck**

Run: `npm --prefix src/frontend run build` (or `npx tsc -b` in frontend)  
Expected: success (or only pre-existing errors unrelated to these types)

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/types.ts src/frontend/src/api/client.ts src/frontend/src/pages/Users.tsx
git commit -m "feat: add Software API client types and user scopes"
```

---

### Task 7: Operation drawer + job hook

**Files:**
- Create: `src/frontend/src/hooks/useSoftwareJob.ts`
- Create: `src/frontend/src/components/SoftwareOperationDrawer.tsx`

**Interfaces:**
- Consumes: `api.software.getJob`, `getActiveJob`
- Produces: hook `{ job, drawerOpen, startPolling, restoreActive, closeDrawer, minimize }` + drawer UI

- [ ] **Step 1: Implement `useSoftwareJob`**

Behavior:
- `poll(jobId)` every 1000ms until status is Succeeded or Failed
- `restoreActive()` on mount of pages calls `getActiveJob` and starts polling if present
- Expose `onTerminal?: (job) => void` callback for list refresh on Succeeded
- Clear interval on unmount

- [ ] **Step 2: Implement `SoftwareOperationDrawer`**

Ant Design `Drawer` placement `right`, width ~400:
- Title: targetName
- Tag for type / status
- `Progress` percent={progressPercent ?? undefined} status={failed? exception : active? active : success}; if progressPercent null and Running → `Progress` with `status="active"` without percent (indeterminate look via pulse or spinning Tip)
- Paragraph: statusMessage
- Alert on Failed with error
- Alert on Succeeded if message mentions reboot/перезагруз
- Footer: Закрыть (disabled confirm if Running — `Modal.confirm` «Операция ещё выполняется. Скрыть панель?» → minimize keeps polling)

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/hooks/useSoftwareJob.ts src/frontend/src/components/SoftwareOperationDrawer.tsx
git commit -m "feat: add Software operation drawer and job polling hook"
```

---

### Task 8: Applications page + navigation

**Files:**
- Create: `src/frontend/src/pages/Applications.tsx`
- Modify: `src/frontend/src/components/AppLayout.tsx`
- Modify: `src/frontend/src/App.tsx`

**Interfaces:**
- Consumes: `api.software.applications`, `uninstallApp`, drawer/hook
- Produces: working `/software/apps` UI

- [ ] **Step 1: Implement Applications page**

Pattern after `Services.tsx`:
- `useApi(api.software.applications)`
- State: `quickFilter`, `showSystem` (default false), job hook
- `useEffect(() => { restoreActive() }, [])`
- Filter rows: `showSystem ? data : data.filter(a => !a.isSystem)`
- Columns: name, version, publisher, installDate (formatDateTime), sizeBytes (formatBytes), source (Tag), actions (Delete Popconfirm → uninstallApp → open drawer)
- PageHeader title «Приложения», subtitle with counts

- [ ] **Step 2: Menu + routes**

In `AppLayout.tsx`, after Processes (or before Printers), insert:

```tsx
{
  key: '/software', icon: <CodeOutlined />, label: 'Software',
  children: [
    { key: '/software/apps', label: 'Applications' },
    { key: '/software/updates', label: 'Updates' },
  ],
},
```

Import `CodeOutlined`. For selectedKeys with submenu, also set `defaultOpenKeys` to include `/software` when path starts with `/software` (mirror `/logs` behavior).

In `App.tsx`:

```tsx
import Applications from './pages/Applications'
import Updates from './pages/Updates'
// ...
<Route path="/software/apps" element={<Applications />} />
<Route path="/software/updates" element={<Updates />} />
```

Implement Task 9 `Updates.tsx` in the same session before committing Task 8 if both routes are registered together; otherwise register only `/software/apps` in this commit and add the updates route in Task 9.

- [ ] **Step 3: Manual smoke** — list loads, system toggle works, drawer opens on fake 409 if job running

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/pages/Applications.tsx src/frontend/src/components/AppLayout.tsx src/frontend/src/App.tsx
git commit -m "feat: add Software Applications page and navigation"
```

---

### Task 9: Updates page

**Files:**
- Create: `src/frontend/src/pages/Updates.tsx`

**Interfaces:**
- Consumes: `api.software.updates`, uninstall/rollback, same drawer/hook

- [ ] **Step 1: Implement Updates page**

- Grid: kbArticle, title, installedOn, actions Uninstall / Rollback (Popconfirm)
- Rollback button only if `canRollback`; Uninstall only if `canUninstall`
- Same job drawer + restoreActive

- [ ] **Step 2: Typecheck / smoke list**

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/pages/Updates.tsx
git commit -m "feat: add Software Updates page with uninstall and rollback"
```

---

### Task 10: Docs and ApiDocs

**Files:**
- Modify: `docs/03-api-reference.md`
- Modify: `README.md`
- Modify: `src/frontend/src/pages/ApiDocs.tsx`
- Modify: `docs/00-overview.md` (one bullet under capabilities if present)

- [ ] **Step 1: Document endpoints** in `03-api-reference.md` under new «Software» section: all 7 routes, scopes, 202/409/204 behavior, note in-memory jobs.

- [ ] **Step 2: README** — add bullet: «Software: установленные приложения и обновления Windows, удаление с прогрессом»

- [ ] **Step 3: ApiDocs.tsx** — append endpoint rows for software routes

- [ ] **Step 4: Commit**

```bash
git add docs/03-api-reference.md docs/00-overview.md README.md src/frontend/src/pages/ApiDocs.tsx
git commit -m "docs: document Software API and UI feature"
```

---

### Task 11: Verification gate

**Files:** none (verification only)

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj`  
Expected: all PASS

- [ ] **Step 2: Build backend + frontend**

```powershell
dotnet build src/backend/WinAdmin.Api/WinAdmin.Api.csproj
npm --prefix src/frontend run build
```

Expected: both succeed

- [ ] **Step 3: Manual checklist on a Windows machine with WinAdmin running elevated**

1. Open Software → Applications — list non-empty; toggle system apps
2. Open Updates — list shows KBs
3. Uninstall a safe test app (or cancel-friendly) — drawer shows Running → Succeeded/Failed; list refreshes on success
4. Start second uninstall while first runs — 409 / error toast
5. Refresh page mid-job — drawer restores via `/jobs/active`
6. Audit log shows completion entry after finish

- [ ] **Step 4: Final commit only if verification fixed stray issues; otherwise done**

---

## Self-Review (plan vs spec)

| Spec requirement | Task |
|---|---|
| Software submenu Applications / Updates | 8 |
| Registry + Store apps | 3 |
| Hide system by default + toggle | 8 |
| Updates list + uninstall + rollback | 3, 5, 9 |
| Drawer progress + polling jobs | 4, 5, 7 |
| One active job / 409 | 4, 5 |
| Scopes software.read/manage | 1, 6 |
| Audit on completion | 4 |
| No SignalR / no auto-reboot / no install | Global Constraints |
| Docs | 10 |
| Unit tests helpers + jobs | 2, 4 |

No TBD placeholders remaining. Id prefixes and `CanRollback == CanUninstall` for removable KBs are explicit.
