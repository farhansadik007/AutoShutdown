using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoShutdown.Models;

namespace AutoShutdown.Core;

public readonly record struct PowerResult(bool Success, string? Error);

/// <summary>
/// The entire "hard" part of the product: four Win32 calls. Windows does the
/// shutting down; we only ask for it.
/// </summary>
public static class PowerService
{
    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_POWEROFF = 0x00000008;
    private const uint EWX_FORCEIFHUNG = 0x00000010;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privilege;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true, EntryPoint = "SetSuspendState")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendStateNative(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    public static PowerResult Execute(PowerActionOption action)
    {
        try
        {
            return action switch
            {
                PowerActionOption.Nothing => new PowerResult(true, null),
                PowerActionOption.Sleep => Suspend(false),
                PowerActionOption.Hibernate => Suspend(true),
                PowerActionOption.Shutdown => ExitWindows(EWX_SHUTDOWN | EWX_POWEROFF),
                PowerActionOption.Restart => ExitWindows(EWX_REBOOT),
                PowerActionOption.Lock => Lock(),
                _ => new PowerResult(false, "Unknown power action.")
            };
        }
        catch (Exception ex)
        {
            Log.Write("Power action failed: " + ex);
            return new PowerResult(false, ex.Message);
        }
    }

    private static PowerResult Suspend(bool hibernate)
    {
        Log.Write(hibernate ? "Requesting hibernate" : "Requesting sleep");
        EnableShutdownPrivilege();

        if (!SetSuspendStateNative(hibernate, false, false))
        {
            return new PowerResult(
                false,
                hibernate
                    ? "Windows rejected the hibernate request (is hibernation enabled?)."
                    : "Windows rejected the sleep request.");
        }

        Log.Write("Machine resumed");
        return new PowerResult(true, null);
    }

    private static PowerResult Lock()
    {
        Log.Write("Requesting workstation lock");

        if (!LockWorkStation())
        {
            return new PowerResult(false, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        return new PowerResult(true, null);
    }

    private static PowerResult ExitWindows(uint flags)
    {
        Log.Write($"Requesting exit (flags 0x{flags:X})");

        // EWX_FORCEIFHUNG rather than EWX_FORCE: hung apps still get killed,
        // but healthy ones get the chance to save.
        EnableShutdownPrivilege();
        if (!ExitWindowsEx(flags | EWX_FORCEIFHUNG, SHTDN_REASON_FLAG_PLANNED))
        {
            return new PowerResult(false, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        return new PowerResult(true, null);
    }

    /// <summary>
    /// Grants the process SeShutdownPrivilege. Normal users already hold it, so
    /// this never triggers a UAC prompt.
    /// </summary>
    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out var luid))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privilege = new LuidAndAttributes { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
            };

            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (Marshal.GetLastWin32Error() == ERROR_NOT_ALL_ASSIGNED)
            {
                throw new Win32Exception(ERROR_NOT_ALL_ASSIGNED, "SeShutdownPrivilege could not be assigned.");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
