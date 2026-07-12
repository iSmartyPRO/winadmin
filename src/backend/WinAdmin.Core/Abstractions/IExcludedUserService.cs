using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

/// <summary>
/// Управление глобальным чёрным списком учётных записей, которые не должны
/// показываться в журналах событий (настройка администратора).
/// </summary>
public interface IExcludedUserService
{
    Task<IReadOnlyList<ExcludedUserDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Только имена — для применения фильтра при запросе журналов.</summary>
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default);

    Task<ExcludedUserDto> AddAsync(string userName, CancellationToken ct = default);

    Task<bool> RemoveAsync(string id, CancellationToken ct = default);
}
