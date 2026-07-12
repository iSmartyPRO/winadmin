namespace WinAdmin.Infrastructure.Software;

public interface ISoftwareProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, CancellationToken ct);
    Task RemoveStorePackageAsync(string packageFullName, CancellationToken ct);
}
