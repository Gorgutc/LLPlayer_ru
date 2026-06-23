using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Wraps a child process in a Windows Job Object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> so the
/// OS reaps the (multi-GB GPU) sidecar if LLPlayer dies, crashes, or is killed from Task Manager — the
/// exact orphan class PR #33/#34 removed. Keep the returned handle open for the host's lifetime;
/// closing it (on dispose or on process death) terminates the assigned child. Best-effort: any failure
/// returns null and the caller falls back to explicit Kill + the sidecar's own parent-PID watchdog.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class JobObjectGuard
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary>
    /// Create a kill-on-close job and assign <paramref name="processHandle"/> to it. Returns the job
    /// handle to hold open, or null on any failure (the caller logs and continues without the net).
    /// </summary>
    public static SafeFileHandle? AssignKillOnClose(IntPtr processHandle)
    {
        SafeFileHandle job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (job.IsInvalid)
            return null;

        JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = default;
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                job.Dispose();
                return null;
            }

            if (!AssignProcessToJobObject(job, processHandle))
            {
                job.Dispose();
                return null;
            }

            return job;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle CreateJobObjectW(IntPtr lpJobAttributes, IntPtr lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle hJob, int infoClass, IntPtr lpInfo, uint cbInfo);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
