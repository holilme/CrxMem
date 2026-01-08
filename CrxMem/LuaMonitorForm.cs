using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CrxMem.Core;

namespace CrxMem
{
    public partial class LuaMonitorForm : Form
    {
        private ProcessAccess _process;
        private LuaDetector.LuaInfo? _luaInfo;
        private NamedPipeServerStream? _pipeServer;
        private CancellationTokenSource? _cancellationToken;
        private bool _isMonitoring = false;

        // UI Controls
        private Label lblProcessInfo;
        private Label lblLuaDetected;
        private Label lblStatus;
        private GroupBox gbStealthOptions;
        private CheckBox chkManualMap;
        private CheckBox chkErasePE;
        private CheckBox chkUnlinkPEB;
        private CheckBox chkRandomize;
        private Button btnInject;
        private Button btnStop;
        private Button btnClear;
        private Button btnExport;
        private DataGridView dgvEvents;
        private TextBox txtScript;
        private Button btnExecute;
        private TabControl tabControl;

        public LuaMonitorForm(ProcessAccess process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            InitializeComponent();
            DetectLua();
        }

        private void InitializeComponent()
        {
            this.Text = "LUA Function Monitor";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormClosing += LuaMonitorForm_FormClosing;

            // Top panel - Process info
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BorderStyle = BorderStyle.FixedSingle
            };

            lblProcessInfo = new Label
            {
                Location = new Point(10, 10),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            lblLuaDetected = new Label
            {
                Location = new Point(10, 35),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 9)
            };

            lblStatus = new Label
            {
                Location = new Point(10, 55),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray,
                Text = "Status: Not injected"
            };

            topPanel.Controls.AddRange(new Control[] { lblProcessInfo, lblLuaDetected, lblStatus });

            // Stealth options panel
            gbStealthOptions = new GroupBox
            {
                Text = "Stealth Injection Options",
                Location = new Point(420, 10),
                Size = new Size(550, 65)
            };

            chkManualMap = new CheckBox
            {
                Text = "Manual PE Mapping",
                Location = new Point(10, 20),
                Size = new Size(150, 20),
                Checked = true
            };

            chkErasePE = new CheckBox
            {
                Text = "Erase PE Headers",
                Location = new Point(10, 42),
                Size = new Size(150, 20),
                Checked = true
            };

            chkUnlinkPEB = new CheckBox
            {
                Text = "Unlink from PEB",
                Location = new Point(170, 20),
                Size = new Size(150, 20),
                Checked = true
            };

            chkRandomize = new CheckBox
            {
                Text = "Randomize Sections",
                Location = new Point(170, 42),
                Size = new Size(150, 20),
                Checked = false
            };

            btnInject = new Button
            {
                Text = "Inject && Start Monitoring",
                Location = new Point(340, 20),
                Size = new Size(180, 35),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnInject.Click += BtnInject_Click;

            gbStealthOptions.Controls.AddRange(new Control[] {
                chkManualMap, chkErasePE, chkUnlinkPEB, chkRandomize, btnInject
            });

            topPanel.Controls.Add(gbStealthOptions);

            // Tab control for monitor and script injection
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // Monitor tab
            var tabMonitor = new TabPage("Function Monitor");

            dgvEvents = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            dgvEvents.Columns.Add("Time", "Timestamp");
            dgvEvents.Columns.Add("Event", "Event Type");
            dgvEvents.Columns.Add("Function", "Function");
            dgvEvents.Columns.Add("Args", "Args");
            dgvEvents.Columns.Add("Results", "Results");
            dgvEvents.Columns.Add("Script", "Script Content");

            dgvEvents.Columns[0].Width = 100;
            dgvEvents.Columns[1].Width = 120;
            dgvEvents.Columns[2].Width = 150;
            dgvEvents.Columns[3].Width = 60;
            dgvEvents.Columns[4].Width = 60;
            dgvEvents.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            tabMonitor.Controls.Add(dgvEvents);

            // Script injection tab
            var tabScript = new TabPage("Script Injection");

            var lblScriptHelp = new Label
            {
                Text = "Enter LUA script to execute in target process:",
                Location = new Point(10, 10),
                Size = new Size(400, 20)
            };

            txtScript = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 10),
                Location = new Point(10, 35),
                Size = new Size(940, 450)
            };
            txtScript.Text = "-- Example LUA script\nprint(\"Hello from injected script!\")\n";

            btnExecute = new Button
            {
                Text = "Execute in Target Process",
                Location = new Point(10, 495),
                Size = new Size(200, 30),
                Enabled = false
            };
            btnExecute.Click += BtnExecute_Click;

            tabScript.Controls.AddRange(new Control[] { lblScriptHelp, txtScript, btnExecute });

            tabControl.TabPages.Add(tabMonitor);
            tabControl.TabPages.Add(tabScript);

            // Bottom button panel
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnStop = new Button
            {
                Text = "Stop Monitoring",
                Location = new Point(10, 8),
                Size = new Size(130, 28),
                Enabled = false
            };
            btnStop.Click += BtnStop_Click;

            btnClear = new Button
            {
                Text = "Clear Log",
                Location = new Point(150, 8),
                Size = new Size(100, 28)
            };
            btnClear.Click += (s, e) => dgvEvents.Rows.Clear();

            btnExport = new Button
            {
                Text = "Export to CSV",
                Location = new Point(260, 8),
                Size = new Size(120, 28)
            };
            btnExport.Click += BtnExport_Click;

            bottomPanel.Controls.AddRange(new Control[] { btnStop, btnClear, btnExport });

            // Add all to form
            this.Controls.Add(tabControl);
            this.Controls.Add(topPanel);
            this.Controls.Add(bottomPanel);
        }

        private void DetectLua()
        {
            lblProcessInfo.Text = $"Process: {_process.Target.ProcessName} (PID: {_process.Target.Id})";

            _luaInfo = LuaDetector.Detect(_process);

            if (_luaInfo.IsDetected)
            {
                string version = LuaDetector.GetVersionString(_luaInfo.Version);
                lblLuaDetected.Text = $"✓ LUA Detected: {version} ({_luaInfo.ModuleName})";
                lblLuaDetected.ForeColor = Color.Green;
            }
            else
            {
                lblLuaDetected.Text = "✗ LUA Not Detected in process";
                lblLuaDetected.ForeColor = Color.Red;
                btnInject.Enabled = false;
                MessageBox.Show("LUA runtime not detected in this process. This feature only works with LUA-based games.",
                    "LUA Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void BtnInject_Click(object? sender, EventArgs e)
        {
            if (_isMonitoring)
                return;

            try
            {
                btnInject.Enabled = false;
                lblStatus.Text = "Status: Injecting...";
                lblStatus.ForeColor = Color.Orange;

                // Get stealth options
                var options = new StealthOptions
                {
                    ManualMap = chkManualMap.Checked,
                    ErasePEHeader = chkErasePE.Checked,
                    UnlinkFromPEB = chkUnlinkPEB.Checked,
                    RandomizeSectionNames = chkRandomize.Checked
                };

                // Determine DLL path (AudioBridge64.dll or AudioBridge32.dll)
                string dllName = _process.Is64Bit ? "AudioBridge64.dll" : "AudioBridge32.dll";
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);

                if (!File.Exists(dllPath))
                {
                    MessageBox.Show($"Audio bridge library not found: {dllName}\n\nPlease ensure the C++ component is built.",
                        "DLL Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    lblStatus.Text = "Status: Injection failed - DLL not found";
                    lblStatus.ForeColor = Color.Red;
                    btnInject.Enabled = true;
                    return;
                }

                // Inject DLL
                var injector = new StealthInjector(_process);
                IntPtr moduleBase = await Task.Run(() => injector.InjectDLL(dllPath, options));

                if (moduleBase == IntPtr.Zero)
                {
                    throw new Exception("Injection failed - returned NULL base address");
                }

                // Start named pipe server for IPC
                await StartMonitoring();

                lblStatus.Text = $"Status: Monitoring active (Base: 0x{moduleBase.ToInt64():X})";
                lblStatus.ForeColor = Color.Green;
                btnStop.Enabled = true;
                btnExecute.Enabled = true;
                _isMonitoring = true;

                // Disable stealth options after injection
                gbStealthOptions.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Injection failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Status: Injection failed";
                lblStatus.ForeColor = Color.Red;
                btnInject.Enabled = true;
            }
        }

        private async Task StartMonitoring()
        {
            _cancellationToken = new CancellationTokenSource();

            // Create named pipe server
            string pipeName = $"crxmem_lua_pid{_process.Target.Id}";
            _pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            // Start monitoring task
            _ = Task.Run(async () =>
            {
                try
                {
                    await _pipeServer.WaitForConnectionAsync(_cancellationToken.Token);

                    while (!_cancellationToken.Token.IsCancellationRequested)
                    {
                        // Read LuaEvent struct from pipe
                        byte[] buffer = new byte[4096];
                        int bytesRead = await _pipeServer.ReadAsync(buffer, 0, buffer.Length, _cancellationToken.Token);

                        if (bytesRead > 0)
                        {
                            ProcessLuaEvent(buffer);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                }
                catch (Exception ex)
                {
                    this.Invoke(() =>
                    {
                        lblStatus.Text = $"Status: Monitoring error - {ex.Message}";
                        lblStatus.ForeColor = Color.Red;
                    });
                }
            }, _cancellationToken.Token);
        }

        private void ProcessLuaEvent(byte[] data)
        {
            try
            {
                // Parse LuaEvent struct (matches C++ structure)
                int offset = 0;
                uint eventType = BitConverter.ToUInt32(data, offset); offset += 4;
                uint timestamp = BitConverter.ToUInt32(data, offset); offset += 4;
                uint threadId = BitConverter.ToUInt32(data, offset); offset += 4;
                ulong luaStatePtr = BitConverter.ToUInt64(data, offset); offset += 8;
                int numArgs = BitConverter.ToInt32(data, offset); offset += 4;
                int numResults = BitConverter.ToInt32(data, offset); offset += 4;

                string functionName = System.Text.Encoding.ASCII.GetString(data, offset, 256).TrimEnd('\0'); offset += 256;
                string scriptContent = System.Text.Encoding.ASCII.GetString(data, offset, 4096).TrimEnd('\0');

                // Add to DataGridView
                this.Invoke(() =>
                {
                    string eventTypeName = eventType switch
                    {
                        1 => "gettop",
                        2 => "pcall",
                        3 => "loadstring",
                        4 => "newstate",
                        _ => "unknown"
                    };

                    string time = DateTime.Now.ToString("HH:mm:ss.fff");

                    dgvEvents.Rows.Add(
                        time,
                        eventTypeName,
                        functionName,
                        numArgs.ToString(),
                        numResults.ToString(),
                        scriptContent
                    );

                    // Auto-scroll to latest
                    if (dgvEvents.Rows.Count > 0)
                    {
                        dgvEvents.FirstDisplayedScrollingRowIndex = dgvEvents.Rows.Count - 1;
                    }
                });
            }
            catch
            {
                // Failed to parse event
            }
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            StopMonitoring();
        }

        private void StopMonitoring()
        {
            _cancellationToken?.Cancel();
            _pipeServer?.Close();
            _pipeServer?.Dispose();
            _pipeServer = null;

            _isMonitoring = false;
            btnStop.Enabled = false;
            btnExecute.Enabled = false;
            btnInject.Enabled = true;
            gbStealthOptions.Enabled = true;

            lblStatus.Text = "Status: Monitoring stopped";
            lblStatus.ForeColor = Color.Gray;
        }

        private void BtnExecute_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtScript.Text))
            {
                MessageBox.Show("Please enter a LUA script to execute.", "No Script",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Send script execution command via pipe
            // This would require bidirectional communication
            MessageBox.Show("Script injection feature coming soon!", "Info",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"lua_monitor_{_process.Target.ProcessName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    using var writer = new System.IO.StreamWriter(sfd.FileName);

                    // Write header
                    writer.WriteLine("Timestamp,Event,Function,Args,Results,Script");

                    // Write rows
                    foreach (DataGridViewRow row in dgvEvents.Rows)
                    {
                        if (!row.IsNewRow)
                        {
                            var values = new string[6];
                            for (int i = 0; i < 6; i++)
                            {
                                values[i] = row.Cells[i].Value?.ToString()?.Replace("\"", "\"\"") ?? "";
                            }
                            writer.WriteLine($"\"{string.Join("\",\"", values)}\"");
                        }
                    }

                    MessageBox.Show("Log exported successfully!", "Export Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void LuaMonitorForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            StopMonitoring();
        }
    }
}
