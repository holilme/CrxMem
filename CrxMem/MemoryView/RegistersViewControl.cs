using System;
using System.Drawing;
using System.Windows.Forms;

namespace CrxMem.MemoryView
{
    /// <summary>
    /// Displays CPU registers in x64dbg style
    /// </summary>
    public class RegistersViewControl : UserControl
    {
        private Font _font;
        private Font _fontBold;
        private int _rowHeight;
        private VScrollBar _vScrollBar;

        // Register values (would come from debugger in real implementation)
        private ulong _rax, _rbx, _rcx, _rdx;
        private ulong _rsi, _rdi, _rbp, _rsp;
        private ulong _r8, _r9, _r10, _r11;
        private ulong _r12, _r13, _r14, _r15;
        private ulong _rip, _rflags;

        // Segment registers
        private ushort _cs, _ds, _es, _fs, _gs, _ss;

        // Flags
        private bool _cf, _pf, _af, _zf, _sf, _tf, _if, _df, _of;

        private bool _is64Bit = true;
        private Core.ProcessAccess? _process;
        private ulong[] _previousValues = new ulong[32]; // For change detection
        private bool[] _changedFlags = new bool[32];     // Which registers changed
        private uint _selectedThreadId;

        public RegistersViewControl()
        {
            _font = new Font("Consolas", 9F);
            _fontBold = new Font("Consolas", 9F, FontStyle.Bold);
            _rowHeight = 16;

            DoubleBuffered = true;

            _vScrollBar = new VScrollBar
            {
                Dock = DockStyle.Right,
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 10
            };
            _vScrollBar.Scroll += (s, e) => Invalidate();
            Controls.Add(_vScrollBar);

            ApplyTheme();
            InitializeContextMenu();

            // Set some demo values
            SetDemoValues();
        }

        private void SetDemoValues()
        {
            // Demo values to show the UI
            _rax = 0x00007FF6A1234567;
            _rbx = 0x0000000000000000;
            _rcx = 0x00000000DEADBEEF;
            _rdx = 0x0000000000000001;
            _rsi = 0x000000000063DF28;
            _rdi = 0x00007FF6A1230000;
            _rbp = 0x000000000014F9D0;
            _rsp = 0x000000000014F8B0;
            _r8 = 0x0000000000000000;
            _r9 = 0x00000000FFFFFFFF;
            _r10 = 0x00007FFE12345678;
            _r11 = 0x0000000000000246;
            _r12 = 0x0000000000000000;
            _r13 = 0x0000000000000000;
            _r14 = 0x0000000000000000;
            _r15 = 0x0000000000000000;
            _rip = 0x00007FF6A1234567;
            _rflags = 0x0000000000000246;

            _cs = 0x0033;
            _ds = 0x002B;
            _es = 0x002B;
            _fs = 0x0053;
            _gs = 0x002B;
            _ss = 0x002B;

            // Parse flags from rflags
            _cf = (_rflags & 0x0001) != 0;
            _pf = (_rflags & 0x0004) != 0;
            _af = (_rflags & 0x0010) != 0;
            _zf = (_rflags & 0x0040) != 0;
            _sf = (_rflags & 0x0080) != 0;
            _tf = (_rflags & 0x0100) != 0;
            _if = (_rflags & 0x0200) != 0;
            _df = (_rflags & 0x0400) != 0;
            _of = (_rflags & 0x0800) != 0;
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.Background;
            ForeColor = ThemeManager.Foreground;
            Invalidate();
        }

        public void SetProcess(Core.ProcessAccess process)
        {
            _process = process;
            _is64Bit = process.Is64Bit;
            Invalidate();
        }

        public void UpdateFromContext(Core.CONTEXT64 context)
        {
            // Store previous values for change detection
            _previousValues[0] = _rax; _previousValues[1] = _rbx; _previousValues[2] = _rcx; _previousValues[3] = _rdx;
            _previousValues[4] = _rsi; _previousValues[5] = _rdi; _previousValues[6] = _rbp; _previousValues[7] = _rsp;
            _previousValues[8] = _r8; _previousValues[9] = _r9; _previousValues[10] = _r10; _previousValues[11] = _r11;
            _previousValues[12] = _r12; _previousValues[13] = _r13; _previousValues[14] = _r14; _previousValues[15] = _r15;
            _previousValues[16] = _rip; _previousValues[17] = _rflags;

            // Update current values
            _rax = context.Rax; _rbx = context.Rbx; _rcx = context.Rcx; _rdx = context.Rdx;
            _rsi = context.Rsi; _rdi = context.Rdi; _rbp = context.Rbp; _rsp = context.Rsp;
            _r8 = context.R8; _r9 = context.R9; _r10 = context.R10; _r11 = context.R11;
            _r12 = context.R12; _r13 = context.R13; _r14 = context.R14; _r15 = context.R15;
            _rip = context.Rip; _rflags = context.EFlags;

            _cs = context.SegCs; _ds = context.SegDs; _es = context.SegEs;
            _fs = context.SegFs; _gs = context.SegGs; _ss = context.SegSs;

            // Detect changes
            for (int i = 0; i < 18; i++) _changedFlags[i] = GetValueByIndex(i) != _previousValues[i];

            ParseFlags();
            Invalidate();
        }

        public void UpdateFromContext(Core.CONTEXT32 context)
        {
            _previousValues[0] = _rax; _previousValues[1] = _rbx; _previousValues[2] = _rcx; _previousValues[3] = _rdx;
            _previousValues[4] = _rsi; _previousValues[5] = _rdi; _previousValues[6] = _rbp; _previousValues[7] = _rsp;
            _previousValues[16] = _rip; _previousValues[17] = _rflags;

            _rax = context.Eax; _rbx = context.Ebx; _rcx = context.Ecx; _rdx = context.Edx;
            _rsi = context.Esi; _rdi = context.Edi; _rbp = context.Ebp; _rsp = context.Esp;
            _rip = context.Eip; _rflags = context.EFlags;

            _cs = (ushort)context.SegCs; _ds = (ushort)context.SegDs; _es = (ushort)context.SegEs;
            _fs = (ushort)context.SegFs; _gs = (ushort)context.SegGs; _ss = (ushort)context.SegSs;

            for (int i = 0; i < 8; i++) _changedFlags[i] = GetValueByIndex(i) != _previousValues[i];
            _changedFlags[16] = _rip != _previousValues[16];
            _changedFlags[17] = _rflags != _previousValues[17];

            ParseFlags();
            Invalidate();
        }

        private ulong GetValueByIndex(int index) => index switch {
            0 => _rax, 1 => _rbx, 2 => _rcx, 3 => _rdx, 4 => _rsi, 5 => _rdi, 6 => _rbp, 7 => _rsp,
            8 => _r8, 9 => _r9, 10 => _r10, 11 => _r11, 12 => _r12, 13 => _r13, 14 => _r14, 15 => _r15,
            16 => _rip, 17 => _rflags, _ => 0
        };

        private void ParseFlags()
        {
            _cf = (_rflags & 0x0001) != 0;
            _pf = (_rflags & 0x0004) != 0;
            _af = (_rflags & 0x0010) != 0;
            _zf = (_rflags & 0x0040) != 0;
            _sf = (_rflags & 0x0080) != 0;
            _tf = (_rflags & 0x0100) != 0;
            _if = (_rflags & 0x0200) != 0;
            _df = (_rflags & 0x0400) != 0;
            _of = (_rflags & 0x0800) != 0;
        }

        public void RefreshFromThread(uint threadId)
        {
            if (_process == null) return;
            _selectedThreadId = threadId;

            IntPtr hThread = Core.ProcessAccess.OpenThread(Core.ProcessAccess.THREAD_GET_CONTEXT, false, threadId);
            if (hThread == IntPtr.Zero) return;

            try
            {
                if (_is64Bit)
                {
                    var context = new Core.CONTEXT64 { ContextFlags = Core.ProcessAccess.CONTEXT_ALL };
                    if (Core.ProcessAccess.GetThreadContext(hThread, ref context)) UpdateFromContext(context);
                }
                else
                {
                    var context = new Core.CONTEXT32 { ContextFlags = Core.ProcessAccess.CONTEXT_ALL };
                    if (Core.ProcessAccess.Wow64GetThreadContext(hThread, ref context)) UpdateFromContext(context);
                }
            }
            finally { Core.ProcessAccess.CloseHandle(hThread); }
        }
        public void SetArchitecture(bool is64Bit)
        {
            _is64Bit = is64Bit;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.Clear(BackColor);

            int yPos = 5 - (_vScrollBar.Value * _rowHeight);
            int col1 = 5;  // Register name
            int col2 = 45; // Register value

            using var nameBrush = new SolidBrush(ThemeManager.SyntaxRegister);
            using var valueBrush = new SolidBrush(ThemeManager.Foreground);
            using var changedBrush = new SolidBrush(ThemeManager.StatusRed);
            using var sectionBrush = new SolidBrush(ThemeManager.SyntaxMnemonic);
            using var flagOnBrush = new SolidBrush(ThemeManager.StatusGreen);
            using var flagOffBrush = new SolidBrush(ThemeManager.ForegroundDim);

            // General Purpose Registers
            DrawSectionHeader(g, "General Purpose", ref yPos, sectionBrush);

            if (_is64Bit)
            {
                DrawRegister(g, "RAX", _rax, col1, col2, ref yPos, nameBrush, valueBrush, true, 0);
                DrawRegister(g, "RBX", _rbx, col1, col2, ref yPos, nameBrush, valueBrush, true, 1);
                DrawRegister(g, "RCX", _rcx, col1, col2, ref yPos, nameBrush, valueBrush, true, 2);
                DrawRegister(g, "RDX", _rdx, col1, col2, ref yPos, nameBrush, valueBrush, true, 3);
                DrawRegister(g, "RSI", _rsi, col1, col2, ref yPos, nameBrush, valueBrush, true, 4);
                DrawRegister(g, "RDI", _rdi, col1, col2, ref yPos, nameBrush, valueBrush, true, 5);
                DrawRegister(g, "RBP", _rbp, col1, col2, ref yPos, nameBrush, valueBrush, true, 6);
                DrawRegister(g, "RSP", _rsp, col1, col2, ref yPos, nameBrush, valueBrush, true, 7);
                DrawRegister(g, "R8", _r8, col1, col2, ref yPos, nameBrush, valueBrush, true, 8);
                DrawRegister(g, "R9", _r9, col1, col2, ref yPos, nameBrush, valueBrush, true, 9);
                DrawRegister(g, "R10", _r10, col1, col2, ref yPos, nameBrush, valueBrush, true, 10);
                DrawRegister(g, "R11", _r11, col1, col2, ref yPos, nameBrush, valueBrush, true, 11);
                DrawRegister(g, "R12", _r12, col1, col2, ref yPos, nameBrush, valueBrush, true, 12);
                DrawRegister(g, "R13", _r13, col1, col2, ref yPos, nameBrush, valueBrush, true, 13);
                DrawRegister(g, "R14", _r14, col1, col2, ref yPos, nameBrush, valueBrush, true, 14);
                DrawRegister(g, "R15", _r15, col1, col2, ref yPos, nameBrush, valueBrush, true, 15);
            }
            else
            {
                DrawRegister(g, "EAX", (uint)_rax, col1, col2, ref yPos, nameBrush, valueBrush, false, 0);
                DrawRegister(g, "EBX", (uint)_rbx, col1, col2, ref yPos, nameBrush, valueBrush, false, 1);
                DrawRegister(g, "ECX", (uint)_rcx, col1, col2, ref yPos, nameBrush, valueBrush, false, 2);
                DrawRegister(g, "EDX", (uint)_rdx, col1, col2, ref yPos, nameBrush, valueBrush, false, 3);
                DrawRegister(g, "ESI", (uint)_rsi, col1, col2, ref yPos, nameBrush, valueBrush, false, 4);
                DrawRegister(g, "EDI", (uint)_rdi, col1, col2, ref yPos, nameBrush, valueBrush, false, 5);
                DrawRegister(g, "EBP", (uint)_rbp, col1, col2, ref yPos, nameBrush, valueBrush, false, 6);
                DrawRegister(g, "ESP", (uint)_rsp, col1, col2, ref yPos, nameBrush, valueBrush, false, 7);
            }

            yPos += 5;

            // Instruction Pointer
            DrawSectionHeader(g, "Instruction Pointer", ref yPos, sectionBrush);
            if (_is64Bit)
                DrawRegister(g, "RIP", _rip, col1, col2, ref yPos, nameBrush, valueBrush, true, 16);
            else
                DrawRegister(g, "EIP", (uint)_rip, col1, col2, ref yPos, nameBrush, valueBrush, false, 16);

            yPos += 5;

            // Flags
            DrawSectionHeader(g, "Flags", ref yPos, sectionBrush);
            DrawRegister(g, "RFLAGS", _rflags, col1, col2, ref yPos, nameBrush, valueBrush, true, 17);

            // Individual flags
            yPos += 2;
            DrawFlags(g, col1, ref yPos, flagOnBrush, flagOffBrush);

            yPos += 5;

            // Segment Registers
            DrawSectionHeader(g, "Segments", ref yPos, sectionBrush);
            DrawSegmentRegister(g, "CS", _cs, col1, col2, ref yPos, nameBrush, valueBrush);
            DrawSegmentRegister(g, "DS", _ds, col1, col2, ref yPos, nameBrush, valueBrush);
            DrawSegmentRegister(g, "ES", _es, col1, col2, ref yPos, nameBrush, valueBrush);
            DrawSegmentRegister(g, "FS", _fs, col1, col2, ref yPos, nameBrush, valueBrush);
            DrawSegmentRegister(g, "GS", _gs, col1, col2, ref yPos, nameBrush, valueBrush);
            DrawSegmentRegister(g, "SS", _ss, col1, col2, ref yPos, nameBrush, valueBrush);

            // Update scrollbar
            int totalHeight = yPos + (_vScrollBar.Value * _rowHeight);
            _vScrollBar.Maximum = Math.Max(0, (totalHeight - Height) / _rowHeight + 10);
        }

        private void DrawSectionHeader(Graphics g, string text, ref int yPos, Brush brush)
        {
            g.DrawString(text, _fontBold, brush, 5, yPos);
            yPos += _rowHeight + 2;
        }

        private void DrawRegister(Graphics g, string name, ulong value, int col1, int col2, ref int yPos,
            Brush nameBrush, Brush valueBrush, bool is64, int index)
        {
            if (yPos < -_rowHeight || yPos > Height) { yPos += _rowHeight; return; }

            g.DrawString(name, _font, nameBrush, col1, yPos);

            Brush actualValueBrush = _changedFlags[index] ? new SolidBrush(ThemeManager.StatusRed) : valueBrush;
            string valueStr = is64 ? $"{value:X16}" : $"{(uint)value:X8}";
            g.DrawString(valueStr, _font, actualValueBrush, col2, yPos);

            yPos += _rowHeight;
        }

        private void DrawSegmentRegister(Graphics g, string name, ushort value, int col1, int col2, ref int yPos,
            Brush nameBrush, Brush valueBrush)
        {
            if (yPos < -_rowHeight || yPos > Height) { yPos += _rowHeight; return; }

            g.DrawString(name, _font, nameBrush, col1, yPos);
            g.DrawString($"{value:X4}", _font, valueBrush, col2, yPos);
            yPos += _rowHeight;
        }

        private void DrawFlags(Graphics g, int col1, ref int yPos, Brush onBrush, Brush offBrush)
        {
            // Draw flags in a compact format
            string[] flagNames = { "CF", "PF", "AF", "ZF", "SF", "TF", "IF", "DF", "OF" };
            bool[] flagValues = { _cf, _pf, _af, _zf, _sf, _tf, _if, _df, _of };

            int xPos = col1;
            for (int i = 0; i < flagNames.Length; i++)
            {
                var brush = flagValues[i] ? onBrush : offBrush;
                string text = flagValues[i] ? $"{flagNames[i]}=1" : $"{flagNames[i]}=0";
                g.DrawString(text, _font, brush, xPos, yPos);
                xPos += 45;

                if ((i + 1) % 3 == 0)
                {
                    xPos = col1;
                    yPos += _rowHeight;
                }
            }

            if (flagNames.Length % 3 != 0)
                yPos += _rowHeight;
        }

        public event EventHandler<IntPtr>? AddressSelected;

        private void InitializeContextMenu()
        {
            var menu = new ContextMenuStrip();
            var copyItem = new ToolStripMenuItem("Copy Value");
            copyItem.Click += (s, e) =>
            {
                var reg = GetRegisterAtPoint(_lastMouseLocation);
                if (reg != null)
                {
                    Clipboard.SetText($"{reg.Value:X}");
                }
            };
            menu.Items.Add(copyItem);

            var editItem = new ToolStripMenuItem("Edit Value...");
            editItem.Click += (s, e) =>
            {
                var reg = GetRegisterAtPoint(_lastMouseLocation);
                if (reg != null)
                {
                    ShowEditRegisterDialog(reg.Value.Name, reg.Value.Value);
                }
            };
            menu.Items.Add(editItem);

            var editFlagsItem = new ToolStripMenuItem("Edit Flags...");
            editFlagsItem.Click += (s, e) => ShowEditFlagsDialog();
            menu.Items.Add(editFlagsItem);

            this.ContextMenuStrip = menu;
        }

        private void ShowEditFlagsDialog()
        {
            using var dialog = new Form
            {
                Text = "Edit Flags",
                Size = new Size(280, 280),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            // Flag checkboxes
            var chkCF = new CheckBox { Text = "CF (Carry)", Location = new Point(20, 20), Checked = _cf, AutoSize = true };
            var chkPF = new CheckBox { Text = "PF (Parity)", Location = new Point(20, 45), Checked = _pf, AutoSize = true };
            var chkAF = new CheckBox { Text = "AF (Adjust)", Location = new Point(20, 70), Checked = _af, AutoSize = true };
            var chkZF = new CheckBox { Text = "ZF (Zero)", Location = new Point(20, 95), Checked = _zf, AutoSize = true };
            var chkSF = new CheckBox { Text = "SF (Sign)", Location = new Point(20, 120), Checked = _sf, AutoSize = true };
            var chkTF = new CheckBox { Text = "TF (Trap)", Location = new Point(140, 20), Checked = _tf, AutoSize = true };
            var chkIF = new CheckBox { Text = "IF (Interrupt)", Location = new Point(140, 45), Checked = _if, AutoSize = true };
            var chkDF = new CheckBox { Text = "DF (Direction)", Location = new Point(140, 70), Checked = _df, AutoSize = true };
            var chkOF = new CheckBox { Text = "OF (Overflow)", Location = new Point(140, 95), Checked = _of, AutoSize = true };

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(90, 200), Size = new Size(75, 25) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(175, 200), Size = new Size(75, 25) };

            dialog.Controls.AddRange(new Control[] { chkCF, chkPF, chkAF, chkZF, chkSF, chkTF, chkIF, chkDF, chkOF, btnOk, btnCancel });
            dialog.AcceptButton = btnOk;
            ThemeManager.ApplyTheme(dialog);

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // Build new flags value
                ulong newFlags = _rflags;
                newFlags = SetFlag(newFlags, 0, chkCF.Checked);   // CF
                newFlags = SetFlag(newFlags, 2, chkPF.Checked);   // PF
                newFlags = SetFlag(newFlags, 4, chkAF.Checked);   // AF
                newFlags = SetFlag(newFlags, 6, chkZF.Checked);   // ZF
                newFlags = SetFlag(newFlags, 7, chkSF.Checked);   // SF
                newFlags = SetFlag(newFlags, 8, chkTF.Checked);   // TF
                newFlags = SetFlag(newFlags, 9, chkIF.Checked);   // IF
                newFlags = SetFlag(newFlags, 10, chkDF.Checked);  // DF
                newFlags = SetFlag(newFlags, 11, chkOF.Checked);  // OF

                WriteFlags(newFlags);
            }
        }

        private ulong SetFlag(ulong flags, int bit, bool value)
        {
            if (value)
                return flags | (1UL << bit);
            else
                return flags & ~(1UL << bit);
        }

        private void WriteFlags(ulong newFlags)
        {
            if (_process == null || _selectedThreadId == 0) return;

            IntPtr hThread = Core.ProcessAccess.OpenThread(Core.ProcessAccess.THREAD_ALL_ACCESS, false, _selectedThreadId);
            if (hThread == IntPtr.Zero) return;

            try
            {
                if (_is64Bit)
                {
                    var context = new Core.CONTEXT64 { ContextFlags = Core.ProcessAccess.CONTEXT_ALL };
                    if (Core.ProcessAccess.GetThreadContext(hThread, ref context))
                    {
                        context.EFlags = (uint)newFlags;
                        Core.ProcessAccess.SetThreadContext(hThread, ref context);
                    }
                }
                else
                {
                    var context = new Core.CONTEXT32 { ContextFlags = Core.ProcessAccess.CONTEXT_ALL };
                    if (Core.ProcessAccess.Wow64GetThreadContext(hThread, ref context))
                    {
                        context.EFlags = (uint)newFlags;
                        Core.ProcessAccess.Wow64SetThreadContext(hThread, ref context);
                    }
                }
                RefreshFromThread(_selectedThreadId);
            }
            finally { Core.ProcessAccess.CloseHandle(hThread); }
        }

        private void ShowEditRegisterDialog(string name, ulong currentValue)
        {
            using var dialog = new Form
            {
                Text = $"Change {name}",
                Size = new Size(300, 130),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label { Text = $"New value for {name}:", Location = new Point(10, 15), AutoSize = true };
            var txt = new TextBox { Text = $"{currentValue:X}", Location = new Point(10, 38), Size = new Size(260, 23) };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(115, 70) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(195, 70) };

            dialog.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
            dialog.AcceptButton = btnOk;
            ThemeManager.ApplyTheme(dialog);

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (ulong.TryParse(txt.Text, System.Globalization.NumberStyles.HexNumber, null, out ulong newValue))
                {
                    WriteRegisterValue(name, newValue);
                }
            }
        }

        private void WriteRegisterValue(string name, ulong value)
        {
            if (_process == null || _selectedThreadId == 0) return;

            IntPtr hThread = Core.ProcessAccess.OpenThread(Core.ProcessAccess.THREAD_ALL_ACCESS, false, _selectedThreadId);
            if (hThread == IntPtr.Zero) return;

            try
            {
                if (_is64Bit)
                {
                    var context = new Core.CONTEXT64 { ContextFlags = Core.ProcessAccess.CONTEXT_ALL };
                    if (Core.ProcessAccess.GetThreadContext(hThread, ref context))
                    {
                        switch (name)
                        {
                            case "RAX": context.Rax = value; break;
                            case "RBX": context.Rbx = value; break;
                            case "RCX": context.Rcx = value; break;
                            case "RDX": context.Rdx = value; break;
                            case "RSI": context.Rsi = value; break;
                            case "RDI": context.Rdi = value; break;
                            case "RBP": context.Rbp = value; break;
                            case "RSP": context.Rsp = value; break;
                            case "R8": context.R8 = value; break;
                            case "R9": context.R9 = value; break;
                            case "R10": context.R10 = value; break;
                            case "R11": context.R11 = value; break;
                            case "R12": context.R12 = value; break;
                            case "R13": context.R13 = value; break;
                            case "R14": context.R14 = value; break;
                            case "R15": context.R15 = value; break;
                            case "RIP": context.Rip = value; break;
                        }
                        Core.ProcessAccess.SetThreadContext(hThread, ref context);
                    }
                }
                else
                {
                    var context = new Core.CONTEXT32 { ContextFlags = Core.ProcessAccess.CONTEXT_ALL };
                    if (Core.ProcessAccess.Wow64GetThreadContext(hThread, ref context))
                    {
                        uint val32 = (uint)value;
                        switch (name)
                        {
                            case "EAX": context.Eax = val32; break;
                            case "EBX": context.Ebx = val32; break;
                            case "ECX": context.Ecx = val32; break;
                            case "EDX": context.Edx = val32; break;
                            case "ESI": context.Esi = val32; break;
                            case "EDI": context.Edi = val32; break;
                            case "EBP": context.Ebp = val32; break;
                            case "ESP": context.Esp = val32; break;
                            case "EIP": context.Eip = val32; break;
                        }
                        Core.ProcessAccess.Wow64SetThreadContext(hThread, ref context);
                    }
                }
                RefreshFromThread(_selectedThreadId);
            }
            finally { Core.ProcessAccess.CloseHandle(hThread); }
        }

        private Point _lastMouseLocation;
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _lastMouseLocation = e.Location;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            
            var reg = GetRegisterAtPoint(e.Location);
            if (reg.HasValue)
            {
                AddressSelected?.Invoke(this, (IntPtr)reg.Value.Value);
            }
        }

        private (string Name, ulong Value)? GetRegisterAtPoint(Point pt)
        {
            int row = (pt.Y - 5 + (_vScrollBar.Value * _rowHeight)) / _rowHeight;
            
            // This needs to match the drawing order in OnPaint
            // Let's simplify and just map indices to names/values
            // (In a real app, we'd store the hit regions during OnPaint)
            
            int currentRow = 0;
            
            // General Purpose header
            currentRow++; 
            
            if (row == currentRow++) return ("RAX", _rax);
            if (row == currentRow++) return ("RBX", _rbx);
            if (row == currentRow++) return ("RCX", _rcx);
            if (row == currentRow++) return ("RDX", _rdx);
            if (row == currentRow++) return ("RSI", _rsi);
            if (row == currentRow++) return ("RDI", _rdi);
            if (row == currentRow++) return ("RBP", _rbp);
            if (row == currentRow++) return ("RSP", _rsp);
            
            if (_is64Bit)
            {
                if (row == currentRow++) return ("R8", _r8);
                if (row == currentRow++) return ("R9", _r9);
                if (row == currentRow++) return ("R10", _r10);
                if (row == currentRow++) return ("R11", _r11);
                if (row == currentRow++) return ("R12", _r12);
                if (row == currentRow++) return ("R13", _r13);
                if (row == currentRow++) return ("R14", _r14);
                if (row == currentRow++) return ("R15", _r15);
            }

            currentRow++; // Gap
            currentRow++; // IP Header
            if (row == currentRow++) return (_is64Bit ? "RIP" : "EIP", _rip);

            currentRow++; // Gap
            currentRow++; // Flags Header
            if (row == currentRow++) return ("RFLAGS", _rflags);

            return null;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            int delta = e.Delta / 120;
            int newValue = _vScrollBar.Value - (delta * 2);
            newValue = Math.Max(_vScrollBar.Minimum, Math.Min(_vScrollBar.Maximum - _vScrollBar.LargeChange + 1, newValue));
            _vScrollBar.Value = newValue;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font?.Dispose();
                _fontBold?.Dispose();
                _vScrollBar?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
