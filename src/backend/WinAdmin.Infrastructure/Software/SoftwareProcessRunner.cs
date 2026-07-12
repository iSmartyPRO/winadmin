using System.Diagnostics;

namespace WinAdmin.Infrastructure.Software;

public sealed class SoftwareProcessRunner : ISoftwareProcessRunner
{
    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    public async Task RemoveStorePackageAsync(string packageFullName, CancellationToken ct)
    {
        var packageManagerType = Type.GetType("Windows.Management.Deployment.PackageManager, Microsoft.Windows.SDK.NET")
            ?? Type.GetType("Windows.Management.Deployment.PackageManager, Windows")
            ?? Type.GetType("Windows.Management.Deployment.PackageManager");

        if (packageManagerType == null)
            throw new InvalidOperationException("AppX PackageManager API is unavailable.");

        var packageManager = Activator.CreateInstance(packageManagerType)
            ?? throw new InvalidOperationException("Unable to create AppX PackageManager.");
        var removePackage = packageManagerType.GetMethod("RemovePackageAsync", new[] { typeof(string) })
            ?? throw new InvalidOperationException("AppX PackageManager.RemovePackageAsync is unavailable.");

        var operation = removePackage.Invoke(packageManager, new object[] { packageFullName })
            ?? throw new InvalidOperationException("AppX package removal did not return an operation.");

        await WaitForAsyncOperation(operation, ct);
    }

    private static async Task WaitForAsyncOperation(object operation, CancellationToken ct)
    {
        var type = operation.GetType();
        var statusProperty = type.GetProperty("Status");
        var errorCodeProperty = type.GetProperty("ErrorCode");

        if (statusProperty == null)
            return;

        while (Convert.ToInt32(statusProperty.GetValue(operation)) == 0)
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

        var status = Convert.ToInt32(statusProperty.GetValue(operation));
        if (status == 3 && errorCodeProperty?.GetValue(operation) is Exception ex)
            throw ex;
        if (status == 2)
            throw new OperationCanceledException("AppX package removal was canceled.", ct);
    }
}
