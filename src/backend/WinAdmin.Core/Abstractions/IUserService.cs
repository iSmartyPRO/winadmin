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
