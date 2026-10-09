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

/// <summary>Модуль, которому нужно действие при первом включении (например, шаблоны ролей).</summary>
public interface IModuleLifecycle
{
    Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct);
}
