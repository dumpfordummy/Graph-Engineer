# The job handle belongs only to this PowerShell process. Windows closes it even
# if the launcher is terminated before its finally block can run.
if (-not ('GraphEngineering.LauncherProcessJob' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace GraphEngineering {
    public sealed class LauncherProcessJob : IDisposable {
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits {
            public long perProcessTime, perJobTime;
            public uint flags;
            public UIntPtr minimumWorkingSet, maximumWorkingSet;
            public uint activeProcessLimit;
            public UIntPtr affinity;
            public uint priorityClass, schedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong readOperations, writeOperations, otherOperations, readBytes, writeBytes, otherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits {
            public BasicLimits basic;
            public IoCounters io;
            public UIntPtr processMemory, jobMemory, peakProcessMemory, peakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        readonly SafeFileHandle job;
        public LauncherProcessJob() {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits();
            limits.basic.flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no child breakaway.
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) {
                var error = Marshal.GetLastWin32Error();
                job.Dispose();
                throw new Win32Exception(error);
            }
        }
        public void AddProcess(IntPtr process) {
            if (!AssignProcessToJobObject(job, process)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose() { job.Dispose(); }
    }
}
'@
}
