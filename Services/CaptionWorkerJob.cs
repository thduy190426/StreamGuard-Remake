using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StreamGuard.Services;

internal sealed class CaptionWorkerJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    private readonly SafeFileHandle _handle;
    public CaptionWorkerJob(Process process)
    {
        _handle = CreateJobObjectW(IntPtr.Zero, null);
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 /* KILL_ON_JOB_CLOSE */ } };
        if (_handle.IsInvalid || !SetInformationJobObject(_handle, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(_handle, process.Handle))
        { var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error, "The caption worker could not be attached to its owner."); }
    }
    public void Dispose() => _handle.Dispose();
}
