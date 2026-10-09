using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Tests.Fakes;

/// <summary>Каталог в памяти: пользователи по логину (sAMAccountName), группы, «падение» домена.</summary>
public sealed class FakeDirectory : IDirectoryService
{
    public Dictionary<string, (DirectoryObject User, string Password, List<string> Groups)> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DirectoryObject> Groups { get; } = [];
    public bool Down { get; set; }
    public int TokenGroupCalls { get; private set; }

    public DirectoryObject AddUser(string sam, string password, params string[] groupSids)
    {
        var user = new DirectoryObject($"S-1-5-21-10-20-30-{1000 + Users.Count}", DirectoryObjectKind.User, sam, sam.ToUpperInvariant(),
            $"{sam}@test.local", $"CN={sam},DC=test,DC=local", true);
        Users[sam] = (user, password, [.. groupSids]);
        return user;
    }

    public DirectoryObject AddGroup(string name)
    {
        var group = new DirectoryObject($"S-1-5-21-10-20-30-{5000 + Groups.Count}", DirectoryObjectKind.Group, name, name, null, $"CN={name},DC=test,DC=local", true);
        Groups.Add(group);
        return group;
    }

    public void Disable(string sam) => Users[sam] = Users[sam] with { User = Users[sam].User with { Enabled = false } };

    private void ThrowIfDown()
    {
        if (Down) throw new DirectoryUnavailableException("Контроллер домена недоступен");
    }

    public Task<DirectoryObject?> FindUserAsync(string login, CancellationToken ct = default)
    {
        ThrowIfDown();
        var (sam, _) = DirectoryLogin.Parse(login);
        return Task.FromResult(Users.TryGetValue(sam, out var u) ? u.User : null);
    }

    public Task<DirectoryObject?> FindBySidAsync(string sid, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(Users.Values.Select(u => u.User).Concat(Groups).FirstOrDefault(o => o.Sid == sid));
    }

    public Task<IReadOnlyList<string>> GetTokenGroupsAsync(string userSid, CancellationToken ct = default)
    {
        ThrowIfDown();
        TokenGroupCalls++;
        var user = Users.Values.FirstOrDefault(u => u.User.Sid == userSid);
        return Task.FromResult<IReadOnlyList<string>>(user.Groups ?? []);
    }

    public Task<IReadOnlyList<DirectoryObject>> SearchAsync(string query, DirectoryObjectKind? kind, int limit, CancellationToken ct = default)
    {
        ThrowIfDown();
        var all = Users.Values.Select(u => u.User).Concat(Groups)
            .Where(o => (kind is null || o.Kind == kind) && o.SamAccountName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<DirectoryObject>>(all);
    }

    public Task<bool> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password)) return Task.FromResult(false);
        ThrowIfDown();
        var (sam, _) = DirectoryLogin.Parse(login);
        return Task.FromResult(Users.TryGetValue(sam, out var u) && u.Password == password);
    }

    public Task<IReadOnlyList<DirectoryTestStep>> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DirectoryTestStep>>(Down
            ? [new("Подключение", false, "Контроллер домена недоступен")]
            : [new("Подключение", true, "ok")]);
}
