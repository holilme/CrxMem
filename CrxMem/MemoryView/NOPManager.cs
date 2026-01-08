using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CrxMem.Core;

namespace CrxMem.MemoryView
{
    public class NOPManager
    {
        private const byte NOP_OPCODE = 0x90;

        private ProcessAccess _process;
        private List<CodeListEntry> _codeList;

        // Memory protection constants
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_READONLY = 0x02;

        public NOPManager(ProcessAccess process)
        {
            _process = process;
            _codeList = new List<CodeListEntry>();
        }

        public IReadOnlyList<CodeListEntry> CodeList => _codeList.AsReadOnly();

        /// <summary>
        /// Check if kernel mode is enabled for memory operations
        /// </summary>
        private bool UseKernelMode => AntiCheatBypassForm.UseKernelMode && AntiCheatBypassForm.DriverController != null;

        /// <summary>
        /// Replaces instruction bytes at the given address with NOP (0x90) instructions
        /// </summary>
        public bool ReplaceWithNOPs(IntPtr address, int length, string description = "")
        {
            if (_process == null || length <= 0)
                return false;

            try
            {
                // Read original bytes first
                byte[] originalBytes = _process.Read(address, length);
                if (originalBytes == null || originalBytes.Length < length)
                    return false;

                // Create NOP buffer
                byte[] nopBytes = new byte[length];
                for (int i = 0; i < length; i++)
                {
                    nopBytes[i] = NOP_OPCODE;
                }

                uint oldProtect = 0;
                bool protectionChanged = false;

                // When using kernel mode, skip VirtualProtectEx - driver can write anywhere
                if (!UseKernelMode)
                {
                    // Change memory protection to allow writing
                    if (!VirtualProtectEx(_process.Handle, address, (UIntPtr)length, PAGE_EXECUTE_READWRITE, out oldProtect))
                    {
                        return false;
                    }
                    protectionChanged = true;
                }

                try
                {
                    // Write NOPs
                    if (!_process.Write(address, nopBytes))
                    {
                        return false;
                    }

                    // Flush instruction cache (only if we have a valid handle)
                    if (_process.Handle != IntPtr.Zero)
                    {
                        FlushInstructionCache(_process.Handle, address, (UIntPtr)length);
                    }

                    // Add to code list for undo
                    var entry = new CodeListEntry
                    {
                        Address = address,
                        Length = length,
                        OriginalBytes = originalBytes,
                        Description = string.IsNullOrEmpty(description) ? $"NOP {address.ToInt64():X8}" : description,
                        IsEnabled = true,
                        Timestamp = DateTime.Now
                    };
                    _codeList.Add(entry);

                    return true;
                }
                finally
                {
                    // Restore original protection (only if we changed it)
                    if (protectionChanged)
                    {
                        VirtualProtectEx(_process.Handle, address, (UIntPtr)length, oldProtect, out _);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restores original bytes for a code list entry
        /// </summary>
        public bool RestoreOriginalBytes(CodeListEntry entry)
        {
            if (_process == null || entry == null || entry.OriginalBytes == null)
                return false;

            try
            {
                uint oldProtect = 0;
                bool protectionChanged = false;

                // When using kernel mode, skip VirtualProtectEx
                if (!UseKernelMode)
                {
                    // Change memory protection
                    if (!VirtualProtectEx(_process.Handle, entry.Address, (UIntPtr)entry.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
                    {
                        return false;
                    }
                    protectionChanged = true;
                }

                try
                {
                    // Write original bytes
                    if (!_process.Write(entry.Address, entry.OriginalBytes))
                    {
                        return false;
                    }

                    // Flush instruction cache (only if we have a valid handle)
                    if (_process.Handle != IntPtr.Zero)
                    {
                        FlushInstructionCache(_process.Handle, entry.Address, (UIntPtr)entry.Length);
                    }

                    entry.IsEnabled = false;
                    return true;
                }
                finally
                {
                    // Restore protection (only if we changed it)
                    if (protectionChanged)
                    {
                        VirtualProtectEx(_process.Handle, entry.Address, (UIntPtr)entry.Length, oldProtect, out _);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Re-applies NOPs for a code list entry
        /// </summary>
        public bool ReapplyNOPs(CodeListEntry entry)
        {
            if (_process == null || entry == null)
                return false;

            try
            {
                byte[] nopBytes = new byte[entry.Length];
                for (int i = 0; i < entry.Length; i++)
                {
                    nopBytes[i] = NOP_OPCODE;
                }

                uint oldProtect = 0;
                bool protectionChanged = false;

                // When using kernel mode, skip VirtualProtectEx
                if (!UseKernelMode)
                {
                    if (!VirtualProtectEx(_process.Handle, entry.Address, (UIntPtr)entry.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
                    {
                        return false;
                    }
                    protectionChanged = true;
                }

                try
                {
                    if (!_process.Write(entry.Address, nopBytes))
                    {
                        return false;
                    }

                    // Flush instruction cache (only if we have a valid handle)
                    if (_process.Handle != IntPtr.Zero)
                    {
                        FlushInstructionCache(_process.Handle, entry.Address, (UIntPtr)entry.Length);
                    }

                    entry.IsEnabled = true;
                    return true;
                }
                finally
                {
                    // Restore protection (only if we changed it)
                    if (protectionChanged)
                    {
                        VirtualProtectEx(_process.Handle, entry.Address, (UIntPtr)entry.Length, oldProtect, out _);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Adds a modified code entry to the code list (for tracking assembly modifications)
        /// </summary>
        public void AddModifiedCode(IntPtr address, byte[] originalBytes, string description)
        {
            var entry = new CodeListEntry
            {
                Address = address,
                Length = originalBytes.Length,
                OriginalBytes = originalBytes,
                Description = description,
                IsEnabled = true,
                Timestamp = DateTime.Now
            };
            _codeList.Add(entry);
        }

        /// <summary>
        /// Removes a code list entry
        /// </summary>
        public bool RemoveEntry(CodeListEntry entry)
        {
            if (entry == null)
                return false;

            // Restore original bytes if still NOPped
            if (entry.IsEnabled)
            {
                RestoreOriginalBytes(entry);
            }

            return _codeList.Remove(entry);
        }

        /// <summary>
        /// Clears all code list entries
        /// </summary>
        public void ClearAll()
        {
            // Restore all entries first
            foreach (var entry in _codeList.ToArray())
            {
                if (entry.IsEnabled)
                {
                    RestoreOriginalBytes(entry);
                }
            }

            _codeList.Clear();
        }

        #region P/Invoke

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flNewProtect,
            out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            UIntPtr dwSize);

        #endregion
    }

    /// <summary>
    /// Represents a code modification entry in the code list
    /// </summary>
    public class CodeListEntry
    {
        public IntPtr Address { get; set; }
        public int Length { get; set; }
        public byte[]? OriginalBytes { get; set; }
        public string Description { get; set; } = "";
        public bool IsEnabled { get; set; }
        public DateTime Timestamp { get; set; }

        public string GetBytesString()
        {
            if (OriginalBytes == null)
                return "";

            return BitConverter.ToString(OriginalBytes).Replace("-", " ");
        }
    }
}
