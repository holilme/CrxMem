using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrxMem.Core
{
    /// <summary>
    /// Ultra-fast process watcher that detects new processes the moment they start.
    /// Uses WMI events (Option A) with high-priority polling fallback (Option B).
    /// Designed for auto-attaching before anti-cheat can initialize.
    /// </summary>
    public class FastProcessWatcher : IDisposable
    {
        #region P/Invoke for fast suspend

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

        #endregion

        private ManagementEventWatcher? _wmiWatcher;
        private Thread? _pollingThread;
        private volatile bool _isRunning;
        private readonly object _lock = new();
        private HashSet<int> _knownProcessIds = new();
        private List<string> _targetProcessNames = new();
        private int _pollingIntervalMs = 1; // 1ms polling for fallback mode
        private bool _useWmi = true;
        private bool _immediatelySuspend = false;

        /// <summary>
        /// Fired when a target process is detected. Includes the Process object.
        /// </summary>
        public event Action<Process>? ProcessDetected;

        /// <summary>
        /// Fired when status changes (for UI feedback)
        /// </summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Whether the watcher is currently active
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Whether WMI mode is being used (true) or polling fallback (false)
        /// </summary>
        public bool IsUsingWmi => _useWmi && _wmiWatcher != null;

        /// <summary>
        /// Polling interval in milliseconds (only used in fallback mode)
        /// </summary>
        public int PollingIntervalMs
        {
            get => _pollingIntervalMs;
            set => _pollingIntervalMs = Math.Max(1, Math.Min(1000, value));
        }

        /// <summary>
        /// If true, immediately suspend the process the moment it's detected (before event fires).
        /// This is critical for attaching before anti-cheat can initialize.
        /// </summary>
        public bool ImmediatelySuspend
        {
            get => _immediatelySuspend;
            set => _immediatelySuspend = value;
        }

        /// <summary>
        /// Fired when a process was suspended (includes PID and success status)
        /// </summary>
        public event Action<int, bool>? ProcessSuspended;

        /// <summary>
        /// Start watching for processes with the given names.
        /// Process names should NOT include .exe extension.
        /// </summary>
        /// <param name="processNames">List of process names to watch for (case-insensitive)</param>
        /// <param name="preferWmi">If true, try WMI first; if false, use polling directly</param>
        public void Start(IEnumerable<string> processNames, bool preferWmi = true)
        {
            Stop();

            lock (_lock)
            {
                _targetProcessNames = processNames
                    .Select(p => p.Trim().ToLowerInvariant())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p => p.EndsWith(".exe") ? p[..^4] : p) // Remove .exe if present
                    .ToList();

                if (_targetProcessNames.Count == 0)
                {
                    StatusChanged?.Invoke("No processes to watch");
                    return;
                }

                _isRunning = true;
                _useWmi = preferWmi;

                // Build initial list of known process IDs to avoid false positives
                RefreshKnownProcesses();

                if (preferWmi && TryStartWmiWatcher())
                {
                    StatusChanged?.Invoke($"WMI watcher active for: {string.Join(", ", _targetProcessNames)}");
                }
                else
                {
                    // Fallback to high-priority polling
                    _useWmi = false;
                    StartPollingThread();
                    StatusChanged?.Invoke($"Polling watcher active ({_pollingIntervalMs}ms) for: {string.Join(", ", _targetProcessNames)}");
                }
            }
        }

        /// <summary>
        /// Stop watching for processes
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _isRunning = false;

                if (_wmiWatcher != null)
                {
                    try
                    {
                        _wmiWatcher.Stop();
                        _wmiWatcher.Dispose();
                    }
                    catch { }
                    _wmiWatcher = null;
                }

                if (_pollingThread != null)
                {
                    try
                    {
                        _pollingThread.Join(100);
                    }
                    catch { }
                    _pollingThread = null;
                }

                _knownProcessIds.Clear();
                StatusChanged?.Invoke("Watcher stopped");
            }
        }

        /// <summary>
        /// Try to start WMI-based process watcher (Option A - fastest)
        /// </summary>
        private bool TryStartWmiWatcher()
        {
            try
            {
                // WMI query for process creation events
                // Win32_ProcessStartTrace fires the moment a process is created
                var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");

                _wmiWatcher = new ManagementEventWatcher(query);
                _wmiWatcher.EventArrived += WmiWatcher_EventArrived;
                _wmiWatcher.Start();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI watcher failed: {ex.Message}");
                _wmiWatcher?.Dispose();
                _wmiWatcher = null;
                return false;
            }
        }

        /// <summary>
        /// Immediately suspend a process by PID using NtSuspendProcess (fastest method)
        /// </summary>
        /// <param name="processId">Process ID to suspend</param>
        /// <returns>True if successful</returns>
        private bool FastSuspendProcess(int processId)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                // Open with suspend/resume rights
                handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
                if (handle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[FastSuspend] Failed to open process {processId}");
                    return false;
                }

                // Suspend immediately using NtSuspendProcess (faster than thread enumeration)
                int result = NtSuspendProcess(handle);
                bool success = result == 0; // STATUS_SUCCESS

                Debug.WriteLine($"[FastSuspend] Process {processId} suspend: {(success ? "SUCCESS" : $"FAILED (0x{result:X})")}");
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FastSuspend] Exception suspending {processId}: {ex.Message}");
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    CloseHandle(handle);
            }
        }

        /// <summary>
        /// Handle WMI process creation event
        /// </summary>
        private void WmiWatcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            if (!_isRunning) return;

            try
            {
                var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
                var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

                // Remove .exe for comparison
                var nameWithoutExt = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName[..^4]
                    : processName;

                // Check if this is a target process (exact match only - no substring matching)
                if (_targetProcessNames.Any(target =>
                    nameWithoutExt.Equals(target, StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.WriteLine($"[WMI] Target process detected: {processName} (PID: {processId})");

                    // IMMEDIATELY suspend if configured - this is BEFORE any delay
                    if (_immediatelySuspend)
                    {
                        bool suspended = FastSuspendProcess(processId);
                        ProcessSuspended?.Invoke(processId, suspended);
                    }

                    // Small delay to let process initialize enough to be accessible
                    Thread.Sleep(5);

                    try
                    {
                        var process = Process.GetProcessById(processId);
                        ProcessDetected?.Invoke(process);
                    }
                    catch (ArgumentException)
                    {
                        // Process already exited
                        Debug.WriteLine($"[WMI] Process {processId} exited before we could attach");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WMI] Event processing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Start high-priority polling thread (Option B - fallback)
        /// </summary>
        private void StartPollingThread()
        {
            _pollingThread = new Thread(PollingLoop)
            {
                Name = "FastProcessWatcher",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            _pollingThread.Start();
        }

        /// <summary>
        /// High-speed polling loop for process detection
        /// </summary>
        private void PollingLoop()
        {
            var sw = new Stopwatch();

            while (_isRunning)
            {
                sw.Restart();

                try
                {
                    CheckForNewProcesses();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Polling] Error: {ex.Message}");
                }

                // Sleep for remaining time to hit target interval
                sw.Stop();
                int sleepTime = _pollingIntervalMs - (int)sw.ElapsedMilliseconds;
                if (sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }

        /// <summary>
        /// Check for new processes matching our targets
        /// </summary>
        private void CheckForNewProcesses()
        {
            // Use GetProcesses() - it's faster than querying by name multiple times
            var currentProcesses = Process.GetProcesses();

            try
            {
                foreach (var process in currentProcesses)
                {
                    try
                    {
                        // Skip if we already know about this process
                        if (_knownProcessIds.Contains(process.Id))
                            continue;

                        // Add to known list
                        _knownProcessIds.Add(process.Id);

                        // Check if this is a target
                        var processName = process.ProcessName.ToLowerInvariant();

                        // Exact match only - no substring matching
                        if (_targetProcessNames.Any(target =>
                            processName.Equals(target, StringComparison.OrdinalIgnoreCase)))
                        {
                            Debug.WriteLine($"[Polling] Target process detected: {process.ProcessName} (PID: {process.Id})");

                            // IMMEDIATELY suspend if configured - this is BEFORE invoking the event
                            if (_immediatelySuspend)
                            {
                                bool suspended = FastSuspendProcess(process.Id);
                                ProcessSuspended?.Invoke(process.Id, suspended);
                            }

                            ProcessDetected?.Invoke(process);
                        }
                    }
                    catch
                    {
                        // Process may have exited or we don't have access
                    }
                }
            }
            finally
            {
                // Dispose processes we don't need
                foreach (var p in currentProcesses)
                {
                    try { p.Dispose(); } catch { }
                }
            }

            // Periodically clean up known process IDs (remove dead ones)
            if (_knownProcessIds.Count > 1000)
            {
                RefreshKnownProcesses();
            }
        }

        /// <summary>
        /// Refresh the list of known process IDs
        /// </summary>
        private void RefreshKnownProcesses()
        {
            try
            {
                var currentProcesses = Process.GetProcesses();
                _knownProcessIds = new HashSet<int>(currentProcesses.Select(p => p.Id));

                foreach (var p in currentProcesses)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            catch
            {
                _knownProcessIds.Clear();
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
