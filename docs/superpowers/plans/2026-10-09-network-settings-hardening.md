# Network Settings & Install Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Панель WinAdmin по умолчанию слушает только `127.0.0.1`, порт и режим доступа меняются через CLI (`WinAdmin.exe network ...`) и в UI под scope `admin` с перепривязкой Kestrel на лету, а папки приложения/данных защищены от записи/чтения непривилегированными пользователями.

**Architecture:** Настройки — файл `network.json` рядом с БД. Собственный `IConfigurationProvider` превращает его в `Kestrel:Endpoints`, Kestrel перечитывает их и перепривязывается без перезапуска. Общий `INetworkSettingsService` (валидация, проверка порта, брандмауэр через `netsh`, атомарная запись) используется и API-контроллером, и CLI. При старте как служба Windows — исправление ACL и сверка брандмауэра.

**Tech Stack:** .NET 10 (ASP.NET Core, Kestrel, System.CommandLine beta4), xUnit + Moq + `Microsoft.AspNetCore.Mvc.Testing`, React 19 + Ant Design 6 + axios, PowerShell, Rust (`winadmin-ctl`).

**Spec:** `docs/superpowers/specs/2026-10-09-network-settings-hardening-design.md`

## Global Constraints

- Репозиторий `C:\dev\winadmin`, ветка `feat/network-settings-hardening`.
- Backend: `net10.0-windows` (Api, Infrastructure, Tests), `net10.0` (Core — без Windows-API).
- Файл настроек: `network.json` в каталоге `WinAdmin:DatabasePath` (иначе `AppContext.BaseDirectory`); JSON camelCase, режим строкой: `{ "mode": "local", "port": 8080, "allow": [] }`.
- Режимы: `Local` → `http://127.0.0.1:{port}`; `Network` → `http://0.0.0.0:{port}`.
- Правило брандмауэра: имя `WinAdmin (managed)`, `profile=domain,private` (никогда Public); устаревшие правила `WinAdmin HTTP {port}` удаляются.
- API: `GET/PUT /api/v1/settings/network`, только scope `admin`; аудит action `settings.network` / `settings.network.rollback`.
- Права задаются по SID: Администраторы `S-1-5-32-544`, SYSTEM `S-1-5-18`, Пользователи `S-1-5-32-545`.
- ACL и сверка брандмауэра при старте — только когда `WindowsServiceHelpers.IsWindowsService()`.
- Пользовательские строки — по-русски (i18n в коде ещё нет), как соседний код.
- Тесты: `dotnet test src/tests/WinAdmin.Tests` (база — 93 зелёных до начала работы).
- Каждый коммит заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Админ сохраняет те же настройки, что уже действуют (порт не изменился) — проверка «порт занят» не должна срабатывать на собственный порт службы (тест в Task 4).
2. Ввод подсети вида `10` или `0.0.0.0/0` — должен отклоняться: первое `IPAddress.TryParse` принимает как `0.0.0.10`, второе открывает всем (тест в Task 1).
3. `network.json` повреждён или содержит `port: 0` — служба стартует на `127.0.0.1:8080`, файл не перезаписывается (тест в Task 2).
4. `network set` из консоли без прав администратора — понятное сообщение и код 1, без стектрейса и без частично применённых изменений (тест в Task 7).
5. Переключение режима с тем же портом (`Local` ↔ `Network`) — Kestrel должен перепривязаться, хотя порт тот же (тест в Task 5).

---

## File Structure

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/Models/NetworkModels.cs` | `NetworkMode`, `NetworkSettings`, DTO API |
| `src/backend/WinAdmin.Core/Network/NetworkSettingsValidator.cs` | Нормализация и валидация (чистая) |
| `src/backend/WinAdmin.Core/Network/NetworkEndpoints.cs` | URL прослушивания, URL панели, порт из `urls` (чистые) |
| `src/backend/WinAdmin.Core/Abstractions/INetworkSettingsService.cs` | Интерфейс сервиса + `NetworkCheckResult` |
| `src/backend/WinAdmin.Infrastructure/WinAdminPaths.cs` | Путь к БД и каталогу данных (единый для API и CLI) |
| `src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsStore.cs` | Чтение/атомарная запись/первичное создание `network.json` |
| `src/backend/WinAdmin.Infrastructure/Network/FirewallCommands.cs` | Построение команд `netsh` (чистая) |
| `src/backend/WinAdmin.Infrastructure/Network/NetshFirewallRunner.cs` | `IFirewallRunner` + выполнение `netsh` |
| `src/backend/WinAdmin.Infrastructure/Network/PortProbe.cs` | `IPortProbe` — занят ли порт, слушаем ли endpoint |
| `src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsService.cs` | Реализация `INetworkSettingsService` |
| `src/backend/WinAdmin.Infrastructure/Network/NetworkServiceCollectionExtensions.cs` | `AddWinAdminNetwork(store)` |
| `src/backend/WinAdmin.Infrastructure/Hardening/AclPlan.cs` | Наборы правил ACL (чистая) |
| `src/backend/WinAdmin.Infrastructure/Hardening/InstallationHardening.cs` | Применение ACL к папкам |
| `src/backend/WinAdmin.Api/Network/NetworkConfigurationSource.cs` | Источник/провайдер конфигурации `Kestrel:Endpoints` из файла |
| `src/backend/WinAdmin.Api/Network/NetworkApplyWatchdog.cs` | Проверка после применения и откат |
| `src/backend/WinAdmin.Api/Controllers/NetworkSettingsController.cs` | `GET/PUT /api/v1/settings/network` |
| `src/backend/WinAdmin.Api/Cli/NetworkCommands.cs` | `network show` / `network set` |
| `src/backend/WinAdmin.Api/Program.cs`, `Cli/CliRunner.cs` | Подключение всего перечисленного |
| `src/frontend/src/components/NetworkSettingsCard.tsx` | Карточка «Сеть» |
| `src/frontend/src/api/{types,client}.ts`, `pages/Settings.tsx` | Клиент API и встраивание карточки |
| `releases/install-service.ps1`, `releases/build.ps1`, `releases/package/README.md`, `releases/package/docs/security.md`, `README.md`, `docs/00-overview.md` | Установщик и документация |
| `src/ctl/winadmin-ctl/src/{settings,service,installer}.rs` | `winadmin-ctl` без `--urls`, порт в `network.json`, ACL |
| `src/tests/WinAdmin.Tests/*` | Тесты |

---

### Task 1: Модель, валидатор и вычисление адресов (Core)

**Files:**
- Create: `src/backend/WinAdmin.Core/Models/NetworkModels.cs`
- Create: `src/backend/WinAdmin.Core/Network/NetworkSettingsValidator.cs`
- Create: `src/backend/WinAdmin.Core/Network/NetworkEndpoints.cs`
- Test: `src/tests/WinAdmin.Tests/NetworkSettingsValidatorTests.cs`
- Test: `src/tests/WinAdmin.Tests/NetworkEndpointsTests.cs`

**Interfaces:**
- Produces:
  - `enum NetworkMode { Local, Network }`
  - `sealed record NetworkSettings(NetworkMode Mode, int Port, IReadOnlyList<string> Allow)` с `static NetworkSettings Default` и `bool IsEquivalentTo(NetworkSettings other)`
  - `sealed record NetworkSettingsDto(NetworkMode Mode, int Port, IReadOnlyList<string> Allow, string Url, bool FirewallRule)`
  - `sealed record UpdateNetworkSettingsRequest(NetworkMode Mode, int Port, List<string>? Allow)`
  - `sealed record NetworkUpdateResult(string Url)`
  - `static (NetworkSettings Settings, IReadOnlyList<string> Errors) NetworkSettingsValidator.Normalize(NetworkSettings input)`
  - `NetworkEndpoints.DefaultPort = 8080`, `string ListenUrl(NetworkSettings)`, `IPAddress BindAddress(NetworkSettings)`, `string PanelUrl(NetworkSettings, string? host)`, `int? PortFromUrls(string? urls)`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/NetworkSettingsValidatorTests.cs`:

```csharp
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Tests;

public sealed class NetworkSettingsValidatorTests
{
    private static NetworkSettings S(NetworkMode mode, int port, params string[] allow) => new(mode, port, allow);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Validates_port_range(int port, bool valid)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Local, port));
        Assert.Equal(valid, errors.Count == 0);
    }

    [Theory]
    [InlineData("10.77.77.0/24")]
    [InlineData("192.168.88.5")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::/32")]
    public void Accepts_addresses_and_cidr(string item)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Network, 8080, item));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("10")]              // IPAddress.TryParse принимает как 0.0.0.10
    [InlineData("abc")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/x")]
    [InlineData("0.0.0.0/0")]       // «любой адрес» запрещён
    [InlineData("::/0")]
    [InlineData("2001:db8::/129")]
    public void Rejects_garbage_and_any(string item)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Network, 8080, item));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Network_mode_requires_allow_list()
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Network, 8080));
        Assert.Single(errors);
    }

    [Fact]
    public void Local_mode_keeps_allow_list_without_requiring_it()
    {
        var (settings, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Local, 8080, "10.0.0.0/8"));
        Assert.Empty(errors);
        Assert.Equal(new[] { "10.0.0.0/8" }, settings.Allow);
    }

    [Fact]
    public void Trims_drops_empty_and_deduplicates()
    {
        var (settings, errors) = NetworkSettingsValidator.Normalize(
            S(NetworkMode.Network, 8080, " 10.0.0.0/8 ", "", "10.0.0.0/8", "192.168.1.1"));
        Assert.Empty(errors);
        Assert.Equal(new[] { "10.0.0.0/8", "192.168.1.1" }, settings.Allow);
    }

    [Fact]
    public void Null_allow_is_treated_as_empty()
    {
        var (settings, errors) = NetworkSettingsValidator.Normalize(new NetworkSettings(NetworkMode.Local, 8080, null!));
        Assert.Empty(errors);
        Assert.Empty(settings.Allow);
    }

    [Fact]
    public void Equivalence_compares_allow_by_content()
    {
        var a = S(NetworkMode.Network, 8080, "10.0.0.0/8");
        var b = S(NetworkMode.Network, 8080, "10.0.0.0/8");
        Assert.True(a.IsEquivalentTo(b));
        Assert.False(a.IsEquivalentTo(b with { Port = 9090 }));
    }
}
```

`src/tests/WinAdmin.Tests/NetworkEndpointsTests.cs`:

```csharp
using System.Net;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Tests;

public sealed class NetworkEndpointsTests
{
    [Fact]
    public void Local_listens_on_loopback()
    {
        var s = new NetworkSettings(NetworkMode.Local, 9090, []);
        Assert.Equal("http://127.0.0.1:9090", NetworkEndpoints.ListenUrl(s));
        Assert.Equal(IPAddress.Loopback, NetworkEndpoints.BindAddress(s));
        Assert.Equal("http://127.0.0.1:9090", NetworkEndpoints.PanelUrl(s, "server01"));
    }

    [Fact]
    public void Network_listens_on_all_interfaces_and_uses_request_host()
    {
        var s = new NetworkSettings(NetworkMode.Network, 9090, ["10.0.0.0/8"]);
        Assert.Equal("http://0.0.0.0:9090", NetworkEndpoints.ListenUrl(s));
        Assert.Equal(IPAddress.Any, NetworkEndpoints.BindAddress(s));
        Assert.Equal("http://server01:9090", NetworkEndpoints.PanelUrl(s, "server01"));
        Assert.Equal($"http://{Environment.MachineName}:9090", NetworkEndpoints.PanelUrl(s, null));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080", 8080)]
    [InlineData("http://+:9090", 9090)]
    [InlineData("http://[::]:7070", 7070)]
    [InlineData("http://localhost:5000/;https://localhost:5001", 5000)]
    [InlineData("http://localhost", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Extracts_port_from_urls(string? urls, int? expected)
        => Assert.Equal(expected, NetworkEndpoints.PortFromUrls(urls));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~NetworkSettingsValidatorTests|FullyQualifiedName~NetworkEndpointsTests"`
Expected: build FAIL — `The type or namespace name 'Network' does not exist in the namespace 'WinAdmin.Core'`.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Core/Models/NetworkModels.cs`:

```csharp
namespace WinAdmin.Core.Models;

/// <summary>Режим доступа к панели.</summary>
public enum NetworkMode
{
    /// <summary>Только этот компьютер (127.0.0.1).</summary>
    Local,
    /// <summary>Сеть: все интерфейсы + правило брандмауэра для перечисленных подсетей.</summary>
    Network,
}

/// <summary>Сетевые настройки панели (содержимое network.json).</summary>
public sealed record NetworkSettings(NetworkMode Mode, int Port, IReadOnlyList<string> Allow)
{
    public static NetworkSettings Default { get; } = new(NetworkMode.Local, 8080, []);

    /// <summary>Сравнение по содержимому (record сравнивает списки по ссылке).</summary>
    public bool IsEquivalentTo(NetworkSettings other)
        => Mode == other.Mode && Port == other.Port && (Allow ?? []).SequenceEqual(other.Allow ?? []);
}

/// <summary>Текущие сетевые настройки для UI.</summary>
public sealed record NetworkSettingsDto(NetworkMode Mode, int Port, IReadOnlyList<string> Allow, string Url, bool FirewallRule);

/// <summary>Запрос на изменение сетевых настроек.</summary>
public sealed record UpdateNetworkSettingsRequest(NetworkMode Mode, int Port, List<string>? Allow);

/// <summary>Результат изменения: новый адрес панели.</summary>
public sealed record NetworkUpdateResult(string Url);
```

`src/backend/WinAdmin.Core/Network/NetworkSettingsValidator.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Network;

/// <summary>Нормализация и проверка сетевых настроек.</summary>
public static class NetworkSettingsValidator
{
    public static (NetworkSettings Settings, IReadOnlyList<string> Errors) Normalize(NetworkSettings input)
    {
        var errors = new List<string>();
        if (input.Port is < 1 or > 65535)
            errors.Add("Порт должен быть от 1 до 65535.");

        var allow = new List<string>();
        bool badItems = false;
        foreach (var raw in input.Allow ?? [])
        {
            string item = raw?.Trim() ?? "";
            if (item.Length == 0) continue;
            if (!IsAddressOrCidr(item))
            {
                errors.Add($"«{item}» — не IP-адрес и не подсеть CIDR (например 10.0.0.0/24).");
                badItems = true;
                continue;
            }
            if (!allow.Contains(item, StringComparer.OrdinalIgnoreCase))
                allow.Add(item);
        }

        if (input.Mode == NetworkMode.Network && allow.Count == 0 && !badItems)
            errors.Add("Для режима «Сеть» укажите хотя бы один разрешённый адрес или подсеть.");

        return (input with { Allow = allow }, errors);
    }

    private static bool IsAddressOrCidr(string value)
    {
        int slash = value.IndexOf('/');
        string addressPart = slash < 0 ? value : value[..slash];
        if (!IPAddress.TryParse(addressPart, out var ip))
            return false;
        // TryParse принимает «10» как 0.0.0.10 — требуем полную запись IPv4.
        if (ip.AddressFamily == AddressFamily.InterNetwork && addressPart.Count(c => c == '.') != 3)
            return false;
        if (slash < 0)
            return true;

        int max = ip.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
            && prefix >= 1 && prefix <= max; // /0 — «любой адрес», запрещено
    }
}
```

`src/backend/WinAdmin.Core/Network/NetworkEndpoints.cs`:

```csharp
using System.Globalization;
using System.Net;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Network;

/// <summary>Адреса прослушивания и панели для сетевых настроек.</summary>
public static class NetworkEndpoints
{
    public const int DefaultPort = 8080;

    public static IPAddress BindAddress(NetworkSettings s)
        => s.Mode == NetworkMode.Local ? IPAddress.Loopback : IPAddress.Any;

    /// <summary>URL для Kestrel:Endpoints.</summary>
    public static string ListenUrl(NetworkSettings s) => $"http://{BindAddress(s)}:{s.Port}";

    /// <summary>Адрес, по которому открывать панель после применения настроек.</summary>
    public static string PanelUrl(NetworkSettings s, string? host)
        => s.Mode == NetworkMode.Local
            ? $"http://127.0.0.1:{s.Port}"
            : $"http://{(string.IsNullOrWhiteSpace(host) ? Environment.MachineName : host)}:{s.Port}";

    /// <summary>Порт первого адреса из значения --urls (например «http://0.0.0.0:8080»).</summary>
    public static int? PortFromUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return null;
        string first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        int scheme = first.IndexOf("://", StringComparison.Ordinal);
        string rest = scheme < 0 ? first : first[(scheme + 3)..];
        int slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest[..slash];
        int colon = rest.LastIndexOf(':');
        if (colon < 0 || colon < rest.LastIndexOf(']')) return null;
        return int.TryParse(rest[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~NetworkSettingsValidatorTests|FullyQualifiedName~NetworkEndpointsTests"`
Expected: PASS (все).

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core src/tests/WinAdmin.Tests/NetworkSettingsValidatorTests.cs src/tests/WinAdmin.Tests/NetworkEndpointsTests.cs
git commit -m "feat(core): add network settings model, validator and endpoints"
```

---

### Task 2: Пути и хранилище `network.json` (Infrastructure)

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/WinAdminPaths.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsStore.cs`
- Test: `src/tests/WinAdmin.Tests/NetworkSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `NetworkSettings`, `NetworkMode`, `NetworkSettingsValidator.Normalize`, `NetworkEndpoints.DefaultPort` (Task 1).
- Produces:
  - `static class WinAdminPaths { const string NetworkFileName = "network.json"; string DatabasePath(string? configured); string DataDirectory(string databasePath); }`
  - `sealed class NetworkSettingsStore(string dataDirectory)` с `string DataDirectory`, `string FilePath`, `bool Exists`, `static JsonSerializerOptions JsonOptions`, `NetworkSettings ReadOrDefault(out string? error)`, `void Write(NetworkSettings)`, `void EnsureCreated(int? port)`.

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/NetworkSettingsStoreTests.cs`:

```csharp
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-net-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;

    public NetworkSettingsStoreTests() => _store = new NetworkSettingsStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void EnsureCreated_writes_local_with_port_from_urls()
    {
        _store.EnsureCreated(9191);
        var s = _store.ReadOrDefault(out var error);
        Assert.Null(error);
        Assert.Equal(NetworkMode.Local, s.Mode);
        Assert.Equal(9191, s.Port);
        Assert.Contains("\"mode\": \"local\"", File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public void EnsureCreated_defaults_to_8080_and_does_not_overwrite_existing()
    {
        _store.EnsureCreated(null);
        Assert.Equal(8080, _store.ReadOrDefault(out _).Port);

        _store.Write(new NetworkSettings(NetworkMode.Network, 7000, ["10.0.0.0/8"]));
        _store.EnsureCreated(9999);
        var s = _store.ReadOrDefault(out _);
        Assert.Equal(NetworkMode.Network, s.Mode);
        Assert.Equal(7000, s.Port);
        Assert.Equal(new[] { "10.0.0.0/8" }, s.Allow);
    }

    [Fact]
    public void Missing_file_returns_default_without_error()
    {
        var s = _store.ReadOrDefault(out var error);
        Assert.Null(error);
        Assert.True(s.IsEquivalentTo(NetworkSettings.Default));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"mode\": \"local\", \"port\": 0, \"allow\": [] }")]
    [InlineData("{ \"mode\": \"network\", \"port\": 8080, \"allow\": [] }")]
    [InlineData("{ \"mode\": \"banana\", \"port\": 8080 }")]
    public void Broken_file_returns_default_with_error_and_is_not_overwritten(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.FilePath, content);

        var s = _store.ReadOrDefault(out var error);
        _store.EnsureCreated(9999);

        Assert.NotNull(error);
        Assert.True(s.IsEquivalentTo(NetworkSettings.Default));
        Assert.Equal(content, File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public void Write_is_atomic_and_leaves_no_temp_file()
    {
        _store.Write(new NetworkSettings(NetworkMode.Local, 8081, []));
        _store.Write(new NetworkSettings(NetworkMode.Local, 8082, []));
        Assert.Equal(8082, _store.ReadOrDefault(out _).Port);
        Assert.Equal(new[] { _store.FilePath }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Paths_resolve_data_directory_from_database_path()
    {
        Assert.Equal(@"C:\ProgramData\WinAdmin", WinAdminPaths.DataDirectory(@"C:\ProgramData\WinAdmin\WinAdmin.db"));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "WinAdmin.db"), WinAdminPaths.DatabasePath(null));
        Assert.Equal(@"D:\x.db", WinAdminPaths.DatabasePath(@"D:\x.db"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~NetworkSettingsStoreTests`
Expected: build FAIL — `NetworkSettingsStore` / `WinAdminPaths` не найдены.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Infrastructure/WinAdminPaths.cs`:

```csharp
namespace WinAdmin.Infrastructure;

/// <summary>Единый расчёт путей к БД и каталогу данных (веб-режим и CLI).</summary>
public static class WinAdminPaths
{
    public const string NetworkFileName = "network.json";

    /// <summary>Путь к SQLite: WinAdmin:DatabasePath либо WinAdmin.db рядом с exe.</summary>
    public static string DatabasePath(string? configured)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db")
            : configured;

    /// <summary>Каталог данных — папка, где лежит БД.</summary>
    public static string DataDirectory(string databasePath)
        => Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? AppContext.BaseDirectory;
}
```

`src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Файл network.json: чтение, атомарная запись, первичное создание.</summary>
public sealed class NetworkSettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public NetworkSettingsStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        FilePath = Path.Combine(dataDirectory, WinAdminPaths.NetworkFileName);
    }

    public string DataDirectory { get; }
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);

    /// <summary>
    /// Читает настройки. Нет файла → значения по умолчанию без ошибки.
    /// Файл битый/невалидный → значения по умолчанию и текст ошибки (файл не трогаем).
    /// </summary>
    public NetworkSettings ReadOrDefault(out string? error)
    {
        error = null;
        if (!Exists) return NetworkSettings.Default;
        try
        {
            var parsed = JsonSerializer.Deserialize<NetworkSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (parsed is null)
            {
                error = "файл пуст";
                return NetworkSettings.Default;
            }
            var (settings, errors) = NetworkSettingsValidator.Normalize(parsed);
            if (errors.Count > 0)
            {
                error = string.Join(" ", errors);
                return NetworkSettings.Default;
            }
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return NetworkSettings.Default;
        }
    }

    /// <summary>Атомарная запись: временный файл + замена.</summary>
    public void Write(NetworkSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Создаёт network.json (режим Local), если его ещё нет.</summary>
    public void EnsureCreated(int? port)
    {
        if (Exists) return;
        Write(NetworkSettings.Default with { Port = port ?? NetworkEndpoints.DefaultPort });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~NetworkSettingsStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/WinAdminPaths.cs src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsStore.cs src/tests/WinAdmin.Tests/NetworkSettingsStoreTests.cs
git commit -m "feat(infra): add network.json store and shared paths"
```

---

### Task 3: Брандмауэр (`netsh`) и проверка портов

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Network/FirewallCommands.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Network/NetshFirewallRunner.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Network/PortProbe.cs`
- Test: `src/tests/WinAdmin.Tests/FirewallCommandsTests.cs`
- Test: `src/tests/WinAdmin.Tests/PortProbeTests.cs`

**Interfaces:**
- Consumes: `NetworkSettings`, `NetworkMode`, `NetworkEndpoints` (Task 1).
- Produces:
  - `sealed record FirewallCommand(IReadOnlyList<string> Args, bool IgnoreFailure)`
  - `static class FirewallCommands { const string ManagedRuleName = "WinAdmin (managed)"; static string LegacyRuleName(int port); static IReadOnlyList<FirewallCommand> Build(NetworkSettings settings, IEnumerable<int> legacyPorts); }`
  - `interface IFirewallRunner { void Run(IReadOnlyList<FirewallCommand> commands); }` + `sealed class NetshFirewallRunner : IFirewallRunner`
  - `interface IPortProbe { bool IsInUse(int port); bool IsListening(NetworkSettings settings); }` + `sealed class SystemPortProbe : IPortProbe`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/FirewallCommandsTests.cs`:

```csharp
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class FirewallCommandsTests
{
    [Fact]
    public void Local_mode_only_deletes_rules()
    {
        var cmds = FirewallCommands.Build(new NetworkSettings(NetworkMode.Local, 9090, ["10.0.0.0/8"]), [8080]);

        Assert.All(cmds, c => Assert.Equal("delete", c.Args[2]));
        Assert.All(cmds, c => Assert.True(c.IgnoreFailure));
        var names = cmds.Select(c => c.Args[4]).ToList();
        Assert.Contains("name=WinAdmin (managed)", names);
        Assert.Contains("name=WinAdmin HTTP 8080", names);
        Assert.Contains("name=WinAdmin HTTP 9090", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Network_mode_recreates_managed_rule_for_allowed_subnets_only()
    {
        var cmds = FirewallCommands.Build(new NetworkSettings(NetworkMode.Network, 9090, ["10.77.77.0/24", "192.168.88.5"]), []);

        var add = Assert.Single(cmds, c => c.Args[2] == "add");
        Assert.False(add.IgnoreFailure);
        Assert.Equal(
            new[] { "advfirewall", "firewall", "add", "rule", "name=WinAdmin (managed)", "dir=in", "action=allow",
                    "protocol=TCP", "localport=9090", "remoteip=10.77.77.0/24,192.168.88.5", "profile=domain,private" },
            add.Args);
        Assert.Same(add, cmds[^1]); // add — последней, после всех delete
    }
}
```

`src/tests/WinAdmin.Tests/PortProbeTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class PortProbeTests
{
    [Fact]
    public void Detects_listening_loopback_port()
    {
        var probe = new SystemPortProbe();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(probe.IsInUse(port));
            Assert.True(probe.IsListening(new NetworkSettings(NetworkMode.Local, port, [])));
            Assert.False(probe.IsListening(new NetworkSettings(NetworkMode.Network, port, ["10.0.0.0/8"])));
        }
        finally
        {
            listener.Stop();
        }
        Assert.False(probe.IsInUse(port));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~FirewallCommandsTests|FullyQualifiedName~PortProbeTests"`
Expected: build FAIL — типы не найдены.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Infrastructure/Network/FirewallCommands.cs`:

```csharp
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Одна команда netsh (argv без «netsh.exe»).</summary>
public sealed record FirewallCommand(IReadOnlyList<string> Args, bool IgnoreFailure);

/// <summary>Построение команд netsh для правила брандмауэра WinAdmin.</summary>
public static class FirewallCommands
{
    public const string ManagedRuleName = "WinAdmin (managed)";

    /// <summary>Имя правил, которые создавали install-service.ps1 и winadmin-ctl до 1.0.6.</summary>
    public static string LegacyRuleName(int port) => $"WinAdmin HTTP {port}";

    public static IReadOnlyList<FirewallCommand> Build(NetworkSettings settings, IEnumerable<int> legacyPorts)
    {
        var commands = new List<FirewallCommand> { Delete(ManagedRuleName) };
        foreach (int port in legacyPorts.Append(settings.Port).Append(NetworkEndpoints.DefaultPort).Distinct())
            commands.Add(Delete(LegacyRuleName(port)));

        if (settings.Mode == NetworkMode.Network)
        {
            commands.Add(new FirewallCommand(
            [
                "advfirewall", "firewall", "add", "rule",
                $"name={ManagedRuleName}", "dir=in", "action=allow", "protocol=TCP",
                $"localport={settings.Port}",
                $"remoteip={string.Join(',', settings.Allow)}",
                "profile=domain,private",
            ], IgnoreFailure: false));
        }
        return commands;
    }

    // Удаление несуществующего правила — код 1 «No rules match», это не ошибка.
    private static FirewallCommand Delete(string name)
        => new(["advfirewall", "firewall", "delete", "rule", $"name={name}"], IgnoreFailure: true);
}
```

`src/backend/WinAdmin.Infrastructure/Network/NetshFirewallRunner.cs`:

```csharp
using System.Diagnostics;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Выполняет команды брандмауэра.</summary>
public interface IFirewallRunner
{
    /// <summary>Выполняет команды по порядку; бросает InvalidOperationException на первой ошибке без IgnoreFailure.</summary>
    void Run(IReadOnlyList<FirewallCommand> commands);
}

/// <summary>Реализация через netsh advfirewall (не требует PowerShell).</summary>
public sealed class NetshFirewallRunner : IFirewallRunner
{
    public void Run(IReadOnlyList<FirewallCommand> commands)
    {
        foreach (var command in commands)
        {
            var psi = new ProcessStartInfo("netsh.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in command.Args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить netsh.exe.");
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 && !command.IgnoreFailure)
                throw new InvalidOperationException(
                    $"netsh {string.Join(' ', command.Args)} завершился с кодом {process.ExitCode}: {output.Trim()}");
        }
    }
}
```

`src/backend/WinAdmin.Infrastructure/Network/PortProbe.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Проверка занятости портов (без открытия сокетов — без запроса брандмауэра).</summary>
public interface IPortProbe
{
    /// <summary>Порт слушает кто-либо на любом адресе.</summary>
    bool IsInUse(int port);

    /// <summary>Endpoint из настроек сейчас слушается.</summary>
    bool IsListening(NetworkSettings settings);
}

public sealed class SystemPortProbe : IPortProbe
{
    public bool IsInUse(int port) => Listeners().Any(e => e.Port == port);

    public bool IsListening(NetworkSettings settings)
    {
        var address = NetworkEndpoints.BindAddress(settings);
        return Listeners().Any(e => e.Port == settings.Port && e.Address.Equals(address));
    }

    private static IPEndPoint[] Listeners() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~FirewallCommandsTests|FullyQualifiedName~PortProbeTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/Network src/tests/WinAdmin.Tests/FirewallCommandsTests.cs src/tests/WinAdmin.Tests/PortProbeTests.cs
git commit -m "feat(infra): add netsh firewall commands and port probe"
```

---

### Task 4: `NetworkSettingsService` и регистрация в DI

**Files:**
- Create: `src/backend/WinAdmin.Core/Abstractions/INetworkSettingsService.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsService.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Network/NetworkServiceCollectionExtensions.cs`
- Test: `src/tests/WinAdmin.Tests/NetworkSettingsServiceTests.cs`

**Interfaces:**
- Consumes: Task 1–3 (`NetworkSettingsValidator`, `NetworkSettingsStore`, `FirewallCommands`, `IFirewallRunner`, `IPortProbe`).
- Produces:
  - `sealed record NetworkCheckResult(NetworkSettings Normalized, IReadOnlyList<string> Errors, bool PortBusy)` с `bool Ok`
  - `interface INetworkSettingsService { NetworkSettings Current { get; } NetworkCheckResult Check(NetworkSettings next); void Apply(NetworkSettings next, NetworkSettings previous); void ApplyFirewall(NetworkSettings settings, IEnumerable<int> legacyPorts); }`
  - `IServiceCollection AddWinAdminNetwork(this IServiceCollection services, NetworkSettingsStore store)` — регистрирует store, `IFirewallRunner`→`NetshFirewallRunner`, `IPortProbe`→`SystemPortProbe`, `INetworkSettingsService`→`NetworkSettingsService` (все singleton).

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/NetworkSettingsServiceTests.cs`:

```csharp
using Moq;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-netsvc-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;
    private readonly Mock<IFirewallRunner> _firewall = new();
    private readonly Mock<IPortProbe> _probe = new();
    private readonly NetworkSettingsService _service;

    public NetworkSettingsServiceTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, 8080, []));
        _service = new NetworkSettingsService(_store, _firewall.Object, _probe.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Same_port_is_not_reported_busy_even_though_we_listen_on_it()
    {
        _probe.Setup(p => p.IsInUse(8080)).Returns(true);
        var result = _service.Check(new NetworkSettings(NetworkMode.Network, 8080, ["10.0.0.0/8"]));
        Assert.True(result.Ok);
        _probe.Verify(p => p.IsInUse(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Busy_new_port_is_reported()
    {
        _probe.Setup(p => p.IsInUse(9090)).Returns(true);
        var result = _service.Check(new NetworkSettings(NetworkMode.Local, 9090, []));
        Assert.True(result.PortBusy);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Invalid_settings_skip_port_probe()
    {
        var result = _service.Check(new NetworkSettings(NetworkMode.Network, 9090, []));
        Assert.NotEmpty(result.Errors);
        _probe.Verify(p => p.IsInUse(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Apply_runs_firewall_then_writes_file_and_cleans_previous_port_rule()
    {
        IReadOnlyList<FirewallCommand>? ran = null;
        _firewall.Setup(f => f.Run(It.IsAny<IReadOnlyList<FirewallCommand>>())).Callback<IReadOnlyList<FirewallCommand>>(c => ran = c);
        var previous = _service.Current;
        var next = new NetworkSettings(NetworkMode.Network, 9090, ["10.0.0.0/8"]);

        _service.Apply(next, previous);

        Assert.True(_service.Current.IsEquivalentTo(next));
        Assert.NotNull(ran);
        Assert.Contains(ran!, c => c.Args.Contains("name=WinAdmin HTTP 8080"));
        Assert.Contains(ran!, c => c.Args[2] == "add");
    }

    [Fact]
    public void Firewall_failure_leaves_file_untouched()
    {
        _firewall.Setup(f => f.Run(It.IsAny<IReadOnlyList<FirewallCommand>>()))
            .Throws(new InvalidOperationException("Требуется повышение прав"));
        var previous = _service.Current;

        Assert.Throws<InvalidOperationException>(() =>
            _service.Apply(new NetworkSettings(NetworkMode.Network, 9090, ["10.0.0.0/8"]), previous));

        Assert.True(_service.Current.IsEquivalentTo(previous));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~NetworkSettingsServiceTests`
Expected: build FAIL — `NetworkSettingsService` не найден.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Core/Abstractions/INetworkSettingsService.cs`:

```csharp
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

/// <summary>Результат проверки новых сетевых настроек.</summary>
public sealed record NetworkCheckResult(NetworkSettings Normalized, IReadOnlyList<string> Errors, bool PortBusy)
{
    public bool Ok => Errors.Count == 0 && !PortBusy;
}

/// <summary>Сетевые настройки панели: чтение, проверка, применение (брандмауэр + network.json).</summary>
public interface INetworkSettingsService
{
    /// <summary>Действующие настройки (битый файл → значения по умолчанию).</summary>
    NetworkSettings Current { get; }

    /// <summary>Нормализует и проверяет; порт проверяется на занятость, только если он меняется.</summary>
    NetworkCheckResult Check(NetworkSettings next);

    /// <summary>Применяет брандмауэр, затем пишет network.json. Ошибка брандмауэра → файл не меняется.</summary>
    void Apply(NetworkSettings next, NetworkSettings previous);

    /// <summary>Приводит правило брандмауэра к настройкам и удаляет устаревшие правила для legacyPorts.</summary>
    void ApplyFirewall(NetworkSettings settings, IEnumerable<int> legacyPorts);
}
```

`src/backend/WinAdmin.Infrastructure/Network/NetworkSettingsService.cs`:

```csharp
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

public sealed class NetworkSettingsService : INetworkSettingsService
{
    private readonly NetworkSettingsStore _store;
    private readonly IFirewallRunner _firewall;
    private readonly IPortProbe _probe;

    public NetworkSettingsService(NetworkSettingsStore store, IFirewallRunner firewall, IPortProbe probe)
    {
        _store = store;
        _firewall = firewall;
        _probe = probe;
    }

    public NetworkSettings Current => _store.ReadOrDefault(out _);

    public NetworkCheckResult Check(NetworkSettings next)
    {
        var (normalized, errors) = NetworkSettingsValidator.Normalize(next);
        bool busy = errors.Count == 0
            && normalized.Port != Current.Port
            && _probe.IsInUse(normalized.Port);
        return new NetworkCheckResult(normalized, errors, busy);
    }

    public void Apply(NetworkSettings next, NetworkSettings previous)
    {
        ApplyFirewall(next, [previous.Port]);
        _store.Write(next);
    }

    public void ApplyFirewall(NetworkSettings settings, IEnumerable<int> legacyPorts)
        => _firewall.Run(FirewallCommands.Build(settings, legacyPorts));
}
```

`src/backend/WinAdmin.Infrastructure/Network/NetworkServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Network;

public static class NetworkServiceCollectionExtensions
{
    /// <summary>Сетевые настройки панели (используется и веб-режимом, и CLI).</summary>
    public static IServiceCollection AddWinAdminNetwork(this IServiceCollection services, NetworkSettingsStore store)
    {
        services.AddSingleton(store);
        services.AddSingleton<IFirewallRunner, NetshFirewallRunner>();
        services.AddSingleton<IPortProbe, SystemPortProbe>();
        services.AddSingleton<INetworkSettingsService, NetworkSettingsService>();
        return services;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~NetworkSettingsServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core/Abstractions/INetworkSettingsService.cs src/backend/WinAdmin.Infrastructure/Network src/tests/WinAdmin.Tests/NetworkSettingsServiceTests.cs
git commit -m "feat(infra): add network settings service"
```

---

### Task 5: Kestrel из `network.json` с перепривязкой на лету + подключение в `Program.cs`

**Files:**
- Create: `src/backend/WinAdmin.Api/Network/NetworkConfigurationSource.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (блок «Конфигурация», регистрация сервисов, блок после bootstrap, удалить `UseHttpsRedirection`)
- Modify: `src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj` (ссылка на Api + `Microsoft.AspNetCore.Mvc.Testing`)
- Test: `src/tests/WinAdmin.Tests/KestrelRebindTests.cs`

**Interfaces:**
- Consumes: `NetworkSettingsStore`, `NetworkEndpoints.ListenUrl`, `WinAdminPaths`, `AddWinAdminNetwork` (Tasks 1–4).
- Produces: `sealed class NetworkConfigurationSource(NetworkSettingsStore store) : IConfigurationSource` со свойством `NetworkConfigurationProvider? Provider`; `NetworkConfigurationProvider.LastError` (`string?`). `Program.cs` экспортирует в DI `NetworkSettingsStore` и `INetworkSettingsService`.

- [ ] **Step 1: Add Api reference and test package to the test project**

В `src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj` добавить в `ItemGroup` с пакетами:

```xml
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.9" />
```

и в `ItemGroup` с проектами:

```xml
    <ProjectReference Include="..\..\backend\WinAdmin.Api\WinAdmin.Api.csproj" />
```

Run: `dotnet build src/tests/WinAdmin.Tests`
Expected: Build succeeded. (Если версии 10.0.9 нет — взять последнюю 10.0.x, которую выдаст `dotnet add src/tests/WinAdmin.Tests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.*`.)

- [ ] **Step 2: Write the failing test**

`src/tests/WinAdmin.Tests/KestrelRebindTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using WinAdmin.Api.Network;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class KestrelRebindTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-kestrel-" + Guid.NewGuid().ToString("N"));
    private NetworkSettingsStore _store = null!;
    private WebApplication _app = null!;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task InitializeAsync()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, FreePort(), []));

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Add(new NetworkConfigurationSource(_store));
        _app = builder.Build();
        _app.MapGet("/", () => "ok");
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _http.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Rebinds_to_new_port_when_file_changes()
    {
        int oldPort = _store.ReadOrDefault(out _).Port;
        Assert.True(await RespondsAsync($"http://127.0.0.1:{oldPort}/"));

        int newPort = FreePort();
        _store.Write(new NetworkSettings(NetworkMode.Local, newPort, []));

        Assert.True(await EventuallyAsync(() => RespondsAsync($"http://127.0.0.1:{newPort}/")));
        Assert.True(await EventuallyAsync(async () => !await RespondsAsync($"http://127.0.0.1:{oldPort}/")));
    }

    [Fact]
    public async Task Rebinds_when_only_mode_changes()
    {
        var current = _store.ReadOrDefault(out _);
        _store.Write(current with { Mode = NetworkMode.Network, Allow = ["10.0.0.0/8"] });

        var probe = new SystemPortProbe();
        Assert.True(await EventuallyAsync(() =>
            Task.FromResult(probe.IsListening(current with { Mode = NetworkMode.Network }))));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task<bool> RespondsAsync(string url)
    {
        try { return (await _http.GetStringAsync(url)) == "ok"; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 50; i++)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }
        return false;
    }
}
```

Примечание: тест режима `Network` слушает `0.0.0.0` от имени текущего пользователя — Windows может один раз показать запрос брандмауэра для `testhost.exe`; выбор не влияет на результат (проверяется таблица слушающих сокетов, а не входящее подключение).

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~KestrelRebindTests`
Expected: build FAIL — `WinAdmin.Api.Network` не существует.

- [ ] **Step 4: Implement the configuration source**

`src/backend/WinAdmin.Api/Network/NetworkConfigurationSource.cs`:

```csharp
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Network;

/// <summary>
/// Источник конфигурации Kestrel:Endpoints из network.json. Kestrel загружает секцию
/// «Kestrel» с reloadOnChange, поэтому изменение файла перепривязывает endpoint без
/// перезапуска процесса.
/// </summary>
public sealed class NetworkConfigurationSource : IConfigurationSource
{
    private readonly NetworkSettingsStore _store;

    public NetworkConfigurationSource(NetworkSettingsStore store) => _store = store;

    /// <summary>Последний созданный провайдер (для чтения LastError после старта).</summary>
    public NetworkConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => Provider = new NetworkConfigurationProvider(_store);
}

public sealed class NetworkConfigurationProvider : ConfigurationProvider, IDisposable
{
    private const string EndpointKey = "Kestrel:Endpoints:Http:Url";

    private readonly NetworkSettingsStore _store;
    private readonly PhysicalFileProvider? _files;
    private readonly IDisposable? _watch;

    public NetworkConfigurationProvider(NetworkSettingsStore store)
    {
        _store = store;
        if (Directory.Exists(store.DataDirectory))
        {
            _files = new PhysicalFileProvider(store.DataDirectory);
            _watch = ChangeToken.OnChange(
                () => _files.Watch(WinAdminPaths.NetworkFileName),
                () =>
                {
                    // Даём писателю закончить (как JsonConfigurationProvider: ReloadDelay 250 мс).
                    Thread.Sleep(250);
                    Load();
                    OnReload();
                });
        }
    }

    /// <summary>Ошибка чтения network.json (null — файл в порядке или отсутствует).</summary>
    public string? LastError { get; private set; }

    public override void Load()
    {
        var settings = _store.ReadOrDefault(out var error);
        LastError = error;
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EndpointKey] = NetworkEndpoints.ListenUrl(settings),
        };
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _files?.Dispose();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~KestrelRebindTests`
Expected: PASS (оба теста). Если `Rebinds_*` падают, значит Kestrel не перечитывает секцию — остановиться и сообщить (это опровергает ключевое допущение спецификации).

- [ ] **Step 6: Wire into `Program.cs`**

В `src/backend/WinAdmin.Api/Program.cs`:

1. В `using` добавить:

```csharp
using Microsoft.Extensions.Hosting.WindowsServices;
using WinAdmin.Api.Network;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Hardening;
using WinAdmin.Infrastructure.Network;
```

(`WinAdmin.Infrastructure.Hardening` появится в Task 8 — до тех пор не добавлять эту строку.)

2. Заменить блок

```csharp
string? configuredDbPath = builder.Configuration["WinAdmin:DatabasePath"];
string dbPath = string.IsNullOrWhiteSpace(configuredDbPath)
    ? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db")
    : configuredDbPath;
string connectionString = $"Data Source={dbPath}";
```

на

```csharp
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
builder.Configuration.Add(networkSource);
```

3. Сразу после `builder.Services.AddWinAdminInfrastructure(connectionString, jwtOptions);` добавить:

```csharp
builder.Services.AddWinAdminNetwork(networkStore);
```

4. После закрывающей скобки блока `using (var scope = app.Services.CreateScope()) { ... }` (bootstrap) добавить:

```csharp
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
```

5. Удалить строки

```csharp
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
```

и пустую строку после них.

- [ ] **Step 7: Build and run full test suite**

Run: `dotnet build src/backend/WinAdmin.Api && dotnet test src/tests/WinAdmin.Tests`
Expected: Build succeeded; все тесты PASS (93 старых + новые).

- [ ] **Step 8: Commit**

```bash
git add src/backend/WinAdmin.Api src/tests/WinAdmin.Tests
git commit -m "feat(api): bind Kestrel from network.json with live rebind"
```

---

### Task 6: API `GET/PUT /api/v1/settings/network`, аудит и откат

**Files:**
- Create: `src/backend/WinAdmin.Api/Network/NetworkApplyWatchdog.cs`
- Create: `src/backend/WinAdmin.Api/Controllers/NetworkSettingsController.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (регистрация watchdog)
- Test: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs`

**Interfaces:**
- Consumes: `INetworkSettingsService`, `NetworkCheckResult`, `IPortProbe`, `IAuditService`, `NetworkEndpoints.PanelUrl`, DTO из Task 1.
- Produces: `sealed class NetworkApplyWatchdog` с `Task Schedule(NetworkSettings previous, NetworkSettings applied, string actor, string? sourceIp)`; конфиг `WinAdmin:Network:VerifyDelaySeconds` (по умолчанию 10).

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

/// <summary>Общий хост для API-тестов: временный каталог данных, фейковые брандмауэр и проба портов.</summary>
public sealed class NetworkApiFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "winadmin-api-" + Guid.NewGuid().ToString("N"));
    public FakeFirewall Firewall { get; } = new();
    public HashSet<int> BusyPorts { get; } = [];

    public NetworkApiFactory()
    {
        Directory.CreateDirectory(DataDir);
        // Program читает конфигурацию до Build(), поэтому — через переменные окружения.
        Environment.SetEnvironmentVariable("WinAdmin__DatabasePath", Path.Combine(DataDir, "WinAdmin.db"));
        Environment.SetEnvironmentVariable("WinAdmin__Network__VerifyDelaySeconds", "3600");
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_WINADMIN", AppContext.BaseDirectory);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFirewallRunner>();
            services.AddSingleton<IFirewallRunner>(Firewall);
            services.RemoveAll<IPortProbe>();
            services.AddSingleton<IPortProbe>(new FakeProbe(BusyPorts));
        });
    }

    public async Task<HttpClient> ClientWithScopesAsync(params string[] scopes)
    {
        using var scope = Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await keys.CreateAsync(new CreateApiKeyRequest { Name = "t-" + Guid.NewGuid().ToString("N"), Scopes = [.. scopes] });
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", created.PlaintextKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("WinAdmin__DatabasePath", null);
        Environment.SetEnvironmentVariable("WinAdmin__Network__VerifyDelaySeconds", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }

    public sealed class FakeFirewall : IFirewallRunner
    {
        public List<IReadOnlyList<FirewallCommand>> Calls { get; } = [];
        public void Run(IReadOnlyList<FirewallCommand> commands) { lock (Calls) Calls.Add(commands); }
    }

    private sealed class FakeProbe(HashSet<int> busy) : IPortProbe
    {
        public bool IsInUse(int port) => busy.Contains(port);
        public bool IsListening(NetworkSettings settings) => true;
    }
}

[CollectionDefinition("network-api", DisableParallelization = true)]
public sealed class NetworkApiCollection : ICollectionFixture<NetworkApiFactory>;

[Collection("network-api")]
public sealed class NetworkSettingsApiTests(NetworkApiFactory factory)
{
    private string NetworkFile => Path.Combine(factory.DataDir, "network.json");

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/settings/network");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requires_admin_scope()
    {
        var client = await factory.ClientWithScopesAsync("system.read");
        var response = await client.GetAsync("/api/v1/settings/network");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_local_defaults_created_at_startup()
    {
        var client = await factory.ClientWithScopesAsync("admin");
        var dto = await client.GetFromJsonAsync<NetworkSettingsDto>("/api/v1/settings/network", JsonOpts);
        Assert.NotNull(dto);
        Assert.Equal(NetworkMode.Local, dto!.Mode);
        Assert.False(dto.FirewallRule);
        Assert.StartsWith("http://127.0.0.1:", dto.Url);
    }

    [Fact]
    public async Task Invalid_settings_return_400_and_do_not_touch_file()
    {
        var client = await factory.ClientWithScopesAsync("admin");
        string before = File.ReadAllText(NetworkFile);
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Network", port = 9090, allow = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, File.ReadAllText(NetworkFile));
    }

    [Fact]
    public async Task Busy_port_returns_409()
    {
        factory.BusyPorts.Add(9555);
        var client = await factory.ClientWithScopesAsync("admin");
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Local", port = 9555, allow = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Valid_settings_return_url_then_write_file_and_audit()
    {
        var client = await factory.ClientWithScopesAsync("admin");
        int port = 9000 + Random.Shared.Next(500);
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Network", port, allow = new[] { "10.77.77.0/24" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NetworkUpdateResult>(JsonOpts);
        Assert.Equal($"http://localhost:{port}", result!.Url);

        // Запись происходит после отправки ответа (Response.OnCompleted).
        bool written = false;
        for (int i = 0; i < 50 && !written; i++)
        {
            written = File.ReadAllText(NetworkFile).Contains($"\"port\": {port}");
            if (!written) await Task.Delay(100);
        }
        Assert.True(written);
        Assert.Contains(factory.Firewall.Calls, c => c.Any(x => x.Args.Contains("remoteip=10.77.77.0/24")));

        using var scope = factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<IAuditService>().QueryAsync();
        Assert.Contains(audit, a => a.Action == "settings.network" && a.Success);

        // Вернуть Local, чтобы не влиять на другие тесты коллекции.
        await client.PutAsJsonAsync("/api/v1/settings/network", new { mode = "Local", port, allow = Array.Empty<string>() });
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~NetworkSettingsApiTests`
Expected: `Requires_authentication` может пройти (fallback отдаёт index.html → 200, тогда FAIL), остальные — FAIL с 404/200 вместо ожидаемых кодов, т.к. контроллера нет. Если вместо этого падает старт хоста с ошибкой про content root — проверить, что переменная `ASPNETCORE_TEST_CONTENTROOT_WINADMIN` задаётся в конструкторе фабрики (имя сборки API — `WinAdmin`).

- [ ] **Step 3: Implement the watchdog**

`src/backend/WinAdmin.Api/Network/NetworkApplyWatchdog.cs`:

```csharp
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Network;

/// <summary>
/// После применения сетевых настроек проверяет, что Kestrel действительно слушает новый
/// endpoint; если нет — возвращает прежние настройки (защита от потери доступа).
/// </summary>
public sealed class NetworkApplyWatchdog
{
    private readonly INetworkSettingsService _network;
    private readonly IPortProbe _probe;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<NetworkApplyWatchdog> _logger;

    public NetworkApplyWatchdog(
        INetworkSettingsService network, IPortProbe probe, IServiceScopeFactory scopes,
        IConfiguration config, ILogger<NetworkApplyWatchdog> logger)
    {
        _network = network;
        _probe = probe;
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    public Task Schedule(NetworkSettings previous, NetworkSettings applied, string actor, string? sourceIp)
        => Task.Run(async () =>
        {
            int delay = _config.GetValue("WinAdmin:Network:VerifyDelaySeconds", 10);
            await Task.Delay(TimeSpan.FromSeconds(delay));
            if (_probe.IsListening(applied))
                return;

            _logger.LogError("Панель не слушает {Url} после применения настроек — откат к {Previous}.",
                NetworkEndpoints.ListenUrl(applied), NetworkEndpoints.ListenUrl(previous));
            string details = $"Не удалось начать прослушивание {NetworkEndpoints.ListenUrl(applied)}; возвращено {NetworkEndpoints.ListenUrl(previous)}";
            try
            {
                _network.Apply(previous, applied);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Откат сетевых настроек не удался.");
                details += $"; ошибка отката: {ex.Message}";
            }

            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
            {
                Actor = actor,
                Action = "settings.network.rollback",
                Target = "network",
                Success = false,
                Details = details,
                SourceIp = sourceIp,
            });
        });
}
```

- [ ] **Step 4: Implement the controller**

`src/backend/WinAdmin.Api/Controllers/NetworkSettingsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Network;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>
/// Сетевые настройки панели: режим (только этот компьютер / сеть), порт и разрешённые
/// подсети (требует scope admin). Применяются без перезапуска службы.
/// </summary>
[Authorize(Policy = "scope:" + Scopes.Admin)]
[Route("api/v1/settings/network")]
public sealed class NetworkSettingsController : WinAdminControllerBase
{
    private readonly INetworkSettingsService _network;
    private readonly NetworkApplyWatchdog _watchdog;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NetworkSettingsController> _logger;

    public NetworkSettingsController(
        INetworkSettingsService network, NetworkApplyWatchdog watchdog,
        IServiceScopeFactory scopes, ILogger<NetworkSettingsController> logger)
    {
        _network = network;
        _watchdog = watchdog;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Текущие сетевые настройки.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NetworkSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<NetworkSettingsDto> Get()
    {
        var s = _network.Current;
        return Ok(new NetworkSettingsDto(s.Mode, s.Port, s.Allow,
            NetworkEndpoints.PanelUrl(s, Request.Host.Host), s.Mode == NetworkMode.Network));
    }

    /// <summary>
    /// Изменить сетевые настройки. Ответ содержит новый адрес панели; применение
    /// (брандмауэр + перепривязка) происходит сразу после отправки ответа.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(NetworkUpdateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Update([FromBody] UpdateNetworkSettingsRequest request)
    {
        var previous = _network.Current;
        var check = _network.Check(new NetworkSettings(request.Mode, request.Port, request.Allow ?? []));
        if (check.Errors.Count > 0)
            return BadRequest(new { message = check.Errors[0], errors = check.Errors });
        if (check.PortBusy)
            return Conflict(new { message = $"Порт {check.Normalized.Port} уже занят другой программой." });

        var next = check.Normalized;
        string url = NetworkEndpoints.PanelUrl(next, Request.Host.Host);
        if (next.IsEquivalentTo(previous))
            return Ok(new NetworkUpdateResult(url));

        string actor = Actor;
        string? sourceIp = SourceIp;
        Response.OnCompleted(async () =>
        {
            bool success = true;
            string details = $"{Describe(previous)} → {Describe(next)}";
            try
            {
                _network.Apply(next, previous);
            }
            catch (Exception ex)
            {
                success = false;
                details += $": {ex.Message}";
                _logger.LogError(ex, "Не удалось применить сетевые настройки.");
            }

            using (var scope = _scopes.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
                {
                    Actor = actor,
                    Action = "settings.network",
                    Target = "network",
                    Success = success,
                    Details = details,
                    SourceIp = sourceIp,
                });
            }

            if (success)
                _ = _watchdog.Schedule(previous, next, actor, sourceIp);
        });

        return Ok(new NetworkUpdateResult(url));
    }

    private static string Describe(NetworkSettings s)
        => s.Mode == NetworkMode.Local
            ? $"local:{s.Port}"
            : $"network:{s.Port} [{string.Join(", ", s.Allow)}]";
}
```

- [ ] **Step 5: Register the watchdog**

В `Program.cs` сразу после `builder.Services.AddWinAdminNetwork(networkStore);` добавить:

```csharp
builder.Services.AddSingleton<NetworkApplyWatchdog>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS.

- [ ] **Step 7: Commit**

```bash
git add src/backend/WinAdmin.Api src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs
git commit -m "feat(api): add network settings endpoint with audit and rollback"
```

---

### Task 7: CLI `WinAdmin.exe network show|set`

**Files:**
- Create: `src/backend/WinAdmin.Api/Cli/NetworkCommands.cs`
- Modify: `src/backend/WinAdmin.Api/Cli/CliRunner.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs:18` (условие CLI-режима)
- Test: `src/tests/WinAdmin.Tests/NetworkCommandsTests.cs`

**Interfaces:**
- Consumes: `INetworkSettingsService`, `NetworkSettingsStore`, `NetworkEndpoints.PanelUrl`, `AddWinAdminNetwork`, `WinAdminPaths`.
- Produces: `static void NetworkCommands.Register(Command network, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)`.

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/NetworkCommandsTests.cs`:

```csharp
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Api.Cli;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkCommandsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-cli-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;
    private readonly Mock<IFirewallRunner> _firewall = new();
    private readonly Mock<IPortProbe> _probe = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly RootCommand _root;

    public NetworkCommandsTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, 8080, []));

        var services = new ServiceCollection();
        services.AddWinAdminNetwork(_store);
        services.AddSingleton(_firewall.Object);
        services.AddSingleton(_probe.Object);
        var provider = services.BuildServiceProvider();

        var network = new Command("network");
        NetworkCommands.Register(network, provider, _out, _err);
        _root = new RootCommand { network };
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Show_prints_mode_port_and_url()
    {
        int code = await _root.InvokeAsync(["network", "show"]);
        Assert.Equal(0, code);
        Assert.Contains("local", _out.ToString());
        Assert.Contains("http://127.0.0.1:8080", _out.ToString());
    }

    [Fact]
    public async Task Set_merges_options_with_current_settings()
    {
        int code = await _root.InvokeAsync(["network", "set", "--port", "9191"]);
        Assert.Equal(0, code);
        var s = _store.ReadOrDefault(out _);
        Assert.Equal(NetworkMode.Local, s.Mode);
        Assert.Equal(9191, s.Port);
        Assert.Contains("http://127.0.0.1:9191", _out.ToString());
    }

    [Fact]
    public async Task Set_network_mode_applies_firewall()
    {
        int code = await _root.InvokeAsync(["network", "set", "--mode", "network", "--allow", "10.77.77.0/24, 192.168.88.5"]);
        Assert.Equal(0, code);
        Assert.Equal(new[] { "10.77.77.0/24", "192.168.88.5" }, _store.ReadOrDefault(out _).Allow);
        _firewall.Verify(f => f.Run(It.Is<IReadOnlyList<FirewallCommand>>(c =>
            c.Any(x => x.Args.Contains("remoteip=10.77.77.0/24,192.168.88.5")))), Times.Once);
    }

    [Theory]
    [InlineData("--mode", "banana")]
    [InlineData("--port", "70000")]
    public async Task Invalid_input_exits_1_without_changes(string option, string value)
    {
        int code = await _root.InvokeAsync(["network", "set", option, value]);
        Assert.Equal(1, code);
        Assert.Equal(8080, _store.ReadOrDefault(out _).Port);
        Assert.NotEmpty(_err.ToString());
    }

    [Fact]
    public async Task Busy_port_exits_1()
    {
        _probe.Setup(p => p.IsInUse(9292)).Returns(true);
        int code = await _root.InvokeAsync(["network", "set", "--port", "9292"]);
        Assert.Equal(1, code);
        Assert.Contains("9292", _err.ToString());
    }

    [Fact]
    public async Task Missing_admin_rights_give_clear_message_and_no_changes()
    {
        _firewall.Setup(f => f.Run(It.IsAny<IReadOnlyList<FirewallCommand>>()))
            .Throws(new InvalidOperationException("The requested operation requires elevation."));
        int code = await _root.InvokeAsync(["network", "set", "--mode", "network", "--allow", "10.0.0.0/8"]);
        Assert.Equal(1, code);
        Assert.Contains("от имени администратора", _err.ToString());
        Assert.DoesNotContain("   at ", _err.ToString()); // без стектрейса
        Assert.Equal(NetworkMode.Local, _store.ReadOrDefault(out _).Mode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~NetworkCommandsTests`
Expected: build FAIL — `NetworkCommands` не найден.

- [ ] **Step 3: Implement the commands**

`src/backend/WinAdmin.Api/Cli/NetworkCommands.cs`:

```csharp
using System.CommandLine;
using System.CommandLine.Invocation;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Cli;

public static class NetworkCommands
{
    public static void Register(Command networkCommand, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        networkCommand.AddCommand(ShowCommand(provider, o));
        networkCommand.AddCommand(SetCommand(provider, o, e));
    }

    // ── show ─────────────────────────────────────────────────────
    private static Command ShowCommand(IServiceProvider provider, TextWriter output)
    {
        var cmd = new Command("show", "Показать сетевые настройки панели");
        cmd.SetHandler(() =>
        {
            var store = provider.GetRequiredService<NetworkSettingsStore>();
            var s = store.ReadOrDefault(out var problem);
            output.WriteLine($"Файл:     {store.FilePath}");
            if (problem is not null)
                output.WriteLine($"Ошибка:   {problem} (используются значения по умолчанию)");
            output.WriteLine($"Режим:    {(s.Mode == NetworkMode.Local ? "local (только этот компьютер)" : "network (сеть)")}");
            output.WriteLine($"Порт:     {s.Port}");
            output.WriteLine($"Подсети:  {(s.Allow.Count == 0 ? "—" : string.Join(", ", s.Allow))}");
            output.WriteLine($"Адрес:    {NetworkEndpoints.PanelUrl(s, null)}");
        });
        return cmd;
    }

    // ── set ──────────────────────────────────────────────────────
    private static Command SetCommand(IServiceProvider provider, TextWriter output, TextWriter error)
    {
        var modeOpt = new Option<string?>("--mode", "local — только этот компьютер; network — сеть (нужен --allow)");
        var portOpt = new Option<int?>("--port", "Порт панели (1–65535)");
        var allowOpt = new Option<string?>("--allow", "Разрешённые адреса/подсети через запятую, например 10.77.77.0/24,192.168.88.5");
        var cmd = new Command("set", "Изменить сетевые настройки (нужны права администратора)") { modeOpt, portOpt, allowOpt };

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var network = provider.GetRequiredService<INetworkSettingsService>();
            var current = network.Current;

            string? modeRaw = ctx.ParseResult.GetValueForOption(modeOpt);
            var mode = current.Mode;
            if (modeRaw is not null)
            {
                switch (modeRaw.Trim().ToLowerInvariant())
                {
                    case "local": mode = NetworkMode.Local; break;
                    case "network": mode = NetworkMode.Network; break;
                    default:
                        error.WriteLine($"Неизвестный режим «{modeRaw}». Допустимо: local, network.");
                        ctx.ExitCode = 1;
                        return;
                }
            }

            int port = ctx.ParseResult.GetValueForOption(portOpt) ?? current.Port;
            string? allowRaw = ctx.ParseResult.GetValueForOption(allowOpt);
            IReadOnlyList<string> allow = allowRaw is null
                ? current.Allow
                : allowRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var check = network.Check(new NetworkSettings(mode, port, allow));
            foreach (var message in check.Errors)
                error.WriteLine(message);
            if (check.PortBusy)
                error.WriteLine($"Порт {check.Normalized.Port} уже занят другой программой.");
            if (!check.Ok)
            {
                ctx.ExitCode = 1;
                return;
            }

            try
            {
                network.Apply(check.Normalized, current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException)
            {
                error.WriteLine($"Не удалось применить настройки: {ex.Message}");
                error.WriteLine("Запустите консоль от имени администратора.");
                ctx.ExitCode = 1;
                return;
            }

            output.WriteLine($"Сохранено. Панель: {NetworkEndpoints.PanelUrl(check.Normalized, null)}");
            output.WriteLine("Запущенная служба применит настройки автоматически (без перезапуска).");
        });
        return cmd;
    }
}
```

- [ ] **Step 4: Wire into `CliRunner` and `Program`**

`src/backend/WinAdmin.Api/Cli/CliRunner.cs` — заменить тело `RunAsync` до `var provider = ...` и регистрацию команд:

```csharp
        // Minimal DI — DB + user service + network settings
        var services = new ServiceCollection();
        string dbPath = WinAdminPaths.DatabasePath(config["WinAdmin:DatabasePath"]);
        services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUserService, UserService>();
        services.AddWinAdminNetwork(new NetworkSettingsStore(WinAdminPaths.DataDirectory(dbPath)));

        var provider = services.BuildServiceProvider();
```

и

```csharp
        var userCommand = new Command("user", "Управление пользователями");
        UserCommands.Register(userCommand, provider);

        var networkCommand = new Command("network", "Сетевые настройки панели (порт, режим доступа)");
        NetworkCommands.Register(networkCommand, provider);

        var root = new RootCommand("WinAdmin CLI") { userCommand, networkCommand };
        return await root.InvokeAsync(args);
```

Добавить `using WinAdmin.Infrastructure;` и `using WinAdmin.Infrastructure.Network;`.

Миграцию БД (`MigrateAsync`) выполнять только для `user`: обернуть существующий блок `using (var scope = ...) { ... MigrateAsync(); }` в `if (args.Length > 0 && args[0] == "user") { ... }` — `network` не должен требовать права на запись в БД.

`src/backend/WinAdmin.Api/Program.cs` — заменить

```csharp
if (args.Length > 0 && args[0] == "user")
```

на

```csharp
if (args.Length > 0 && args[0] is "user" or "network")
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS.

- [ ] **Step 6: Smoke-test the real CLI**

Run (PowerShell, без прав администратора, временный каталог):

```powershell
$env:WinAdmin__DatabasePath = "$env:TEMP\winadmin-smoke\WinAdmin.db"
dotnet run --project src/backend/WinAdmin.Api -- network show
dotnet run --project src/backend/WinAdmin.Api -- network set --port 9393
dotnet run --project src/backend/WinAdmin.Api -- network show
Remove-Item -Recurse -Force "$env:TEMP\winadmin-smoke"; Remove-Item Env:WinAdmin__DatabasePath
```

Expected: `show` печатает `Режим: local`, `Порт: 8080`; `set` печатает `Сохранено. Панель: http://127.0.0.1:9393`; повторный `show` — порт 9393. (В режиме `local` netsh только удаляет правила — без прав это не ошибка.)

- [ ] **Step 7: Commit**

```bash
git add src/backend/WinAdmin.Api src/tests/WinAdmin.Tests/NetworkCommandsTests.cs
git commit -m "feat(cli): add network show/set commands"
```

---

### Task 8: Права на папки приложения и данных

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Hardening/AclPlan.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Hardening/InstallationHardening.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (вызов в блоке `IsWindowsService()`)
- Test: `src/tests/WinAdmin.Tests/InstallationHardeningTests.cs`

**Interfaces:**
- Consumes: `WinAdminPaths.DataDirectory` (через `dataDirectory` в Program).
- Produces:
  - `sealed record AclRule(string Sid, FileSystemRights Rights)`
  - `static class AclPlan { const string Administrators, LocalSystem, Users; IReadOnlyList<AclRule> ForAppDirectory(); IReadOnlyList<AclRule> ForDataDirectory(); bool SameDirectory(string a, string b); }`
  - `static class InstallationHardening { void Apply(string appDirectory, string dataDirectory, ILogger logger); void HardenDirectory(string directory, IReadOnlyList<AclRule> rules, ILogger logger); }`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/InstallationHardeningTests.cs`:

```csharp
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Infrastructure.Hardening;

namespace WinAdmin.Tests;

public sealed class InstallationHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-acl-" + Guid.NewGuid().ToString("N"));
    private static readonly string Me = WindowsIdentity.GetCurrent().User!.Value;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void App_directory_plan_gives_users_read_only()
    {
        var rules = AclPlan.ForAppDirectory();
        Assert.Contains(rules, r => r.Sid == AclPlan.Administrators && r.Rights == FileSystemRights.FullControl);
        Assert.Contains(rules, r => r.Sid == AclPlan.LocalSystem && r.Rights == FileSystemRights.FullControl);
        Assert.Contains(rules, r => r.Sid == AclPlan.Users && r.Rights == FileSystemRights.ReadAndExecute);
        Assert.Equal(3, rules.Count);
    }

    [Fact]
    public void Data_directory_plan_excludes_users()
    {
        Assert.DoesNotContain(AclPlan.ForDataDirectory(), r => r.Sid == AclPlan.Users);
    }

    [Theory]
    [InlineData(@"C:\apps\WinAdmin", @"C:\apps\WinAdmin\", true)]
    [InlineData(@"C:\apps\WinAdmin", @"c:\APPS\winadmin", true)]
    [InlineData(@"C:\apps\WinAdmin", @"C:\ProgramData\WinAdmin", false)]
    public void Compares_directories(string a, string b, bool same)
        => Assert.Equal(same, AclPlan.SameDirectory(a, b));

    [Fact]
    public void HardenDirectory_replaces_dacl_and_resets_children_to_inherited()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        string file = Path.Combine(_dir, "sub", "app.dll");
        File.WriteAllText(file, "x");
        // Явное разрешение «Все: изменение» на файле — должно исчезнуть.
        var fileInfo = new FileInfo(file);
        var fs = fileInfo.GetAccessControl();
        fs.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify, AccessControlType.Allow));
        fileInfo.SetAccessControl(fs);

        var rules = new List<AclRule>
        {
            new(AclPlan.Administrators, FileSystemRights.FullControl),
            new(Me, FileSystemRights.FullControl), // иначе тест не сможет удалить каталог
        };
        InstallationHardening.HardenDirectory(_dir, rules, NullLogger.Instance);

        var dirAcl = new DirectoryInfo(_dir).GetAccessControl();
        Assert.True(dirAcl.AreAccessRulesProtected);
        var dirSids = dirAcl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).Distinct().ToList();
        Assert.Equal(new[] { AclPlan.Administrators, Me }.Order(), dirSids.Order());

        var fileRules = new FileInfo(file).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();
        Assert.All(fileRules, r => Assert.True(r.IsInherited));
        Assert.DoesNotContain(fileRules, r => r.IdentityReference.Value == "S-1-1-0");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~InstallationHardeningTests`
Expected: build FAIL — `WinAdmin.Infrastructure.Hardening` не существует.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Infrastructure/Hardening/AclPlan.cs`:

```csharp
using System.Security.AccessControl;

namespace WinAdmin.Infrastructure.Hardening;

/// <summary>Разрешение для SID (SID, а не имя группы — имена локализованы).</summary>
public sealed record AclRule(string Sid, FileSystemRights Rights);

/// <summary>Наборы прав для папок WinAdmin.</summary>
public static class AclPlan
{
    public const string Administrators = "S-1-5-32-544";
    public const string LocalSystem = "S-1-5-18";
    public const string Users = "S-1-5-32-545";

    /// <summary>Папка приложения: служба работает как SYSTEM, поэтому писать сюда могут только админы.</summary>
    public static IReadOnlyList<AclRule> ForAppDirectory() =>
    [
        new(Administrators, FileSystemRights.FullControl),
        new(LocalSystem, FileSystemRights.FullControl),
        new(Users, FileSystemRights.ReadAndExecute),
    ];

    /// <summary>Папка данных: БД (хеши паролей, ключи), network.json — только админы и SYSTEM.</summary>
    public static IReadOnlyList<AclRule> ForDataDirectory() =>
    [
        new(Administrators, FileSystemRights.FullControl),
        new(LocalSystem, FileSystemRights.FullControl),
    ];

    public static bool SameDirectory(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
```

`src/backend/WinAdmin.Infrastructure/Hardening/InstallationHardening.cs`:

```csharp
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace WinAdmin.Infrastructure.Hardening;

/// <summary>
/// Приводит права на папки приложения и данных к безопасным (вызывается при старте службы).
/// Без этого любой пользователь мог подменить exe службы LocalSystem или прочитать БД.
/// </summary>
public static class InstallationHardening
{
    public static void Apply(string appDirectory, string dataDirectory, ILogger logger)
    {
        HardenDirectory(appDirectory, AclPlan.ForAppDirectory(), logger);
        if (AclPlan.SameDirectory(appDirectory, dataDirectory))
            logger.LogWarning("БД лежит в папке приложения ({Dir}) и доступна пользователям на чтение. " +
                              "Вынесите её в C:\\ProgramData\\WinAdmin (WinAdmin__DatabasePath).", dataDirectory);
        else
            HardenDirectory(dataDirectory, AclPlan.ForDataDirectory(), logger);
    }

    /// <summary>
    /// Ставит на каталог защищённый DACL из rules (с наследованием) и сбрасывает
    /// явные разрешения у вложенных файлов/папок, чтобы они наследовали только его.
    /// </summary>
    public static void HardenDirectory(string directory, IReadOnlyList<AclRule> rules, ILogger logger)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists) return;
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var rule in rules)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(rule.Sid), rule.Rights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            root.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            logger.LogWarning(ex, "Не удалось исправить права на {Dir}.", directory);
            return;
        }

        foreach (var item in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            try
            {
                switch (item)
                {
                    case DirectoryInfo d:
                        var ds = new DirectorySecurity();
                        ds.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                        d.SetAccessControl(ds);
                        break;
                    case FileInfo f:
                        var fs = new FileSecurity();
                        fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                        f.SetAccessControl(fs);
                        break;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
            {
                logger.LogWarning("Не удалось сбросить права на {Path}: {Message}", item.FullName, ex.Message);
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~InstallationHardeningTests`
Expected: PASS. Если `HardenDirectory_replaces_dacl...` падает на файле с оставшимся явным ACE — значит `SetAccessControl` с пустым незащищённым DACL не сбросил явные записи; тогда в `case FileInfo f` удалять явные правила: `var fs = f.GetAccessControl(); foreach (FileSystemAccessRule r in fs.GetAccessRules(true, false, typeof(SecurityIdentifier))) fs.RemoveAccessRuleSpecific(r); fs.SetAccessRuleProtection(false, false); f.SetAccessControl(fs);` (и аналогично для каталогов).

- [ ] **Step 5: Call from `Program.cs`**

Добавить `using WinAdmin.Infrastructure.Hardening;`. В блоке `if (WindowsServiceHelpers.IsWindowsService())` (Task 5) первой строкой внутри `if`:

```csharp
    InstallationHardening.Apply(AppContext.BaseDirectory, dataDirectory, app.Logger);
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS.

- [ ] **Step 7: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure/Hardening src/backend/WinAdmin.Api/Program.cs src/tests/WinAdmin.Tests/InstallationHardeningTests.cs
git commit -m "feat(infra): harden app and data folder ACLs on service start"
```

---

### Task 9: Карточка «Сеть» в UI

**Files:**
- Modify: `src/frontend/src/api/types.ts` (после `ExcludedUserDto`)
- Modify: `src/frontend/src/api/client.ts` (секция `settings`)
- Create: `src/frontend/src/components/NetworkSettingsCard.tsx`
- Modify: `src/frontend/src/pages/Settings.tsx`

**Interfaces:**
- Consumes: `GET/PUT /api/v1/settings/network` (Task 6): GET → `{ mode: 'Local'|'Network', port, allow, url, firewallRule }`; PUT body `{ mode, port, allow }` → `{ url }`, ошибки `{ message }`.
- Produces: компонент `NetworkSettingsCard` (default export).

- [ ] **Step 1: Types and client**

`src/frontend/src/api/types.ts` — после `ExcludedUserDto` добавить:

```ts
export type NetworkMode = 'Local' | 'Network'

export interface NetworkSettingsDto {
  mode: NetworkMode
  port: number
  allow: string[]
  url: string
  firewallRule: boolean
}

export interface UpdateNetworkSettingsRequest {
  mode: NetworkMode
  port: number
  allow: string[]
}
```

`src/frontend/src/api/client.ts` — в объект `settings` после `removeExcludedUser` добавить:

```ts
    network: () => http.get<NetworkSettingsDto>('/settings/network').then((r) => r.data),
    updateNetwork: (req: UpdateNetworkSettingsRequest) =>
      http.put<{ url: string }>('/settings/network', req).then((r) => r.data),
```

и добавить `NetworkSettingsDto`, `UpdateNetworkSettingsRequest` в существующий `import type { ... } from './types'` в начале файла.

- [ ] **Step 2: Component**

`src/frontend/src/components/NetworkSettingsCard.tsx`:

```tsx
import { useCallback, useEffect, useState } from 'react'
import { Alert, App, Button, Card, Form, InputNumber, Radio, Select, Space, Typography } from 'antd'
import { GlobalOutlined } from '@ant-design/icons'
import { api } from '../api/client'
import type { NetworkMode, NetworkSettingsDto } from '../api/types'

const { Paragraph, Text } = Typography

interface FormValues {
  mode: NetworkMode
  port: number
  allow: string[]
}

export default function NetworkSettingsCard() {
  const { message, modal } = App.useApp()
  const [form] = Form.useForm<FormValues>()
  const [current, setCurrent] = useState<NetworkSettingsDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const mode = Form.useWatch('mode', form)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.settings.network()
      setCurrent(data)
      form.setFieldsValue({ mode: data.mode, port: data.port, allow: data.allow })
    } catch {
      message.error('Не удалось загрузить сетевые настройки')
    } finally {
      setLoading(false)
    }
  }, [form, message])

  useEffect(() => {
    load()
  }, [load])

  const save = async (values: FormValues) => {
    setSaving(true)
    try {
      const { url } = await api.settings.updateNetwork({ ...values, allow: values.allow ?? [] })
      message.success(`Настройки сохранены. Переход на ${url}`)
      setTimeout(() => window.location.assign(url + window.location.pathname), 1500)
    } catch (e: any) {
      message.error(e?.response?.data?.message ?? 'Не удалось сохранить сетевые настройки')
      setSaving(false)
    }
  }

  const handleFinish = (values: FormValues) => {
    modal.confirm({
      title: 'Применить сетевые настройки?',
      content:
        'Панель переедет на новый адрес. Если вы подключены не с этого компьютера, доступ может пропасть.',
      okText: 'Применить',
      cancelText: 'Отмена',
      onOk: () => save(values),
    })
  }

  return (
    <Card
      loading={loading}
      title={
        <Space>
          <GlobalOutlined />
          <span>Сеть</span>
        </Space>
      }
      style={{ maxWidth: 720, marginBottom: 16 }}
    >
      <Paragraph type="secondary" style={{ marginTop: 0 }}>
        Откуда доступна панель. Изменения применяются без перезапуска службы. Текущий адрес:{' '}
        <Text code>{current?.url}</Text>
      </Paragraph>

      <Form form={form} layout="vertical" onFinish={handleFinish} requiredMark={false}>
        <Form.Item name="mode" label="Доступ">
          <Radio.Group>
            <Radio.Button value="Local">Только этот компьютер</Radio.Button>
            <Radio.Button value="Network">Сеть</Radio.Button>
          </Radio.Group>
        </Form.Item>

        <Form.Item
          name="port"
          label="Порт"
          rules={[{ required: true, message: 'Укажите порт' }]}
        >
          <InputNumber min={1} max={65535} style={{ width: 160 }} />
        </Form.Item>

        {mode === 'Network' && (
          <>
            <Form.Item
              name="allow"
              label="Разрешённые адреса и подсети"
              extra="Например 10.77.77.0/24 или 192.168.88.5. Остальным брандмауэр закроет доступ."
              rules={[{ required: true, type: 'array', min: 1, message: 'Укажите хотя бы одну подсеть' }]}
            >
              <Select mode="tags" tokenSeparators={[',', ' ']} placeholder="10.0.0.0/24" open={false} />
            </Form.Item>
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 16 }}
              message="Трафик не шифруется (HTTP). Используйте режим «Сеть» только в доверенной сети."
            />
          </>
        )}

        <Form.Item style={{ marginBottom: 0 }}>
          <Button type="primary" htmlType="submit" loading={saving}>
            Сохранить
          </Button>
        </Form.Item>
      </Form>
    </Card>
  )
}
```

- [ ] **Step 3: Embed in Settings page**

`src/frontend/src/pages/Settings.tsx`:
- добавить импорт `import NetworkSettingsCard from '../components/NetworkSettingsCard'`;
- в `PageHeader` заменить `subtitle="Учётные записи, скрываемые из журналов событий"` на `subtitle="Сетевой доступ к панели и учётные записи, скрываемые из журналов"`;
- сразу после закрывающего `/>` компонента `PageHeader` вставить `<NetworkSettingsCard />`.

- [ ] **Step 4: Build and lint**

Run: `npm --prefix src/frontend run build && npm --prefix src/frontend run lint`
Expected: сборка без ошибок TypeScript, oxlint без ошибок.

- [ ] **Step 5: Check in the browser**

Запустить API на тестовом каталоге данных:

```powershell
$env:WinAdmin__DatabasePath = "$env:TEMP\winadmin-ui\WinAdmin.db"
dotnet run --project src/backend/WinAdmin.Api -- user add --login uitest --password "UiTest-12345" --scopes admin
dotnet run --project src/backend/WinAdmin.Api
```

Открыть `http://127.0.0.1:8080/settings` во встроенном браузере и войти `uitest` (тестовая учётка во временном каталоге). Проверить: карточка «Сеть» показывает «Только этот компьютер», порт 8080; при выборе «Сеть» появляются поле подсетей и предупреждение; «Сохранить» с портом 8181 → подтверждение → переход на `http://127.0.0.1:8181/settings`, страница открывается. Затем вернуть 8080 из UI. Остановить процесс, удалить `$env:TEMP\winadmin-ui`.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src
git commit -m "feat(ui): add network settings card"
```

---

### Task 10: Установщик PowerShell и документация

**Files:**
- Modify: `releases/install-service.ps1`
- Modify: `releases/build.ps1` (подсказка в конце)
- Modify: `releases/package/README.md` (разделы 2, 4, 5, 9, 10 и новый «Сетевой доступ»)
- Modify: `releases/package/docs/security.md`
- Modify: `README.md`, `docs/00-overview.md` (места с `0.0.0.0` / `--urls` / `New-NetFirewallRule`)

**Interfaces:**
- Consumes: формат `network.json` (Global Constraints), команды `network show|set` (Task 7).

- [ ] **Step 1: Rewrite `install-service.ps1`**

Полностью заменить содержимое `releases/install-service.ps1`:

```powershell
<#
.SYNOPSIS
    Install WinAdmin as a Windows service (quick server setup).

.DESCRIPTION
    - Registers the WinAdmin service (LocalSystem, auto start) without --urls:
      address and port come from network.json in the data folder.
    - Creates network.json (mode "local" = 127.0.0.1 only) if it does not exist.
    - Restricts folder permissions: install folder writable by Administrators/SYSTEM only,
      data folder (database, network.json) accessible by Administrators/SYSTEM only.
    - Removes the legacy "WinAdmin HTTP <port>" firewall rule (open to any address).
      Network access is enabled later with: WinAdmin.exe network set --mode network --allow <subnets>

.EXAMPLE
    .\install-service.ps1
    .\install-service.ps1 -InstallPath "D:\WinAdmin" -Port 8080
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "WinAdmin",
    [string]$InstallPath = $PSScriptRoot,
    [int]$Port = 8080,
    [string]$DataPath = "C:\ProgramData\WinAdmin"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator."
}

$exe = Join-Path $InstallPath "WinAdmin.exe"
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

New-Item -ItemType Directory -Force -Path $DataPath | Out-Null

# ── Network settings (address/port) ──────────────────────────────
$networkFile = Join-Path $DataPath "network.json"
if (-not (Test-Path $networkFile)) {
    @{ mode = "local"; port = $Port; allow = @() } | ConvertTo-Json | Set-Content -Path $networkFile -Encoding UTF8
    Write-Host "Created $networkFile (local, port $Port)" -ForegroundColor Green
} else {
    Write-Host "Keeping existing $networkFile" -ForegroundColor Yellow
}

# ── Folder permissions (by SID: Administrators, SYSTEM, Users) ───
function Set-StrictAcl([string]$Path, [string[]]$Grants) {
    icacls $Path /reset /T /C /Q | Out-Null
    icacls $Path /inheritance:r /grant:r @Grants /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for $Path" }
}
Set-StrictAcl $InstallPath @('*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-545:(OI)(CI)RX')
Set-StrictAcl $DataPath    @('*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F')
Write-Host "Folder permissions restricted: $InstallPath, $DataPath" -ForegroundColor Green

# ── Service ──────────────────────────────────────────────────────
$binPath = "`"$exe`""
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq 'Running') { Stop-Service $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "WinAdmin" | Out-Null
sc.exe description $ServiceName "WinAdmin - monitoring and management for Windows" | Out-Null

$dbPath = Join-Path $DataPath "WinAdmin.db"
[Environment]::SetEnvironmentVariable("WinAdmin__DatabasePath", $dbPath, "Machine")
Write-Host "WinAdmin__DatabasePath = $dbPath" -ForegroundColor Yellow
Write-Host "Recommended: also set WinAdmin__Jwt__Secret (see README.md)" -ForegroundColor Yellow

# ── Legacy firewall rule (was open to any address) ───────────────
$legacyRule = "WinAdmin HTTP $Port"
if (Get-NetFirewallRule -DisplayName $legacyRule -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $legacyRule
    Write-Host "Removed legacy firewall rule: $legacyRule" -ForegroundColor Green
}

Start-Service $ServiceName
$settings = Get-Content $networkFile -Raw | ConvertFrom-Json
Write-Host ""
Write-Host "Service '$ServiceName' started." -ForegroundColor Green
Write-Host "Open: http://127.0.0.1:$($settings.port)" -ForegroundColor Green
Write-Host ""
Write-Host "Network access (optional, admin console):" -ForegroundColor Yellow
Write-Host "  .\WinAdmin.exe network set --mode network --allow 10.0.0.0/24" -ForegroundColor Yellow
Write-Host ""
Write-Host "Create the first user (if none yet):" -ForegroundColor Yellow
Write-Host "  cd `"$InstallPath`"" -ForegroundColor Yellow
Write-Host '  .\WinAdmin.exe user add --login admin --password "YourPassword" --scopes admin' -ForegroundColor Yellow
```

- [ ] **Step 2: Validate script syntax**

Run: `pwsh -NoProfile -Command "$null = [System.Management.Automation.Language.Parser]::ParseFile('releases/install-service.ps1', [ref]$null, [ref]$e); $e"`
Expected: пустой вывод (нет ошибок разбора).

- [ ] **Step 3: Update `build.ps1` hint**

В `releases/build.ps1` в итоговой подсказке заменить строку

```
       .\WinAdmin.exe --urls http://localhost:8080
```

на

```
       .\WinAdmin.exe            (адрес и порт — network.json рядом с БД, по умолчанию http://127.0.0.1:8080)
```

- [ ] **Step 4: Update docs**

`releases/package/README.md`:
- Раздел «2. Быстрый старт»: команду `.\WinAdmin.exe --urls http://0.0.0.0:8080` заменить на `.\WinAdmin.exe`; «Откройте в браузере: **http://<имя-сервера>:8080**» → «Откройте в браузере на этом компьютере: **http://127.0.0.1:8080**»; `Invoke-RestMethod http://localhost:8080/health` → `http://127.0.0.1:8080/health`.
- Раздел «4. Запуск как службы»: в списке «Скрипт:» заменить «открывает порт в брандмауэре;» на «создаёт `C:\ProgramData\WinAdmin\network.json` (только этот компьютер, порт `-Port`); ограничивает права на папку приложения и данных; удаляет старое правило брандмауэра `WinAdmin HTTP <порт>`;». В «Ручная установка службы» убрать `--urls http://0.0.0.0:8080` из `binPath`.
- Раздел «5. Брандмауэр» заменить целиком на новый раздел:

```markdown
## 5. Сетевой доступ

По умолчанию панель доступна **только с этого компьютера** (`127.0.0.1`), правило брандмауэра не нужно.
Настройки хранятся в `C:\ProgramData\WinAdmin\network.json` и применяются **без перезапуска службы**.

В веб-интерфейсе: **Настройки → Сеть** (нужен scope `admin`).

Из консоли администратора:

```powershell
.\WinAdmin.exe network show
.\WinAdmin.exe network set --port 9090
.\WinAdmin.exe network set --mode network --allow "10.77.77.0/24,192.168.88.5"
.\WinAdmin.exe network set --mode local
```

В режиме `network` WinAdmin сам создаёт правило брандмауэра `WinAdmin (managed)` — только для перечисленных
адресов и только в профилях «Домен» и «Частная». Трафик не шифруется (HTTP) — используйте только в доверенной сети.

Под IIS адрес и порт задаёт сайт IIS — эти настройки не действуют.
```

- Раздел «9. CLI»: добавить строки `.\WinAdmin.exe network show` и `.\WinAdmin.exe network set --port 9090`.
- Раздел «10. Устранение неполадок»: строку «Порт занят» заменить на `| Порт занят | `.\WinAdmin.exe network set --port 9090` |`; строку «Не открывается с другой машины» заменить на `| Не открывается с другой машины | Режим «Сеть» с нужной подсетью (п. 5) |`; добавить строку `| Панель пропала после смены порта | `.\WinAdmin.exe network show` / `network set --port 8080` из консоли администратора |`.

`releases/package/docs/security.md` — в «Рекомендации для сервера» заменить пункт «Ограничить доступ к порту брандмауэром (только нужные подсети).» на «Оставить режим «только этот компьютер» либо включить режим «Сеть» только для нужных подсетей (`WinAdmin.exe network set`). Правило брандмауэра WinAdmin создаёт сам.» и добавить пункт «Служба при старте ограничивает права: папка приложения — запись только Администраторы/SYSTEM, `C:\ProgramData\WinAdmin` — доступ только Администраторы/SYSTEM.»

`README.md` и `docs/00-overview.md`: найти `0.0.0.0`, `--urls`, `New-NetFirewallRule` (`grep -n "0\.0\.0\.0\|--urls\|New-NetFirewallRule" README.md docs/00-overview.md`) и привести к тому же: запуск без `--urls`, адрес `http://127.0.0.1:8080`, сетевой доступ через `network set`. Разработческие команды `dotnet run` с `--urls` в README оставить, если они явно про разработку.

- [ ] **Step 5: Commit**

```bash
git add releases README.md docs/00-overview.md
git commit -m "feat(install): local-only default, strict ACLs, docs for network settings"
```

---

### Task 11: `winadmin-ctl` — порт через `network.json`, без `--urls`, строгие права

**Files:**
- Modify: `src/ctl/winadmin-ctl/src/settings.rs`
- Modify: `src/ctl/winadmin-ctl/src/service.rs` (`installed_port`, тесты)
- Modify: `src/ctl/winadmin-ctl/src/installer.rs` (`register_service`, `ensure_firewall`, `remove_firewall`)

**Interfaces:**
- Consumes: формат `network.json`, имена правил `WinAdmin (managed)` / `WinAdmin HTTP {port}`.
- Produces: `settings::network_file() -> PathBuf`, `settings::port_from_network_json(&str) -> Option<u16>`, `settings::write_network_port(u16) -> Result<(), String>`.

- [ ] **Step 1: Write the failing tests**

В конец `src/ctl/winadmin-ctl/src/settings.rs` добавить:

```rust
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_port_from_network_json() {
        assert_eq!(port_from_network_json(r#"{ "mode": "local", "port": 9090, "allow": [] }"#), Some(9090));
    }

    #[test]
    fn rejects_missing_or_invalid_port() {
        assert_eq!(port_from_network_json(r#"{ "mode": "local" }"#), None);
        assert_eq!(port_from_network_json(r#"{ "port": 0 }"#), None);
        assert_eq!(port_from_network_json(r#"{ "port": 70000 }"#), None);
        assert_eq!(port_from_network_json("not json"), None);
    }

    #[test]
    fn merges_port_preserving_mode_and_allow() {
        let merged = merge_network_port(Some(r#"{ "mode": "network", "port": 8080, "allow": ["10.0.0.0/8"] }"#), 9191);
        let v: serde_json::Value = serde_json::from_str(&merged).unwrap();
        assert_eq!(v["mode"], "network");
        assert_eq!(v["port"], 9191);
        assert_eq!(v["allow"][0], "10.0.0.0/8");
    }

    #[test]
    fn merge_creates_local_defaults_for_missing_or_broken_file() {
        for input in [None, Some("garbage"), Some("[1,2]")] {
            let v: serde_json::Value = serde_json::from_str(&merge_network_port(input, 8181)).unwrap();
            assert_eq!(v["mode"], "local");
            assert_eq!(v["port"], 8181);
            assert!(v["allow"].as_array().unwrap().is_empty());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cargo test --manifest-path src/ctl/winadmin-ctl/Cargo.toml settings`
Expected: FAIL — `cannot find function port_from_network_json` / `merge_network_port`.

- [ ] **Step 3: Implement in `settings.rs`**

Добавить после `impl Settings { ... }`:

```rust
pub const NETWORK_FILE: &str = "network.json";

/// network.json in the data folder: { "mode": "local"|"network", "port": N, "allow": [...] }.
pub fn network_file() -> PathBuf {
    PathBuf::from(DATA_PATH).join(NETWORK_FILE)
}

pub fn port_from_network_json(text: &str) -> Option<u16> {
    let v: serde_json::Value = serde_json::from_str(text).ok()?;
    let port = v.get("port")?.as_u64()?;
    u16::try_from(port).ok().filter(|p| *p > 0)
}

/// Sets the port in network.json content, keeping mode/allow; broken or missing → local defaults.
pub fn merge_network_port(existing: Option<&str>, port: u16) -> String {
    let mut v = existing
        .and_then(|t| serde_json::from_str::<serde_json::Value>(t).ok())
        .filter(|v| v.is_object())
        .unwrap_or_else(|| serde_json::json!({ "mode": "local", "allow": [] }));
    v["port"] = serde_json::json!(port);
    if v.get("mode").is_none() {
        v["mode"] = serde_json::json!("local");
    }
    if v.get("allow").is_none() {
        v["allow"] = serde_json::json!([]);
    }
    serde_json::to_string_pretty(&v).unwrap_or_default()
}

pub fn write_network_port(port: u16) -> Result<(), String> {
    let path = network_file();
    std::fs::create_dir_all(DATA_PATH).map_err(|e| e.to_string())?;
    let existing = std::fs::read_to_string(&path).ok();
    std::fs::write(&path, merge_network_port(existing.as_deref(), port)).map_err(|e| e.to_string())
}
```

- [ ] **Step 4: `installed_port` reads network.json first**

В `src/ctl/winadmin-ctl/src/service.rs` заменить функцию `installed_port` на:

```rust
/// Port of the installed panel: network.json (1.0.6+), else --urls in the service binary path (older installs).
pub fn installed_port(name: &str) -> Option<u16> {
    if let Ok(text) = std::fs::read_to_string(crate::settings::network_file()) {
        if let Some(port) = crate::settings::port_from_network_json(&text) {
            return Some(port);
        }
    }
    let out = sc(&["qc", name]).ok()?;
    let text = String::from_utf8_lossy(&out.stdout);
    let idx = text.find("--urls")?;
    let after = &text[idx..];
    let scheme = after.find("://")?;
    let rest = &after[scheme + 3..];
    let port_start = rest.find(':')? + 1;
    let digits: String = rest[port_start..].chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse().ok()
}
```

- [ ] **Step 5: Installer: no `--urls`, port to network.json, strict ACLs, no open firewall rule**

В `src/ctl/winadmin-ctl/src/installer.rs`:

1. Заменить

```rust
    // sc.exe needs the whole binPath (exe + args) inside one quoted token: binPath= "..."
    let bin_token = format!("\"{inner} --urls http://0.0.0.0:{}\"", s.port);
```

на

```rust
    // sc.exe needs the whole binPath inside one quoted token: binPath= "..."
    // Address/port come from network.json (no --urls).
    let bin_token = format!("\"{inner}\"");
    crate::settings::write_network_port(s.port)?;
    log(&format!("Port {} written to {}", s.port, crate::settings::network_file().display()));
```

2. В конце `register_service` заменить `ensure_firewall(s.port, log);` на:

```rust
    remove_firewall(s.port, log);
    harden_acl(&s.install_path, true, log);
    harden_acl(DATA_PATH, false, log);
```

3. Удалить функцию `ensure_firewall` целиком.

4. Заменить функцию `remove_firewall` на:

```rust
/// Removes the managed rule and the legacy "WinAdmin HTTP <port>" rule (open to any address).
fn remove_firewall(port: u16, log: &dyn Fn(&str)) {
    for rule in ["WinAdmin (managed)".to_string(), format!("WinAdmin HTTP {port}"), "WinAdmin HTTP 8080".to_string()] {
        let _ = Command::new("netsh.exe")
            .args(["advfirewall", "firewall", "delete", "rule", &format!("name={rule}")])
            .creation_flags(CREATE_NO_WINDOW)
            .output();
        log(&format!("Firewall rule removed (if present): {rule}"));
    }
}

/// Install folder: Administrators/SYSTEM full, Users read; data folder: Administrators/SYSTEM only.
fn harden_acl(path: &str, users_read: bool, log: &dyn Fn(&str)) {
    let _ = Command::new("icacls.exe")
        .args([path, "/reset", "/T", "/C", "/Q"])
        .creation_flags(CREATE_NO_WINDOW)
        .output();
    let mut grant = Command::new("icacls.exe");
    grant.args([path, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F"]);
    if users_read {
        grant.arg("*S-1-5-32-545:(OI)(CI)RX");
    }
    grant.args(["/C", "/Q"]);
    match grant.creation_flags(CREATE_NO_WINDOW).output() {
        Ok(out) if out.status.success() => log(&format!("Permissions restricted: {path}")),
        _ => log(&format!("Warning: could not restrict permissions on {path}")),
    }
}
```

5. Сообщения `http://localhost:{}` в `register_service_only` и `install_or_update` заменить на `http://127.0.0.1:{}`.

- [ ] **Step 6: Run Rust tests and build**

Run: `cargo test --manifest-path src/ctl/winadmin-ctl/Cargo.toml && cargo build --release --manifest-path src/ctl/winadmin-ctl/Cargo.toml`
Expected: все тесты PASS (включая существующие в `service.rs`), сборка без ошибок и предупреждений о неиспользуемом коде (`ensure_firewall` удалена).

- [ ] **Step 7: Commit**

```bash
git add src/ctl/winadmin-ctl/src
git commit -m "feat(ctl): store port in network.json, restrict ACLs, drop open firewall rule"
```

(Пересборку `releases/ctl/WinAdmin.Ctl.exe` через `releases/ctl/build-ctl.ps1` делать в рамках релиза, не в этой задаче.)

---

### Task 12: Сборка пакета и проверка на этой машине

**Files:** нет изменений кода (только проверка; найденные дефекты — отдельные исправления с тестом).

- [ ] **Step 1: Full test run**

Run: `dotnet test src/tests/WinAdmin.Tests && cargo test --manifest-path src/ctl/winadmin-ctl/Cargo.toml && npm --prefix src/frontend run lint`
Expected: всё PASS.

- [ ] **Step 2: Build the package**

Run: `pwsh -NoProfile -File releases/build.ps1 -OutputPath releases/dist`
Expected: «Готово. Размер пакета…», в `releases/dist` есть `WinAdmin.exe`, `wwwroot/index.html`, `install-service.ps1`.

- [ ] **Step 3: Hand the user the elevated update commands**

Обновление `C:\apps\WinAdmin` требует прав администратора — выполняет пользователь в PowerShell «от имени администратора». Передать ему ровно эти команды:

```powershell
Stop-Service WinAdmin
robocopy C:\dev\winadmin\releases\dist C:\apps\WinAdmin /E /NFL /NDL /NJH /NJS
sc.exe config WinAdmin binPath= "\"C:\apps\WinAdmin\WinAdmin.exe\""
Start-Service WinAdmin
```

`binPath` меняется до запуска, поэтому служба стартует без `--urls` и создаёт `network.json` с портом по умолчанию 8080 (совпадает с текущим). Старое правило `WinAdmin HTTP 8080` служба удалит сама при сверке брандмауэра.

- [ ] **Step 4: Verify (после того как пользователь выполнил Step 3)**

Проверки без прав администратора:

```powershell
Get-NetTCPConnection -LocalPort 8080 -State Listen | Select-Object LocalAddress
Get-Content C:\ProgramData\WinAdmin\network.json
netsh advfirewall firewall show rule name="WinAdmin HTTP 8080"
(Get-Acl C:\apps\WinAdmin).Access | ForEach-Object { "$($_.IdentityReference) $($_.FileSystemRights)" }
Test-Path C:\ProgramData\WinAdmin\WinAdmin.db; try { [IO.File]::OpenRead('C:\ProgramData\WinAdmin\WinAdmin.db').Close(); 'READABLE' } catch { 'DENIED' }
Invoke-RestMethod http://127.0.0.1:8080/health
```

Expected:
1. `LocalAddress` = `127.0.0.1` (не `0.0.0.0`).
2. `network.json` = `{ "mode": "local", "port": 8080, "allow": [] }`.
3. `No rules match the specified criteria.`
4. Нет «Прошедшие проверку»; «Пользователи» — только `ReadAndExecute, Synchronize`.
5. `DENIED` — обычный пользователь не читает БД.
6. `status: ok`.

Затем в браузере (`http://127.0.0.1:8080/settings`, пользователь входит сам): сменить порт на 8181 → переход на `http://127.0.0.1:8181/settings`; `Get-NetTCPConnection -LocalPort 8080 -State Listen` пусто; «Аудит» содержит `settings.network`. Вернуть 8080 из консоли администратора: `C:\apps\WinAdmin\WinAdmin.exe network set --port 8080` → панель снова на 8080 без перезапуска службы.

Проверка режима «Сеть» (консоль администратора): `network set --mode network --allow 10.77.77.0/24` → `netsh advfirewall firewall show rule name="WinAdmin (managed)"` показывает `RemoteIP: 10.77.77.0/24`, `Profiles: Domain,Private`; `network set --mode local` → правило удалено.

- [ ] **Step 5: Report**

Сообщить пользователю результаты Step 4 по пунктам; ветку не пушить и PR не создавать без запроса.
