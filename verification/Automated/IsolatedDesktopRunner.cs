using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HenLib.WinFormsTheming.Verification;

internal static class IsolatedDesktopRunner
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--skip-package")
            {
                throw new ArgumentException("Expected the verification script path and optional --skip-package.", nameof(args));
            }
            string script = Path.GetFullPath(args[0]);
            if (!File.Exists(script))
            {
                throw new FileNotFoundException("Verification script not found.", script);
            }
            string desktopName = "HenLib-Theming-" + Guid.NewGuid().ToString("N");
            using var desktop = CreateDesktop(desktopName, 0, 0, 0, 0x10000000, 0);
            if (desktop.IsInvalid)
            {
                throw NativeError("CreateDesktop");
            }
            using var input = DuplicateStandardHandle(-10);
            using var output = DuplicateStandardHandle(-11);
            using var error = DuplicateStandardHandle(-12);
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = desktopName,
                Flags = 0x100 | 0x80, // Use inherited streams and suppress the startup busy cursor.
                StandardInput = input.DangerousGetHandle(),
                StandardOutput = output.DangerousGetHandle(),
                StandardError = error.DangerousGetHandle()
            };
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var command = new StringBuilder($"\"{powershell}\" -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\" -IsolatedChild");
            if (args.Length == 2)
            {
                command.Append(" -SkipPackage");
            }
            Console.WriteLine("Running verification on an isolated desktop; test windows will not appear on your desktop.");
            // CreateProcess assigns only the child to this desktop. Never switch the input desktop.
            if (!CreateProcess(powershell, command, 0, 0, true, 0x08000000, 0,
                Path.GetDirectoryName(script), ref startup, out ProcessInformation information))
            {
                throw NativeError("CreateProcess for isolated verification");
            }
            using var processHandle = new SafeFileHandle(information.Process, true);
            using var threadHandle = new SafeFileHandle(information.Thread, true);
            using var process = Process.GetProcessById((int)information.ProcessId);
            try
            {
                if (!process.WaitForExit(600000))
                {
                    throw new TimeoutException("Isolated verification exceeded ten minutes.");
                }
                if (!GetExitCodeProcess(processHandle, out uint exitCode))
                {
                    throw NativeError("GetExitCodeProcess for isolated verification");
                }
                return unchecked((int)exitCode);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Isolated verification failed; it will not fall back to visible windows: {exception.ToString()}");
            return 1;
        }
    }

    private static SafeFileHandle DuplicateStandardHandle(int kind)
    {
        nint current = GetCurrentProcess();
        if (!DuplicateHandle(current, GetStdHandle(kind), current, out nint duplicate, 0, true, 2))
        {
            throw NativeError("DuplicateHandle for verification stream " + kind);
        }
        return new SafeFileHandle(duplicate, true);
    }

    private static Win32Exception NativeError(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, operation + " failed (Win32 " + error + ").");
    }

    private sealed class DesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DesktopHandle() : base(true)
        {
        }

        protected override bool ReleaseHandle() => CloseDesktop(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize;
        public nint ReservedPointer, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern DesktopHandle CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int kind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(nint sourceProcess, nint source, nint targetProcess,
        out nint target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint exitCode);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, nint processSecurity,
        nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment,
        string? directory, ref StartupInfo startup, out ProcessInformation information);
}
