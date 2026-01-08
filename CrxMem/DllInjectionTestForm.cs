using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CrxMem
{
    public class DllInjectionTestForm : Form
    {
        private TextBox _txtOutput;
        private Button _btnTestNotepad;
        private Button _btnTestAttached;
        private Button _btnCheckDlls;
        private Core.ProcessAccess? _process;

        public DllInjectionTestForm(Core.ProcessAccess? process = null)
        {
            _process = process;
            InitializeComponent();
            ApplyTheme();
        }

        private void InitializeComponent()
        {
            Text = "DLL Injection Diagnostic";
            Size = new Size(600, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight
            };

            _btnCheckDlls = new Button { Text = "Check DLLs", Width = 100, Height = 35 };
            _btnCheckDlls.Click += BtnCheckDlls_Click;

            _btnTestNotepad = new Button { Text = "Test on Notepad", Width = 120, Height = 35 };
            _btnTestNotepad.Click += BtnTestNotepad_Click;

            _btnTestAttached = new Button { Text = "Test on Attached Process", Width = 160, Height = 35 };
            _btnTestAttached.Click += BtnTestAttached_Click;
            _btnTestAttached.Enabled = _process != null;

            var btnTestCrossArch = new Button { Text = "Test Cross-Arch Resolution", Width = 160, Height = 35 };
            btnTestCrossArch.Click += (s, e) =>
            {
                if (_process == null)
                {
                    MessageBox.Show("No process attached!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _txtOutput.Clear();
                Log("=== Cross-Architecture Resolution Test ===");
                Log(_process.TestCrossArchResolution());
            };
            btnTestCrossArch.Enabled = _process != null;

            buttonPanel.Controls.AddRange(new Control[] { _btnCheckDlls, _btnTestNotepad, _btnTestAttached, btnTestCrossArch });

            _txtOutput = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9F),
                ReadOnly = true,
                WordWrap = false
            };

            panel.Controls.Add(buttonPanel, 0, 0);
            panel.Controls.Add(_txtOutput, 0, 1);

            Controls.Add(panel);
        }

        private void Log(string message)
        {
            _txtOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }

        private void BtnCheckDlls_Click(object? sender, EventArgs e)
        {
            _txtOutput.Clear();
            Log("=== DLL Check ===");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Log($"Application directory: {baseDir}");

            string[] dllNames = { "VEHDebug32.dll", "VEHDebug64.dll" };
            foreach (var dll in dllNames)
            {
                string path = Path.Combine(baseDir, dll);
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    Log($"✓ {dll}: {info.Length} bytes, modified {info.LastWriteTime}");

                    // Check if it's a valid PE
                    try
                    {
                        byte[] header = new byte[64];
                        using (var fs = File.OpenRead(path))
                        {
                            fs.Read(header, 0, 64);
                        }

                        if (header[0] == 'M' && header[1] == 'Z')
                        {
                            int peOffset = BitConverter.ToInt32(header, 0x3C);
                            using (var fs = File.OpenRead(path))
                            {
                                fs.Seek(peOffset, SeekOrigin.Begin);
                                byte[] peHeader = new byte[6];
                                fs.Read(peHeader, 0, 6);

                                if (peHeader[0] == 'P' && peHeader[1] == 'E')
                                {
                                    ushort machine = BitConverter.ToUInt16(peHeader, 4);
                                    string arch = machine switch
                                    {
                                        0x14c => "x86 (32-bit)",
                                        0x8664 => "x64 (64-bit)",
                                        _ => $"Unknown (0x{machine:X4})"
                                    };
                                    Log($"  Architecture: {arch}");
                                }
                            }
                        }
                        else
                        {
                            Log($"  WARNING: Not a valid PE file!");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  Error reading PE: {ex.Message}");
                    }
                }
                else
                {
                    Log($"✗ {dll}: NOT FOUND at {path}");
                }
            }

            Log("");
            Log("=== System Info ===");
            Log($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            Log($"64-bit Process (CrxMem): {Environment.Is64BitProcess}");
        }

        private void BtnTestNotepad_Click(object? sender, EventArgs e)
        {
            _txtOutput.Clear();
            Log("=== Notepad Injection Test ===");

            Process? notepad = null;
            try
            {
                // Start notepad
                Log("Starting notepad.exe...");
                notepad = Process.Start("notepad.exe");
                if (notepad == null)
                {
                    Log("ERROR: Failed to start notepad");
                    return;
                }

                notepad.WaitForInputIdle(5000);
                System.Threading.Thread.Sleep(500);

                Log($"Notepad PID: {notepad.Id}");

                // Open the process
                var testProcess = new Core.ProcessAccess();
                if (!testProcess.Open(notepad.Id))
                {
                    Log("ERROR: Failed to open notepad process");
                    return;
                }

                Log($"Process opened successfully");
                Log($"Is64Bit detected: {testProcess.Is64Bit}");

                // Determine correct DLL
                string dllName = testProcess.Is64Bit ? "VEHDebug64.dll" : "VEHDebug32.dll";
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);

                Log($"Will inject: {dllName}");
                Log($"Full path: {dllPath}");

                if (!File.Exists(dllPath))
                {
                    Log($"ERROR: DLL not found!");
                    return;
                }

                // Try injection
                Log("Attempting injection...");
                IntPtr result = testProcess.InjectDll(dllPath);

                if (result != IntPtr.Zero)
                {
                    Log($"SUCCESS! DLL loaded at: 0x{result.ToInt64():X}");

                    // Verify by checking modules
                    testProcess.InvalidateModuleCache();
                    var modules = testProcess.GetModules(true);
                    bool found = false;
                    foreach (var mod in modules)
                    {
                        if (mod.ModuleName.Contains("VEHDebug", StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"Verified in module list: {mod.ModuleName} at 0x{mod.BaseAddress.ToInt64():X}");
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        Log("WARNING: DLL not found in module list (might be manual mapped or unloaded)");
                    }
                }
                else
                {
                    Log($"FAILED: {testProcess.LastInjectionError}");
                }

                testProcess.Close();
            }
            catch (Exception ex)
            {
                Log($"EXCEPTION: {ex.Message}");
                Log(ex.StackTrace ?? "");
            }
            finally
            {
                if (notepad != null && !notepad.HasExited)
                {
                    Log("Closing notepad...");
                    notepad.Kill();
                }
            }
        }

        private void BtnTestAttached_Click(object? sender, EventArgs e)
        {
            if (_process == null || _process.Target == null)
            {
                MessageBox.Show("No process attached!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _txtOutput.Clear();
            Log($"=== Injection Test on {_process.Target.ProcessName} ===");
            Log($"PID: {_process.Target.Id}");
            Log($"Is64Bit detected: {_process.Is64Bit}");

            string dllName = _process.Is64Bit ? "VEHDebug64.dll" : "VEHDebug32.dll";
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);

            Log($"Will inject: {dllName}");
            Log($"Full path: {dllPath}");

            if (!File.Exists(dllPath))
            {
                Log($"ERROR: DLL not found!");
                return;
            }

            // Check if already injected and list modules
            _process.InvalidateModuleCache();
            var modulesBefore = _process.GetModules(true);
            Log($"Found {modulesBefore.Count} modules in target process");

            bool foundKernel32 = false;
            foreach (var mod in modulesBefore)
            {
                if (mod.ModuleName.Contains("VEHDebug", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Already loaded: {mod.ModuleName} at 0x{mod.BaseAddress.ToInt64():X}");
                    return;
                }
                if (mod.ModuleName.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"kernel32.dll found at: 0x{mod.BaseAddress.ToInt64():X}");
                    foundKernel32 = true;
                }
            }

            if (!foundKernel32)
            {
                Log("WARNING: kernel32.dll NOT found in module list!");
                Log("Listing all modules:");
                foreach (var mod in modulesBefore)
                {
                    Log($"  {mod.ModuleName} at 0x{mod.BaseAddress.ToInt64():X}");
                }
            }

            Log("Attempting injection...");
            IntPtr result = _process.InjectDll(dllPath);

            if (result != IntPtr.Zero)
            {
                Log($"SUCCESS! DLL loaded at: 0x{result.ToInt64():X}");
            }
            else
            {
                Log($"FAILED: {_process.LastInjectionError}");

                // Additional diagnostics
                Log("");
                Log("=== Additional Diagnostics ===");

                // Check if we can allocate memory
                Log("Testing VirtualAllocEx...");
                IntPtr testAlloc = VirtualAllocEx(_process.Handle, IntPtr.Zero, 4096, 0x3000, 0x04);
                if (testAlloc != IntPtr.Zero)
                {
                    Log($"  VirtualAllocEx succeeded: 0x{testAlloc.ToInt64():X}");
                    VirtualFreeEx(_process.Handle, testAlloc, 0, 0x8000);
                }
                else
                {
                    Log($"  VirtualAllocEx FAILED: {Marshal.GetLastWin32Error()}");
                }

                // Check if we can create remote thread
                Log("Testing CreateRemoteThread with GetCurrentThread...");
                IntPtr kernel32 = GetModuleHandle("kernel32.dll");
                IntPtr getCurrentThread = GetProcAddress(kernel32, "GetCurrentThread");
                if (getCurrentThread != IntPtr.Zero)
                {
                    IntPtr hThread = CreateRemoteThread(_process.Handle, IntPtr.Zero, 0, getCurrentThread, IntPtr.Zero, 0, out _);
                    if (hThread != IntPtr.Zero)
                    {
                        Log($"  CreateRemoteThread succeeded");
                        WaitForSingleObject(hThread, 1000);
                        CloseHandle(hThread);
                    }
                    else
                    {
                        Log($"  CreateRemoteThread FAILED: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
        }

        private void ApplyTheme()
        {
            BackColor = ThemeManager.Background;
            ForeColor = ThemeManager.Foreground;
            _txtOutput.BackColor = ThemeManager.BackgroundAlt;
            _txtOutput.ForeColor = ThemeManager.Foreground;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
