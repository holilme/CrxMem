using System;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using CrxMem.Core;
using Iced.Intel;

namespace CrxMem.MemoryView
{
    public class MemoryAccessForm : Form
    {
        private ListView _lvHits;
        private DebugMonitor _monitor;
        private IntPtr _address;
        private ProcessAccess _process;
        private NasmFormatter _formatter;
        private ColoredFormatterOutput _output;
        private System.Windows.Forms.Label _lblStatus;
        private bool _useHardwareBreakpoint;

        public MemoryAccessForm(ProcessAccess process, IntPtr address, bool useHardwareBreakpoint = false)
        {
            _process = process;
            _address = address;
            _useHardwareBreakpoint = useHardwareBreakpoint;
            _monitor = new DebugMonitor(process);
            _monitor.BreakpointHit += Monitor_BreakpointHit;
            _formatter = new NasmFormatter();
            _output = new ColoredFormatterOutput();

            Text = $"Addresses that access {address.ToInt64():X}" + (useHardwareBreakpoint ? " [HWBP]" : " [PAGE_GUARD]");
            Size = new Size(650, 450);
            StartPosition = FormStartPosition.CenterParent;

            // Status label at top
            _lblStatus = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Starting monitoring...",
                Padding = new Padding(5, 0, 0, 0)
            };
            Controls.Add(_lblStatus);

            _lvHits = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true
            };
            _lvHits.Columns.Add("Address", 140);
            _lvHits.Columns.Add("Instruction", 340);
            _lvHits.Columns.Add("Count", 80);

            Controls.Add(_lvHits);
            _lvHits.BringToFront(); // Make sure ListView is above the status label
            
            ThemeManager.ApplyTheme(this);
            _lblStatus.BackColor = ThemeManager.BackgroundAlt;
            _lblStatus.ForeColor = ThemeManager.Foreground;
            
            this.FormClosing += (s, e) => _monitor.StopMonitoring();
            
            StartMonitoring();
        }

        private void StartMonitoring()
        {
            bool success;
            
            if (_useHardwareBreakpoint)
            {
                success = _monitor.StartMonitoringWithHardwareBreakpoint((ulong)_address, false, 4);
                if (success)
                {
                    _lblStatus.Text = $"Monitoring 0x{_address.ToInt64():X} using Hardware Breakpoint (DR0-DR3)";
                    _lblStatus.ForeColor = Color.LightGreen;
                }
            }
            else
            {
                success = _monitor.StartMonitoring((ulong)_address, false, 4);
                if (success)
                {
                    _lblStatus.Text = $"Monitoring 0x{_address.ToInt64():X} using PAGE_GUARD";
                    _lblStatus.ForeColor = Color.LightGreen;
                }
            }

            if (!success)
            {
                _lblStatus.Text = $"Failed: {_monitor.LastError}";
                _lblStatus.ForeColor = Color.Red;

                string mode = _useHardwareBreakpoint ? "Hardware Breakpoint" : "PAGE_GUARD";
                string altMode = _useHardwareBreakpoint ? "PAGE_GUARD" : "Hardware Breakpoint";
                
                var result = MessageBox.Show(
                    $"Failed to start monitoring using {mode}.\n\n" +
                    $"Error: {_monitor.LastError}\n\n" +
                    $"Would you like to try using {altMode} instead?",
                    "Monitoring Failed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    _useHardwareBreakpoint = !_useHardwareBreakpoint;
                    Text = $"Addresses that access {_address.ToInt64():X}" + (_useHardwareBreakpoint ? " [HWBP]" : " [PAGE_GUARD]");
                    StartMonitoring(); // Try again with alternate method
                }
            }
        }

        private void Monitor_BreakpointHit(object? sender, BreakpointHitEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler<BreakpointHitEventArgs>(Monitor_BreakpointHit), sender, e);
                return;
            }

            string addrStr = $"{e.InstructionAddress:X}";
            foreach (ListViewItem existing in _lvHits.Items)
            {
                if (existing.Text == addrStr)
                {
                    int count = int.Parse(existing.SubItems[2].Text);
                    existing.SubItems[2].Text = (count + 1).ToString();
                    return;
                }
            }

            var item = new ListViewItem(addrStr);
            item.SubItems.Add("Calculating...");
            item.SubItems.Add("1");
            _lvHits.Items.Add(item);
            
            // Try to get instruction text
            try {
                byte[]? code = _process.Read((IntPtr)e.InstructionAddress, 15);
                if (code != null) {
                    var decoder = Decoder.Create(_process.Is64Bit ? 64 : 32, code);
                    decoder.IP = e.InstructionAddress;
                    decoder.Decode(out var instruction);
                    
                    _output.Clear();
                    _formatter.Format(instruction, _output);
                    item.SubItems[1].Text = string.Join("", _output.GetParts().Select(p => p.Text));
                }
            } catch { }
        }
    }
}
