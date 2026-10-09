using System.Collections.Concurrent;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>
/// Группы пользователя AD для проверки прав на каждом запросе. Свежие (до 5 минут) — из памяти;
/// старше — перечитываются; домен недоступен — последние известные, пока им меньше 15 минут.
/// Учётка отключена/удалена → null (сеанс недействителен).
/// </summary>
public sealed class AdGroupCache(IDirectoryService directory, TimeProvider? time = null) : IAdGroupCache
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StaleLimit = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<string> Groups)> _entries = new();

    public async Task<IReadOnlyList<string>?> GetGroupsAsync(string userSid, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        bool cached = _entries.TryGetValue(userSid, out var entry);
        if (cached && now - entry.At < RefreshAfter)
            return entry.Groups;

        try
        {
            var account = await directory.FindBySidAsync(userSid, ct);
            if (account is not { Kind: DirectoryObjectKind.User, Enabled: true })
            {
                _entries.TryRemove(userSid, out _);
                return null;
            }
            var groups = await directory.GetTokenGroupsAsync(userSid, ct);
            _entries[userSid] = (now, groups);
            return groups;
        }
        catch (DirectoryUnavailableException)
        {
            return cached && now - entry.At < StaleLimit ? entry.Groups : null;
        }
    }
}
