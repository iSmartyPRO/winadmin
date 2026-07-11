using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Power;

/// <summary>
/// Перезагрузка / выключение машины через InitiateSystemShutdownEx,
/// предварительно включая привилегию SeShutdownPrivilege для текущего процесса.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerService : IPowerService
{
    private readonly ILogger<PowerService> _logger;

    public PowerService(ILogger<PowerService> logger) => _logger = logger;

    public OperationResult Reboot(PowerRequest request) => Execute(request, reboot: true);
    public OperationResult Shutdown(PowerRequest request) => Execute(request, reboot: false);

    private OperationResult Execute(PowerRequest request, bool reboot)
    {
        try
        {
            EnableShutdownPrivilege();
            string verb = reboot ? "перезагрузка" : "выключение";
            string message = request.Comment ?? $"WinAdmin: запланирована {verb} системы.";

            bool ok = NativeMethods.InitiateSystemShutdownEx(
                lpMachineName: null,
                lpMessage: message,
                dwTimeout: (uint)Math.Max(0, request.DelaySeconds),
                bForceAppsClosed: request.Force,
                bRebootAfterShutdown: reboot,
                dwReason: NativeMethods.SHTDN_REASON_MAJOR_OTHER | NativeMethods.SHTDN_REASON_MINOR_OTHER | NativeMethods.SHTDN_REASON_FLAG_PLANNED);

            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return OperationResult.Ok($"Запланирована {verb} через {request.DelaySeconds} c.");
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Сбой действия питания (reboot={Reboot})", reboot);
            return OperationResult.Fail($"Не удалось выполнить действие: {ex.Message} (код {ex.NativeErrorCode}). " +
                                     "Проверьте, что пул приложения работает под учётной записью с правами администратора.");
        }
    }

    public OperationResult CancelPending()
    {
        try
        {
            EnableShutdownPrivilege();
            if (!NativeMethods.AbortSystemShutdown(null))
            {
                int err = Marshal.GetLastWin32Error();
                // 1116 = ERROR_NO_SHUTDOWN_IN_PROGRESS
                if (err == 1116)
                    return OperationResult.Fail("Нет запланированных действий питания для отмены");
                throw new Win32Exception(err);
            }
            return OperationResult.Ok("Запланированное действие питания отменено");
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Не удалось отменить действие питания");
            return OperationResult.Fail($"Не удалось отменить: {ex.Message}");
        }
    }

    private static void EnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out IntPtr token))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, NativeMethods.SE_SHUTDOWN_NAME, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
            };

            if (!NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            int last = Marshal.GetLastWin32Error();
            if (last == NativeMethods.ERROR_NOT_ALL_ASSIGNED)
                throw new Win32Exception(last, "Привилегия SeShutdownPrivilege недоступна для текущей учётной записи");
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static class NativeMethods
    {
        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const uint TOKEN_QUERY = 0x0008;
        public const int ERROR_NOT_ALL_ASSIGNED = 1300;
        public const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

        public const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
        public const uint SHTDN_REASON_MINOR_OTHER = 0x00000000;
        public const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitiateSystemShutdownEx(string? lpMachineName, string? lpMessage,
            uint dwTimeout, [MarshalAs(UnmanagedType.Bool)] bool bForceAppsClosed,
            [MarshalAs(UnmanagedType.Bool)] bool bRebootAfterShutdown, uint dwReason);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AbortSystemShutdown(string? lpMachineName);
    }
}
