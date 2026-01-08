using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using CrxMem.Core;
using CrxShield;

namespace CrxMem
{
    public partial class MainForm : Form
    {
        // P/Invoke for global hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        // Hotkey tracking
        private Dictionary<int, string> _hotkeyIdToFunction = new();
        private int _nextHotkeyId = 1;

        private ProcessAccess? _process;
        private MemoryScanner? _scanner;
        private System.Windows.Forms.Timer? _freezeTimer;
        private System.Windows.Forms.Timer? _autoAttachTimer;
        private FastProcessWatcher? _fastProcessWatcher;
        private bool _liveUpdatesEnabled = true;

        // Singleton form instances to prevent multiple windows
        private MemoryView.MemoryViewForm? _memoryViewForm;
        private AntiCheatBypassForm? _acBypassForm;

        // Driver status indicator controls
        private Panel? _driverStatusPanel;
        private Label? _driverStatusLabel;
        private Button? _btnQuickUnloadDriver;

        // Tooltip for UI hints
        private ToolTip _toolTip = new ToolTip();

        public MainForm()
        {
            InitializeComponent();

            // Set window title with version
            string version = Application.ProductVersion;
            // Trim any trailing .0.0 from version for cleaner display
            if (version.EndsWith(".0.0")) version = version[..^4];
            else if (version.EndsWith(".0")) version = version[..^2];
            this.Text = $"CrxMem v{version}";

            cmbScanType.SelectedIndex = 0; // Exact value
            cmbValueType.SelectedIndex = 3; // 4 Bytes

            // Apply theme
            ThemeManager.ApplyTheme(this);

            // Set application icon (window and taskbar) from embedded resources
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("favicon.ico"))
                {
                    if (stream != null)
                    {
                        this.Icon = new Icon(stream);
                    }
                }
            }
            catch { /* Ignore if icon resource not found */ }

            // Load Open Process icon from embedded resources
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("openprocess128x128.png"))
                {
                    if (stream != null)
                    {
                        using (var img = Image.FromStream(stream))
                        {
                            btnOpenProcess.Image = new Bitmap(img, new Size(16, 16)); // Scale to 16x16 for toolbar
                        }
                    }
                }
            }
            catch { /* Ignore if icon resource not found */ }

            // Load Load Table icon from embedded resources
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("_load128x128.png"))
                {
                    if (stream != null)
                    {
                        using (var img = Image.FromStream(stream))
                        {
                            btnLoadTable.Image = new Bitmap(img, new Size(16, 16));
                        }
                    }
                }
            }
            catch { /* Ignore if icon resource not found */ }

            // Load Save Table icon from embedded resources
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("save128x128.png"))
                {
                    if (stream != null)
                    {
                        using (var img = Image.FromStream(stream))
                        {
                            btnSaveTable.Image = new Bitmap(img, new Size(16, 16));
                        }
                    }
                }
            }
            catch { /* Ignore if icon resource not found */ }

            // Load logo image in top right corner from embedded resources
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("android-chrome-192x192.png"))
                {
                    if (stream != null)
                    {
                        picLogo.Image = Image.FromStream(stream);
                        picLogo.BringToFront(); // Ensure logo is visible on top
                    }
                }
            }
            catch { /* Ignore if logo resource not found */ }

            // Make process label clickable to reopen process selector (like Cheat Engine)
            lblProcessTitle.Cursor = Cursors.Hand;
            lblProcessTitle.Click += (s, e) => OpenProcess_Click(s, e);

            // Initialize freeze timer for locking values
            _freezeTimer = new System.Windows.Forms.Timer();
            _freezeTimer.Interval = SettingsManager.FreezeInterval; // Use saved setting
            _freezeTimer.Tick += FreezeTimer_Tick;
            _freezeTimer.Start();

            // Apply saved settings
            ApplySettings();

            // Register global hotkeys from settings
            RegisterGlobalHotkeys();

            // Initialize auto-attach timer (checks every 2 seconds like Cheat Engine)
            _autoAttachTimer = new System.Windows.Forms.Timer();
            _autoAttachTimer.Interval = 2000;
            _autoAttachTimer.Tick += AutoAttachTimer_Tick;

            // Start auto-attach if configured
            if (!string.IsNullOrEmpty(SettingsManager.AutoAttachProcess))
            {
                if (SettingsManager.FastAutoAttach)
                {
                    // Use fast process watcher (WMI or high-speed polling)
                    StartFastAutoAttach();
                }
                else
                {
                    // Use legacy slow timer
                    _autoAttachTimer.Start();
                    TryAutoAttach();
                }
            }

            // Add context menu to scan results
            var scanContextMenu = new ContextMenuStrip();
            var copyAddressItem = new ToolStripMenuItem("Copy Address");
            copyAddressItem.Click += (s, e) =>
            {
                if (lvFoundAddresses.SelectedItems.Count > 0)
                {
                    Clipboard.SetText(lvFoundAddresses.SelectedItems[0].Text);
                }
            };
            var browseMemoryItem = new ToolStripMenuItem("Browse this memory region");
            browseMemoryItem.Click += (s, e) =>
            {
                if (lvFoundAddresses.SelectedItems.Count > 0 && _process != null)
                {
                    string addrText = lvFoundAddresses.SelectedItems[0].Text.Replace("0x", "");
                    if (IntPtr.TryParse(addrText, System.Globalization.NumberStyles.HexNumber, null, out IntPtr addr))
                    {
                        OpenOrFocusMemoryView(addr);
                    }
                }
            };
            var addToListItem = new ToolStripMenuItem("Add to address list");
            addToListItem.Click += (s, e) => AddSelectedFoundAddressesToList();
            scanContextMenu.Items.AddRange(new ToolStripItem[] { copyAddressItem, browseMemoryItem, new ToolStripSeparator(), addToListItem });
            lvFoundAddresses.ContextMenuStrip = scanContextMenu;

            // Create "Add to list" button with down arrow image (like Cheat Engine)
            // Positioned between Memory View and Add Address Manually buttons
            var btnAddToList = new Button
            {
                Size = new Size(32, 23),
                Location = new Point(110, 456),  // Between Memory View (at 3) and Add Address Manually (at 274)
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnAddToList.FlatAppearance.BorderSize = 1;

            // Load arrow image from embedded resources
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("arrow128x128.png"))
                {
                    if (stream != null)
                    {
                        using (var img = Image.FromStream(stream))
                        {
                            btnAddToList.Image = new Bitmap(img, new Size(20, 20));
                        }
                    }
                    else
                    {
                        btnAddToList.Text = "\u25BC";  // Fallback to Unicode arrow
                    }
                }
            }
            catch
            {
                btnAddToList.Text = "\u25BC";  // Fallback to Unicode arrow
            }

            btnAddToList.Click += (s, e) => AddSelectedFoundAddressesToList();
            _toolTip.SetToolTip(btnAddToList, "Add selected address(es) to the list below (Ctrl+Enter)");
            panelTop.Controls.Add(btnAddToList);

            // Add context menu to address list
            var addressContextMenu = new ContextMenuStrip();
            var toggleFreezeItem = new ToolStripMenuItem("Toggle Freeze");
            toggleFreezeItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    entry.Frozen = !entry.Frozen;
                    if (entry.Frozen)
                    {
                        entry.FrozenValue = ReadValueAsString(entry.Address);
                    }
                    UpdateAddressList();
                }
            };
            var toggleHexItem = new ToolStripMenuItem("Show as Hex");
            toggleHexItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    entry.ShowAsHex = !entry.ShowAsHex;
                    var item = lvAddressList.SelectedItems[0];
                    string rawValue = ReadValueAsString(entry.Address);
                    item.SubItems[4].Text = FormatValue(rawValue, entry.Type, entry.ShowAsHex);
                }
            };
            var copyAddressListItem = new ToolStripMenuItem("Copy Address");
            copyAddressListItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    Clipboard.SetText($"0x{entry.Address.ToInt64():X}");
                }
            };
            var copyValueItem = new ToolStripMenuItem("Copy Value");
            copyValueItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    string value = ReadValueAsString(entry.Address);
                    Clipboard.SetText(value);
                }
            };
            var copyDescriptionItem = new ToolStripMenuItem("Copy Description");
            copyDescriptionItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry &&
                    !string.IsNullOrEmpty(entry.Description))
                {
                    Clipboard.SetText(entry.Description);
                }
            };
            var incrementValueItem = new ToolStripMenuItem("Increase Value (+1)");
            incrementValueItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    ModifyValue(entry, 1);
                    UpdateAddressList();
                }
            };
            var decrementValueItem = new ToolStripMenuItem("Decrease Value (-1)");
            decrementValueItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    ModifyValue(entry, -1);
                    UpdateAddressList();
                }
            };
            var changeValueItem = new ToolStripMenuItem("Change Value...");
            changeValueItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0 &&
                    lvAddressList.SelectedItems[0].Tag is AddressEntry entry)
                {
                    string currentValue = ReadValueAsString(entry.Address);
                    var dialog = new ChangeValueDialog(currentValue, entry.Type);
                    if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(dialog.NewValue))
                    {
                        WriteValueFromString(entry.Address, entry.Type, dialog.NewValue);
                        entry.FrozenValue = dialog.NewValue; // Update frozen value so it doesn't get reverted
                        UpdateAddressList();
                    }
                }
            };
            var deleteItem = new ToolStripMenuItem("Delete");
            deleteItem.ShortcutKeys = Keys.Delete;
            deleteItem.Click += (s, e) =>
            {
                if (lvAddressList.SelectedItems.Count > 0)
                {
                    // Remove all selected items (multi-select support)
                    var itemsToRemove = new List<ListViewItem>();
                    foreach (ListViewItem item in lvAddressList.SelectedItems)
                    {
                        itemsToRemove.Add(item);
                    }
                    foreach (var item in itemsToRemove)
                    {
                        lvAddressList.Items.Remove(item);
                    }
                }
            };

            // Debug/monitoring menu items
            var findWhatWritesItem = new ToolStripMenuItem("Find out what writes to this address");
            findWhatWritesItem.Click += (s, e) => FindWhatAccesses(writeOnly: true);

            var findWhatAccessesItem = new ToolStripMenuItem("Find out what accesses this address");
            findWhatAccessesItem.Click += (s, e) => FindWhatAccesses(writeOnly: false);

            addressContextMenu.Items.AddRange(new ToolStripItem[] {
                toggleFreezeItem, toggleHexItem,
                new ToolStripSeparator(),
                incrementValueItem, decrementValueItem, changeValueItem,
                new ToolStripSeparator(),
                findWhatWritesItem, findWhatAccessesItem,
                new ToolStripSeparator(),
                copyAddressListItem, copyValueItem, copyDescriptionItem,
                new ToolStripSeparator(),
                deleteItem
            });
            lvAddressList.ContextMenuStrip = addressContextMenu;

            // Handle Delete key on address list for multi-delete
            lvAddressList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && lvAddressList.SelectedItems.Count > 0)
                {
                    var itemsToRemove = new List<ListViewItem>();
                    foreach (ListViewItem item in lvAddressList.SelectedItems)
                    {
                        itemsToRemove.Add(item);
                    }
                    foreach (var item in itemsToRemove)
                    {
                        lvAddressList.Items.Remove(item);
                    }
                    e.Handled = true;
                }
            };

            // Initialize driver status panel (hidden by default)
            InitializeDriverStatusPanel();

            // Check if driver is already loaded from a previous session
            CheckExistingDriverConnection();
        }

        /// <summary>
        /// Check if the CrxShield driver is already loaded and try to reconnect
        /// </summary>
        private void CheckExistingDriverConnection()
        {
            try
            {
                // Try to connect to an existing driver
                var controller = new CrxDriverController();
                if (controller.Connect())
                {
                    // Driver is already loaded! Store it and update UI
                    AntiCheatBypassForm.SetDriverController(controller);
                    UpdateDriverStatusIndicator();

                    System.Diagnostics.Debug.WriteLine("[MainForm] Reconnected to existing CrxShield driver");
                }
                else
                {
                    controller.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainForm] No existing driver found: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize the driver status indicator panel
        /// </summary>
        private void InitializeDriverStatusPanel()
        {
            _driverStatusPanel = new Panel
            {
                Height = 28,
                Dock = DockStyle.Bottom,
                BackColor = Color.FromArgb(40, 80, 40),
                Visible = false
            };

            _driverStatusLabel = new Label
            {
                Text = "Driver: CrxShield Loaded",
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(10, 6),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _btnQuickUnloadDriver = new Button
            {
                Text = "Unload Driver",
                Size = new Size(100, 22),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(150, 50, 50),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnQuickUnloadDriver.FlatAppearance.BorderColor = Color.DarkRed;
            _btnQuickUnloadDriver.Click += BtnQuickUnloadDriver_Click;

            // Position button on the right side
            _driverStatusPanel.Controls.Add(_driverStatusLabel);
            _driverStatusPanel.Controls.Add(_btnQuickUnloadDriver);

            // Adjust button position when panel resizes
            _driverStatusPanel.Resize += (s, e) =>
            {
                if (_btnQuickUnloadDriver != null && _driverStatusPanel != null)
                {
                    _btnQuickUnloadDriver.Location = new Point(_driverStatusPanel.Width - _btnQuickUnloadDriver.Width - 10, 3);
                }
            };

            this.Controls.Add(_driverStatusPanel);
        }

        /// <summary>
        /// Update driver status indicator visibility
        /// </summary>
        public void UpdateDriverStatusIndicator()
        {
            if (_driverStatusPanel == null) return;

            bool driverLoaded = AntiCheatBypassForm.DriverController?.IsConnected() ?? false;

            _driverStatusPanel.Visible = driverLoaded;

            if (driverLoaded && _driverStatusLabel != null)
            {
                _driverStatusLabel.Text = "Driver: CrxShield Loaded";
            }
        }

        /// <summary>
        /// Quick unload driver button click handler - opens AC Bypass form to unload
        /// </summary>
        private void BtnQuickUnloadDriver_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "To properly unload the driver, you need to use the AC Bypass Research Lab.\n\n" +
                "Would you like to open it now?",
                "Unload Driver",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                OpenOrFocusACBypassForm();
            }
        }

        /// <summary>
        /// Open or focus the Memory View form (singleton pattern)
        /// </summary>
        private void OpenOrFocusMemoryView(IntPtr startAddress)
        {
            if (_process == null)
            {
                MessageBox.Show("No process attached.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check if form exists and is not disposed
            if (_memoryViewForm != null && !_memoryViewForm.IsDisposed)
            {
                // Navigate to the new address and bring to front
                _memoryViewForm.NavigateToAddress(startAddress);
                _memoryViewForm.BringToFront();
                _memoryViewForm.Focus();
            }
            else
            {
                // Create new instance
                _memoryViewForm = new MemoryView.MemoryViewForm(_process, startAddress);
                _memoryViewForm.FormClosed += (s, e) => _memoryViewForm = null;
                _memoryViewForm.Show();
            }
        }

        /// <summary>
        /// Open or focus the AC Bypass form (singleton pattern)
        /// </summary>
        private void OpenOrFocusACBypassForm()
        {
            // Check if form exists and is not disposed
            if (_acBypassForm != null && !_acBypassForm.IsDisposed)
            {
                _acBypassForm.BringToFront();
                _acBypassForm.Focus();
            }
            else
            {
                // Create new instance
                _acBypassForm = new AntiCheatBypassForm(_process);
                _acBypassForm.FormClosed += (s, e) =>
                {
                    _acBypassForm = null;
                    // Update driver status when AC Bypass form closes
                    UpdateDriverStatusIndicator();
                };
                // Update driver status when form loads driver
                _acBypassForm.DriverStatusChanged += (s, e) => UpdateDriverStatusIndicator();
                _acBypassForm.Show();
            }
        }


        private void OpenProcess_Click(object? sender, EventArgs e)
        {
            ProcessSelectDialog dialog = new ProcessSelectDialog();
            if (dialog.ShowDialog() == DialogResult.OK && dialog.SelectedProcess != null)
            {
                try
                {
                    _process?.Dispose();
                    _process = new ProcessAccess();

                    if (_process.Open(dialog.SelectedProcess.Id))
                    {
                        lblProcessTitle.Text = $"{dialog.SelectedProcess.ProcessName} - PID: {dialog.SelectedProcess.Id} ({(_process.Is64Bit ? "64" : "32")}-bit)";
                        lblProcessTitle.ForeColor = Color.MediumSeaGreen;

                        // Update window title to show attached process (like Cheat Engine) with version
                        string ver = Application.ProductVersion;
                        if (ver.EndsWith(".0.0")) ver = ver[..^4];
                        else if (ver.EndsWith(".0")) ver = ver[..^2];
                        this.Text = $"CrxMem v{ver} - {dialog.SelectedProcess.ProcessName} (PID: {dialog.SelectedProcess.Id})";

                        _scanner = new MemoryScanner(_process);

                        // Hook up progress events (use BeginInvoke to avoid blocking)
                        _scanner.StatusChanged += (status) =>
                        {
                            try
                            {
                                if (InvokeRequired)
                                    BeginInvoke(() => { if (!IsDisposed) lblFound.Text = status; });
                                else
                                    lblFound.Text = status;
                            }
                            catch { /* Form may be disposed */ }
                        };

                        _scanner.ProgressChanged += (current, total) =>
                        {
                            try
                            {
                                if (InvokeRequired)
                                    BeginInvoke(() =>
                                    {
                                        if (!IsDisposed)
                                        {
                                            progressBarScan.Maximum = total;
                                            progressBarScan.Value = Math.Min(current, total);
                                        }
                                    });
                                else
                                {
                                    progressBarScan.Maximum = total;
                                    progressBarScan.Value = Math.Min(current, total);
                                }
                            }
                            catch { /* Form may be disposed */ }
                        };

                        // Populate module dropdown (like Cheat Engine)
                        PopulateModuleDropdown();

                        btnFirstScan.Enabled = true;
                    }
                    else
                    {
                        MessageBox.Show("Failed to open process. Try running as Administrator.",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening process: {ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Exit_Click(object? sender, EventArgs e)
        {
            Application.Exit();
        }

        private void Settings_Click(object? sender, EventArgs e)
        {
            using var settingsForm = new SettingsForm();
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                // Re-apply settings after they're saved
                ApplySettings();

                // Re-register global hotkeys with new settings
                RegisterGlobalHotkeys();

                // Update auto-attach
                RestartAutoAttach();
            }
        }

        /// <summary>
        /// Restart auto-attach with current settings (fast or legacy mode)
        /// </summary>
        private void RestartAutoAttach()
        {
            // Stop any existing watchers
            _autoAttachTimer?.Stop();
            _fastProcessWatcher?.Stop();

            if (!string.IsNullOrEmpty(SettingsManager.AutoAttachProcess))
            {
                if (SettingsManager.FastAutoAttach)
                {
                    StartFastAutoAttach();
                }
                else
                {
                    _autoAttachTimer?.Start();
                }
            }
        }

        /// <summary>
        /// Start the fast process watcher (WMI or high-speed polling)
        /// </summary>
        private void StartFastAutoAttach()
        {
            // Auto-load driver if configured
            if (SettingsManager.AutoLoadDriver)
            {
                try
                {
                    var driverPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(Application.ExecutablePath) ?? "",
                        "CrxShield.sys"
                    );
                    if (System.IO.File.Exists(driverPath))
                    {
                        var driverManager = new KernelDriverManager();
                        if (!driverManager.IsDriverLoaded)
                        {
                            driverManager.LoadDriver();
                            System.Diagnostics.Debug.WriteLine("CrxShield driver auto-loaded");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to auto-load driver: {ex.Message}");
                }
            }

            // Create fast watcher if needed
            if (_fastProcessWatcher == null)
            {
                _fastProcessWatcher = new FastProcessWatcher();
                _fastProcessWatcher.ProcessDetected += FastProcessWatcher_ProcessDetected;
                _fastProcessWatcher.ProcessSuspended += FastProcessWatcher_ProcessSuspended;
                _fastProcessWatcher.StatusChanged += (status) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[FastWatcher] {status}");
                };
            }

            // Configure polling interval for fallback mode
            _fastProcessWatcher.PollingIntervalMs = SettingsManager.FastAutoAttachInterval;

            // Configure immediate suspend (for anti-cheat bypass)
            _fastProcessWatcher.ImmediatelySuspend = SettingsManager.FastSuspendOnAttach;

            // Parse process names
            var processNames = SettingsManager.AutoAttachProcess
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p));

            // Start watching
            _fastProcessWatcher.Start(processNames, SettingsManager.UseWmiProcessWatch);
        }

        /// <summary>
        /// Handle fast process suspension notification
        /// </summary>
        private void FastProcessWatcher_ProcessSuspended(int processId, bool success)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => FastProcessWatcher_ProcessSuspended(processId, success));
                return;
            }

            if (success)
            {
                // Update status to show process is suspended
                lblProcessTitle.Text = $"Process {processId} SUSPENDED (waiting for attach)";
                lblProcessTitle.ForeColor = Color.Orange;
                System.Diagnostics.Debug.WriteLine($"[FastWatcher] Process {processId} suspended successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[FastWatcher] Failed to suspend process {processId}");
            }
        }

        /// <summary>
        /// Handle fast process detection
        /// </summary>
        private void FastProcessWatcher_ProcessDetected(Process process)
        {
            // This is called from a background thread, so invoke on UI thread
            if (InvokeRequired)
            {
                BeginInvoke(() => HandleFastProcessDetected(process));
            }
            else
            {
                HandleFastProcessDetected(process);
            }
        }

        /// <summary>
        /// Handle detected process on UI thread
        /// </summary>
        private void HandleFastProcessDetected(Process process)
        {
            try
            {
                // Don't attach to ourselves
                if (process.Id == Environment.ProcessId)
                    return;

                // Don't re-attach to same process
                if (_process != null && _process.IsOpen && _process.Target?.Id == process.Id)
                    return;

                System.Diagnostics.Debug.WriteLine($"[FastWatcher] Attaching to {process.ProcessName} (PID: {process.Id})");

                // Attach to the process
                AttachToProcess(process);

                // Auto-inject VEH if configured
                if (SettingsManager.AutoInjectVEH && _process != null && _process.IsOpen)
                {
                    try
                    {
                        var injector = new StealthInjector(_process);
                        string dllName = _process.Is64Bit ? "VEHDebug64.dll" : "VEHDebug32.dll";
                        string dllPath = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(Application.ExecutablePath) ?? "",
                            dllName
                        );

                        if (System.IO.File.Exists(dllPath))
                        {
                            var result = injector.InjectDLL(dllPath, new StealthOptions());
                            bool injected = result != IntPtr.Zero;
                            System.Diagnostics.Debug.WriteLine($"[FastWatcher] VEH DLL injection: {(injected ? "SUCCESS" : "FAILED")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FastWatcher] VEH injection error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FastWatcher] Attach error: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply saved settings from SettingsManager
        /// </summary>
        private void ApplySettings()
        {
            // Calculate interval multiplier for kernel mode
            int multiplier = 1;
            if (AntiCheatBypassForm.UseKernelMode)
            {
                multiplier = Math.Max(1, SettingsManager.KernelModeUpdateMultiplier);
            }

            // Apply timer intervals (with kernel mode adjustment)
            if (_freezeTimer != null)
                _freezeTimer.Interval = Math.Max(10, SettingsManager.FreezeInterval * multiplier);

            if (updateTimer != null)
                updateTimer.Interval = Math.Max(100, SettingsManager.UpdateInterval * multiplier);

            // Apply Fast Scan default
            chkFastScan.Checked = SettingsManager.FastScanByDefault;
        }

        /// <summary>
        /// Register all global hotkeys from settings
        /// </summary>
        private void RegisterGlobalHotkeys()
        {
            // First unregister any existing hotkeys
            UnregisterAllHotkeys();

            var hotkeys = SettingsManager.GetAllHotkeys();
            foreach (var kvp in hotkeys)
            {
                if (kvp.Value == 0) continue;

                var keys = (Keys)kvp.Value;
                uint modifiers = 0;
                uint vk = (uint)(keys & Keys.KeyCode);

                if ((keys & Keys.Control) == Keys.Control) modifiers |= MOD_CONTROL;
                if ((keys & Keys.Shift) == Keys.Shift) modifiers |= MOD_SHIFT;
                if ((keys & Keys.Alt) == Keys.Alt) modifiers |= MOD_ALT;

                if (RegisterHotKey(this.Handle, _nextHotkeyId, modifiers, vk))
                {
                    _hotkeyIdToFunction[_nextHotkeyId] = kvp.Key;
                    _nextHotkeyId++;
                }
            }
        }

        /// <summary>
        /// Unregister all previously registered hotkeys
        /// </summary>
        private void UnregisterAllHotkeys()
        {
            foreach (var id in _hotkeyIdToFunction.Keys)
            {
                UnregisterHotKey(this.Handle, id);
            }
            _hotkeyIdToFunction.Clear();
            _nextHotkeyId = 1;
        }

        /// <summary>
        /// Override WndProc to handle global hotkey messages
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                if (_hotkeyIdToFunction.TryGetValue(hotkeyId, out string? functionId) && functionId != null)
                {
                    ExecuteHotkeyFunction(functionId);
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// Execute the function associated with a hotkey
        /// </summary>
        private void ExecuteHotkeyFunction(string functionId)
        {
            switch (functionId)
            {
                // Process Control
                case "AttachForeground":
                    AttachToForegroundProcess();
                    break;
                case "PopupHide":
                    ToggleVisibility();
                    break;
                case "PauseProcess":
                    TogglePauseProcess();
                    break;

                // Speedhack (not yet implemented - placeholder)
                case "ToggleSpeedhack":
                case "SpeedhackSpeed1":
                case "SpeedhackSpeed2":
                case "SpeedhackSpeed3":
                case "SpeedhackSpeed4":
                case "SpeedhackSpeed5":
                case "SpeedhackSpeedUp":
                case "SpeedhackSpeedDown":
                    // Speedhack not implemented yet
                    break;

                // Value Type Changes
                case "ChangeTypeBinary":
                    cmbValueType.SelectedIndex = 0;
                    break;
                case "ChangeTypeByte":
                    cmbValueType.SelectedIndex = 1;
                    break;
                case "ChangeType2Bytes":
                    cmbValueType.SelectedIndex = 2;
                    break;
                case "ChangeType4Bytes":
                    cmbValueType.SelectedIndex = 3;
                    break;
                case "ChangeType8Bytes":
                    cmbValueType.SelectedIndex = 4;
                    break;
                case "ChangeTypeFloat":
                    cmbValueType.SelectedIndex = 5;
                    break;
                case "ChangeTypeDouble":
                    cmbValueType.SelectedIndex = 6;
                    break;
                case "ChangeTypeText":
                    cmbValueType.SelectedIndex = 7;
                    break;
                case "ChangeTypeAOB":
                    if (cmbValueType.Items.Count > 8)
                        cmbValueType.SelectedIndex = 8;
                    break;

                // Scanning
                case "NewScan":
                    BtnNewScan_Click(null, EventArgs.Empty);
                    break;
                case "NewScanExact":
                    cmbScanType.SelectedIndex = 0; // Exact value
                    if (btnFirstScan.Enabled)
                        BtnFirstScan_Click(null, EventArgs.Empty);
                    break;
                case "NewScanUnknown":
                    cmbScanType.SelectedIndex = 8; // Unknown initial value
                    if (btnFirstScan.Enabled)
                        BtnFirstScan_Click(null, EventArgs.Empty);
                    break;
                case "NextScanExact":
                    cmbScanType.SelectedIndex = 0;
                    if (btnNextScan.Enabled)
                        BtnNextScan_Click(null, EventArgs.Empty);
                    break;
                case "NextScanIncreased":
                    cmbScanType.SelectedIndex = 1; // Increased
                    if (btnNextScan.Enabled)
                        BtnNextScan_Click(null, EventArgs.Empty);
                    break;
                case "NextScanDecreased":
                    cmbScanType.SelectedIndex = 3; // Decreased
                    if (btnNextScan.Enabled)
                        BtnNextScan_Click(null, EventArgs.Empty);
                    break;
                case "NextScanChanged":
                    cmbScanType.SelectedIndex = 6; // Changed
                    if (btnNextScan.Enabled)
                        BtnNextScan_Click(null, EventArgs.Empty);
                    break;
                case "NextScanUnchanged":
                    cmbScanType.SelectedIndex = 7; // Unchanged
                    if (btnNextScan.Enabled)
                        BtnNextScan_Click(null, EventArgs.Empty);
                    break;
                case "ToggleScanCompare":
                    // Toggle between first/last scan compare - not implemented
                    break;
                case "UndoScan":
                    BtnUndoScan_Click(null, EventArgs.Empty);
                    break;
                case "CancelScan":
                    // Cancel scan - would need to implement cancellation token
                    break;

                // Debug
                case "DebugRun":
                    // Debug run - not implemented
                    break;
            }
        }

        /// <summary>
        /// Attach to the current foreground window's process
        /// </summary>
        private void AttachToForegroundProcess()
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return;

                GetWindowThreadProcessId(foregroundWindow, out uint processId);
                if (processId == 0 || processId == Environment.ProcessId) return;

                var process = Process.GetProcessById((int)processId);
                AttachToProcess(process);
            }
            catch
            {
                // Ignore errors
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// Toggle window visibility (minimize/restore)
        /// </summary>
        private void ToggleVisibility()
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
                this.Activate();
            }
            else
            {
                this.WindowState = FormWindowState.Minimized;
            }
        }

        /// <summary>
        /// Toggle pause/resume of the attached process
        /// </summary>
        private void TogglePauseProcess()
        {
            if (_process == null || !_process.IsOpen) return;

            try
            {
                if (_process.IsSuspended)
                {
                    _process.Resume();
                }
                else
                {
                    _process.Suspend();
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// Timer tick for auto-attach functionality
        /// </summary>
        private void AutoAttachTimer_Tick(object? sender, EventArgs e)
        {
            // Only check if no process attached, OR if AlwaysAutoAttach is enabled
            if (_process == null || !_process.IsOpen || SettingsManager.AlwaysAutoAttach)
            {
                TryAutoAttach();
            }
        }

        /// <summary>
        /// Try to auto-attach to a process from the configured list
        /// </summary>
        private void TryAutoAttach()
        {
            string autoAttachList = SettingsManager.AutoAttachProcess;
            if (string.IsNullOrWhiteSpace(autoAttachList))
                return;

            // If already attached and not "always auto-attach", skip
            if (_process != null && _process.IsOpen && !SettingsManager.AlwaysAutoAttach)
                return;

            var processNames = autoAttachList.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            try
            {
                var runningProcesses = Process.GetProcesses();

                foreach (var targetName in processNames)
                {
                    string trimmedName = targetName.Trim();
                    if (string.IsNullOrEmpty(trimmedName))
                        continue;

                    // Find matching process (case-insensitive, supports partial match)
                    var match = runningProcesses.FirstOrDefault(p =>
                    {
                        try
                        {
                            return p.ProcessName.Contains(trimmedName, StringComparison.OrdinalIgnoreCase) ||
                                   p.ProcessName.Equals(trimmedName, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    });

                    if (match != null)
                    {
                        // Don't attach to ourselves
                        if (match.Id == Environment.ProcessId)
                            continue;

                        // Don't re-attach to same process
                        if (_process != null && _process.IsOpen && _process.Target?.Id == match.Id)
                            continue;

                        // Attach to the process
                        AttachToProcess(match);
                        break;
                    }
                }

                // Dispose process array
                foreach (var p in runningProcesses)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            catch
            {
                // Ignore errors during auto-attach enumeration
            }
        }

        /// <summary>
        /// Attach to a specific process (used by auto-attach and manual selection)
        /// </summary>
        private void AttachToProcess(Process process)
        {
            try
            {
                _process?.Dispose();
                _process = new ProcessAccess();

                if (_process.Open(process.Id))
                {
                    lblProcessTitle.Text = $"{process.ProcessName} - PID: {process.Id} ({(_process.Is64Bit ? "64" : "32")}-bit)";
                    lblProcessTitle.ForeColor = Color.MediumSeaGreen;

                    // Update window title with version
                    string ver = Application.ProductVersion;
                    if (ver.EndsWith(".0.0")) ver = ver[..^4];
                    else if (ver.EndsWith(".0")) ver = ver[..^2];
                    this.Text = $"CrxMem v{ver} - {process.ProcessName} (PID: {process.Id})";

                    _scanner = new MemoryScanner(_process);

                    // Hook up progress events (use BeginInvoke to avoid blocking)
                    _scanner.StatusChanged += (status) =>
                    {
                        try
                        {
                            if (InvokeRequired)
                                BeginInvoke(() => { if (!IsDisposed) lblFound.Text = status; });
                            else
                                lblFound.Text = status;
                        }
                        catch { }
                    };

                    _scanner.ProgressChanged += (current, total) =>
                    {
                        try
                        {
                            if (InvokeRequired)
                                BeginInvoke(() =>
                                {
                                    if (!IsDisposed)
                                    {
                                        progressBarScan.Maximum = total;
                                        progressBarScan.Value = Math.Min(current, total);
                                    }
                                });
                            else
                            {
                                progressBarScan.Maximum = total;
                                progressBarScan.Value = Math.Min(current, total);
                            }
                        }
                        catch { }
                    };

                    // Populate module dropdown
                    PopulateModuleDropdown();

                    btnFirstScan.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-attach error: {ex.Message}");
            }
        }

        private void SaveTable_Click(object? sender, EventArgs e)
        {
            SaveCheatTable();
        }

        private void LoadTable_Click(object? sender, EventArgs e)
        {
            LoadCheatTable();
        }

        private void CmbScanType_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // Disable value input for scan types that don't need it
            int idx = cmbScanType.SelectedIndex;
            // Unknown, Changed, Unchanged don't need value input
            bool needsValue = idx != 8 && idx != 6 && idx != 7;
            txtValue.Enabled = needsValue;
            lblValue.Enabled = needsValue;
        }

        private void PopulateModuleDropdown()
        {
            cmbModule.Items.Clear();
            cmbModule.Items.Add("All Modules"); // Default option to scan all memory

            if (_process == null) return;

            try
            {
                var modules = _process.GetModules();
                foreach (var module in modules)
                {
                    cmbModule.Items.Add(module);
                }
            }
            catch
            {
                // Ignore errors in module enumeration
            }

            cmbModule.SelectedIndex = 0; // Select "All Modules" by default
        }

        private void CmbModule_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbModule.SelectedIndex <= 0 || cmbModule.SelectedItem == null)
            {
                // "All Modules" selected - reset to full range
                txtStartAddress.Text = "0000000000000000";
                txtStopAddress.Text = "7FFFFFFFFFFFFFFF";
                return;
            }

            // Get selected module and set address range
            if (cmbModule.SelectedItem is Core.ModuleInfo module)
            {
                long baseAddr = module.BaseAddress.ToInt64();
                long endAddr = baseAddr + module.Size;

                txtStartAddress.Text = baseAddr.ToString("X16");
                txtStopAddress.Text = endAddr.ToString("X16");
            }
        }

        private void TxtValue_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;
                if (btnNextScan.Enabled)
                    BtnNextScan_Click(sender, e);
                else if (btnFirstScan.Enabled)
                    BtnFirstScan_Click(sender, e);
            }
        }

        private async void BtnFirstScan_Click(object? sender, EventArgs e)
        {
            if (_scanner == null)
            {
                MessageBox.Show("Please select a process first!", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string value = txtValue.Text;

            // Check if AOB scan mode
            if (cmbValueType.SelectedIndex == 8) // Array of Bytes
            {
                await PerformAOBScan(value);
                return;
            }

            var scanType = GetScanType();
            var valueType = GetValueType();

            // Validate value input if needed
            if (txtValue.Enabled && string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("Please enter a value to scan for!", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnFirstScan.Enabled = false;
            btnNextScan.Enabled = false;
            btnCancelScan.Enabled = true;
            lvFoundAddresses.Items.Clear();
            lblFound.Text = "Preparing scan...";
            panelScanProgress.Visible = true;
            progressBarScan.Value = 0;

            try
            {
                var startTime = DateTime.Now;

                // Get memory scan options from checkboxes (3-state: checked=true, unchecked=false, indeterminate=null)
                bool? writableOnly = chkWritable.CheckState == CheckState.Checked ? true :
                                      chkWritable.CheckState == CheckState.Unchecked ? false : null;
                bool? copyOnWrite = chkCopyOnWrite.CheckState == CheckState.Checked ? true :
                                     chkCopyOnWrite.CheckState == CheckState.Unchecked ? false : null;
                bool? activeMemoryOnly = chkActiveMemoryOnly.CheckState == CheckState.Checked ? true :
                                          chkActiveMemoryOnly.CheckState == CheckState.Unchecked ? false : null;

                // Run scan on background thread
                await System.Threading.Tasks.Task.Run(() =>
                {
                    _scanner.FirstScan(scanType, valueType, value ?? "",
                        chkFastScan.Checked, writableOnly, copyOnWrite, activeMemoryOnly);
                });

                var elapsed = DateTime.Now - startTime;
                panelScanProgress.Visible = false;

                UpdateResultsList();
                lblFound.Text = $"Found: {_scanner.ResultCount:N0} ({elapsed.TotalSeconds:F1}s)";

                btnNextScan.Enabled = _scanner.ResultCount > 0;
                btnUndoScan.Enabled = _scanner.CanUndo;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblFound.Text = "Scan failed";
            }
            finally
            {
                panelScanProgress.Visible = false;
                btnFirstScan.Enabled = true;
                btnCancelScan.Enabled = false;
            }
        }

        /// <summary>
        /// Perform Array of Bytes (AOB) pattern scan
        /// </summary>
        private async System.Threading.Tasks.Task PerformAOBScan(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                MessageBox.Show("Enter a byte pattern (e.g., 48 8B 05 ?? ?? ?? ??)", "AOB Scan",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnFirstScan.Enabled = false;
            btnNextScan.Enabled = false;
            lvFoundAddresses.Items.Clear();
            lblFound.Text = "Scanning for pattern...";
            panelScanProgress.Visible = true;
            progressBarScan.Value = 0;

            try
            {
                var startTime = DateTime.Now;
                List<IntPtr>? aobResults = null;

                // Run AOB scan on background thread
                await System.Threading.Tasks.Task.Run(() =>
                {
                    aobResults = _scanner.ScanArrayOfBytes(pattern);
                });

                var elapsed = DateTime.Now - startTime;
                panelScanProgress.Visible = false;

                // Display AOB results
                if (aobResults != null && aobResults.Count > 0)
                {
                    int displayCount = Math.Min(aobResults.Count, 5000);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var addr = aobResults[i];
                        string addressDisplay;
                        Color addressColor;

                        if (_process != null)
                        {
                            var module = _process.GetModuleForAddress(addr);
                            if (module != null)
                            {
                                long offset = addr.ToInt64() - module.BaseAddress.ToInt64();
                                addressDisplay = $"{module.ModuleName}+{offset:X}";
                                addressColor = Color.Green;
                            }
                            else
                            {
                                addressDisplay = $"{addr.ToInt64():X}";
                                addressColor = Color.Black;
                            }
                        }
                        else
                        {
                            addressDisplay = $"{addr.ToInt64():X8}";
                            addressColor = Color.Black;
                        }

                        var item = new ListViewItem(new[] { addressDisplay, pattern, "" });
                        item.ForeColor = addressColor;
                        item.Tag = new ScanResult { Address = addr, Value = System.Text.Encoding.ASCII.GetBytes(pattern) };
                        lvFoundAddresses.Items.Add(item);
                    }

                    if (aobResults.Count > displayCount)
                    {
                        var item = new ListViewItem(new[] { $"... +{aobResults.Count - displayCount:N0} more", "", "" });
                        lvFoundAddresses.Items.Add(item);
                    }

                    lblFound.Text = $"Found: {aobResults.Count:N0} ({elapsed.TotalSeconds:F1}s)";
                }
                else
                {
                    lblFound.Text = $"Found: 0 ({elapsed.TotalSeconds:F1}s)";
                }

                btnNextScan.Enabled = false; // AOB doesn't support next scan
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AOB Scan error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblFound.Text = "Scan failed";
            }
            finally
            {
                panelScanProgress.Visible = false;
                btnFirstScan.Enabled = true;
            }
        }

        private async void BtnNextScan_Click(object? sender, EventArgs e)
        {
            if (_scanner == null || _scanner.ResultCount == 0)
                return;

            var scanType = GetScanType();
            var valueType = GetValueType();
            string value = txtValue.Text;

            btnNextScan.Enabled = false;
            btnFirstScan.Enabled = false;
            btnCancelScan.Enabled = true;
            lblFound.Text = "Scanning...";
            panelScanProgress.Visible = true;
            progressBarScan.Value = 0;

            try
            {
                var startTime = DateTime.Now;

                // Run next scan on background thread
                await System.Threading.Tasks.Task.Run(() =>
                {
                    _scanner.NextScan(scanType, valueType, value ?? "");
                });

                var elapsed = DateTime.Now - startTime;
                panelScanProgress.Visible = false;

                UpdateResultsList();
                lblFound.Text = $"Found: {_scanner.ResultCount:N0} ({elapsed.TotalSeconds:F1}s)";

                btnNextScan.Enabled = _scanner.ResultCount > 0;
                btnUndoScan.Enabled = _scanner.CanUndo;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblFound.Text = "Scan failed";
            }
            finally
            {
                panelScanProgress.Visible = false;
                btnNextScan.Enabled = _scanner.ResultCount > 0;
                btnUndoScan.Enabled = _scanner.CanUndo;
                btnFirstScan.Enabled = true;
                btnCancelScan.Enabled = false;
            }
        }

        private void UpdateResultsList()
        {
            lvFoundAddresses.Items.Clear();

            if (_scanner == null) return;

            int displayCount = Math.Min(_scanner.ResultCount, 5000);

            for (int i = 0; i < displayCount; i++)
            {
                var result = _scanner.Results[i];
                string value = ReadValueAsString(result.Address);

                // Format address as Module+Offset (like Cheat Engine)
                string addressDisplay;
                Color addressColor;

                if (_process != null)
                {
                    var module = _process.GetModuleForAddress(result.Address);
                    if (module != null)
                    {
                        // Static address - inside a module
                        long offset = result.Address.ToInt64() - module.BaseAddress.ToInt64();
                        addressDisplay = $"{module.ModuleName}+{offset:X}";
                        addressColor = Color.MediumSeaGreen;  // Modern green for static addresses
                    }
                    else
                    {
                        // Dynamic address - heap/stack allocation
                        addressDisplay = $"{result.Address.ToInt64():X}";
                        addressColor = ThemeManager.Foreground; // Flexible color for dynamic addresses
                    }
                }
                else
                {
                    addressDisplay = $"{result.Address.ToInt64():X8}";
                    addressColor = ThemeManager.Foreground;
                }

                var item = new ListViewItem(new[] {
                    addressDisplay,
                    value,
                    ""
                });
                
                // Match item appearance to theme
                item.ForeColor = addressColor;
                item.BackColor = lvFoundAddresses.BackColor; 
                item.Tag = result;
                lvFoundAddresses.Items.Add(item);
            }

            if (_scanner.ResultCount > displayCount)
            {
                var item = new ListViewItem(new[] {
                    $"... +{_scanner.ResultCount - displayCount:N0} more",
                    "",
                    ""
                });
                lvFoundAddresses.Items.Add(item);
            }
        }

        private string ReadValueAsString(IntPtr address)
        {
            if (_process == null) return "?";

            try
            {
                var valueType = GetValueType();
                return valueType switch
                {
                    ScanValueType.Byte => _process.Read<byte>(address).ToString(),
                    ScanValueType.Int16 => _process.Read<short>(address).ToString(),
                    ScanValueType.Int32 => _process.Read<int>(address).ToString(),
                    ScanValueType.Int64 => _process.Read<long>(address).ToString(),
                    ScanValueType.Float => _process.Read<float>(address).ToString("F2"),
                    ScanValueType.Double => _process.Read<double>(address).ToString("F4"),
                    _ => "?"
                };
            }
            catch
            {
                return "?";
            }
        }

        private void LvFoundAddresses_DoubleClick(object? sender, EventArgs e)
        {
            AddSelectedFoundAddressesToList();
        }

        /// <summary>
        /// Adds all selected addresses from the found addresses list to the address list below.
        /// Supports multi-select (Ctrl+Click, Shift+Click).
        /// </summary>
        private void AddSelectedFoundAddressesToList()
        {
            if (lvFoundAddresses.SelectedItems.Count == 0)
                return;

            string typeStr = GetValueType() switch
            {
                ScanValueType.Byte => "Byte",
                ScanValueType.Int16 => "2 Bytes",
                ScanValueType.Int32 => "4 Bytes",
                ScanValueType.Int64 => "8 Bytes",
                ScanValueType.Float => "Float",
                ScanValueType.Double => "Double",
                _ => "4 Bytes"
            };

            foreach (ListViewItem item in lvFoundAddresses.SelectedItems)
            {
                if (item.Tag is ScanResult result)
                {
                    AddAddressToList(result.Address, "", ReadValueAsString(result.Address), typeStr);
                }
            }
        }

        private void BtnAddAddressManually_Click(object? sender, EventArgs e)
        {
            AddAddressDialog dialog = new AddAddressDialog(_process);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                AddAddressToList(dialog.Address, dialog.Description, "?", dialog.ValueType, dialog.OriginalAddressString);
            }
        }

        private void AddAddressToList(IntPtr address, string description, string value, string type, string originalAddressString = "")
        {
            var (addressDisplay, addressColor) = FormatAddressDisplay(address);

            var item = new ListViewItem("");
            item.SubItems.Add(description);
            item.SubItems.Add(addressDisplay);
            item.SubItems.Add(type);
            item.SubItems.Add(value);
            item.Tag = new AddressEntry
            {
                Address = address,
                Description = description,
                Type = type,
                Active = true,
                Frozen = false,
                FrozenValue = "",
                OriginalAddressString = originalAddressString
            };
            item.Checked = false;
            // Color the address column (index 2) based on whether it's static or dynamic
            item.UseItemStyleForSubItems = false;
            item.SubItems[2].ForeColor = addressColor;
            item.BackColor = lvAddressList.BackColor;
            lvAddressList.Items.Add(item);
        }

        private void LvAddressList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            // Checkbox toggle for freeze/unfreeze
            if (lvAddressList.Items[e.Index].Tag is AddressEntry entry)
            {
                entry.Frozen = (e.NewValue == CheckState.Checked);
                if (entry.Frozen)
                {
                    entry.FrozenValue = ReadValueAsString(entry.Address);
                }
            }
        }

        private void LvAddressList_BeforeLabelEdit(object? sender, LabelEditEventArgs e)
        {
            // Only allow editing description column (index 1)
            var hitInfo = lvAddressList.HitTest(lvAddressList.PointToClient(Cursor.Position));
            if (hitInfo.SubItem != lvAddressList.Items[e.Item].SubItems[1])
            {
                e.CancelEdit = true;
            }
        }

        private void LvAddressList_AfterLabelEdit(object? sender, LabelEditEventArgs e)
        {
            if (e.Label != null && lvAddressList.Items[e.Item].Tag is AddressEntry entry)
            {
                entry.Description = e.Label;
            }
        }

        private void LvAddressList_MouseClick(object? sender, MouseEventArgs e)
        {
            var hit = lvAddressList.HitTest(e.Location);
            if (hit.Item != null && hit.SubItem == hit.Item.SubItems[0])
            {
                // Toggle active state
                if (hit.Item.Tag is AddressEntry entry)
                {
                    entry.Active = !entry.Active;
                    hit.SubItem.Text = entry.Frozen ? "❄" : (entry.Active ? "✓" : "");
                }
            }
        }

        private void LvAddressList_DoubleClick(object? sender, EventArgs e)
        {
            if (lvAddressList.SelectedItems.Count > 0)
            {
                var item = lvAddressList.SelectedItems[0];
                if (item.Tag is AddressEntry entry)
                {
                    string currentValue = item.SubItems[4].Text;
                    string description = item.SubItems[1].Text;

                    // Get formatted address display
                    var (addressDisplay, addressColor) = FormatAddressDisplay(entry.Address);
                    EditAddressDialog dialog = new EditAddressDialog(addressDisplay, currentValue, description, addressColor);
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        // Write new value
                        if (_process != null && !string.IsNullOrEmpty(dialog.NewValue))
                        {
                            try
                            {
                                bool success = WriteValue(entry.Address, entry.Type, dialog.NewValue);
                                if (success)
                                {
                                    entry.FrozenValue = dialog.NewValue; // Update frozen value so it doesn't get reverted
                                    item.SubItems[4].Text = dialog.NewValue;
                                }
                                else
                                {
                                    MessageBox.Show($"Failed to write value to address {entry.Address.ToInt64():X8}.\n\nPossible causes:\n- Memory protection (try running as Administrator)\n- Invalid address\n- Process has exited", "Write Failed",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Failed to write value: {ex.Message}", "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }

                        item.SubItems[1].Text = dialog.Description;
                        entry.Description = dialog.Description;
                    }
                }
            }
        }

        private bool WriteValue(IntPtr address, string type, string value)
        {
            if (_process == null) return false;

            bool success = false;
            switch (type)
            {
                case "Byte":
                    if (byte.TryParse(value, out byte b))
                        success = _process.WriteWithProtection(address, b);
                    break;
                case "2 Bytes":
                    if (short.TryParse(value, out short s))
                        success = _process.WriteWithProtection(address, s);
                    break;
                case "4 Bytes":
                    if (int.TryParse(value, out int i))
                        success = _process.WriteWithProtection(address, i);
                    break;
                case "8 Bytes":
                    if (long.TryParse(value, out long l))
                        success = _process.WriteWithProtection(address, l);
                    break;
                case "Float":
                    if (float.TryParse(value, out float f))
                        success = _process.WriteWithProtection(address, f);
                    break;
                case "Double":
                    if (double.TryParse(value, out double d))
                        success = _process.WriteWithProtection(address, d);
                    break;
            }
            return success;
        }

        /// <summary>
        /// Opens the "Find out what writes/accesses" dialog for the selected address
        /// </summary>
        private void FindWhatAccesses(bool writeOnly)
        {
            if (_process == null)
            {
                MessageBox.Show("No process attached.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (lvAddressList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select an address from the list.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (lvAddressList.SelectedItems[0].Tag is not AddressEntry entry)
                return;

            ulong address = (ulong)entry.Address.ToInt64();

            var dialog = new FindAccessDialog(_process, address, writeOnly);

            // Handle navigation to disassembler
            dialog.NavigateToAddress += (s, addr) =>
            {
                OpenOrFocusMemoryView(new IntPtr((long)addr));
            };

            dialog.Show();
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_process == null || !_liveUpdatesEnabled) return;

            Color resultBackground = lvFoundAddresses.BackColor;
            Color highlightColor = ThemeManager.IsDarkMode ? Color.FromArgb(60, 100, 60) : Color.LightGreen;

            // Update values in scan results list (live values!)
            foreach (ListViewItem item in lvFoundAddresses.Items)
            {
                if (item.Text.Contains("+")) // Skip the "... more" item
                    continue;

                try
                {
                    // Parse address from first column
                    if (IntPtr.TryParse(item.Text.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out IntPtr address))
                    {
                        string currentValue = ReadValueAsString(address);
                        string previousValue = item.SubItems[1].Text;

                        if (currentValue != previousValue)
                        {
                            item.SubItems[1].Text = currentValue;
                            item.BackColor = highlightColor; // Highlight changed values
                        }
                        else
                        {
                            item.BackColor = resultBackground;
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }

            Color addressListBackground = lvAddressList.BackColor;
            Color addressHighlightColor = ThemeManager.IsDarkMode ? Color.FromArgb(100, 45, 45) : Color.MistyRose;

            // Update values in address list
            foreach (ListViewItem item in lvAddressList.Items)
            {
                if (item.Tag is AddressEntry entry)
                {
                    try
                    {
                        string rawValue = entry.Type switch
                        {
                            "Byte" => _process.Read<byte>(entry.Address).ToString(),
                            "2 Bytes" => _process.Read<short>(entry.Address).ToString(),
                            "4 Bytes" => _process.Read<int>(entry.Address).ToString(),
                            "8 Bytes" => _process.Read<long>(entry.Address).ToString(),
                            "Float" => _process.Read<float>(entry.Address).ToString("F2"),
                            "Double" => _process.Read<double>(entry.Address).ToString("F4"),
                            _ => "?"
                        };

                        string displayValue = FormatValue(rawValue, entry.Type, entry.ShowAsHex);

                        if (item.SubItems[4].Text != displayValue)
                        {
                            item.SubItems[4].Text = displayValue;
                            item.BackColor = addressHighlightColor;
                        }
                        else
                        {
                            item.BackColor = addressListBackground;
                        }
                    }
                    catch
                    {
                        item.SubItems[4].Text = "???";
                    }
                }
            }
        }

        private void LiveUpdates_Click(object? sender, EventArgs e)
        {
            _liveUpdatesEnabled = liveUpdatesToolStripMenuItem.Checked;
        }

        private ScanType GetScanType()
        {
            return cmbScanType.SelectedIndex switch
            {
                0 => ScanType.Exact,
                1 => ScanType.Increased,
                2 => ScanType.IncreasedBy, // "by..." variant
                3 => ScanType.Decreased,
                4 => ScanType.DecreasedBy, // "by..." variant
                5 => ScanType.Between,
                6 => ScanType.Changed,
                7 => ScanType.Unchanged,
                8 => ScanType.Unknown,
                _ => ScanType.Exact
            };
        }

        private ScanValueType GetValueType()
        {
            return cmbValueType.SelectedIndex switch
            {
                0 => ScanValueType.Byte, // Binary
                1 => ScanValueType.Byte,
                2 => ScanValueType.Int16, // 2 Bytes
                3 => ScanValueType.Int32, // 4 Bytes
                4 => ScanValueType.Int64, // 8 Bytes
                5 => ScanValueType.Float,
                6 => ScanValueType.Double,
                7 => ScanValueType.String,
                _ => ScanValueType.Int32
            };
        }

        private void BtnMemoryView_Click(object? sender, EventArgs e)
        {
            if (_process == null)
            {
                MessageBox.Show("Please select a process first!", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Get selected address if any
            IntPtr startAddress = IntPtr.Zero;
            if (lvFoundAddresses.SelectedItems.Count > 0)
            {
                var item = lvFoundAddresses.SelectedItems[0];
                if (item.Tag is ScanResult result)
                {
                    startAddress = result.Address;
                }
            }
            else if (lvAddressList.SelectedItems.Count > 0)
            {
                var item = lvAddressList.SelectedItems[0];
                if (item.Tag is AddressEntry entry)
                {
                    startAddress = entry.Address;
                }
            }

            // Open Memory View window (use singleton pattern)
            OpenOrFocusMemoryView(startAddress);
        }

        private void BtnNewScan_Click(object? sender, EventArgs e)
        {
            if (_scanner == null) return;

            _scanner.ClearResults();
            lvFoundAddresses.Items.Clear();
            btnNextScan.Enabled = false;
            btnUndoScan.Enabled = false;
            lblFound.Text = "Found: 0";
            panelScanProgress.Visible = false;
        }

        private void BtnUndoScan_Click(object? sender, EventArgs e)
        {
            if (_scanner == null || !_scanner.CanUndo) return;

            _scanner.UndoScan();
            UpdateResultsList();
            lblFound.Text = $"Found: {_scanner.ResultCount:N0}";
            btnNextScan.Enabled = _scanner.ResultCount > 0;
            btnUndoScan.Enabled = _scanner.CanUndo;
        }

        private void BtnCancelScan_Click(object? sender, EventArgs e)
        {
            if (_scanner == null) return;

            // Cancel the scan - this is non-blocking and will stop the scan ASAP
            _scanner.CancelScan();
            btnCancelScan.Enabled = false;
            lblFound.Text = "Cancelling...";
        }

        private void FreezeTimer_Tick(object? sender, EventArgs e)
        {
            if (_process == null) return;

            foreach (ListViewItem item in lvAddressList.Items)
            {
                if (item.Tag is AddressEntry entry && entry.Frozen)
                {
                    try
                    {
                        WriteValue(entry.Address, entry.Type, entry.FrozenValue);
                    }
                    catch { }
                }
            }
        }

        private void UpdateAddressList()
        {
            // Refresh the address list display
            foreach (ListViewItem item in lvAddressList.Items)
            {
                if (item.Tag is AddressEntry entry)
                {
                    item.SubItems[1].Text = entry.Description;
                    // Removed frozen/active icons as per user request
                    item.SubItems[0].Text = "";
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    if (btnFirstScan.Enabled)
                        BtnFirstScan_Click(null, EventArgs.Empty);
                    return true;

                case Keys.F6:
                    if (btnNextScan.Enabled)
                        BtnNextScan_Click(null, EventArgs.Empty);
                    return true;

                case Keys.F3:
                    BtnNewScan_Click(null, EventArgs.Empty);
                    return true;

                case Keys.Control | Keys.S:
                    SaveCheatTable();
                    return true;

                case Keys.Control | Keys.O:
                    LoadCheatTable();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveCheatTable()
        {
            if (_process == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Cheat Table (*.ct)|*.ct|All Files (*.*)|*.*",
                DefaultExt = "ct"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                var table = new CheatTable
                {
                    ProcessName = _process.Target.ProcessName,
                    Entries = new List<CheatEntry>()
                };

                foreach (ListViewItem item in lvAddressList.Items)
                {
                    if (item.Tag is AddressEntry entry)
                    {
                        // Use original address string if available, otherwise compute module+offset or use raw hex
                        string addressToSave;
                        if (!string.IsNullOrEmpty(entry.OriginalAddressString))
                        {
                            // User entered this address manually with module+offset format
                            addressToSave = entry.OriginalAddressString;
                        }
                        else
                        {
                            // Address was added from scan results - compute module+offset if possible
                            var module = _process.GetModuleForAddress(entry.Address);
                            if (module != null)
                            {
                                long offset = entry.Address.ToInt64() - module.BaseAddress.ToInt64();
                                addressToSave = $"{module.ModuleName}+{offset:X}";
                            }
                            else
                            {
                                // No module found, save as raw hex
                                addressToSave = entry.Address.ToInt64().ToString("X");
                            }
                        }

                        table.Entries.Add(new CheatEntry
                        {
                            Address = addressToSave,
                            Description = entry.Description,
                            Type = entry.Type,
                            Frozen = entry.Frozen,
                            FrozenValue = entry.FrozenValue,
                            Active = entry.Active,
                            ShowAsHex = entry.ShowAsHex
                        });
                    }
                }

                var json = JsonSerializer.Serialize(table, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                MessageBox.Show("Cheat table saved!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void LoadCheatTable()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Cheat Table (*.ct)|*.ct|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var json = File.ReadAllText(ofd.FileName);
                    var table = JsonSerializer.Deserialize<CheatTable>(json);

                    if (table == null) return;

                    lvAddressList.Items.Clear();

                    int failedCount = 0;
                    foreach (var entry in table.Entries)
                    {
                        IntPtr address;
                        string originalAddressString = entry.Address;

                        // Check if it's a module+offset format (contains '+' and has letters before it)
                        if (entry.Address.Contains('+'))
                        {
                            // Module+Offset format - resolve it
                            address = ResolveModuleOffsetAddress(entry.Address);
                            if (address == IntPtr.Zero)
                            {
                                // Failed to resolve - module might not be loaded yet
                                failedCount++;
                                System.Diagnostics.Debug.WriteLine($"Failed to resolve address: {entry.Address}");
                                continue; // Skip this entry
                            }
                        }
                        else
                        {
                            // Raw hex address
                            try
                            {
                                address = new IntPtr(Convert.ToInt64(entry.Address, 16));
                            }
                            catch
                            {
                                failedCount++;
                                continue;
                            }
                        }

                        var addressEntry = new AddressEntry
                        {
                            Address = address,
                            Description = entry.Description,
                            Type = entry.Type,
                            Frozen = entry.Frozen,
                            FrozenValue = entry.FrozenValue,
                            Active = entry.Active,
                            ShowAsHex = entry.ShowAsHex,
                            OriginalAddressString = originalAddressString
                        };

                        // Format address as Module+Offset
                        var (addressDisplay, addressColor) = FormatAddressDisplay(address);

                        // Add to list
                        var item = new ListViewItem(addressEntry.Frozen ? "❄" : (addressEntry.Active ? "✓" : ""));
                        item.SubItems.Add(addressEntry.Description);
                        item.SubItems.Add(addressDisplay);
                        item.SubItems.Add(addressEntry.Type);
                        string displayValue = FormatValue(addressEntry.FrozenValue, addressEntry.Type, addressEntry.ShowAsHex);
                        item.SubItems.Add(displayValue);
                        item.Tag = addressEntry;
                        item.Checked = false;
                        // Color the address column based on static/dynamic
                        item.UseItemStyleForSubItems = false;
                        item.SubItems[2].ForeColor = addressColor;
                        lvAddressList.Items.Add(item);
                    }

                    int loadedCount = table.Entries.Count - failedCount;
                    string message = $"Loaded {loadedCount} addresses!";
                    if (failedCount > 0)
                    {
                        message += $"\n\n{failedCount} addresses could not be resolved (module not loaded?).";
                    }
                    MessageBox.Show(message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load cheat table: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private string FormatValue(string value, string type, bool showHex)
        {
            if (!showHex) return value;

            try
            {
                switch (type)
                {
                    case "Byte":
                        if (byte.TryParse(value, out byte b))
                            return $"0x{b:X2}";
                        break;
                    case "2 Bytes":
                        if (short.TryParse(value, out short s))
                            return $"0x{s:X4}";
                        break;
                    case "4 Bytes":
                        if (int.TryParse(value, out int i))
                            return $"0x{i:X8}";
                        break;
                    case "8 Bytes":
                        if (long.TryParse(value, out long l))
                            return $"0x{l:X16}";
                        break;
                }
            }
            catch { }

            return value;
        }

        private void WriteValueFromString(IntPtr address, string type, string value)
        {
            // Just call the main WriteValue method which uses WriteWithProtection
            WriteValue(address, type, value);
        }

        /// <summary>
        /// Formats an address as "Module+Offset" if it belongs to a module, otherwise as raw hex
        /// </summary>
        private (string display, Color color) FormatAddressDisplay(IntPtr address)
        {
            if (_process != null)
            {
                var module = _process.GetModuleForAddress(address);
                if (module != null)
                {
                    // Static address - inside a module
                    long offset = address.ToInt64() - module.BaseAddress.ToInt64();
                    return ($"{module.ModuleName}+{offset:X}", Color.MediumSeaGreen);
                }
            }
            // Dynamic address - heap/stack allocation
            return ($"{address.ToInt64():X8}", ThemeManager.Foreground);
        }

        /// <summary>
        /// Resolves a "Module+Offset" string to an absolute address.
        /// Returns IntPtr.Zero if the module is not found.
        /// </summary>
        private IntPtr ResolveModuleOffsetAddress(string addressString)
        {
            if (_process == null || string.IsNullOrEmpty(addressString))
                return IntPtr.Zero;

            int plusIndex = addressString.IndexOf('+');
            if (plusIndex <= 0 || plusIndex >= addressString.Length - 1)
                return IntPtr.Zero;

            string moduleName = addressString.Substring(0, plusIndex).Trim();
            string offsetStr = addressString.Substring(plusIndex + 1).Trim();

            // Remove common prefixes from offset
            if (offsetStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                offsetStr = offsetStr.Substring(2);

            // Parse offset as hex
            if (!long.TryParse(offsetStr, System.Globalization.NumberStyles.HexNumber, null, out long offset))
                return IntPtr.Zero;

            // Find the module
            var modules = _process.GetModules();
            foreach (var module in modules)
            {
                // Match by name (case-insensitive), with or without .exe/.dll extension
                string modNameLower = module.ModuleName.ToLowerInvariant();
                string searchLower = moduleName.ToLowerInvariant();

                if (modNameLower == searchLower ||
                    modNameLower == searchLower + ".exe" ||
                    modNameLower == searchLower + ".dll" ||
                    modNameLower.Replace(".exe", "") == searchLower ||
                    modNameLower.Replace(".dll", "") == searchLower)
                {
                    // Found the module - calculate absolute address
                    long baseAddress = module.BaseAddress.ToInt64();
                    return new IntPtr(baseAddress + offset);
                }
            }

            // Module not found
            return IntPtr.Zero;
        }

        private void ModifyValue(AddressEntry entry, int delta)
        {
            if (_process == null)
                return;

            try
            {
                // Read current value
                string currentValue = ReadValueAsString(entry.Address);
                string newValueStr = "";

                // Parse and modify based on type
                switch (entry.Type)
                {
                    case "Byte":
                        if (byte.TryParse(currentValue, out byte b))
                        {
                            int newValue = Math.Max(0, Math.Min(255, b + delta));
                            _process.WriteWithProtection(entry.Address, (byte)newValue);
                            newValueStr = newValue.ToString();
                        }
                        break;
                    case "2 Bytes":
                        if (short.TryParse(currentValue, out short s))
                        {
                            short newValue = (short)(s + delta);
                            _process.WriteWithProtection(entry.Address, newValue);
                            newValueStr = newValue.ToString();
                        }
                        break;
                    case "4 Bytes":
                        if (int.TryParse(currentValue, out int i))
                        {
                            int newValue = i + delta;
                            _process.WriteWithProtection(entry.Address, newValue);
                            newValueStr = newValue.ToString();
                        }
                        break;
                    case "8 Bytes":
                        if (long.TryParse(currentValue, out long l))
                        {
                            long newValue = l + delta;
                            _process.WriteWithProtection(entry.Address, newValue);
                            newValueStr = newValue.ToString();
                        }
                        break;
                    case "Float":
                        if (float.TryParse(currentValue, out float f))
                        {
                            float newValue = f + delta;
                            _process.WriteWithProtection(entry.Address, newValue);
                            newValueStr = newValue.ToString("F2");
                        }
                        break;
                    case "Double":
                        if (double.TryParse(currentValue, out double d))
                        {
                            double newValue = d + delta;
                            _process.WriteWithProtection(entry.Address, newValue);
                            newValueStr = newValue.ToString("F4");
                        }
                        break;
                }

                // Update frozen value if the entry is frozen
                if (entry.Frozen && !string.IsNullOrEmpty(newValueStr))
                {
                    entry.FrozenValue = newValueStr;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to modify value: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LuaMonitor_Click(object? sender, EventArgs e)
        {
            if (_process == null)
            {
                MessageBox.Show("Please attach to a process first.", "No Process",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Open LUA Monitor form
            var luaMonitorForm = new LuaMonitorForm(_process);
            luaMonitorForm.Show();
        }

        private void PEAnalysis_Click(object? sender, EventArgs e)
        {
            if (_process == null)
            {
                MessageBox.Show("Please attach to a process first.", "No Process",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Open PE Analysis form
            var peAnalysisForm = new PEAnalysisForm(_process);
            peAnalysisForm.Show();
        }

        private void ACBypassLab_Click(object? sender, EventArgs e)
        {
            // AC Bypass Research Lab does not require a process attachment
            // It can work standalone for driver testing and anti-cheat detection
            OpenOrFocusACBypassForm();
        }

        private void DllInjectionTest_Click(object? sender, EventArgs e)
        {
            var testForm = new DllInjectionTestForm(_process);
            testForm.Show();
        }


        private void About_Click(object? sender, EventArgs e)
        {
            string version = Application.ProductVersion;
            if (version.EndsWith(".0.0")) version = version[..^4];
            else if (version.EndsWith(".0")) version = version[..^2];

            string buildDate = File.GetLastWriteTime(Application.ExecutablePath).ToString("yyyy-MM-dd HH:mm:ss");
            string dotNetVersion = Environment.Version.ToString();

            string message = $"CrxMem v{version}\n\n" +
                            $"Build Date: {buildDate}\n" +
                            $".NET Version: {dotNetVersion}\n" +
                            $"Platform: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}\n\n" +
                            $"A modern memory scanner and debugger.\n" +
                            $"Inspired by Cheat Engine.";

            MessageBox.Show(message, "About CrxMem", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Unregister all global hotkeys before closing
            UnregisterAllHotkeys();

            // Stop and dispose fast process watcher
            _fastProcessWatcher?.Dispose();
            _fastProcessWatcher = null;

            base.OnFormClosing(e);
        }

        private class AddressEntry
        {
            public IntPtr Address { get; set; }
            public string Description { get; set; } = "";
            public string Type { get; set; } = "";
            public bool Active { get; set; } = true;
            public bool Frozen { get; set; } = false;
            public string FrozenValue { get; set; } = "";
            public bool ShowAsHex { get; set; } = false;
            /// <summary>
            /// Original address string (e.g., "Gunz.exe+29DF28") for module+offset addresses.
            /// Empty string means it was added from scan results (use raw hex address).
            /// </summary>
            public string OriginalAddressString { get; set; } = "";
        }
    }
}
