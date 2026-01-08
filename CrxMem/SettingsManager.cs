using System;
using Microsoft.Win32;

namespace CrxMem
{
    public static class SettingsManager
    {
        private const string RegistryPath = @"Software\CrxMem";

        // General Settings
        public static int UpdateInterval
        {
            get => GetInt("UpdateInterval", 500);
            set => SetInt("UpdateInterval", value);
        }

        public static int FreezeInterval
        {
            get => GetInt("FreezeInterval", 100);
            set => SetInt("FreezeInterval", value);
        }

        public static int FoundListUpdateInterval
        {
            get => GetInt("FoundListUpdateInterval", 1000);
            set => SetInt("FoundListUpdateInterval", value);
        }

        public static bool ShowValuesAsSigned
        {
            get => GetBool("ShowValuesAsSigned", false);
            set => SetBool("ShowValuesAsSigned", value);
        }

        public static bool SimpleCopyPaste
        {
            get => GetBool("SimpleCopyPaste", false);
            set => SetBool("SimpleCopyPaste", value);
        }

        public static bool SaveWindowPositions
        {
            get => GetBool("SaveWindowPositions", true);
            set => SetBool("SaveWindowPositions", value);
        }

        public static string AutoAttachProcess
        {
            get => GetString("AutoAttachProcess", "");
            set => SetString("AutoAttachProcess", value);
        }

        public static bool AlwaysAutoAttach
        {
            get => GetBool("AlwaysAutoAttach", false);
            set => SetBool("AlwaysAutoAttach", value);
        }

        public static bool FastAutoAttach
        {
            get => GetBool("FastAutoAttach", true);
            set => SetBool("FastAutoAttach", value);
        }

        public static bool UseWmiProcessWatch
        {
            get => GetBool("UseWmiProcessWatch", true);
            set => SetBool("UseWmiProcessWatch", value);
        }

        public static int FastAutoAttachInterval
        {
            get => GetInt("FastAutoAttachInterval", 1); // 1ms default
            set => SetInt("FastAutoAttachInterval", value);
        }

        public static bool AutoLoadDriver
        {
            get => GetBool("AutoLoadDriver", false);
            set => SetBool("AutoLoadDriver", value);
        }

        public static bool AutoInjectVEH
        {
            get => GetBool("AutoInjectVEH", false);
            set => SetBool("AutoInjectVEH", value);
        }

        /// <summary>
        /// Immediately suspend process upon fast auto-attach detection (before anti-cheat initializes)
        /// </summary>
        public static bool FastSuspendOnAttach
        {
            get => GetBool("FastSuspendOnAttach", false);
            set => SetBool("FastSuspendOnAttach", value);
        }

        /// <summary>
        /// Interval in milliseconds to re-suspend the process while attached (0 = disabled, only suspend once on attach)
        /// </summary>
        public static int SuspendInterval
        {
            get => GetInt("SuspendInterval", 0);
            set => SetInt("SuspendInterval", value);
        }

        /// <summary>
        /// Multiplier for UI update intervals when using kernel driver (higher = less lag)
        /// 1 = normal speed, 2 = half as often, 3 = third as often, etc.
        /// </summary>
        public static int KernelModeUpdateMultiplier
        {
            get => GetInt("KernelModeUpdateMultiplier", 2);
            set => SetInt("KernelModeUpdateMultiplier", value);
        }

        /// <summary>
        /// Cache lifetime in milliseconds for kernel mode reads
        /// Higher = faster but may show stale values
        /// </summary>
        public static int KernelCacheLifetimeMs
        {
            get => GetInt("KernelCacheLifetimeMs", 50);
            set => SetInt("KernelCacheLifetimeMs", value);
        }

        public static bool AskToClearListOnNewProcess
        {
            get => GetBool("AskToClearListOnNewProcess", true);
            set => SetBool("AskToClearListOnNewProcess", value);
        }

        public static bool AlwaysLaunchAsAdmin
        {
            get => GetBool("AlwaysLaunchAsAdmin", false);
            set => SetBool("AlwaysLaunchAsAdmin", value);
        }

        // Theme Settings
        public static bool DarkMode
        {
            get => GetBool("DarkMode", true); // Dark mode on by default
            set => SetBool("DarkMode", value);
        }

        // Scan Settings
        public static int ScanThreadPriority
        {
            get => GetInt("ScanThreadPriority", 2); // Normal
            set => SetInt("ScanThreadPriority", value);
        }

        public static int ScanBufferSize
        {
            get => GetInt("ScanBufferSize", 4096);
            set => SetInt("ScanBufferSize", value);
        }

        public static bool FastScanByDefault
        {
            get => GetBool("FastScanByDefault", false);
            set => SetBool("FastScanByDefault", value);
        }

        public static bool PauseWhileScanning
        {
            get => GetBool("PauseWhileScanning", false);
            set => SetBool("PauseWhileScanning", value);
        }

        public static bool ScanMemPrivate
        {
            get => GetBool("ScanMemPrivate", true);
            set => SetBool("ScanMemPrivate", value);
        }

        public static bool ScanMemImage
        {
            get => GetBool("ScanMemImage", true);
            set => SetBool("ScanMemImage", value);
        }

        public static bool ScanMemMapped
        {
            get => GetBool("ScanMemMapped", false);
            set => SetBool("ScanMemMapped", value);
        }

        public static bool SkipPageNoCache
        {
            get => GetBool("SkipPageNoCache", false);
            set => SetBool("SkipPageNoCache", value);
        }

        public static bool SkipPageWriteCombine
        {
            get => GetBool("SkipPageWriteCombine", false);
            set => SetBool("SkipPageWriteCombine", value);
        }

        // Hotkey Settings (stored as key code integers)
        public static int HotkeySpeedhack
        {
            get => GetInt("HotkeySpeedhack", 0); // 0 = none
            set => SetInt("HotkeySpeedhack", value);
        }

        public static int HotkeyFreezeAll
        {
            get => GetInt("HotkeyFreezeAll", 0);
            set => SetInt("HotkeyFreezeAll", value);
        }

        public static int HotkeyUnfreezeAll
        {
            get => GetInt("HotkeyUnfreezeAll", 0);
            set => SetInt("HotkeyUnfreezeAll", value);
        }

        public static int HotkeyNewScan
        {
            get => GetInt("HotkeyNewScan", 0);
            set => SetInt("HotkeyNewScan", value);
        }

        public static int HotkeyNextScan
        {
            get => GetInt("HotkeyNextScan", 0);
            set => SetInt("HotkeyNextScan", value);
        }

        public static int HotkeyUndoScan
        {
            get => GetInt("HotkeyUndoScan", 0);
            set => SetInt("HotkeyUndoScan", value);
        }

        /// <summary>
        /// Get all hotkeys as a dictionary (function ID -> key code)
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, int> GetAllHotkeys()
        {
            var hotkeys = new System.Collections.Generic.Dictionary<string, int>();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath + @"\Hotkeys");
                if (key != null)
                {
                    foreach (var valueName in key.GetValueNames())
                    {
                        if (key.GetValue(valueName) is int keyCode)
                        {
                            hotkeys[valueName] = keyCode;
                        }
                    }
                }
            }
            catch { }
            return hotkeys;
        }

        /// <summary>
        /// Set all hotkeys from a dictionary (function ID -> key code)
        /// </summary>
        public static void SetAllHotkeys(System.Collections.Generic.Dictionary<string, int> hotkeys)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath + @"\Hotkeys");
                if (key != null)
                {
                    foreach (var kvp in hotkeys)
                    {
                        key.SetValue(kvp.Key, kvp.Value, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Get a specific hotkey by function ID
        /// </summary>
        public static int GetHotkey(string functionId)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath + @"\Hotkeys");
                return key?.GetValue(functionId) is int value ? value : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Save settings (registry writes are immediate, this is for API consistency)
        /// </summary>
        public static void Save()
        {
            // Registry writes are immediate, no action needed
        }

        // Helper methods
        private static int GetInt(string name, int defaultValue)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                return key?.GetValue(name) is int value ? value : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static void SetInt(string name, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static bool GetBool(string name, bool defaultValue)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                return key?.GetValue(name) is int value ? value != 0 : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static void SetBool(string name, bool value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static string GetString(string name, string defaultValue)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                return key?.GetValue(name) as string ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static void SetString(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(name, value, RegistryValueKind.String);
            }
            catch { }
        }
    }
}
