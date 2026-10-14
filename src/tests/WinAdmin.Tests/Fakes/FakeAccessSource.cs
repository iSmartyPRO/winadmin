using WinAdmin.Core.Import;

namespace WinAdmin.Tests.Fakes;

/// <summary>Данные портала Access в памяти.</summary>
public sealed class FakeAccessSource : IAccessSource
{
    public string Description => "fake-access";
    public AccessAppSettings? AppSettings { get; set; }
    public List<string> ExcludedOus { get; } = [];
    public List<AccessTerminatedOu> TerminatedOus { get; } = [];
    public List<AccessRolePrincipal> RolePrincipals { get; } = [];
    public List<AccessPortalUser> PortalUsers { get; } = [];
    public List<AccessLocalUser> LocalUsers { get; } = [];
    public AccessExtras Extras { get; set; } = new(0, 0);
    public List<AccessAuditEntry> Audit { get; } = [];

    public Task<AccessAppSettings?> GetAppSettingsAsync(CancellationToken ct = default) => Task.FromResult(AppSettings);
    public Task<IReadOnlyList<string>> GetExcludedOusAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(ExcludedOus);
    public Task<IReadOnlyList<AccessTerminatedOu>> GetTerminatedOusAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccessTerminatedOu>>(TerminatedOus);
    public Task<IReadOnlyList<AccessRolePrincipal>> GetRolePrincipalsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccessRolePrincipal>>(RolePrincipals);
    public Task<IReadOnlyList<AccessPortalUser>> GetPortalUsersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccessPortalUser>>(PortalUsers);
    public Task<IReadOnlyList<AccessLocalUser>> GetLocalUsersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccessLocalUser>>(LocalUsers);
    public Task<AccessExtras> GetExtrasAsync(CancellationToken ct = default) => Task.FromResult(Extras);
    public Task<long> CountAuditAsync(long afterId, CancellationToken ct = default) => Task.FromResult((long)Audit.Count(a => a.Id > afterId));

    public Task<IReadOnlyList<AccessAuditEntry>> ReadAuditAsync(long afterId, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AccessAuditEntry>>(Audit.Where(a => a.Id > afterId).OrderBy(a => a.Id).Take(limit).ToList());
}
