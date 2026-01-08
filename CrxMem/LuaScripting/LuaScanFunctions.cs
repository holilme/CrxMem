using System;
using System.Collections.Generic;
using System.Linq;
using NLua;
using CrxMem.Core;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// Memory scanning functions for Lua scripts.
    /// Matches CheatEngine's scanning API.
    /// </summary>
    public class LuaScanFunctions
    {
        private readonly LuaEngine _engine;
        private MemoryScanner? _currentScanner;

        public LuaScanFunctions(LuaEngine engine)
        {
            _engine = engine;
        }

        public void Register(Lua lua)
        {
            lua.RegisterFunction("AOBScan", this, GetType().GetMethod(nameof(AOBScan)));
            lua.RegisterFunction("AOBScanUnique", this, GetType().GetMethod(nameof(AOBScanUnique)));
            lua.RegisterFunction("AOBScanEx", this, GetType().GetMethod(nameof(AOBScanEx)));
        }

        /// <summary>
        /// Scan for an array of bytes pattern.
        /// Supports wildcards with '?' or '??'.
        /// Returns an array of addresses.
        /// </summary>
        public long[] AOBScan(string pattern)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return Array.Empty<long>();

            try
            {
                var scanner = new MemoryScanner(process);
                var results = scanner.ScanArrayOfBytes(pattern);

                if (results != null && results.Count > 0)
                {
                    return results.Select(r => r.ToInt64()).ToArray();
                }
            }
            catch { }

            return Array.Empty<long>();
        }

        /// <summary>
        /// Scan for a unique AOB pattern.
        /// Returns the address if found exactly once, 0 otherwise.
        /// </summary>
        public long AOBScanUnique(string pattern)
        {
            var results = AOBScan(pattern);
            if (results.Length == 1)
            {
                return results[0];
            }
            return 0;
        }

        /// <summary>
        /// Extended AOB scan with options.
        /// </summary>
        public long[] AOBScanEx(string pattern, string? moduleName = null, long startAddress = 0, long endAddress = 0)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return Array.Empty<long>();

            try
            {
                // If module specified, limit scan to that module
                if (!string.IsNullOrEmpty(moduleName))
                {
                    var modules = process.GetModules();
                    var module = modules.FirstOrDefault(m =>
                        m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
                        m.ModuleName.Equals(moduleName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                        m.ModuleName.Equals(moduleName + ".dll", StringComparison.OrdinalIgnoreCase));

                    if (module != null)
                    {
                        startAddress = module.BaseAddress.ToInt64();
                        endAddress = startAddress + module.Size;
                    }
                }

                var scanner = new MemoryScanner(process);
                var results = scanner.ScanArrayOfBytes(pattern);

                if (results != null && results.Count > 0)
                {
                    // Filter by address range if specified
                    if (startAddress != 0 || endAddress != 0)
                    {
                        return results
                            .Select(r => r.ToInt64())
                            .Where(a => (startAddress == 0 || a >= startAddress) &&
                                       (endAddress == 0 || a <= endAddress))
                            .ToArray();
                    }

                    return results.Select(r => r.ToInt64()).ToArray();
                }
            }
            catch { }

            return Array.Empty<long>();
        }
    }
}
