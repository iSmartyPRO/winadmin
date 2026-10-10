using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Доступ к шаре под служебной учёткой: LogonUser(NEW_CREDENTIALS) — сетевые обращения идут от её имени.</summary>
public static class Impersonation
{
    private const int Logon32LogonNewCredentials = 9;
    private const int Logon32ProviderWinnt50 = 3;

    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain, string password, int logonType, int provider, out SafeAccessTokenHandle token);

    public static T Run<T>(AdWriteCredential credential, string? domain, Func<T> action)
    {
        if (credential.Mode == AdWriteMode.ProcessAccount) return action();
        var network = AdCredentials.ToNetwork(credential, domain)!;
        if (!LogonUser(network.UserName, string.IsNullOrEmpty(network.Domain) ? null : network.Domain, network.Password,
                Logon32LogonNewCredentials, Logon32ProviderWinnt50, out var token))
            throw new UnauthorizedAccessException("Не удалось войти служебной учёткой для доступа к файловому серверу: "
                                                  + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        using (token)
            return WindowsIdentity.RunImpersonated(token, action);
    }
}
