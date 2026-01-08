using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace CrxShield
{
    public class CrxDriverController : IDisposable
    {
        private SafeFileHandle m_hDevice;
        private const string DeviceName = @"\\.\CrxShield";

        // IOCTL Codes
        private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;

        private static uint CTL_CODE(uint DeviceType, uint Function, uint Method, uint Access)
        {
            return ((DeviceType << 16) | (Access << 14) | (Function << 2) | Method);
        }

        private const uint IOCTL_BASE = 0x1337;
        public static readonly uint IOCTL_CRXSHIELD_GET_VERSION = CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x0, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public static readonly uint IOCTL_CRXSHIELD_READ_MEMORY = CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x1, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public static readonly uint IOCTL_CRXSHIELD_WRITE_MEMORY = CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x2, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public static readonly uint IOCTL_CRXSHIELD_GET_PROCESS_BASE = CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x3, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public static readonly uint IOCTL_CRXSHIELD_ENUM_CALLBACKS = CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x4, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public static readonly uint IOCTL_CRXSHIELD_REMOVE_CALLBACK = CTL_CODE(FILE_DEVICE_UNKNOWN, IOCTL_BASE + 0x5, METHOD_BUFFERED, FILE_ANY_ACCESS);

        // Structures
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CRXSHIELD_VERSION
        {
            public uint Major;
            public uint Minor;
            public uint Build;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CRXSHIELD_READ_REQUEST
        {
            public uint ProcessId;
            public ulong Address;
            public uint Size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CRXSHIELD_WRITE_REQUEST
        {
            public uint ProcessId;
            public ulong Address;
            public uint Size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CRXSHIELD_PROCESS_BASE_REQUEST
        {
            public uint ProcessId;
            public ulong BaseAddress;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CRXSHIELD_CALLBACK_ENTRY
        {
            public ulong CallbackAddress;
            public ulong Context;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CRXSHIELD_ENUM_CALLBACKS_REQUEST
        {
            public uint MaxEntries;
            public uint EntryCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CRXSHIELD_REMOVE_CALLBACK_REQUEST
        {
            public ulong CallbackAddress;
        }

        // P/Invoke
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        public CrxDriverController()
        {
            m_hDevice = new SafeFileHandle(IntPtr.Zero, true);
        }

        public bool Connect()
        {
            m_hDevice = CreateFile(
                DeviceName,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero
            );

            return !m_hDevice.IsInvalid;
        }

        public void Disconnect()
        {
            if (m_hDevice != null && !m_hDevice.IsInvalid)
            {
                m_hDevice.Close();
                m_hDevice.SetHandleAsInvalid();
            }
        }

        public bool IsConnected()
        {
            return m_hDevice != null && !m_hDevice.IsInvalid;
        }

        public bool GetVersion(out uint Major, out uint Minor, out uint Build)
        {
            Major = 0; Minor = 0; Build = 0;
            if (!IsConnected()) return false;

            var version = new CRXSHIELD_VERSION();
            uint bytesReturned;
            
            // Marshalling struct to pointer for DeviceIoControl
            int size = Marshal.SizeOf(version);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(version, ptr, false);

            try
            {
                if (DeviceIoControl(m_hDevice, IOCTL_CRXSHIELD_GET_VERSION, IntPtr.Zero, 0, ptr, (uint)size, out bytesReturned, IntPtr.Zero))
                {
                    version = (CRXSHIELD_VERSION)Marshal.PtrToStructure(ptr, typeof(CRXSHIELD_VERSION));
                    Major = version.Major;
                    Minor = version.Minor;
                    Build = version.Build;
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return false;
        }

        public bool ReadProcessMemory(uint ProcessId, ulong Address, byte[] Buffer)
        {
            if (!IsConnected() || Buffer == null || Buffer.Length == 0) return false;

            var req = new CRXSHIELD_READ_REQUEST
            {
                ProcessId = ProcessId,
                Address = Address,
                Size = (uint)Buffer.Length
            };

            int reqSize = Marshal.SizeOf(req);
            IntPtr reqPtr = Marshal.AllocHGlobal(reqSize);
            Marshal.StructureToPtr(req, reqPtr, false);

            GCHandle bufferHandle = GCHandle.Alloc(Buffer, GCHandleType.Pinned);

            try
            {
                uint bytesReturned;
                return DeviceIoControl(m_hDevice, IOCTL_CRXSHIELD_READ_MEMORY, reqPtr, (uint)reqSize, bufferHandle.AddrOfPinnedObject(), (uint)Buffer.Length, out bytesReturned, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(reqPtr);
                bufferHandle.Free();
            }
        }

        public bool WriteProcessMemory(uint ProcessId, ulong Address, byte[] Buffer)
        {
            if (!IsConnected() || Buffer == null || Buffer.Length == 0) return false;

            var req = new CRXSHIELD_WRITE_REQUEST
            {
                ProcessId = ProcessId,
                Address = Address,
                Size = (uint)Buffer.Length
            };

            // Allocate a buffer that holds Request + Data
            int reqSize = Marshal.SizeOf(req);
            int totalSize = reqSize + Buffer.Length;
            IntPtr ptr = Marshal.AllocHGlobal(totalSize);

            try
            {
                Marshal.StructureToPtr(req, ptr, false);
                Marshal.Copy(Buffer, 0, IntPtr.Add(ptr, reqSize), Buffer.Length);

                uint bytesReturned;
                return DeviceIoControl(m_hDevice, IOCTL_CRXSHIELD_WRITE_MEMORY, ptr, (uint)totalSize, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public bool GetProcessBaseAddress(uint ProcessId, out ulong BaseAddress)
        {
            BaseAddress = 0;
            if (!IsConnected()) return false;

            var req = new CRXSHIELD_PROCESS_BASE_REQUEST
            {
                ProcessId = ProcessId,
                BaseAddress = 0
            };

            int size = Marshal.SizeOf(req);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(req, ptr, false);

            try
            {
                uint bytesReturned;
                if (DeviceIoControl(m_hDevice, IOCTL_CRXSHIELD_GET_PROCESS_BASE, ptr, (uint)size, ptr, (uint)size, out bytesReturned, IntPtr.Zero))
                {
                    req = (CRXSHIELD_PROCESS_BASE_REQUEST)Marshal.PtrToStructure(ptr, typeof(CRXSHIELD_PROCESS_BASE_REQUEST));
                    BaseAddress = req.BaseAddress;
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return false;
        }

        public bool EnumObCallbacks(out List<CRXSHIELD_CALLBACK_ENTRY> Callbacks)
        {
            Callbacks = new List<CRXSHIELD_CALLBACK_ENTRY>();
            if (!IsConnected()) return false;

            const uint MAX_ENTRIES = 100;
            var req = new CRXSHIELD_ENUM_CALLBACKS_REQUEST
            {
                MaxEntries = MAX_ENTRIES,
                EntryCount = 0
            };

            int reqSize = Marshal.SizeOf(req);
            int entrySize = Marshal.SizeOf(typeof(CRXSHIELD_CALLBACK_ENTRY));
            int bufferSize = reqSize + (int)(MAX_ENTRIES * entrySize);
            
            IntPtr ptr = Marshal.AllocHGlobal(bufferSize);
            Marshal.StructureToPtr(req, ptr, false);

            try
            {
                uint bytesReturned;
                if (DeviceIoControl(m_hDevice, IOCTL_CRXSHIELD_ENUM_CALLBACKS, ptr, (uint)bufferSize, ptr, (uint)bufferSize, out bytesReturned, IntPtr.Zero))
                {
                    req = (CRXSHIELD_ENUM_CALLBACKS_REQUEST)Marshal.PtrToStructure(ptr, typeof(CRXSHIELD_ENUM_CALLBACKS_REQUEST));
                    
                    IntPtr entriesPtr = IntPtr.Add(ptr, reqSize);
                    for (int i = 0; i < req.EntryCount; i++)
                    {
                        var entry = (CRXSHIELD_CALLBACK_ENTRY)Marshal.PtrToStructure(IntPtr.Add(entriesPtr, i * entrySize), typeof(CRXSHIELD_CALLBACK_ENTRY));
                        Callbacks.Add(entry);
                    }
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return false;
        }

        public bool RemoveObCallback(ulong CallbackAddress)
        {
            if (!IsConnected()) return false;

            var req = new CRXSHIELD_REMOVE_CALLBACK_REQUEST
            {
                CallbackAddress = CallbackAddress
            };

            int size = Marshal.SizeOf(req);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(req, ptr, false);

            try
            {
                uint bytesReturned;
                return DeviceIoControl(m_hDevice, IOCTL_CRXSHIELD_REMOVE_CALLBACK, ptr, (uint)size, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
