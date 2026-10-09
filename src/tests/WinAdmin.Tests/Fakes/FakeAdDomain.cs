using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;

namespace WinAdmin.Tests.Fakes;

/// <summary>
/// Домен в памяти: каталог (IAdUserDirectory) и запись (IAdWriter) над одним состоянием.
/// Основная группа моделируется как в AD: смена primaryGroupID делает прежнюю основную группу обычным членством.
/// </summary>
public sealed class FakeAdDomain : IAdUserDirectory, IAdWriter
{
    public const string DomainSid = "S-1-5-21-7-8-9";
    private int _rid = 2000;

    public sealed class User
    {
        public required string Sam { get; init; }
        public required string Dn { get; set; }
        public required string Sid { get; init; }
        public bool Enabled { get; set; } = true;
        public int PrimaryGroupId { get; set; } = 513;
        public Dictionary<string, string?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[]? Photo { get; set; }
        public string? Password { get; set; }
        public bool MustChange { get; set; }
    }

    public sealed class Group
    {
        public required string Name { get; init; }
        public required string Dn { get; init; }
        public required string Sid { get; init; }
        public HashSet<string> Members { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public List<User> Users { get; } = [];
    public List<Group> Groups { get; } = [];
    public List<string> Calls { get; } = [];
    /// <summary>Операция (например «RemoveMember:CN=g,…») → исключение при вызове.</summary>
    public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExistingOus { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> WriterSids { get; } = ["S-1-1-0", "S-1-5-11", DomainSid + "-1500"];
    public Dictionary<string, byte[]> SecurityDescriptors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeAdDomain() => AddGroup("Пользователи домена", "CN=Users,DC=test,DC=local", 513);

    public Group DomainUsers => Groups.Single(g => g.Sid.EndsWith("-513"));

    public Group AddGroup(string name, string ouDn, int? rid = null)
    {
        var g = new Group { Name = name, Dn = $"CN={name},{ouDn}", Sid = $"{DomainSid}-{rid ?? ++_rid}" };
        Groups.Add(g);
        return g;
    }

    public User AddUser(string sam, string ouDn, params string[] groupDns)
    {
        var u = new User { Sam = sam, Dn = $"CN={sam},{ouDn}", Sid = $"{DomainSid}-{++_rid}" };
        u.Attributes["displayName"] = sam.ToUpperInvariant();
        Users.Add(u);
        foreach (var dn in groupDns) Groups.Single(g => g.Dn == dn).Members.Add(u.Dn);
        return u;
    }

    public User U(string sam) => Users.Single(u => u.Sam == sam);

    private void Call(string op, string target)
    {
        string key = $"{op}:{target}";
        Calls.Add(key);
        if (FailOn.TryGetValue(key, out var ex)) throw ex;
    }

    private AdUser ToAd(User u, IReadOnlyCollection<string> attributes)
    {
        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes) attrs[a] = u.Attributes.GetValueOrDefault(a);
        return new AdUser(u.Sam, u.Dn, u.Sid, u.Attributes.GetValueOrDefault("displayName"), u.Enabled, null, null,
            u.Photo is not null, u.PrimaryGroupId, attrs);
    }

    // ── IAdUserDirectory ─────────────────────────────────────────
    public Task<IReadOnlyList<AdUser>> ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AdUser>>(Users.Where(u => DnUtils.IsUnderOrSame(u.Dn, baseDn)).Select(u => ToAd(u, attributes)).ToList());

    public Task<AdUser?> FindUserAsync(string sam, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Sam.Equals(sam, StringComparison.OrdinalIgnoreCase)) is { } u ? ToAd(u, attributes) : null);

    public Task<IReadOnlyList<AdGroupRef>> GetGroupsAsync(AdUser user, CancellationToken ct = default)
    {
        var groups = Groups.Where(g => g.Members.Contains(user.Dn)).Select(g => new AdGroupRef(g.Dn, g.Name, g.Sid, false)).ToList();
        var primary = Groups.FirstOrDefault(g => g.Sid == $"{DomainSid}-{user.PrimaryGroupId}");
        if (primary is not null) groups.Insert(0, new AdGroupRef(primary.Dn, primary.Name, primary.Sid, true));
        return Task.FromResult<IReadOnlyList<AdGroupRef>>(groups);
    }

    public Task<byte[]?> GetPhotoAsync(string dn, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Dn == dn)?.Photo);

    public Task<AdGroupRef?> FindGroupAsync(string nameOrDn, CancellationToken ct = default)
        => Task.FromResult(Groups.FirstOrDefault(g => g.Dn.Equals(nameOrDn, StringComparison.OrdinalIgnoreCase)
                                                     || g.Name.Equals(nameOrDn, StringComparison.OrdinalIgnoreCase))
            is { } g ? new AdGroupRef(g.Dn, g.Name, g.Sid, false) : null);

    public Task<AdGroupRef> GetDomainUsersGroupAsync(CancellationToken ct = default)
        => Task.FromResult(new AdGroupRef(DomainUsers.Dn, DomainUsers.Name, DomainUsers.Sid, false));

    public Task<string?> FindSampleUserAsync(string ouDn, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => DnUtils.IsUnderOrSame(u.Dn, ouDn))?.Dn);

    public Task<IReadOnlyList<string>> GetWriterSidsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(WriterSids);

    public Task<byte[]?> ReadSecurityDescriptorAsync(string dn, CancellationToken ct = default)
        => Task.FromResult(SecurityDescriptors.GetValueOrDefault(dn));

    // ── IAdWriter ────────────────────────────────────────────────
    private User ByDn(string dn) => Users.FirstOrDefault(u => u.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase))
                                    ?? throw new AdWriteException(32, "Объект не найден в AD.");

    public Task ModifyAttributesAsync(string dn, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
    {
        Call("Modify", dn);
        var u = ByDn(dn);
        foreach (var (k, v) in changes) u.Attributes[k] = string.IsNullOrEmpty(v) ? null : v;
        return Task.CompletedTask;
    }

    public Task<bool> AddMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
    {
        Call("AddMember", groupDn);
        return Task.FromResult(Groups.Single(g => g.Dn == groupDn).Members.Add(memberDn));
    }

    public Task<bool> RemoveMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
    {
        Call("RemoveMember", groupDn);
        var g = Groups.Single(x => x.Dn == groupDn);
        var u = Users.FirstOrDefault(x => x.Dn == memberDn);
        if (u is not null && g.Sid == $"{DomainSid}-{u.PrimaryGroupId}")
            throw new AdWriteException(53, "AD отклонил операцию: нельзя удалить из основной группы.");
        return Task.FromResult(g.Members.Remove(memberDn));
    }

    public Task SetPrimaryGroupAsync(string userDn, string groupSid, CancellationToken ct = default)
    {
        Call("SetPrimary", userDn);
        var u = ByDn(userDn);
        var target = Groups.Single(g => g.Sid == groupSid);
        if (!target.Members.Contains(userDn)) throw new AdWriteException(53, "Пользователь не состоит в группе.");
        var old = Groups.FirstOrDefault(g => g.Sid == $"{DomainSid}-{u.PrimaryGroupId}");
        old?.Members.Add(userDn);           // прежняя основная — теперь обычное членство
        target.Members.Remove(userDn);      // основная группа не хранится в member
        u.PrimaryGroupId = int.Parse(groupSid[(groupSid.LastIndexOf('-') + 1)..]);
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(string userDn, bool enabled, CancellationToken ct = default)
    {
        Call(enabled ? "Enable" : "Disable", userDn);
        ByDn(userDn).Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task ResetPasswordAsync(string userDn, string password, bool mustChange, CancellationToken ct = default)
    {
        Call("ResetPassword", userDn);
        var u = ByDn(userDn);
        u.Password = password;
        u.MustChange = mustChange;
        return Task.CompletedTask;
    }

    public Task<string> MoveAsync(string dn, string targetOuDn, CancellationToken ct = default)
    {
        Call("Move", dn);
        var u = ByDn(dn);
        string newDn = $"{DnUtils.Split(dn)[0]},{targetOuDn}";
        foreach (var g in Groups.Where(g => g.Members.Remove(dn))) g.Members.Add(newDn);
        u.Dn = newDn;
        return Task.FromResult(newDn);
    }

    public Task<string> CreateGroupAsync(string ouDn, string cn, string description, CancellationToken ct = default)
    {
        Call("CreateGroup", ouDn);
        return Task.FromResult(AddGroup(cn, ouDn).Dn);
    }

    public Task SetPhotoAsync(string userDn, byte[]? photo, CancellationToken ct = default)
    {
        Call(photo is null ? "RemovePhoto" : "SetPhoto", userDn);
        ByDn(userDn).Photo = photo;
        return Task.CompletedTask;
    }
}
