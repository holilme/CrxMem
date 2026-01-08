using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLua;
using CrxMem.Core;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// Process control functions for Lua scripts.
    /// Matches CheatEngine's process API.
    /// </summary>
    public class LuaProcessFunctions
    {
        private readonly LuaEngine _engine;

        public LuaProcessFunctions(LuaEngine engine)
        {
            _engine = engine;
        }

        public void Register(Lua lua)
        {
            lua.RegisterFunction("openProcess", this, GetType().GetMethod(nameof(OpenProcess)));
            lua.RegisterFunction("getOpenedProcessID", this, GetType().GetMethod(nameof(GetOpenedProcessID)));
            lua.RegisterFunction("getProcessList", this, GetType().GetMethod(nameof(GetProcessList)));
            lua.RegisterFunction("getModuleList", this, GetType().GetMethod(nameof(GetModuleList)));
            lua.RegisterFunction("getAddress", this, GetType().GetMethod(nameof(GetAddress)));
            lua.RegisterFunction("pause", this, GetType().GetMethod(nameof(Pause)));
            lua.RegisterFunction("unpause", this, GetType().GetMethod(nameof(Unpause)));
            lua.RegisterFunction("targetIs64Bit", this, GetType().GetMethod(nameof(TargetIs64Bit)));
            lua.RegisterFunction("getProcessIDFromProcessName", this, GetType().GetMethod(nameof(GetProcessIDFromProcessName)));
        }

        /// <summary>
        /// Open a process by PID or name.
        /// </summary>
        public bool OpenProcess(object processIdOrName)
        {
            try
            {
                int pid;

                if (processIdOrName is double d)
                {
                    pid = (int)d;
                }
                else if (processIdOrName is long l)
                {
                    pid = (int)l;
                }
                else if (processIdOrName is int i)
                {
                    pid = i;
                }
                else if (processIdOrName is string name)
                {
                    // Find by name
                    var processes = Process.GetProcessesByName(name.Replace(".exe", ""));
                    if (processes.Length == 0) return false;
                    pid = processes[0].Id;
                }
                else
                {
                    return false;
                }

                var process = new ProcessAccess();
                if (process.Open(pid))
                {
                    _engine.SetProcessAccess(process);
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Get the currently opened process ID.
        /// </summary>
        public int GetOpenedProcessID()
        {
            var process = _engine.ProcessAccess;
            if (process?.Target == null) return 0;
            return process.Target.Id;
        }

        /// <summary>
        /// Get a list of all running processes.
        /// Returns a table with process info.
        /// </summary>
        public object[] GetProcessList()
        {
            try
            {
                var processes = Process.GetProcesses();
                var result = new List<object>();

                foreach (var p in processes)
                {
                    try
                    {
                        result.Add(new Dictionary<string, object>
                        {
                            { "pid", p.Id },
                            { "name", p.ProcessName }
                        });
                    }
                    catch { } // Skip processes we can't access
                }

                return result.ToArray();
            }
            catch { return Array.Empty<object>(); }
        }

        /// <summary>
        /// Get a list of modules in the opened process.
        /// Returns a table with module info.
        /// </summary>
        public object[] GetModuleList()
        {
            var process = _engine.ProcessAccess;
            if (process == null) return Array.Empty<object>();

            try
            {
                var modules = process.GetModules();
                var result = new List<object>();

                foreach (var m in modules)
                {
                    result.Add(new Dictionary<string, object>
                    {
                        { "name", m.ModuleName },
                        { "address", m.BaseAddress.ToInt64() },
                        { "size", m.Size }
                    });
                }

                return result.ToArray();
            }
            catch { return Array.Empty<object>(); }
        }

        /// <summary>
        /// Resolve an address expression like "module.exe+0x1234" to an actual address.
        /// </summary>
        public long GetAddress(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return 0;

            var process = _engine.ProcessAccess;
            expression = expression.Trim();

            // Check for module+offset format
            if (expression.Contains('+'))
            {
                var parts = expression.Split('+');
                if (parts.Length == 2)
                {
                    string moduleName = parts[0].Trim();
                    string offsetStr = parts[1].Trim().Replace("0x", "").Replace("0X", "");

                    if (long.TryParse(offsetStr, System.Globalization.NumberStyles.HexNumber, null, out long offset))
                    {
                        if (process != null)
                        {
                            var modules = process.GetModules();
                            foreach (var module in modules)
                            {
                                if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
                                    module.ModuleName.Equals(moduleName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                                    module.ModuleName.Equals(moduleName + ".dll", StringComparison.OrdinalIgnoreCase))
                                {
                                    return module.BaseAddress.ToInt64() + offset;
                                }
                            }
                        }
                    }
                }
                return 0;
            }

            // Check for pointer notation like [[base]+offset]
            if (expression.StartsWith("[") && expression.Contains("]"))
            {
                return ResolvePointerPath(expression);
            }

            // Plain hex address
            string cleanAddr = expression.Replace("0x", "").Replace("0X", "");
            if (long.TryParse(cleanAddr, System.Globalization.NumberStyles.HexNumber, null, out long addr))
            {
                return addr;
            }

            // Try as module name only (get base address)
            if (process != null)
            {
                var modules = process.GetModules();
                foreach (var module in modules)
                {
                    if (module.ModuleName.Equals(expression, StringComparison.OrdinalIgnoreCase) ||
                        module.ModuleName.Equals(expression + ".exe", StringComparison.OrdinalIgnoreCase) ||
                        module.ModuleName.Equals(expression + ".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return module.BaseAddress.ToInt64();
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Resolve a pointer path like [[base]+offset1]+offset2
        /// </summary>
        private long ResolvePointerPath(string expression)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return 0;

            try
            {
                // Simple implementation - parse basic pointer paths
                // Format: [[module+base]+off1]+off2
                // This is a simplified version - CE has much more complex parsing

                string current = expression;
                long address = 0;

                while (current.StartsWith("["))
                {
                    // Find matching bracket
                    int depth = 0;
                    int endBracket = -1;
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (current[i] == '[') depth++;
                        else if (current[i] == ']')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                endBracket = i;
                                break;
                            }
                        }
                    }

                    if (endBracket == -1) return 0;

                    string inner = current.Substring(1, endBracket - 1);
                    current = current.Substring(endBracket + 1);

                    // Resolve inner part
                    if (inner.StartsWith("["))
                    {
                        address = ResolvePointerPath(inner);
                    }
                    else
                    {
                        address = GetAddress(inner);
                    }

                    // Read pointer
                    if (address != 0)
                    {
                        if (process.Is64Bit)
                        {
                            address = process.Read<long>(new IntPtr(address));
                        }
                        else
                        {
                            address = process.Read<int>(new IntPtr(address));
                        }
                    }

                    // Handle offset after bracket
                    if (current.StartsWith("+") || current.StartsWith("-"))
                    {
                        bool subtract = current.StartsWith("-");
                        string offsetStr = current.Substring(1).TrimStart();

                        // Find end of offset (next bracket or end)
                        int endOffset = offsetStr.IndexOfAny(new[] { '[', ']', '+', '-' });
                        if (endOffset == -1) endOffset = offsetStr.Length;

                        string offsetPart = offsetStr.Substring(0, endOffset).Replace("0x", "").Replace("0X", "").Trim();
                        if (long.TryParse(offsetPart, System.Globalization.NumberStyles.HexNumber, null, out long offset))
                        {
                            address = subtract ? address - offset : address + offset;
                        }

                        current = offsetStr.Substring(endOffset);
                    }
                }

                return address;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Pause (suspend) the opened process.
        /// </summary>
        public bool Pause()
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            try
            {
                process.Suspend();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Unpause (resume) the opened process.
        /// </summary>
        public bool Unpause()
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            try
            {
                process.Resume();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if the target process is 64-bit.
        /// </summary>
        public bool TargetIs64Bit()
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;
            return process.Is64Bit;
        }

        /// <summary>
        /// Get process ID from process name.
        /// </summary>
        public int GetProcessIDFromProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;

            try
            {
                var processes = Process.GetProcessesByName(name.Replace(".exe", ""));
                if (processes.Length > 0)
                {
                    return processes[0].Id;
                }
            }
            catch { }

            return 0;
        }
    }
}
