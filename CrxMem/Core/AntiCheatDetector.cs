using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using Microsoft.Win32;

namespace CrxMem.Core
{
    /// <summary>
    /// Detects anti-cheat systems in processes and on the system
    /// </summary>
    public class AntiCheatDetector
    {
        public class AntiCheatInfo
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = ""; // Kernel, User, Hypervisor, Custom
            public string Severity { get; set; } = ""; // Low, Medium, High, Extreme
            public List<string> DetectedComponents { get; set; } = new();
            public string Description { get; set; } = "";
            public bool IsCustom { get; set; } = false; // Indicates homebrew/custom anti-cheat
            public string Version { get; set; } = "Unknown"; // Anti-cheat version if detected
        }

        public class DetectionResult
        {
            public List<AntiCheatInfo> DetectedAntiCheats { get; set; } = new();
            public bool HasKernelMode { get; set; }
            public bool HasUserMode { get; set; }
            public bool HasHypervisor { get; set; }
            public string OverallThreat { get; set; } = "None";
        }

        // Anti-cheat signatures
        private static readonly Dictionary<string, AntiCheatSignature> Signatures = new()
        {
            // Kernel-mode anti-cheats (most dangerous)
            ["EasyAntiCheat"] = new()
            {
                Name = "Easy Anti-Cheat",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Epic Games kernel-level anti-cheat with driver protection",
                Modules = new[] { "EasyAntiCheat.sys", "EasyAntiCheat_EOS.dll", "EasyAntiCheat.exe" },
                Services = new[] { "EasyAntiCheat" },
                Processes = new[] { "EasyAntiCheat.exe", "EasyAntiCheat_Setup.exe" }
            },

            ["BattlEye"] = new()
            {
                Name = "BattlEye",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Kernel-mode anti-cheat with strong memory protection",
                Modules = new[] { "BEDaisy.sys", "BEService.exe", "BEService_x64.exe" },
                Services = new[] { "BEService", "BEDaisy" },
                Processes = new[] { "BEService.exe", "BEService_x64.exe" }
            },

            ["XIGNCODE"] = new()
            {
                Name = "XIGNCODE3",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Korean anti-cheat with kernel driver",
                Modules = new[] { "xhunter1.sys", "XIGNCODE3.dll", "xhunter1.exe" },
                Services = new[] { "xhunter1" },
                Processes = new[] { "xhunter1.exe" }
            },

            ["nProtect"] = new()
            {
                Name = "nProtect GameGuard",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Invasive Korean anti-cheat with multiple drivers",
                Modules = new[] { "npggnt.des", "npggnt.sys", "npggsrv.exe", "GameGuard.des" },
                Services = new[] { "npggsvc" },
                Processes = new[] { "npggsvc.exe", "npggNT.des" }
            },

            ["Vanguard"] = new()
            {
                Name = "Riot Vanguard",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Riot Games kernel anti-cheat, runs at boot",
                Modules = new[] { "vgk.sys", "vgc.exe" },
                Services = new[] { "vgk", "vgc" },
                Processes = new[] { "vgc.exe" }
            },

            ["PunkBuster"] = new()
            {
                Name = "PunkBuster",
                Type = "Kernel",
                Severity = "High",
                Description = "Legacy anti-cheat with kernel components",
                Modules = new[] { "PnkBstrK.sys", "PnkBstrA.exe", "PnkBstrB.exe" },
                Services = new[] { "PnkBstrK", "PnkBstrA" },
                Processes = new[] { "PnkBstrA.exe", "PnkBstrB.exe" }
            },

            ["mhyprot"] = new()
            {
                Name = "mhyprot (Genshin/miHoYo)",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "miHoYo anti-cheat kernel driver",
                Modules = new[] { "mhyprot2.sys", "mhyprot3.sys" },
                Services = new[] { "mhyprot2", "mhyprot3" },
                Processes = Array.Empty<string>()
            },

            // Hypervisor-based (extremely dangerous)
            ["Byfron"] = new()
            {
                Name = "Byfron/Hyperion (Roblox)",
                Type = "Hypervisor",
                Severity = "Extreme",
                Description = "Hypervisor-level anti-cheat, very difficult to bypass",
                Modules = new[] { "hmpalert.dll", "RobloxPlayerBeta.exe" },
                Services = Array.Empty<string>(),
                Processes = new[] { "RobloxPlayerBeta.exe" },
                RegistryKeys = new[] { @"SOFTWARE\Roblox\Hyperion" }
            },

            // User-mode anti-cheats (less dangerous but still significant)
            ["VAC"] = new()
            {
                Name = "Valve Anti-Cheat (VAC)",
                Type = "User",
                Severity = "Medium",
                Description = "Steam user-mode anti-cheat with delayed bans",
                Modules = new[] { "steamservice.dll", "steam.dll", "tier0_s.dll" },
                Services = Array.Empty<string>(),
                Processes = new[] { "steam.exe", "steamservice.exe" }
            },

            ["FairFight"] = new()
            {
                Name = "FairFight",
                Type = "User",
                Severity = "Low",
                Description = "Server-side behavioral analysis",
                Modules = Array.Empty<string>(),
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["Denuvo"] = new()
            {
                Name = "Denuvo Anti-Tamper",
                Type = "User",
                Severity = "High",
                Description = "Anti-tamper protection, not strictly anti-cheat",
                Modules = new[] { "denuvo64.dll", "denuvo32.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["FACEIT"] = new()
            {
                Name = "FACEIT Anti-Cheat",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Kernel-level anti-cheat for competitive gaming",
                Modules = new[] { "FACEIT.sys", "FACEITService.exe" },
                Services = new[] { "FACEIT" },
                Processes = new[] { "FACEITService.exe", "FACEIT.exe" }
            },

            ["ESEA"] = new()
            {
                Name = "ESEA Anti-Cheat",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Invasive kernel anti-cheat",
                Modules = new[] { "ESEADriver2.sys" },
                Services = new[] { "ESEADriver2" },
                Processes = new[] { "ESEAClient.exe" }
            },

            ["Nexon"] = new()
            {
                Name = "Nexon Game Security",
                Type = "Kernel",
                Severity = "High",
                Description = "Nexon's anti-cheat system",
                Modules = new[] { "ngs.sys", "ngs.dll", "BlackCipher.aes" },
                Services = new[] { "ngs" },
                Processes = Array.Empty<string>()
            },

            // Additional commercial anti-cheats
            ["ACE"] = new()
            {
                Name = "Anti-Cheat Expert (ACE)",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Tencent's anti-cheat system used in PUBG and other games",
                Modules = new[] { "ACE-BASE.sys", "ACE-DRV64.sys", "SGuard64.dll", "TenSLX.sys", "TenRSafe2.sys" },
                Services = new[] { "ACE", "TenProtect" },
                Processes = new[] { "TenProtect.exe", "TPSVC.exe" }
            },

            ["Arbiter"] = new()
            {
                Name = "Arbiter Anti-Cheat",
                Type = "Kernel",
                Severity = "High",
                Description = "Anti-cheat for Paladins and Smite",
                Modules = new[] { "EasyAntiCheat.sys" }, // Uses EAC backend
                Services = new[] { "EasyAntiCheat" },
                Processes = Array.Empty<string>()
            },

            ["Zakynthos"] = new()
            {
                Name = "Zakynthos (VALORANT)",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Riot's server-side anti-cheat complementing Vanguard",
                Modules = Array.Empty<string>(),
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["Treyarch"] = new()
            {
                Name = "Treyarch Anti-Cheat",
                Type = "Kernel",
                Severity = "High",
                Description = "Call of Duty Treyarch anti-cheat",
                Modules = new[] { "atvi-audio.sys", "CodeTrackKrnl.sys" },
                Services = new[] { "atvi-audio" },
                Processes = Array.Empty<string>()
            },

            ["RICOCHET"] = new()
            {
                Name = "RICOCHET Anti-Cheat",
                Type = "Kernel",
                Severity = "Extreme",
                Description = "Activision's kernel-level anti-cheat for Call of Duty",
                Modules = new[] { "aswArPot.sys", "RicochetCore.dll" },
                Services = new[] { "RicochetAC" },
                Processes = new[] { "RicochetClient.exe" }
            },

            ["Warden"] = new()
            {
                Name = "Warden (Blizzard)",
                Type = "User",
                Severity = "Medium",
                Description = "Blizzard's user-mode anti-cheat for WoW and other games",
                Modules = new[] { "Warden.dll", "Warden64.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["SXEOS"] = new()
            {
                Name = "SXEOS Anti-Cheat",
                Type = "Kernel",
                Severity = "High",
                Description = "Anti-cheat used in various indie games",
                Modules = new[] { "SXEOS.sys", "SXEOS64.dll" },
                Services = new[] { "SXEOS" },
                Processes = new[] { "SXEOS.exe" }
            },

            ["AntiCheatEngine"] = new()
            {
                Name = "Anti-Cheat Engine (ARMA)",
                Type = "User",
                Severity = "Low",
                Description = "BattlEye predecessor for ARMA series",
                Modules = new[] { "AntiCheatEngine.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            // Custom/Homebrew anti-cheat patterns
            ["CustomCRC"] = new()
            {
                Name = "Custom CRC Integrity Check",
                Type = "Custom",
                Severity = "Low",
                Description = "Homebrew CRC-based integrity checking system",
                Modules = new[] { "integrity.dll", "crc32check.dll", "checksum.dll", "validation.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["CustomMemoryProtect"] = new()
            {
                Name = "Custom Memory Protection",
                Type = "Custom",
                Severity = "Medium",
                Description = "Homebrew memory scanner/protector",
                Modules = new[] { "memprotect.dll", "memguard.dll", "anticheat.dll", "protection.dll", "guard.dll" },
                Services = Array.Empty<string>(),
                Processes = new[] { "memprotect.exe", "memguard.exe", "anticheat.exe" }
            },

            ["CustomAntiDebug"] = new()
            {
                Name = "Custom Anti-Debug",
                Type = "Custom",
                Severity = "Low",
                Description = "Homebrew anti-debugger detection",
                Modules = new[] { "antidebug.dll", "debugdetect.dll", "nodbg.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["CustomHookDetect"] = new()
            {
                Name = "Custom Hook Detection",
                Type = "Custom",
                Severity = "Medium",
                Description = "Homebrew API hook/inline hook detector",
                Modules = new[] { "hookdetect.dll", "nohook.dll", "hookguard.dll", "iatprotect.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["CustomDriver"] = new()
            {
                Name = "Custom Kernel Driver Protection",
                Type = "Custom",
                Severity = "High",
                Description = "Homebrew kernel driver for game protection",
                Modules = new[] { "gameprotect.sys", "anticheatsys.sys", "customac.sys", "protect.sys", "guard.sys" },
                Services = new[] { "gameprotect", "anticheatsys", "customac" },
                Processes = Array.Empty<string>()
            },

            ["CustomScreenCapture"] = new()
            {
                Name = "Custom Screenshot Detection",
                Type = "Custom",
                Severity = "Low",
                Description = "Homebrew screen capture/overlay detector",
                Modules = new[] { "screencapdetect.dll", "overlayblock.dll", "antioverlay.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["CustomVMDetect"] = new()
            {
                Name = "Custom VM Detection",
                Type = "Custom",
                Severity = "Medium",
                Description = "Homebrew virtual machine/sandbox detector",
                Modules = new[] { "vmdetect.dll", "antivm.dll", "sandboxdetect.dll" },
                Services = Array.Empty<string>(),
                Processes = Array.Empty<string>()
            },

            ["CustomSignature"] = new()
            {
                Name = "Custom Signature Scanner",
                Type = "Custom",
                Severity = "Medium",
                Description = "Homebrew pattern/signature-based cheat detection",
                Modules = new[] { "sigscan.dll", "patternscan.dll", "cheatdetect.dll" },
                Services = Array.Empty<string>(),
                Processes = new[] { "sigscan.exe", "cheatdetect.exe" }
            }
        };

        private class AntiCheatSignature
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string Severity { get; set; } = "";
            public string Description { get; set; } = "";
            public string[] Modules { get; set; } = Array.Empty<string>();
            public string[] Services { get; set; } = Array.Empty<string>();
            public string[] Processes { get; set; } = Array.Empty<string>();
            public string[] RegistryKeys { get; set; } = Array.Empty<string>();
        }

        /// <summary>
        /// Detect anti-cheats in a specific process
        /// </summary>
        public static DetectionResult DetectInProcess(ProcessAccess process)
        {
            var result = new DetectionResult();

            try
            {
                var modules = process.GetModules();
                var moduleNames = modules.Select(m => m.ModuleName.ToLower()).ToHashSet();

                foreach (var kvp in Signatures)
                {
                    var sig = kvp.Value;
                    var detected = new List<string>();

                    // Check modules
                    foreach (var module in sig.Modules)
                    {
                        if (moduleNames.Contains(module.ToLower()))
                        {
                            detected.Add($"Module: {module}");
                        }
                    }

                    if (detected.Count > 0)
                    {
                        var acInfo = new AntiCheatInfo
                        {
                            Name = sig.Name,
                            Type = sig.Type,
                            Severity = sig.Severity,
                            Description = sig.Description,
                            DetectedComponents = detected,
                            IsCustom = sig.Type == "Custom",
                            Version = "Unknown"
                        };

                        result.DetectedAntiCheats.Add(acInfo);

                        UpdateThreatFlags(result, sig.Type);
                    }
                }

                // Add behavioral detection for suspicious patterns
                DetectSuspiciousBehavior(process, result);
            }
            catch { }

            UpdateOverallThreat(result);
            return result;
        }

        /// <summary>
        /// Detect anti-cheats system-wide
        /// </summary>
        public static DetectionResult DetectSystemWide()
        {
            var result = new DetectionResult();

            try
            {
                // Get all running processes
                var runningProcesses = Process.GetProcesses().Select(p => p.ProcessName.ToLower()).ToHashSet();

                // Get all services
                var services = GetServices();

                // Get all drivers
                var drivers = GetDrivers();

                foreach (var kvp in Signatures)
                {
                    var sig = kvp.Value;
                    var detected = new List<string>();

                    // Check processes
                    foreach (var proc in sig.Processes)
                    {
                        if (runningProcesses.Contains(Path.GetFileNameWithoutExtension(proc).ToLower()))
                        {
                            detected.Add($"Process: {proc}");
                        }
                    }

                    // Check services
                    foreach (var svc in sig.Services)
                    {
                        if (services.Contains(svc.ToLower()))
                        {
                            detected.Add($"Service: {svc}");
                        }
                    }

                    // Check drivers
                    foreach (var drv in sig.Modules)
                    {
                        if (drv.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
                        {
                            if (drivers.Contains(drv.ToLower()))
                            {
                                detected.Add($"Driver: {drv}");
                            }
                        }
                    }

                    // Check registry
                    foreach (var regKey in sig.RegistryKeys)
                    {
                        if (CheckRegistryKey(regKey))
                        {
                            detected.Add($"Registry: {regKey}");
                        }
                    }

                    if (detected.Count > 0)
                    {
                        var acInfo = new AntiCheatInfo
                        {
                            Name = sig.Name,
                            Type = sig.Type,
                            Severity = sig.Severity,
                            Description = sig.Description,
                            DetectedComponents = detected,
                            IsCustom = sig.Type == "Custom",
                            Version = "Unknown"
                        };

                        result.DetectedAntiCheats.Add(acInfo);

                        UpdateThreatFlags(result, sig.Type);
                    }
                }
            }
            catch { }

            UpdateOverallThreat(result);
            return result;
        }

        private static HashSet<string> GetServices()
        {
            var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (ServiceController service in ServiceController.GetServices())
                {
                    services.Add(service.ServiceName.ToLower());
                }
            }
            catch { }

            return services;
        }

        private static HashSet<string> GetDrivers()
        {
            var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string driverPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                if (Directory.Exists(driverPath))
                {
                    foreach (string file in Directory.GetFiles(driverPath, "*.sys"))
                    {
                        drivers.Add(Path.GetFileName(file).ToLower());
                    }
                }

                // Also check via WMI
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SystemDriver"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string? pathName = obj["PathName"]?.ToString();
                        if (!string.IsNullOrEmpty(pathName))
                        {
                            drivers.Add(Path.GetFileName(pathName).ToLower());
                        }
                    }
                }
            }
            catch { }

            return drivers;
        }

        private static bool CheckRegistryKey(string keyPath)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void UpdateThreatFlags(DetectionResult result, string type)
        {
            switch (type)
            {
                case "Kernel":
                    result.HasKernelMode = true;
                    break;
                case "User":
                    result.HasUserMode = true;
                    break;
                case "Hypervisor":
                    result.HasHypervisor = true;
                    break;
                case "Custom":
                    result.HasUserMode = true; // Custom anti-cheats typically user-mode
                    break;
            }
        }

        private static void UpdateOverallThreat(DetectionResult result)
        {
            if (result.HasHypervisor)
            {
                result.OverallThreat = "Extreme (Hypervisor)";
            }
            else if (result.HasKernelMode)
            {
                result.OverallThreat = "High (Kernel-Mode)";
            }
            else if (result.HasUserMode)
            {
                result.OverallThreat = "Medium (User-Mode)";
            }
            else
            {
                result.OverallThreat = result.DetectedAntiCheats.Count > 0 ? "Low" : "None";
            }
        }

        /// <summary>
        /// Detect suspicious behavioral patterns that may indicate custom anti-cheat
        /// </summary>
        private static void DetectSuspiciousBehavior(ProcessAccess process, DetectionResult result)
        {
            try
            {
                var modules = process.GetModules();
                var detectedBehaviors = new List<string>();

                // Check for suspicious DLL names that might be custom anti-cheat
                var suspiciousPatterns = new[]
                {
                    "protect", "guard", "anticheat", "anti-cheat", "security",
                    "integrity", "validation", "checker", "monitor", "detector",
                    "shield", "defense", "secure", "verify"
                };

                foreach (var module in modules)
                {
                    string moduleLower = module.ModuleName.ToLower();

                    // Skip system modules
                    if (moduleLower.StartsWith("kernel32") || moduleLower.StartsWith("ntdll") ||
                        moduleLower.StartsWith("user32") || moduleLower.StartsWith("advapi32") ||
                        moduleLower.StartsWith("msvcr") || moduleLower.StartsWith("ucrtbase"))
                        continue;

                    foreach (var pattern in suspiciousPatterns)
                    {
                        if (moduleLower.Contains(pattern) && !moduleLower.Contains("microsoft") &&
                            !moduleLower.Contains("windows"))
                        {
                            detectedBehaviors.Add($"Suspicious Module: {module.ModuleName}");
                            break;
                        }
                    }
                }

                // Check for unusual number of monitoring threads (possible anti-cheat behavior)
                int threadCount = process.Target.Threads.Count;
                if (threadCount > 50)
                {
                    detectedBehaviors.Add($"High Thread Count: {threadCount} threads (possible monitoring)");
                }

                // If we found suspicious behaviors, add a generic custom anti-cheat detection
                if (detectedBehaviors.Count > 0)
                {
                    // Check if not already detected by known signatures
                    bool alreadyDetected = result.DetectedAntiCheats.Any(ac =>
                        detectedBehaviors.Any(b => ac.DetectedComponents.Contains(b)));

                    if (!alreadyDetected)
                    {
                        var behavioralAC = new AntiCheatInfo
                        {
                            Name = "Potential Custom Anti-Cheat (Behavioral)",
                            Type = "Custom",
                            Severity = "Medium",
                            Description = "Detected suspicious patterns suggesting custom/homebrew anti-cheat implementation",
                            DetectedComponents = detectedBehaviors,
                            IsCustom = true,
                            Version = "Unknown"
                        };

                        result.DetectedAntiCheats.Add(behavioralAC);
                        result.HasUserMode = true;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Get all anti-cheat signatures (for display purposes)
        /// </summary>
        public static Dictionary<string, string> GetAllSignatures()
        {
            return Signatures.ToDictionary(
                kvp => kvp.Value.Name,
                kvp => $"{kvp.Value.Type} - {kvp.Value.Severity}"
            );
        }
    }
}
