using System.Collections.Concurrent;

namespace WinAdmin.Api.Auth;

/// <summary>Неудачные входы за последнюю минуту: 5 на пару (IP, логин), 20 на IP.</summary>
public sealed class LoginThrottle(TimeProvider? time = null)
{
    public const int PerPair = 5;
    public const int PerIp = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _failures = new();

    private static string Pair(string ip, string login) => $"{ip}|{login.Trim().ToLowerInvariant()}";

    public TimeSpan? RetryAfter(string ip, string login)
    {
        var now = _time.GetUtcNow();
        return Wait(Pair(ip, login), PerPair, now) ?? Wait(ip, PerIp, now);
    }

    public void Failed(string ip, string login)
    {
        var now = _time.GetUtcNow();
        foreach (var key in new[] { Pair(ip, login), ip })
        {
            var list = _failures.GetOrAdd(key, _ => []);
            lock (list) list.Add(now);
        }
    }

    public void Succeeded(string ip, string login) => _failures.TryRemove(Pair(ip, login), out _);

    private TimeSpan? Wait(string key, int limit, DateTimeOffset now)
    {
        if (!_failures.TryGetValue(key, out var list)) return null;
        lock (list)
        {
            list.RemoveAll(t => now - t >= Window);
            if (list.Count < limit) return null;
            var wait = list[^limit] + Window - now;
            return wait > TimeSpan.Zero ? wait : null;
        }
    }
}
