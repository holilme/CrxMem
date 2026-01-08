using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CrxMem.Core
{
    /// <summary>
    /// Detects LUA runtime in target processes
    /// </summary>
    public class LuaDetector
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        // Known LUA module names to search for
        private static readonly string[] LuaModulePatterns = new[]
        {
            "lua51.dll", "lua5.1.dll",
            "lua52.dll", "lua5.2.dll",
            "lua53.dll", "lua5.3.dll",
            "lua54.dll", "lua5.4.dll",
            "luajit.dll",
            "lua.dll"
        };

        // Core LUA API functions to verify
        private static readonly string[] CoreLuaFunctions = new[]
        {
            "lua_newstate",
            "lua_close",
            "lua_gettop",
            "lua_settop",
            "lua_pcall",
            "luaL_newstate",
            "luaL_loadstring"
        };

        public class LuaInfo
        {
            public bool IsDetected { get; set; }
            public string? ModuleName { get; set; }
            public IntPtr ModuleBase { get; set; }
            public int ModuleSize { get; set; }
            public LuaVersion Version { get; set; }
            public Dictionary<string, IntPtr> FunctionAddresses { get; set; } = new();
        }

        public enum LuaVersion
        {
            Unknown = 0,
            Lua51 = 51,
            Lua52 = 52,
            Lua53 = 53,
            Lua54 = 54,
            LuaJIT = 100
        }

        /// <summary>
        /// Detect LUA in the target process
        /// </summary>
        public static LuaInfo Detect(ProcessAccess process)
        {
            var luaInfo = new LuaInfo();

            try
            {
                var modules = process.GetModules();

                // First pass: Search for known LUA DLL names (fast)
                foreach (var module in modules)
                {
                    string moduleName = module.ModuleName.ToLower();

                    // Check if module name matches any known LUA DLL pattern
                    foreach (var pattern in LuaModulePatterns)
                    {
                        if (moduleName.Contains(pattern.Replace(".dll", "")))
                        {
                            // Potential LUA module found - verify by checking exports
                            if (VerifyLuaModule(module))
                            {
                                luaInfo.IsDetected = true;
                                luaInfo.ModuleName = module.ModuleName;
                                luaInfo.ModuleBase = module.BaseAddress;
                                luaInfo.ModuleSize = module.Size;
                                luaInfo.Version = DetectLuaVersion(module.ModuleName);

                                // Resolve function addresses
                                ResolveLuaFunctions(module, luaInfo);

                                return luaInfo;
                            }
                        }
                    }
                }

                // Second pass: Scan ALL modules for LUA function signatures (slower but comprehensive)
                // This catches embedded LUA, custom implementations, and renamed DLLs
                foreach (var module in modules)
                {
                    if (VerifyLuaModule(module))
                    {
                        luaInfo.IsDetected = true;
                        luaInfo.ModuleName = module.ModuleName;
                        luaInfo.ModuleBase = module.BaseAddress;
                        luaInfo.ModuleSize = module.Size;
                        luaInfo.Version = DetectLuaVersionFromExports(module);

                        // Resolve function addresses
                        ResolveLuaFunctions(module, luaInfo);

                        return luaInfo;
                    }
                }
            }
            catch
            {
                // Failed to detect LUA
            }

            return luaInfo;
        }

        /// <summary>
        /// Verify if a module is actually LUA by checking for core exports
        /// </summary>
        private static bool VerifyLuaModule(ModuleInfo module)
        {
            try
            {
                // Try to resolve core LUA functions
                int foundCount = 0;
                foreach (var funcName in CoreLuaFunctions)
                {
                    IntPtr funcAddr = GetProcAddress(module.BaseAddress, funcName);
                    if (funcAddr != IntPtr.Zero)
                    {
                        foundCount++;
                    }
                }

                // If we found at least 4 core functions, it's likely LUA
                return foundCount >= 4;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detect LUA version from module name
        /// </summary>
        private static LuaVersion DetectLuaVersion(string moduleName)
        {
            moduleName = moduleName.ToLower();

            if (moduleName.Contains("luajit"))
                return LuaVersion.LuaJIT;
            if (moduleName.Contains("54"))
                return LuaVersion.Lua54;
            if (moduleName.Contains("53"))
                return LuaVersion.Lua53;
            if (moduleName.Contains("52"))
                return LuaVersion.Lua52;
            if (moduleName.Contains("51") || moduleName.Contains("5.1"))
                return LuaVersion.Lua51;

            return LuaVersion.Unknown;
        }

        /// <summary>
        /// Detect LUA version from exported functions
        /// Different LUA versions have different function signatures
        /// </summary>
        private static LuaVersion DetectLuaVersionFromExports(ModuleInfo module)
        {
            try
            {
                // Check for version-specific functions

                // Lua 5.4 specific
                if (GetProcAddress(module.BaseAddress, "lua_resetthread") != IntPtr.Zero ||
                    GetProcAddress(module.BaseAddress, "lua_closeslot") != IntPtr.Zero)
                {
                    return LuaVersion.Lua54;
                }

                // Lua 5.3 specific
                if (GetProcAddress(module.BaseAddress, "lua_isinteger") != IntPtr.Zero ||
                    GetProcAddress(module.BaseAddress, "lua_rotate") != IntPtr.Zero)
                {
                    return LuaVersion.Lua53;
                }

                // Lua 5.2 specific
                if (GetProcAddress(module.BaseAddress, "lua_compare") != IntPtr.Zero ||
                    GetProcAddress(module.BaseAddress, "lua_arith") != IntPtr.Zero)
                {
                    return LuaVersion.Lua52;
                }

                // LuaJIT specific
                if (GetProcAddress(module.BaseAddress, "luaJIT_setmode") != IntPtr.Zero ||
                    GetProcAddress(module.BaseAddress, "luaJIT_version_2_1_0") != IntPtr.Zero)
                {
                    return LuaVersion.LuaJIT;
                }

                // Default to 5.1 if basic functions exist but no version-specific ones
                if (GetProcAddress(module.BaseAddress, "lua_gettop") != IntPtr.Zero)
                {
                    return LuaVersion.Lua51;
                }
            }
            catch
            {
                // Error detecting version
            }

            return LuaVersion.Unknown;
        }

        /// <summary>
        /// Resolve addresses of important LUA API functions
        /// </summary>
        private static void ResolveLuaFunctions(ModuleInfo module, LuaInfo luaInfo)
        {
            // Extended list of LUA functions to resolve
            var functionsToResolve = new[]
            {
                // State management
                "lua_newstate", "lua_close", "luaL_newstate",
                // Stack manipulation
                "lua_gettop", "lua_settop", "lua_pushvalue", "lua_remove",
                // Push functions
                "lua_pushnil", "lua_pushnumber", "lua_pushinteger", "lua_pushstring",
                "lua_pushboolean", "lua_pushcclosure", "lua_pushlightuserdata",
                // Access functions
                "lua_tonumber", "lua_tointeger", "lua_toboolean", "lua_tostring",
                "lua_touserdata", "lua_type", "lua_typename",
                // Load and call
                "luaL_loadstring", "luaL_loadbuffer", "luaL_loadfile",
                "lua_pcall", "lua_call",
                // Table manipulation
                "lua_gettable", "lua_settable", "lua_getfield", "lua_setfield",
                "lua_rawget", "lua_rawset", "lua_rawgeti", "lua_rawseti",
                // Global access
                "lua_getglobal", "lua_setglobal",
                // Metatables
                "lua_getmetatable", "lua_setmetatable",
                // Auxiliary library
                "luaL_ref", "luaL_unref", "luaL_error", "luaL_where"
            };

            foreach (var funcName in functionsToResolve)
            {
                try
                {
                    IntPtr funcAddr = GetProcAddress(module.BaseAddress, funcName);
                    if (funcAddr != IntPtr.Zero)
                    {
                        luaInfo.FunctionAddresses[funcName] = funcAddr;
                    }
                }
                catch
                {
                    // Function not found - continue
                }
            }
        }

        /// <summary>
        /// Get a user-friendly version string
        /// </summary>
        public static string GetVersionString(LuaVersion version)
        {
            return version switch
            {
                LuaVersion.Lua51 => "Lua 5.1",
                LuaVersion.Lua52 => "Lua 5.2",
                LuaVersion.Lua53 => "Lua 5.3",
                LuaVersion.Lua54 => "Lua 5.4",
                LuaVersion.LuaJIT => "LuaJIT",
                _ => "Unknown"
            };
        }
    }
}
