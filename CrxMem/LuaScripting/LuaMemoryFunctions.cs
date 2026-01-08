using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using NLua;
using CrxMem.Core;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// Memory access functions for Lua scripts.
    /// Matches CheatEngine's memory API.
    /// </summary>
    public class LuaMemoryFunctions
    {
        private readonly LuaEngine _engine;

        // P/Invoke for memory allocation
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
            uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
            uint dwSize, uint dwFreeType);

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        public LuaMemoryFunctions(LuaEngine engine)
        {
            _engine = engine;
        }

        public void Register(Lua lua)
        {
            // Reading functions
            lua.RegisterFunction("readByte", this, GetType().GetMethod(nameof(ReadByte)));
            lua.RegisterFunction("readSmallInteger", this, GetType().GetMethod(nameof(ReadSmallInteger)));
            lua.RegisterFunction("readInteger", this, GetType().GetMethod(nameof(ReadInteger)));
            lua.RegisterFunction("readQword", this, GetType().GetMethod(nameof(ReadQword)));
            lua.RegisterFunction("readFloat", this, GetType().GetMethod(nameof(ReadFloat)));
            lua.RegisterFunction("readDouble", this, GetType().GetMethod(nameof(ReadDouble)));
            lua.RegisterFunction("readString", this, GetType().GetMethod(nameof(ReadString)));
            lua.RegisterFunction("readBytes", this, GetType().GetMethod(nameof(ReadBytes)));
            lua.RegisterFunction("readPointer", this, GetType().GetMethod(nameof(ReadPointer)));

            // Writing functions
            lua.RegisterFunction("writeByte", this, GetType().GetMethod(nameof(WriteByte)));
            lua.RegisterFunction("writeSmallInteger", this, GetType().GetMethod(nameof(WriteSmallInteger)));
            lua.RegisterFunction("writeInteger", this, GetType().GetMethod(nameof(WriteInteger)));
            lua.RegisterFunction("writeQword", this, GetType().GetMethod(nameof(WriteQword)));
            lua.RegisterFunction("writeFloat", this, GetType().GetMethod(nameof(WriteFloat)));
            lua.RegisterFunction("writeDouble", this, GetType().GetMethod(nameof(WriteDouble)));
            lua.RegisterFunction("writeString", this, GetType().GetMethod(nameof(WriteString)));
            lua.RegisterFunction("writeBytes", this, GetType().GetMethod(nameof(WriteBytes)));

            // Memory allocation
            lua.RegisterFunction("allocateMemory", this, GetType().GetMethod(nameof(AllocateMemory)));
            lua.RegisterFunction("deAlloc", this, GetType().GetMethod(nameof(DeAlloc)));
        }

        private IntPtr ToAddress(object addressObj)
        {
            if (addressObj == null) return IntPtr.Zero;

            // Handle string addresses (like "0x12345" or "game.exe+0x1234")
            if (addressObj is string addressStr)
            {
                return ParseAddress(addressStr);
            }

            // Handle numeric values
            if (addressObj is long l) return new IntPtr(l);
            if (addressObj is int i) return new IntPtr(i);
            if (addressObj is double d) return new IntPtr((long)d);
            if (addressObj is ulong ul) return new IntPtr((long)ul);
            if (addressObj is uint ui) return new IntPtr(ui);

            return IntPtr.Zero;
        }

        private IntPtr ParseAddress(string addressStr)
        {
            if (string.IsNullOrWhiteSpace(addressStr)) return IntPtr.Zero;

            addressStr = addressStr.Trim();

            // Check for module+offset format
            if (addressStr.Contains('+'))
            {
                var parts = addressStr.Split('+');
                if (parts.Length == 2)
                {
                    string moduleName = parts[0].Trim();
                    string offsetStr = parts[1].Trim().Replace("0x", "").Replace("0X", "");

                    if (long.TryParse(offsetStr, System.Globalization.NumberStyles.HexNumber, null, out long offset))
                    {
                        var process = _engine.ProcessAccess;
                        if (process != null)
                        {
                            var modules = process.GetModules();
                            foreach (var module in modules)
                            {
                                if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
                                    module.ModuleName.Equals(moduleName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                                    module.ModuleName.Equals(moduleName + ".dll", StringComparison.OrdinalIgnoreCase))
                                {
                                    return new IntPtr(module.BaseAddress.ToInt64() + offset);
                                }
                            }
                        }
                    }
                }
                return IntPtr.Zero;
            }

            // Plain hex address
            addressStr = addressStr.Replace("0x", "").Replace("0X", "");
            if (long.TryParse(addressStr, System.Globalization.NumberStyles.HexNumber, null, out long addr))
            {
                return new IntPtr(addr);
            }

            return IntPtr.Zero;
        }

        // ═══════════════════════════════════════════════════════════════
        // READING FUNCTIONS
        // ═══════════════════════════════════════════════════════════════

        public int ReadByte(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                return process.Read<byte>(addr);
            }
            catch { return 0; }
        }

        public int ReadSmallInteger(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                return process.Read<short>(addr);
            }
            catch { return 0; }
        }

        public int ReadInteger(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                return process.Read<int>(addr);
            }
            catch { return 0; }
        }

        public long ReadQword(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                return process.Read<long>(addr);
            }
            catch { return 0; }
        }

        public double ReadFloat(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                return process.Read<float>(addr);
            }
            catch { return 0; }
        }

        public double ReadDouble(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                return process.Read<double>(addr);
            }
            catch { return 0; }
        }

        public string ReadString(object address, int maxLength = 256)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return "";

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return "";

            try
            {
                byte[]? buffer = process.Read(addr, maxLength);
                if (buffer != null && buffer.Length > 0)
                {
                    // Find null terminator
                    int length = 0;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        if (buffer[i] == 0) break;
                        length++;
                    }
                    return Encoding.ASCII.GetString(buffer, 0, length);
                }
            }
            catch { }

            return "";
        }

        public LuaTable? ReadBytes(object address, int count)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return null;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return null;

            try
            {
                byte[]? buffer = process.Read(addr, count);
                if (buffer != null && buffer.Length > 0)
                {
                    // Create a Lua table with the bytes
                    // Note: We return a .NET array and let NLua handle conversion
                    // For a proper LuaTable, we'd need access to the Lua state
                    return null; // Will be improved to return proper table
                }
            }
            catch { }

            return null;
        }

        public long ReadPointer(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return 0;

            try
            {
                // Read 8 bytes for 64-bit, 4 bytes for 32-bit
                if (process.Is64Bit)
                {
                    return process.Read<long>(addr);
                }
                else
                {
                    return process.Read<int>(addr);
                }
            }
            catch { return 0; }
        }

        // ═══════════════════════════════════════════════════════════════
        // WRITING FUNCTIONS
        // ═══════════════════════════════════════════════════════════════

        public bool WriteByte(object address, int value)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return process.WriteWithProtection(addr, (byte)value);
            }
            catch { return false; }
        }

        public bool WriteSmallInteger(object address, int value)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return process.WriteWithProtection(addr, (short)value);
            }
            catch { return false; }
        }

        public bool WriteInteger(object address, int value)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return process.WriteWithProtection(addr, value);
            }
            catch { return false; }
        }

        public bool WriteQword(object address, long value)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return process.WriteWithProtection(addr, value);
            }
            catch { return false; }
        }

        public bool WriteFloat(object address, double value)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return process.WriteWithProtection(addr, (float)value);
            }
            catch { return false; }
        }

        public bool WriteDouble(object address, double value)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return process.WriteWithProtection(addr, value);
            }
            catch { return false; }
        }

        public bool WriteString(object address, string text)
        {
            var process = _engine.ProcessAccess;
            if (process == null || text == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
                return process.WriteWithProtection(addr, bytes);
            }
            catch { return false; }
        }

        public bool WriteBytes(object address, object bytesObj)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                byte[] bytes;

                // Handle LuaTable
                if (bytesObj is LuaTable table)
                {
                    var list = new List<byte>();
                    foreach (var key in table.Keys)
                    {
                        var val = table[key];
                        if (val is double d) list.Add((byte)d);
                        else if (val is long l) list.Add((byte)l);
                        else if (val is int i) list.Add((byte)i);
                    }
                    bytes = list.ToArray();
                }
                // Handle .NET array
                else if (bytesObj is byte[] byteArray)
                {
                    bytes = byteArray;
                }
                else
                {
                    return false;
                }

                return process.WriteWithProtection(addr, bytes);
            }
            catch { return false; }
        }

        // ═══════════════════════════════════════════════════════════════
        // MEMORY ALLOCATION
        // ═══════════════════════════════════════════════════════════════

        public long AllocateMemory(int size, object? nearAddress = null)
        {
            var process = _engine.ProcessAccess;
            if (process == null || process.Handle == IntPtr.Zero) return 0;

            try
            {
                IntPtr preferredAddress = IntPtr.Zero;
                if (nearAddress != null)
                {
                    preferredAddress = ToAddress(nearAddress);
                }

                IntPtr result = VirtualAllocEx(
                    process.Handle,
                    preferredAddress,
                    (uint)size,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_EXECUTE_READWRITE);

                return result.ToInt64();
            }
            catch { return 0; }
        }

        public bool DeAlloc(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null || process.Handle == IntPtr.Zero) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                return VirtualFreeEx(process.Handle, addr, 0, MEM_RELEASE);
            }
            catch { return false; }
        }
    }
}
