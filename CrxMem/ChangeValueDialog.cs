using System;
using System.Drawing;
using System.Windows.Forms;

namespace CrxMem
{
    public partial class ChangeValueDialog : Form
    {
        private TextBox txtValue;
        private Button btnOK;
        private Button btnCancel;
        private Button btnPlus1;
        private Button btnMinus1;
        private Button btnPlus10;
        private Button btnMinus10;
        private Button btnPlus100;
        private Button btnMinus100;
        private Button btnZero;
        private Button btnMax;
        private Label lblInstruction;

        public string NewValue { get; private set; } = "";
        private string _currentValue;
        private string _valueType;

        public ChangeValueDialog(string currentValue, string valueType)
        {
            _currentValue = currentValue;
            _valueType = valueType;
            InitializeComponent();
            txtValue.Text = currentValue;
        }

        private void InitializeComponent()
        {
            this.Text = "Change Value";
            this.Size = new Size(350, 250);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            lblInstruction = new Label
            {
                Text = "Enter new value or use preset buttons:",
                Location = new Point(10, 10),
                Size = new Size(320, 20)
            };

            txtValue = new TextBox
            {
                Location = new Point(10, 35),
                Size = new Size(320, 25)
            };

            // Preset buttons
            btnPlus1 = new Button { Text = "+1", Location = new Point(10, 70), Size = new Size(70, 30) };
            btnMinus1 = new Button { Text = "-1", Location = new Point(90, 70), Size = new Size(70, 30) };
            btnPlus10 = new Button { Text = "+10", Location = new Point(170, 70), Size = new Size(70, 30) };
            btnMinus10 = new Button { Text = "-10", Location = new Point(250, 70), Size = new Size(70, 30) };

            btnPlus100 = new Button { Text = "+100", Location = new Point(10, 110), Size = new Size(70, 30) };
            btnMinus100 = new Button { Text = "-100", Location = new Point(90, 110), Size = new Size(70, 30) };
            btnZero = new Button { Text = "0", Location = new Point(170, 110), Size = new Size(70, 30) };
            btnMax = new Button { Text = "Max", Location = new Point(250, 110), Size = new Size(70, 30) };

            btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(170, 160),
                Size = new Size(75, 30)
            };

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(255, 160),
                Size = new Size(75, 30)
            };

            // Wire up preset button events
            btnPlus1.Click += (s, e) => ApplyDelta(1);
            btnMinus1.Click += (s, e) => ApplyDelta(-1);
            btnPlus10.Click += (s, e) => ApplyDelta(10);
            btnMinus10.Click += (s, e) => ApplyDelta(-10);
            btnPlus100.Click += (s, e) => ApplyDelta(100);
            btnMinus100.Click += (s, e) => ApplyDelta(-100);
            btnZero.Click += (s, e) => txtValue.Text = "0";
            btnMax.Click += (s, e) => SetMaxValue();

            btnOK.Click += (s, e) => { NewValue = txtValue.Text; };

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            Controls.Add(lblInstruction);
            Controls.Add(txtValue);
            Controls.Add(btnPlus1);
            Controls.Add(btnMinus1);
            Controls.Add(btnPlus10);
            Controls.Add(btnMinus10);
            Controls.Add(btnPlus100);
            Controls.Add(btnMinus100);
            Controls.Add(btnZero);
            Controls.Add(btnMax);
            Controls.Add(btnOK);
            Controls.Add(btnCancel);
        }

        private void ApplyDelta(int delta)
        {
            try
            {
                switch (_valueType)
                {
                    case "Byte":
                        if (byte.TryParse(txtValue.Text, out byte b))
                            txtValue.Text = Math.Max(0, Math.Min(255, b + delta)).ToString();
                        break;
                    case "2 Bytes":
                        if (short.TryParse(txtValue.Text, out short s))
                            txtValue.Text = ((short)(s + delta)).ToString();
                        break;
                    case "4 Bytes":
                        if (int.TryParse(txtValue.Text, out int i))
                            txtValue.Text = (i + delta).ToString();
                        break;
                    case "8 Bytes":
                        if (long.TryParse(txtValue.Text, out long l))
                            txtValue.Text = (l + delta).ToString();
                        break;
                    case "Float":
                        if (float.TryParse(txtValue.Text, out float f))
                            txtValue.Text = (f + delta).ToString();
                        break;
                    case "Double":
                        if (double.TryParse(txtValue.Text, out double d))
                            txtValue.Text = (d + delta).ToString();
                        break;
                }
            }
            catch { }
        }

        private void SetMaxValue()
        {
            switch (_valueType)
            {
                case "Byte":
                    txtValue.Text = "255";
                    break;
                case "2 Bytes":
                    txtValue.Text = short.MaxValue.ToString();
                    break;
                case "4 Bytes":
                    txtValue.Text = int.MaxValue.ToString();
                    break;
                case "8 Bytes":
                    txtValue.Text = long.MaxValue.ToString();
                    break;
                case "Float":
                    txtValue.Text = float.MaxValue.ToString();
                    break;
                case "Double":
                    txtValue.Text = double.MaxValue.ToString();
                    break;
            }
        }
    }
}
