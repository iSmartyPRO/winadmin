using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Tests.Fakes;

public sealed class FakeAdReader : IAdReader
{
    public List<AdProject> Projects { get; } = [];
    public HashSet<string> ExistingDns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public WriterStatus Writer { get; set; } = new("PCS\\svc", true, null, true, false, null, true);
    public Dictionary<string, EffectiveRights> Effective { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Down { get; set; }

    private void ThrowIfDown()
    {
        if (Down) throw new DirectoryUnavailableException("Контроллер домена недоступен");
    }

    public Task<IReadOnlyList<AdProject>> ListProjectsAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<AdProject>>(Projects.ToList());
    }

    public Task<bool> ExistsAsync(string dn, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(ExistingDns.Contains(dn));
    }

    public Task<EffectiveRights> ReadEffectiveAsync(string dn, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(Effective.TryGetValue(dn, out var r) ? r
            : new EffectiveRights(new HashSet<string>(), new HashSet<string>()));
    }

    public Task<WriterStatus> GetWriterStatusAsync(CancellationToken ct = default) => Task.FromResult(Writer);
}
