using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CrxMem.Core;
using Iced.Intel;

namespace CrxMem.MemoryView
{
    public class DisassemblerViewControl : UserControl
    {
        #region P/Invoke for Memory Protection
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flNewProtect,
            out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            UIntPtr dwSize);

        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        #endregion

        private const int MaxInstructionBytes = 15; // Max x86/x64 instruction length
        private const int InstructionsPerPage = 100;

        private ProcessAccess? _process;
        private IntPtr _baseAddress;
        private bool _is64Bit;
        private VScrollBar _vScrollBar;

        private Font _font;
        private int _charWidth;
        private int _charHeight;
        private int _rowHeight;

        private int _flowMarginWidth;  // Left margin for flow lines
        private int _addressColumnWidth;
        private int _opcodeColumnWidth;
        private int _instructionColumnWidth;
        private int _commentColumnWidth;

        // Flow line data for drawing jump/call arrows
        private List<FlowLine> _flowLines = new List<FlowLine>();
        private const int MaxFlowLineDepth = 6; // Maximum nested flow lines

        private List<DisassembledInstruction> _instructions;
        private int _scrollPosition;
        private HashSet<int> _selectedIndices = new HashSet<int>();
        private int _selectionAnchor = -1;  // For Shift+Click range selection
        private bool _isDragging = false;   // For drag selection
        private int _dragStartIndex = -1;   // Starting row of drag selection

        // Helper property for backwards compatibility - returns primary selected index
        private int _selectedIndex => _selectedIndices.Count > 0 ? _selectedIndices.Min() : -1;

        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem? _menuRemoveBookmark;
        private NasmFormatter _formatter;
        private ColoredFormatterOutput _coloredOutput;
        private NOPManager? _nopManager;

        // Cache for resolved address strings (populated during disassembly, not paint)
        private Dictionary<IntPtr, string> _addressCache = new Dictionary<IntPtr, string>();
        private Dictionary<IntPtr, string> _commentCache = new Dictionary<IntPtr, string>();

        // Pre-cached brushes for syntax highlighting (avoid GDI allocation in paint loop)
        private Dictionary<Color, SolidBrush> _brushCache = new Dictionary<Color, SolidBrush>();

        public DisassemblerViewControl(ProcessAccess? process, bool is64Bit)
        {
            _process = process;
            _is64Bit = is64Bit;
            _baseAddress = IntPtr.Zero;
            _instructions = new List<DisassembledInstruction>();
            _scrollPosition = 0;

            _font = new Font("Consolas", 9F);
            _formatter = new NasmFormatter();
            _coloredOutput = new ColoredFormatterOutput();
            _nopManager = process != null ? new NOPManager(process) : null;

            // Create scrollbar
            _vScrollBar = new VScrollBar
            {
                Dock = DockStyle.Right,
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 10
            };
            _vScrollBar.Scroll += VScrollBar_Scroll;
            Controls.Add(_vScrollBar);

            // Create context menu
            _contextMenu = new ContextMenuStrip();
            var menuGoto = new ToolStripMenuItem("Go to address (Ctrl+G)");
            menuGoto.Click += MenuGoto_Click;
            var menuFollow = new ToolStripMenuItem("Follow (Enter)");
            menuFollow.Click += MenuFollow_Click;

            // Copy submenu with multiple format options
            var menuCopyParent = new ToolStripMenuItem("Copy");
            var menuCopyAddress = new ToolStripMenuItem("Address(es) (Ctrl+C)");
            menuCopyAddress.Click += MenuCopyAddress_Click;
            var menuCopyBytes = new ToolStripMenuItem("Bytes (hex) (Ctrl+Shift+C)");
            menuCopyBytes.Click += MenuCopyBytes_Click;
            var menuCopyBinary = new ToolStripMenuItem("Bytes (binary)");
            menuCopyBinary.Click += MenuCopyBinary_Click;
            var menuCopyInstructions = new ToolStripMenuItem("Instructions only");
            menuCopyInstructions.Click += MenuCopyInstructions_Click;
            var menuCopyFullLines = new ToolStripMenuItem("Full lines (address + bytes + instruction)");
            menuCopyFullLines.Click += MenuCopyFullLines_Click;
            var menuCopyDisassembly = new ToolStripMenuItem("Disassembly (address + instruction)");
            menuCopyDisassembly.Click += MenuCopyDisassembly_Click;
            menuCopyParent.DropDownItems.Add(menuCopyAddress);
            menuCopyParent.DropDownItems.Add(menuCopyBytes);
            menuCopyParent.DropDownItems.Add(menuCopyBinary);
            menuCopyParent.DropDownItems.Add(new ToolStripSeparator());
            menuCopyParent.DropDownItems.Add(menuCopyInstructions);
            menuCopyParent.DropDownItems.Add(menuCopyDisassembly);
            menuCopyParent.DropDownItems.Add(menuCopyFullLines);

            _contextMenu.Items.Add(menuGoto);
            _contextMenu.Items.Add(menuFollow);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(menuCopyParent);
            _contextMenu.Items.Add(new ToolStripSeparator());

            // NOP menu item
            var menuNop = new ToolStripMenuItem("Replace with NOPs");
            menuNop.Click += MenuNop_Click;
            _contextMenu.Items.Add(menuNop);

            // Restore (Un-NOP) menu item
            var menuRestore = new ToolStripMenuItem("Restore original bytes");
            menuRestore.Click += MenuRestore_Click;
            _contextMenu.Items.Add(menuRestore);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Comment menu item
            var menuComment = new ToolStripMenuItem("Add/Edit Comment (;)");
            menuComment.Click += MenuComment_Click;
            _contextMenu.Items.Add(menuComment);

            // Bookmark menu items
            var menuBookmark = new ToolStripMenuItem("Bookmark Address (Ctrl+D)");
            menuBookmark.Click += MenuBookmark_Click;
            _contextMenu.Items.Add(menuBookmark);

            _menuRemoveBookmark = new ToolStripMenuItem("Remove Bookmark");
            _menuRemoveBookmark.Click += MenuRemoveBookmark_Click;
            _contextMenu.Items.Add(_menuRemoveBookmark);

            // Handle context menu opening to enable/disable items dynamically
            _contextMenu.Opening += ContextMenu_Opening;

            ContextMenuStrip = _contextMenu;

            // Subscribe to manager changes
            BookmarkManager.BookmarksChanged += OnBookmarksChanged;
            CommentManager.CommentsChanged += OnCommentsChanged;

            // Set control properties
            DoubleBuffered = true;
            // Make control selectable to receive keyboard input for hotkeys (Ctrl+G, Ctrl+D, etc.)
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            ApplyTheme();

            // Calculate dimensions
            using (Graphics g = CreateGraphics())
            {
                SizeF charSize = g.MeasureString("0", _font);
                _charWidth = (int)Math.Ceiling(charSize.Width);
                _charHeight = (int)Math.Ceiling(charSize.Height);
                _rowHeight = _charHeight + 4;
            }

            _flowMarginWidth = 90; // Left margin for flow lines (arrows)
            _addressColumnWidth = _charWidth * 25; // "ModuleName+Offset" (e.g., "Gunz.exe+4B91058")
            _opcodeColumnWidth = _charWidth * 24; // Opcode bytes (up to 15 bytes = ~24 chars with spaces)
            _instructionColumnWidth = _charWidth * 35; // Instruction text
            _commentColumnWidth = _charWidth * 30; // Comment (jump targets, module+offset)

            MouseDown += DisassemblerView_MouseDown;
            MouseMove += DisassemblerView_MouseMove;
            MouseUp += DisassemblerView_MouseUp;
            MouseDoubleClick += DisassemblerView_MouseDoubleClick;
        }

        private void VScrollBar_Scroll(object? sender, ScrollEventArgs e)
        {
            _scrollPosition = e.NewValue;
            Invalidate();
        }

        /// <summary>
        /// Apply the current theme colors
        /// </summary>
        public void ApplyTheme()
        {
            BackColor = ThemeManager.Background;
            ForeColor = ThemeManager.Foreground;
            _vScrollBar.BackColor = ThemeManager.BackgroundAlt;

            // Clear brush cache when theme changes
            foreach (var brush in _brushCache.Values)
            {
                brush?.Dispose();
            }
            _brushCache.Clear();

            // Update ColoredFormatterOutput with new theme colors
            _coloredOutput = new ColoredFormatterOutput();

            Invalidate();
        }

        public void SetAddress(IntPtr address)
        {
            _baseAddress = address;
            _scrollPosition = 0;
            _selectedIndices.Clear();
            _selectionAnchor = -1;
            _vScrollBar.Value = 0;

            // Clear caches when address changes
            _addressCache.Clear();
            _commentCache.Clear();

            DisassembleFromAddress(address);
            CalculateFlowLines();
            Invalidate();
        }

        /// <summary>
        /// Refreshes the disassembly view at the current scroll position without jumping back to base address.
        /// Useful after modifying memory (e.g., NOPing an instruction).
        /// </summary>
        public void RefreshCurrentView()
        {
            if (_baseAddress == IntPtr.Zero || _process == null)
                return;

            // Remember current scroll position and selection
            int savedScrollPosition = _scrollPosition;
            var savedSelectedIndices = new HashSet<int>(_selectedIndices);

            // Re-disassemble from the same base address
            DisassembleFromAddress(_baseAddress);
            CalculateFlowLines();

            // Restore scroll position (clamped to valid range)
            int maxScroll = Math.Max(0, _instructions.Count - (Height / _rowHeight));
            _scrollPosition = Math.Min(savedScrollPosition, maxScroll);
            _vScrollBar.Value = _scrollPosition;

            // Try to restore selection if still valid
            _selectedIndices.Clear();
            foreach (var idx in savedSelectedIndices)
            {
                if (idx < _instructions.Count)
                    _selectedIndices.Add(idx);
            }

            Invalidate();
        }

        private void DisassembleFromAddress(IntPtr address)
        {
            _instructions.Clear();

            if (_process == null)
                return;

            try
            {
                // Read memory BEFORE the address (to show context like Cheat Engine)
                // We read 4KB before and 12KB after for a total of 16KB (reduced for data regions)
                int beforeSize = 4096; // 4KB before
                int afterSize = 12288; // 12KB after
                int totalSize = beforeSize + afterSize;

                // Calculate the actual start address (go back before the target)
                long startAddr = Math.Max(0, address.ToInt64() - beforeSize);

                byte[]? code = _process.Read(new IntPtr(startAddr), totalSize);

                // If we couldn't read from the adjusted start address, try the exact address
                if (code == null || code.Length == 0)
                {
                    code = _process.Read(address, afterSize);
                    startAddr = address.ToInt64();
                }

                // Still couldn't read? Try reading just a small amount at the exact address
                if (code == null || code.Length == 0)
                {
                    code = _process.Read(address, 256);
                    startAddr = address.ToInt64();
                }

                if (code == null || code.Length == 0)
                    return;

                // Create decoder starting from the beginning of our buffer
                int bitness = _is64Bit ? 64 : 32;
                var decoder = Decoder.Create(bitness, code);
                decoder.IP = (ulong)startAddr;

                // Decode all instructions in the buffer
                int instructionCount = 0;
                int targetInstructionIndex = -1;
                int consecutiveInvalid = 0;

                while (decoder.IP < (ulong)(startAddr + code.Length) &&
                       instructionCount < InstructionsPerPage * 50) // Cache 50 pages worth (~5,000 instructions)
                {
                    ulong currentIP = decoder.IP;
                    int bufferOffset = (int)(currentIP - (ulong)startAddr);

                    decoder.Decode(out var instruction);

                    if (instruction.IsInvalid)
                    {
                        consecutiveInvalid++;

                        // For invalid bytes, create a "db XX" entry (like Cheat Engine does for data)
                        if (bufferOffset < code.Length)
                        {
                            var disasm = new DisassembledInstruction
                            {
                                Address = (IntPtr)currentIP,
                                Length = 1,
                                Bytes = new byte[] { code[bufferOffset] },
                                Instruction = default, // Invalid instruction marker
                                IsDataByte = true // Mark as raw data byte
                            };

                            _instructions.Add(disasm);

                            // Track target address
                            if ((long)currentIP == address.ToInt64())
                            {
                                targetInstructionIndex = instructionCount;
                            }

                            instructionCount++;
                        }

                        decoder.IP = currentIP + 1;
                        continue;
                    }

                    consecutiveInvalid = 0;

                    var validDisasm = new DisassembledInstruction
                    {
                        Address = (IntPtr)instruction.IP,
                        Length = instruction.Length,
                        Bytes = new byte[instruction.Length],
                        Instruction = instruction,
                        IsDataByte = false
                    };

                    // Copy instruction bytes
                    int offset = (int)(instruction.IP - (ulong)startAddr);
                    if (offset >= 0 && offset + instruction.Length <= code.Length)
                    {
                        Array.Copy(code, offset, validDisasm.Bytes, 0, instruction.Length);
                    }

                    _instructions.Add(validDisasm);

                    // Track where our target address is (find closest instruction at or before target)
                    if ((long)instruction.IP <= address.ToInt64() &&
                        (long)instruction.IP + instruction.Length > address.ToInt64())
                    {
                        targetInstructionIndex = instructionCount;
                    }
                    // Also check for exact match
                    if ((long)instruction.IP == address.ToInt64())
                    {
                        targetInstructionIndex = instructionCount;
                    }

                    instructionCount++;
                }

                // Scroll to center the target address in the view
                if (targetInstructionIndex >= 0)
                {
                    int visibleRows = Math.Max(1, Height / _rowHeight);
                    _scrollPosition = Math.Max(0, targetInstructionIndex - (visibleRows / 2));
                    _selectedIndices.Clear();
                    _selectedIndices.Add(targetInstructionIndex);
                    _selectionAnchor = targetInstructionIndex;
                }
                else if (_instructions.Count > 0)
                {
                    // If we couldn't find target, scroll to show the beginning and select first instruction
                    _scrollPosition = 0;
                    _selectedIndices.Clear();
                    _selectedIndices.Add(0);
                    _selectionAnchor = 0;
                }
                else
                {
                    _scrollPosition = 0;
                    _selectedIndices.Clear();
                    _selectionAnchor = -1;
                }
            }
            catch
            {
                // Failed to disassemble
                _instructions.Clear();
            }
        }

        /// <summary>
        /// Calculate flow lines for visible jumps and calls
        /// </summary>
        private void CalculateFlowLines()
        {
            _flowLines.Clear();

            if (_instructions.Count == 0)
                return;

            // Build address-to-index lookup for quick target resolution
            var addressToIndex = new Dictionary<long, int>();
            for (int i = 0; i < _instructions.Count; i++)
            {
                long addr = _instructions[i].Address.ToInt64();
                if (!addressToIndex.ContainsKey(addr))
                    addressToIndex[addr] = i;
            }

            // Track which columns (depths) are in use at each row
            var depthUsage = new int[MaxFlowLineDepth];

            // Find all branch instructions and their targets
            for (int i = 0; i < _instructions.Count; i++)
            {
                var disasm = _instructions[i];
                if (disasm.IsDataByte)
                    continue;

                var instruction = disasm.Instruction;
                var flowControl = instruction.FlowControl;

                // Only process jumps and calls
                if (flowControl != FlowControl.UnconditionalBranch &&
                    flowControl != FlowControl.ConditionalBranch &&
                    flowControl != FlowControl.Call)
                    continue;

                // Get target address
                long targetAddr = 0;
                if (instruction.NearBranchTarget != 0)
                {
                    targetAddr = (long)instruction.NearBranchTarget;
                }
                else if (instruction.IsIPRelativeMemoryOperand)
                {
                    targetAddr = (long)instruction.IPRelativeMemoryAddress;
                }

                if (targetAddr == 0)
                    continue;

                // Check if target is within our instruction list
                if (!addressToIndex.TryGetValue(targetAddr, out int targetIndex))
                    continue;

                // Find an available depth for this flow line
                int minRow = Math.Min(i, targetIndex);
                int maxRow = Math.Max(i, targetIndex);
                int depth = 0;

                for (int d = 0; d < MaxFlowLineDepth; d++)
                {
                    bool available = true;
                    // Check if this depth is free for the entire range
                    if (depthUsage[d] > minRow && depthUsage[d] < maxRow)
                    {
                        available = false;
                    }
                    if (available)
                    {
                        depth = d;
                        depthUsage[d] = maxRow;
                        break;
                    }
                }

                // Determine color based on type
                Color lineColor;
                if (flowControl == FlowControl.Call)
                    lineColor = ThemeManager.SyntaxCall; // Green for calls
                else if (flowControl == FlowControl.ConditionalBranch)
                    lineColor = ThemeManager.SyntaxConditional; // Cyan for conditional jumps
                else
                    lineColor = ThemeManager.SyntaxJump; // Blue for unconditional jumps

                _flowLines.Add(new FlowLine
                {
                    SourceIndex = i,
                    TargetIndex = targetIndex,
                    Depth = depth,
                    IsCall = flowControl == FlowControl.Call,
                    IsConditional = flowControl == FlowControl.ConditionalBranch,
                    Color = lineColor
                });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (_process == null)
            {
                DrawCenteredText(g, "No process attached", SystemBrushes.ControlText);
                return;
            }

            if (!_process.IsOpen)
            {
                DrawCenteredText(g, "Process handle not open", Brushes.Red);
                return;
            }

            if (_baseAddress == IntPtr.Zero)
            {
                DrawCenteredText(g, "Navigate to an address using the toolbar", Brushes.Gray);
                return;
            }

            if (_instructions.Count == 0)
            {
                DrawCenteredText(g, $"Unable to read/disassemble memory at {_baseAddress.ToInt64():X8}\n(Try a different address, like 0x00400000)", Brushes.Orange);
                return;
            }

            int yPos = 5;
            int visibleRows = Height / _rowHeight;

            // Draw flow lines first (so they appear behind the text)
            DrawFlowLines(g, _scrollPosition, visibleRows);

            // Draw visible instructions
            for (int i = _scrollPosition; i < _instructions.Count && i < _scrollPosition + visibleRows; i++)
            {
                DrawInstruction(g, _instructions[i], yPos, _selectedIndices.Contains(i));
                yPos += _rowHeight;
            }

            // Update scrollbar
            _vScrollBar.Maximum = Math.Max(0, _instructions.Count - visibleRows);
        }

        /// <summary>
        /// Draw flow lines (arrows) for jumps and calls in the left margin
        /// </summary>
        private void DrawFlowLines(Graphics g, int scrollPosition, int visibleRows)
        {
            if (_flowLines.Count == 0)
                return;

            int endRow = scrollPosition + visibleRows;

            // Draw a subtle separator line between flow margin and addresses
            using var separatorPen = new Pen(ThemeManager.BackgroundAlt, 1);
            g.DrawLine(separatorPen, _flowMarginWidth - 2, 0, _flowMarginWidth - 2, Height);

            foreach (var flow in _flowLines)
            {
                // Skip if flow line is completely outside visible range
                int minRow = Math.Min(flow.SourceIndex, flow.TargetIndex);
                int maxRow = Math.Max(flow.SourceIndex, flow.TargetIndex);

                if (maxRow < scrollPosition || minRow > endRow)
                    continue;

                // Calculate Y positions
                int sourceY = 5 + (flow.SourceIndex - scrollPosition) * _rowHeight + _rowHeight / 2;
                int targetY = 5 + (flow.TargetIndex - scrollPosition) * _rowHeight + _rowHeight / 2;

                // Calculate X position based on depth
                int xOffset = _flowMarginWidth - 8 - (flow.Depth * 8);
                xOffset = Math.Max(4, xOffset); // Minimum margin

                using var pen = new Pen(flow.Color, 1.5f);

                // Dashed line for conditional jumps
                if (flow.IsConditional)
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                }

                bool jumpingDown = flow.TargetIndex > flow.SourceIndex;

                // Draw the flow line:
                // - Horizontal line from instruction to margin
                // - Vertical line in margin
                // - Horizontal line to target + arrow

                // Source horizontal line (from margin edge towards center)
                g.DrawLine(pen, _flowMarginWidth - 4, sourceY, xOffset, sourceY);

                // Vertical line
                g.DrawLine(pen, xOffset, sourceY, xOffset, targetY);

                // Target horizontal line with arrow
                g.DrawLine(pen, xOffset, targetY, _flowMarginWidth - 4, targetY);

                // Draw arrow head pointing to the target instruction
                int arrowSize = 4;
                Point arrowTip = new Point(_flowMarginWidth - 4, targetY);
                Point[] arrowHead;

                // Arrow always points right (towards the instruction)
                arrowHead = new Point[]
                {
                    arrowTip,
                    new Point(arrowTip.X - arrowSize, arrowTip.Y - arrowSize),
                    new Point(arrowTip.X - arrowSize, arrowTip.Y + arrowSize)
                };

                using var brush = new SolidBrush(flow.Color);
                g.FillPolygon(brush, arrowHead);
            }
        }

        private void DrawInstruction(Graphics g, DisassembledInstruction disasm, int yPos, bool selected)
        {
            int xPos = _flowMarginWidth; // Start after the flow margin
            long addr = disasm.Address.ToInt64();

            // Draw bookmark indicator in the flow margin (star icon)
            if (BookmarkManager.Exists(addr))
            {
                // Draw a small star/bookmark indicator
                using var bookmarkBrush = new SolidBrush(Color.Gold);
                int starX = 4;
                int starY = yPos + (_rowHeight / 2) - 5;
                // Simple star shape
                g.FillPolygon(bookmarkBrush, new Point[] {
                    new Point(starX + 5, starY),
                    new Point(starX + 6, starY + 4),
                    new Point(starX + 10, starY + 4),
                    new Point(starX + 7, starY + 6),
                    new Point(starX + 8, starY + 10),
                    new Point(starX + 5, starY + 8),
                    new Point(starX + 2, starY + 10),
                    new Point(starX + 3, starY + 6),
                    new Point(starX, starY + 4),
                    new Point(starX + 4, starY + 4)
                });
            }

            // Background for selected row (theme-aware)
            if (selected)
            {
                using var selBrush = new SolidBrush(ThemeManager.SelectionHighlight);
                g.FillRectangle(selBrush, _flowMarginWidth, yPos - 2, Width - _vScrollBar.Width - _flowMarginWidth, _rowHeight);
            }

            // Draw address (Module+Offset format like Cheat Engine) - uses cache
            string addrStr = ResolveAddress((IntPtr)disasm.Address);
            var addrBrush = GetCachedBrush(ThemeManager.SyntaxAddress);
            g.DrawString(addrStr, _font, addrBrush, xPos, yPos);
            xPos += _addressColumnWidth;

            // Draw opcode bytes (hex representation of instruction bytes)
            string opcodeBytes = BitConverter.ToString(disasm.Bytes).Replace("-", " ");
            var opcodeBrush = GetCachedBrush(ThemeManager.ForegroundDim);
            g.DrawString(opcodeBytes, _font, opcodeBrush, xPos, yPos);
            xPos += _opcodeColumnWidth;

            // Check if this is a raw data byte or a valid instruction
            if (disasm.IsDataByte)
            {
                // Draw as "db XX" for data bytes (like Cheat Engine does)
                string dbText = $"db {disasm.Bytes[0]:X2}";
                var dbBrush = GetCachedBrush(ThemeManager.ForegroundDim);
                g.DrawString(dbText, _font, dbBrush, xPos, yPos);
            }
            else
            {
                // Draw instruction with colored syntax highlighting
                _coloredOutput.Clear();
                _formatter.Format(disasm.Instruction, _coloredOutput);

                float instructionXPos = xPos;
                foreach (var part in _coloredOutput.GetParts())
                {
                    // Use cached brush instead of creating new one each time
                    var brush = GetCachedBrush(part.Color);
                    g.DrawString(part.Text, _font, brush, instructionXPos, yPos);
                    // Use fixed-width character approximation instead of MeasureString
                    instructionXPos += part.Text.Length * _charWidth * 0.6f;
                }
                xPos += _instructionColumnWidth;

                // Draw comment - user comment takes priority over auto-generated comment
                string? userComment = CommentManager.Get(addr);
                string comment;
                if (!string.IsNullOrEmpty(userComment))
                {
                    comment = "; " + userComment;
                    // User comments shown in a different color (cyan/teal)
                    var userCommentBrush = GetCachedBrush(Color.FromArgb(78, 201, 176));
                    g.DrawString(comment, _font, userCommentBrush, xPos, yPos);
                }
                else
                {
                    comment = GetInstructionComment(disasm.Instruction);
                    if (!string.IsNullOrEmpty(comment))
                    {
                        var commentBrush = GetCachedBrush(ThemeManager.SyntaxComment);
                        g.DrawString(comment, _font, commentBrush, xPos, yPos);
                    }
                }
            }
        }

        /// <summary>
        /// Get or create a cached brush for the given color
        /// </summary>
        private SolidBrush GetCachedBrush(Color color)
        {
            if (!_brushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidBrush(color);
                _brushCache[color] = brush;
            }
            return brush;
        }

        private Brush GetInstructionBrush(Instruction instruction)
        {
            // Color code by instruction type
            var flowControl = instruction.FlowControl;

            return flowControl switch
            {
                FlowControl.UnconditionalBranch => Brushes.Blue,      // jmp
                FlowControl.ConditionalBranch => Brushes.DarkCyan,    // jz, jnz, etc.
                FlowControl.Call => Brushes.Green,                     // call
                FlowControl.Return => Brushes.DarkGreen,               // ret
                FlowControl.Interrupt => Brushes.Red,                  // int
                _ => Brushes.Black
            };
        }

        /// <summary>
        /// Get comment for instruction (jump targets, module+offset)
        /// </summary>
        private string GetInstructionComment(Instruction instruction)
        {
            try
            {
                // For branches (jmp, jz, jne, etc.)
                if (instruction.FlowControl == FlowControl.UnconditionalBranch ||
                    instruction.FlowControl == FlowControl.ConditionalBranch)
                {
                    if (instruction.NearBranchTarget != 0)
                    {
                        IntPtr targetAddress = new IntPtr((long)instruction.NearBranchTarget);
                        return ResolveAddress(targetAddress);
                    }
                }

                // For calls
                if (instruction.FlowControl == FlowControl.Call)
                {
                    if (instruction.NearBranchTarget != 0)
                    {
                        IntPtr targetAddress = new IntPtr((long)instruction.NearBranchTarget);
                        return ResolveAddress(targetAddress);
                    }
                }

                // For memory references with displacement
                if (instruction.IsIPRelativeMemoryOperand)
                {
                    IntPtr targetAddress = new IntPtr((long)instruction.IPRelativeMemoryAddress);
                    return $"[{ResolveAddress(targetAddress)}]";
                }
            }
            catch
            {
                // Ignore errors in comment generation
            }

            return "";
        }

        /// <summary>
        /// Resolve address to Module+Offset format (like Cheat Engine) - CACHED for performance
        /// </summary>
        private string ResolveAddress(IntPtr address)
        {
            // Check cache first
            if (_addressCache.TryGetValue(address, out string? cached))
                return cached;

            string result;

            if (_process == null)
            {
                result = $"{address.ToInt64():X}";
            }
            else
            {
                try
                {
                    var module = _process.GetModuleForAddress(address);
                    if (module != null)
                    {
                        long offset = address.ToInt64() - module.BaseAddress.ToInt64();
                        result = $"{module.ModuleName}+{offset:X}";
                    }
                    else
                    {
                        result = $"{address.ToInt64():X}";
                    }
                }
                catch
                {
                    // If module resolution fails, fall back to raw address
                    result = $"{address.ToInt64():X}";
                }
            }

            // Cache the result
            _addressCache[address] = result;
            return result;
        }

        private void DisassemblerView_MouseDown(object? sender, MouseEventArgs e)
        {
            // Take focus when clicked so keyboard shortcuts work
            if (!Focused) Focus();

            if (e.Button != MouseButtons.Left) return;

            int rowIndex = (e.Y / _rowHeight) + _scrollPosition;
            if (rowIndex < 0 || rowIndex >= _instructions.Count)
                return;

            if (Control.ModifierKeys.HasFlag(Keys.Control))
            {
                // Ctrl+Click: Toggle selection of this row
                if (_selectedIndices.Contains(rowIndex))
                    _selectedIndices.Remove(rowIndex);
                else
                    _selectedIndices.Add(rowIndex);
                _selectionAnchor = rowIndex;
            }
            else if (Control.ModifierKeys.HasFlag(Keys.Shift) && _selectionAnchor >= 0)
            {
                // Shift+Click: Range selection from anchor to clicked row
                _selectedIndices.Clear();
                int start = Math.Min(_selectionAnchor, rowIndex);
                int end = Math.Max(_selectionAnchor, rowIndex);
                for (int i = start; i <= end; i++)
                    _selectedIndices.Add(i);
            }
            else
            {
                // Normal click: Start drag selection
                _selectedIndices.Clear();
                _selectedIndices.Add(rowIndex);
                _selectionAnchor = rowIndex;
                _dragStartIndex = rowIndex;
                _isDragging = true;
            }

            Invalidate();
            Capture = true; // Capture mouse for drag outside control
        }

        private void DisassemblerView_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging || e.Button != MouseButtons.Left) return;

            int rowIndex = (e.Y / _rowHeight) + _scrollPosition;

            // Clamp to valid range
            rowIndex = Math.Max(0, Math.Min(_instructions.Count - 1, rowIndex));

            if (rowIndex < 0 || _dragStartIndex < 0) return;

            // Update selection to range from drag start to current position
            _selectedIndices.Clear();
            int start = Math.Min(_dragStartIndex, rowIndex);
            int end = Math.Max(_dragStartIndex, rowIndex);
            for (int i = start; i <= end; i++)
                _selectedIndices.Add(i);

            // Auto-scroll if dragging near edges
            int visibleRows = Height / _rowHeight;
            if (e.Y < _rowHeight && _scrollPosition > 0)
            {
                _scrollPosition--;
                _vScrollBar.Value = _scrollPosition;
            }
            else if (e.Y > Height - _rowHeight && _scrollPosition < _instructions.Count - visibleRows)
            {
                _scrollPosition++;
                _vScrollBar.Value = Math.Min(_vScrollBar.Maximum, _scrollPosition);
            }

            Invalidate();
        }

        private void DisassemblerView_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                Capture = false;
            }
        }

        private void DisassemblerView_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
            {
                var disasm = _instructions[_selectedIndex];
                var instruction = disasm.Instruction;

                // Check if click is in instruction column area (to determine whether to follow or assemble)
                int instructionColumnStart = _flowMarginWidth + _addressColumnWidth + _opcodeColumnWidth;
                int instructionColumnEnd = instructionColumnStart + _instructionColumnWidth;

                // If clicking in instruction area and it's not a data byte, open assembler
                if (e.X >= instructionColumnStart && e.X < instructionColumnEnd && !disasm.IsDataByte)
                {
                    // For branch/call instructions, Shift+DoubleClick follows, normal double-click opens assembler
                    if (Control.ModifierKeys.HasFlag(Keys.Shift))
                    {
                        // Shift+DoubleClick: Follow if it's a branch/call
                        if (instruction.FlowControl == FlowControl.UnconditionalBranch ||
                            instruction.FlowControl == FlowControl.ConditionalBranch ||
                            instruction.FlowControl == FlowControl.Call)
                        {
                            if (instruction.IsIPRelativeMemoryOperand)
                            {
                                SetAddress(new IntPtr((long)instruction.IPRelativeMemoryAddress));
                            }
                            else if (instruction.NearBranchTarget != 0)
                            {
                                SetAddress(new IntPtr((long)instruction.NearBranchTarget));
                            }
                        }
                    }
                    else
                    {
                        // Normal double-click: Open assembler dialog
                        OpenAssemblerDialog(disasm);
                    }
                }
                else
                {
                    // Click outside instruction column on a branch/call - follow it
                    if (instruction.FlowControl == FlowControl.UnconditionalBranch ||
                        instruction.FlowControl == FlowControl.ConditionalBranch ||
                        instruction.FlowControl == FlowControl.Call)
                    {
                        if (instruction.IsIPRelativeMemoryOperand)
                        {
                            SetAddress(new IntPtr((long)instruction.IPRelativeMemoryAddress));
                        }
                        else if (instruction.NearBranchTarget != 0)
                        {
                            SetAddress(new IntPtr((long)instruction.NearBranchTarget));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Open the assembler dialog to modify an instruction (like Cheat Engine)
        /// </summary>
        private void OpenAssemblerDialog(DisassembledInstruction disasm)
        {
            // Get the current instruction text
            _coloredOutput.Clear();
            _formatter.Format(disasm.Instruction, _coloredOutput);
            string currentInstruction = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));

            using var dialog = new Form
            {
                Text = $"Assemble at {disasm.Address.ToInt64():X}",
                Size = new Size(500, 180),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lblAddress = new System.Windows.Forms.Label { Text = $"Address: {disasm.Address.ToInt64():X}", Location = new Point(10, 15), AutoSize = true };
            var lblOriginal = new System.Windows.Forms.Label { Text = $"Original: {currentInstruction}", Location = new Point(10, 35), AutoSize = true };
            var lblBytes = new System.Windows.Forms.Label { Text = $"Bytes: {BitConverter.ToString(disasm.Bytes).Replace("-", " ")} ({disasm.Length} bytes)", Location = new Point(10, 55), AutoSize = true };
            var lblNew = new System.Windows.Forms.Label { Text = "New instruction:", Location = new Point(10, 80), AutoSize = true };
            var txtInstruction = new TextBox { Location = new Point(110, 77), Size = new Size(365, 23), Text = currentInstruction };
            var btnOk = new Button { Text = "Assemble", Location = new Point(300, 110), Size = new Size(80, 25), DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(390, 110), Size = new Size(80, 25), DialogResult = DialogResult.Cancel };

            ThemeManager.ApplyTheme(dialog);
            dialog.Controls.AddRange(new Control[] { lblAddress, lblOriginal, lblBytes, lblNew, txtInstruction, btnOk, btnCancel });
            dialog.AcceptButton = btnOk;
            dialog.CancelButton = btnCancel;

            // Select all text for easy replacement
            txtInstruction.SelectAll();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string newInstruction = txtInstruction.Text.Trim();
                if (!string.IsNullOrEmpty(newInstruction) && newInstruction != currentInstruction)
                {
                    AssembleAndWriteInstruction(disasm, newInstruction);
                }
            }
        }

        /// <summary>
        /// Assemble the instruction and write it to memory
        /// </summary>
        private void AssembleAndWriteInstruction(DisassembledInstruction disasm, string instruction)
        {
            if (_process == null || !_process.IsOpen)
            {
                MessageBox.Show("Process is not attached.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // Use Keystone or Iced.Intel to assemble the instruction
                // For now, we'll use a simple approach with Iced.Intel's Assembler
                var assembler = new Iced.Intel.Assembler(_is64Bit ? 64 : 32);

                // Parse and assemble the instruction
                byte[]? assembledBytes = AssembleInstruction(instruction, (ulong)disasm.Address.ToInt64());

                if (assembledBytes == null || assembledBytes.Length == 0)
                {
                    MessageBox.Show($"Failed to assemble instruction: {instruction}\n\nMake sure the instruction syntax is correct.",
                        "Assembly Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Check if new instruction fits
                if (assembledBytes.Length > disasm.Length)
                {
                    var result = MessageBox.Show(
                        $"New instruction is larger than original!\n\n" +
                        $"Original: {disasm.Length} bytes\n" +
                        $"New: {assembledBytes.Length} bytes\n\n" +
                        $"This may overwrite the next instruction(s). Continue?",
                        "Size Warning",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result != DialogResult.Yes)
                        return;
                }

                // If new instruction is smaller, pad with NOPs
                byte[] finalBytes;
                if (assembledBytes.Length < disasm.Length)
                {
                    finalBytes = new byte[disasm.Length];
                    Array.Copy(assembledBytes, finalBytes, assembledBytes.Length);
                    // Fill remainder with NOPs
                    for (int i = assembledBytes.Length; i < disasm.Length; i++)
                        finalBytes[i] = 0x90;
                }
                else
                {
                    finalBytes = assembledBytes;
                }

                // Store original bytes for potential restoration
                if (_nopManager != null)
                {
                    _coloredOutput.Clear();
                    _formatter.Format(disasm.Instruction, _coloredOutput);
                    string originalText = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));

                    // Add to code list for restoration later
                    _nopManager.AddModifiedCode(disasm.Address, disasm.Bytes, $"Modified: {originalText} -> {instruction}");
                }

                // When using kernel mode, we can write directly without VirtualProtectEx
                // The driver's MmCopyVirtualMemory can write to any memory
                bool useKernelMode = AntiCheatBypassForm.UseKernelMode && AntiCheatBypassForm.DriverController != null;
                uint oldProtect = 0;

                if (!useKernelMode)
                {
                    // Change memory protection to allow writing to executable memory
                    if (!VirtualProtectEx(_process.Handle, disasm.Address, (UIntPtr)finalBytes.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
                    {
                        int error = Marshal.GetLastWin32Error();
                        MessageBox.Show(
                            $"Failed to change memory protection.\n\n" +
                            $"Error code: {error}\n" +
                            $"Make sure you have sufficient privileges (run as Administrator).",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                bool success = false;
                try
                {
                    // Write the new bytes to memory
                    success = _process.Write(disasm.Address, finalBytes);

                    if (success)
                    {
                        // Flush instruction cache so CPU sees the new code (only if we have a valid handle)
                        if (_process.Handle != IntPtr.Zero)
                        {
                            FlushInstructionCache(_process.Handle, disasm.Address, (UIntPtr)finalBytes.Length);
                        }

                        MessageBox.Show(
                            $"Instruction assembled and written successfully!\n\n" +
                            $"Address: {disasm.Address.ToInt64():X}\n" +
                            $"New bytes: {BitConverter.ToString(finalBytes).Replace("-", " ")}" +
                            (useKernelMode ? "\n\n(Written via kernel driver)" : ""),
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        // Refresh disassembly at current position (don't jump back to base)
                        RefreshCurrentView();
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        MessageBox.Show(
                            $"Failed to write bytes to memory.\n\n" +
                            $"Error code: {error}\n" +
                            (useKernelMode ? "Kernel driver write failed. Check DebugView for details." :
                            $"Make sure you have sufficient privileges (run as Administrator)."),
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
                finally
                {
                    // Restore original memory protection (only if we changed it)
                    if (!useKernelMode && oldProtect != 0)
                    {
                        VirtualProtectEx(_process.Handle, disasm.Address, (UIntPtr)finalBytes.Length, oldProtect, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Assembly error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Assemble a single instruction to bytes using Iced.Intel
        /// </summary>
        private byte[]? AssembleInstruction(string instruction, ulong ip)
        {
            try
            {
                int bitness = _is64Bit ? 64 : 32;
                var assembler = new Iced.Intel.Assembler(bitness);

                // Parse the instruction manually and add to assembler
                // This is a simplified approach - for full support we'd need a complete parser
                string instr = instruction.Trim().ToLowerInvariant();

                // Try to parse common instructions
                if (TryParseAndAddInstruction(assembler, instr, ip))
                {
                    // Assemble to bytes
                    using var stream = new System.IO.MemoryStream();
                    assembler.Assemble(new Iced.Intel.StreamCodeWriter(stream), ip);
                    return stream.ToArray();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to parse an address or immediate value (handles 0x prefix, h suffix, near prefix)
        /// </summary>
        private static bool TryParseAddress(string s, out ulong address)
        {
            address = 0;
            s = s.Trim().ToLowerInvariant();

            // Handle "near" or "short" prefix (e.g., "near 00834838h")
            if (s.StartsWith("near ")) s = s.Substring(5).Trim();
            if (s.StartsWith("short ")) s = s.Substring(6).Trim();

            // Handle hex with 'h' suffix (e.g., "00834838h")
            if (s.EndsWith("h"))
            {
                s = s.Substring(0, s.Length - 1);
                return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out address);
            }

            // Handle 0x prefix
            if (s.StartsWith("0x"))
            {
                s = s.Substring(2);
                return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out address);
            }

            // Try as hex first (most addresses are hex without prefix)
            if (ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out address))
                return true;

            // Try as decimal
            return ulong.TryParse(s, out address);
        }

        /// <summary>
        /// Parse operands separated by comma
        /// </summary>
        private static string[] ParseOperands(string operands)
        {
            // Split by comma, but handle spaces around comma
            return operands.Split(',').Select(s => s.Trim()).ToArray();
        }

        /// <summary>
        /// Try to parse a single instruction and add it to the assembler.
        /// Uses Iced.Intel.Assembler which provides type-safe instruction building.
        /// </summary>
        private bool TryParseAndAddInstruction(Iced.Intel.Assembler a, string instruction, ulong ip)
        {
            try
            {
                // Common instruction patterns
                string[] parts = instruction.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return false;

                string mnemonic = parts[0].ToLowerInvariant();
                string operands = parts.Length > 1 ? parts[1].Trim() : "";

                // === No-operand instructions ===
                switch (mnemonic)
                {
                    case "nop": a.nop(); return true;
                    case "int3": a.int3(); return true;
                    case "pause": a.pause(); return true;
                    case "hlt": a.hlt(); return true;
                    case "clc": a.clc(); return true;
                    case "stc": a.stc(); return true;
                    case "cmc": a.cmc(); return true;
                    case "cld": a.cld(); return true;
                    case "std": a.std(); return true;
                    case "pushfq": a.pushfq(); return true;
                    case "popfq": a.popfq(); return true;
                    case "pushfd": a.pushfd(); return true;
                    case "popfd": a.popfd(); return true;
                    case "ret" when string.IsNullOrEmpty(operands): a.ret(); return true;
                    case "retn" when string.IsNullOrEmpty(operands): a.ret(); return true;
                    case "leave": a.leave(); return true;
                    case "cdq": a.cdq(); return true;
                    case "cqo": a.cqo(); return true;
                    case "cbw": a.cbw(); return true;
                    case "cwde": a.cwde(); return true;
                    case "cdqe": a.cdqe(); return true;
                }

                // === RET with immediate ===
                if (mnemonic == "ret" || mnemonic == "retn")
                {
                    if (ushort.TryParse(operands, out ushort imm))
                    {
                        a.ret(imm);
                        return true;
                    }
                }

                // === INT with vector ===
                if (mnemonic == "int")
                {
                    if (TryParseAddress(operands, out ulong vec) && vec <= 255)
                    {
                        a.@int((byte)vec);
                        return true;
                    }
                }

                // === JMP instruction (to address) ===
                if (mnemonic == "jmp")
                {
                    if (TryParseAddress(operands, out ulong target))
                    {
                        a.jmp(target);
                        return true;
                    }
                }

                // === CALL instruction (to address) ===
                if (mnemonic == "call")
                {
                    if (TryParseAddress(operands, out ulong target))
                    {
                        a.call(target);
                        return true;
                    }
                }

                // === Conditional jumps ===
                if (TryParseAddress(operands, out ulong jmpTarget))
                {
                    switch (mnemonic)
                    {
                        case "je": case "jz": a.je(jmpTarget); return true;
                        case "jne": case "jnz": a.jne(jmpTarget); return true;
                        case "ja": case "jnbe": a.ja(jmpTarget); return true;
                        case "jae": case "jnb": case "jnc": a.jae(jmpTarget); return true;
                        case "jb": case "jnae": case "jc": a.jb(jmpTarget); return true;
                        case "jbe": case "jna": a.jbe(jmpTarget); return true;
                        case "jg": case "jnle": a.jg(jmpTarget); return true;
                        case "jge": case "jnl": a.jge(jmpTarget); return true;
                        case "jl": case "jnge": a.jl(jmpTarget); return true;
                        case "jle": case "jng": a.jle(jmpTarget); return true;
                        case "jo": a.jo(jmpTarget); return true;
                        case "jno": a.jno(jmpTarget); return true;
                        case "jp": case "jpe": a.jp(jmpTarget); return true;
                        case "jnp": case "jpo": a.jnp(jmpTarget); return true;
                        case "js": a.js(jmpTarget); return true;
                        case "jns": a.jns(jmpTarget); return true;
                        case "jecxz": a.jecxz(jmpTarget); return true;
                        case "jrcxz": a.jrcxz(jmpTarget); return true;
                        case "loop": a.loop(jmpTarget); return true;
                        case "loope": case "loopz": a.loope(jmpTarget); return true;
                        case "loopne": case "loopnz": a.loopne(jmpTarget); return true;
                    }
                }

                // === Push immediate ===
                if (mnemonic == "push" && TryParseAddress(operands, out ulong pushImm))
                {
                    if (pushImm <= int.MaxValue)
                    {
                        a.push((int)pushImm);
                        return true;
                    }
                }

                // === Single-operand 64-bit register instructions ===
                string opLower = operands.ToLowerInvariant();
                if (TryHandleUnaryInstruction64(a, mnemonic, opLower)) return true;
                if (TryHandleUnaryInstruction32(a, mnemonic, opLower)) return true;

                // === Two-operand instructions ===
                var ops = ParseOperands(operands);
                if (ops.Length == 2)
                {
                    string dest = ops[0].ToLowerInvariant();
                    string src = ops[1].ToLowerInvariant();

                    // Try 64-bit register operations first
                    if (TryHandleTwoOperandInstruction64(a, mnemonic, dest, src)) return true;
                    // Try 32-bit register operations
                    if (TryHandleTwoOperandInstruction32(a, mnemonic, dest, src)) return true;
                }

                // Instruction not supported - show message
                MessageBox.Show(
                    $"Could not assemble: {instruction}\n\n" +
                    $"Supported instruction categories:\n" +
                    $"  - Control: jmp, call, ret, je/jne/jz/jnz/ja/jb/jg/jl/etc\n" +
                    $"  - Data: mov, xchg, push, pop\n" +
                    $"  - Arithmetic: add, sub, inc, dec\n" +
                    $"  - Logic: xor, and, or, not, test, cmp\n" +
                    $"  - Other: nop, int, int3, hlt, etc.\n\n" +
                    $"Format examples:\n" +
                    $"  jmp 0x140001000  |  jmp 140001000h\n" +
                    $"  mov rax, rbx     |  mov eax, 0x1234\n" +
                    $"  xor rax, rax     |  add eax, 10\n\n" +
                    $"Note: Memory operands [reg+offset] not yet supported.",
                    "Assembler Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Assembler exception: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 64-bit register mapping for typed assembler
        private static readonly Dictionary<string, AssemblerRegister64> _reg64Map = new(StringComparer.OrdinalIgnoreCase)
        {
            {"rax", AssemblerRegisters.rax}, {"rbx", AssemblerRegisters.rbx}, {"rcx", AssemblerRegisters.rcx}, {"rdx", AssemblerRegisters.rdx},
            {"rsi", AssemblerRegisters.rsi}, {"rdi", AssemblerRegisters.rdi}, {"rsp", AssemblerRegisters.rsp}, {"rbp", AssemblerRegisters.rbp},
            {"r8", AssemblerRegisters.r8}, {"r9", AssemblerRegisters.r9}, {"r10", AssemblerRegisters.r10}, {"r11", AssemblerRegisters.r11},
            {"r12", AssemblerRegisters.r12}, {"r13", AssemblerRegisters.r13}, {"r14", AssemblerRegisters.r14}, {"r15", AssemblerRegisters.r15},
        };

        // 32-bit register mapping for typed assembler
        private static readonly Dictionary<string, AssemblerRegister32> _reg32Map = new(StringComparer.OrdinalIgnoreCase)
        {
            {"eax", AssemblerRegisters.eax}, {"ebx", AssemblerRegisters.ebx}, {"ecx", AssemblerRegisters.ecx}, {"edx", AssemblerRegisters.edx},
            {"esi", AssemblerRegisters.esi}, {"edi", AssemblerRegisters.edi}, {"esp", AssemblerRegisters.esp}, {"ebp", AssemblerRegisters.ebp},
            {"r8d", AssemblerRegisters.r8d}, {"r9d", AssemblerRegisters.r9d}, {"r10d", AssemblerRegisters.r10d}, {"r11d", AssemblerRegisters.r11d},
            {"r12d", AssemblerRegisters.r12d}, {"r13d", AssemblerRegisters.r13d}, {"r14d", AssemblerRegisters.r14d}, {"r15d", AssemblerRegisters.r15d},
        };

        private bool TryHandleUnaryInstruction64(Iced.Intel.Assembler a, string mnemonic, string operand)
        {
            if (!_reg64Map.TryGetValue(operand, out var reg)) return false;

            switch (mnemonic)
            {
                case "push": a.push(reg); return true;
                case "pop": a.pop(reg); return true;
                case "inc": a.inc(reg); return true;
                case "dec": a.dec(reg); return true;
                case "neg": a.neg(reg); return true;
                case "not": a.not(reg); return true;
                case "mul": a.mul(reg); return true;
                case "imul": a.imul(reg); return true;
                case "div": a.div(reg); return true;
                case "idiv": a.idiv(reg); return true;
                case "jmp": a.jmp(reg); return true;
                case "call": a.call(reg); return true;
            }
            return false;
        }

        private bool TryHandleUnaryInstruction32(Iced.Intel.Assembler a, string mnemonic, string operand)
        {
            if (!_reg32Map.TryGetValue(operand, out var reg)) return false;

            switch (mnemonic)
            {
                case "push": a.push(reg); return true;
                case "pop": a.pop(reg); return true;
                case "inc": a.inc(reg); return true;
                case "dec": a.dec(reg); return true;
                case "neg": a.neg(reg); return true;
                case "not": a.not(reg); return true;
                case "mul": a.mul(reg); return true;
                case "imul": a.imul(reg); return true;
                case "div": a.div(reg); return true;
                case "idiv": a.idiv(reg); return true;
                case "jmp": a.jmp(reg); return true;
                case "call": a.call(reg); return true;
            }
            return false;
        }

        private bool TryHandleTwoOperandInstruction64(Iced.Intel.Assembler a, string mnemonic, string dest, string src)
        {
            // Try reg64, reg64
            if (_reg64Map.TryGetValue(dest, out var destReg64) && _reg64Map.TryGetValue(src, out var srcReg64))
            {
                switch (mnemonic)
                {
                    case "mov": a.mov(destReg64, srcReg64); return true;
                    case "add": a.add(destReg64, srcReg64); return true;
                    case "sub": a.sub(destReg64, srcReg64); return true;
                    case "xor": a.xor(destReg64, srcReg64); return true;
                    case "and": a.and(destReg64, srcReg64); return true;
                    case "or": a.or(destReg64, srcReg64); return true;
                    case "cmp": a.cmp(destReg64, srcReg64); return true;
                    case "test": a.test(destReg64, srcReg64); return true;
                    case "xchg": a.xchg(destReg64, srcReg64); return true;
                    case "imul": a.imul(destReg64, srcReg64); return true;
                }
            }

            // Try reg64, imm
            if (_reg64Map.TryGetValue(dest, out destReg64) && TryParseAddress(src, out ulong srcImm))
            {
                int immValue = (int)srcImm;
                switch (mnemonic)
                {
                    case "mov": a.mov(destReg64, srcImm); return true;
                    case "add": a.add(destReg64, immValue); return true;
                    case "sub": a.sub(destReg64, immValue); return true;
                    case "xor": a.xor(destReg64, immValue); return true;
                    case "and": a.and(destReg64, immValue); return true;
                    case "or": a.or(destReg64, immValue); return true;
                    case "cmp": a.cmp(destReg64, immValue); return true;
                    case "test": a.test(destReg64, immValue); return true;
                    case "shl": case "sal": a.shl(destReg64, (byte)srcImm); return true;
                    case "shr": a.shr(destReg64, (byte)srcImm); return true;
                    case "sar": a.sar(destReg64, (byte)srcImm); return true;
                    case "rol": a.rol(destReg64, (byte)srcImm); return true;
                    case "ror": a.ror(destReg64, (byte)srcImm); return true;
                }
            }

            return false;
        }

        private bool TryHandleTwoOperandInstruction32(Iced.Intel.Assembler a, string mnemonic, string dest, string src)
        {
            // Try reg32, reg32
            if (_reg32Map.TryGetValue(dest, out var destReg32) && _reg32Map.TryGetValue(src, out var srcReg32))
            {
                switch (mnemonic)
                {
                    case "mov": a.mov(destReg32, srcReg32); return true;
                    case "add": a.add(destReg32, srcReg32); return true;
                    case "sub": a.sub(destReg32, srcReg32); return true;
                    case "xor": a.xor(destReg32, srcReg32); return true;
                    case "and": a.and(destReg32, srcReg32); return true;
                    case "or": a.or(destReg32, srcReg32); return true;
                    case "cmp": a.cmp(destReg32, srcReg32); return true;
                    case "test": a.test(destReg32, srcReg32); return true;
                    case "xchg": a.xchg(destReg32, srcReg32); return true;
                    case "imul": a.imul(destReg32, srcReg32); return true;
                }
            }

            // Try reg32, imm
            if (_reg32Map.TryGetValue(dest, out destReg32) && TryParseAddress(src, out ulong srcImm))
            {
                int immValue = (int)srcImm;
                uint uimmValue = (uint)srcImm;
                switch (mnemonic)
                {
                    case "mov": a.mov(destReg32, uimmValue); return true;
                    case "add": a.add(destReg32, immValue); return true;
                    case "sub": a.sub(destReg32, immValue); return true;
                    case "xor": a.xor(destReg32, immValue); return true;
                    case "and": a.and(destReg32, immValue); return true;
                    case "or": a.or(destReg32, immValue); return true;
                    case "cmp": a.cmp(destReg32, immValue); return true;
                    case "test": a.test(destReg32, immValue); return true;
                    case "shl": case "sal": a.shl(destReg32, (byte)srcImm); return true;
                    case "shr": a.shr(destReg32, (byte)srcImm); return true;
                    case "sar": a.sar(destReg32, (byte)srcImm); return true;
                    case "rol": a.rol(destReg32, (byte)srcImm); return true;
                    case "ror": a.ror(destReg32, (byte)srcImm); return true;
                }
            }

            return false;
        }

        private void MenuGoto_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
            {
                var disasm = _instructions[_selectedIndex];
                // Prompt for address or navigate to selected instruction
                SetAddress(disasm.Address);
            }
        }

        private void MenuCopyAddress_Click(object? sender, EventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var sortedIndices = _selectedIndices.OrderBy(i => i).ToList();
            var lines = new List<string>();

            foreach (int idx in sortedIndices)
            {
                if (idx >= 0 && idx < _instructions.Count)
                {
                    var disasm = _instructions[idx];
                    lines.Add($"{disasm.Address.ToInt64():X16}");
                }
            }

            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void MenuCopyBytes_Click(object? sender, EventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var sortedIndices = _selectedIndices.OrderBy(i => i).ToList();
            var lines = new List<string>();

            foreach (int idx in sortedIndices)
            {
                if (idx >= 0 && idx < _instructions.Count)
                {
                    var disasm = _instructions[idx];
                    lines.Add(BitConverter.ToString(disasm.Bytes).Replace("-", " "));
                }
            }

            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void MenuCopyBinary_Click(object? sender, EventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var sortedIndices = _selectedIndices.OrderBy(i => i).ToList();
            var lines = new List<string>();

            foreach (int idx in sortedIndices)
            {
                if (idx >= 0 && idx < _instructions.Count)
                {
                    var disasm = _instructions[idx];
                    // Convert each byte to 8-bit binary string
                    string binaryStr = string.Join(" ", disasm.Bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
                    lines.Add(binaryStr);
                }
            }

            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void MenuCopyInstructions_Click(object? sender, EventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var sortedIndices = _selectedIndices.OrderBy(i => i).ToList();
            var lines = new List<string>();

            foreach (int idx in sortedIndices)
            {
                if (idx >= 0 && idx < _instructions.Count)
                {
                    var disasm = _instructions[idx];
                    _coloredOutput.Clear();
                    _formatter.Format(disasm.Instruction, _coloredOutput);
                    string instructionStr = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));
                    lines.Add(instructionStr);
                }
            }

            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void MenuCopyDisassembly_Click(object? sender, EventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var sortedIndices = _selectedIndices.OrderBy(i => i).ToList();
            var lines = new List<string>();

            foreach (int idx in sortedIndices)
            {
                if (idx >= 0 && idx < _instructions.Count)
                {
                    var disasm = _instructions[idx];
                    string addressStr = ResolveAddress(disasm.Address);

                    _coloredOutput.Clear();
                    _formatter.Format(disasm.Instruction, _coloredOutput);
                    string instructionStr = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));

                    lines.Add($"{addressStr,-25} {instructionStr}");
                }
            }

            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void MenuCopyFullLines_Click(object? sender, EventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var sortedIndices = _selectedIndices.OrderBy(i => i).ToList();
            var lines = new List<string>();

            foreach (int idx in sortedIndices)
            {
                if (idx >= 0 && idx < _instructions.Count)
                {
                    var disasm = _instructions[idx];
                    string addressStr = ResolveAddress(disasm.Address);
                    string bytesStr = BitConverter.ToString(disasm.Bytes).Replace("-", " ");

                    _coloredOutput.Clear();
                    _formatter.Format(disasm.Instruction, _coloredOutput);
                    string instructionStr = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));

                    string comment = GetInstructionComment(disasm.Instruction);

                    string fullLine = $"{addressStr,-25} {bytesStr,-24} {instructionStr}";
                    if (!string.IsNullOrEmpty(comment))
                    {
                        fullLine += $" ; {comment}";
                    }
                    lines.Add(fullLine);
                }
            }

            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void MenuFollow_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
            {
                var disasm = _instructions[_selectedIndex];
                var instruction = disasm.Instruction;

                if (instruction.FlowControl == FlowControl.UnconditionalBranch ||
                    instruction.FlowControl == FlowControl.ConditionalBranch ||
                    instruction.FlowControl == FlowControl.Call)
                {
                    if (instruction.NearBranchTarget != 0)
                    {
                        SetAddress(new IntPtr((long)instruction.NearBranchTarget));
                    }
                    else if (instruction.IsIPRelativeMemoryOperand)
                    {
                        SetAddress(new IntPtr((long)instruction.IPRelativeMemoryAddress));
                    }
                }
            }
        }

        private void MenuNop_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _instructions.Count)
            {
                MessageBox.Show("Please select an instruction first.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_nopManager == null)
            {
                MessageBox.Show("NOP manager not initialized.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var disasm = _instructions[_selectedIndex];
            var formatter = new StringBuilderFormatterOutput();
            _formatter.Format(disasm.Instruction, formatter);
            string instructionText = formatter.ToString();

            // Confirm with user
            var result = MessageBox.Show(
                $"Replace instruction at {disasm.Address.ToInt64():X8} with NOPs?\n\n" +
                $"Instruction: {instructionText}\n" +
                $"Bytes: {BitConverter.ToString(disasm.Bytes).Replace("-", " ")}\n" +
                $"Length: {disasm.Length} bytes\n\n" +
                "This will replace the instruction with NOP (0x90) opcodes.",
                "Confirm NOP Replacement",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                bool success = _nopManager.ReplaceWithNOPs(
                    disasm.Address,
                    disasm.Length,
                    $"{disasm.Address.ToInt64():X8}: {instructionText}");

                if (success)
                {
                    MessageBox.Show("Instruction replaced with NOPs successfully!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Refresh disassembly at current position (don't jump back to base)
                    RefreshCurrentView();
                }
                else
                {
                    MessageBox.Show("Failed to replace instruction with NOPs.\n\n" +
                        "Make sure you have sufficient privileges (run as Administrator).",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void MenuRestore_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _instructions.Count)
            {
                MessageBox.Show("Please select an instruction first.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_nopManager == null)
            {
                MessageBox.Show("NOP manager not initialized.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var disasm = _instructions[_selectedIndex];

            // Find matching code list entry
            CodeListEntry? matchingEntry = null;
            foreach (var entry in _nopManager.CodeList)
            {
                if (entry.Address == disasm.Address && entry.IsEnabled)
                {
                    matchingEntry = entry;
                    break;
                }
            }

            if (matchingEntry == null)
            {
                MessageBox.Show("No active NOP entry found at this address.\n\n" +
                    "This instruction may not have been NOPed, or the NOP was already restored.",
                    "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Confirm with user
            var result = MessageBox.Show(
                $"Restore original bytes at {matchingEntry.Address.ToInt64():X8}?\n\n" +
                $"Description: {matchingEntry.Description}\n" +
                $"Original bytes: {matchingEntry.GetBytesString()}\n" +
                $"Length: {matchingEntry.Length} bytes\n\n" +
                "This will restore the instruction to its original state.",
                "Confirm Restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                bool success = _nopManager.RestoreOriginalBytes(matchingEntry);

                if (success)
                {
                    MessageBox.Show("Original bytes restored successfully!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Refresh disassembly at current position (don't jump back to base)
                    RefreshCurrentView();
                }
                else
                {
                    MessageBox.Show("Failed to restore original bytes.\n\n" +
                        "Make sure you have sufficient privileges (run as Administrator).",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void MenuComment_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _instructions.Count) return;

            var disasm = _instructions[_selectedIndex];
            long addr = disasm.Address.ToInt64();
            string existingComment = CommentManager.Get(addr) ?? "";

            using var dialog = new Form
            {
                Text = "Add/Edit Comment",
                Size = new Size(450, 130),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lblComment = new System.Windows.Forms.Label { Text = $"Comment for {addr:X}:", Location = new Point(10, 15), AutoSize = true };
            var txtComment = new TextBox { Location = new Point(10, 35), Size = new Size(410, 23), Text = existingComment };
            var btnOk = new Button { Text = "OK", Location = new Point(260, 65), Size = new Size(80, 25), DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(345, 65), Size = new Size(80, 25), DialogResult = DialogResult.Cancel };

            ThemeManager.ApplyTheme(dialog);
            dialog.Controls.AddRange(new Control[] { lblComment, txtComment, btnOk, btnCancel });
            dialog.AcceptButton = btnOk;
            dialog.CancelButton = btnCancel;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                CommentManager.Set(addr, txtComment.Text);
            }
        }

        private void MenuBookmark_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _instructions.Count) return;

            var disasm = _instructions[_selectedIndex];
            long addr = disasm.Address.ToInt64();

            // Get instruction text
            string instructionText = "";
            if (!disasm.IsDataByte)
            {
                _coloredOutput.Clear();
                _formatter.Format(disasm.Instruction, _coloredOutput);
                instructionText = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));
            }
            else
            {
                instructionText = $"db {disasm.Bytes[0]:X2}";
            }

            // Get module info (format: "ModuleName+Offset" like "Gunz.exe+29DF28")
            string moduleInfo = ResolveAddress(disasm.Address);

            var bookmark = new Bookmark
            {
                Address = addr,
                Label = "",
                Comment = CommentManager.Get(addr) ?? "",
                Instruction = instructionText,
                Module = moduleInfo,
                // Store the module+offset string for re-resolving after game restart (ASLR)
                OriginalAddressString = moduleInfo.Contains('+') ? moduleInfo : ""
            };

            BookmarkManager.Add(bookmark);
        }

        private void MenuRemoveBookmark_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _instructions.Count) return;

            var disasm = _instructions[_selectedIndex];
            long addr = disasm.Address.ToInt64();

            if (BookmarkManager.Exists(addr))
            {
                BookmarkManager.Remove(addr);
            }
        }

        private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Enable/disable "Remove Bookmark" based on whether selected address is bookmarked
            if (_menuRemoveBookmark != null)
            {
                bool isBookmarked = false;
                if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
                {
                    var disasm = _instructions[_selectedIndex];
                    isBookmarked = BookmarkManager.Exists(disasm.Address.ToInt64());
                }
                _menuRemoveBookmark.Enabled = isBookmarked;
            }
        }

        private void OnBookmarksChanged()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnBookmarksChanged));
                return;
            }
            Invalidate();
        }

        private void OnCommentsChanged()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnCommentsChanged));
                return;
            }
            Invalidate();
        }

        private void DrawCenteredText(Graphics g, string text, Brush brush)
        {
            SizeF textSize = g.MeasureString(text, _font);
            float x = (Width - textSize.Width) / 2;
            float y = (Height - textSize.Height) / 2;
            g.DrawString(text, _font, brush, x, y);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            int delta = e.Delta / 120;
            int newValue = _scrollPosition - delta;

            newValue = Math.Max(0, Math.Min(_vScrollBar.Maximum, newValue));
            _scrollPosition = newValue;
            _vScrollBar.Value = newValue;

            Invalidate();
        }

        /// <summary>
        /// Set single selection to the specified index
        /// </summary>
        private void SetSingleSelection(int index)
        {
            _selectedIndices.Clear();
            if (index >= 0 && index < _instructions.Count)
            {
                _selectedIndices.Add(index);
                _selectionAnchor = index;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int visibleRows = Math.Max(1, Height / _rowHeight);

            switch (keyData)
            {
                case Keys.Up:
                    if (_selectedIndex > 0)
                    {
                        SetSingleSelection(_selectedIndex - 1);
                        EnsureVisible(_selectedIndex);
                        Invalidate();
                    }
                    return true;

                case Keys.Down:
                    if (_selectedIndex < _instructions.Count - 1)
                    {
                        SetSingleSelection(_selectedIndex + 1);
                        EnsureVisible(_selectedIndex);
                        Invalidate();
                    }
                    return true;

                case Keys.PageUp:
                    SetSingleSelection(Math.Max(0, _selectedIndex - visibleRows));
                    EnsureVisible(_selectedIndex);
                    Invalidate();
                    return true;

                case Keys.PageDown:
                    SetSingleSelection(Math.Min(_instructions.Count - 1, _selectedIndex + visibleRows));
                    EnsureVisible(_selectedIndex);
                    Invalidate();
                    return true;

                case Keys.Home:
                    SetSingleSelection(0);
                    _scrollPosition = 0;
                    _vScrollBar.Value = 0;
                    Invalidate();
                    return true;

                case Keys.End:
                    SetSingleSelection(_instructions.Count - 1);
                    _scrollPosition = Math.Max(0, _instructions.Count - visibleRows);
                    _vScrollBar.Value = _scrollPosition;
                    Invalidate();
                    return true;

                case Keys.Enter:
                    // Follow jump/call on Enter
                    if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
                    {
                        var disasm = _instructions[_selectedIndex];
                        var instruction = disasm.Instruction;

                        if (instruction.FlowControl == FlowControl.UnconditionalBranch ||
                            instruction.FlowControl == FlowControl.ConditionalBranch ||
                            instruction.FlowControl == FlowControl.Call)
                        {
                            if (instruction.NearBranchTarget != 0)
                            {
                                SetAddress(new IntPtr((long)instruction.NearBranchTarget));
                            }
                        }
                    }
                    return true;

                case Keys.G | Keys.Control:
                    // Ctrl+G = Go to address
                    PromptGotoAddress();
                    return true;

                case Keys.C | Keys.Control:
                    // Ctrl+C = Copy addresses (supports multi-selection)
                    MenuCopyAddress_Click(null, EventArgs.Empty);
                    return true;

                case Keys.C | Keys.Control | Keys.Shift:
                    // Ctrl+Shift+C = Copy bytes (supports multi-selection)
                    MenuCopyBytes_Click(null, EventArgs.Empty);
                    return true;

                case Keys.D | Keys.Control:
                    // Ctrl+D = Toggle bookmark
                    if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
                    {
                        var disasm = _instructions[_selectedIndex];
                        long addr = disasm.Address.ToInt64();
                        if (BookmarkManager.Exists(addr))
                            MenuRemoveBookmark_Click(null, EventArgs.Empty);
                        else
                            MenuBookmark_Click(null, EventArgs.Empty);
                    }
                    return true;

                case Keys.Oem1: // Semicolon key (;)
                    // ; = Add/Edit comment
                    MenuComment_Click(null, EventArgs.Empty);
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void EnsureVisible(int index)
        {
            int visibleRows = Math.Max(1, Height / _rowHeight);

            if (index < _scrollPosition)
            {
                _scrollPosition = index;
            }
            else if (index >= _scrollPosition + visibleRows)
            {
                _scrollPosition = index - visibleRows + 1;
            }

            _scrollPosition = Math.Max(0, Math.Min(_vScrollBar.Maximum, _scrollPosition));
            _vScrollBar.Value = _scrollPosition;
        }

        private void PromptGotoAddress()
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Go to Address";
                dialog.Size = new Size(300, 120);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                var label = new System.Windows.Forms.Label { Text = "Address (hex):", Left = 10, Top = 15, Width = 80 };
                var textBox = new TextBox { Left = 95, Top = 12, Width = 180 };
                var btnOk = new Button { Text = "OK", Left = 115, Top = 50, Width = 75, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Cancel", Left = 200, Top = 50, Width = 75, DialogResult = DialogResult.Cancel };

                dialog.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
                dialog.AcceptButton = btnOk;
                dialog.CancelButton = btnCancel;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string addrText = textBox.Text.Trim().Replace("0x", "").Replace("0X", "");
                    if (long.TryParse(addrText, System.Globalization.NumberStyles.HexNumber, null, out long addr))
                    {
                        SetAddress(new IntPtr(addr));
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font?.Dispose();
                _vScrollBar?.Dispose();
                _contextMenu?.Dispose();

                // Dispose cached brushes
                foreach (var brush in _brushCache.Values)
                {
                    brush?.Dispose();
                }
                _brushCache.Clear();
            }
            base.Dispose(disposing);
        }

        public IntPtr? SelectedAddress
        {
            get
            {
                if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
                    return _instructions[_selectedIndex].Address;
                return null;
            }
        }

        public int? SelectedInstructionLength
        {
            get
            {
                if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
                    return _instructions[_selectedIndex].Length;
                return null;
            }
        }

        /// <summary>
        /// Get all disassembled instructions (for cross-reference scanning)
        /// </summary>
        public IReadOnlyList<DisassembledInstruction> Instructions => _instructions.AsReadOnly();

        /// <summary>
        /// Get selected instruction (primary selection)
        /// </summary>
        public DisassembledInstruction? SelectedInstruction
        {
            get
            {
                if (_selectedIndex >= 0 && _selectedIndex < _instructions.Count)
                    return _instructions[_selectedIndex];
                return null;
            }
        }

        /// <summary>
        /// Get all selected indices
        /// </summary>
        public IReadOnlyCollection<int> SelectedIndicesCollection => _selectedIndices;

        /// <summary>
        /// Export the current disassembly to a string
        /// </summary>
        public string ExportDisassembly()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("; CrxMem Disassembly Export");
            sb.AppendLine($"; Base Address: {_baseAddress.ToInt64():X}");
            sb.AppendLine($"; Total Instructions: {_instructions.Count}");
            sb.AppendLine($"; Architecture: {(_is64Bit ? "x64" : "x86")}");
            sb.AppendLine();
            sb.AppendLine("; Address                  Bytes                    Instruction");
            sb.AppendLine("; " + new string('-', 80));

            foreach (var disasm in _instructions)
            {
                string addressStr = ResolveAddress(disasm.Address);
                string bytesStr = BitConverter.ToString(disasm.Bytes).Replace("-", " ");

                string instructionStr;
                if (disasm.IsDataByte)
                {
                    instructionStr = $"db {disasm.Bytes[0]:X2}";
                }
                else
                {
                    _coloredOutput.Clear();
                    _formatter.Format(disasm.Instruction, _coloredOutput);
                    instructionStr = string.Join("", _coloredOutput.GetParts().Select(p => p.Text));
                }

                string comment = disasm.IsDataByte ? "" : GetInstructionComment(disasm.Instruction);

                sb.Append($"{addressStr,-25} {bytesStr,-24} {instructionStr}");
                if (!string.IsNullOrEmpty(comment))
                {
                    sb.Append($" ; {comment}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    public class DisassembledInstruction
    {
        public IntPtr Address { get; set; }
        public int Length { get; set; }
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public Instruction Instruction { get; set; }
        public bool IsDataByte { get; set; } // True if this is raw data (db XX), not a valid instruction
    }

    /// <summary>
    /// Represents a flow line (jump/call arrow) in the disassembler margin
    /// </summary>
    public class FlowLine
    {
        public int SourceIndex { get; set; }  // Index of the source instruction
        public int TargetIndex { get; set; }  // Index of the target instruction
        public int Depth { get; set; }        // Nesting depth for visual offset
        public bool IsCall { get; set; }      // True if CALL, false if jump
        public bool IsConditional { get; set; } // True if conditional jump
        public Color Color { get; set; }      // Line color
    }

    // Custom formatter output that writes to StringBuilder
    class StringBuilderFormatterOutput : FormatterOutput
    {
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

        public override void Write(string text, FormatterTextKind kind)
        {
            _sb.Append(text);
        }

        public override string ToString()
        {
            var result = _sb.ToString();
            _sb.Clear();
            return result;
        }
    }
}
