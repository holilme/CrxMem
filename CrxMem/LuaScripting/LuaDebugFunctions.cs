using System;
using NLua;
using CrxMem.Core;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// Debugging functions for Lua scripts.
    /// Provides access to breakpoint and debug functionality.
    /// </summary>
    public class LuaDebugFunctions
    {
        private readonly LuaEngine _engine;

        public LuaDebugFunctions(LuaEngine engine)
        {
            _engine = engine;
        }

        public void Register(Lua lua)
        {
            lua.RegisterFunction("debug_setBreakpoint", this, GetType().GetMethod(nameof(Debug_SetBreakpoint)));
            lua.RegisterFunction("debug_removeBreakpoint", this, GetType().GetMethod(nameof(Debug_RemoveBreakpoint)));
            lua.RegisterFunction("debug_getBreakpointList", this, GetType().GetMethod(nameof(Debug_GetBreakpointList)));
            lua.RegisterFunction("debug_isDebugging", this, GetType().GetMethod(nameof(Debug_IsDebugging)));
        }

        /// <summary>
        /// Set a hardware breakpoint at the specified address.
        /// bpType: 0=execute, 1=write, 2=read/write
        /// size: 1, 2, 4, or 8 bytes
        /// </summary>
        public bool Debug_SetBreakpoint(object address, int bpType = 1, int size = 4)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                // This would integrate with the HardwareBreakpointManager
                // Placeholder for now
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Remove a breakpoint at the specified address.
        /// </summary>
        public bool Debug_RemoveBreakpoint(object address)
        {
            var process = _engine.ProcessAccess;
            if (process == null) return false;

            IntPtr addr = ToAddress(address);
            if (addr == IntPtr.Zero) return false;

            try
            {
                // This would integrate with the HardwareBreakpointManager
                // Placeholder for now
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get a list of all active breakpoints.
        /// </summary>
        public object[] Debug_GetBreakpointList()
        {
            // Placeholder - would return list of active breakpoints
            return Array.Empty<object>();
        }

        /// <summary>
        /// Check if the debugger is active.
        /// </summary>
        public bool Debug_IsDebugging()
        {
            // Placeholder - would check if debugging is active
            return false;
        }

        private IntPtr ToAddress(object addressObj)
        {
            if (addressObj == null) return IntPtr.Zero;

            if (addressObj is string addressStr)
            {
                addressStr = addressStr.Trim().Replace("0x", "").Replace("0X", "");
                if (long.TryParse(addressStr, System.Globalization.NumberStyles.HexNumber, null, out long addr))
                {
                    return new IntPtr(addr);
                }
                return IntPtr.Zero;
            }

            if (addressObj is long l) return new IntPtr(l);
            if (addressObj is int i) return new IntPtr(i);
            if (addressObj is double d) return new IntPtr((long)d);

            return IntPtr.Zero;
        }
    }
}
