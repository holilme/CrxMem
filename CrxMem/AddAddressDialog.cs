using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using CrxMem.Core;

namespace CrxMem
{
    public class AddAddressDialog : Form
    {
        private TextBox txtAddress;
        private Label lblValuePreview;
        private TextBox txtDescription;
        private ComboBox cmbType;
        private CheckBox chkHexadecimal;
        private CheckBox chkSigned;
        private Button btnOK;
        private Button btnCancel;
        private ProcessAccess? _process;
        private System.Windows.Forms.Timer? _previewTimer;

        public IntPtr Address { get; private set; }
        public string Description { get; private set; } = "";
        public string ValueType { get; private set; } = "4 Bytes";
        public bool IsHexadecimal { get; private set; } = false;
        public bool IsSigned { get; private set; } = false;
        /// <summary>
        /// The original address string as entered by the user (e.g., "Gunz.exe+29DF28")
        /// This is preserved for saving to cheat tables so it works across restarts.
        /// </summary>
        public string OriginalAddressString { get; private set; } = "";

        public AddAddressDialog() : this(null)
        {
        }

        public AddAddressDialog(ProcessAccess? process)
        {
            _process = process;
            InitializeComponents();
            SetupPreviewTimer();
        }

        private void InitializeComponents()
        {
            this.Text = "Add address";
            this.Size = new Size(380, 230);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Address label
            var lblAddress = new Label
            {
                Text = "Address:",
                Location = new Point(10, 10),
                Size = new Size(60, 20)
            };

            // Address textbox
            txtAddress = new TextBox
            {
                Location = new Point(10, 30),
                Size = new Size(270, 23),
                Font = new Font("Consolas", 9F)
            };
            txtAddress.TextChanged += TxtAddress_TextChanged;

            // Value preview label (shows =??? or =value)
            lblValuePreview = new Label
            {
                Text = "=???",
                Location = new Point(285, 32),
                Size = new Size(80, 20),
                ForeColor = Color.Gray,
                Font = new Font("Consolas", 9F)
            };

            // Description label
            var lblDesc = new Label
            {
                Text = "Description",
                Location = new Point(10, 58),
                Size = new Size(100, 20)
            };

            // Description textbox
            txtDescription = new TextBox
            {
                Location = new Point(10, 78),
                Size = new Size(345, 23),
                Text = "No description"
            };
            txtDescription.GotFocus += (s, e) => {
                if (txtDescription.Text == "No description")
                    txtDescription.Text = "";
            };
            txtDescription.LostFocus += (s, e) => {
                if (string.IsNullOrWhiteSpace(txtDescription.Text))
                    txtDescription.Text = "No description";
            };

            // Type label
            var lblType = new Label
            {
                Text = "Type",
                Location = new Point(10, 106),
                Size = new Size(50, 20)
            };

            // Type combobox
            cmbType = new ComboBox
            {
                Location = new Point(10, 126),
                Size = new Size(120, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbType.Items.AddRange(new object[] {
                "Byte",
                "2 Bytes",
                "4 Bytes",
                "8 Bytes",
                "Float",
                "Double",
                "String",
                "Array of byte"
            });
            cmbType.SelectedIndex = 2; // Default to 4 Bytes
            cmbType.SelectedIndexChanged += CmbType_SelectedIndexChanged;

            // Hexadecimal checkbox
            chkHexadecimal = new CheckBox
            {
                Text = "Hexadecimal",
                Location = new Point(140, 128),
                Size = new Size(95, 20)
            };
            chkHexadecimal.CheckedChanged += (s, e) => UpdateValuePreview();

            // Signed checkbox
            chkSigned = new CheckBox
            {
                Text = "Signed",
                Location = new Point(240, 128),
                Size = new Size(70, 20)
            };
            chkSigned.CheckedChanged += (s, e) => UpdateValuePreview();

            // OK button
            btnOK = new Button
            {
                Text = "OK",
                Location = new Point(100, 158),
                Size = new Size(80, 25),
                DialogResult = DialogResult.OK
            };
            btnOK.Click += BtnOK_Click;

            // Cancel button
            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(190, 158),
                Size = new Size(80, 25),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[] {
                lblAddress, txtAddress, lblValuePreview,
                lblDesc, txtDescription,
                lblType, cmbType, chkHexadecimal, chkSigned,
                btnOK, btnCancel
            });

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void SetupPreviewTimer()
        {
            _previewTimer = new System.Windows.Forms.Timer();
            _previewTimer.Interval = 300; // Update preview every 300ms after typing stops
            _previewTimer.Tick += (s, e) => {
                _previewTimer?.Stop();
                UpdateValuePreview();
            };
        }

        private void TxtAddress_TextChanged(object? sender, EventArgs e)
        {
            // Restart the timer to debounce
            _previewTimer?.Stop();
            _previewTimer?.Start();
        }

        private void CmbType_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateValuePreview();
        }

        private void UpdateValuePreview()
        {
            string addrText = txtAddress.Text.Trim();
            if (string.IsNullOrEmpty(addrText))
            {
                lblValuePreview.Text = "=???";
                lblValuePreview.ForeColor = Color.Gray;
                return;
            }

            IntPtr? resolved = ResolveAddress(addrText, silent: true);
            if (!resolved.HasValue || _process == null || !_process.IsOpen)
            {
                lblValuePreview.Text = "=???";
                lblValuePreview.ForeColor = Color.Gray;
                return;
            }

            try
            {
                string value = ReadValueAtAddress(resolved.Value);
                lblValuePreview.Text = "=" + value;
                lblValuePreview.ForeColor = Color.LimeGreen;
            }
            catch
            {
                lblValuePreview.Text = "=???";
                lblValuePreview.ForeColor = Color.Red;
            }
        }

        private string ReadValueAtAddress(IntPtr address)
        {
            if (_process == null || !_process.IsOpen)
                return "???";

            string type = cmbType.SelectedItem?.ToString() ?? "4 Bytes";
            bool hex = chkHexadecimal.Checked;
            bool signed = chkSigned.Checked;

            try
            {
                switch (type)
                {
                    case "Byte":
                        byte b = _process.Read<byte>(address);
                        return hex ? b.ToString("X2") : (signed ? ((sbyte)b).ToString() : b.ToString());

                    case "2 Bytes":
                        ushort u16 = _process.Read<ushort>(address);
                        return hex ? u16.ToString("X4") : (signed ? ((short)u16).ToString() : u16.ToString());

                    case "4 Bytes":
                        uint u32 = _process.Read<uint>(address);
                        return hex ? u32.ToString("X8") : (signed ? ((int)u32).ToString() : u32.ToString());

                    case "8 Bytes":
                        ulong u64 = _process.Read<ulong>(address);
                        return hex ? u64.ToString("X16") : (signed ? ((long)u64).ToString() : u64.ToString());

                    case "Float":
                        float f = _process.Read<float>(address);
                        return f.ToString("G");

                    case "Double":
                        double d = _process.Read<double>(address);
                        return d.ToString("G");

                    case "String":
                        byte[] strBytes = _process.Read(address, 32) ?? Array.Empty<byte>();
                        int nullIdx = Array.IndexOf(strBytes, (byte)0);
                        if (nullIdx >= 0)
                            strBytes = strBytes.Take(nullIdx).ToArray();
                        return System.Text.Encoding.ASCII.GetString(strBytes);

                    case "Array of byte":
                        byte[] aob = _process.Read(address, 8) ?? Array.Empty<byte>();
                        return BitConverter.ToString(aob).Replace("-", " ");

                    default:
                        return "???";
                }
            }
            catch
            {
                return "???";
            }
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            string addrText = txtAddress.Text.Trim();

            // Try to parse the address
            IntPtr? resolvedAddress = ResolveAddress(addrText, silent: false);

            if (resolvedAddress.HasValue)
            {
                Address = resolvedAddress.Value;
                Description = txtDescription.Text == "No description" ? "" : txtDescription.Text;
                ValueType = cmbType.SelectedItem?.ToString() ?? "4 Bytes";
                IsHexadecimal = chkHexadecimal.Checked;
                IsSigned = chkSigned.Checked;
                // Preserve the original address string for module+offset support
                OriginalAddressString = addrText;
            }
            else
            {
                this.DialogResult = DialogResult.None;
            }
        }

        /// <summary>
        /// Resolve an address string to an IntPtr
        /// Supports: raw hex (7FF6A1234567), 0x prefix (0x12345), Module+Offset (game.exe+1234)
        /// </summary>
        private IntPtr? ResolveAddress(string addrText, bool silent = false)
        {
            if (string.IsNullOrWhiteSpace(addrText))
            {
                if (!silent)
                    MessageBox.Show("Please enter an address.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            // Check for Module+Offset format (e.g., "game.exe+1234" or "game+1234")
            if (addrText.Contains('+'))
            {
                return ResolveModuleOffset(addrText, isNegative: false, silent: silent);
            }

            // Check for Module-Offset format (e.g., "game.exe-1234") - rare but valid
            if (addrText.Contains('-') && !addrText.StartsWith("-"))
            {
                // Make sure it's not a negative hex number
                int dashIndex = addrText.IndexOf('-');
                if (dashIndex > 0)
                {
                    string beforeDash = addrText.Substring(0, dashIndex);
                    // If before dash contains letters that aren't hex, it's a module name
                    if (ContainsNonHexLetters(beforeDash))
                    {
                        return ResolveModuleOffset(addrText, isNegative: true, silent: silent);
                    }
                }
            }

            // Try parsing as raw hex address
            // Handle both 0x prefix and h suffix (assembly notation)
            string cleanAddr = addrText.Replace("0x", "").Replace("0X", "").Replace(" ", "");
            if (cleanAddr.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                cleanAddr = cleanAddr[..^1];

            if (long.TryParse(cleanAddr, NumberStyles.HexNumber, null, out long addr))
            {
                return new IntPtr(addr);
            }

            if (!silent)
            {
                MessageBox.Show(
                    "Invalid address format.\n\n" +
                    "Supported formats:\n" +
                    "  Hex address: 7FF6A1234567, 0x12345\n" +
                    "  Module+Offset: module.exe+1234, gunz+ABC",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return null;
        }

        /// <summary>
        /// Check if a string contains letters that aren't valid hex digits (A-F)
        /// </summary>
        private bool ContainsNonHexLetters(string text)
        {
            foreach (char c in text.ToUpperInvariant())
            {
                if (char.IsLetter(c) && (c < 'A' || c > 'F'))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolve Module+Offset format to absolute address
        /// </summary>
        private IntPtr? ResolveModuleOffset(string addrText, bool isNegative, bool silent)
        {
            char separator = isNegative ? '-' : '+';
            int sepIndex = addrText.IndexOf(separator);

            if (sepIndex <= 0 || sepIndex >= addrText.Length - 1)
            {
                if (!silent)
                    MessageBox.Show("Invalid module+offset format.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            string moduleName = addrText.Substring(0, sepIndex).Trim();
            string offsetStr = addrText.Substring(sepIndex + 1).Trim()
                .Replace("0x", "").Replace("0X", "");
            // Handle "h" suffix (assembly notation)
            if (offsetStr.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                offsetStr = offsetStr[..^1];

            // Parse the offset
            if (!long.TryParse(offsetStr, NumberStyles.HexNumber, null, out long offset))
            {
                if (!silent)
                    MessageBox.Show($"Invalid offset: {offsetStr}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            if (isNegative)
                offset = -offset;

            // If no process attached, we can't resolve module addresses
            if (_process == null || !_process.IsOpen)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        "Cannot resolve module address: No process attached.\n" +
                        "Please attach to a process first, or use a raw hex address.",
                        "No Process", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return null;
            }

            // Find the module
            var modules = _process.GetModules();
            ModuleInfo? foundModule = null;

            foreach (var module in modules)
            {
                // Match by exact name or partial match (without extension)
                string modNameLower = module.ModuleName.ToLowerInvariant();
                string searchLower = moduleName.ToLowerInvariant();

                if (modNameLower == searchLower ||
                    modNameLower == searchLower + ".exe" ||
                    modNameLower == searchLower + ".dll" ||
                    modNameLower.StartsWith(searchLower + "."))
                {
                    foundModule = module;
                    break;
                }
            }

            if (foundModule == null)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        $"Module '{moduleName}' not found in the target process.\n\n" +
                        "Make sure the module name is correct and the process is running.",
                        "Module Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return null;
            }

            // Calculate absolute address
            long absoluteAddr = foundModule.BaseAddress.ToInt64() + offset;
            return new IntPtr(absoluteAddr);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _previewTimer?.Stop();
            _previewTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
