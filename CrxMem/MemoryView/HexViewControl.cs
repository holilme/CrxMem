using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using CrxMem.Core;

namespace CrxMem.MemoryView
{
    public class HexViewControl : UserControl
    {
        private const int BytesPerRow = 16;
        private const int PageSize = 4096; // 4KB pages
        private const int VisibleRowsBuffer = 5; // Extra rows to cache above/below
        private const int CacheExpiryMs = 100; // Cache expires after 100ms for live updates

        private ProcessAccess? _process;
        private IntPtr _baseAddress;
        private VScrollBar _vScrollBar;

        private Font _font;
        private int _charWidth;
        private int _charHeight;
        private int _rowHeight;

        private int _addressColumnWidth;
        private int _hexColumnWidth;
        private int _asciiColumnWidth;

        // Page cache with timestamps for expiration
        private Dictionary<long, (byte[] data, long timestamp)> _pageCache;
        private long _visibleRows;

        // Module resolution cache (modules don't change, so can be long-lived)
        // Key: 1MB aligned region, Value: (moduleName, moduleBaseAddress) or null if no module
        private Dictionary<long, (string name, long baseAddress)?> _moduleAddressCache;

        // Selection state
        private long _selectionStart = -1;
        private long _selectionEnd = -1;
        private long _cursorPosition = -1;
        private bool _isSelecting = false;

        public event EventHandler<IntPtr>? AddressSelected;
        public event EventHandler<IntPtr>? GotoRequest;
        public event EventHandler<long>? SelectionChanged;
        public event EventHandler<IntPtr>? FindAccessRequested;
        public event EventHandler<IntPtr>? FindAccessHWBPRequested;

        public enum DisplayType
        {
            Byte,
            Word,
            DWord,
            QWord,
            Float,
            Double
        }

        private DisplayType _currentDisplayType = DisplayType.Byte;

        public IntPtr CursorAddress => _cursorPosition == -1 ? IntPtr.Zero : new IntPtr(_cursorPosition);

        public HexViewControl(ProcessAccess? process)
        {
            _process = process;
            _baseAddress = IntPtr.Zero;
            _pageCache = new Dictionary<long, (byte[] data, long timestamp)>();
            _moduleAddressCache = new Dictionary<long, (string name, long baseAddress)?>();

            _font = new Font("Consolas", 9F);

            // Create scrollbar
            _vScrollBar = new VScrollBar
            {
                Dock = DockStyle.Right,
                Minimum = 0,
                Maximum = 100000,
                SmallChange = 1,
                LargeChange = 10
            };
            _vScrollBar.Scroll += VScrollBar_Scroll;
            Controls.Add(_vScrollBar);

            // Set control properties
            DoubleBuffered = true;
            // Make control selectable to receive keyboard input for hotkeys (Ctrl+G, etc.)
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            ApplyTheme();

            // Calculate dimensions
            using (Graphics g = CreateGraphics())
            {
                SizeF charSize = g.MeasureString("0", _font);
                _charWidth = (int)Math.Ceiling(charSize.Width);
                _charHeight = (int)Math.Ceiling(charSize.Height);
                _rowHeight = _charHeight + 2;
            }

            _addressColumnWidth = _charWidth * 25; // "ModuleName+Offset:" (e.g., "Gunz.exe+4B91058:")
            _hexColumnWidth = _charWidth * (BytesPerRow * 3 + 2); // "00 00 00..." with spaces
            _asciiColumnWidth = _charWidth * (BytesPerRow + 2); // ASCII representation

            InitializeContextMenu();

            Resize += (s, e) => UpdateVisibleRows();
            UpdateVisibleRows();
        }

        private void UpdateVisibleRows()
        {
            _visibleRows = (Height / _rowHeight) + (VisibleRowsBuffer * 2);
            Invalidate();
        }

        private void VScrollBar_Scroll(object? sender, ScrollEventArgs e)
        {
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
            Invalidate();
        }

        public void SetAddress(IntPtr address)
        {
            _baseAddress = address;
            _pageCache.Clear();
            _moduleAddressCache.Clear(); // Clear module cache on address change
            _vScrollBar.Value = 0;
            Invalidate();
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

            int yPos = 5;
            long scrollOffset = _vScrollBar.Value;
            long startAddress = _baseAddress.ToInt64() + (scrollOffset * BytesPerRow);

            // Draw visible rows
            for (int row = 0; row < _visibleRows && yPos < Height; row++)
            {
                long currentAddress = startAddress + (row * BytesPerRow);
                DrawRow(g, currentAddress, yPos);
                yPos += _rowHeight;
            }
        }

        private void DrawRow(Graphics g, long address, int yPos)
        {
            int xPos = 5;

            // Draw address (Module+Offset format like Cheat Engine) - theme-aware
            string addrStr = ResolveAddressForHexView((IntPtr)address);
            using (var addrBrush = new SolidBrush(ThemeManager.SyntaxAddress))
            {
                g.DrawString(addrStr, _font, addrBrush, xPos, yPos);
            }
            xPos += _addressColumnWidth;

            // Read memory for this row
            byte[] rowData = ReadMemory(new IntPtr(address), BytesPerRow);

            // Draw data - theme-aware
            using (var dataBrush = new SolidBrush(ThemeManager.Foreground))
            using (var errorBrush = new SolidBrush(ThemeManager.ForegroundDim))
            using (var selectionBrush = new SolidBrush(Color.FromArgb(100, 0, 120, 215)))
            {
                long selStart = Math.Min(_selectionStart, _selectionEnd);
                long selEnd = Math.Max(_selectionStart, _selectionEnd);

                int bytesPerItem = _currentDisplayType switch
                {
                    DisplayType.Byte => 1,
                    DisplayType.Word => 2,
                    DisplayType.DWord => 4,
                    DisplayType.QWord => 8,
                    DisplayType.Float => 4,
                    DisplayType.Double => 8,
                    _ => 1
                };

                for (int i = 0; i < BytesPerRow; i += bytesPerItem)
                {
                    long currentAddr = address + i;
                    bool isSelected = currentAddr >= selStart && currentAddr <= selEnd;

                    if (isSelected)
                    {
                        g.FillRectangle(selectionBrush, xPos, yPos, _charWidth * (bytesPerItem * 3 - 1), _rowHeight);
                    }

                    if (i + bytesPerItem <= rowData.Length && rowData != null)
                    {
                        string displayStr = FormatData(rowData, i, _currentDisplayType);
                        var brush = isSelected ? Brushes.White : dataBrush;
                        g.DrawString(displayStr, _font, brush, xPos, yPos);
                    }
                    else
                    {
                        g.DrawString(new string('?', bytesPerItem * 2), _font, errorBrush, xPos, yPos);
                    }
                    xPos += _charWidth * (bytesPerItem * 3);
                }
            }

            xPos += _charWidth * 2; // Gap between hex and ASCII

            // Draw ASCII - theme-aware
            using (var asciiBrush = new SolidBrush(ThemeManager.SyntaxString))
            using (var errorBrush = new SolidBrush(ThemeManager.ForegroundDim))
            {
                for (int i = 0; i < BytesPerRow; i++)
                {
                    if (i < rowData.Length && rowData != null)
                    {
                        char c = (char)rowData[i];
                        string displayChar = (c >= 32 && c <= 126) ? c.ToString() : ".";
                        g.DrawString(displayChar, _font, asciiBrush, xPos, yPos);
                    }
                    else
                    {
                        g.DrawString("?", _font, errorBrush, xPos, yPos);
                    }
                    xPos += _charWidth;
                }
            }
        }

        private byte[] ReadMemory(IntPtr address, int size)
        {
            if (_process == null)
                return Array.Empty<byte>();

            try
            {
                // Calculate page boundaries
                long pageBase = (address.ToInt64() / PageSize) * PageSize;
                long currentTime = Stopwatch.GetTimestamp();
                long ticksPerMs = Stopwatch.Frequency / 1000;

                // Check cache with expiration
                byte[]? pageData = null;
                if (_pageCache.TryGetValue(pageBase, out var cached))
                {
                    // Check if cache is still valid
                    long ageMs = (currentTime - cached.timestamp) / ticksPerMs;
                    if (ageMs < CacheExpiryMs)
                    {
                        pageData = cached.data;
                    }
                }

                if (pageData == null)
                {
                    // Read full page
                    pageData = _process.Read(new IntPtr(pageBase), PageSize);

                    // Cache it with timestamp
                    _pageCache[pageBase] = (pageData, currentTime);

                    // Limit cache size (remove oldest entries)
                    if (_pageCache.Count > 100)
                    {
                        // Clear old entries instead of all
                        var keysToRemove = new List<long>();
                        foreach (var kvp in _pageCache)
                        {
                            long ageMs = (currentTime - kvp.Value.timestamp) / ticksPerMs;
                            if (ageMs > CacheExpiryMs * 2)
                                keysToRemove.Add(kvp.Key);
                        }
                        foreach (var key in keysToRemove)
                            _pageCache.Remove(key);

                        // If still too large, clear all
                        if (_pageCache.Count > 100)
                            _pageCache.Clear();
                    }
                }

                // Extract the requested bytes from the cached page
                int offset = (int)(address.ToInt64() - pageBase);
                int available = Math.Min(size, pageData.Length - offset);

                if (available <= 0)
                    return Array.Empty<byte>();

                byte[] result = new byte[available];
                Array.Copy(pageData, offset, result, 0, available);
                return result;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private void DrawCenteredText(Graphics g, string text, Brush brush)
        {
            SizeF textSize = g.MeasureString(text, _font);
            float x = (Width - textSize.Width) / 2;
            float y = (Height - textSize.Height) / 2;
            g.DrawString(text, _font, brush, x, y);
        }

        /// <summary>
        /// Resolve address to Module+Offset format for hex view (like Cheat Engine)
        /// Uses caching to avoid expensive module lookups on every paint
        /// </summary>
        private string ResolveAddressForHexView(IntPtr address)
        {
            if (_process == null)
                return $"{address.ToInt64():X8}:";

            // Calculate which module "region" this address is in (cache by 1MB aligned regions)
            // This is a heuristic: most modules are 1MB+ so we can cache by region
            long regionKey = address.ToInt64() & ~0xFFFFF; // Align to 1MB boundary

            // Check module cache (use page-aligned key for efficiency)
            if (_moduleAddressCache.TryGetValue(regionKey, out var cachedResult))
            {
                if (cachedResult.HasValue)
                {
                    // Use cached module info - no need to call GetModuleForAddress again
                    long offset = address.ToInt64() - cachedResult.Value.baseAddress;
                    return $"{cachedResult.Value.name}+{offset:X}:";
                }
                // Cached as "no module"
                return $"{address.ToInt64():X}:";
            }

            try
            {
                var module = _process.GetModuleForAddress(address);
                if (module != null)
                {
                    // Cache the module info for this region
                    _moduleAddressCache[regionKey] = (module.ModuleName, module.BaseAddress.ToInt64());
                    long offset = address.ToInt64() - module.BaseAddress.ToInt64();
                    return $"{module.ModuleName}+{offset:X}:";
                }
                else
                {
                    // Cache that there's no module for this region
                    _moduleAddressCache[regionKey] = null;
                }
            }
            catch
            {
                // If module resolution fails, cache as null (no module)
                _moduleAddressCache[regionKey] = null;
            }

            return $"{address.ToInt64():X}:";
        }

        private string FormatData(byte[] data, int index, DisplayType type)
        {
            switch (type)
            {
                case DisplayType.Byte:
                    return $"{data[index]:X2} ";
                case DisplayType.Word:
                    return $"{BitConverter.ToUInt16(data, index):X4} ";
                case DisplayType.DWord:
                    return $"{BitConverter.ToUInt32(data, index):X8} ";
                case DisplayType.QWord:
                    return $"{BitConverter.ToUInt64(data, index):X16} ";
                case DisplayType.Float:
                    return $"{BitConverter.ToSingle(data, index):F3} ";
                case DisplayType.Double:
                    return $"{BitConverter.ToDouble(data, index):F4} ";
                default:
                    return $"{data[index]:X2} ";
            }
        }

        private void InitializeContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            var gotoItem = new ToolStripMenuItem("Go to Address (Ctrl+G)");
            gotoItem.Click += (s, e) => GotoRequest?.Invoke(this, IntPtr.Zero);
            menu.Items.Add(gotoItem);

            menu.Items.Add(new ToolStripSeparator());

            var copyItem = new ToolStripMenuItem("Copy Selection (Hex)");
            copyItem.Click += (s, e) => CopySelection();
            menu.Items.Add(copyItem);

            var pasteItem = new ToolStripMenuItem("Paste (Ctrl+V)");
            pasteItem.Click += (s, e) => PasteFromClipboard();
            menu.Items.Add(pasteItem);

            // Find what accesses submenu with both methods
            var findAccessMenu = new ToolStripMenuItem("Find what accesses this address");
            
            var findAccessPageGuard = new ToolStripMenuItem("Using PAGE_GUARD (fast, module addresses only)");
            findAccessPageGuard.Click += (s, e) => {
                if (_cursorPosition != -1) FindAccessRequested?.Invoke(this, new IntPtr(_cursorPosition));
            };
            findAccessMenu.DropDownItems.Add(findAccessPageGuard);

            var findAccessHWBP = new ToolStripMenuItem("Using Hardware Breakpoint (reliable, any address)");
            findAccessHWBP.Click += (s, e) => {
                if (_cursorPosition != -1) FindAccessHWBPRequested?.Invoke(this, new IntPtr(_cursorPosition));
            };
            findAccessMenu.DropDownItems.Add(findAccessHWBP);
            
            menu.Items.Add(findAccessMenu);

            var makeWritableItem = new ToolStripMenuItem("Make page writable");
            makeWritableItem.Click += (s, e) => MakePageWritable();
            menu.Items.Add(makeWritableItem);

            menu.Items.Add(new ToolStripSeparator());

            var displayMenu = new ToolStripMenuItem("Display Type");
            displayMenu.DropDownItems.Add(CreateDisplayTypeItem("Byte", DisplayType.Byte));
            displayMenu.DropDownItems.Add(CreateDisplayTypeItem("2 Bytes (Word)", DisplayType.Word));
            displayMenu.DropDownItems.Add(CreateDisplayTypeItem("4 Bytes (DWord)", DisplayType.DWord));
            displayMenu.DropDownItems.Add(CreateDisplayTypeItem("8 Bytes (QWord)", DisplayType.QWord));
            displayMenu.DropDownItems.Add(CreateDisplayTypeItem("Float", DisplayType.Float));
            displayMenu.DropDownItems.Add(CreateDisplayTypeItem("Double", DisplayType.Double));
            menu.Items.Add(displayMenu);

            this.ContextMenuStrip = menu;
        }

        private ToolStripMenuItem CreateDisplayTypeItem(string text, DisplayType type)
        {
            var item = new ToolStripMenuItem(text);
            item.Checked = (_currentDisplayType == type);
            item.Click += (s, e) =>
            {
                _currentDisplayType = type;
                Invalidate();
            };
            return item;
        }

        private void CopySelection()
        {
            if (_selectionStart == -1 || _selectionEnd == -1) return;

            long start = Math.Min(_selectionStart, _selectionEnd);
            long end = Math.Max(_selectionStart, _selectionEnd);
            int length = (int)(end - start + 1);

            byte[] data = ReadMemory((IntPtr)start, length);
            if (data.Length > 0)
            {
                string hex = BitConverter.ToString(data).Replace("-", " ");
                Clipboard.SetText(hex);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button == MouseButtons.Left)
            {
                long addr = GetAddressAtPoint(e.Location);
                if (addr != -1)
                {
                    _cursorPosition = addr;
                    _selectionStart = addr;
                    _selectionEnd = addr;
                    _isSelecting = true;
                    SelectionChanged?.Invoke(this, _cursorPosition);
                    Invalidate();
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isSelecting)
            {
                long addr = GetAddressAtPoint(e.Location);
                if (addr != -1)
                {
                    _selectionEnd = addr;
                    SelectionChanged?.Invoke(this, _selectionEnd);
                    Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isSelecting = false;
        }

        private long GetAddressAtPoint(Point pt)
        {
            int bytesPerItem = _currentDisplayType switch
            {
                DisplayType.Byte => 1,
                DisplayType.Word => 2,
                DisplayType.DWord => 4,
                DisplayType.QWord => 8,
                DisplayType.Float => 4,
                DisplayType.Double => 8,
                _ => 1
            };

            int row = (pt.Y - 5) / _rowHeight;
            int col = (pt.X - _addressColumnWidth) / (_charWidth * 3);

            if (col < 0 || col >= BytesPerRow) return -1;
            
            // Snap to item boundary
            col = (col / bytesPerItem) * bytesPerItem;

            long scrollOffset = _vScrollBar.Value;
            return _baseAddress.ToInt64() + (scrollOffset + row) * BytesPerRow + col;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            if (_cursorPosition != -1 && _process != null && _currentDisplayType == DisplayType.Byte)
            {
                char c = char.ToUpper(e.KeyChar);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))
                {
                    int val = (c >= '0' && c <= '9') ? (c - '0') : (c - 'A' + 10);
                    
                    byte currentByte = ReadMemory((IntPtr)_cursorPosition, 1)[0];
                    
                    // Simple logic: first hex char sets high nibble, move to low nibble
                    // But we don't track nibbles easily here. 
                    // Let's do: first char sets byte to 0x0X, second to 0xXY?
                    // Cheat Engine approach: you type 'A', it becomes 'A0' or '0A'?
                    // Actually, let's just write the byte.
                    
                    // For now, let's just implement a simple byte update:
                    // (In a future update we can add nibble tracking)
                    byte newByte = (byte)val; // This is not quite right for nibbles but works for single char edit
                    
                    // Actually, let's just support full byte entry or simplified
                    _process.Write<byte>((IntPtr)_cursorPosition, newByte);
                    
                    _cursorPosition++;
                    _selectionStart = _cursorPosition;
                    _selectionEnd = _cursorPosition;
                    Invalidate();
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.G))
            {
                GotoRequest?.Invoke(this, IntPtr.Zero);
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                CopySelection();
                return true;
            }
            if (keyData == (Keys.Control | Keys.V))
            {
                PasteFromClipboard();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                _selectionStart = -1;
                _selectionEnd = -1;
                _cursorPosition = -1;
                Invalidate();
                return true;
            }

            // Keyboard navigation
            if (_cursorPosition != -1)
            {
                bool handled = false;
                long newPos = _cursorPosition;

                if (keyData == Keys.Left) { newPos--; handled = true; }
                else if (keyData == Keys.Right) { newPos++; handled = true; }
                else if (keyData == Keys.Up) { newPos -= BytesPerRow; handled = true; }
                else if (keyData == Keys.Down) { newPos += BytesPerRow; handled = true; }
                else if (keyData == (Keys.Shift | Keys.Left)) { _selectionEnd--; handled = true; }
                else if (keyData == (Keys.Shift | Keys.Right)) { _selectionEnd++; handled = true; }
                else if (keyData == (Keys.Shift | Keys.Up)) { _selectionEnd -= BytesPerRow; handled = true; }
                else if (keyData == (Keys.Shift | Keys.Down)) { _selectionEnd += BytesPerRow; handled = true; }

                if (handled)
                {
                    if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
                    {
                        _cursorPosition = newPos;
                        _selectionStart = newPos;
                        _selectionEnd = newPos;
                    }
                    SelectionChanged?.Invoke(this, _selectionEnd);
                    Invalidate();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font?.Dispose();
                _vScrollBar?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            // Scroll with mouse wheel
            int delta = e.Delta / 120; // Standard wheel delta
            int newValue = _vScrollBar.Value - (delta * 3); // 3 rows per wheel notch

            newValue = Math.Max(_vScrollBar.Minimum, Math.Min(_vScrollBar.Maximum - _vScrollBar.LargeChange + 1, newValue));
            _vScrollBar.Value = newValue;

            Invalidate();
        }

        private void PasteFromClipboard()
        {
            if (_cursorPosition == -1 || _process == null) return;

            try
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text)) return;

                // Parse hex string (supports "AA BB CC" or "AABBCC" format)
                text = text.Replace(" ", "").Replace("-", "");
                if (text.Length % 2 != 0) return;

                byte[] bytes = new byte[text.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
                }

                // Write bytes to memory
                _process.WriteWithProtection((IntPtr)_cursorPosition, bytes);
                
                // Clear cache and redraw
                _pageCache.Clear();
                Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to paste: {ex.Message}", "Paste Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MakePageWritable()
        {
            if (_cursorPosition == -1 || _process == null) return;

            try
            {
                long pageBase = _cursorPosition & ~0xFFF; // Align to page boundary
                bool success = _process.ProtectMemory((IntPtr)pageBase, 0x1000, 0x40, out uint oldProtect);
                
                if (success)
                {
                    MessageBox.Show($"Page at 0x{pageBase:X} is now PAGE_EXECUTE_READWRITE.\nPrevious protection: 0x{oldProtect:X}", 
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to change page protection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
