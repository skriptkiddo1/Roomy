using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Roomy
{
    static class Native
    {
        public const uint PROCESS_SET_QUOTA = 0x0100;
        public const uint PROCESS_SET_INFORMATION = 0x0200;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public const int ProcessMemoryPriority = 0;
        public const int ProcessPowerThrottling = 4;

        public const uint MEMORY_PRIORITY_LOW = 2;
        public const uint MEMORY_PRIORITY_NORMAL = 5;

        public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
        public static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION pi, uint cb);

        public static PERFORMANCE_INFORMATION PerformanceInfo()
        {
            var pi = new PERFORMANCE_INFORMATION();
            pi.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
            GetPerformanceInfo(ref pi, pi.cb);
            return pi;
        }

        public static long Pages(UIntPtr pages, UIntPtr pageSize)
        {
            return (long)(pages.ToUInt64() * pageSize.ToUInt64());
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
        public static extern bool GetProcessMemoryInfo(IntPtr h, ref PROCESS_MEMORY_COUNTERS_EX2 c, uint cb);

        [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
        public static extern bool EmptyWorkingSet(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr h, int cls, ref uint info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr h, int cls, ref PROCESS_POWER_THROTTLING_STATE info, uint size);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref uint size);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public static MEMORYSTATUSEX MemoryStatus()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            return m;
        }

        // private working set (как колонка "Память" в диспетчере задач). -1 если не удалось.
        public static long PrivateWorkingSet(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero) h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return -1;
            try
            {
                var c = new PROCESS_MEMORY_COUNTERS_EX2();
                c.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX2));
                if (!GetProcessMemoryInfo(h, ref c, c.cb)) return -1;
                long priv = (long)c.PrivateWorkingSetSize.ToUInt64();
                if (priv <= 0) priv = (long)c.WorkingSetSize.ToUInt64();
                return priv;
            }
            finally { CloseHandle(h); }
        }

        public static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static string ForegroundProcessName()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            int pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return null;
            string path = ImagePath(pid);
            return path == null ? null : Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        }

        // понизить приоритет памяти и включить режим эффективности (EcoQoS).
        public static bool LowerProcess(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                uint prio = MEMORY_PRIORITY_LOW;
                bool a = SetProcessInformation(h, ProcessMemoryPriority, ref prio, 4);
                var st = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                };
                bool b = SetProcessInformation(h, ProcessPowerThrottling, ref st, (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
                return a || b;
            }
            finally { CloseHandle(h); }
        }

        // вернуть обычный приоритет памяти и отдать управление EcoQoS системе.
        public static bool RestoreProcess(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                uint prio = MEMORY_PRIORITY_NORMAL;
                bool a = SetProcessInformation(h, ProcessMemoryPriority, ref prio, 4);
                var st = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = 0,
                    StateMask = 0
                };
                bool b = SetProcessInformation(h, ProcessPowerThrottling, ref st, (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
                return a || b;
            }
            finally { CloseHandle(h); }
        }

        public static bool TrimProcess(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return EmptyWorkingSet(h); }
            finally { CloseHandle(h); }
        }
    }
}
