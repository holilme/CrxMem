using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrxMem.Core
{
    /// <summary>
    /// Event args for when a breakpoint is hit
    /// </summary>
    public class BreakpointHitEventArgs : EventArgs
    {
        public ulong InstructionAddress { get; set; }
        public uint ThreadId { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Monitors a memory address for reads/writes using hardware breakpoints (DR0-DR3).
    /// Uses VEH (Vectored Exception Handler) injection to catch single-step exceptions.
    /// The target process continues running normally while hits are captured.
    /// </summary>
    public class DebugMonitor : IDisposable
    {
        private readonly ProcessAccess _process;
        private readonly HardwareBreakpointManager _breakpointManager;
        private Thread? _pollingThread;
        private volatile bool _running;
        private int _breakpointSlot = -1;
        private ulong _watchAddress;
        private int _watchSize = 4;
        private bool _disposed;
        private bool _vehInjected;
        private VEHSharedMemory? _sharedMem;

        // PAGE_GUARD mode state
        private ulong _pageBase;
        private uint _originalProtection;
        private const uint PAGE_GUARD = 0x100;
        private const int PAGE_SIZE = 4096;

        // For calling functions in the injected DLL
        private IntPtr _dllBase;
        private IntPtr _initializeVEH;
        private IntPtr _uninitializeVEH;
        private IntPtr _setBreakpoint;
        private IntPtr _clearBreakpoint;
        private IntPtr _refreshBreakpoints;

        // Periodic refresh counter
        private int _pollCount;

        public event EventHandler<BreakpointHitEventArgs>? BreakpointHit;
        public event EventHandler? MonitoringStopped;

        public bool IsMonitoring => _running;
        public ulong WatchAddress => _watchAddress;
        public string LastError { get; private set; } = "";
        public bool IsUsingHardwareBreakpoint { get; private set; } = false;

        /// <summary>
        /// Get the current hit count from shared memory
        /// </summary>
        public int GetHitCount()
        {
            if (_sharedMem == null)
                return 0;
            return _sharedMem.GetHitCount();
        }

        public DebugMonitor(ProcessAccess process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            _breakpointManager = new HardwareBreakpointManager(process);
        }

        /// <summary>
        /// Determines if an address is dynamic (heap/stack) or static (module).
        /// Dynamic addresses should use hardware breakpoints for reliability.
        /// </summary>
        private bool IsDynamicAddress(ulong address)
        {
            try
            {
                var modules = _process.GetModules(false); // Don't force refresh
                foreach (var mod in modules)
                {
                    ulong modBase = (ulong)mod.BaseAddress.ToInt64();
                    ulong modEnd = modBase + (ulong)mod.Size;
                    if (address >= modBase && address < modEnd)
                    {
                        System.Diagnostics.Debug.WriteLine($"Address 0x{address:X} is in module {mod.ModuleName} - using PAGE_GUARD");
                        return false; // Static module address
                    }
                }
                System.Diagnostics.Debug.WriteLine($"Address 0x{address:X} is dynamic (heap/stack) - using Hardware Breakpoint");
                return true; // Dynamic heap/stack address
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IsDynamicAddress check failed: {ex.Message} - defaulting to PAGE_GUARD");
                return false; // Default to PAGE_GUARD on error
            }
        }

        /// <summary>
        /// Starts monitoring the specified address for memory access.
        /// Automatically selects Hardware Breakpoint mode for dynamic (heap/stack) addresses
        /// and PAGE_GUARD mode for static (module) addresses.
        /// </summary>
        /// <param name="address">Address to monitor</param>
        /// <param name="writeOnly">True to only monitor writes, false to monitor all access (reads + writes)</param>
        /// <param name="size">Size of the memory region to monitor (1, 2, 4, or 8 bytes)</param>
        public bool StartMonitoring(ulong address, bool writeOnly, int size = 4)
        {
            LastError = "";
            IsUsingHardwareBreakpoint = false;

            if (_running)
            {
                LastError = "Already running";
                return false;
            }

            if (_process?.Target == null)
            {
                LastError = "No target process";
                return false;
            }

            // Auto-select monitoring mode:
            // 1. "Write only" monitoring ALWAYS uses hardware breakpoints (DR7 can filter writes precisely)
            // 2. Dynamic addresses (heap/stack) use hardware breakpoints (PAGE_GUARD unreliable)
            // 3. Static addresses with "all access" can use PAGE_GUARD
            if (writeOnly)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-selecting Hardware Breakpoint mode for write-only monitoring at 0x{address:X}");
                return StartMonitoringWithHardwareBreakpoint(address, writeOnly, size);
            }

            if (IsDynamicAddress(address))
            {
                System.Diagnostics.Debug.WriteLine($"Auto-selecting Hardware Breakpoint mode for dynamic address 0x{address:X}");
                return StartMonitoringWithHardwareBreakpoint(address, writeOnly, size);
            }

            _watchAddress = address;
            _watchSize = size;

            // Calculate the page base (4KB alignment)
            _pageBase = address & ~0xFFFUL;

            // Create shared memory for communication
            _sharedMem = new VEHSharedMemory();
            if (!_sharedMem.Create())
            {
                LastError = "Failed to create shared memory";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }

            // IMPORTANT: Do NOT enable PAGE_GUARD mode yet!
            // Just set the watch address and size. Leave UsePageGuard=0 until we're fully set up.
            // This prevents the VEH handler from trying to handle exceptions before we're ready.
            _sharedMem.Configure(address, writeOnly ? 1 : 3, size, 0);

            // Inject VEH DLL to handle PAGE_GUARD exceptions
            if (!TryVEHInjection(address, writeOnly, size))
            {
                System.Diagnostics.Debug.WriteLine($"VEH injection failed: {LastError}");
                _sharedMem.Dispose();
                _sharedMem = null;
                return false;
            }

            _vehInjected = true;

            // Query original protection BEFORE setting PAGE_GUARD
            MEMORY_BASIC_INFORMATION mbi = new MEMORY_BASIC_INFORMATION();
            if (VirtualQueryEx(_process.Handle, (IntPtr)_pageBase, ref mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
            {
                LastError = $"VirtualQueryEx failed: {Marshal.GetLastWin32Error()}";
                System.Diagnostics.Debug.WriteLine(LastError);
                _sharedMem.Dispose();
                _sharedMem = null;
                return false;
            }
            _originalProtection = mbi.Protect;
            System.Diagnostics.Debug.WriteLine($"Original protection: 0x{_originalProtection:X}");

            // Don't add PAGE_GUARD if the page already has special protections
            if ((_originalProtection & 0x1) != 0) // PAGE_NOACCESS
            {
                LastError = "Cannot set PAGE_GUARD on PAGE_NOACCESS page.\n" +
                           "This address may be unmapped or protected.\n" +
                           "Try using Hardware Breakpoint mode instead (DR0-DR7).";
                System.Diagnostics.Debug.WriteLine(LastError);
                _sharedMem.Dispose();
                _sharedMem = null;
                return false;
            }

            // NOW configure PAGE_GUARD mode with the correct original protection
            // The VEH handler needs this to properly reapply PAGE_GUARD after single-step
            _sharedMem.ConfigurePageGuard(address, _pageBase, PAGE_SIZE, size, _originalProtection);

            // Small delay to ensure DLL sees the updated shared memory values
            // This prevents race conditions where PAGE_GUARD fires before config is complete
            Thread.Sleep(10);

            // Enable monitoring BEFORE setting the page guard to ensure we catch the very first hit
            _sharedMem.SetActive(true);

            // Now set PAGE_GUARD on the memory page
            if (!SetPageGuard())
            {
                LastError = $"Failed to set PAGE_GUARD on page 0x{_pageBase:X}";
                System.Diagnostics.Debug.WriteLine(LastError);
                _sharedMem.Dispose();
                _sharedMem = null;
                return false;
            }

            _running = true;

            // Start polling thread to read hits and re-apply PAGE_GUARD
            _pollingThread = new Thread(PollHits)
            {
                Name = "DebugMonitor_Poll",
                IsBackground = true,
                Priority = ThreadPriority.Normal
            };
            _pollingThread.Start();

            return true;
        }

        /// <summary>
        /// Starts monitoring using hardware breakpoints (DR0-DR3).
        /// More reliable than PAGE_GUARD for stack/heap addresses but limited to 4 simultaneous watches.
        /// </summary>
        public bool StartMonitoringWithHardwareBreakpoint(ulong address, bool writeOnly, int size = 4)
        {
            LastError = "";
            IsUsingHardwareBreakpoint = true;

            if (_running)
            {
                LastError = "Already running";
                return false;
            }

            if (_process?.Target == null)
            {
                LastError = "No target process";
                return false;
            }

            _watchAddress = address;
            _watchSize = size;

            // Get a free hardware breakpoint slot
            int slot = _breakpointManager.GetFreeSlot();
            if (slot == -1)
            {
                LastError = "No free hardware breakpoint slots (max 4)";
                return false;
            }
            _breakpointSlot = slot;

            // Determine breakpoint size
            var bpSize = size switch
            {
                1 => HardwareBreakpointManager.BreakpointSize.Byte1,
                2 => HardwareBreakpointManager.BreakpointSize.Byte2,
                8 => HardwareBreakpointManager.BreakpointSize.Byte8,
                _ => HardwareBreakpointManager.BreakpointSize.Byte4
            };

            // Determine breakpoint type
            var bpType = writeOnly
                ? HardwareBreakpointManager.BreakpointType.Write
                : HardwareBreakpointManager.BreakpointType.ReadWrite;

            // Create shared memory for communication
            _sharedMem = new VEHSharedMemory();
            if (!_sharedMem.Create())
            {
                LastError = "Failed to create shared memory for hardware breakpoint mode";
                return false;
            }

            // Configure for hardware breakpoint mode (UsePageGuard = 0)
            _sharedMem.Configure(address, writeOnly ? 1 : 3, size, slot);

            // Inject VEH DLL to catch single-step exceptions
            if (!TryVEHInjection(address, writeOnly, size))
            {
                _sharedMem.Dispose();
                _sharedMem = null;
                return false;
            }
            _vehInjected = true;

            // Set the hardware breakpoint using the DLL's function (runs inside target process)
            // This is more reliable than setting from outside, especially for 32-bit processes
            if (!CallRemoteSetHardwareBreakpoint(address, writeOnly, size))
            {
                // Fallback to external method if remote call fails
                System.Diagnostics.Debug.WriteLine("CallRemoteSetHardwareBreakpoint failed, trying external method");
                if (!_breakpointManager.SetBreakpoint(slot, address, bpType, bpSize))
                {
                    LastError = "Failed to set hardware breakpoint on threads";
                    _sharedMem.Dispose();
                    _sharedMem = null;
                    return false;
                }
            }

            _sharedMem.SetActive(true);
            _running = true;

            // Start polling thread
            _pollingThread = new Thread(PollHits)
            {
                Name = "DebugMonitor_HWBPPoll",
                IsBackground = true,
                Priority = ThreadPriority.Normal
            };
            _pollingThread.Start();

            return true;
        }

        /// <summary>
        /// Sets PAGE_GUARD protection on the watched page.
        /// Note: _originalProtection must already be set before calling this.
        /// </summary>
        private bool SetPageGuard()
        {
            if (_process?.Handle == IntPtr.Zero)
                return false;

            // Add PAGE_GUARD flag to the original protection
            uint newProtection = _originalProtection | PAGE_GUARD;

            uint oldProtect;
            if (!VirtualProtectEx(_process.Handle, (IntPtr)_pageBase, (UIntPtr)PAGE_SIZE, newProtection, out oldProtect))
            {
                int error = Marshal.GetLastWin32Error();
                LastError = $"Failed to set PAGE_GUARD on page 0x{_pageBase:X8}.\n" +
                           $"Win32 Error: {error}\n" +
                           $"This may be a stack/heap address that cannot use PAGE_GUARD.\n" +
                           $"Try Hardware Breakpoint mode instead (limited to 4 addresses).";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"PAGE_GUARD set on 0x{_pageBase:X}, old protect: 0x{oldProtect:X}");
            return true;
        }

        /// <summary>
        /// Re-applies PAGE_GUARD after it was triggered
        /// </summary>
        private void ReapplyPageGuard()
        {
            if (_process?.Handle == IntPtr.Zero || !_running)
                return;

            uint newProtection = _originalProtection | PAGE_GUARD;
            uint oldProtect;

            if (!VirtualProtectEx(_process.Handle, (IntPtr)_pageBase, (UIntPtr)PAGE_SIZE, newProtection, out oldProtect))
            {
                System.Diagnostics.Debug.WriteLine($"Failed to re-apply PAGE_GUARD: {Marshal.GetLastWin32Error()}");
            }
        }

        /// <summary>
        /// Removes PAGE_GUARD protection
        /// </summary>
        private void RemovePageGuard()
        {
            if (_process?.Handle == IntPtr.Zero)
                return;

            uint oldProtect;
            VirtualProtectEx(_process.Handle, (IntPtr)_pageBase, (UIntPtr)PAGE_SIZE, _originalProtection, out oldProtect);
        }

        private bool TryVEHInjection(ulong address, bool writeOnly, int size)
        {
            try
            {
                // Determine the correct DLL based on target architecture
                string dllName = _process.Is64Bit ? "VEHDebug64.dll" : "VEHDebug32.dll";
                string dllPath = GetDllPath(dllName);

                if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                {
                    LastError = $"DLL not found: {dllName}";
                    System.Diagnostics.Debug.WriteLine(LastError);
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"Injecting DLL: {dllPath}");

                // Inject the DLL
                _dllBase = _process.InjectDll(dllPath);
                if (_dllBase == IntPtr.Zero)
                {
                    LastError = $"DLL injection failed: {_process.LastInjectionError}";
                    System.Diagnostics.Debug.WriteLine(LastError);
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"VEH DLL injected at: {_dllBase:X}");

                // Give DLL time to initialize
                Thread.Sleep(100);

                // Call InitializeVEH in the injected DLL
                if (!CallRemoteInitializeVEH())
                {
                    LastError = "InitializeVEH call failed in target process";
                    System.Diagnostics.Debug.WriteLine(LastError);
                    return false;
                }

                // NOTE: We no longer set hardware breakpoints here
                // PAGE_GUARD mode handles memory access detection via page protection
                // The VEH handler will catch STATUS_GUARD_PAGE_VIOLATION exceptions

                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Injection exception: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"VEH injection exception: {ex}");
                return false;
            }
        }

        private string GetDllPath(string dllName)
        {
            // Look in several locations
            string[] searchPaths = new[]
            {
                // Same directory as the application
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName),
                // Parent directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", dllName),
                // VEHDebugDll build output
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VEHDebugDll", "build64", "bin", dllName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VEHDebugDll", "build32", "bin", dllName),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);
        }

        private bool CallRemoteInitializeVEH()
        {
            // Get the module in the target process
            _process.InvalidateModuleCache();
            Thread.Sleep(100);
            var modules = _process.GetModules(true);

            string dllName = _process.Is64Bit ? "VEHDebug64.dll" : "VEHDebug32.dll";
            ModuleInfo? vehModule = null;

            foreach (var mod in modules)
            {
                if (mod.ModuleName.Equals(dllName, StringComparison.OrdinalIgnoreCase))
                {
                    vehModule = mod;
                    break;
                }
            }

            if (vehModule == null)
            {
                System.Diagnostics.Debug.WriteLine("VEH module not found in target");
                return false;
            }

            // Allocate memory for the shared memory name
            string sharedMemName = _sharedMem!.Name;
            byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(sharedMemName + "\0");

            IntPtr remoteName = _process.AllocateMemory((uint)nameBytes.Length);
            if (remoteName == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Failed to allocate memory for shared mem name");
                return false;
            }

            if (!_process.Write(remoteName, nameBytes))
            {
                _process.FreeMemory(remoteName);
                System.Diagnostics.Debug.WriteLine("Failed to write shared mem name");
                return false;
            }

            // Get the InitializeVEH function address by parsing the PE export table
            IntPtr initVehAddr = GetExportedFunctionAddress(vehModule.BaseAddress, "InitializeVEH");
            if (initVehAddr == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Failed to find InitializeVEH export");
                _process.FreeMemory(remoteName);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"InitializeVEH at: {initVehAddr:X}");

            // Create a remote thread to call InitializeVEH with the shared memory name
            IntPtr hThread = _process.CreateThread(initVehAddr, remoteName);
            if (hThread == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"Failed to create remote thread, error: {error}");
                _process.FreeMemory(remoteName);
                return false;
            }

            // Wait for thread to complete
            WaitForSingleObject(hThread, 5000);
            GetExitCodeThread(hThread, out uint exitCode);
            CloseHandle(hThread);

            // Don't free remoteName yet - DLL may still reference it
            // It will be leaked but that's acceptable for now

            System.Diagnostics.Debug.WriteLine($"InitializeVEH returned: {exitCode}");

            // Also look up RefreshBreakpoints for periodic thread re-enumeration
            _refreshBreakpoints = GetExportedFunctionAddress(vehModule.BaseAddress, "RefreshBreakpoints");
            if (_refreshBreakpoints != IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshBreakpoints at: {_refreshBreakpoints:X}");
            }

            // Look up UninitializeVEH for cleanup
            _uninitializeVEH = GetExportedFunctionAddress(vehModule.BaseAddress, "UninitializeVEH");
            if (_uninitializeVEH != IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"UninitializeVEH at: {_uninitializeVEH:X}");
            }

            return exitCode != 0; // InitializeVEH returns TRUE (1) on success
        }

        /// <summary>
        /// Calls UninitializeVEH in the injected DLL to clean up the VEH handler
        /// </summary>
        private void CallRemoteUninitializeVEH()
        {
            if (_uninitializeVEH == IntPtr.Zero || _process?.Handle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("CallRemoteUninitializeVEH: No function address or process handle");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Calling UninitializeVEH at {_uninitializeVEH:X}");

                // Create a remote thread to call UninitializeVEH (no parameters)
                IntPtr hThread = _process.CreateThread(_uninitializeVEH, IntPtr.Zero);
                if (hThread == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"Failed to create remote thread for UninitializeVEH, error: {error}");
                    return;
                }

                // Wait for thread to complete
                WaitForSingleObject(hThread, 5000);
                GetExitCodeThread(hThread, out uint exitCode);
                CloseHandle(hThread);

                System.Diagnostics.Debug.WriteLine($"UninitializeVEH completed, exit code: {exitCode}");

                // Clear the function pointers
                _uninitializeVEH = IntPtr.Zero;
                _refreshBreakpoints = IntPtr.Zero;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CallRemoteUninitializeVEH exception: {ex.Message}");
            }
        }

        private IntPtr GetExportedFunctionAddress(IntPtr moduleBase, string functionName)
        {
            try
            {
                // Read the DOS header
                byte[] dosHeader = _process.Read(moduleBase, 64);
                if (dosHeader == null || dosHeader.Length < 64)
                    return IntPtr.Zero;

                // Get e_lfanew (offset to PE header)
                int e_lfanew = BitConverter.ToInt32(dosHeader, 60);

                // Read the PE header
                IntPtr peHeader = IntPtr.Add(moduleBase, e_lfanew);
                byte[] peBytes = _process.Read(peHeader, 264); // PE header + optional header
                if (peBytes == null)
                    return IntPtr.Zero;

                // Verify PE signature
                if (peBytes[0] != 'P' || peBytes[1] != 'E')
                    return IntPtr.Zero;

                // Get the export directory RVA
                // For PE32+: offset 136 from PE signature (24 + 112)
                // For PE32: offset 120 from PE signature (24 + 96)
                int exportDirRva;
                if (_process.Is64Bit)
                {
                    exportDirRva = BitConverter.ToInt32(peBytes, 136);
                }
                else
                {
                    exportDirRva = BitConverter.ToInt32(peBytes, 120);
                }

                if (exportDirRva == 0)
                    return IntPtr.Zero;

                // Read export directory
                IntPtr exportDir = IntPtr.Add(moduleBase, exportDirRva);
                byte[] exportBytes = _process.Read(exportDir, 40);
                if (exportBytes == null)
                    return IntPtr.Zero;

                int numberOfFunctions = BitConverter.ToInt32(exportBytes, 20);
                int numberOfNames = BitConverter.ToInt32(exportBytes, 24);
                int addressTableRva = BitConverter.ToInt32(exportBytes, 28);
                int nameTableRva = BitConverter.ToInt32(exportBytes, 32);
                int ordinalTableRva = BitConverter.ToInt32(exportBytes, 36);

                // Read the name table
                IntPtr nameTable = IntPtr.Add(moduleBase, nameTableRva);
                byte[] nameRvas = _process.Read(nameTable, numberOfNames * 4);
                if (nameRvas == null)
                    return IntPtr.Zero;

                // Read the ordinal table
                IntPtr ordinalTable = IntPtr.Add(moduleBase, ordinalTableRva);
                byte[] ordinals = _process.Read(ordinalTable, numberOfNames * 2);
                if (ordinals == null)
                    return IntPtr.Zero;

                // Read the address table
                IntPtr addressTable = IntPtr.Add(moduleBase, addressTableRva);
                byte[] addresses = _process.Read(addressTable, numberOfFunctions * 4);
                if (addresses == null)
                    return IntPtr.Zero;

                // Search for the function name
                for (int i = 0; i < numberOfNames; i++)
                {
                    int nameRva = BitConverter.ToInt32(nameRvas, i * 4);
                    IntPtr namePtr = IntPtr.Add(moduleBase, nameRva);

                    // Read the function name
                    byte[] nameBuffer = _process.Read(namePtr, 64);
                    if (nameBuffer == null)
                        continue;

                    string name = System.Text.Encoding.ASCII.GetString(nameBuffer);
                    int nullIdx = name.IndexOf('\0');
                    if (nullIdx >= 0)
                        name = name.Substring(0, nullIdx);

                    if (name == functionName)
                    {
                        // Found it! Get the ordinal and then the address
                        ushort ordinal = BitConverter.ToUInt16(ordinals, i * 2);
                        int functionRva = BitConverter.ToInt32(addresses, ordinal * 4);

                        return IntPtr.Add(moduleBase, functionRva);
                    }
                }

                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetExportedFunctionAddress error: {ex}");
                return IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        private bool CallRemoteSetBreakpoint(ulong address, bool writeOnly, int size)
        {
            // Set the breakpoint directly using HardwareBreakpointManager
            // This works from outside the process by manipulating thread contexts

            int bpType = writeOnly ? 1 : 3;

            var bpSize = size switch
            {
                1 => HardwareBreakpointManager.BreakpointSize.Byte1,
                2 => HardwareBreakpointManager.BreakpointSize.Byte2,
                8 => HardwareBreakpointManager.BreakpointSize.Byte8,
                _ => HardwareBreakpointManager.BreakpointSize.Byte4
            };

            var bpTypeEnum = writeOnly
                ? HardwareBreakpointManager.BreakpointType.Write
                : HardwareBreakpointManager.BreakpointType.ReadWrite;

            return _breakpointManager.SetBreakpoint(0, address, bpTypeEnum, bpSize);
        }

        /// <summary>
        /// Call SetHardwareBreakpoint in the injected DLL to set breakpoints on all threads
        /// </summary>
        private bool CallRemoteSetHardwareBreakpoint(ulong address, bool writeOnly, int size)
        {
            // Get the module in the target process
            _process.InvalidateModuleCache();
            var modules = _process.GetModules(true);

            string dllName = _process.Is64Bit ? "VEHDebug64.dll" : "VEHDebug32.dll";
            ModuleInfo? vehModule = null;

            foreach (var mod in modules)
            {
                if (mod.ModuleName.Equals(dllName, StringComparison.OrdinalIgnoreCase))
                {
                    vehModule = mod;
                    break;
                }
            }

            if (vehModule == null)
            {
                System.Diagnostics.Debug.WriteLine("VEH module not found for SetHardwareBreakpoint");
                return false;
            }

            // Get the SetHardwareBreakpoint function address
            _setBreakpoint = GetExportedFunctionAddress(vehModule.BaseAddress, "SetHardwareBreakpoint");
            if (_setBreakpoint == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Failed to find SetHardwareBreakpoint export");
                return false;
            }

            // Also get ClearHardwareBreakpoint for cleanup
            _clearBreakpoint = GetExportedFunctionAddress(vehModule.BaseAddress, "ClearHardwareBreakpoint");

            System.Diagnostics.Debug.WriteLine($"SetHardwareBreakpoint at: {_setBreakpoint:X}");

            // SetHardwareBreakpoint signature: BOOL __stdcall SetHardwareBreakpoint(int slot, DWORD64 address, DWORD type, DWORD size)
            // We need to allocate memory for parameters and create a small shellcode stub

            // Parameters:
            // slot = _breakpointSlot (the slot we selected earlier)
            // address = the watch address
            // type = 1 (write) or 3 (read/write)
            // size = 1, 2, 4, or 8

            int slot = _breakpointSlot;
            if (slot < 0 || slot > 3)
                slot = 0;

            int bpType = writeOnly ? 1 : 3;

            // For 32-bit targets, we need to push params on stack and call the function
            // For 64-bit targets, we need to use registers (rcx, rdx, r8, r9)

            if (_process.Is64Bit)
            {
                // 64-bit calling convention: rcx=slot, rdx=address, r8=type, r9=size
                // Shellcode:
                // mov ecx, 0                ; slot
                // mov rdx, address          ; address (64-bit immediate)
                // mov r8d, type             ; type
                // mov r9d, size             ; size
                // mov rax, SetHardwareBreakpoint
                // sub rsp, 0x28             ; shadow space
                // call rax
                // add rsp, 0x28
                // ret

                byte[] shellcode = new byte[64];
                int i = 0;

                // mov ecx, slot
                shellcode[i++] = 0xB9;
                byte[] slotBytes = BitConverter.GetBytes(slot);
                Array.Copy(slotBytes, 0, shellcode, i, 4);
                i += 4;

                // mov rdx, address (movabs rdx, imm64)
                shellcode[i++] = 0x48;
                shellcode[i++] = 0xBA;
                byte[] addrBytes = BitConverter.GetBytes(address);
                Array.Copy(addrBytes, 0, shellcode, i, 8);
                i += 8;

                // mov r8d, type
                shellcode[i++] = 0x41;
                shellcode[i++] = 0xB8;
                byte[] typeBytes = BitConverter.GetBytes(bpType);
                Array.Copy(typeBytes, 0, shellcode, i, 4);
                i += 4;

                // mov r9d, size
                shellcode[i++] = 0x41;
                shellcode[i++] = 0xB9;
                byte[] sizeBytes = BitConverter.GetBytes(size);
                Array.Copy(sizeBytes, 0, shellcode, i, 4);
                i += 4;

                // mov rax, SetHardwareBreakpoint (movabs rax, imm64)
                shellcode[i++] = 0x48;
                shellcode[i++] = 0xB8;
                byte[] funcBytes = BitConverter.GetBytes(_setBreakpoint.ToInt64());
                Array.Copy(funcBytes, 0, shellcode, i, 8);
                i += 8;

                // sub rsp, 0x28 (shadow space)
                shellcode[i++] = 0x48;
                shellcode[i++] = 0x83;
                shellcode[i++] = 0xEC;
                shellcode[i++] = 0x28;

                // call rax
                shellcode[i++] = 0xFF;
                shellcode[i++] = 0xD0;

                // add rsp, 0x28
                shellcode[i++] = 0x48;
                shellcode[i++] = 0x83;
                shellcode[i++] = 0xC4;
                shellcode[i++] = 0x28;

                // ret
                shellcode[i++] = 0xC3;

                // Allocate and write shellcode
                IntPtr shellcodeAddr = _process.AllocateMemory((uint)i, 0x40); // PAGE_EXECUTE_READWRITE
                if (shellcodeAddr == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to allocate shellcode memory");
                    return false;
                }

                if (!_process.Write(shellcodeAddr, shellcode.AsSpan(0, i).ToArray()))
                {
                    _process.FreeMemory(shellcodeAddr);
                    System.Diagnostics.Debug.WriteLine("Failed to write shellcode");
                    return false;
                }

                // Execute shellcode
                IntPtr hThread = _process.CreateThread(shellcodeAddr, IntPtr.Zero);
                if (hThread == IntPtr.Zero)
                {
                    _process.FreeMemory(shellcodeAddr);
                    System.Diagnostics.Debug.WriteLine("Failed to create shellcode thread");
                    return false;
                }

                WaitForSingleObject(hThread, 5000);
                GetExitCodeThread(hThread, out uint exitCode);
                CloseHandle(hThread);
                _process.FreeMemory(shellcodeAddr);

                System.Diagnostics.Debug.WriteLine($"SetHardwareBreakpoint returned: {exitCode}");
                return exitCode != 0;
            }
            else
            {
                // 32-bit: push params right-to-left, call function
                // push size
                // push type
                // push address_low
                // push address_high (for DWORD64)
                // push slot
                // call SetHardwareBreakpoint
                // ret

                byte[] shellcode = new byte[64];
                int i = 0;

                // push size
                shellcode[i++] = 0x68;
                byte[] sizeBytes = BitConverter.GetBytes(size);
                Array.Copy(sizeBytes, 0, shellcode, i, 4);
                i += 4;

                // push type
                shellcode[i++] = 0x68;
                byte[] typeBytes = BitConverter.GetBytes(bpType);
                Array.Copy(typeBytes, 0, shellcode, i, 4);
                i += 4;

                // For DWORD64 address, push high then low (cdecl pushes right to left)
                uint addrHigh = (uint)(address >> 32);
                uint addrLow = (uint)(address & 0xFFFFFFFF);

                // push address_high
                shellcode[i++] = 0x68;
                byte[] highBytes = BitConverter.GetBytes(addrHigh);
                Array.Copy(highBytes, 0, shellcode, i, 4);
                i += 4;

                // push address_low
                shellcode[i++] = 0x68;
                byte[] lowBytes = BitConverter.GetBytes(addrLow);
                Array.Copy(lowBytes, 0, shellcode, i, 4);
                i += 4;

                // push slot
                shellcode[i++] = 0x68;
                byte[] slotBytes = BitConverter.GetBytes(slot);
                Array.Copy(slotBytes, 0, shellcode, i, 4);
                i += 4;

                // call SetHardwareBreakpoint (relative call won't work, use mov eax + call eax)
                // mov eax, SetHardwareBreakpoint
                shellcode[i++] = 0xB8;
                byte[] funcBytes = BitConverter.GetBytes(_setBreakpoint.ToInt32());
                Array.Copy(funcBytes, 0, shellcode, i, 4);
                i += 4;

                // call eax
                shellcode[i++] = 0xFF;
                shellcode[i++] = 0xD0;

                // ret (stdcall cleans up stack automatically)
                shellcode[i++] = 0xC3;

                // Allocate and write shellcode
                IntPtr shellcodeAddr = _process.AllocateMemory((uint)i, 0x40); // PAGE_EXECUTE_READWRITE
                if (shellcodeAddr == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to allocate shellcode memory");
                    return false;
                }

                if (!_process.Write(shellcodeAddr, shellcode.AsSpan(0, i).ToArray()))
                {
                    _process.FreeMemory(shellcodeAddr);
                    System.Diagnostics.Debug.WriteLine("Failed to write shellcode");
                    return false;
                }

                // Execute shellcode
                IntPtr hThread = _process.CreateThread(shellcodeAddr, IntPtr.Zero);
                if (hThread == IntPtr.Zero)
                {
                    _process.FreeMemory(shellcodeAddr);
                    System.Diagnostics.Debug.WriteLine("Failed to create shellcode thread");
                    return false;
                }

                WaitForSingleObject(hThread, 5000);
                GetExitCodeThread(hThread, out uint exitCode);
                CloseHandle(hThread);
                _process.FreeMemory(shellcodeAddr);

                System.Diagnostics.Debug.WriteLine($"SetHardwareBreakpoint returned: {exitCode}");
                return exitCode != 0;
            }
        }

        private void PollHits()
        {
            while (_running)
            {
                try
                {
                    // NOTE: Guard reapply is now handled by the DLL using single-step
                    // The DLL sets TF (trap flag) on PAGE_GUARD hit, then reapplies the guard
                    // on the subsequent EXCEPTION_SINGLE_STEP after the instruction completes

                    // Read new hits from shared memory
                    var hits = _sharedMem?.ReadNewHits();
                    if (hits != null && hits.Count > 0)
                    {
                        foreach (var (address, threadId) in hits)
                        {
                            BreakpointHit?.Invoke(this, new BreakpointHitEventArgs
                            {
                                InstructionAddress = address,
                                ThreadId = threadId,
                                Timestamp = DateTime.Now
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Poll exception: {ex}");
                }

                Thread.Sleep(50); // Poll every 50ms - no need to poll frequently since DLL handles guard reapply
            }
        }

        /// <summary>
        /// Polling thread for hardware breakpoint mode
        /// </summary>
        private void PollHitsHardwareBreakpoint()
        {
            _pollCount = 0;

            while (_running)
            {
                try
                {
                    // Read new hits from shared memory
                    var hits = _sharedMem?.ReadNewHits();
                    if (hits != null && hits.Count > 0)
                    {
                        foreach (var (address, threadId) in hits)
                        {
                            BreakpointHit?.Invoke(this, new BreakpointHitEventArgs
                            {
                                InstructionAddress = address,
                                ThreadId = threadId,
                                Timestamp = DateTime.Now
                            });
                        }
                    }

                    // Periodically refresh breakpoints to catch new threads
                    _pollCount++;
                    if (_pollCount >= 100) // Every ~1 second (100 * 10ms)
                    {
                        _pollCount = 0;
                        CallRefreshBreakpoints();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Poll exception: {ex}");
                }

                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Call RefreshBreakpoints in the injected DLL to re-apply breakpoints to new threads
        /// </summary>
        private void CallRefreshBreakpoints()
        {
            if (_refreshBreakpoints == IntPtr.Zero || _process?.Target == null)
                return;

            try
            {
                // Create a remote thread to call RefreshBreakpoints (takes no parameters)
                IntPtr hThread = _process.CreateThread(_refreshBreakpoints, IntPtr.Zero);
                if (hThread != IntPtr.Zero)
                {
                    // Wait briefly for completion (don't block too long)
                    WaitForSingleObject(hThread, 500);
                    CloseHandle(hThread);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshBreakpoints call failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops monitoring with proper thread suspension to prevent crashes.
        ///
        /// The crash happens because:
        /// - Thread hits PAGE_GUARD, VEH sets trap flag (TF), page guard is consumed
        /// - We remove PAGE_GUARD / dispose shared memory
        /// - Thread completes single-step, VEH tries to access invalid memory or re-apply guard
        /// - CRASH
        ///
        /// Fix: Suspend all threads, clear trap flags, then cleanup safely.
        /// </summary>
        public void StopMonitoring()
        {
            if (!_running)
                return;

            _running = false;
            System.Diagnostics.Debug.WriteLine("StopMonitoring: Starting safe shutdown...");

            // STEP 1: Signal DLL to stop processing new exceptions
            _sharedMem?.SetShutdownRequested(true);
            _sharedMem?.SetActive(false);
            _sharedMem?.DisablePageGuard();

            // STEP 1.5: Clear hardware breakpoints if we were using them
            if (_breakpointSlot >= 0)
            {
                _breakpointManager.ClearBreakpoint(_breakpointSlot);
                _breakpointSlot = -1;
                System.Diagnostics.Debug.WriteLine("StopMonitoring: Hardware breakpoint cleared");
            }

            // STEP 2: Remove PAGE_GUARD FIRST - this prevents new PAGE_GUARD exceptions
            // Any threads currently mid-single-step will complete, but no NEW ones will start
            RemovePageGuard();
            System.Diagnostics.Debug.WriteLine("StopMonitoring: PAGE_GUARD removed");

            // STEP 3: Wait for in-flight handlers AND pending single-steps
            int waitMs = 0;
            const int maxWaitMs = 2000;
            const int checkInterval = 50;

            while (waitMs < maxWaitMs)
            {
                int activeHandlers = _sharedMem?.GetActiveHandlers() ?? 0;
                if (activeHandlers == 0)
                {
                    // Extra wait for any threads that JUST set TF but haven't fired single-step yet
                    Thread.Sleep(100);
                    break;
                }

                System.Diagnostics.Debug.WriteLine($"StopMonitoring: Waiting for {activeHandlers} active handlers...");
                Thread.Sleep(checkInterval);
                waitMs += checkInterval;
            }

            // STEP 4: CRITICAL - Suspend all threads and clear trap flags
            // This catches any threads that still have TF set
            var suspendedThreads = SuspendAllThreads();
            System.Diagnostics.Debug.WriteLine($"StopMonitoring: Suspended {suspendedThreads.Count} threads");

            try
            {
                // Clear trap flags on all suspended threads
                ClearTrapFlagsOnAllThreads(suspendedThreads);

                // Invalidate shared memory while threads are suspended
                _sharedMem?.InvalidateMagic();
            }
            finally
            {
                // Resume all threads - they will continue normally without any pending exceptions
                ResumeAllThreads(suspendedThreads);
                System.Diagnostics.Debug.WriteLine("StopMonitoring: Resumed all threads");
            }

            // STEP 5: Wait for polling thread
            if (_pollingThread != null && _pollingThread.IsAlive)
            {
                _pollingThread.Join(2000);
            }

            // STEP 6: Call UninitializeVEH in the DLL BEFORE disposing shared memory
            // This removes the VEH handler so it won't try to access shared memory after it's freed
            if (_vehInjected)
            {
                CallRemoteUninitializeVEH();
                Thread.Sleep(100);  // Give DLL time to fully clean up VEH handler
                _vehInjected = false;
                System.Diagnostics.Debug.WriteLine("StopMonitoring: VEH handler uninitialized");
            }

            // STEP 7: Final cleanup - dispose shared memory AFTER VEH is cleaned up
            Thread.Sleep(50);
            _sharedMem?.Dispose();
            _sharedMem = null;

            System.Diagnostics.Debug.WriteLine("StopMonitoring: Shutdown complete");
            MonitoringStopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Suspends all threads in the target process (except system threads)
        /// Returns list of thread handles that were suspended
        /// </summary>
        private List<(IntPtr handle, uint threadId)> SuspendAllThreads()
        {
            var suspended = new List<(IntPtr, uint)>();

            if (_process?.Target == null)
                return suspended;

            uint processId = (uint)_process.Target.Id;
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);

            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                return suspended;

            try
            {
                var entry = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };

                if (Thread32First(snapshot, ref entry))
                {
                    do
                    {
                        if (entry.th32OwnerProcessID == processId)
                        {
                            IntPtr hThread = OpenThread(
                                THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_SET_CONTEXT,
                                false, entry.th32ThreadID);

                            if (hThread != IntPtr.Zero)
                            {
                                int suspendCount = SuspendThread(hThread);
                                if (suspendCount >= 0)
                                {
                                    suspended.Add((hThread, entry.th32ThreadID));
                                }
                                else
                                {
                                    CloseHandle(hThread);
                                }
                            }
                        }
                        entry.dwSize = (uint)Marshal.SizeOf<THREADENTRY32>();
                    }
                    while (Thread32Next(snapshot, ref entry));
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return suspended;
        }

        /// <summary>
        /// Clears the trap flag (TF) on all suspended threads to prevent single-step exceptions
        /// </summary>
        private void ClearTrapFlagsOnAllThreads(List<(IntPtr handle, uint threadId)> threads)
        {
            foreach (var (hThread, threadId) in threads)
            {
                try
                {
                    if (_process.Is64Bit)
                    {
                        ClearTrapFlag64(hThread);
                    }
                    else
                    {
                        ClearTrapFlag32(hThread);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to clear TF on thread {threadId}: {ex.Message}");
                }
            }
        }

        private void ClearTrapFlag64(IntPtr hThread)
        {
            var context = new CONTEXT64();
            context.ContextFlags = CONTEXT_CONTROL;

            if (GetThreadContext(hThread, ref context))
            {
                if ((context.EFlags & 0x100) != 0) // TF is bit 8
                {
                    context.EFlags &= ~0x100UL; // Clear TF
                    SetThreadContext(hThread, ref context);
                    System.Diagnostics.Debug.WriteLine("Cleared TF on 64-bit thread");
                }
            }
        }

        private void ClearTrapFlag32(IntPtr hThread)
        {
            var context = new CONTEXT32();
            context.ContextFlags = CONTEXT32_CONTROL;

            if (Wow64GetThreadContext(hThread, ref context))
            {
                if ((context.EFlags & 0x100) != 0) // TF is bit 8
                {
                    context.EFlags &= ~0x100U; // Clear TF
                    Wow64SetThreadContext(hThread, ref context);
                    System.Diagnostics.Debug.WriteLine("Cleared TF on 32-bit thread");
                }
            }
        }

        /// <summary>
        /// Resumes all previously suspended threads
        /// </summary>
        private void ResumeAllThreads(List<(IntPtr handle, uint threadId)> threads)
        {
            foreach (var (hThread, _) in threads)
            {
                ResumeThread(hThread);
                CloseHandle(hThread);
            }
        }

        /// <summary>
        /// Call ClearHardwareBreakpoint in the injected DLL
        /// </summary>
        private void CallRemoteClearHardwareBreakpoint(int slot)
        {
            if (_clearBreakpoint == IntPtr.Zero || _process?.Target == null)
                return;

            try
            {
                // ClearHardwareBreakpoint(int slot) - simple call
                if (_process.Is64Bit)
                {
                    // 64-bit: mov ecx, slot; mov rax, func; sub rsp, 28h; call rax; add rsp, 28h; ret
                    byte[] shellcode = new byte[32];
                    int i = 0;

                    // mov ecx, slot
                    shellcode[i++] = 0xB9;
                    byte[] slotBytes = BitConverter.GetBytes(slot);
                    Array.Copy(slotBytes, 0, shellcode, i, 4);
                    i += 4;

                    // mov rax, ClearHardwareBreakpoint
                    shellcode[i++] = 0x48;
                    shellcode[i++] = 0xB8;
                    byte[] funcBytes = BitConverter.GetBytes(_clearBreakpoint.ToInt64());
                    Array.Copy(funcBytes, 0, shellcode, i, 8);
                    i += 8;

                    // sub rsp, 0x28
                    shellcode[i++] = 0x48;
                    shellcode[i++] = 0x83;
                    shellcode[i++] = 0xEC;
                    shellcode[i++] = 0x28;

                    // call rax
                    shellcode[i++] = 0xFF;
                    shellcode[i++] = 0xD0;

                    // add rsp, 0x28
                    shellcode[i++] = 0x48;
                    shellcode[i++] = 0x83;
                    shellcode[i++] = 0xC4;
                    shellcode[i++] = 0x28;

                    // ret
                    shellcode[i++] = 0xC3;

                    IntPtr shellcodeAddr = _process.AllocateMemory((uint)i, 0x40); // PAGE_EXECUTE_READWRITE
                    if (shellcodeAddr != IntPtr.Zero)
                    {
                        _process.Write(shellcodeAddr, shellcode.AsSpan(0, i).ToArray());
                        IntPtr hThread = _process.CreateThread(shellcodeAddr, IntPtr.Zero);
                        if (hThread != IntPtr.Zero)
                        {
                            WaitForSingleObject(hThread, 2000);
                            CloseHandle(hThread);
                        }
                        _process.FreeMemory(shellcodeAddr);
                    }
                }
                else
                {
                    // 32-bit: push slot; mov eax, func; call eax; ret
                    byte[] shellcode = new byte[16];
                    int i = 0;

                    // push slot
                    shellcode[i++] = 0x6A;
                    shellcode[i++] = (byte)slot;

                    // mov eax, ClearHardwareBreakpoint
                    shellcode[i++] = 0xB8;
                    byte[] funcBytes = BitConverter.GetBytes(_clearBreakpoint.ToInt32());
                    Array.Copy(funcBytes, 0, shellcode, i, 4);
                    i += 4;

                    // call eax
                    shellcode[i++] = 0xFF;
                    shellcode[i++] = 0xD0;

                    // ret
                    shellcode[i++] = 0xC3;

                    IntPtr shellcodeAddr = _process.AllocateMemory((uint)i, 0x40); // PAGE_EXECUTE_READWRITE
                    if (shellcodeAddr != IntPtr.Zero)
                    {
                        _process.Write(shellcodeAddr, shellcode.AsSpan(0, i).ToArray());
                        IntPtr hThread = _process.CreateThread(shellcodeAddr, IntPtr.Zero);
                        if (hThread != IntPtr.Zero)
                        {
                            WaitForSingleObject(hThread, 2000);
                            CloseHandle(hThread);
                        }
                        _process.FreeMemory(shellcodeAddr);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearHardwareBreakpoint failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopMonitoring();
                _disposed = true;
            }
        }

        #region P/Invoke

        private const uint TH32CS_SNAPTHREAD = 0x00000004;
        private const uint THREAD_SUSPEND_RESUME = 0x0002;
        private const uint THREAD_GET_CONTEXT = 0x0008;
        private const uint THREAD_SET_CONTEXT = 0x0010;
        private const uint CONTEXT_CONTROL = 0x00010001;
        private const uint CONTEXT32_CONTROL = 0x00010001;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, ref MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadContext(IntPtr hThread, ref CONTEXT64 lpContext);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadContext(IntPtr hThread, ref CONTEXT64 lpContext);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64GetThreadContext(IntPtr hThread, ref CONTEXT32 lpContext);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64SetThreadContext(IntPtr hThread, ref CONTEXT32 lpContext);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct THREADENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ThreadID;
            public uint th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONTEXT64
        {
            public ulong P1Home, P2Home, P3Home, P4Home, P5Home, P6Home;
            public uint ContextFlags;
            public uint MxCsr;
            public ushort SegCs, SegDs, SegEs, SegFs, SegGs, SegSs;
            public ulong EFlags;
            public ulong Dr0, Dr1, Dr2, Dr3, Dr6, Dr7;
            public ulong Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi;
            public ulong R8, R9, R10, R11, R12, R13, R14, R15;
            public ulong Rip;
            // FPU/XMM state not needed for TF clearing
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] FltSave;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONTEXT32
        {
            public uint ContextFlags;
            public uint Dr0, Dr1, Dr2, Dr3, Dr6, Dr7;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 112)]
            public byte[] FloatSave;
            public uint SegGs, SegFs, SegEs, SegDs;
            public uint Edi, Esi, Ebx, Edx, Ecx, Eax;
            public uint Ebp, Eip;
            public uint SegCs;
            public uint EFlags;
            public uint Esp;
            public uint SegSs;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] ExtendedRegisters;
        }

        #endregion
    }
}
