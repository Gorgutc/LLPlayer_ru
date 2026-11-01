using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FlyleafLib.MediaPlayer.Dubbing;
using Microsoft.Win32.SafeHandles;

namespace FlyleafLib.Plugins;

public sealed partial class OnlineVideoProcess
{
    [SupportedOSPlatform("windows")]
    static unsafe OnlineVideoProcess StartWindows(ProcessStartInfo info)
    {
        // Process.Start cannot create a suspended child. Set up the pipes and restrict
        // inheritance to these three handles; unrelated player handles must stay private.
        using var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        AnonymousPipeServerStream error = null;
        SafeFileHandle job = null;
        Process root = null;
        try
        {
            error = new(PipeDirection.In, HandleInheritability.Inheritable);
            nint* handles = stackalloc nint[] { input.ClientSafePipeHandle.DangerousGetHandle(),
                output.ClientSafePipeHandle.DangerousGetHandle(), error.ClientSafePipeHandle.DangerousGetHandle() };
            nuint bytes = 0;
            InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
            var attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            ProcessInfo child;
            try
            {
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes))
                    throw new Win32Exception();
                try
                {
                    const int handleList = 0x20002;
                    if (!UpdateProcThreadAttribute(attributes, 0, handleList, (nint)handles,
                            (nuint)(3 * IntPtr.Size), 0, 0))
                        throw new Win32Exception();
                    StartupInfoEx startup = new()
                    {
                        Size = sizeof(StartupInfoEx), Flags = 0x100, // STARTF_USESTDHANDLES
                        Input = handles[0], Output = handles[1], Error = handles[2], Attributes = attributes
                    };
                    const uint flags = 0x00000004 | 0x00000400 | 0x00080000 | 0x08000000;
                    // CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW
                    string environment = string.Join('\0', info.Environment.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(k => k.Key + "=" + k.Value)) + "\0\0";
                    var command = new StringBuilder();
                    AppendArgument(command, info.FileName);
                    foreach (var argument in info.ArgumentList)
                    {
                        command.Append(' ');
                        AppendArgument(command, argument);
                    }
                    var mutableCommand = (command.ToString() + '\0').ToCharArray();
                    fixed (char* env = environment, cmd = mutableCommand)
                        if (!CreateProcessW(info.FileName, cmd, 0, 0, true, flags, env,
                                string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory,
                                ref startup, out child))
                            throw new Win32Exception();
                }
                finally { DeleteProcThreadAttributeList(attributes); }
            }
            finally { Marshal.FreeHGlobal(attributes); }

            using var nativeProcess = new SafeProcessHandle(child.Process, ownsHandle: true);
            using var thread = new SafeWaitHandle(child.Thread, ownsHandle: true);
            try
            {
                job = JobObjectGuard.AssignKillOnClose(child.Process) ??
                    throw new IOException("Could not contain the extractor in a Windows job.");
                root = Process.GetProcessById(child.Pid);
                _ = root.Handle; // Retain a handle for ExitCode, even if this child exits immediately.
                input.DisposeLocalCopyOfClientHandle();
                output.DisposeLocalCopyOfClientHandle();
                error.DisposeLocalCopyOfClientHandle();
                if (ResumeThread(thread) == uint.MaxValue)
                    throw new Win32Exception();
                return new(root, output, error, job);
            }
            catch
            {
                TerminateProcess(nativeProcess, 1); // Also covers a rejected job assignment before resume.
                throw;
            }
        }
        catch
        {
            job?.Dispose();
            root?.Dispose();
            output.Dispose();
            error?.Dispose();
            throw;
        }
    }

    // Windows CreateProcessW accepts one command line. Encode each ArgumentList element
    // once using the CRT backslash/quote rules; no shell interprets this string.
    static void AppendArgument(StringBuilder command, string value)
    {
        command.Append('"');
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            command.Append('\\', c == '"' ? 2 * slashes + 1 : slashes);
            command.Append(c);
            slashes = 0;
        }
        command.Append('\\', 2 * slashes).Append('"');
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfoEx
    {
        public int Size;
        public nint Reserved, Desktop, Title;
        public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public nint ReservedBuffer, Input, Output, Error, Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public nint Process, Thread; public int Pid, Tid; }

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcessW(string app, char* command, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, char* environment, string directory,
        ref StartupInfoEx startup, out ProcessInfo child);
    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint bytes);
    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nuint size, nint previous, nint returned);
    [LibraryImport("kernel32")]
    private static partial void DeleteProcThreadAttributeList(nint list);
    [LibraryImport("kernel32", SetLastError = true)]
    private static partial uint ResumeThread(SafeWaitHandle thread);
    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint code);
}
