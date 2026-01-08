using System;
using System.Drawing;
using System.Windows.Forms;

namespace CrxMem
{
    public class EditAddressDialog : Form
    {
        private TextBox txtAddress;
        private TextBox txtValue;
        private TextBox txtDescription;
        private Button btnOK;
        private Button btnCancel;

        public string NewValue { get; private set; } = "";
        public string Description { get; private set; } = "";
        public string Address { get; private set; } = "";

        public EditAddressDialog(IntPtr address, string currentValue, string description)
            : this($"{address.ToInt64():X8}", currentValue, description, Color.Black)
        {
        }

        public EditAddressDialog(string addressDisplay, string currentValue, string description, Color addressColor, bool addressEditable = false)
        {
            InitializeComponents(addressDisplay, currentValue, description, addressColor, addressEditable);
        }

        private void InitializeComponents(string addressDisplay, string currentValue, string description, Color addressColor, bool addressEditable)
        {
            this.Text = "Change Value";
            this.Size = new Size(350, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lblAddressTitle = new Label
            {
                Text = "Address:",
                Location = new Point(15, 20),
                Size = new Size(80, 20)
            };

            txtAddress = new TextBox
            {
                Text = addressDisplay,
                Location = new Point(100, 17),
                Size = new Size(220, 23),
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = addressColor,
                ReadOnly = !addressEditable,
                BackColor = addressEditable ? Color.White : SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = addressEditable
            };
            // Allow user to select and copy the address
            txtAddress.Enter += (s, e) => txtAddress.SelectAll();

            Label lblValue = new Label
            {
                Text = "New Value:",
                Location = new Point(15, 55),
                Size = new Size(80, 20)
            };

            txtValue = new TextBox
            {
                Location = new Point(100, 52),
                Size = new Size(220, 23),
                Text = currentValue,
                Font = new Font("Consolas", 9F)
            };
            txtValue.SelectAll();

            Label lblDesc = new Label
            {
                Text = "Description:",
                Location = new Point(15, 90),
                Size = new Size(80, 20)
            };

            txtDescription = new TextBox
            {
                Location = new Point(100, 87),
                Size = new Size(220, 23),
                Text = description
            };

            btnOK = new Button
            {
                Text = "OK",
                Location = new Point(150, 125),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };
            btnOK.Click += (s, e) =>
            {
                Address = txtAddress.Text;
                NewValue = txtValue.Text;
                Description = txtDescription.Text;
            };

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(240, 125),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[] {
                lblAddressTitle, txtAddress, lblValue, txtValue,
                lblDesc, txtDescription, btnOK, btnCancel
            });

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }
    }
}
