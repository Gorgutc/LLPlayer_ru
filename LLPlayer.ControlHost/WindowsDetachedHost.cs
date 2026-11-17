using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LLPlayer.ControlHost;

[SupportedOSPlatform("windows")]
internal static class WindowsDetachedHost
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateNewProcessGroup = 0x00000200;

    public static void Start(ProcessStartInfo startInfo)
    {
        var command = new StringBuilder(Quote(startInfo.FileName));
        foreach (var argument in startInfo.ArgumentList)
            command.Append(' ').Append(Quote(argument));
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        // Stdout can be an inheritable MCP protocol pipe in the launching process. The
        // durable host must keep running without any handles from the bridge.
        if (!CreateProcess(startInfo.FileName, command, 0, 0, false, CreateNoWindow | CreateNewProcessGroup,
                0, startInfo.WorkingDirectory, ref startup, out var child))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(child.Thread);
        CloseHandle(child.Process);
    }

    private static string Quote(string argument)
    {
        if (argument.Contains('"'))
            throw new ArgumentException("The detached host accepts flags and physical file paths.");
        var length = argument.Length - argument.TrimEnd('\\').Length;
        return '"' + argument + new string('\\', length) + '"';
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command,
        nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, nint environment, string directory, ref StartupInfo startup, out ProcessInformation child);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort ReservedBytes;
        public nint ReservedData;
        public nint Input;
        public nint Output;
        public nint Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
}
