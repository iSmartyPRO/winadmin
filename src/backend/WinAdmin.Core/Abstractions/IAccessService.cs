using WinAdmin.Core.Security;

namespace WinAdmin.Core.Abstractions;

/// <summary>Итоговые права субъекта (с кэшем на 60 с).</summary>
public interface IAccessService
{
    Task<EffectivePermissions> GetAsync(PrincipalRef principal, CancellationToken ct = default);

    /// <summary>Сбросить кэш (после изменения ролей, назначений, модулей).</summary>
    void Invalidate();
}
