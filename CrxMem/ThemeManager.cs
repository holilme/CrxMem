using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace CrxMem
{
    /// <summary>
    /// Manages application-wide theming (dark/light mode)
    /// </summary>
    public static class ThemeManager
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1000 + 54;
        private const int LVS_EX_DOUBLEBUFFER = 0x00010000;
        private const int LVS_EX_GRIDLINES = 0x00000001;
        // Dark theme colors (x64dbg/modern debugger inspired)
        public static class DarkTheme
        {
            public static readonly Color Background = Color.FromArgb(30, 30, 30);
            public static readonly Color BackgroundAlt = Color.FromArgb(45, 45, 48);
            public static readonly Color BackgroundPanel = Color.FromArgb(37, 37, 38);
            public static readonly Color Foreground = Color.FromArgb(220, 220, 220);
            public static readonly Color ForegroundDim = Color.FromArgb(150, 150, 150);
            public static readonly Color Accent = Color.FromArgb(0, 122, 204);
            public static readonly Color AccentLight = Color.FromArgb(28, 151, 234);
            public static readonly Color Border = Color.FromArgb(63, 63, 70);
            public static readonly Color Selection = Color.FromArgb(51, 51, 51);
            public static readonly Color SelectionHighlight = Color.FromArgb(0, 122, 204);
            public static readonly Color GridLines = Color.FromArgb(42, 42, 42); // Very subtle dark gridlines
            public static readonly Color HeaderBackground = Color.FromArgb(40, 40, 40); // Darker header
            public static readonly Color AccentHover = Color.FromArgb(20, 142, 224);
            public static readonly Color AccentPressed = Color.FromArgb(0, 102, 184);

            // Syntax highlighting colors for disassembler (bright on dark)
            public static readonly Color SyntaxMnemonic = Color.FromArgb(86, 156, 214);       // Blue
            public static readonly Color SyntaxRegister = Color.FromArgb(156, 220, 254);      // Light blue
            public static readonly Color SyntaxNumber = Color.FromArgb(181, 206, 168);        // Green
            public static readonly Color SyntaxAddress = Color.FromArgb(206, 145, 120);       // Orange/brown
            public static readonly Color SyntaxString = Color.FromArgb(214, 157, 133);        // Light orange
            public static readonly Color SyntaxComment = Color.FromArgb(87, 166, 74);         // Green
            public static readonly Color SyntaxJump = Color.FromArgb(100, 149, 237);          // Cornflower Blue
            public static readonly Color SyntaxCall = Color.FromArgb(78, 201, 176);           // Teal/Green
            public static readonly Color SyntaxConditional = Color.FromArgb(156, 220, 254);   // Cyan
            public static readonly Color SyntaxMemory = Color.FromArgb(78, 201, 176);         // Teal
            public static readonly Color SyntaxPrefix = Color.FromArgb(197, 134, 192);        // Purple

            // Status colors
            public static readonly Color StatusGreen = Color.FromArgb(78, 201, 176);
            public static readonly Color StatusRed = Color.FromArgb(244, 71, 71);
            public static readonly Color StatusYellow = Color.FromArgb(220, 220, 170);
        }

        // Light theme colors (classic)
        // Light theme colors (modern)
        public static class LightTheme
        {
            public static readonly Color Background = Color.FromArgb(252, 252, 252);     // Very light grey/off-white (App Background)
            public static readonly Color BackgroundAlt = Color.White;                      // Pure white (Input fields, Lists)
            public static readonly Color BackgroundPanel = Color.White;                    // Cards/Panels
            public static readonly Color HeaderBackground = Color.FromArgb(248, 248, 248);  // Very light off-white header
            public static readonly Color Foreground = Color.FromArgb(30, 30, 30);        // Soft Black
            public static readonly Color ForegroundDim = Color.FromArgb(100, 100, 100);  // Grey text
            public static readonly Color Accent = Color.FromArgb(0, 120, 215);           // Modern Windows Blue
            public static readonly Color AccentLight = Color.FromArgb(66, 165, 245);     // Lighter Blue
            public static readonly Color AccentHover = Color.FromArgb(30, 150, 245);
            public static readonly Color AccentPressed = Color.FromArgb(0, 100, 180);
            public static readonly Color Border = Color.FromArgb(224, 224, 224);         // Subtle border
            public static readonly Color Selection = Color.FromArgb(235, 245, 255);      // Very light blue selection
            public static readonly Color SelectionHighlight = Color.FromArgb(0, 120, 215);
            public static readonly Color GridLines = Color.FromArgb(240, 240, 240);

            // Syntax highlighting colors for disassembler (dark on light)
            public static readonly Color SyntaxMnemonic = Color.FromArgb(0, 51, 179);         // IntelliJ Blue
            public static readonly Color SyntaxRegister = Color.FromArgb(23, 80, 235);        // Brighter Blue
            public static readonly Color SyntaxNumber = Color.FromArgb(23, 161, 165);         // Teal
            public static readonly Color SyntaxAddress = Color.FromArgb(160, 160, 160);       // Grey
            public static readonly Color SyntaxString = Color.FromArgb(6, 125, 23);           // Green
            public static readonly Color SyntaxComment = Color.FromArgb(128, 128, 128);       // Grey comments
            public static readonly Color SyntaxJump = Color.FromArgb(0, 0, 180);              // Blue
            public static readonly Color SyntaxCall = Color.FromArgb(128, 0, 128);            // Purple
            public static readonly Color SyntaxConditional = Color.FromArgb(0, 0, 255);       // Blue
            public static readonly Color SyntaxMemory = Color.FromArgb(43, 145, 175);         // Teal
            public static readonly Color SyntaxPrefix = Color.FromArgb(128, 0, 128);          // Purple

            // Status colors
            public static readonly Color StatusGreen = Color.FromArgb(56, 142, 60);
            public static readonly Color StatusRed = Color.FromArgb(211, 47, 47);
            public static readonly Color StatusYellow = Color.FromArgb(245, 124, 0);
        }

        /// <summary>
        /// Current theme state
        /// </summary>
        public static bool IsDarkMode
        {
            get => SettingsManager.DarkMode;
            set
            {
                SettingsManager.DarkMode = value;
                SettingsManager.Save();
            }
        }

        // Quick access properties for current theme colors
        public static Color Background => IsDarkMode ? DarkTheme.Background : LightTheme.Background;
        public static Color BackgroundAlt => IsDarkMode ? DarkTheme.BackgroundAlt : LightTheme.BackgroundAlt;
        public static Color BackgroundPanel => IsDarkMode ? DarkTheme.BackgroundPanel : LightTheme.BackgroundPanel;
        public static Color HeaderBackground => IsDarkMode ? DarkTheme.HeaderBackground : LightTheme.HeaderBackground;
        public static Color Foreground => IsDarkMode ? DarkTheme.Foreground : LightTheme.Foreground;
        public static Color ForegroundDim => IsDarkMode ? DarkTheme.ForegroundDim : LightTheme.ForegroundDim;
        public static Color Accent => IsDarkMode ? DarkTheme.Accent : LightTheme.Accent;
        public static Color AccentHover => IsDarkMode ? DarkTheme.AccentHover : LightTheme.AccentHover;
        public static Color AccentPressed => IsDarkMode ? DarkTheme.AccentPressed : LightTheme.AccentPressed;
        public static Color Border => IsDarkMode ? DarkTheme.Border : LightTheme.Border;
        public static Color Selection => IsDarkMode ? DarkTheme.Selection : LightTheme.Selection;
        public static Color SelectionHighlight => IsDarkMode ? DarkTheme.SelectionHighlight : LightTheme.SelectionHighlight;
        public static Color GridLines => IsDarkMode ? DarkTheme.GridLines : LightTheme.GridLines;

        // Syntax colors
        public static Color SyntaxMnemonic => IsDarkMode ? DarkTheme.SyntaxMnemonic : LightTheme.SyntaxMnemonic;
        public static Color SyntaxRegister => IsDarkMode ? DarkTheme.SyntaxRegister : LightTheme.SyntaxRegister;
        public static Color SyntaxNumber => IsDarkMode ? DarkTheme.SyntaxNumber : LightTheme.SyntaxNumber;
        public static Color SyntaxAddress => IsDarkMode ? DarkTheme.SyntaxAddress : LightTheme.SyntaxAddress;
        public static Color SyntaxString => IsDarkMode ? DarkTheme.SyntaxString : LightTheme.SyntaxString;
        public static Color SyntaxComment => IsDarkMode ? DarkTheme.SyntaxComment : LightTheme.SyntaxComment;
        public static Color SyntaxJump => IsDarkMode ? DarkTheme.SyntaxJump : LightTheme.SyntaxJump;
        public static Color SyntaxCall => IsDarkMode ? DarkTheme.SyntaxCall : LightTheme.SyntaxCall;
        public static Color SyntaxConditional => IsDarkMode ? DarkTheme.SyntaxConditional : LightTheme.SyntaxConditional;
        public static Color SyntaxMemory => IsDarkMode ? DarkTheme.SyntaxMemory : LightTheme.SyntaxMemory;
        public static Color SyntaxPrefix => IsDarkMode ? DarkTheme.SyntaxPrefix : LightTheme.SyntaxPrefix;

        // Status colors
        public static Color StatusGreen => IsDarkMode ? DarkTheme.StatusGreen : LightTheme.StatusGreen;
        public static Color StatusRed => IsDarkMode ? DarkTheme.StatusRed : LightTheme.StatusRed;
        public static Color StatusYellow => IsDarkMode ? DarkTheme.StatusYellow : LightTheme.StatusYellow;

        /// <summary>
        /// Apply theme to a form and all its controls recursively
        /// </summary>
        public static void ApplyTheme(Control control)
        {
            ApplyThemeToControl(control);
            
            // Skip recursion for custom controls that handle their own internal theming
            // to avoid resetting internal sub-control properties (like BorderStyle)
            if (control is CrxMem.Controls.ModernTextBox || control is CrxMem.Controls.ModernButton)
                return;

            foreach (Control child in control.Controls)
            {
                ApplyTheme(child);
            }
        }

        private static void ApplyThemeToControl(Control control)
        {
            // Skip certain control types that handle their own theming
            if (control is PictureBox) return;

            // Apply base colors
            control.BackColor = GetBackColorForControl(control);
            control.ForeColor = Foreground;

            // Handle specific control types (order matters - more specific types first!)
            switch (control)
            {
                case CrxMem.Controls.ModernButton:
                case CrxMem.Controls.ModernProgressBar:
                    // These controls handle their own theming via OnPaint
                    break;

                case Form form:
                    form.BackColor = Background;
                    if (IsDarkMode)
                    {
                        int darkMode = 1;
                        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                    }
                    break;

                // StatusStrip and MenuStrip inherit from ToolStrip, so check them BEFORE ToolStrip
                case StatusStrip statusStrip:
                    statusStrip.BackColor = BackgroundAlt;
                    statusStrip.ForeColor = Foreground;
                    statusStrip.Renderer = IsDarkMode ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
                    break;

                case MenuStrip menu:
                    menu.BackColor = BackgroundAlt;
                    menu.ForeColor = Foreground;
                    menu.Renderer = IsDarkMode ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
                    break;

                case ToolStrip toolStrip:
                    toolStrip.BackColor = BackgroundAlt;
                    toolStrip.ForeColor = Foreground;
                    toolStrip.Renderer = IsDarkMode ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
                    break;

                case ListView listView:
                    listView.BackColor = IsDarkMode ? DarkTheme.BackgroundAlt : Color.White;
                    listView.ForeColor = Foreground;
                    listView.BorderStyle = BorderStyle.FixedSingle;
                    listView.GridLines = false; // Disable invasive native gridlines
                    
                    // Apply Explorer theme for more modern look and subtle gridlines
                    if (listView.IsHandleCreated)
                    {
                        SetWindowTheme(listView.Handle, "Explorer", null);
                        // Enable double buffering via SendMessage to prevent flickering when resizing/scrolling
                        SendMessage(listView.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, LVS_EX_DOUBLEBUFFER, LVS_EX_DOUBLEBUFFER);
                    }
                    else
                    {
                        listView.HandleCreated += (s, e) =>
                        {
                            SetWindowTheme(listView.Handle, "Explorer", null);
                            SendMessage(listView.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, LVS_EX_DOUBLEBUFFER, LVS_EX_DOUBLEBUFFER);
                        };
                    }
                    break;

                case TextBox textBox:
                    textBox.BackColor = IsDarkMode ? DarkTheme.BackgroundAlt : Color.White;
                    textBox.ForeColor = Foreground;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case ComboBox comboBox:
                    comboBox.BackColor = IsDarkMode ? DarkTheme.BackgroundAlt : Color.White;
                    comboBox.ForeColor = Foreground;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    break;

                case Button button:
                    button.BackColor = IsDarkMode ? DarkTheme.BackgroundAlt : SystemColors.Control;
                    button.ForeColor = Foreground;
                    button.FlatStyle = IsDarkMode ? FlatStyle.Flat : FlatStyle.Standard;
                    if (IsDarkMode)
                    {
                        button.FlatAppearance.BorderColor = Border;
                    }
                    break;

                case GroupBox groupBox:
                    groupBox.BackColor = BackgroundPanel;
                    groupBox.ForeColor = Foreground;
                    break;

                case SplitContainer splitContainer:
                    splitContainer.BackColor = Background;
                    splitContainer.Panel1.BackColor = Background;
                    splitContainer.Panel2.BackColor = Background;
                    break;

                case CheckBox checkBox:
                    checkBox.BackColor = Color.Transparent;
                    checkBox.ForeColor = Foreground;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = Foreground;
                    break;

                case ProgressBar:
                    // ProgressBar doesn't support custom colors well in WinForms
                    break;

                case TabControl tabControl:
                    tabControl.BackColor = Background;
                    break;

                // TabPage inherits from Panel, so check BEFORE Panel
                case TabPage tabPage:
                    tabPage.BackColor = Background;
                    tabPage.ForeColor = Foreground;
                    break;

                case Panel panel:
                    panel.BackColor = BackgroundPanel;
                    break;
            }
        }

        private static Color GetBackColorForControl(Control control)
        {
            // Order matters - more specific types first!
            return control switch
            {
                Form => Background,
                TabPage => Background, // TabPage before Panel
                GroupBox => BackgroundPanel,
                Panel => BackgroundPanel,
                ListView => IsDarkMode ? DarkTheme.BackgroundAlt : Color.White,
                TextBox => IsDarkMode ? DarkTheme.BackgroundAlt : Color.White,
                ComboBox => IsDarkMode ? DarkTheme.BackgroundAlt : Color.White,
                _ => Background
            };
        }
    }

    /// <summary>
    /// Custom renderer for dark mode menus and toolstrips
    /// </summary>
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = ThemeManager.Foreground;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Don't draw the default border
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(ThemeManager.Border);
            int y = e.Item.ContentRectangle.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }
    }

    /// <summary>
    /// Color table for dark mode menu/toolbar rendering
    /// </summary>
    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => ThemeManager.Border;
        public override Color MenuItemBorder => ThemeManager.Accent;
        public override Color MenuItemSelected => ThemeManager.Selection;
        public override Color MenuItemSelectedGradientBegin => ThemeManager.Selection;
        public override Color MenuItemSelectedGradientEnd => ThemeManager.Selection;
        public override Color MenuItemPressedGradientBegin => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color MenuItemPressedGradientEnd => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color MenuStripGradientBegin => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color MenuStripGradientEnd => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color ToolStripDropDownBackground => ThemeManager.DarkTheme.Background;
        public override Color ImageMarginGradientBegin => ThemeManager.DarkTheme.Background;
        public override Color ImageMarginGradientEnd => ThemeManager.DarkTheme.Background;
        public override Color ImageMarginGradientMiddle => ThemeManager.DarkTheme.Background;
        public override Color SeparatorDark => ThemeManager.Border;
        public override Color SeparatorLight => ThemeManager.Border;
        public override Color StatusStripGradientBegin => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color StatusStripGradientEnd => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color ToolStripBorder => ThemeManager.Border;
        public override Color ToolStripContentPanelGradientBegin => ThemeManager.DarkTheme.Background;
        public override Color ToolStripContentPanelGradientEnd => ThemeManager.DarkTheme.Background;
        public override Color ToolStripGradientBegin => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color ToolStripGradientEnd => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color ToolStripGradientMiddle => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color ButtonSelectedHighlight => ThemeManager.Selection;
        public override Color ButtonSelectedGradientBegin => ThemeManager.Selection;
        public override Color ButtonSelectedGradientEnd => ThemeManager.Selection;
        public override Color ButtonPressedGradientBegin => ThemeManager.Accent;
        public override Color ButtonPressedGradientEnd => ThemeManager.Accent;
        public override Color CheckBackground => ThemeManager.DarkTheme.BackgroundAlt;
        public override Color CheckSelectedBackground => ThemeManager.Accent;
    }
}
