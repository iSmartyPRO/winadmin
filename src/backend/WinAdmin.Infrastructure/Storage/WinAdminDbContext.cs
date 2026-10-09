using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>
/// База WinAdmin. Модель общая; у каждого провайдера свой наследник со своими миграциями
/// (<see cref="SqliteWinAdminDbContext"/>, <see cref="PostgresWinAdminDbContext"/>).
/// </summary>
public class WinAdminDbContext : DbContext
{
    public WinAdminDbContext(DbContextOptions<WinAdminDbContext> options) : base(options) { }

    protected WinAdminDbContext(DbContextOptions options) : base(options) { }

    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<ExcludedUserEntity> ExcludedUsers => Set<ExcludedUserEntity>();
    public DbSet<ModuleStateEntity> ModuleStates => Set<ModuleStateEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<RoleAssignmentEntity> RoleAssignments => Set<RoleAssignmentEntity>();

    // SQLite не умеет ORDER BY по DateTimeOffset — храним как UTC-тики (long),
    // что сортируемо и сохраняет момент времени.
    private static readonly ValueConverter<DateTimeOffset, long> DtoConverter = new(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableDtoConverter = new(
        v => v.HasValue ? v.Value.UtcTicks : null,
        v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.KeyHash).IsRequired();
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.Scopes).IsRequired();
            e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
            e.Property(x => x.ExpiresAt).HasConversion(NullableDtoConverter);
            e.Property(x => x.LastUsedAt).HasConversion(NullableDtoConverter);
        });

        modelBuilder.Entity<AuditEntryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Actor).IsRequired();
            e.Property(x => x.Action).IsRequired();
            e.Property(x => x.Timestamp).HasConversion(DtoConverter);
            e.HasIndex(x => x.Timestamp);
        });

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

        modelBuilder.Entity<ExcludedUserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).IsRequired().HasMaxLength(256);
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.CreatedAt).HasConversion(DtoConverter);
        });

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
    }
}

/// <summary>API-ключ. Хранится только хеш секрета.</summary>
public sealed class ApiKeyEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string KeyHash { get; set; } = "";

    /// <summary>Последние 4 символа сырого ключа — для опознания.</summary>
    public string? Hint { get; set; }

    /// <summary>Scopes через запятую.</summary>
    public string Scopes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
}

/// <summary>Запись аудита управляющих действий.</summary>
public sealed class AuditEntryEntity
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public bool Success { get; set; }
    public string? Details { get; set; }
    public string? SourceIp { get; set; }
}

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

/// <summary>Учётная запись, скрываемая из журналов событий (глобальный чёрный список настроек).</summary>
public sealed class ExcludedUserEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

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
