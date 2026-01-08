using System;
using System.Runtime.InteropServices;

namespace CrxMem.Core
{
    /// <summary>
    /// Represents a region of memory in the target process
    /// </summary>
    public class MemoryRegion
    {
        [DllImport("kernel32.dll")]
        private static extern bool VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        // Memory states
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_FREE = 0x10000;
        public const uint MEM_RESERVE = 0x2000;

        // Protection flags
        public const uint PAGE_NOACCESS = 0x01;
        public const uint PAGE_READONLY = 0x02;
        public const uint PAGE_READWRITE = 0x04;
        public const uint PAGE_WRITECOPY = 0x08;
        public const uint PAGE_EXECUTE = 0x10;
        public const uint PAGE_EXECUTE_READ = 0x20;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        public const uint PAGE_EXECUTE_WRITECOPY = 0x80;
        public const uint PAGE_GUARD = 0x100;

        public IntPtr BaseAddress { get; set; }
        public IntPtr AllocationBase { get; set; }
        public long Size { get; set; }
        public long RegionSize => Size;  // Alias for Size
        public uint Protection { get; set; }
        public uint State { get; set; }
        public uint Type { get; set; }

        public bool IsReadable =>
            (Protection & PAGE_READONLY) != 0 ||
            (Protection & PAGE_READWRITE) != 0 ||
            (Protection & PAGE_EXECUTE_READ) != 0 ||
            (Protection & PAGE_EXECUTE_READWRITE) != 0;

        public bool IsWritable =>
            (Protection & PAGE_READWRITE) != 0 ||
            (Protection & PAGE_EXECUTE_READWRITE) != 0;

        public bool IsCopyOnWrite =>
            (Protection & PAGE_WRITECOPY) != 0 ||
            (Protection & PAGE_EXECUTE_WRITECOPY) != 0;

        public bool IsExecutable =>
            (Protection & PAGE_EXECUTE) != 0 ||
            (Protection & PAGE_EXECUTE_READ) != 0 ||
            (Protection & PAGE_EXECUTE_READWRITE) != 0 ||
            (Protection & PAGE_EXECUTE_WRITECOPY) != 0;

        public bool IsCommitted => (State & MEM_COMMIT) != 0;

        /// <summary>
        /// Query memory region information
        /// </summary>
        public static MemoryRegion? Query(IntPtr processHandle, IntPtr address)
        {
            if (!VirtualQueryEx(processHandle, address, out MEMORY_BASIC_INFORMATION mbi,
                (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))))
            {
                return null;
            }

            return new MemoryRegion
            {
                BaseAddress = mbi.BaseAddress,
                AllocationBase = mbi.AllocationBase,
                Size = mbi.RegionSize.ToInt64(),
                Protection = mbi.Protect,
                State = mbi.State,
                Type = mbi.Type
            };
        }

        /// <summary>
        /// Enumerate all memory regions in a process
        /// </summary>
        public static System.Collections.Generic.List<MemoryRegion> EnumerateRegions(IntPtr processHandle)
        {
            var regions = new System.Collections.Generic.List<MemoryRegion>();
            IntPtr address = IntPtr.Zero;

            while (true)
            {
                if (!VirtualQueryEx(processHandle, address, out MEMORY_BASIC_INFORMATION mbi,
                    (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))))
                {
                    break;
                }

                regions.Add(new MemoryRegion
                {
                    BaseAddress = mbi.BaseAddress,
                    AllocationBase = mbi.AllocationBase,
                    Size = mbi.RegionSize.ToInt64(),
                    Protection = mbi.Protect,
                    State = mbi.State,
                    Type = mbi.Type
                });

                // Move to next region
                long nextAddr = mbi.BaseAddress.ToInt64() + mbi.RegionSize.ToInt64();
                if (nextAddr <= address.ToInt64())
                    break; // Prevent infinite loop

                address = new IntPtr(nextAddr);
            }

            return regions;
        }

        public override string ToString()
        {
            return $"0x{BaseAddress.ToInt64():X} - 0x{(BaseAddress.ToInt64() + Size):X} " +
                   $"({Size / 1024}KB) " +
                   $"[{(IsReadable ? "R" : "-")}{(IsWritable ? "W" : "-")}]";
        }
    }
}
