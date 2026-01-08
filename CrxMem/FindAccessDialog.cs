using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CrxMem.Core;
using Iced.Intel;

namespace CrxMem
{
    /// <summary>
    /// Filter policy for instruction relevance
    /// </summary>
    public enum AccessFilterMode
    {
        /// <summary>Show all memory accesses (raw VEH data with PAGE_GUARD filtering only)</summary>
        All,
        /// <summary>Hide pure comparison instructions (ucomiss, cmp, test, etc.)</summary>
        NoComparisons,
        /// <summary>Only show arithmetic instructions that modify values (addss, subss, etc.)</summary>
        ArithmeticOnly
    }

    /// <summary>
    /// Dialog that shows instructions that access a specific memory address
    /// </summary>
    public class FindAccessDialog : Form
    {
        private readonly ProcessAccess _process;
        private readonly ulong _watchAddress;
        private readonly int _watchSize;
        private readonly bool _writeOnly;
        private DebugMonitor? _monitor;

        private ListView _listView;
        private Button _btnStop;
        private Button _btnShowDisassembler;
        private Button _btnExport;
        private Button _btnClose;
        private System.Windows.Forms.Label _lblStatus;
        private System.Windows.Forms.Label _lblAddress;
        private ComboBox _cmbFilter;
        private System.Windows.Forms.Label _lblFilter;
        private SplitContainer _splitContainer;
        private RichTextBox _rtbExtraInfo;

        // Current filter mode
        private AccessFilterMode _filterMode = AccessFilterMode.NoComparisons;

        private readonly Dictionary<ulong, AccessEntry> _entries = new();
        private readonly object _entriesLock = new object();

        // Timeout/watchdog tracking
        private System.Windows.Forms.Timer? _watchdogTimer;
        private int _lastHitCount;
        private int _noHitSeconds;

        // UI update throttling to prevent flickering/lag
        private System.Windows.Forms.Timer? _uiUpdateTimer;
        private volatile bool _needsUiRefresh;
        private DateTime _lastUiUpdate = DateTime.MinValue;

        public event EventHandler<ulong>? NavigateToAddress;

        // Address format string based on process bitness
        private string _addressFormat => _process.Is64Bit ? "X16" : "X8";

        private string FormatAddress(ulong address) => _process.Is64Bit
            ? $"{address:X16}"
            : $"{(uint)address:X8}";

        public FindAccessDialog(ProcessAccess process, ulong address, bool writeOnly, int size = 4)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            _watchAddress = address;
            _watchSize = size;
            _writeOnly = writeOnly;

            InitializeComponent();
            ApplyTheme();
        }

        private void InitializeComponent()
        {
            this.Text = _writeOnly
                ? "Find out what writes to this address"
                : "Find out what accesses this address";
            this.Size = new Size(700, 450);
            this.MinimumSize = new Size(500, 300);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            // Address label
            _lblAddress = new System.Windows.Forms.Label
            {
                Text = $"Watching: {FormatAddress(_watchAddress)}",
                Location = new Point(10, 10),
                Size = new Size(400, 20),
                Font = new Font("Consolas", 10)
            };

            // Status label
            _lblStatus = new System.Windows.Forms.Label
            {
                Text = "Starting...",
                Location = new Point(10, 35),
                Size = new Size(300, 20)
            };

            // Filter label and dropdown
            _lblFilter = new System.Windows.Forms.Label
            {
                Text = "Filter:",
                Location = new Point(420, 10),
                Size = new Size(40, 20),
                TextAlign = ContentAlignment.MiddleRight
            };

            _cmbFilter = new ComboBox
            {
                Location = new Point(465, 7),
                Size = new Size(150, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbFilter.Items.AddRange(new object[] { "All", "No Comparisons", "Arithmetic Only" });
            _cmbFilter.SelectedIndex = 1; // Default to "No Comparisons"
            _cmbFilter.SelectedIndexChanged += CmbFilter_SelectedIndexChanged;

            // Split container for ListView + Extra Info panel
            _splitContainer = new SplitContainer
            {
                Location = new Point(10, 60),
                Size = new Size(665, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 180,
                Panel1MinSize = 100,
                Panel2MinSize = 80
            };

            // List view in top panel
            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Consolas", 9)
            };

            _listView.Columns.Add("Count", 60, HorizontalAlignment.Right);
            _listView.Columns.Add("Address", 140, HorizontalAlignment.Left);
            _listView.Columns.Add("Instruction", 300, HorizontalAlignment.Left);
            _listView.Columns.Add("Module", 150, HorizontalAlignment.Left);

            _listView.DoubleClick += ListView_DoubleClick;
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;

            // Extra info panel in bottom panel (CE-style surrounding disassembly)
            _rtbExtraInfo = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Text = "Select an instruction to see surrounding disassembly..."
            };

            _splitContainer.Panel1.Controls.Add(_listView);
            _splitContainer.Panel2.Controls.Add(_rtbExtraInfo);

            // Buttons
            _btnStop = new Button
            {
                Text = "Stop",
                Location = new Point(10, 370),
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _btnStop.Click += BtnStop_Click;

            _btnShowDisassembler = new Button
            {
                Text = "Show Disassembler",
                Location = new Point(120, 370),
                Size = new Size(130, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Enabled = false
            };
            _btnShowDisassembler.Click += BtnShowDisassembler_Click;

            _btnExport = new Button
            {
                Text = "Export",
                Location = new Point(260, 370),
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Enabled = false
            };
            _btnExport.Click += BtnExport_Click;

            _btnClose = new Button
            {
                Text = "Close",
                Location = new Point(590, 370),
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel
            };
            _btnClose.Click += (s, e) => this.Close();

            this.Controls.Add(_lblAddress);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(_lblFilter);
            this.Controls.Add(_cmbFilter);
            this.Controls.Add(_splitContainer);
            this.Controls.Add(_btnStop);
            this.Controls.Add(_btnShowDisassembler);
            this.Controls.Add(_btnExport);
            this.Controls.Add(_btnClose);

            this.CancelButton = _btnClose;
            this.FormClosing += FindAccessDialog_FormClosing;
            this.Load += FindAccessDialog_Load;
        }

        private void ApplyTheme()
        {
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;

            // SplitContainer
            _splitContainer.BackColor = ThemeManager.Background;

            // ListView with softer grid lines
            _listView.BackColor = ThemeManager.BackgroundAlt;
            _listView.ForeColor = ThemeManager.Foreground;
            _listView.GridLines = false; // Disable harsh white grid lines
            _listView.BorderStyle = BorderStyle.FixedSingle;

            // Extra info RichTextBox
            _rtbExtraInfo.BackColor = ThemeManager.BackgroundAlt;
            _rtbExtraInfo.ForeColor = ThemeManager.Foreground;

            // Set column header style
            _listView.OwnerDraw = true;
            _listView.DrawColumnHeader += (s, e) =>
            {
                e.Graphics.FillRectangle(new SolidBrush(ThemeManager.BackgroundAlt), e.Bounds);
                e.Graphics.DrawRectangle(new Pen(ThemeManager.Border), e.Bounds);
                TextRenderer.DrawText(e.Graphics, _listView.Columns[e.ColumnIndex].Text,
                    _listView.Font, e.Bounds, ThemeManager.Foreground,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            _listView.DrawItem += (s, e) => { e.DrawDefault = true; };
            _listView.DrawSubItem += (s, e) => { e.DrawDefault = true; };

            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Button btn)
                {
                    btn.BackColor = ThemeManager.BackgroundAlt;
                    btn.ForeColor = ThemeManager.Foreground;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = ThemeManager.Border;
                }
                else if (ctrl is System.Windows.Forms.Label lbl)
                {
                    lbl.ForeColor = ThemeManager.Foreground;
                }
                else if (ctrl is ComboBox cmb)
                {
                    cmb.BackColor = ThemeManager.BackgroundAlt;
                    cmb.ForeColor = ThemeManager.Foreground;
                    cmb.FlatStyle = FlatStyle.Flat;
                }
            }
        }

        private void CmbFilter_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _filterMode = _cmbFilter.SelectedIndex switch
            {
                0 => AccessFilterMode.All,
                1 => AccessFilterMode.NoComparisons,
                2 => AccessFilterMode.ArithmeticOnly,
                _ => AccessFilterMode.NoComparisons
            };

            // Refilter and refresh the list with the new mode
            RefilterAndRefresh();
        }

        private void RefilterAndRefresh()
        {
            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();

                lock (_entriesLock)
                {
                    // Sort by count descending and rebuild list
                    var sortedEntries = _entries.Values
                        .Where(e => ShouldShowEntry(e))
                        .OrderByDescending(e => e.Count)
                        .ToList();

                    foreach (var entry in sortedEntries)
                    {
                        var item = new ListViewItem(entry.Count.ToString())
                        {
                            Tag = entry.InstructionAddress
                        };
                        item.SubItems.Add(FormatAddress(entry.InstructionAddress));
                        item.SubItems.Add(entry.Instruction);
                        item.SubItems.Add(entry.Module);
                        _listView.Items.Add(item);
                    }
                }

                _btnShowDisassembler.Enabled = _listView.Items.Count > 0;
                _btnExport.Enabled = _listView.Items.Count > 0;
                UpdateStatusWithFilter();
            }
            finally
            {
                _listView.EndUpdate();
            }
        }

        private bool ShouldShowEntry(AccessEntry entry)
        {
            // Decode instruction to check filter
            try
            {
                byte[] buffer = _process.Read(new IntPtr((long)entry.InstructionAddress), 15);
                if (buffer == null || buffer.Length == 0)
                    return true;

                var decoder = Iced.Intel.Decoder.Create(_process.Is64Bit ? 64 : 32, buffer);
                decoder.IP = entry.InstructionAddress;
                var instruction = decoder.Decode();

                if (instruction.Code == Code.INVALID)
                    return true;

                return IsRelevantInstruction(instruction, _writeOnly);
            }
            catch
            {
                return true;
            }
        }

        private void FindAccessDialog_Load(object? sender, EventArgs e)
        {
            StartMonitoring();
        }

        private void StartMonitoring()
        {
            try
            {
                _monitor = new DebugMonitor(_process);
                _monitor.BreakpointHit += Monitor_BreakpointHit;
                _monitor.MonitoringStopped += Monitor_MonitoringStopped;

                if (_monitor.StartMonitoring(_watchAddress, _writeOnly))
                {
                    // Show which monitoring mode was auto-selected
                    string modeText = _monitor.IsUsingHardwareBreakpoint
                        ? "Hardware Breakpoint (dynamic address)"
                        : "PAGE_GUARD (static address)";
                    _lblStatus.Text = $"Monitoring via {modeText}... (waiting for access)";
                    _btnStop.Enabled = true;

                    // Start watchdog timer to show activity status
                    StartWatchdogTimer();

                    // Start UI update timer (throttled updates to prevent flickering)
                    StartUiUpdateTimer();

                    // Only offer fallback if using PAGE_GUARD (not already HW BP)
                    if (!_monitor.IsUsingHardwareBreakpoint)
                    {
                        // After 15 seconds, check if we got ANY hits (detect silent failures)
                        // Give user time to trigger the access in-game
                        Task.Delay(15000).ContinueWith(_ =>
                        {
                            if (_monitor != null && _monitor.GetHitCount() == 0 && _monitor.IsMonitoring)
                            {
                                // No hits with PAGE_GUARD - offer hardware breakpoint fallback
                                if (InvokeRequired)
                                    Invoke(new Action(OfferHardwareBreakpointFallback));
                                else
                                    OfferHardwareBreakpointFallback();
                            }
                        });
                    }
                }
                else
                {
                    // PAGE_GUARD failed completely - try hardware breakpoint automatically
                    if (!AttemptHardwareBreakpointFallback())
                    {
                        string errorDetail = string.IsNullOrEmpty(_monitor.LastError)
                            ? "Make sure you have admin rights."
                            : _monitor.LastError;
                        _lblStatus.Text = $"Failed: {errorDetail}";
                        _btnStop.Enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Error: {ex.Message}";
                _btnStop.Enabled = false;
            }
        }

        private bool AttemptHardwareBreakpointFallback()
        {
            try
            {
                // Try hardware breakpoint
                bool started = _monitor.StartMonitoringWithHardwareBreakpoint(_watchAddress, _writeOnly, 4);

                if (started)
                {
                    _lblStatus.Text = "Monitoring via Hardware Breakpoint (DR0-DR3)";
                    _btnStop.Enabled = true;

                    // Start watchdog timer
                    StartWatchdogTimer();
                    StartUiUpdateTimer();

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hardware breakpoint fallback failed: {ex.Message}");
            }

            return false;
        }

        private void OfferHardwareBreakpointFallback()
        {
            var result = MessageBox.Show(
                "PAGE_GUARD monitoring hasn't captured any accesses yet.\n\n" +
                "This often happens with stack or heap addresses.\n\n" +
                "Switch to Hardware Breakpoint mode (more reliable but limited to 4 addresses)?",
                "Switch to Hardware Breakpoint?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                StopMonitoring();

                // Give monitor time to stop
                Task.Delay(100).ContinueWith(_ =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => AttemptHardwareBreakpointFallback()));
                    else
                        AttemptHardwareBreakpointFallback();
                });
            }
        }

        private void StartWatchdogTimer()
        {
            _watchdogTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000 // 1 second
            };
            _watchdogTimer.Tick += WatchdogTimer_Tick;
            _watchdogTimer.Start();
            _lastHitCount = 0;
            _noHitSeconds = 0;
        }

        private void WatchdogTimer_Tick(object? sender, EventArgs e)
        {
            int currentHitCount;
            lock (_entriesLock)
            {
                currentHitCount = 0;
                foreach (var entry in _entries.Values)
                {
                    currentHitCount += entry.Count;
                }
            }

            if (currentHitCount == _lastHitCount)
            {
                // No new hits
                _noHitSeconds++;

                // Update status to show we're still waiting
                if (_entries.Count == 0)
                {
                    if (_noHitSeconds >= 10)
                    {
                        _lblStatus.Text = $"Monitoring active ({_noHitSeconds}s) - No accesses detected yet. Check if the address is being accessed.";
                    }
                    else
                    {
                        _lblStatus.Text = $"Monitoring... (waiting for access - {_noHitSeconds}s)";
                    }
                }
            }
            else
            {
                // Got new hits - reset counter
                _noHitSeconds = 0;
                _lastHitCount = currentHitCount;
            }
        }

        private void StopWatchdogTimer()
        {
            if (_watchdogTimer != null)
            {
                _watchdogTimer.Stop();
                _watchdogTimer.Dispose();
                _watchdogTimer = null;
            }
        }

        private void StartUiUpdateTimer()
        {
            _uiUpdateTimer = new System.Windows.Forms.Timer
            {
                Interval = 250 // Update UI at most 4 times per second
            };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            _uiUpdateTimer.Start();
        }

        private void StopUiUpdateTimer()
        {
            if (_uiUpdateTimer != null)
            {
                _uiUpdateTimer.Stop();
                _uiUpdateTimer.Dispose();
                _uiUpdateTimer = null;
            }
        }

        private void UiUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_needsUiRefresh)
            {
                _needsUiRefresh = false;
                RefreshListView();
            }
        }

        private void Monitor_BreakpointHit(object? sender, BreakpointHitEventArgs e)
        {
            // Process hits on background - just update the data, don't touch UI
            AddOrUpdateEntryData(e.InstructionAddress, e.ThreadId);
        }

        private void AddOrUpdateEntryData(ulong instructionAddress, uint threadId)
        {
            // Filter 1: Only add instructions that actually access the watched address range
            // PAGE_GUARD monitors an entire 4KB page, so we get many false positives
            if (!InstructionAccessesWatchedAddress(instructionAddress, out _))
            {
                return; // Skip this instruction - it accesses a different part of the page
            }

            // NOTE: We collect ALL instructions here. Filtering (NoComparisons, ArithmeticOnly)
            // is applied during DISPLAY so users can switch filters without losing data.

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(instructionAddress, out var entry))
                {
                    entry.Count++;
                    entry.NeedsUpdate = true;
                }
                else
                {
                    entry = new AccessEntry
                    {
                        InstructionAddress = instructionAddress,
                        Count = 1,
                        ThreadId = threadId,
                        Instruction = DisassembleInstruction(instructionAddress),
                        Module = GetModuleName(instructionAddress),
                        IsNew = true
                    };
                    _entries[instructionAddress] = entry;
                }
            }

            _needsUiRefresh = true;
        }

        private void RefreshListView()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(RefreshListView));
                return;
            }

            _listView.BeginUpdate();
            try
            {
                // Rebuild entire list sorted by count (descending), applying current filter
                _listView.Items.Clear();

                lock (_entriesLock)
                {
                    // Reset flags, apply filter, and sort by count descending
                    var sortedEntries = _entries.Values
                        .Where(e => ShouldShowEntry(e))
                        .OrderByDescending(e => e.Count)
                        .ToList();

                    foreach (var entry in sortedEntries)
                    {
                        entry.IsNew = false;
                        entry.NeedsUpdate = false;

                        var item = new ListViewItem(entry.Count.ToString())
                        {
                            Tag = entry.InstructionAddress
                        };
                        item.SubItems.Add(FormatAddress(entry.InstructionAddress));
                        item.SubItems.Add(entry.Instruction);
                        item.SubItems.Add(entry.Module);
                        _listView.Items.Add(item);
                    }
                }

                UpdateStatusWithFilter();
                _btnShowDisassembler.Enabled = _listView.Items.Count > 0;
                _btnExport.Enabled = _listView.Items.Count > 0;
            }
            finally
            {
                _listView.EndUpdate();
            }
        }

        /// <summary>
        /// Determines if an instruction is relevant based on the current filter mode.
        /// For "What writes", always checks if instruction writes to memory.
        /// For "What accesses", applies the selected filter policy.
        /// </summary>
        private bool IsRelevantInstruction(Instruction instruction, bool writeOnly)
        {
            // For "what WRITES to this address", show instructions where memory is the destination
            if (writeOnly)
            {
                return InstructionWritesToMemory(instruction);
            }

            // Apply filter based on current mode
            return _filterMode switch
            {
                AccessFilterMode.All => true,
                AccessFilterMode.NoComparisons => !IsComparisonInstruction(instruction),
                AccessFilterMode.ArithmeticOnly => IsArithmeticInstruction(instruction),
                _ => true
            };
        }

        /// <summary>
        /// Returns true if the instruction is a comparison/test instruction (noise for value tracking)
        /// </summary>
        private static bool IsComparisonInstruction(Instruction instruction)
        {
            var mnemonic = instruction.Mnemonic;
            switch (mnemonic)
            {
                // SSE/AVX scalar comparisons
                case Mnemonic.Ucomiss:
                case Mnemonic.Ucomisd:
                case Mnemonic.Comiss:
                case Mnemonic.Comisd:
                case Mnemonic.Cmpss:
                case Mnemonic.Cmpsd:
                case Mnemonic.Cmpps:
                case Mnemonic.Cmppd:
                case Mnemonic.Vcmpss:
                case Mnemonic.Vcmpsd:
                case Mnemonic.Vcmpps:
                case Mnemonic.Vcmppd:
                case Mnemonic.Vucomiss:
                case Mnemonic.Vucomisd:
                case Mnemonic.Vcomiss:
                case Mnemonic.Vcomisd:
                // General compare/test instructions
                case Mnemonic.Cmp:
                case Mnemonic.Test:
                // x87 FPU compare instructions
                case Mnemonic.Fcom:
                case Mnemonic.Fcomp:
                case Mnemonic.Fcompp:
                case Mnemonic.Fcomi:
                case Mnemonic.Fcomip:
                case Mnemonic.Fucomi:
                case Mnemonic.Fucomip:
                case Mnemonic.Fucom:
                case Mnemonic.Fucomp:
                case Mnemonic.Fucompp:
                case Mnemonic.Ftst:
                case Mnemonic.Fxam:
                case Mnemonic.Ficom:
                case Mnemonic.Ficomp:
                // Bit test instructions
                case Mnemonic.Bt:
                case Mnemonic.Btc:
                case Mnemonic.Btr:
                case Mnemonic.Bts:
                // Packed compare instructions
                case Mnemonic.Pcmpeqb:
                case Mnemonic.Pcmpeqw:
                case Mnemonic.Pcmpeqd:
                case Mnemonic.Pcmpeqq:
                case Mnemonic.Pcmpgtb:
                case Mnemonic.Pcmpgtw:
                case Mnemonic.Pcmpgtd:
                case Mnemonic.Pcmpgtq:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true if the instruction performs arithmetic on the memory value
        /// </summary>
        private static bool IsArithmeticInstruction(Instruction instruction)
        {
            var mnemonic = instruction.Mnemonic;
            switch (mnemonic)
            {
                // SSE scalar single-precision arithmetic
                case Mnemonic.Addss:
                case Mnemonic.Subss:
                case Mnemonic.Mulss:
                case Mnemonic.Divss:
                // SSE scalar double-precision arithmetic
                case Mnemonic.Addsd:
                case Mnemonic.Subsd:
                case Mnemonic.Mulsd:
                case Mnemonic.Divsd:
                // AVX scalar single-precision arithmetic
                case Mnemonic.Vaddss:
                case Mnemonic.Vsubss:
                case Mnemonic.Vmulss:
                case Mnemonic.Vdivss:
                // AVX scalar double-precision arithmetic
                case Mnemonic.Vaddsd:
                case Mnemonic.Vsubsd:
                case Mnemonic.Vmulsd:
                case Mnemonic.Vdivsd:
                // SSE packed single-precision arithmetic
                case Mnemonic.Addps:
                case Mnemonic.Subps:
                case Mnemonic.Mulps:
                case Mnemonic.Divps:
                // SSE packed double-precision arithmetic
                case Mnemonic.Addpd:
                case Mnemonic.Subpd:
                case Mnemonic.Mulpd:
                case Mnemonic.Divpd:
                // AVX packed arithmetic
                case Mnemonic.Vaddps:
                case Mnemonic.Vsubps:
                case Mnemonic.Vmulps:
                case Mnemonic.Vdivps:
                case Mnemonic.Vaddpd:
                case Mnemonic.Vsubpd:
                case Mnemonic.Vmulpd:
                case Mnemonic.Vdivpd:
                // x87 FPU arithmetic with memory operand
                case Mnemonic.Fadd:
                case Mnemonic.Fsub:
                case Mnemonic.Fmul:
                case Mnemonic.Fdiv:
                case Mnemonic.Fiadd:
                case Mnemonic.Fisub:
                case Mnemonic.Fimul:
                case Mnemonic.Fidiv:
                case Mnemonic.Fsubr:
                case Mnemonic.Fdivr:
                case Mnemonic.Fisubr:
                case Mnemonic.Fidivr:
                // Integer arithmetic
                case Mnemonic.Add:
                case Mnemonic.Sub:
                case Mnemonic.Imul:
                case Mnemonic.Idiv:
                case Mnemonic.Mul:
                case Mnemonic.Div:
                case Mnemonic.Inc:
                case Mnemonic.Dec:
                case Mnemonic.Neg:
                // Bitwise operations (can be used for value manipulation)
                case Mnemonic.And:
                case Mnemonic.Or:
                case Mnemonic.Xor:
                case Mnemonic.Shl:
                case Mnemonic.Shr:
                case Mnemonic.Sar:
                case Mnemonic.Rol:
                case Mnemonic.Ror:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if the instruction writes to memory (vs just reading from it)
        /// </summary>
        private bool InstructionWritesToMemory(Instruction instruction)
        {
            // Check the first operand (usually the destination)
            // For most x86/x64 instructions, the first operand is the destination
            if (instruction.OpCount == 0)
                return false;

            // For instructions like "mov [mem], reg" - first operand is memory destination
            if (instruction.GetOpKind(0) == OpKind.Memory)
                return true;

            // Special case: some instructions have destination in later operands
            // or modify memory implicitly (push, call with stack, etc.)
            var mnemonic = instruction.Mnemonic;

            // Instructions that always write to memory
            switch (mnemonic)
            {
                case Mnemonic.Push:
                case Mnemonic.Pushf:
                case Mnemonic.Pushfd:
                case Mnemonic.Pushfq:
                case Mnemonic.Pusha:
                case Mnemonic.Pushad:
                    return true; // Push writes to stack

                // x87 store instructions
                case Mnemonic.Fst:
                case Mnemonic.Fstp:
                case Mnemonic.Fist:
                case Mnemonic.Fistp:
                case Mnemonic.Fisttp:
                case Mnemonic.Fbstp:
                    return true;

                // String instructions with rep
                case Mnemonic.Stosb:
                case Mnemonic.Stosw:
                case Mnemonic.Stosd:
                case Mnemonic.Stosq:
                case Mnemonic.Movsb:
                case Mnemonic.Movsw:
                case Mnemonic.Movsd: // Note: this is overloaded - string move vs scalar double
                case Mnemonic.Movsq:
                    return true;
            }

            // Check for SSE/AVX moves where destination might be second operand
            // Format: movss xmm, [mem] = read, movss [mem], xmm = write
            // We already checked if first operand is memory, so if we get here it's a read
            return false;
        }

        /// <summary>
        /// Check if an instruction at the given address actually accesses the watched memory range.
        /// This filters out false positives from PAGE_GUARD mode which monitors entire 4KB pages.
        /// Also returns the decoded instruction for further semantic filtering.
        /// </summary>
        private bool InstructionAccessesWatchedAddress(ulong instructionAddress, out Instruction? decodedInstruction)
        {
            decodedInstruction = null;
            try
            {
                byte[] buffer = _process.Read(new IntPtr((long)instructionAddress), 15);
                if (buffer == null || buffer.Length == 0)
                    return true; // Can't read instruction, assume it's relevant

                var decoder = Iced.Intel.Decoder.Create(_process.Is64Bit ? 64 : 32, buffer);
                decoder.IP = instructionAddress;

                var instruction = decoder.Decode();
                decodedInstruction = instruction; // Return for semantic filtering

                if (instruction.Code == Code.INVALID)
                    return true; // Can't decode, assume relevant

                // Check all memory operands of the instruction
                for (int i = 0; i < instruction.OpCount; i++)
                {
                    if (instruction.GetOpKind(i) == OpKind.Memory)
                    {
                        // Check if this is a DIRECT static memory access (no base/index registers)
                        // Only these can be reliably filtered
                        bool hasBaseReg = instruction.MemoryBase != Register.None;
                        bool hasIndexReg = instruction.MemoryIndex != Register.None;

                        if (!hasBaseReg && !hasIndexReg)
                        {
                            // Pure displacement-only access like [0x004FDEB0]
                            // This is a static address we can filter
                            ulong disp = instruction.MemoryDisplacement64;
                            int accessSize = GetMemoryOperandSize(instruction);

                            ulong watchEnd = _watchAddress + (ulong)_watchSize;
                            ulong accessEnd = disp + (ulong)accessSize;

                            // Check if ranges overlap
                            if (disp < watchEnd && accessEnd > _watchAddress)
                            {
                                return true; // Overlaps with watched range
                            }
                            // Static address that doesn't match - can filter this out
                            continue;
                        }

                        // For RIP-relative addressing (common in 64-bit), compute the address
                        if (_process.Is64Bit && instruction.IsIPRelativeMemoryOperand)
                        {
                            ulong ripAddr = instruction.IPRelativeMemoryAddress;
                            int accessSize = GetMemoryOperandSize(instruction);

                            ulong watchEnd = _watchAddress + (ulong)_watchSize;
                            ulong accessEnd = ripAddr + (ulong)accessSize;

                            if (ripAddr < watchEnd && accessEnd > _watchAddress)
                            {
                                return true;
                            }
                            // RIP-relative but doesn't match - can filter this out
                            continue;
                        }

                        // Register-based access like [eax], [esi+10h], [ebx+ecx*4+8], etc.
                        // We can't determine the runtime address statically, so include it
                        return true;
                    }
                }

                // No memory operand found or none of the static addresses matched
                // Conservative: If PAGE_GUARD fired and we can't determine if instruction
                // accesses the watched address, assume it MIGHT be relevant (better to show extra results)
                return true; // Changed from false - conservative assumption for PAGE_GUARD mode
            }
            catch
            {
                decodedInstruction = null;
                return true; // On error, assume relevant
            }
        }

        /// <summary>
        /// Get the size of a memory operand in bytes
        /// </summary>
        private int GetMemoryOperandSize(Instruction instruction)
        {
            // Try to determine memory operand size from the instruction
            return instruction.MemorySize switch
            {
                MemorySize.UInt8 or MemorySize.Int8 => 1,
                MemorySize.UInt16 or MemorySize.Int16 => 2,
                MemorySize.UInt32 or MemorySize.Int32 or MemorySize.Float32 => 4,
                MemorySize.UInt64 or MemorySize.Int64 or MemorySize.Float64 => 8,
                MemorySize.UInt128 or MemorySize.Int128 or MemorySize.Float128 => 16,
                _ => 4 // Default to 4 bytes
            };
        }

        private void AddListViewItem(AccessEntry entry)
        {
            var item = new ListViewItem(entry.Count.ToString())
            {
                Tag = entry.InstructionAddress
            };
            item.SubItems.Add(FormatAddress(entry.InstructionAddress));
            item.SubItems.Add(entry.Instruction);
            item.SubItems.Add(entry.Module);

            _listView.Items.Insert(0, item); // Add at top
        }

        private void UpdateListViewItem(AccessEntry entry)
        {
            foreach (ListViewItem item in _listView.Items)
            {
                if (item.Tag is ulong addr && addr == entry.InstructionAddress)
                {
                    item.Text = entry.Count.ToString();
                    // Move to top if hit again
                    _listView.Items.Remove(item);
                    _listView.Items.Insert(0, item);
                    break;
                }
            }
        }

        private string DisassembleInstruction(ulong address)
        {
            try
            {
                byte[] buffer = _process.Read(new IntPtr((long)address), 15);
                if (buffer == null || buffer.Length == 0)
                    return "(failed to read)";

                var decoder = Iced.Intel.Decoder.Create(_process.Is64Bit ? 64 : 32, buffer);
                decoder.IP = address;

                var instruction = decoder.Decode();
                if (instruction.Code != Code.INVALID)
                {
                    var formatter = new NasmFormatter();
                    var output = new StringOutput();
                    formatter.Format(instruction, output);
                    return output.ToStringAndReset();
                }
            }
            catch { }

            return "(disassembly failed)";
        }

        private string GetModuleName(ulong address)
        {
            try
            {
                var module = _process.GetModuleForAddress(new IntPtr((long)address));
                if (module != null)
                {
                    // Debug: show module base for troubleshooting
                    System.Diagnostics.Debug.WriteLine($"GetModuleName: addr=0x{address:X8} -> {module.ModuleName} (base=0x{module.BaseAddress.ToInt64():X8}, size=0x{module.Size:X})");
                    return module.ModuleName;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GetModuleName: addr=0x{address:X8} -> NO MODULE FOUND");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetModuleName: addr=0x{address:X8} -> ERROR: {ex.Message}");
            }

            return "";
        }

        private void UpdateStatusWithFilter()
        {
            int totalHits = 0;
            int visibleCount = _listView.Items.Count;
            int totalUnique;

            lock (_entriesLock)
            {
                totalUnique = _entries.Count;
                foreach (var entry in _entries.Values)
                {
                    totalHits += entry.Count;
                }
            }

            string filterName = _filterMode switch
            {
                AccessFilterMode.All => "All",
                AccessFilterMode.NoComparisons => "No Cmp",
                AccessFilterMode.ArithmeticOnly => "Arith",
                _ => ""
            };

            if (visibleCount < totalUnique)
            {
                _lblStatus.Text = $"Showing {visibleCount}/{totalUnique} instructions, {totalHits} total hits [{filterName}]";
            }
            else
            {
                _lblStatus.Text = $"Found {totalUnique} instruction(s), {totalHits} total hit(s)";
            }
        }

        private void Monitor_MonitoringStopped(object? sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() =>
                {
                    _lblStatus.Text += " - Stopped";
                    _btnStop.Enabled = false;
                }));
            }
            else
            {
                _lblStatus.Text += " - Stopped";
                _btnStop.Enabled = false;
            }
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            StopMonitoring();
        }

        private void StopMonitoring()
        {
            StopWatchdogTimer();
            StopUiUpdateTimer();
            _monitor?.StopMonitoring();
            _btnStop.Enabled = false;

            // Do a final refresh to show all collected data
            RefreshListView();
        }

        private void BtnShowDisassembler_Click(object? sender, EventArgs e)
        {
            ulong address = _watchAddress;

            if (_listView.SelectedItems.Count > 0 && _listView.SelectedItems[0].Tag is ulong selectedAddr)
            {
                address = selectedAddr;
            }
            else if (_listView.Items.Count > 0 && _listView.Items[0].Tag is ulong firstAddr)
            {
                address = firstAddr;
            }

            NavigateToAddress?.Invoke(this, address);
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Export Results",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"access_{_watchAddress:X}_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    ExportResultsToFile(dialog.FileName);
                    MessageBox.Show($"Results exported to:\n{dialog.FileName}", "Export Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export results:\n{ex.Message}", "Export Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportResultsToFile(string filePath)
        {
            using var writer = new System.IO.StreamWriter(filePath);

            // Write header
            string mode = _writeOnly ? "What writes to this address" : "What accesses this address";
            writer.WriteLine($"CrxMem - {mode}");
            writer.WriteLine($"Watched Address: {_watchAddress:X16}");
            writer.WriteLine($"Process: {_process.Target?.ProcessName ?? "Unknown"} (PID: {_process.Target?.Id ?? 0})");
            writer.WriteLine($"Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine(new string('=', 80));
            writer.WriteLine();

            // Write column headers
            writer.WriteLine($"{"Count",-10} {"Address",-18} {"Instruction",-40} {"Module"}");
            writer.WriteLine(new string('-', 80));

            // Write entries
            lock (_entriesLock)
            {
                // Sort by count descending
                var sortedEntries = _entries.Values.OrderByDescending(e => e.Count);

                foreach (var entry in sortedEntries)
                {
                    writer.WriteLine($"{entry.Count,-10} {FormatAddress(entry.InstructionAddress),-18}  {entry.Instruction,-40} {entry.Module}");
                }
            }

            writer.WriteLine();
            writer.WriteLine(new string('=', 80));

            // Write summary
            int totalHits = 0;
            lock (_entriesLock)
            {
                foreach (var entry in _entries.Values)
                {
                    totalHits += entry.Count;
                }
            }
            writer.WriteLine($"Total: {_entries.Count} unique instruction(s), {totalHits} total hit(s)");
        }

        private void ListView_DoubleClick(object? sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count > 0 && _listView.SelectedItems[0].Tag is ulong address)
            {
                NavigateToAddress?.Invoke(this, address);
            }
        }

        private void ListView_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count > 0 && _listView.SelectedItems[0].Tag is ulong address)
            {
                ShowExtraInfo(address);
            }
        }

        /// <summary>
        /// Shows CE-style surrounding disassembly for the selected instruction
        /// </summary>
        private void ShowExtraInfo(ulong instructionAddress)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Address          Instruction");
            sb.AppendLine(new string('-', 60));

            try
            {
                // Read ~40 bytes before and after to get surrounding instructions
                // Start earlier to ensure we have enough context
                ulong startAddr = instructionAddress > 40 ? instructionAddress - 40 : 0;
                int totalBytes = 100; // Should cover ~10 instructions before + target + 10 after

                byte[] code = _process.Read(new IntPtr((long)startAddr), totalBytes);
                if (code == null || code.Length == 0)
                {
                    sb.AppendLine("(Failed to read memory)");
                    _rtbExtraInfo.Text = sb.ToString();
                    return;
                }

                var decoder = Iced.Intel.Decoder.Create(_process.Is64Bit ? 64 : 32, code);
                decoder.IP = startAddr;

                var formatter = new NasmFormatter();
                var output = new StringOutput();

                // Collect all instructions
                var instructions = new List<(ulong addr, string text, int len)>();
                while (decoder.IP < startAddr + (ulong)code.Length)
                {
                    ulong currentIP = decoder.IP;
                    var instruction = decoder.Decode();
                    if (instruction.Code == Code.INVALID)
                        break;

                    output.Reset();
                    formatter.Format(instruction, output);
                    instructions.Add((currentIP, output.ToStringAndReset(), instruction.Length));
                }

                // Find the target instruction index
                int targetIdx = -1;
                for (int i = 0; i < instructions.Count; i++)
                {
                    if (instructions[i].addr == instructionAddress)
                    {
                        targetIdx = i;
                        break;
                    }
                }

                // Show 5 before, target, 5 after
                int startIdx = targetIdx >= 5 ? targetIdx - 5 : 0;
                int endIdx = Math.Min(targetIdx + 6, instructions.Count);

                for (int i = startIdx; i < endIdx; i++)
                {
                    var (addr, text, _) = instructions[i];
                    string prefix = (i == targetIdx) ? ">>> " : "    ";
                    sb.AppendLine($"{prefix}{FormatAddress(addr)}  {text}");
                }

                if (targetIdx == -1)
                {
                    sb.AppendLine();
                    sb.AppendLine($"(Target instruction at {FormatAddress(instructionAddress)} not found in context)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(Error: {ex.Message})");
            }

            _rtbExtraInfo.Text = sb.ToString();
        }

        private void FindAccessDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            StopMonitoring();
            _monitor?.Dispose();
        }

        private class AccessEntry
        {
            public ulong InstructionAddress { get; set; }
            public int Count { get; set; }
            public uint ThreadId { get; set; }
            public string Instruction { get; set; } = "";
            public string Module { get; set; } = "";
            public bool IsNew { get; set; }
            public bool NeedsUpdate { get; set; }
        }
    }
}
