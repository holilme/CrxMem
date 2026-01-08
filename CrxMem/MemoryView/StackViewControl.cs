using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CrxMem.Core;

namespace CrxMem.MemoryView
{
    /// <summary>
    /// Displays stack memory in x64dbg style
    /// </summary>
    public class StackViewControl : UserControl
    {
        // P/Invoke for getting thread context (to read RSP/ESP)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadContext(IntPtr hThread, ref CONTEXT64 lpContext);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        private const uint THREAD_GET_CONTEXT = 0x0008;
        private const uint THREAD_SUSPEND_RESUME = 0x0002;
        private const uint CONTEXT_CONTROL = 0x00010001;
        private const uint CONTEXT_INTEGER = 0x00010002;
        private const uint CONTEXT_ALL = 0x0010001F;

        [StructLayout(LayoutKind.Sequential)]
        private struct CONTEXT64
        {
            public ulong P1Home, P2Home, P3Home, P4Home, P5Home, P6Home;
            public uint ContextFlags;
            public uint MxCsr;
            public ushort SegCs, SegDs, SegEs, SegFs, SegGs, SegSs;
            public uint EFlags;
            public ulong Dr0, Dr1, Dr2, Dr3, Dr6, Dr7;
            public ulong Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi;
            public ulong R8, R9, R10, R11, R12, R13, R14, R15;
            public ulong Rip;
            // XSAVE area would follow but we don't need it for RSP
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] ExtendedRegisters;
        }

        private Font _font;
        private int _rowHeight;
        private VScrollBar _vScrollBar;
        private ProcessAccess? _process;
        private bool _is64Bit = true;

        // Stack values (either real or demo)
        private ulong _rsp;
        private ulong[] _stackValues = Array.Empty<ulong>();
        private int _stackEntries = 50;
        private bool _usingRealStack = false;

        public StackViewControl()
        {
            _font = new Font("Consolas", 9F);
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

            // Set demo values
            SetDemoValues();
        }

        public void SetProcess(Core.ProcessAccess? process, bool is64Bit)
        {
            _process = process;
            _is64Bit = is64Bit;
            RefreshStack();
        }

        private void SetDemoValues()
        {
            // Demo RSP value
            _rsp = 0x000000000014F8B0;

            // Demo stack values (simulating what would be on the stack)
            _stackValues = new ulong[_stackEntries];
            var random = new Random(42); // Fixed seed for consistent display

            for (int i = 0; i < _stackEntries; i++)
            {
                // Mix of return addresses, saved registers, and local variables
                if (i % 5 == 0)
                {
                    // Likely a return address (high address)
                    _stackValues[i] = 0x00007FF6A1230000 + (ulong)(random.Next(0, 0x100000));
                }
                else if (i % 3 == 0)
                {
                    // Saved register value
                    _stackValues[i] = 0x0000000000000000 + (ulong)(random.Next(0, 0x10000));
                }
                else
                {
                    // Local variable or padding
                    _stackValues[i] = (ulong)random.Next(0, 0x1000);
                }
            }
        }

        public void RefreshStack()
        {
            _usingRealStack = false;

            if (_process == null || !_process.IsOpen)
            {
                SetDemoValues();
                Invalidate();
                return;
            }

            try
            {
                // Try to get RSP from the first thread
                var threads = _process.GetThreads();
                if (threads.Count > 0)
                {
                    var firstThread = threads[0];
                    ulong? rspValue = GetThreadRSP(firstThread.Id);

                    if (rspValue.HasValue && rspValue.Value != 0)
                    {
                        _rsp = rspValue.Value;

                        // Read stack memory
                        if (ReadStackMemory())
                        {
                            _usingRealStack = true;
                            Invalidate();
                            return;
                        }
                    }
                }

                // Fallback: Try to find and read from stack region
                if (TryReadFromStackRegion())
                {
                    _usingRealStack = true;
                    Invalidate();
                    return;
                }
            }
            catch
            {
                // Failed to read real stack
            }

            // Fall back to demo values
            SetDemoValues();
            Invalidate();
        }

        /// <summary>
        /// Get RSP value from a thread
        /// </summary>
        private ulong? GetThreadRSP(int threadId)
        {
            if (!_is64Bit) return null; // 32-bit not implemented yet

            IntPtr hThread = OpenThread(THREAD_GET_CONTEXT | THREAD_SUSPEND_RESUME, false, (uint)threadId);
            if (hThread == IntPtr.Zero) return null;

            try
            {
                // Must suspend thread to get context
                SuspendThread(hThread);

                var context = new CONTEXT64();
                context.ContextFlags = CONTEXT_CONTROL;
                context.ExtendedRegisters = new byte[512];

                if (GetThreadContext(hThread, ref context))
                {
                    return context.Rsp;
                }
            }
            finally
            {
                ResumeThread(hThread);
                CloseHandle(hThread);
            }

            return null;
        }

        /// <summary>
        /// Read stack memory starting from RSP
        /// </summary>
        private bool ReadStackMemory()
        {
            if (_process == null || _rsp == 0) return false;

            int pointerSize = _is64Bit ? 8 : 4;
            int bytesToRead = _stackEntries * pointerSize;

            byte[]? stackData = _process.Read(new IntPtr((long)_rsp), bytesToRead);
            if (stackData == null || stackData.Length < pointerSize) return false;

            _stackValues = new ulong[_stackEntries];
            int entriesRead = stackData.Length / pointerSize;

            for (int i = 0; i < entriesRead && i < _stackEntries; i++)
            {
                if (_is64Bit)
                    _stackValues[i] = BitConverter.ToUInt64(stackData, i * 8);
                else
                    _stackValues[i] = BitConverter.ToUInt32(stackData, i * 4);
            }

            return entriesRead > 0;
        }

        /// <summary>
        /// Try to find stack region and read from it
        /// </summary>
        private bool TryReadFromStackRegion()
        {
            if (_process == null) return false;

            try
            {
                // Find stack regions (MEM_PRIVATE, readable/writable)
                var regions = MemoryRegion.EnumerateRegions(_process.Handle);
                var stackRegion = regions.FirstOrDefault(r =>
                    r.IsReadable && r.IsWritable &&
                    r.Type == 0x20000 && // MEM_PRIVATE
                    r.Size >= 0x10000 && r.Size <= 0x200000); // Stack-like size (64KB to 2MB)

                if (stackRegion == null) return false;

                // Use the end of the region as a reasonable stack pointer guess
                // (stack grows downward, so active stack is near the end of the region)
                long estimatedRsp = stackRegion.BaseAddress.ToInt64() + stackRegion.Size - 0x1000;
                _rsp = (ulong)estimatedRsp;

                return ReadStackMemory();
            }
            catch
            {
                return false;
            }
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.Background;
            ForeColor = ThemeManager.Foreground;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.Clear(BackColor);

            int yPos = 5 - (_vScrollBar.Value * _rowHeight);
            int colAddress = 5;
            int colValue = _is64Bit ? 150 : 100;
            int colComment = _is64Bit ? 310 : 200;

            int entrySize = _is64Bit ? 8 : 4;

            using var addressBrush = new SolidBrush(ThemeManager.SyntaxAddress);
            using var valueBrush = new SolidBrush(ThemeManager.Foreground);
            using var commentBrush = new SolidBrush(ThemeManager.ForegroundDim);
            using var rspBrush = new SolidBrush(ThemeManager.StatusGreen);
            using var headerBrush = new SolidBrush(ThemeManager.SyntaxMnemonic);

            // Header
            string headerText = _usingRealStack ? "Stack View (Live)" : "Stack View (Demo)";
            g.DrawString(headerText, new Font(_font, FontStyle.Bold), headerBrush, 5, yPos);
            yPos += _rowHeight + 5;

            // Column headers
            g.DrawString("Address", _font, commentBrush, colAddress, yPos);
            g.DrawString("Value", _font, commentBrush, colValue, yPos);
            g.DrawString("Comment", _font, commentBrush, colComment, yPos);
            yPos += _rowHeight + 2;

            // Stack entries
            for (int i = 0; i < _stackEntries && i < _stackValues.Length; i++)
            {
                if (yPos < -_rowHeight)
                {
                    yPos += _rowHeight;
                    continue;
                }
                if (yPos > Height)
                    break;

                ulong stackAddr = _rsp + (ulong)(i * entrySize);
                ulong value = _stackValues[i];

                // Draw address
                bool isRsp = (i == 0);
                string addrStr = _is64Bit ? $"{stackAddr:X16}" : $"{(uint)stackAddr:X8}";
                string prefix = isRsp ? "RSP> " : "     ";
                var addrBrush = isRsp ? rspBrush : addressBrush;
                g.DrawString(prefix + addrStr, _font, addrBrush, colAddress, yPos);

                // Draw value
                string valueStr = _is64Bit ? $"{value:X16}" : $"{(uint)value:X8}";
                g.DrawString(valueStr, _font, valueBrush, colValue, yPos);

                // Draw comment (guess what the value might be)
                string comment = GetValueComment(value);
                g.DrawString(comment, _font, commentBrush, colComment, yPos);

                yPos += _rowHeight;
            }

            // Update scrollbar
            int totalHeight = yPos + (_vScrollBar.Value * _rowHeight);
            _vScrollBar.Maximum = Math.Max(0, (totalHeight - Height) / _rowHeight + 10);
        }

        private string GetValueComment(ulong value)
        {
            // Try to guess what kind of value this is
            if (value == 0)
                return "NULL";

            if (value >= 0x00007FF000000000 && value < 0x00008000_00000000)
                return "Return addr?";

            if (value >= 0x00000000_00010000 && value < 0x00000001_00000000)
            {
                if (value > 0x00000000_00100000)
                    return "Stack ptr?";
                return "Local var";
            }

            if (value < 0x10000)
                return $"= {value}";

            return "";
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
                _vScrollBar?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
