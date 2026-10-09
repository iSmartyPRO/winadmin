# Platform 1a — Secrets & Database Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Секреты WinAdmin шифруются ключом машины (AES-256-GCM под DPAPI), JWT-секрет постоянный, а база данных выбирается между SQLite и PostgreSQL через `database.json` и CLI.

**Architecture:** `ISecretProtector` (Core) реализуется `AesGcmSecretProtector` с ключом из `MasterKeyStore` (`keys\master.key`, DPAPI LocalMachine, ACL). Один `WinAdminDbContext` и два наследника (`SqliteWinAdminDbContext`, `PostgresWinAdminDbContext`) со своими миграциями в той же сборке; `AddWinAdminDatabase` выбирает провайдера по `DatabaseSettings` из `database.json`. CLI `db` и `keys` управляют настройками.

**Tech Stack:** .NET 10, EF Core 10 (Sqlite, Npgsql 10.0.3), `System.Security.Cryptography` (AesGcm, Pbkdf2, ProtectedData), System.CommandLine beta4, xUnit/Moq.

**Spec:** `docs/superpowers/specs/2026-10-09-module-platform-design.md` (§4, §8, §9 строка 1a, §10)

## Global Constraints

- Репозиторий `C:\dev\winadmin`, ветка `feat/module-platform`.
- Формат секрета: `enc:v1:` + base64(nonce 12 байт | шифртекст | тег 16 байт); AES-256-GCM; ассоциированные данные — назначение секрета (строка).
- Ключ: 32 случайных байта, `<каталог данных>\keys\master.key`, содержимое защищено `ProtectedData` `DataProtectionScope.LocalMachine`, ACL — Администраторы, SYSTEM, текущий пользователь процесса.
- Экспорт ключа: PBKDF2-SHA256, 600 000 итераций, затем AES-GCM.
- `database.json` в каталоге данных: `{ "provider": "sqlite" | "postgresql", "connectionString": "…" }`; пароль PostgreSQL — `Password=enc:v1:…`.
- Нет `database.json` → SQLite по `WinAdmin:DatabasePath` (обратная совместимость), файл создаётся.
- Смена провайдера данные не переносит.
- Аудит и логи не содержат секретов.
- Пользовательские строки — по-русски.
- Тесты: `dotnet test src/tests/WinAdmin.Tests`; PostgreSQL-тесты выполняются только при заданной переменной `WINADMIN_TEST_POSTGRES`, иначе пропускаются.
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Обновление существующей установки: SQLite-миграции, перенесённые на `SqliteWinAdminDbContext`, должны считаться уже применёнными (идентификаторы не меняются), повторный `Migrate()` ничего не делает (тест в Task 4).
2. `database.json` с зашифрованным паролем, а ключ отсутствует или с другой машины — понятная ошибка с подсказкой `keys import`, а не stack trace (тест в Task 5).
3. Пароль PostgreSQL со спецсимволами (`;`, `=`, `'`, `"`) переживает шифрование и расшифровку (тест в Task 5).
4. `keys import` поверх существующего ключа без `--force` отклоняется — иначе все секреты станут нерасшифровываемыми (тест в Task 2 и Task 7).
5. Повреждённый `jwt.key` не роняет запуск — генерируется новый секрет; заданный в конфигурации `WinAdmin:Jwt:Secret` имеет приоритет (тест в Task 3).

---

## File Structure

| Файл | Ответственность |
|---|---|
| `src/backend/WinAdmin.Core/Abstractions/ISecretProtector.cs` | `ISecretProtector`, `ProtectedValue`, `SecretUnavailableException` |
| `src/backend/WinAdmin.Infrastructure/Secrets/AesGcmSecretProtector.cs` | Шифрование AES-256-GCM |
| `src/backend/WinAdmin.Infrastructure/Secrets/UnavailableSecretProtector.cs` | Заглушка, когда ключ недоступен |
| `src/backend/WinAdmin.Infrastructure/Secrets/MasterKeyStore.cs` | `master.key`: создание, чтение, экспорт/импорт |
| `src/backend/WinAdmin.Infrastructure/Secrets/JwtSecretStore.cs` | Постоянный JWT-секрет в `keys\jwt.key` |
| `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs` | Становится наследуемым |
| `src/backend/WinAdmin.Infrastructure/Storage/ProviderDbContexts.cs` | `SqliteWinAdminDbContext`, `PostgresWinAdminDbContext` |
| `src/backend/WinAdmin.Infrastructure/Storage/DesignTimeDbContextFactory.cs` | Фабрики для `dotnet ef` для обоих контекстов |
| `src/backend/WinAdmin.Infrastructure/Storage/Migrations/Sqlite/*` | Перенесённые миграции SQLite |
| `src/backend/WinAdmin.Infrastructure/Storage/Migrations/PostgreSql/*` | Новые миграции PostgreSQL |
| `src/backend/WinAdmin.Infrastructure/Storage/DatabaseSettings.cs` | `DatabaseProvider`, `DatabaseSettings`, `AddWinAdminDatabase` |
| `src/backend/WinAdmin.Infrastructure/Storage/DatabaseSettingsStore.cs` | `database.json`, шифрование пароля |
| `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs` | Принимает `DatabaseSettings`, регистрирует `ISecretProtector` |
| `src/backend/WinAdmin.Api/Program.cs`, `Cli/CliRunner.cs` | Ключи, JWT, база |
| `src/backend/WinAdmin.Api/Cli/DbCommands.cs`, `Cli/KeysCommands.cs` | CLI `db`, `keys` |
| `src/tests/WinAdmin.Tests/*` | Тесты; `PostgresFactAttribute`; `start-test-postgres.ps1` |
| `releases/package/README.md`, `releases/package/docs/security.md` | Документация |

---

### Task 1: Шифрование секретов (AES-256-GCM)

**Files:**
- Create: `src/backend/WinAdmin.Core/Abstractions/ISecretProtector.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Secrets/AesGcmSecretProtector.cs`
- Create: `src/backend/WinAdmin.Infrastructure/Secrets/UnavailableSecretProtector.cs`
- Test: `src/tests/WinAdmin.Tests/SecretProtectorTests.cs`

**Interfaces:**
- Produces:
  - `interface ISecretProtector { string Protect(string plaintext, string purpose); string Unprotect(string protectedValue, string purpose); }`
  - `static class ProtectedValue { const string Prefix = "enc:v1:"; static bool IsProtected(string? value); }`
  - `sealed class SecretUnavailableException(string message, Exception? inner = null) : Exception`
  - `sealed class AesGcmSecretProtector(byte[] key) : ISecretProtector`
  - `sealed class UnavailableSecretProtector(string reason) : ISecretProtector` — оба метода бросают `SecretUnavailableException(reason)`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/SecretProtectorTests.cs`:

```csharp
using System.Security.Cryptography;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class SecretProtectorTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private readonly AesGcmSecretProtector _protector = new(Key);

    [Fact]
    public void Round_trips_with_prefix_and_random_nonce()
    {
        string a = _protector.Protect("P@ss;word=1", "module:ad:ServicePassword");
        string b = _protector.Protect("P@ss;word=1", "module:ad:ServicePassword");

        Assert.StartsWith("enc:v1:", a);
        Assert.NotEqual(a, b); // новый nonce каждый раз
        Assert.True(ProtectedValue.IsProtected(a));
        Assert.Equal("P@ss;word=1", _protector.Unprotect(a, "module:ad:ServicePassword"));
    }

    [Fact]
    public void Value_cannot_be_moved_to_another_field()
    {
        string value = _protector.Protect("secret", "database:password");
        Assert.Throws<SecretUnavailableException>(() => _protector.Unprotect(value, "platform:jwt"));
    }

    [Fact]
    public void Other_key_or_tampering_is_rejected()
    {
        string value = _protector.Protect("secret", "p");
        var other = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
        Assert.Throws<SecretUnavailableException>(() => other.Unprotect(value, "p"));

        var raw = Convert.FromBase64String(value["enc:v1:".Length..]);
        raw[13] ^= 0xFF;
        string tampered = "enc:v1:" + Convert.ToBase64String(raw);
        Assert.Throws<SecretUnavailableException>(() => _protector.Unprotect(tampered, "p"));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("enc:v1:not-base64!")]
    [InlineData("enc:v1:AAAA")]
    public void Malformed_values_are_rejected(string value)
        => Assert.Throws<SecretUnavailableException>(() => _protector.Unprotect(value, "p"));

    [Fact]
    public void Key_must_be_32_bytes()
        => Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(new byte[16]));

    [Fact]
    public void Unavailable_protector_explains_why()
    {
        var p = new UnavailableSecretProtector("ключ не найден");
        var ex = Assert.Throws<SecretUnavailableException>(() => p.Protect("x", "p"));
        Assert.Contains("ключ не найден", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~SecretProtectorTests`
Expected: build FAIL — `ISecretProtector` / `AesGcmSecretProtector` не найдены.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Core/Abstractions/ISecretProtector.cs`:

```csharp
namespace WinAdmin.Core.Abstractions;

/// <summary>
/// Шифрование секретов (пароли служебных учёток, ключи API, пароль БД).
/// purpose — назначение секрета: зашифрованное значение нельзя подставить в другое поле.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext, string purpose);

    /// <summary>Бросает <see cref="SecretUnavailableException"/>, если расшифровать нельзя.</summary>
    string Unprotect(string protectedValue, string purpose);
}

public static class ProtectedValue
{
    public const string Prefix = "enc:v1:";

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
}

/// <summary>Секрет недоступен: нет ключа, другой ключ или значение повреждено.</summary>
public sealed class SecretUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
```

`src/backend/WinAdmin.Infrastructure/Secrets/AesGcmSecretProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>AES-256-GCM: enc:v1:base64(nonce12 | ciphertext | tag16), AAD = purpose.</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Ключ шифрования должен быть 32 байта.", nameof(key));
        _key = key.ToArray();
    }

    public string Protect(string plaintext, string purpose)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));
        return ProtectedValue.Prefix + Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    public string Unprotect(string protectedValue, string purpose)
    {
        if (!ProtectedValue.IsProtected(protectedValue))
            throw new SecretUnavailableException("Значение не зашифровано.");

        byte[] data;
        try
        {
            data = Convert.FromBase64String(protectedValue[ProtectedValue.Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new SecretUnavailableException("Зашифрованное значение повреждено.", ex);
        }
        if (data.Length < NonceSize + TagSize)
            throw new SecretUnavailableException("Зашифрованное значение повреждено.");

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(data.Length - TagSize);
        var cipher = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        byte[] plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(purpose));
        }
        catch (CryptographicException ex)
        {
            throw new SecretUnavailableException(
                "Не удалось расшифровать секрет: значение зашифровано другим ключом или повреждено.", ex);
        }
        return Encoding.UTF8.GetString(plain);
    }
}
```

`src/backend/WinAdmin.Infrastructure/Secrets/UnavailableSecretProtector.cs`:

```csharp
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>Используется, когда ключ шифрования недоступен: любая операция объясняет причину.</summary>
public sealed class UnavailableSecretProtector(string reason) : ISecretProtector
{
    public string Protect(string plaintext, string purpose) => throw new SecretUnavailableException(reason);
    public string Unprotect(string protectedValue, string purpose) => throw new SecretUnavailableException(reason);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~SecretProtectorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/backend/WinAdmin.Core/Abstractions/ISecretProtector.cs src/backend/WinAdmin.Infrastructure/Secrets src/tests/WinAdmin.Tests/SecretProtectorTests.cs
git commit -m "feat(secrets): add AES-256-GCM secret protector"
```

---

### Task 2: Ключ машины (`master.key`), экспорт и импорт

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj` (пакет `System.Security.Cryptography.ProtectedData`)
- Create: `src/backend/WinAdmin.Infrastructure/Secrets/MasterKeyStore.cs`
- Test: `src/tests/WinAdmin.Tests/MasterKeyStoreTests.cs`

**Interfaces:**
- Consumes: `SecretUnavailableException` (Task 1); `InstallationHardening.ProtectFile(string, IReadOnlyList<AclRule>, ILogger)`, `AclPlan.ForDataDirectory()`, `AclRule` (существующие, `WinAdmin.Infrastructure.Hardening`).
- Produces: `sealed class MasterKeyStore(string keysDirectory)` с `string KeysDirectory`, `string FilePath`, `bool Exists`, `byte[] LoadOrCreate()`, `byte[] Load()`, `byte[] Export(string password)`, `void Import(byte[] exported, string password, bool overwrite)`, `static IReadOnlyList<AclRule> KeyFileRules()`.

- [ ] **Step 1: Add the package**

Run: `dotnet add src/backend/WinAdmin.Infrastructure package System.Security.Cryptography.ProtectedData --version 10.0.*`
Expected: пакет добавлен (последний 10.0.x).

- [ ] **Step 2: Write the failing tests**

`src/tests/WinAdmin.Tests/MasterKeyStoreTests.cs`:

```csharp
using System.Security.AccessControl;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class MasterKeyStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-keys-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Creates_once_and_reloads_same_key()
    {
        var store = new MasterKeyStore(Path.Combine(_dir, "a"));
        byte[] first = store.LoadOrCreate();
        byte[] again = new MasterKeyStore(Path.Combine(_dir, "a")).LoadOrCreate();

        Assert.Equal(32, first.Length);
        Assert.Equal(first, again);
        Assert.NotEqual(first, File.ReadAllBytes(store.FilePath)); // на диске — под DPAPI
        Assert.True(new FileInfo(store.FilePath).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public void Export_and_import_move_key_to_another_folder()
    {
        var source = new MasterKeyStore(Path.Combine(_dir, "src"));
        byte[] key = source.LoadOrCreate();
        byte[] exported = source.Export("correct horse battery");

        var target = new MasterKeyStore(Path.Combine(_dir, "dst"));
        target.Import(exported, "correct horse battery", overwrite: false);

        Assert.Equal(key, target.Load());
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        var source = new MasterKeyStore(Path.Combine(_dir, "src"));
        source.LoadOrCreate();
        byte[] exported = source.Export("right");

        var target = new MasterKeyStore(Path.Combine(_dir, "dst"));
        var ex = Assert.Throws<InvalidOperationException>(() => target.Import(exported, "wrong", overwrite: false));
        Assert.Contains("пароль", ex.Message);
        Assert.False(target.Exists);
    }

    [Fact]
    public void Import_over_existing_key_requires_overwrite()
    {
        var source = new MasterKeyStore(Path.Combine(_dir, "src"));
        source.LoadOrCreate();
        byte[] exported = source.Export("pw");

        var target = new MasterKeyStore(Path.Combine(_dir, "dst"));
        byte[] own = target.LoadOrCreate();

        Assert.Throws<InvalidOperationException>(() => target.Import(exported, "pw", overwrite: false));
        Assert.Equal(own, target.Load());

        target.Import(exported, "pw", overwrite: true);
        Assert.Equal(source.Load(), target.Load());
    }

    [Fact]
    public void Corrupt_key_file_reports_secret_unavailable()
    {
        var store = new MasterKeyStore(Path.Combine(_dir, "bad"));
        Directory.CreateDirectory(store.KeysDirectory);
        File.WriteAllBytes(store.FilePath, [1, 2, 3, 4]);

        var ex = Assert.Throws<SecretUnavailableException>(() => store.Load());
        Assert.Contains("keys import", ex.Message);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~MasterKeyStoreTests`
Expected: build FAIL — `MasterKeyStore` не найден.

- [ ] **Step 4: Implement**

`src/backend/WinAdmin.Infrastructure/Secrets/MasterKeyStore.cs`:

```csharp
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Hardening;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>
/// Ключ шифрования секретов: 32 байта в keys\master.key под DPAPI (LocalMachine),
/// файл доступен только администраторам, SYSTEM и текущему пользователю процесса.
/// </summary>
public sealed class MasterKeyStore
{
    public const string FileName = "master.key";
    private static readonly byte[] Entropy = "WinAdmin.MasterKey.v1"u8.ToArray();
    private static readonly byte[] ExportMagic = "WAKEY1"u8.ToArray();
    private static readonly byte[] ExportAad = "winadmin-key-export"u8.ToArray();
    private const int KeySize = 32, SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 600_000;

    public MasterKeyStore(string keysDirectory)
    {
        KeysDirectory = keysDirectory;
        FilePath = Path.Combine(keysDirectory, FileName);
    }

    public string KeysDirectory { get; }
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);

    public byte[] LoadOrCreate()
    {
        if (Exists) return Load();
        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        Save(key);
        return key;
    }

    public byte[] Load()
    {
        try
        {
            byte[] key = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.LocalMachine);
            if (key.Length != KeySize) throw new CryptographicException("Неверная длина ключа.");
            return key;
        }
        catch (CryptographicException ex)
        {
            throw new SecretUnavailableException(
                $"Ключ шифрования {FilePath} не читается (повреждён или создан на другой машине). " +
                "Восстановите его командой WinAdmin.exe keys import.", ex);
        }
    }

    /// <summary>Ключ, зашифрованный паролем: WAKEY1 | salt16 | nonce12 | key32 | tag16.</summary>
    public byte[] Export(string password)
    {
        byte[] key = Load();
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[KeySize];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(DeriveKey(password, salt), TagSize))
            aes.Encrypt(nonce, key, cipher, tag, ExportAad);
        return [.. ExportMagic, .. salt, .. nonce, .. cipher, .. tag];
    }

    public void Import(byte[] exported, string password, bool overwrite)
    {
        if (Exists && !overwrite)
            throw new InvalidOperationException(
                $"Ключ {FilePath} уже существует. Замена сделает нерасшифровываемыми секреты, " +
                "зашифрованные текущим ключом. Повторите с --force, если это действительно нужно.");

        int expected = ExportMagic.Length + SaltSize + NonceSize + KeySize + TagSize;
        if (exported.Length != expected || !exported.AsSpan(0, ExportMagic.Length).SequenceEqual(ExportMagic))
            throw new InvalidOperationException("Файл не является экспортом ключа WinAdmin.");

        int o = ExportMagic.Length;
        var salt = exported.AsSpan(o, SaltSize).ToArray(); o += SaltSize;
        var nonce = exported.AsSpan(o, NonceSize); o += NonceSize;
        var cipher = exported.AsSpan(o, KeySize); o += KeySize;
        var tag = exported.AsSpan(o, TagSize);
        byte[] key = new byte[KeySize];
        try
        {
            using var aes = new AesGcm(DeriveKey(password, salt), TagSize);
            aes.Decrypt(nonce, cipher, tag, key, ExportAad);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Неверный пароль или повреждённый файл экспорта ключа.");
        }
        Save(key);
    }

    public static IReadOnlyList<AclRule> KeyFileRules() =>
    [
        .. AclPlan.ForDataDirectory(),
        new AclRule(WindowsIdentity.GetCurrent().User!.Value, FileSystemRights.FullControl),
    ];

    private void Save(byte[] key)
    {
        Directory.CreateDirectory(KeysDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine));
        InstallationHardening.ProtectFile(tmp, KeyFileRules(), NullLogger.Instance);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~MasterKeyStoreTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/backend/WinAdmin.Infrastructure src/tests/WinAdmin.Tests/MasterKeyStoreTests.cs
git commit -m "feat(secrets): add DPAPI-protected master key with password export/import"
```

---

### Task 3: Постоянный JWT-секрет и подключение ключа в `Program.cs`

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Secrets/JwtSecretStore.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (блок «Конфигурация» — JWT; регистрация `ISecretProtector`)
- Test: `src/tests/WinAdmin.Tests/JwtSecretStoreTests.cs`
- Test: `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs` (тест наличия ключей после старта)

**Interfaces:**
- Consumes: `ISecretProtector`, `SecretUnavailableException` (Task 1), `MasterKeyStore` (Task 2).
- Produces:
  - `sealed class JwtSecretStore(string keysDirectory, ISecretProtector protector)` с `string FilePath`, `string GetOrCreate()`; назначение секрета `"platform:jwt"`.
  - `static string JwtSecretStore.Resolve(string? configured, Func<string> fromStore)`.
  - В `Program.cs`: переменные `keysDirectory` (`Path.Combine(dataDirectory, "keys")`) и `secretProtector` (`ISecretProtector`), регистрация `builder.Services.AddSingleton(secretProtector)`.

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/JwtSecretStoreTests.cs`:

```csharp
using System.Security.Cryptography;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class JwtSecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-jwt-" + Guid.NewGuid().ToString("N"));
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Secret_survives_restart_and_is_stored_encrypted()
    {
        string first = new JwtSecretStore(_dir, _protector).GetOrCreate();
        string second = new JwtSecretStore(_dir, _protector).GetOrCreate();

        Assert.Equal(first, second);
        Assert.True(first.Length >= 32);
        string onDisk = File.ReadAllText(Path.Combine(_dir, JwtSecretStore.FileName));
        Assert.StartsWith("enc:v1:", onDisk);
        Assert.DoesNotContain(first, onDisk);
    }

    [Fact]
    public void Corrupt_file_is_replaced_instead_of_failing_start()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, JwtSecretStore.FileName), "garbage");

        string secret = new JwtSecretStore(_dir, _protector).GetOrCreate();

        Assert.False(string.IsNullOrEmpty(secret));
        Assert.Equal(secret, new JwtSecretStore(_dir, _protector).GetOrCreate());
    }

    [Fact]
    public void Configured_secret_wins_over_stored()
    {
        Assert.Equal("from-config", JwtSecretStore.Resolve("from-config", () => throw new InvalidOperationException()));
        Assert.Equal("from-store", JwtSecretStore.Resolve("  ", () => "from-store"));
    }
}
```

В `src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs`, класс `NetworkSettingsApiTests`, добавить тест:

```csharp
    [Fact]
    public async Task Startup_creates_encryption_and_jwt_keys()
    {
        await factory.CreateClient().GetAsync("/health"); // гарантирует, что хост построен
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "keys", "master.key")));
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "keys", "jwt.key")));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~JwtSecretStoreTests|FullyQualifiedName~Startup_creates_encryption_and_jwt_keys"`
Expected: build FAIL — `JwtSecretStore` не найден.

- [ ] **Step 3: Implement the store**

`src/backend/WinAdmin.Infrastructure/Secrets/JwtSecretStore.cs`:

```csharp
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Hardening;

namespace WinAdmin.Infrastructure.Secrets;

/// <summary>JWT-секрет, сгенерированный один раз и хранимый зашифрованным в keys\jwt.key.</summary>
public sealed class JwtSecretStore(string keysDirectory, ISecretProtector protector)
{
    public const string FileName = "jwt.key";
    private const string Purpose = "platform:jwt";

    public string FilePath { get; } = Path.Combine(keysDirectory, FileName);

    public string GetOrCreate()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                return protector.Unprotect(File.ReadAllText(FilePath).Trim(), Purpose);
            }
            catch (SecretUnavailableException)
            {
                // Повреждён или зашифрован другим ключом — выпускаем новый: старые сессии всё равно недействительны.
            }
        }

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        Directory.CreateDirectory(keysDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, protector.Protect(secret, Purpose));
        InstallationHardening.ProtectFile(tmp, MasterKeyStore.KeyFileRules(), NullLogger.Instance);
        File.Move(tmp, FilePath, overwrite: true);
        return secret;
    }

    /// <summary>Секрет из конфигурации (WinAdmin:Jwt:Secret) имеет приоритет над сохранённым.</summary>
    public static string Resolve(string? configured, Func<string> fromStore)
        => string.IsNullOrWhiteSpace(configured) ? fromStore() : configured;
}
```

- [ ] **Step 4: Wire into `Program.cs`**

1. В `using` добавить `using WinAdmin.Infrastructure.Secrets;`.

2. Сразу после строки `string dataDirectory = WinAdminPaths.DataDirectory(dbPath);` добавить:

```csharp
// Ключ шифрования секретов (keys\master.key под DPAPI машины).
string keysDirectory = Path.Combine(dataDirectory, "keys");
ISecretProtector secretProtector;
try
{
    secretProtector = new AesGcmSecretProtector(new MasterKeyStore(keysDirectory).LoadOrCreate());
}
catch (Exception ex) when (ex is SecretUnavailableException or IOException or UnauthorizedAccessException)
{
    Console.WriteLine($"Ключ шифрования недоступен: {ex.Message}");
    secretProtector = new UnavailableSecretProtector(ex.Message);
}
```

3. Заменить блок

```csharp
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
```

на

```csharp
var jwtOptions = builder.Configuration.GetSection("WinAdmin:Jwt").Get<JwtOptions>() ?? new JwtOptions();
try
{
    jwtOptions.Secret = JwtSecretStore.Resolve(jwtOptions.Secret,
        () => new JwtSecretStore(keysDirectory, secretProtector).GetOrCreate());
}
catch (Exception ex) when (ex is SecretUnavailableException or IOException or UnauthorizedAccessException)
{
    jwtOptions.Secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    Console.WriteLine($"JWT-секрет не сохранён ({ex.Message}) — используется временный, сессии сбросятся при перезапуске.");
}
```

4. Сразу после `builder.Services.AddWinAdminNetwork(networkStore);` добавить:

```csharp
builder.Services.AddSingleton(secretProtector);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS (известный нестабильный `SoftwareJobServiceTests.Timeout_*` может упасть — повторить прогон).

- [ ] **Step 6: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/JwtSecretStoreTests.cs src/tests/WinAdmin.Tests/NetworkSettingsApiTests.cs
git commit -m "feat(secrets): persist encrypted JWT secret and load machine key at startup"
```

---

### Task 4: Два провайдера БД — контексты и миграции

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj` (пакет `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3)
- Modify: `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs:6-9`
- Create: `src/backend/WinAdmin.Infrastructure/Storage/ProviderDbContexts.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/Storage/DesignTimeDbContextFactory.cs`
- Move: `src/backend/WinAdmin.Infrastructure/Storage/Migrations/*.cs` → `Storage/Migrations/Sqlite/`
- Create (генерация): `src/backend/WinAdmin.Infrastructure/Storage/Migrations/PostgreSql/*`
- Create: `src/backend/WinAdmin.Infrastructure/Storage/DatabaseSettings.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs`, `src/backend/WinAdmin.Api/Cli/CliRunner.cs` (вызовы регистрации)
- Test: `src/tests/WinAdmin.Tests/DatabaseMigrationTests.cs`

**Interfaces:**
- Produces:
  - `class WinAdminDbContext` (не sealed) с `public WinAdminDbContext(DbContextOptions<WinAdminDbContext>)` и `protected WinAdminDbContext(DbContextOptions)`
  - `sealed class SqliteWinAdminDbContext(DbContextOptions<SqliteWinAdminDbContext>)`, `sealed class PostgresWinAdminDbContext(DbContextOptions<PostgresWinAdminDbContext>)`
  - `enum DatabaseProvider { Sqlite, PostgreSql }`, `sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString)`
  - `static IServiceCollection AddWinAdminDatabase(this IServiceCollection, DatabaseSettings)` (namespace `WinAdmin.Infrastructure.Storage`)
  - `AddWinAdminInfrastructure(this IServiceCollection, DatabaseSettings database, JwtOptions jwtOptions)` — первый параметр меняется со строки на `DatabaseSettings`

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/DatabaseMigrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DatabaseMigrationTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "winadmin-mig-" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_file)) File.Delete(_file);
    }

    private SqliteWinAdminDbContext Sqlite() => new(
        new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite($"Data Source={_file}").Options);

    [Fact]
    public void Existing_sqlite_migration_ids_are_kept_so_upgrades_see_them_as_applied()
    {
        using (var db = Sqlite()) db.Database.Migrate();

        using var again = Sqlite();
        Assert.Equal(
            new[] { "20260623120856_InitialCreate", "20260711023114_AddUsers", "20260712061045_AddExcludedUsers" },
            again.Database.GetAppliedMigrations().Take(3));
        Assert.Empty(again.Database.GetPendingMigrations());
    }

    [Fact]
    public void Sqlite_model_matches_its_migrations()
    {
        using var db = Sqlite();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Postgres_model_matches_its_migrations()
    {
        using var db = new PostgresWinAdminDbContext(new DbContextOptionsBuilder<PostgresWinAdminDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~DatabaseMigrationTests`
Expected: build FAIL — `SqliteWinAdminDbContext` не найден.

- [ ] **Step 3: Add Npgsql provider**

Run: `dotnet add src/backend/WinAdmin.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3`
Expected: пакет добавлен.

- [ ] **Step 4: Make the context inheritable and add provider contexts**

В `WinAdminDbContext.cs` заменить

```csharp
/// <summary>Локальная база (SQLite): API-ключи и журнал аудита.</summary>
public sealed class WinAdminDbContext : DbContext
{
    public WinAdminDbContext(DbContextOptions<WinAdminDbContext> options) : base(options) { }
```

на

```csharp
/// <summary>
/// База WinAdmin. Модель общая; у каждого провайдера свой наследник со своими миграциями
/// (<see cref="SqliteWinAdminDbContext"/>, <see cref="PostgresWinAdminDbContext"/>).
/// </summary>
public class WinAdminDbContext : DbContext
{
    public WinAdminDbContext(DbContextOptions<WinAdminDbContext> options) : base(options) { }

    protected WinAdminDbContext(DbContextOptions options) : base(options) { }
```

`src/backend/WinAdmin.Infrastructure/Storage/ProviderDbContexts.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>SQLite: миграции в Storage/Migrations/Sqlite.</summary>
public sealed class SqliteWinAdminDbContext(DbContextOptions<SqliteWinAdminDbContext> options)
    : WinAdminDbContext(options);

/// <summary>PostgreSQL: миграции в Storage/Migrations/PostgreSql.</summary>
public sealed class PostgresWinAdminDbContext(DbContextOptions<PostgresWinAdminDbContext> options)
    : WinAdminDbContext(options);
```

`src/backend/WinAdmin.Infrastructure/Storage/DesignTimeDbContextFactory.cs` — заменить содержимое:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>Только для dotnet ef (без стартовой логики API).</summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteWinAdminDbContext>
{
    public SqliteWinAdminDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite("Data Source=WinAdmin-design.db").Options);
}

/// <summary>Только для dotnet ef; генерация миграций не требует работающего сервера.</summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresWinAdminDbContext>
{
    public PostgresWinAdminDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<PostgresWinAdminDbContext>().UseNpgsql("Host=localhost;Database=winadmin_design").Options);
}
```

- [ ] **Step 5: Move SQLite migrations to the SQLite context**

```bash
cd src/backend/WinAdmin.Infrastructure/Storage/Migrations
mkdir Sqlite
git mv 20260623120856_InitialCreate.cs 20260623120856_InitialCreate.Designer.cs 20260711023114_AddUsers.cs 20260711023114_AddUsers.Designer.cs 20260712061045_AddExcludedUsers.cs 20260712061045_AddExcludedUsers.Designer.cs Sqlite/
git mv WinAdminDbContextModelSnapshot.cs Sqlite/SqliteWinAdminDbContextModelSnapshot.cs
cd Sqlite
sed -i 's/namespace WinAdmin\.Infrastructure\.Storage\.Migrations\b/namespace WinAdmin.Infrastructure.Storage.Migrations.Sqlite/; s/\[DbContext(typeof(WinAdminDbContext))\]/[DbContext(typeof(SqliteWinAdminDbContext))]/; s/partial class WinAdminDbContextModelSnapshot/partial class SqliteWinAdminDbContextModelSnapshot/' *.cs
grep -L "Migrations.Sqlite" *.cs
```

Expected: последняя команда ничего не выводит (во всех файлах новый namespace). Атрибуты `[Migration("…")]` не меняются — по ним существующие базы видят миграции применёнными.

- [ ] **Step 6: Generate PostgreSQL migrations**

```bash
dotnet ef migrations add InitialCreate --project src/backend/WinAdmin.Infrastructure --startup-project src/backend/WinAdmin.Api --context PostgresWinAdminDbContext --output-dir Storage/Migrations/PostgreSql --namespace WinAdmin.Infrastructure.Storage.Migrations.PostgreSql
```

Expected: `Done.`, созданы `Storage/Migrations/PostgreSql/<timestamp>_InitialCreate.cs`, `.Designer.cs`, `PostgresWinAdminDbContextModelSnapshot.cs`.

Проверка, что SQLite-модель не требует новой миграции:

```bash
dotnet ef migrations has-pending-model-changes --project src/backend/WinAdmin.Infrastructure --startup-project src/backend/WinAdmin.Api --context SqliteWinAdminDbContext
```

Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 7: Provider selection**

`src/backend/WinAdmin.Infrastructure/Storage/DatabaseSettings.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WinAdmin.Infrastructure.Storage;

public enum DatabaseProvider { Sqlite, PostgreSql }

/// <summary>Провайдер и строка подключения (для использования — с расшифрованным паролем).</summary>
public sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString);

public static class DatabaseServiceCollectionExtensions
{
    /// <summary>Регистрирует WinAdminDbContext с реализацией под выбранного провайдера.</summary>
    public static IServiceCollection AddWinAdminDatabase(this IServiceCollection services, DatabaseSettings database)
        => database.Provider switch
        {
            DatabaseProvider.Sqlite => services.AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(
                o => o.UseSqlite(database.ConnectionString)),
            DatabaseProvider.PostgreSql => services.AddDbContext<WinAdminDbContext, PostgresWinAdminDbContext>(
                o => o.UseNpgsql(database.ConnectionString)),
            _ => throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "Неизвестный провайдер БД."),
        };
}
```

`src/backend/WinAdmin.Infrastructure/DependencyInjection.cs` — заменить сигнатуру и первую строку тела:

```csharp
    public static IServiceCollection AddWinAdminInfrastructure(
        this IServiceCollection services,
        DatabaseSettings database,
        JwtOptions jwtOptions)
    {
        services.AddWinAdminDatabase(database);
```

(убрать `services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite(sqliteConnectionString));`; `using Microsoft.EntityFrameworkCore;` удалить, если больше не используется).

`src/backend/WinAdmin.Api/Program.cs` — заменить `builder.Services.AddWinAdminInfrastructure(connectionString, jwtOptions);` на:

```csharp
builder.Services.AddWinAdminInfrastructure(new DatabaseSettings(DatabaseProvider.Sqlite, connectionString), jwtOptions);
```

`src/backend/WinAdmin.Api/Cli/CliRunner.cs` — заменить `services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));` на:

```csharp
        services.AddWinAdminDatabase(new DatabaseSettings(DatabaseProvider.Sqlite, $"Data Source={dbPath}"));
```

(Task 5 заменит оба места на чтение `database.json`.)

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS, включая `DatabaseMigrationTests` (3).

- [ ] **Step 9: Commit**

```bash
git add -A src/backend src/tests/WinAdmin.Tests/DatabaseMigrationTests.cs
git commit -m "feat(storage): split SQLite and PostgreSQL contexts with own migrations"
```

---

### Task 5: `database.json` — выбор провайдера и шифрование пароля

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Storage/DatabaseSettingsStore.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs` (выбор базы)
- Modify: `src/backend/WinAdmin.Api/Cli/CliRunner.cs` (выбор базы, ключ)
- Test: `src/tests/WinAdmin.Tests/DatabaseSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `ISecretProtector`, `ProtectedValue`, `SecretUnavailableException` (Task 1); `DatabaseSettings`, `DatabaseProvider` (Task 4); `MasterKeyStore` (Task 2).
- Produces: `sealed class DatabaseSettingsStore(string dataDirectory, ISecretProtector protector)` с `string FilePath`, `bool Exists`, `DatabaseSettings Read(string defaultSqlitePath)` (как хранится), `void Write(DatabaseSettings settings)` (шифрует пароль PostgreSQL), `DatabaseSettings ResolveForUse(DatabaseSettings stored)` (расшифровывает), `static string Describe(DatabaseSettings settings)` (без пароля), `static string ProviderName(DatabaseProvider)` / `static DatabaseProvider ParseProvider(string)`.

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/DatabaseSettingsStoreTests.cs`:

```csharp
using System.Security.Cryptography;
using Npgsql;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DatabaseSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-dbcfg-" + Guid.NewGuid().ToString("N"));
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private DatabaseSettingsStore Store(ISecretProtector? p = null) => new(_dir, p ?? _protector);

    public DatabaseSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_falls_back_to_sqlite_path()
    {
        var s = Store().Read(@"C:\ProgramData\WinAdmin\WinAdmin.db");
        Assert.Equal(DatabaseProvider.Sqlite, s.Provider);
        Assert.Equal(@"Data Source=C:\ProgramData\WinAdmin\WinAdmin.db", s.ConnectionString);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("p;a=s's\"w")]
    public void Postgres_password_is_encrypted_on_disk_and_restored(string password)
    {
        var cs = new NpgsqlConnectionStringBuilder { Host = "db01", Database = "winadmin", Username = "wa", Password = password };
        Store().Write(new DatabaseSettings(DatabaseProvider.PostgreSql, cs.ConnectionString));

        string onDisk = File.ReadAllText(Store().FilePath);
        Assert.DoesNotContain(password, onDisk);
        Assert.Contains("enc:v1:", onDisk);
        Assert.Contains("\"provider\": \"postgresql\"", onDisk);

        var used = Store().ResolveForUse(Store().Read("unused.db"));
        Assert.Equal(password, new NpgsqlConnectionStringBuilder(used.ConnectionString).Password);
        Assert.DoesNotContain("enc:v1", Store().ResolveForUse(Store().Read("unused.db")).ConnectionString);
    }

    [Fact]
    public void Hand_written_plaintext_password_still_works()
    {
        File.WriteAllText(Store().FilePath,
            "{ \"provider\": \"postgresql\", \"connectionString\": \"Host=db;Username=wa;Password=plain\" }");
        var used = Store().ResolveForUse(Store().Read("unused.db"));
        Assert.Equal("plain", new NpgsqlConnectionStringBuilder(used.ConnectionString).Password);
    }

    [Fact]
    public void Missing_key_gives_actionable_error()
    {
        var cs = "Host=db;Username=wa;Password=secret";
        Store().Write(new DatabaseSettings(DatabaseProvider.PostgreSql, cs));

        var noKey = Store(new UnavailableSecretProtector("ключ не найден"));
        var ex = Assert.Throws<SecretUnavailableException>(() => noKey.ResolveForUse(noKey.Read("unused.db")));
        Assert.Contains("keys import", ex.Message);
    }

    [Theory]
    [InlineData("{ \"provider\": \"oracle\", \"connectionString\": \"x\" }")]
    [InlineData("{ broken")]
    [InlineData("{ \"provider\": \"sqlite\" }")]
    public void Invalid_file_reports_path(string content)
    {
        File.WriteAllText(Store().FilePath, content);
        var ex = Assert.Throws<InvalidOperationException>(() => Store().Read("unused.db"));
        Assert.Contains(Store().FilePath, ex.Message);
    }

    [Fact]
    public void Describe_hides_password()
    {
        var text = DatabaseSettingsStore.Describe(new DatabaseSettings(DatabaseProvider.PostgreSql, "Host=db;Username=wa;Password=topsecret"));
        Assert.DoesNotContain("topsecret", text);
        Assert.Contains("db", text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~DatabaseSettingsStoreTests`
Expected: build FAIL — `DatabaseSettingsStore` не найден.

- [ ] **Step 3: Implement**

`src/backend/WinAdmin.Infrastructure/Storage/DatabaseSettingsStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>
/// database.json в каталоге данных: провайдер и строка подключения.
/// Пароль PostgreSQL хранится зашифрованным (Password=enc:v1:…).
/// </summary>
public sealed class DatabaseSettingsStore(string dataDirectory, ISecretProtector protector)
{
    public const string FileName = "database.json";
    private const string PasswordPurpose = "database:password";

    public string FilePath { get; } = Path.Combine(dataDirectory, FileName);
    public bool Exists => File.Exists(FilePath);

    public static string ProviderName(DatabaseProvider p) => p == DatabaseProvider.PostgreSql ? "postgresql" : "sqlite";

    public static DatabaseProvider ParseProvider(string value) => value.Trim().ToLowerInvariant() switch
    {
        "sqlite" => DatabaseProvider.Sqlite,
        "postgresql" or "postgres" => DatabaseProvider.PostgreSql,
        _ => throw new ArgumentException($"Неизвестный провайдер «{value}». Допустимо: sqlite, postgresql."),
    };

    /// <summary>Настройки как хранятся (пароль может быть зашифрован). Нет файла → SQLite по умолчанию.</summary>
    public DatabaseSettings Read(string defaultSqlitePath)
    {
        if (!Exists)
            return new DatabaseSettings(DatabaseProvider.Sqlite, $"Data Source={defaultSqlitePath}");

        string? provider, connection;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(FilePath));
            provider = node?["provider"]?.GetValue<string>();
            connection = node?["connectionString"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"{FilePath}: файл повреждён ({ex.Message}).", ex);
        }
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException($"{FilePath}: нужны поля provider и connectionString.");
        try
        {
            return new DatabaseSettings(ParseProvider(provider), connection);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{FilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>Сохраняет настройки, шифруя пароль PostgreSQL.</summary>
    public void Write(DatabaseSettings settings)
    {
        string connection = settings.ConnectionString;
        if (settings.Provider == DatabaseProvider.PostgreSql)
        {
            var cs = new NpgsqlConnectionStringBuilder(connection);
            if (!string.IsNullOrEmpty(cs.Password) && !ProtectedValue.IsProtected(cs.Password))
                cs.Password = protector.Protect(cs.Password, PasswordPurpose);
            connection = cs.ConnectionString;
        }

        var json = new JsonObject
        {
            ["provider"] = ProviderName(settings.Provider),
            ["connectionString"] = connection,
        };
        Directory.CreateDirectory(dataDirectory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Строка подключения для использования: пароль расшифрован.</summary>
    public DatabaseSettings ResolveForUse(DatabaseSettings stored)
    {
        if (stored.Provider != DatabaseProvider.PostgreSql)
            return stored;
        var cs = new NpgsqlConnectionStringBuilder(stored.ConnectionString);
        if (ProtectedValue.IsProtected(cs.Password))
        {
            try
            {
                cs.Password = protector.Unprotect(cs.Password!, PasswordPurpose);
            }
            catch (SecretUnavailableException ex)
            {
                throw new SecretUnavailableException(
                    $"Пароль базы данных в {FilePath} не расшифровывается: {ex.Message} " +
                    "Восстановите ключ (WinAdmin.exe keys import) или задайте подключение заново (WinAdmin.exe db set).", ex);
            }
        }
        return stored with { ConnectionString = cs.ConnectionString };
    }

    /// <summary>Описание для вывода — без пароля.</summary>
    public static string Describe(DatabaseSettings settings)
    {
        if (settings.Provider == DatabaseProvider.Sqlite)
            return $"sqlite: {settings.ConnectionString}";
        var cs = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Password = null };
        return $"postgresql: {cs.ConnectionString}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~DatabaseSettingsStoreTests`
Expected: PASS.

- [ ] **Step 5: Use it in `Program.cs` and `CliRunner`**

`Program.cs` — заменить строку `string connectionString = $"Data Source={dbPath}";` на (её место после блока ключа шифрования из Task 3 — переставить, чтобы `secretProtector` был объявлен раньше):

```csharp
// Провайдер БД: database.json (sqlite | postgresql), по умолчанию — SQLite по WinAdmin:DatabasePath.
var databaseStore = new DatabaseSettingsStore(dataDirectory, secretProtector);
var storedDatabase = databaseStore.Read(dbPath);
if (!databaseStore.Exists)
{
    try { databaseStore.Write(storedDatabase); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"Не удалось создать {databaseStore.FilePath}: {ex.Message}"); }
}
DatabaseSettings database = databaseStore.ResolveForUse(storedDatabase);
```

и `builder.Services.AddWinAdminInfrastructure(new DatabaseSettings(DatabaseProvider.Sqlite, connectionString), jwtOptions);` на `builder.Services.AddWinAdminInfrastructure(database, jwtOptions);`.

Порядок блоков в «Конфигурации» после правки: `dbPath` → `dataDirectory` → `network.json` (без изменений) → ключ шифрования → `database.json` → JWT.

`CliRunner.cs` — заменить начало `RunAsync` до `var provider = services.BuildServiceProvider();`:

```csharp
        var services = new ServiceCollection();
        string dbPath = WinAdminPaths.DatabasePath(config["WinAdmin:DatabasePath"]);
        string dataDirectory = WinAdminPaths.DataDirectory(dbPath);
        var keys = new MasterKeyStore(Path.Combine(dataDirectory, "keys"));
        ISecretProtector protector = keys.Exists || args is ["user", ..]
            ? new AesGcmSecretProtector(keys.LoadOrCreate())
            : new UnavailableSecretProtector($"Ключ шифрования {keys.FilePath} не найден.");
        var databaseStore = new DatabaseSettingsStore(dataDirectory, protector);

        services.AddSingleton(protector);
        if (args is ["user", ..])
            services.AddWinAdminDatabase(databaseStore.ResolveForUse(databaseStore.Read(dbPath)));
        services.AddScoped<IUserService, UserService>();
        services.AddWinAdminNetwork(new NetworkSettingsStore(dataDirectory));
```

`using`: `WinAdmin.Core.Abstractions` (уже есть), `WinAdmin.Infrastructure.Secrets`, `WinAdmin.Infrastructure.Storage` (уже есть). `UserService` регистрируется всегда, но разрешается только командами `user` (у них есть БД).

- [ ] **Step 6: Run all tests**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS.

- [ ] **Step 7: Commit**

```bash
git add src/backend src/tests/WinAdmin.Tests/DatabaseSettingsStoreTests.cs
git commit -m "feat(storage): choose database provider via database.json with encrypted password"
```

---

### Task 6: Проверка на настоящем PostgreSQL

**Files:**
- Create: `src/tests/WinAdmin.Tests/PostgresFactAttribute.cs`
- Create: `src/tests/start-test-postgres.ps1`
- Test: `src/tests/WinAdmin.Tests/PostgresStorageTests.cs`

**Interfaces:**
- Consumes: `PostgresWinAdminDbContext` (Task 4), `AuditService`, `UserService`, `ExcludedUserService` (существующие).
- Produces: `sealed class PostgresFactAttribute : FactAttribute` (Skip, если нет `WINADMIN_TEST_POSTGRES`); `static string PostgresFactAttribute.ConnectionString`.

- [ ] **Step 1: Test-cluster script**

`src/tests/start-test-postgres.ps1`:

```powershell
<#
.SYNOPSIS
    Запускает временный кластер PostgreSQL для тестов (не трогает установленную службу).
.EXAMPLE
    .\src\tests\start-test-postgres.ps1            # старт, выводит строку для WINADMIN_TEST_POSTGRES
    .\src\tests\start-test-postgres.ps1 -Stop      # остановка и удаление кластера
#>
param(
    [string]$BinDir = "C:\Program Files\PostgreSQL\18\bin",
    [string]$DataDir = (Join-Path $env:TEMP "winadmin-test-pg"),
    [int]$Port = 55432,
    [switch]$Stop
)
$ErrorActionPreference = "Stop"
$pgCtl = Join-Path $BinDir "pg_ctl.exe"
if ($Stop) {
    if (Test-Path $DataDir) { & $pgCtl -D $DataDir stop -m fast | Out-Null; Remove-Item -Recurse -Force $DataDir }
    return
}
if (-not (Test-Path $DataDir)) {
    & (Join-Path $BinDir "initdb.exe") -D $DataDir -U winadmin --auth=trust -E UTF8 | Out-Null
}
& $pgCtl -D $DataDir -o "-p $Port -c listen_addresses=127.0.0.1" -l (Join-Path $DataDir "log.txt") -w start | Out-Null
"Host=127.0.0.1;Port=$Port;Username=winadmin;Database=postgres"
```

- [ ] **Step 2: Skippable attribute**

`src/tests/WinAdmin.Tests/PostgresFactAttribute.cs`:

```csharp
namespace WinAdmin.Tests;

/// <summary>Тест на настоящем PostgreSQL: выполняется, только если задана WINADMIN_TEST_POSTGRES.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("WINADMIN_TEST_POSTGRES");

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Skip = "WINADMIN_TEST_POSTGRES не задана (см. src/tests/start-test-postgres.ps1).";
    }
}
```

- [ ] **Step 3: Write the tests**

`src/tests/WinAdmin.Tests/PostgresStorageTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Settings;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class PostgresStorageTests : IDisposable
{
    private readonly string _cs;
    private readonly PostgresWinAdminDbContext _db;

    public PostgresStorageTests()
    {
        var b = new NpgsqlConnectionStringBuilder(PostgresFactAttribute.ConnectionString ?? "Host=unused")
        {
            Database = "winadmin_test_" + Guid.NewGuid().ToString("N")[..12],
        };
        _cs = b.ConnectionString;
        _db = new PostgresWinAdminDbContext(new DbContextOptionsBuilder<PostgresWinAdminDbContext>().UseNpgsql(_cs).Options);
    }

    public void Dispose()
    {
        if (PostgresFactAttribute.ConnectionString is not null)
            _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [PostgresFact]
    public async Task Migrations_create_database_and_audit_sorts_newest_first()
    {
        await _db.Database.MigrateAsync();
        var audit = new AuditService(_db);
        await audit.WriteAsync(new AuditEntryDto { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), Actor = "a", Action = "old", Success = true });
        await audit.WriteAsync(new AuditEntryDto { Timestamp = DateTimeOffset.UtcNow, Actor = "b", Action = "new", Success = true });

        var entries = await audit.QueryAsync();
        Assert.Equal("new", entries[0].Action);
        Assert.Empty(await _db.Database.GetPendingMigrationsAsync());
    }

    [PostgresFact]
    public async Task Unique_violation_is_detected_like_on_sqlite()
    {
        await _db.Database.MigrateAsync();
        var excluded = new ExcludedUserService(_db);
        await excluded.AddAsync("svc_backup");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => excluded.AddAsync("svc_backup"));
        // Контроллер распознаёт дубль по слову unique/UNIQUE в сообщении (ExcludedUsersController).
        string all = ex.ToString();
        Assert.True(all.Contains("UNIQUE") || all.Contains("unique"), all);
    }
}
```

(Если конструктор `ExcludedUserService` или `AuditService` принимает другие параметры, смотреть `src/backend/WinAdmin.Infrastructure/Settings/ExcludedUserService.cs` и `Security/AuditService.cs` и передать то же, что в существующих тестах `AuditServiceTests`.)

- [ ] **Step 4: Run without and with PostgreSQL**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~PostgresStorageTests`
Expected: 2 пропущено (`WINADMIN_TEST_POSTGRES не задана`).

Run (PowerShell):

```powershell
$env:WINADMIN_TEST_POSTGRES = (& .\src\tests\start-test-postgres.ps1)
dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~PostgresStorageTests|FullyQualifiedName~DatabaseMigrationTests"
& .\src\tests\start-test-postgres.ps1 -Stop; Remove-Item Env:WINADMIN_TEST_POSTGRES
```

Expected: все PASS (2 Postgres + 3 миграции). Если тест падает — это расхождение провайдеров, исправлять в коде, а не в тесте (systematic-debugging).

- [ ] **Step 5: Commit**

```bash
git add src/tests/start-test-postgres.ps1 src/tests/WinAdmin.Tests/PostgresFactAttribute.cs src/tests/WinAdmin.Tests/PostgresStorageTests.cs
git commit -m "test(storage): run storage tests against a temporary PostgreSQL cluster"
```

---

### Task 7: CLI `db` и `keys`

**Files:**
- Create: `src/backend/WinAdmin.Api/Cli/DbCommands.cs`
- Create: `src/backend/WinAdmin.Api/Cli/KeysCommands.cs`
- Modify: `src/backend/WinAdmin.Api/Cli/CliRunner.cs` (регистрация)
- Modify: `src/backend/WinAdmin.Api/Program.cs:21` (CLI-режим для `db`, `keys`)
- Test: `src/tests/WinAdmin.Tests/DbAndKeysCommandsTests.cs`

**Interfaces:**
- Consumes: `DatabaseSettingsStore`, `DatabaseSettings`, `DatabaseProvider`, `AddWinAdminDatabase`, `SqliteWinAdminDbContext`, `PostgresWinAdminDbContext` (Tasks 4–5); `MasterKeyStore` (Task 2).
- Produces:
  - `static void DbCommands.Register(Command db, DatabaseSettingsStore store, string defaultSqlitePath, TextWriter? output = null, TextWriter? error = null)`
  - `static void KeysCommands.Register(Command keys, MasterKeyStore store, TextWriter? output = null, TextWriter? error = null)`
  - `static WinAdminDbContext DbCommands.CreateContext(DatabaseSettings resolved)` — контекст нужного провайдера без DI

- [ ] **Step 1: Write the failing tests**

`src/tests/WinAdmin.Tests/DbAndKeysCommandsTests.cs`:

```csharp
using System.CommandLine;
using System.Security.Cryptography;
using WinAdmin.Api.Cli;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DbAndKeysCommandsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-clidb-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly DatabaseSettingsStore _store;
    private readonly MasterKeyStore _keys;
    private readonly RootCommand _root;

    public DbAndKeysCommandsTests()
    {
        Directory.CreateDirectory(_dir);
        _keys = new MasterKeyStore(Path.Combine(_dir, "keys"));
        _store = new DatabaseSettingsStore(_dir, new AesGcmSecretProtector(_keys.LoadOrCreate()));
        var db = new Command("db");
        DbCommands.Register(db, _store, Path.Combine(_dir, "WinAdmin.db"), _out, _err);
        var keys = new Command("keys");
        KeysCommands.Register(keys, _keys, _out, _err);
        _root = new RootCommand { db, keys };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Show_prints_default_sqlite()
    {
        Assert.Equal(0, await _root.InvokeAsync(["db", "show"]));
        Assert.Contains("sqlite", _out.ToString());
        Assert.Contains("WinAdmin.db", _out.ToString());
    }

    [Fact]
    public async Task Set_sqlite_creates_and_migrates_database()
    {
        string path = Path.Combine(_dir, "other.db");
        Assert.Equal(0, await _root.InvokeAsync(["db", "set", "--provider", "sqlite", "--path", path]));
        Assert.True(File.Exists(path));
        Assert.Equal($"Data Source={path}", _store.Read("x").ConnectionString);
        Assert.Contains("Перезапустите службу", _out.ToString());
    }

    [Fact]
    public async Task Unreachable_postgres_is_not_saved()
    {
        int code = await _root.InvokeAsync(["db", "set", "--provider", "postgresql", "--connection",
            "Host=127.0.0.1;Port=1;Username=x;Password=secret;Timeout=2"]);
        Assert.Equal(1, code);
        Assert.False(_store.Exists);
        Assert.DoesNotContain("secret", _err.ToString() + _out.ToString());
    }

    [Fact]
    public async Task Keys_export_import_roundtrip_and_force_rule()
    {
        string file = Path.Combine(_dir, "key.bin");
        Assert.Equal(0, await _root.InvokeAsync(["keys", "export", "--file", file, "--password", "pw-123456"]));
        Assert.True(File.Exists(file));

        // Ключ уже есть → без --force нельзя.
        Assert.Equal(1, await _root.InvokeAsync(["keys", "import", "--file", file, "--password", "pw-123456"]));
        Assert.Contains("--force", _err.ToString());

        Assert.Equal(0, await _root.InvokeAsync(["keys", "import", "--file", file, "--password", "pw-123456", "--force"]));
    }

    [Fact]
    public async Task Short_export_password_is_rejected()
    {
        Assert.Equal(1, await _root.InvokeAsync(["keys", "export", "--file", Path.Combine(_dir, "k.bin"), "--password", "short"]));
        Assert.Contains("8", _err.ToString());
    }

    [PostgresFact]
    public async Task Set_postgres_migrates_and_encrypts_password()
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(PostgresFactAttribute.ConnectionString)
        {
            Database = "winadmin_cli_" + Guid.NewGuid().ToString("N")[..10],
            Password = "pg-pass",
        };
        try
        {
            Assert.Equal(0, await _root.InvokeAsync(["db", "set", "--provider", "postgresql", "--connection", b.ConnectionString]));
            Assert.DoesNotContain("pg-pass", File.ReadAllText(_store.FilePath));
        }
        finally
        {
            using var ctx = DbCommands.CreateContext(_store.ResolveForUse(_store.Read("x")));
            ctx.Database.EnsureDeleted();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/tests/WinAdmin.Tests --filter FullyQualifiedName~DbAndKeysCommandsTests`
Expected: build FAIL — `DbCommands` не найден.

- [ ] **Step 3: Implement `DbCommands`**

`src/backend/WinAdmin.Api/Cli/DbCommands.cs`:

```csharp
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

public static class DbCommands
{
    public static void Register(Command db, DatabaseSettingsStore store, string defaultSqlitePath,
        TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        db.AddCommand(Show(store, defaultSqlitePath, o));
        db.AddCommand(Set(store, defaultSqlitePath, o, e));
    }

    /// <summary>Контекст выбранного провайдера без DI (для CLI).</summary>
    public static WinAdminDbContext CreateContext(DatabaseSettings resolved) => resolved.Provider switch
    {
        DatabaseProvider.PostgreSql => new PostgresWinAdminDbContext(
            new DbContextOptionsBuilder<PostgresWinAdminDbContext>().UseNpgsql(resolved.ConnectionString).Options),
        _ => new SqliteWinAdminDbContext(
            new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(resolved.ConnectionString).Options),
    };

    private static Command Show(DatabaseSettingsStore store, string defaultSqlitePath, TextWriter output)
    {
        var cmd = new Command("show", "Показать текущую базу данных");
        cmd.SetHandler(() =>
        {
            output.WriteLine($"Файл:   {store.FilePath}{(store.Exists ? "" : " (нет — используется значение по умолчанию)")}");
            output.WriteLine($"База:   {DatabaseSettingsStore.Describe(store.Read(defaultSqlitePath))}");
        });
        return cmd;
    }

    private static Command Set(DatabaseSettingsStore store, string defaultSqlitePath, TextWriter output, TextWriter error)
    {
        var providerOpt = new Option<string>("--provider", "sqlite или postgresql") { IsRequired = true };
        var pathOpt = new Option<string?>("--path", "Путь к файлу SQLite (для sqlite)");
        var connectionOpt = new Option<string?>("--connection", "Строка подключения PostgreSQL: Host=…;Database=…;Username=…;Password=…");
        var cmd = new Command("set", "Выбрать базу данных (проверяет подключение и применяет миграции)")
            { providerOpt, pathOpt, connectionOpt };

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            DatabaseProvider provider;
            try
            {
                provider = DatabaseSettingsStore.ParseProvider(ctx.ParseResult.GetValueForOption(providerOpt)!);
            }
            catch (ArgumentException ex)
            {
                error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
                return;
            }

            DatabaseSettings settings;
            if (provider == DatabaseProvider.Sqlite)
            {
                string path = ctx.ParseResult.GetValueForOption(pathOpt) ?? defaultSqlitePath;
                settings = new DatabaseSettings(provider, $"Data Source={Path.GetFullPath(path)}");
            }
            else
            {
                string? connection = ctx.ParseResult.GetValueForOption(connectionOpt);
                if (string.IsNullOrWhiteSpace(connection))
                {
                    error.WriteLine("Для postgresql укажите --connection.");
                    ctx.ExitCode = 1;
                    return;
                }
                settings = new DatabaseSettings(provider, connection);
            }

            var previous = store.Read(defaultSqlitePath);
            try
            {
                await using var db = CreateContext(settings);
                await db.Database.MigrateAsync();
            }
            catch (Exception ex) when (ex is NpgsqlException or Microsoft.Data.Sqlite.SqliteException
                                           or InvalidOperationException or ArgumentException or TimeoutException)
            {
                error.WriteLine($"Не удалось подключиться к базе: {ex.GetBaseException().Message}");
                error.WriteLine("Настройки не изменены.");
                ctx.ExitCode = 1;
                return;
            }

            store.Write(settings);
            output.WriteLine($"Сохранено: {DatabaseSettingsStore.Describe(settings)}");
            if (DatabaseSettingsStore.Describe(previous) != DatabaseSettingsStore.Describe(settings))
                output.WriteLine("Данные из прежней базы не переносятся.");
            output.WriteLine("Перезапустите службу WinAdmin, чтобы она подключилась к новой базе.");
        });
        return cmd;
    }
}
```

`Describe` не выводит пароль; сообщения исключений Npgsql пароль не содержат (проверяется тестом `Unreachable_postgres_is_not_saved`).

- [ ] **Step 4: Implement `KeysCommands`**

`src/backend/WinAdmin.Api/Cli/KeysCommands.cs`:

```csharp
using System.CommandLine;
using System.CommandLine.Invocation;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Api.Cli;

public static class KeysCommands
{
    private const int MinPasswordLength = 8;

    public static void Register(Command keys, MasterKeyStore store, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        keys.AddCommand(Export(store, o, e));
        keys.AddCommand(Import(store, o, e));
    }

    private static Command Export(MasterKeyStore store, TextWriter output, TextWriter error)
    {
        var fileOpt = new Option<string>("--file", "Куда сохранить ключ") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", $"Пароль для файла (не короче {MinPasswordLength} символов)") { IsRequired = true };
        var cmd = new Command("export", "Экспортировать ключ шифрования (для переноса на другой сервер)") { fileOpt, passwordOpt };
        cmd.SetHandler((InvocationContext ctx) =>
        {
            string password = ctx.ParseResult.GetValueForOption(passwordOpt)!;
            if (password.Length < MinPasswordLength)
            {
                error.WriteLine($"Пароль должен быть не короче {MinPasswordLength} символов.");
                ctx.ExitCode = 1;
                return;
            }
            try
            {
                string file = ctx.ParseResult.GetValueForOption(fileOpt)!;
                File.WriteAllBytes(file, store.Export(password));
                output.WriteLine($"Ключ сохранён в {file}. Храните файл и пароль отдельно друг от друга.");
            }
            catch (Exception ex) when (ex is SecretUnavailableException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command Import(MasterKeyStore store, TextWriter output, TextWriter error)
    {
        var fileOpt = new Option<string>("--file", "Файл экспорта ключа") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", "Пароль файла") { IsRequired = true };
        var forceOpt = new Option<bool>("--force", "Заменить существующий ключ");
        var cmd = new Command("import", "Импортировать ключ шифрования") { fileOpt, passwordOpt, forceOpt };
        cmd.SetHandler((InvocationContext ctx) =>
        {
            try
            {
                byte[] data = File.ReadAllBytes(ctx.ParseResult.GetValueForOption(fileOpt)!);
                store.Import(data, ctx.ParseResult.GetValueForOption(passwordOpt)!, ctx.ParseResult.GetValueForOption(forceOpt));
                output.WriteLine($"Ключ импортирован в {store.FilePath}. Перезапустите службу WinAdmin.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
            }
        });
        return cmd;
    }
}
```

- [ ] **Step 5: Register in `CliRunner` and `Program`**

`CliRunner.cs` — перед `var root = new RootCommand(...)`:

```csharp
        var dbCommand = new Command("db", "База данных WinAdmin (SQLite / PostgreSQL)");
        DbCommands.Register(dbCommand, databaseStore, dbPath);

        var keysCommand = new Command("keys", "Ключ шифрования секретов");
        KeysCommands.Register(keysCommand, keys);
```

и `var root = new RootCommand("WinAdmin CLI") { userCommand, networkCommand, dbCommand, keysCommand };`.

Для `db set` нужен действующий ключ (шифрование пароля): в выражении выбора `protector` из Task 5 заменить `args is ["user", ..]` на `args is ["user", ..] or ["db", ..] or ["keys", "export", ..]`.

`Program.cs` — условие CLI-режима: `if (args.Length > 0 && args[0] is "user" or "network" or "db" or "keys")`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/tests/WinAdmin.Tests`
Expected: всё PASS (Postgres-тесты пропущены без переменной).

С PostgreSQL:

```powershell
$env:WINADMIN_TEST_POSTGRES = (& .\src\tests\start-test-postgres.ps1)
dotnet test src/tests/WinAdmin.Tests --filter "FullyQualifiedName~DbAndKeysCommandsTests|FullyQualifiedName~PostgresStorageTests"
& .\src\tests\start-test-postgres.ps1 -Stop; Remove-Item Env:WINADMIN_TEST_POSTGRES
```

Expected: все PASS.

- [ ] **Step 7: Smoke-test the real CLI**

```powershell
$env:WinAdmin__DatabasePath = "$env:TEMP\winadmin-smoke1a\WinAdmin.db"
dotnet run --no-launch-profile --project src/backend/WinAdmin.Api -- db show
dotnet run --no-launch-profile --project src/backend/WinAdmin.Api -- keys export --file "$env:TEMP\winadmin-smoke1a\k.bin" --password "smoke-12345"
Remove-Item -Recurse -Force "$env:TEMP\winadmin-smoke1a"; Remove-Item Env:WinAdmin__DatabasePath
```

Expected: `db show` печатает `sqlite: Data Source=…WinAdmin.db`; `keys export` печатает «Ключ сохранён…».

- [ ] **Step 8: Commit**

```bash
git add src/backend/WinAdmin.Api src/tests/WinAdmin.Tests/DbAndKeysCommandsTests.cs
git commit -m "feat(cli): add db show/set and keys export/import"
```

---

### Task 8: Документация

**Files:**
- Modify: `releases/package/README.md` (новый раздел после «Сетевой доступ»)
- Modify: `releases/package/docs/security.md`

- [ ] **Step 1: README**

В `releases/package/README.md` после раздела `## 5. Сетевой доступ` (перед следующим `---`) вставить:

```markdown
## 5а. База данных и ключ шифрования

По умолчанию WinAdmin хранит данные в SQLite (`C:\ProgramData\WinAdmin\WinAdmin.db`). Можно перейти на PostgreSQL:

```powershell
.\WinAdmin.exe db show
.\WinAdmin.exe db set --provider postgresql --connection "Host=db01;Database=winadmin;Username=winadmin;Password=..."
.\WinAdmin.exe db set --provider sqlite
Restart-Service WinAdmin
```

`db set` проверяет подключение и создаёт таблицы. Данные между базами **не переносятся**.

Пароли и ключи (пароль PostgreSQL, JWT-секрет, секреты модулей) хранятся зашифрованными ключом
`C:\ProgramData\WinAdmin\keys\master.key` (защищён DPAPI машины, доступ — только администраторы и SYSTEM).
Без этого ключа зашифрованные значения не восстановить. Сохраните его копию:

```powershell
.\WinAdmin.exe keys export --file D:\backup\winadmin-key.bin --password "<надёжный пароль>"
.\WinAdmin.exe keys import --file D:\backup\winadmin-key.bin --password "<пароль>"          # на новом сервере
```

`keys import` не заменяет существующий ключ без `--force`.
```

- [ ] **Step 2: security.md**

В `releases/package/docs/security.md`:
- В таблице «Важные переменные окружения» строку `WinAdmin__Jwt__Secret` изменить на: `| \`WinAdmin__Jwt__Secret\` | Необязательно: по умолчанию JWT-секрет генерируется один раз и хранится зашифрованным в \`keys\jwt.key\` |`.
- В «Рекомендации для сервера» заменить пункт «Задать `WinAdmin__Jwt__Secret` до продакшн-использования.» на «Сохранить копию ключа шифрования: `WinAdmin.exe keys export` (файл и пароль хранить раздельно).»

- [ ] **Step 3: Commit**

```bash
git add releases/package
git commit -m "docs: database provider and encryption key management"
```
