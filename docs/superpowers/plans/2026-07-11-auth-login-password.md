# Auth Login/Password + User CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить аутентификацию по логину/паролю для веб-интерфейса (JWT + Refresh Token) и CLI-управление пользователями через `WinAdmin.Api.exe user <cmd>`, не затрагивая существующий X-API-Key механизм.

**Architecture:** Два параллельных auth scheme ("ApiKey" + "Bearer") объединены PolicyScheme "Auto" — существующие контроллеры не меняются. Refresh Token хранится как SHA256-хеш в SQLite, raw token передаётся в httpOnly cookie `wa_refresh`. Frontend заменяет KeyGate на LoginForm, JWT хранится в sessionStorage, axios interceptor автоматически обновляет токен при 401.

**Tech Stack:** `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.x · `Microsoft.Extensions.Identity.Core` 10.0.x · `System.CommandLine` 2.0.0-beta4 · React 19 · Ant Design 6 · AG Grid 35

## Global Constraints

- Target framework: `net10.0-windows`
- Все scopes берутся из `WinAdmin.Core.Security.Scopes.All` — новых не добавляем
- Refresh cookie name: `wa_refresh`; JWT claim для scopes: `ApiKeyDefaults.ScopeClaimType = "scope"`
- Пароли: `PasswordHasher<UserEntity>` из `Microsoft.Extensions.Identity.Core`
- JWT claim `ClaimTypes.Name` = Login; `ClaimTypes.NameIdentifier` = Id пользователя
- Все DateTimeOffset в SQLite хранятся как UTC ticks (long) — следовать паттерну DbContext
- Маршруты: `/api/v1/auth/...` и `/api/v1/users`
- CLI entry: `args[0] == "user"` → CLI mode, иначе — веб-сервер

---

## File Map

### Новые файлы
| Файл | Отвечает за |
|---|---|
| `src/backend/WinAdmin.Core/Models/UserModels.cs` | DTOs, request/response types, JwtOptions, UserPrincipal |
| `src/backend/WinAdmin.Core/Abstractions/IUserService.cs` | Интерфейс CRUD + валидации пользователей |
| `src/backend/WinAdmin.Core/Abstractions/ITokenService.cs` | Интерфейс генерации JWT и refresh токенов |
| `src/backend/WinAdmin.Infrastructure/Security/UserService.cs` | Реализация: CRUD + PasswordHasher |
| `src/backend/WinAdmin.Infrastructure/Security/TokenService.cs` | Реализация: JWT + refresh tokens в БД |
| `src/backend/WinAdmin.Api/Controllers/AuthController.cs` | POST login/refresh/logout, GET me |
| `src/backend/WinAdmin.Api/Controllers/UsersController.cs` | CRUD пользователей (scope: admin) |
| `src/backend/WinAdmin.Api/Cli/CliRunner.cs` | CLI entry point, минимальный DI |
| `src/backend/WinAdmin.Api/Cli/UserCommands.cs` | Команды: add, list, password, scopes, deactivate, activate, delete |
| `src/frontend/src/api/authApi.ts` | login(), refresh(), logout(), me() |
| `src/frontend/src/auth/LoginForm.tsx` | Форма входа (заменяет KeyGate) |
| `src/frontend/src/auth/AuthProvider.tsx` | React context: user, scopes, logout |
| `src/frontend/src/pages/Users.tsx` | Страница управления пользователями |

### Изменяемые файлы
| Файл | Что меняется |
|---|---|
| `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs` | + UserEntity, RefreshTokenEntity, DbSet, OnModelCreating |
| `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs` | + UserService, TokenService |
| `src/backend/WinAdmin.Api/Program.cs` | CLI entry, JWT scheme "Auto", empty-users hint |
| `src/backend/WinAdmin.Api/appsettings.json` | + секция WinAdmin:Jwt |
| `src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj` | + NuGet packages |
| `src/backend/WinAdmin.Api/WinAdmin.Api.csproj` | + System.CommandLine |
| `src/frontend/src/api/types.ts` | + UserDto, TokenResponse |
| `src/frontend/src/api/client.ts` | JWT storage, refresh interceptor |
| `src/frontend/src/auth/KeyGate.tsx` | Удалить (заменён LoginForm) |
| `src/frontend/src/components/AppLayout.tsx` | + пункт меню Users (только admin) |
| `src/frontend/src/App.tsx` | AuthProvider, LoginForm, /cp/users route |
| `src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj` | + Microsoft.Extensions.Identity.Core для тестов |

---

## Task 1: DB Entities + EF Migration

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs`
- Create (generated): `src/backend/WinAdmin.Infrastructure/Storage/Migrations/<ts>_AddUsers.cs`

**Interfaces:**
- Produces: `WinAdminDbContext.Users`, `WinAdminDbContext.RefreshTokens`, `UserEntity`, `RefreshTokenEntity`

- [ ] **Step 1: Добавить сущности и DbSet в WinAdminDbContext.cs**

Открыть `src/backend/WinAdmin.Infrastructure/Storage/WinAdminDbContext.cs`. Добавить в конец файла (перед последней `}`) два новых класса, в класс `WinAdminDbContext` — два DbSet, в `OnModelCreating` — конфигурацию:

```csharp
// В WinAdminDbContext — добавить после DbSet<AuditEntryEntity>:
public DbSet<UserEntity> Users => Set<UserEntity>();
public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
```

В `OnModelCreating` добавить после блока `modelBuilder.Entity<AuditEntryEntity>`:

```csharp
modelBuilder.Entity<UserEntity>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Login).IsRequired().HasMaxLength(100);
    e.HasIndex(x => x.Login).IsUnique();
    e.Property(x => x.PasswordHash).IsRequired();
    e.Property(x => x.Scopes).IsRequired();
    e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
});

modelBuilder.Entity<RefreshTokenEntity>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.TokenHash).IsRequired();
    e.HasIndex(x => x.TokenHash).IsUnique();
    e.Property(x => x.ExpiresAt).HasConversion(DtoConverter);
    e.Property(x => x.RevokedAt).HasConversion(NullableDtoConverter);
    e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
    e.HasOne(x => x.User).WithMany(x => x.RefreshTokens)
        .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
});
```

В конец файла добавить классы сущностей:

```csharp
public sealed class UserEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Scopes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = [];
}

public sealed class RefreshTokenEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public UserEntity User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Сгенерировать миграцию**

```bash
cd C:\apps\SysPanel
dotnet ef migrations add AddUsers \
  --project src/backend/WinAdmin.Infrastructure \
  --startup-project src/backend/WinAdmin.Api
```

Ожидаемый вывод: `Build succeeded. Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 3: Проверить сборку**

```bash
dotnet build WinAdmin.slnx
```

Ожидаемый вывод: `Ошибок: 0`

---

## Task 2: Core Models + Interfaces

**Files:**
- Create: `src/backend/WinAdmin.Core/Models/UserModels.cs`
- Create: `src/backend/WinAdmin.Core/Abstractions/IUserService.cs`
- Create: `src/backend/WinAdmin.Core/Abstractions/ITokenService.cs`

**Interfaces:**
- Produces: `UserDto`, `CreateUserRequest`, `LoginRequest`, `TokenResponse`, `UserPrincipal`, `JwtOptions`, `IUserService`, `ITokenService`

- [ ] **Step 1: Создать UserModels.cs**

```csharp
// src/backend/WinAdmin.Core/Models/UserModels.cs
namespace WinAdmin.Core.Models;

public sealed record UserDto
{
    public required string Id { get; init; }
    public required string Login { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsActive { get; init; }
}

public sealed record CreateUserRequest
{
    public required string Login { get; init; }
    public required string Password { get; init; }
    public List<string> Scopes { get; init; } = [];
}

public sealed record UpdateScopesRequest
{
    public List<string> Scopes { get; init; } = [];
}

public sealed record ChangePasswordRequest
{
    public required string NewPassword { get; init; }
}

public sealed record SetActiveRequest
{
    public bool IsActive { get; init; }
}

public sealed record LoginRequest
{
    public required string Login { get; init; }
    public required string Password { get; init; }
}

public sealed record TokenResponse
{
    public required string AccessToken { get; init; }
    public int ExpiresIn { get; init; }
}

public sealed record UserPrincipal
{
    public required string Id { get; init; }
    public required string Login { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public sealed class JwtOptions
{
    public string Secret { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public string Issuer { get; set; } = "WinAdmin";
    public string Audience { get; set; } = "WinAdmin";
}
```

- [ ] **Step 2: Создать IUserService.cs**

```csharp
// src/backend/WinAdmin.Core/Abstractions/IUserService.cs
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<bool> UpdateScopesAsync(string id, IEnumerable<string> scopes, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default);
    Task<bool> SetActiveAsync(string id, bool isActive, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Проверяет логин и пароль. Возвращает UserPrincipal или null.</summary>
    Task<UserPrincipal?> ValidateAsync(string login, string password, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Создать ITokenService.cs**

```csharp
// src/backend/WinAdmin.Core/Abstractions/ITokenService.cs
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

public interface ITokenService
{
    /// <summary>Генерирует JWT access token для пользователя.</summary>
    string GenerateAccessToken(UserPrincipal user);

    /// <summary>Создаёт refresh token в БД, возвращает raw token для cookie.</summary>
    Task<string> CreateRefreshTokenAsync(string userId, CancellationToken ct = default);

    /// <summary>Проверяет raw refresh token. Возвращает UserPrincipal или null.</summary>
    Task<UserPrincipal?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Отзывает refresh token (logout).</summary>
    Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default);
}
```

- [ ] **Step 4: Проверить сборку**

```bash
dotnet build WinAdmin.slnx
```

Ожидаемый вывод: `Ошибок: 0`

---

## Task 3: UserService + Tests

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Security/UserService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj`
- Modify: `src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj`
- Modify: `src/tests/WinAdmin.Tests/` (добавить `UserServiceTests.cs`)

**Interfaces:**
- Consumes: `IUserService`, `UserEntity`, `WinAdminDbContext`, `UserDto`, `CreateUserRequest`, `UserPrincipal`, `Scopes.IsValid`
- Produces: `UserService` (реализация `IUserService`)

- [ ] **Step 1: Добавить NuGet в Infrastructure**

```bash
dotnet add src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj \
  package Microsoft.Extensions.Identity.Core
```

- [ ] **Step 2: Написать тест (failing)**

Создать `src/tests/WinAdmin.Tests/UserServiceTests.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class UserServiceTests : IDisposable
{
    private readonly WinAdminDbContext _db;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        var opts = new DbContextOptionsBuilder<WinAdminDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new WinAdminDbContext(opts);
        _sut = new UserService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_StoresHashedPassword()
    {
        var req = new CreateUserRequest { Login = "alice", Password = "Secret1!", Scopes = ["admin"] };
        var dto = await _sut.CreateAsync(req);

        var entity = await _db.Users.FirstAsync(u => u.Login == "alice");
        Assert.NotEqual("Secret1!", entity.PasswordHash);
        Assert.Equal("alice", dto.Login);
        Assert.Contains("admin", dto.Scopes);
    }

    [Fact]
    public async Task ValidateAsync_CorrectPassword_ReturnsPrincipal()
    {
        await _sut.CreateAsync(new CreateUserRequest { Login = "bob", Password = "Pass1!", Scopes = ["system.read"] });
        var principal = await _sut.ValidateAsync("bob", "Pass1!");
        Assert.NotNull(principal);
        Assert.Equal("bob", principal.Login);
    }

    [Fact]
    public async Task ValidateAsync_WrongPassword_ReturnsNull()
    {
        await _sut.CreateAsync(new CreateUserRequest { Login = "carol", Password = "Pass1!", Scopes = [] });
        var principal = await _sut.ValidateAsync("carol", "wrong");
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateAsync_InactiveUser_ReturnsNull()
    {
        var dto = await _sut.CreateAsync(new CreateUserRequest { Login = "dave", Password = "Pass1!", Scopes = [] });
        await _sut.SetActiveAsync(dto.Id, false);
        var principal = await _sut.ValidateAsync("dave", "Pass1!");
        Assert.Null(principal);
    }

    [Fact]
    public async Task ChangePasswordAsync_UpdatesHash()
    {
        var dto = await _sut.CreateAsync(new CreateUserRequest { Login = "eve", Password = "Old1!", Scopes = [] });
        await _sut.ChangePasswordAsync(dto.Id, "New1!");
        Assert.NotNull(await _sut.ValidateAsync("eve", "New1!"));
        Assert.Null(await _sut.ValidateAsync("eve", "Old1!"));
    }
}
```

- [ ] **Step 3: Добавить InMemory пакет в тесты**

```bash
dotnet add src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj \
  package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 4: Запустить тест — убедиться что FAIL**

```bash
dotnet test src/tests/WinAdmin.Tests --filter "UserServiceTests"
```

Ожидаемый вывод: ошибка компиляции (`UserService` не найден)

- [ ] **Step 5: Реализовать UserService**

Создать `src/backend/WinAdmin.Infrastructure/Security/UserService.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

public sealed class UserService : IUserService
{
    private readonly WinAdminDbContext _db;
    private readonly PasswordHasher<UserEntity> _hasher = new();

    public UserService(WinAdminDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Login).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var scopes = NormalizeScopes(request.Scopes);
        var entity = new UserEntity
        {
            Login = request.Login.Trim(),
            Scopes = string.Join(',', scopes),
        };
        entity.PasswordHash = _hasher.HashPassword(entity, request.Password);
        _db.Users.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateScopesAsync(string id, IEnumerable<string> scopes, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        entity.Scopes = string.Join(',', NormalizeScopes(scopes));
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        entity.PasswordHash = _hasher.HashPassword(entity, newPassword);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetActiveAsync(string id, bool isActive, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        entity.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        _db.Users.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UserPrincipal?> ValidateAsync(string login, string password, CancellationToken ct = default)
    {
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Login == login, ct);
        if (entity is null || !entity.IsActive) return null;
        var result = _hasher.VerifyHashedPassword(entity, entity.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        return ToPrincipal(entity);
    }

    public Task<bool> AnyAsync(CancellationToken ct = default)
        => _db.Users.AnyAsync(ct);

    private static List<string> NormalizeScopes(IEnumerable<string> scopes)
        => scopes.Select(s => s.Trim()).Where(Scopes.IsValid).Distinct().ToList();

    private static UserDto ToDto(UserEntity e) => new()
    {
        Id = e.Id,
        Login = e.Login,
        Scopes = e.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        CreatedAt = e.CreatedAt,
        IsActive = e.IsActive,
    };

    private static UserPrincipal ToPrincipal(UserEntity e) => new()
    {
        Id = e.Id,
        Login = e.Login,
        Scopes = e.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };
}
```

- [ ] **Step 6: Запустить тесты — убедиться что PASS**

```bash
dotnet test src/tests/WinAdmin.Tests --filter "UserServiceTests"
```

Ожидаемый вывод: `Пройден! : не пройдено 0, пройдено 5`

---

## Task 4: TokenService + Tests

**Files:**
- Create: `src/backend/WinAdmin.Infrastructure/Security/TokenService.cs`
- Modify: `src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj`
- Create: `src/tests/WinAdmin.Tests/TokenServiceTests.cs`

**Interfaces:**
- Consumes: `ITokenService`, `UserPrincipal`, `JwtOptions`, `RefreshTokenEntity`, `WinAdminDbContext`, `UserEntity`
- Produces: `TokenService` (реализация `ITokenService`)

- [ ] **Step 1: Добавить NuGet в Infrastructure**

```bash
dotnet add src/backend/WinAdmin.Infrastructure/WinAdmin.Infrastructure.csproj \
  package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Написать тест (failing)**

Создать `src/tests/WinAdmin.Tests/TokenServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class TokenServiceTests : IDisposable
{
    private readonly WinAdminDbContext _db;
    private readonly TokenService _sut;
    private readonly JwtOptions _opts = new()
    {
        Secret = "test-secret-key-must-be-at-least-32-chars!!",
        AccessTokenMinutes = 60,
        RefreshTokenDays = 30,
        Issuer = "WinAdmin",
        Audience = "WinAdmin",
    };

    public TokenServiceTests()
    {
        var dbOpts = new DbContextOptionsBuilder<WinAdminDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new WinAdminDbContext(dbOpts);
        _sut = new TokenService(_db, _opts);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void GenerateAccessToken_ContainsLoginAndScopes()
    {
        var principal = new UserPrincipal { Id = "1", Login = "admin", Scopes = ["admin"] };
        var token = _sut.GenerateAccessToken(principal);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal("admin", jwt.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Name).Value);
        Assert.Contains(jwt.Claims, c => c.Type == "scope" && c.Value == "admin");
    }

    [Fact]
    public async Task CreateAndValidateRefreshToken_ReturnsPrincipal()
    {
        var user = new UserEntity { Login = "alice", PasswordHash = "x", Scopes = "admin" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var raw = await _sut.CreateRefreshTokenAsync(user.Id);
        var principal = await _sut.ValidateRefreshTokenAsync(raw);

        Assert.NotNull(principal);
        Assert.Equal("alice", principal.Login);
    }

    [Fact]
    public async Task RevokeRefreshToken_ValidateReturnsNull()
    {
        var user = new UserEntity { Login = "bob", PasswordHash = "x", Scopes = "admin" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var raw = await _sut.CreateRefreshTokenAsync(user.Id);
        await _sut.RevokeRefreshTokenAsync(raw);
        var principal = await _sut.ValidateRefreshTokenAsync(raw);

        Assert.Null(principal);
    }
}
```

- [ ] **Step 3: Запустить тест — убедиться что FAIL**

```bash
dotnet test src/tests/WinAdmin.Tests --filter "TokenServiceTests"
```

Ожидаемый вывод: ошибка компиляции (`TokenService` не найден)

- [ ] **Step 4: Реализовать TokenService**

Создать `src/backend/WinAdmin.Infrastructure/Security/TokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

public sealed class TokenService : ITokenService
{
    private readonly WinAdminDbContext _db;
    private readonly JwtOptions _opts;

    public TokenService(WinAdminDbContext db, JwtOptions opts)
    {
        _db = db;
        _opts = opts;
    }

    public string GenerateAccessToken(UserPrincipal user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Login),
        };
        claims.AddRange(user.Scopes.Select(s => new Claim(ApiKeyDefaults.ScopeClaimType, s)));

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string> CreateRefreshTokenAsync(string userId, CancellationToken ct = default)
    {
        var raw = GenerateRaw();
        var entity = new RefreshTokenEntity
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_opts.RefreshTokenDays),
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<UserPrincipal?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var entity = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (entity is null || entity.RevokedAt is not null) return null;
        if (entity.ExpiresAt < DateTimeOffset.UtcNow) return null;
        if (!entity.User.IsActive) return null;

        return new UserPrincipal
        {
            Id = entity.User.Id,
            Login = entity.User.Login,
            Scopes = entity.User.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };
    }

    public async Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var entity = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (entity is null) return;
        entity.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string GenerateRaw()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
```

**Важно:** `TokenService` использует `ApiKeyDefaults.ScopeClaimType` из проекта `WinAdmin.Api`. Чтобы избежать циклической зависимости, переместить константу в `WinAdmin.Core`. Открыть `src/backend/WinAdmin.Api/Auth/ApiKeyAuthenticationHandler.cs`, убедиться что `ScopeClaimType = "scope"`. Добавить в `WinAdmin.Core/Security/Scopes.cs`:

```csharp
/// <summary>Тип claim для scope (используется в JWT и ApiKey).</summary>
public const string ScopeClaimType = "scope";
```

Обновить `ApiKeyAuthenticationHandler.cs` — заменить `ApiKeyDefaults.ScopeClaimType` на `Scopes.ScopeClaimType` (или просто строку `"scope"` уже используется через константу в `ApiKeyDefaults`, оставить как есть). В `TokenService.cs` использовать `"scope"` напрямую:

```csharp
// Заменить ApiKeyDefaults.ScopeClaimType на "scope" в TokenService.cs
claims.AddRange(user.Scopes.Select(s => new Claim("scope", s)));
```

И убрать using для `WinAdmin.Api.Auth` из `TokenService.cs`.

- [ ] **Step 5: Запустить тесты — убедиться что PASS**

```bash
dotnet test src/tests/WinAdmin.Tests --filter "TokenServiceTests"
```

Ожидаемый вывод: `Пройден! : не пройдено 0, пройдено 3`

---

## Task 5: DI + JWT в Program.cs + AuthController

**Files:**
- Modify: `src/backend/WinAdmin.Infrastructure/DependencyInjection.cs`
- Modify: `src/backend/WinAdmin.Api/Program.cs`
- Modify: `src/backend/WinAdmin.Api/appsettings.json`
- Create: `src/backend/WinAdmin.Api/Controllers/AuthController.cs`

**Interfaces:**
- Consumes: `IUserService`, `ITokenService`, `UserService`, `TokenService`, `JwtOptions`, `LoginRequest`, `TokenResponse`, `UserPrincipal`
- Produces: `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`

- [ ] **Step 1: Обновить appsettings.json**

Открыть `src/backend/WinAdmin.Api/appsettings.json`, добавить секцию `Jwt` в блок `WinAdmin`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "WinAdmin": {
    "DatabasePath": "",
    "BootstrapKey": "",
    "CorsOrigins": [],
    "Jwt": {
      "Secret": "",
      "AccessTokenMinutes": 60,
      "RefreshTokenDays": 30,
      "Issuer": "WinAdmin",
      "Audience": "WinAdmin"
    }
  }
}
```

- [ ] **Step 2: Обновить DependencyInjection.cs**

Добавить регистрацию `UserService` и `TokenService` — `JwtOptions` передаётся как параметр:

```csharp
public static IServiceCollection AddWinAdminInfrastructure(
    this IServiceCollection services,
    string sqliteConnectionString,
    JwtOptions jwtOptions)
{
    services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite(sqliteConnectionString));
    services.AddSingleton(jwtOptions);
    services.AddSingleton<ISystemInfoService, SystemInfoService>();
    services.AddScoped<IDiskService, DiskService>();
    services.AddScoped<IServiceControlService, ServiceControlService>();
    services.AddScoped<IProcessService, ProcessService>();
    services.AddScoped<IPrinterService, PrinterService>();
    services.AddScoped<IPowerService, PowerService>();
    services.AddScoped<IApiKeyService, ApiKeyService>();
    services.AddScoped<IAuditService, AuditService>();
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<ITokenService, TokenService>();
    return services;
}
```

Добавить using `WinAdmin.Core.Models;` в `DependencyInjection.cs`.

- [ ] **Step 3: Обновить Program.cs**

Заменить содержимое `Program.cs` полностью:

```csharp
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure;
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

var builder = WebApplication.CreateBuilder(args);

// ── Конфигурация ────────────────────────────────────────────────
string? configuredDbPath = builder.Configuration["WinAdmin:DatabasePath"];
string dbPath = string.IsNullOrWhiteSpace(configuredDbPath)
    ? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db")
    : configuredDbPath;
string connectionString = $"Data Source={dbPath}";
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
builder.Services.AddControllers();
builder.Services.AddWinAdminInfrastructure(connectionString, jwtOptions);

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
    var xml = Path.Combine(AppContext.BaseDirectory, "WinAdmin.Api.xml");
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
        app.Logger.LogWarning("WinAdmin.Api.exe user add --login admin --password <пароль> --scopes admin");
        app.Logger.LogWarning("════════════════════════════════════════════════════");
    }
}

// ── Конвейер ────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

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

public partial class Program { }
```

- [ ] **Step 4: Создать AuthController.cs**

```csharp
// src/backend/WinAdmin.Api/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookie = "wa_refresh";
    private readonly IUserService _users;
    private readonly ITokenService _tokens;

    public AuthController(IUserService users, ITokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    /// <summary>Вход по логину и паролю. Возвращает JWT и устанавливает refresh cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var principal = await _users.ValidateAsync(request.Login, request.Password, ct);
        if (principal is null)
            return Unauthorized(new { message = "Неверный логин или пароль" });

        var accessToken = _tokens.GenerateAccessToken(principal);
        var refreshRaw = await _tokens.CreateRefreshTokenAsync(principal.Id, ct);
        SetRefreshCookie(refreshRaw);
        return Ok(new TokenResponse { AccessToken = accessToken, ExpiresIn = 3600 });
    }

    /// <summary>Обновляет access token по refresh cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrWhiteSpace(raw))
            return Unauthorized(new { message = "Refresh token отсутствует" });

        var principal = await _tokens.ValidateRefreshTokenAsync(raw, ct);
        if (principal is null)
        {
            ClearRefreshCookie();
            return Unauthorized(new { message = "Refresh token недействителен или истёк" });
        }

        await _tokens.RevokeRefreshTokenAsync(raw, ct);
        var refreshRaw = await _tokens.CreateRefreshTokenAsync(principal.Id, ct);
        SetRefreshCookie(refreshRaw);
        var accessToken = _tokens.GenerateAccessToken(principal);
        return Ok(new TokenResponse { AccessToken = accessToken, ExpiresIn = 3600 });
    }

    /// <summary>Выход — отзывает refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrWhiteSpace(raw))
            await _tokens.RevokeRefreshTokenAsync(raw, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>Информация о текущем пользователе (требует Bearer token).</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Me() => Ok(new
    {
        login = User.Identity?.Name,
        scopes = User.Claims.Where(c => c.Type == "scope").Select(c => c.Value).ToArray(),
    });

    private void SetRefreshCookie(string raw) =>
        Response.Cookies.Append(RefreshCookie, raw, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            MaxAge = TimeSpan.FromDays(30),
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie);
}
```

- [ ] **Step 5: Проверить сборку и запустить все тесты**

```bash
dotnet build WinAdmin.slnx
dotnet test WinAdmin.slnx
```

Ожидаемый вывод: `Ошибок: 0` и `Пройден! : не пройдено 0, пройдено 17`

---

## Task 6: UsersController

**Files:**
- Create: `src/backend/WinAdmin.Api/Controllers/UsersController.cs`

**Interfaces:**
- Consumes: `IUserService`, `UserDto`, `CreateUserRequest`, `UpdateScopesRequest`, `ChangePasswordRequest`, `SetActiveRequest`, `Scopes.Admin`, `ScopePolicy.Name`
- Produces: `GET /api/v1/users`, `POST /api/v1/users`, `PUT /api/v1/users/{id}/scopes`, `PUT /api/v1/users/{id}/password`, `PUT /api/v1/users/{id}/active`, `DELETE /api/v1/users/{id}`

- [ ] **Step 1: Создать UsersController.cs**

```csharp
// src/backend/WinAdmin.Api/Controllers/UsersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Управление пользователями (требует scope admin).</summary>
[Authorize(Policy = "scope:" + Scopes.Admin)]
[Route("api/v1/users")]
public sealed class UsersController : WinAdminControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>Список пользователей.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct)
        => Ok(await _users.ListAsync(ct));

    /// <summary>Создать пользователя.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        try
        {
            var dto = await _users.CreateAsync(request, ct);
            return CreatedAtAction(nameof(List), new { }, dto);
        }
        catch (Exception ex) when (ex.Message.Contains("UNIQUE") || ex.Message.Contains("unique"))
        {
            return Conflict(new { message = $"Пользователь '{request.Login}' уже существует" });
        }
    }

    /// <summary>Изменить scopes.</summary>
    [HttpPut("{id}/scopes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateScopes(string id, [FromBody] UpdateScopesRequest request, CancellationToken ct)
        => await _users.UpdateScopesAsync(id, request.Scopes, ct) ? NoContent() : NotFound();

    /// <summary>Сменить пароль.</summary>
    [HttpPut("{id}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordRequest request, CancellationToken ct)
        => await _users.ChangePasswordAsync(id, request.NewPassword, ct) ? NoContent() : NotFound();

    /// <summary>Активировать / деактивировать.</summary>
    [HttpPut("{id}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(string id, [FromBody] SetActiveRequest request, CancellationToken ct)
        => await _users.SetActiveAsync(id, request.IsActive, ct) ? NoContent() : NotFound();

    /// <summary>Удалить пользователя.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _users.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
```

- [ ] **Step 2: Проверить сборку**

```bash
dotnet build WinAdmin.slnx
```

Ожидаемый вывод: `Ошибок: 0`

- [ ] **Step 3: Проверить endpoints вручную (после запуска сервера)**

```bash
# Запустить сервер
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/backend/WinAdmin.Api --urls http://localhost:5099

# В другом терминале — создать тестового пользователя через CLI (Task 7 должен быть выполнен)
# Или через bootstrap API-ключ:
BKEY=$(cat src/backend/WinAdmin.Api/bin/Debug/net10.0-windows/bootstrap-key.txt)
curl -s http://localhost:5099/api/v1/users -H "X-API-Key: $BKEY"
# Ожидаемый вывод: []
```

---

## Task 7: CLI

**Files:**
- Modify: `src/backend/WinAdmin.Api/WinAdmin.Api.csproj`
- Create: `src/backend/WinAdmin.Api/Cli/CliRunner.cs`
- Create: `src/backend/WinAdmin.Api/Cli/UserCommands.cs`

**Interfaces:**
- Consumes: `IUserService`, `UserService`, `WinAdminDbContext`, `JwtOptions`, `CreateUserRequest`, `UpdateScopesRequest`
- Produces: `WinAdmin.Api.exe user list|add|password|scopes|deactivate|activate|delete`

- [ ] **Step 1: Добавить System.CommandLine**

```bash
dotnet add src/backend/WinAdmin.Api/WinAdmin.Api.csproj package System.CommandLine --prerelease
```

- [ ] **Step 2: Создать CliRunner.cs**

```csharp
// src/backend/WinAdmin.Api/Cli/CliRunner.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, IConfiguration config)
    {
        // Minimal DI — only DB + user service
        var services = new ServiceCollection();
        string dbPath = config["WinAdmin:DatabasePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db");
        services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUserService, UserService>();

        var provider = services.BuildServiceProvider();

        // Ensure DB is up to date
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            await db.Database.MigrateAsync();
        }

        var userCommand = new Command("user", "Управление пользователями");
        UserCommands.Register(userCommand, provider);

        var root = new RootCommand("WinAdmin CLI") { userCommand };
        return await root.InvokeAsync(args);
    }
}
```

- [ ] **Step 3: Создать UserCommands.cs**

```csharp
// src/backend/WinAdmin.Api/Cli/UserCommands.cs
using System.CommandLine;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Cli;

public static class UserCommands
{
    public static void Register(Command userCommand, IServiceProvider provider)
    {
        userCommand.AddCommand(ListCommand(provider));
        userCommand.AddCommand(AddCommand(provider));
        userCommand.AddCommand(PasswordCommand(provider));
        userCommand.AddCommand(ScopesCommand(provider));
        userCommand.AddCommand(DeactivateCommand(provider));
        userCommand.AddCommand(ActivateCommand(provider));
        userCommand.AddCommand(DeleteCommand(provider));
    }

    private static IUserService GetService(IServiceProvider provider)
        => provider.CreateScope().ServiceProvider.GetRequiredService<IUserService>();

    // ── list ─────────────────────────────────────────────────────
    private static Command ListCommand(IServiceProvider provider)
    {
        var cmd = new Command("list", "Список пользователей");
        cmd.SetHandler(async () =>
        {
            var users = await GetService(provider).ListAsync();
            if (!users.Any())
            {
                Console.WriteLine("Пользователи не найдены.");
                return;
            }
            Console.WriteLine($"{"LOGIN",-20} {"SCOPES",-40} {"CREATED",-12} ACTIVE");
            Console.WriteLine(new string('─', 80));
            foreach (var u in users)
            {
                Console.WriteLine($"{u.Login,-20} {string.Join(',', u.Scopes),-40} {u.CreatedAt:yyyy-MM-dd,-12} {(u.IsActive ? "yes" : "no")}");
            }
        });
        return cmd;
    }

    // ── add ──────────────────────────────────────────────────────
    private static Command AddCommand(IServiceProvider provider)
    {
        var loginOpt = new Option<string>("--login", "Логин") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", "Пароль") { IsRequired = true };
        var scopesOpt = new Option<string>("--scopes", () => "", "Scopes через запятую (например: admin или system.read,disks.read)");
        var cmd = new Command("add", "Создать пользователя") { loginOpt, passwordOpt, scopesOpt };
        cmd.SetHandler(async (login, password, scopesRaw) =>
        {
            var scopes = scopesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var dto = await GetService(provider).CreateAsync(new CreateUserRequest
            {
                Login = login,
                Password = password,
                Scopes = scopes,
            });
            Console.WriteLine($"Создан пользователь: {dto.Login} (scopes: {string.Join(',', dto.Scopes)})");
        }, loginOpt, passwordOpt, scopesOpt);
        return cmd;
    }

    // ── password ─────────────────────────────────────────────────
    private static Command PasswordCommand(IServiceProvider provider)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var passwordArg = new Argument<string>("password", "Новый пароль");
        var cmd = new Command("password", "Сменить пароль") { loginArg, passwordArg };
        cmd.SetHandler(async (login, password) =>
        {
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).ChangePasswordAsync(user.Id, password);
            Console.WriteLine($"Пароль пользователя '{login}' изменён.");
        }, loginArg, passwordArg);
        return cmd;
    }

    // ── scopes ───────────────────────────────────────────────────
    private static Command ScopesCommand(IServiceProvider provider)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var scopesArg = new Argument<string>("scopes", "Scopes через запятую");
        var cmd = new Command("scopes", "Изменить scopes") { loginArg, scopesArg };
        cmd.SetHandler(async (login, scopesRaw) =>
        {
            var scopes = scopesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).UpdateScopesAsync(user.Id, scopes);
            Console.WriteLine($"Scopes пользователя '{login}' обновлены.");
        }, loginArg, scopesArg);
        return cmd;
    }

    // ── deactivate / activate ────────────────────────────────────
    private static Command DeactivateCommand(IServiceProvider provider) =>
        SetActiveCommand(provider, "deactivate", "Деактивировать пользователя", false);

    private static Command ActivateCommand(IServiceProvider provider) =>
        SetActiveCommand(provider, "activate", "Активировать пользователя", true);

    private static Command SetActiveCommand(IServiceProvider provider, string name, string desc, bool active)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var cmd = new Command(name, desc) { loginArg };
        cmd.SetHandler(async (login) =>
        {
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).SetActiveAsync(user.Id, active);
            Console.WriteLine($"Пользователь '{login}' {(active ? "активирован" : "деактивирован")}.");
        }, loginArg);
        return cmd;
    }

    // ── delete ───────────────────────────────────────────────────
    private static Command DeleteCommand(IServiceProvider provider)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var cmd = new Command("delete", "Удалить пользователя") { loginArg };
        cmd.SetHandler(async (login) =>
        {
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).DeleteAsync(user.Id);
            Console.WriteLine($"Пользователь '{login}' удалён.");
        }, loginArg);
        return cmd;
    }
}
```

- [ ] **Step 4: Проверить сборку и протестировать CLI**

```bash
dotnet build WinAdmin.slnx
```

```bash
# Запустить CLI (из папки проекта)
dotnet run --project src/backend/WinAdmin.Api -- user add --login admin --password "Admin1!" --scopes admin
# Ожидаемый вывод: Создан пользователь: admin (scopes: admin)

dotnet run --project src/backend/WinAdmin.Api -- user list
# Ожидаемый вывод:
# LOGIN                SCOPES                                   CREATED      ACTIVE
# ────────────────────────────────────────────────────────────────────────────────
# admin                admin                                    2026-07-11   yes
```

---

## Task 8: Frontend — Types + authApi + client interceptor

**Files:**
- Modify: `src/frontend/src/api/types.ts`
- Create: `src/frontend/src/api/authApi.ts`
- Modify: `src/frontend/src/api/client.ts`

**Interfaces:**
- Produces: `authApi.login()`, `authApi.refresh()`, `authApi.logout()`, `authApi.me()`, `getStoredToken()`, `setStoredToken()`, `clearStoredToken()`; `http` interceptor auto-refreshes on 401

- [ ] **Step 1: Добавить типы в types.ts**

В конец файла `src/frontend/src/api/types.ts` добавить:

```typescript
export interface TokenResponse {
  accessToken: string
  expiresIn: number
}

export interface UserDto {
  id: string
  login: string
  scopes: string[]
  createdAt: string
  isActive: boolean
}

export interface CreateUserRequest {
  login: string
  password: string
  scopes: string[]
}

export interface MeResponse {
  login: string
  scopes: string[]
}
```

- [ ] **Step 2: Создать authApi.ts**

```typescript
// src/frontend/src/api/authApi.ts
import type { MeResponse, TokenResponse } from './types'

export const authApi = {
  login: async (login: string, password: string): Promise<TokenResponse> => {
    const res = await fetch('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login, password }),
      credentials: 'include',
    })
    if (!res.ok) throw new Error((await res.json()).message ?? 'Ошибка входа')
    return res.json()
  },

  refresh: async (): Promise<TokenResponse | null> => {
    const res = await fetch('/api/v1/auth/refresh', {
      method: 'POST',
      credentials: 'include',
    })
    if (!res.ok) return null
    return res.json()
  },

  logout: async (): Promise<void> => {
    await fetch('/api/v1/auth/logout', {
      method: 'POST',
      credentials: 'include',
    })
  },

  me: async (token: string): Promise<MeResponse> => {
    const res = await fetch('/api/v1/auth/me', {
      headers: { Authorization: `Bearer ${token}` },
    })
    if (!res.ok) throw new Error('Unauthorized')
    return res.json()
  },
}
```

- [ ] **Step 3: Заменить client.ts полностью**

```typescript
// src/frontend/src/api/client.ts
import axios from 'axios'
import { authApi } from './authApi'
import type {
  ApiKeyDto, AuditEntryDto, CreatedApiKey, MeResponse, OperationResult,
  PhysicalDisk, PrinterInfo, ProcessInfo, ServiceInfo,
  SystemInfo, SystemMetrics, PowerRequest, UserDto,
  CreateUserRequest, TokenResponse,
} from './types'

// ── JWT storage (sessionStorage — очищается при закрытии вкладки) ──
const TOKEN_KEY = 'wa_token'
export const getStoredToken = () => sessionStorage.getItem(TOKEN_KEY) ?? ''
export const setStoredToken = (token: string) => sessionStorage.setItem(TOKEN_KEY, token)
export const clearStoredToken = () => sessionStorage.removeItem(TOKEN_KEY)

// ── API Key storage (legacy — для совместимости с KeyGate если нужно) ──
const KEY_STORAGE = 'sp_api_key'
export const getStoredKey = () => localStorage.getItem(KEY_STORAGE) ?? ''
export const setStoredKey = (key: string) => localStorage.setItem(KEY_STORAGE, key)
export const clearStoredKey = () => localStorage.removeItem(KEY_STORAGE)

export const http = axios.create({ baseURL: '/api/v1' })

// Request interceptor — добавляет JWT или API key
http.interceptors.request.use((config) => {
  const token = getStoredToken()
  if (token) {
    config.headers['Authorization'] = `Bearer ${token}`
  } else {
    const key = getStoredKey()
    if (key) config.headers['X-API-Key'] = key
  }
  return config
})

// Флаг чтобы не делать несколько refresh одновременно
let refreshPromise: Promise<string | null> | null = null

export const authEvents = new EventTarget()

// Response interceptor — при 401 пробует refresh
http.interceptors.response.use(
  (r) => r,
  async (error) => {
    const originalRequest = error.config
    if (error?.response?.status === 401 && !originalRequest._retry && getStoredToken()) {
      originalRequest._retry = true
      if (!refreshPromise) {
        refreshPromise = authApi.refresh().then((resp) => {
          refreshPromise = null
          if (!resp) { clearStoredToken(); return null }
          setStoredToken(resp.accessToken)
          return resp.accessToken
        })
      }
      const newToken = await refreshPromise
      if (!newToken) {
        authEvents.dispatchEvent(new Event('unauthorized'))
        return Promise.reject(error)
      }
      originalRequest.headers['Authorization'] = `Bearer ${newToken}`
      return http(originalRequest)
    }
    if (error?.response?.status === 401) {
      authEvents.dispatchEvent(new Event('unauthorized'))
    }
    return Promise.reject(error)
  },
)

// ── Эндпоинты ───────────────────────────────────────────────────
export const api = {
  system: () => http.get<SystemInfo>('/system').then((r) => r.data),
  metrics: () => http.get<SystemMetrics>('/system/metrics').then((r) => r.data),
  disks: () => http.get<PhysicalDisk[]>('/disks').then((r) => r.data),

  services: () => http.get<ServiceInfo[]>('/services').then((r) => r.data),
  controlService: (name: string, action: 'start' | 'stop' | 'restart') =>
    http.post<OperationResult>(`/services/${encodeURIComponent(name)}/${action}`).then((r) => r.data),

  processes: () => http.get<ProcessInfo[]>('/processes').then((r) => r.data),
  killProcess: (pid: number) => http.delete<OperationResult>(`/processes/${pid}`).then((r) => r.data),

  printers: () => http.get<PrinterInfo[]>('/printers').then((r) => r.data),
  controlPrinter: (name: string, action: 'pause' | 'resume' | 'purge') =>
    http.post<OperationResult>(`/printers/${encodeURIComponent(name)}/${action}`).then((r) => r.data),

  reboot: (req: PowerRequest) => http.post<OperationResult>('/power/reboot', req).then((r) => r.data),
  shutdown: (req: PowerRequest) => http.post<OperationResult>('/power/shutdown', req).then((r) => r.data),
  cancelPower: () => http.post<OperationResult>('/power/cancel').then((r) => r.data),

  apiKeys: () => http.get<ApiKeyDto[]>('/apikeys').then((r) => r.data),
  availableScopes: () => http.get<string[]>('/apikeys/scopes').then((r) => r.data),
  createKey: (name: string, scopes: string[], expiresAt?: string) =>
    http.post<CreatedApiKey>('/apikeys', { name, scopes, expiresAt }).then((r) => r.data),
  revokeKey: (id: string) => http.delete<OperationResult>(`/apikeys/${id}`).then((r) => r.data),

  audit: (limit = 300) => http.get<AuditEntryDto[]>('/audit', { params: { limit } }).then((r) => r.data),

  users: () => http.get<UserDto[]>('/users').then((r) => r.data),
  createUser: (req: CreateUserRequest) => http.post<UserDto>('/users', req).then((r) => r.data),
  updateUserScopes: (id: string, scopes: string[]) =>
    http.put(`/users/${id}/scopes`, { scopes }),
  changeUserPassword: (id: string, newPassword: string) =>
    http.put(`/users/${id}/password`, { newPassword }),
  setUserActive: (id: string, isActive: boolean) =>
    http.put(`/users/${id}/active`, { isActive }),
  deleteUser: (id: string) => http.delete(`/users/${id}`),
}
```

- [ ] **Step 4: Проверить TypeScript**

```bash
cd src/frontend && npx tsc -p tsconfig.app.json --noEmit
```

Ожидаемый вывод: пусто (нет ошибок)

---

## Task 9: LoginForm + AuthProvider + App.tsx

**Files:**
- Create: `src/frontend/src/auth/LoginForm.tsx`
- Create: `src/frontend/src/auth/AuthProvider.tsx`
- Modify: `src/frontend/src/App.tsx`

**Interfaces:**
- Consumes: `authApi.login()`, `authApi.logout()`, `authApi.refresh()`, `authApi.me()`, `setStoredToken()`, `clearStoredToken()`, `getStoredToken()`
- Produces: `<LoginForm>`, `<AuthProvider>`, `useAuth()` hook, обновлённый `<App>`

- [ ] **Step 1: Создать AuthProvider.tsx**

```tsx
// src/frontend/src/auth/AuthProvider.tsx
import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react'
import { authApi } from '../api/authApi'
import { authEvents, clearStoredToken, getStoredToken, setStoredToken } from '../api/client'
import type { MeResponse } from '../api/types'

interface AuthState {
  user: MeResponse | null
  token: string
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

export function AuthProvider({ children, onLogout }: { children: ReactNode; onLogout: () => void }) {
  const [user, setUser] = useState<MeResponse | null>(null)
  const token = getStoredToken()

  useEffect(() => {
    authApi.me(token).then(setUser).catch(() => setUser(null))
  }, [token])

  useEffect(() => {
    const onUnauthorized = () => {
      clearStoredToken()
      onLogout()
    }
    authEvents.addEventListener('unauthorized', onUnauthorized)
    return () => authEvents.removeEventListener('unauthorized', onUnauthorized)
  }, [onLogout])

  const logout = useCallback(async () => {
    await authApi.logout()
    clearStoredToken()
    onLogout()
  }, [onLogout])

  return (
    <AuthContext.Provider value={{ user, token, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
```

- [ ] **Step 2: Создать LoginForm.tsx**

```tsx
// src/frontend/src/auth/LoginForm.tsx
import { useState } from 'react'
import { Button, Card, Form, Input, Typography, App } from 'antd'
import { LockOutlined, SafetyCertificateOutlined, UserOutlined } from '@ant-design/icons'
import { authApi } from '../api/authApi'
import { setStoredToken } from '../api/client'

const { Title, Paragraph } = Typography

export default function LoginForm({ onAuthed }: { onAuthed: () => void }) {
  const { message } = App.useApp()
  const [loading, setLoading] = useState(false)

  const submit = async ({ login, password }: { login: string; password: string }) => {
    setLoading(true)
    try {
      const resp = await authApi.login(login, password)
      setStoredToken(resp.accessToken)
      message.success('Добро пожаловать!')
      onAuthed()
    } catch (err: any) {
      message.error(err?.message ?? 'Ошибка входа')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', padding: 24 }}>
      <Card className="sp-glass sp-fade-in" style={{ width: 420, maxWidth: '100%' }} variant="borderless">
        <div style={{ textAlign: 'center', marginBottom: 12 }}>
          <SafetyCertificateOutlined style={{ fontSize: 42, color: '#4f7cff' }} />
          <Title level={3} style={{ marginTop: 12, marginBottom: 0 }}>WinAdmin</Title>
          <Paragraph type="secondary" style={{ marginTop: 6 }}>
            Управление и мониторинг Windows-машины
          </Paragraph>
        </div>
        <Form layout="vertical" onFinish={submit}>
          <Form.Item
            name="login"
            label="Логин"
            rules={[{ required: true, message: 'Введите логин' }]}
          >
            <Input prefix={<UserOutlined />} placeholder="admin" size="large" autoFocus />
          </Form.Item>
          <Form.Item
            name="password"
            label="Пароль"
            rules={[{ required: true, message: 'Введите пароль' }]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder="••••••••" size="large" />
          </Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={loading}>
            Войти
          </Button>
        </Form>
      </Card>
    </div>
  )
}
```

- [ ] **Step 3: Заменить App.tsx**

```tsx
// src/frontend/src/App.tsx
import { useState } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import { api } from './api/client'
import { useEffect } from 'react'
import LoginForm from './auth/LoginForm'
import { AuthProvider } from './auth/AuthProvider'
import AppLayout from './components/AppLayout'
import Dashboard from './pages/Dashboard'
import Disks from './pages/Disks'
import Services from './pages/Services'
import Processes from './pages/Processes'
import Printers from './pages/Printers'
import Power from './pages/Power'
import ApiKeys from './pages/ApiKeys'
import AuditLog from './pages/AuditLog'
import ApiDocs from './pages/ApiDocs'
import Users from './pages/Users'
import { getStoredToken } from './api/client'

export default function App() {
  const [authed, setAuthed] = useState(() => Boolean(getStoredToken()))
  const [machine, setMachine] = useState<string>()

  useEffect(() => {
    if (!authed) return
    api.system().then((s) => setMachine(s.hostname)).catch(() => undefined)
  }, [authed])

  const handleLogout = () => {
    setAuthed(false)
    setMachine(undefined)
  }

  if (!authed) return <LoginForm onAuthed={() => setAuthed(true)} />

  return (
    <AuthProvider onLogout={handleLogout}>
      <Routes>
        <Route element={<AppLayout machine={machine} onLogout={handleLogout} />}>
          <Route path="/" element={<Dashboard />} />
          <Route path="/disks" element={<Disks />} />
          <Route path="/services" element={<Services />} />
          <Route path="/processes" element={<Processes />} />
          <Route path="/printers" element={<Printers />} />
          <Route path="/power" element={<Power />} />
          <Route path="/cp/apikeys" element={<ApiKeys />} />
          <Route path="/cp/audit" element={<AuditLog />} />
          <Route path="/cp/users" element={<Users />} />
          <Route path="/docs" element={<ApiDocs />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </AuthProvider>
  )
}
```

- [ ] **Step 4: Обновить AppLayout.tsx — logout + Users menu item**

В `src/frontend/src/components/AppLayout.tsx` изменить:

1. Добавить импорт `TeamOutlined` и `useAuth`:
```tsx
import { TeamOutlined, ... } from '@ant-design/icons'
import { useAuth } from '../auth/AuthProvider'
```

2. Внутри компонента `AppLayout` добавить:
```tsx
const { user, logout } = useAuth()
const isAdmin = user?.scopes.includes('admin') ?? false
```

3. Изменить массив `items` на вычисляемый:
```tsx
const items = [
  { key: '/', icon: <DashboardOutlined />, label: 'Дашборд' },
  { key: '/disks', icon: <HddOutlined />, label: 'Диски' },
  { key: '/services', icon: <ApiOutlined />, label: 'Службы' },
  { key: '/processes', icon: <AppstoreOutlined />, label: 'Процессы' },
  { key: '/printers', icon: <PrinterOutlined />, label: 'Принтеры' },
  { key: '/power', icon: <PoweroffOutlined />, label: 'Питание' },
  { type: 'divider' as const },
  ...(isAdmin ? [{ key: '/cp/users', icon: <TeamOutlined />, label: 'Пользователи' }] : []),
  { key: '/cp/apikeys', icon: <KeyOutlined />, label: 'API-ключи' },
  { key: '/cp/audit', icon: <FileSearchOutlined />, label: 'Аудит' },
  { key: '/docs', icon: <BookOutlined />, label: 'API-документация' },
]
```

4. Обновить кнопку logout — использовать `logout` из `useAuth()`:
```tsx
<Button type="text" icon={<LogoutOutlined />} onClick={logout}>
  Выйти
</Button>
```

- [ ] **Step 5: Проверить TypeScript**

```bash
cd src/frontend && npx tsc -p tsconfig.app.json --noEmit
```

Ожидаемый вывод: пусто (нет ошибок)

---

## Task 10: Frontend — Users Page

**Files:**
- Create: `src/frontend/src/pages/Users.tsx`

**Interfaces:**
- Consumes: `api.users()`, `api.createUser()`, `api.updateUserScopes()`, `api.changeUserPassword()`, `api.setUserActive()`, `api.deleteUser()`, `UserDto`, `CreateUserRequest`

- [ ] **Step 1: Создать Users.tsx**

```tsx
// src/frontend/src/pages/Users.tsx
import { useEffect, useState, useCallback } from 'react'
import { Button, Modal, Form, Input, Select, Space, Tag, Typography, App, Popconfirm } from 'antd'
import { PlusOutlined, EditOutlined, DeleteOutlined, StopOutlined, PlayCircleOutlined } from '@ant-design/icons'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef } from 'ag-grid-community'
import { api } from '../api/client'
import type { UserDto } from '../api/types'

const { Title } = Typography

const ALL_SCOPES = [
  'system.read', 'disks.read',
  'services.read', 'services.manage',
  'processes.read', 'processes.manage',
  'printers.read', 'printers.manage',
  'power.manage', 'admin',
]

export default function Users() {
  const { message } = App.useApp()
  const [users, setUsers] = useState<UserDto[]>([])
  const [loading, setLoading] = useState(true)

  const [createOpen, setCreateOpen] = useState(false)
  const [scopesOpen, setScopesOpen] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [selected, setSelected] = useState<UserDto | null>(null)

  const [createForm] = Form.useForm()
  const [scopesForm] = Form.useForm()
  const [passwordForm] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try { setUsers(await api.users()) } catch { message.error('Ошибка загрузки') } finally { setLoading(false) }
  }, [message])

  useEffect(() => { load() }, [load])

  const handleCreate = async (values: { login: string; password: string; scopes: string[] }) => {
    try {
      await api.createUser(values)
      message.success('Пользователь создан')
      setCreateOpen(false)
      createForm.resetFields()
      load()
    } catch (e: any) { message.error(e?.response?.data?.message ?? 'Ошибка') }
  }

  const handleScopes = async (values: { scopes: string[] }) => {
    if (!selected) return
    await api.updateUserScopes(selected.id, values.scopes)
    message.success('Scopes обновлены')
    setScopesOpen(false)
    load()
  }

  const handlePassword = async (values: { newPassword: string }) => {
    if (!selected) return
    await api.changeUserPassword(selected.id, values.newPassword)
    message.success('Пароль изменён')
    setPasswordOpen(false)
    passwordForm.resetFields()
  }

  const handleToggleActive = async (user: UserDto) => {
    await api.setUserActive(user.id, !user.isActive)
    message.success(user.isActive ? 'Пользователь деактивирован' : 'Пользователь активирован')
    load()
  }

  const handleDelete = async (user: UserDto) => {
    await api.deleteUser(user.id)
    message.success('Пользователь удалён')
    load()
  }

  const cols: ColDef<UserDto>[] = [
    { field: 'login', headerName: 'Логин', flex: 1 },
    {
      field: 'scopes', headerName: 'Scopes', flex: 2,
      cellRenderer: ({ value }: { value: string[] }) => (
        <Space wrap size={4}>
          {value.map((s) => <Tag key={s} color={s === 'admin' ? 'red' : 'blue'}>{s}</Tag>)}
        </Space>
      ),
    },
    {
      field: 'createdAt', headerName: 'Создан', width: 130,
      valueFormatter: ({ value }) => new Date(value).toLocaleDateString('ru'),
    },
    {
      field: 'isActive', headerName: 'Статус', width: 100,
      cellRenderer: ({ value }: { value: boolean }) =>
        <Tag color={value ? 'green' : 'default'}>{value ? 'активен' : 'выкл'}</Tag>,
    },
    {
      headerName: 'Действия', width: 200, sortable: false,
      cellRenderer: ({ data }: { data: UserDto }) => (
        <Space>
          <Button size="small" icon={<EditOutlined />} onClick={() => {
            setSelected(data)
            scopesForm.setFieldsValue({ scopes: data.scopes })
            setScopesOpen(true)
          }}>Scopes</Button>
          <Button size="small" icon={<EditOutlined />} onClick={() => {
            setSelected(data)
            setPasswordOpen(true)
          }}>Пароль</Button>
          <Button size="small"
            icon={data.isActive ? <StopOutlined /> : <PlayCircleOutlined />}
            onClick={() => handleToggleActive(data)}
          />
          <Popconfirm title="Удалить пользователя?" onConfirm={() => handleDelete(data)} okText="Да" cancelText="Нет">
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Пользователи</Title>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
          Создать
        </Button>
      </div>

      <div className="ag-theme-alpine-dark" style={{ height: 500 }}>
        <AgGridReact rowData={users} columnDefs={cols} loading={loading} rowHeight={48} />
      </div>

      {/* Создать */}
      <Modal title="Создать пользователя" open={createOpen} onCancel={() => setCreateOpen(false)} footer={null}>
        <Form form={createForm} layout="vertical" onFinish={handleCreate}>
          <Form.Item name="login" label="Логин" rules={[{ required: true }]}>
            <Input placeholder="admin" />
          </Form.Item>
          <Form.Item name="password" label="Пароль" rules={[{ required: true, min: 6 }]}>
            <Input.Password />
          </Form.Item>
          <Form.Item name="scopes" label="Scopes" initialValue={[]}>
            <Select mode="multiple" options={ALL_SCOPES.map((s) => ({ value: s, label: s }))} placeholder="Выберите права" />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Создать</Button>
        </Form>
      </Modal>

      {/* Scopes */}
      <Modal title={`Scopes — ${selected?.login}`} open={scopesOpen} onCancel={() => setScopesOpen(false)} footer={null}>
        <Form form={scopesForm} layout="vertical" onFinish={handleScopes}>
          <Form.Item name="scopes" label="Scopes">
            <Select mode="multiple" options={ALL_SCOPES.map((s) => ({ value: s, label: s }))} />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Сохранить</Button>
        </Form>
      </Modal>

      {/* Password */}
      <Modal title={`Пароль — ${selected?.login}`} open={passwordOpen} onCancel={() => setPasswordOpen(false)} footer={null}>
        <Form form={passwordForm} layout="vertical" onFinish={handlePassword}>
          <Form.Item name="newPassword" label="Новый пароль" rules={[{ required: true, min: 6 }]}>
            <Input.Password />
          </Form.Item>
          <Button type="primary" htmlType="submit" block>Сохранить</Button>
        </Form>
      </Modal>
    </div>
  )
}
```

- [ ] **Step 2: Проверить TypeScript**

```bash
cd src/frontend && npx tsc -p tsconfig.app.json --noEmit
```

Ожидаемый вывод: пусто (нет ошибок)

- [ ] **Step 3: Финальная проверка — запустить всё и протестировать**

```bash
# Терминал 1: создать тестового пользователя через CLI
dotnet run --project src/backend/WinAdmin.Api -- user add --login admin --password "Admin1!" --scopes admin

# Терминал 2: запустить backend
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/backend/WinAdmin.Api --urls http://localhost:5099

# Терминал 3: запустить frontend
npm --prefix src/frontend run dev
```

Открыть http://localhost:5188 — должна показаться форма входа с полями Логин + Пароль.
Войти: login=`admin`, password=`Admin1!`
Проверить: в меню сайдбара появился пункт **Пользователи**.
Перейти на /cp/users — таблица с пользователем admin.

- [ ] **Step 4: Запустить все тесты**

```bash
dotnet test WinAdmin.slnx
```

Ожидаемый вывод: `Пройден! : не пройдено 0, пройдено 17`
