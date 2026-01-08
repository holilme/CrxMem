using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CrxMem.Controls
{
    public class ModernButton : Button
    {
        private int _borderRadius = 4;
        public int BorderRadius
        {
            get => _borderRadius;
            set { _borderRadius = value; Invalidate(); }
        }

        private bool _isPrimary = false;
        public bool IsPrimary
        {
            get => _isPrimary;
            set { _isPrimary = value; Invalidate(); }
        }

        private bool _isAccent = false;
        public bool IsAccent
        {
            get => _isAccent;
            set { _isAccent = value; Invalidate(); }
        }

        public ModernButton()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.Size = new Size(100, 30);
            this.BackColor = Color.White;
            this.ForeColor = Color.FromArgb(30, 30, 30);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Color backColor;
            Color foreColor;
            Color borderColor;

            // Define modern palette
            Color primaryBlue = Color.FromArgb(0, 120, 215);
            Color accentBlue = Color.FromArgb(0, 120, 215); // User likes the Memory View color
            Color secondaryWhite = Color.White;
            Color borderGrey = Color.FromArgb(210, 210, 210);
            Color textDark = Color.FromArgb(30, 30, 30);

            if (ThemeManager.IsDarkMode) 
            {
                if (_isPrimary || _isAccent)
                {
                    backColor = ThemeManager.Accent;
                    foreColor = Color.White;
                    borderColor = ThemeManager.Accent;
                }
                else
                {
                    backColor = ThemeManager.DarkTheme.BackgroundAlt;
                    foreColor = ThemeManager.DarkTheme.Foreground;
                    borderColor = ThemeManager.DarkTheme.Border;
                }
            }
            else
            {
                // Light Theme
                if (_isPrimary || _isAccent)
                {
                    backColor = accentBlue;
                    foreColor = Color.White;
                    borderColor = accentBlue;
                }
                else
                {
                    backColor = secondaryWhite;
                    foreColor = textDark;
                    borderColor = borderGrey;
                }
            }

            // Interactive States - More subtle
            if (!this.Enabled)
            {
                backColor = ThemeManager.IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(240, 240, 240);
                foreColor = Color.FromArgb(150, 150, 150);
                borderColor = backColor;
            }
            else
            {
                Point mousePos = this.PointToClient(Cursor.Position);
                if (this.ClientRectangle.Contains(mousePos))
                {
                    if (_isPrimary || _isAccent)
                    {
                        backColor = Color.FromArgb(backColor.R + 20 >= 255 ? 255 : backColor.R + 20, 
                                                   backColor.G + 20 >= 255 ? 255 : backColor.G + 20, 
                                                   backColor.B + 20 >= 255 ? 255 : backColor.B + 20);
                    }
                    else
                    {
                        backColor = Color.FromArgb(245, 248, 255); // Very subtle light blue tint
                        borderColor = Color.FromArgb(0, 120, 215);
                    }

                    if ((MouseButtons & MouseButtons.Left) != 0)
                    {
                        if (_isPrimary || _isAccent)
                            backColor = ControlPaint.Dark(accentBlue, 0.1f);
                        else
                            backColor = Color.FromArgb(235, 235, 235);
                    }
                }
            }

            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.Clear(this.Parent?.BackColor ?? Color.White);

            using (GraphicsPath path = GetRoundedPath(new Rectangle(0, 0, this.Width - 1, this.Height - 1), _borderRadius))
            using (SolidBrush brush = new SolidBrush(backColor))
            using (Pen pen = new Pen(borderColor, 1))
            {
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                pevent.Graphics, 
                this.Text, 
                this.Font, 
                this.ClientRectangle, 
                foreColor, 
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius;
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class ModernProgressBar : Control
    {
        private int _value = 0;
        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(0, Math.Min(value, Maximum));
                Invalidate();
            }
        }

        public int Maximum { get; set; } = 100;

        public ModernProgressBar()
        {
            this.SetStyle(ControlStyles.DoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            this.Height = 10;
            this.BackColor = Color.FromArgb(230, 230, 230); // Track color
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color trackColor = ThemeManager.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.FromArgb(230, 230, 230);
            Color barColor = ThemeManager.Accent; // Use global accent

            e.Graphics.SmoothingMode = SmoothingMode.None; // Flat style
            e.Graphics.Clear(this.Parent?.BackColor ?? Color.White);

            // Draw Track
            using (SolidBrush b = new SolidBrush(trackColor))
            {
                e.Graphics.FillRectangle(b, this.ClientRectangle);
            }

            // Draw Bar
            if (Maximum > 0 && Value > 0)
            {
                int width = (int)((float)Value / Maximum * this.Width);
                using (SolidBrush b = new SolidBrush(barColor))
                {
                    e.Graphics.FillRectangle(b, 0, 0, width, this.Height);
                }
            }
        }
    }

    public class ModernTextBox : UserControl
    {
        private TextBox _textBox;
        private Color _borderColor = Color.FromArgb(200, 200, 200);
        private Color _borderFocusColor = ThemeManager.Accent;
        private bool _isFocused = false;

        public override string Text
        {
            get => _textBox.Text;
            set => _textBox.Text = value;
        }

        public new event EventHandler TextChanged
        {
            add => _textBox.TextChanged += value;
            remove => _textBox.TextChanged -= value;
        }
        
        public new event KeyPressEventHandler KeyPress {
             add => _textBox.KeyPress += value;
             remove => _textBox.KeyPress -= value;
        }

        public ModernTextBox()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.BackColor = SystemColors.Window;
            this.Padding = new Padding(7);
            this.Size = new Size(200, 30);
            
            _textBox = new TextBox();
            _textBox.BorderStyle = BorderStyle.None;
            _textBox.Dock = DockStyle.Fill;
            _textBox.BackColor = this.BackColor;
            _textBox.ForeColor = this.ForeColor;
            // _textBox.Font = this.Font; // Will be set in OnFontChanged

            _textBox.Enter += (s, e) => { _isFocused = true; this.Invalidate(); };
            _textBox.Leave += (s, e) => { _isFocused = false; this.Invalidate(); };
            
            this.Controls.Add(_textBox);
            this.Cursor = Cursors.IBeam;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _textBox.Font = this.Font;
            // Accessing Height on a UserControl often doesn't autosize like a TextBox, so we might want to let the inner textbox drive it, 
            // but for now, we just ensure font propagates.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
             base.OnPaint(e);
             e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

             // Resolve colors
            Color borderColor = _isFocused ? Color.FromArgb(0, 120, 215) : Color.FromArgb(200, 200, 200); // Blue focus, Grey default
            Color backColor = Color.White; // Force white in light mode
            Color foreColor = Color.Black;

            if (ThemeManager.IsDarkMode)
            {
                borderColor = _isFocused ? ThemeManager.Accent : ThemeManager.Border;
                backColor = ThemeManager.DarkTheme.BackgroundAlt;
                foreColor = ThemeManager.Foreground;
            }
            
            this.BackColor = backColor;
            _textBox.BackColor = backColor;
            _textBox.ForeColor = foreColor;

             // Draw Border
             using (Pen pen = new Pen(borderColor, 1))
             {
                 Rectangle rect = this.ClientRectangle;
                 rect.Width -= 1;
                 rect.Height -= 1;
                 e.Graphics.DrawRectangle(pen, rect);
             }
        }
        
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
             // Ensure the height accommodates the textbox + padding if we wanted to enforce it, 
             // but user might want to resize manually.
        }
        
        protected override void OnClick(EventArgs e)
        {
            _textBox.Focus();
        }
    }
}
