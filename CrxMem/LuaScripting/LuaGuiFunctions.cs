using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NLua;

namespace CrxMem.LuaScripting
{
    /// <summary>
    /// GUI creation functions for Lua scripts.
    /// Allows scripts to create WinForms windows and controls.
    /// Matches CheatEngine's GUI API.
    /// </summary>
    public class LuaGuiFunctions : IDisposable
    {
        private readonly LuaEngine _engine;
        private readonly List<Form> _createdForms = new();
        private readonly List<System.Windows.Forms.Timer> _createdTimers = new();

        public LuaGuiFunctions(LuaEngine engine)
        {
            _engine = engine;
        }

        public void Register(Lua lua)
        {
            // Form creation
            lua.RegisterFunction("createForm", this, GetType().GetMethod(nameof(CreateForm)));

            // Control creation
            lua.RegisterFunction("createLabel", this, GetType().GetMethod(nameof(CreateLabel)));
            lua.RegisterFunction("createEdit", this, GetType().GetMethod(nameof(CreateEdit)));
            lua.RegisterFunction("createMemo", this, GetType().GetMethod(nameof(CreateMemo)));
            lua.RegisterFunction("createButton", this, GetType().GetMethod(nameof(CreateButton)));
            lua.RegisterFunction("createCheckBox", this, GetType().GetMethod(nameof(CreateCheckBox)));
            lua.RegisterFunction("createComboBox", this, GetType().GetMethod(nameof(CreateComboBox)));
            lua.RegisterFunction("createListBox", this, GetType().GetMethod(nameof(CreateListBox)));
            lua.RegisterFunction("createPanel", this, GetType().GetMethod(nameof(CreatePanel)));
            lua.RegisterFunction("createImage", this, GetType().GetMethod(nameof(CreateImage)));
            lua.RegisterFunction("createTimer", this, GetType().GetMethod(nameof(CreateTimer)));
            lua.RegisterFunction("createProgressBar", this, GetType().GetMethod(nameof(CreateProgressBar)));
            lua.RegisterFunction("createTrackBar", this, GetType().GetMethod(nameof(CreateTrackBar)));
        }

        // ═══════════════════════════════════════════════════════════════
        // FORM CREATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create a new form window.
        /// </summary>
        public LuaForm CreateForm()
        {
            LuaForm? form = null;

            InvokeOnUIThread(() =>
            {
                form = new LuaForm();
                _createdForms.Add(form);
            });

            return form!;
        }

        // ═══════════════════════════════════════════════════════════════
        // CONTROL CREATION
        // ═══════════════════════════════════════════════════════════════

        public LuaLabel CreateLabel(LuaForm parent)
        {
            LuaLabel? label = null;
            InvokeOnUIThread(() =>
            {
                label = new LuaLabel();
                parent.AddControl(label);
            });
            return label!;
        }

        public LuaEdit CreateEdit(LuaForm parent)
        {
            LuaEdit? edit = null;
            InvokeOnUIThread(() =>
            {
                edit = new LuaEdit();
                parent.AddControl(edit);
            });
            return edit!;
        }

        public LuaMemo CreateMemo(LuaForm parent)
        {
            LuaMemo? memo = null;
            InvokeOnUIThread(() =>
            {
                memo = new LuaMemo();
                parent.AddControl(memo);
            });
            return memo!;
        }

        public LuaButton CreateButton(LuaForm parent)
        {
            LuaButton? button = null;
            InvokeOnUIThread(() =>
            {
                button = new LuaButton();
                parent.AddControl(button);
            });
            return button!;
        }

        public LuaCheckBox CreateCheckBox(LuaForm parent)
        {
            LuaCheckBox? checkbox = null;
            InvokeOnUIThread(() =>
            {
                checkbox = new LuaCheckBox();
                parent.AddControl(checkbox);
            });
            return checkbox!;
        }

        public LuaComboBox CreateComboBox(LuaForm parent)
        {
            LuaComboBox? combobox = null;
            InvokeOnUIThread(() =>
            {
                combobox = new LuaComboBox();
                parent.AddControl(combobox);
            });
            return combobox!;
        }

        public LuaListBox CreateListBox(LuaForm parent)
        {
            LuaListBox? listbox = null;
            InvokeOnUIThread(() =>
            {
                listbox = new LuaListBox();
                parent.AddControl(listbox);
            });
            return listbox!;
        }

        public LuaPanel CreatePanel(LuaForm parent)
        {
            LuaPanel? panel = null;
            InvokeOnUIThread(() =>
            {
                panel = new LuaPanel();
                parent.AddControl(panel);
            });
            return panel!;
        }

        public LuaImage CreateImage(LuaForm parent)
        {
            LuaImage? image = null;
            InvokeOnUIThread(() =>
            {
                image = new LuaImage();
                parent.AddControl(image);
            });
            return image!;
        }

        public LuaTimer CreateTimer(int interval = 1000)
        {
            var timer = new LuaTimer(interval);
            _createdTimers.Add(timer.InternalTimer);
            return timer;
        }

        public LuaProgressBar CreateProgressBar(LuaForm parent)
        {
            LuaProgressBar? progressBar = null;
            InvokeOnUIThread(() =>
            {
                progressBar = new LuaProgressBar();
                parent.AddControl(progressBar);
            });
            return progressBar!;
        }

        public LuaTrackBar CreateTrackBar(LuaForm parent)
        {
            LuaTrackBar? trackBar = null;
            InvokeOnUIThread(() =>
            {
                trackBar = new LuaTrackBar();
                parent.AddControl(trackBar);
            });
            return trackBar!;
        }

        private void InvokeOnUIThread(Action action)
        {
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].Invoke(action);
            }
            else
            {
                action();
            }
        }

        public void Dispose()
        {
            // Clean up all created forms and timers
            foreach (var timer in _createdTimers)
            {
                timer.Stop();
                timer.Dispose();
            }
            _createdTimers.Clear();

            foreach (var form in _createdForms)
            {
                if (!form.IsDisposed)
                {
                    if (form.InvokeRequired)
                    {
                        form.Invoke(new Action(() => form.Close()));
                    }
                    else
                    {
                        form.Close();
                    }
                }
            }
            _createdForms.Clear();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // LUA FONT WRAPPER
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lua-accessible Font wrapper for CheatEngine compatibility.
    /// Allows scripts to set Font.Size, Font.Color, Font.Style on controls.
    /// </summary>
    public class LuaFontWrapper
    {
        private readonly Control _control;

        public LuaFontWrapper(Control control)
        {
            _control = control;
        }

        public float Size
        {
            get => _control.Font.Size;
            set => _control.Font = new System.Drawing.Font(_control.Font.FontFamily, value, _control.Font.Style);
        }

        public int Color
        {
            get => _control.ForeColor.ToArgb() & 0xFFFFFF;
            set => _control.ForeColor = System.Drawing.Color.FromArgb(value);
        }

        public string Style
        {
            get => _control.Font.Style.ToString();
            set
            {
                var style = FontStyle.Regular;
                if (value.Contains("fsBold")) style |= FontStyle.Bold;
                if (value.Contains("fsItalic")) style |= FontStyle.Italic;
                if (value.Contains("fsUnderline")) style |= FontStyle.Underline;
                _control.Font = new System.Drawing.Font(_control.Font.FontFamily, _control.Font.Size, style);
            }
        }

        public string Name
        {
            get => _control.Font.Name;
            set => _control.Font = new System.Drawing.Font(value, _control.Font.Size, _control.Font.Style);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // LUA WRAPPER CLASSES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lua-accessible Form wrapper.
    /// Supports both method-style (setCaption) and property-style (Caption) access for CE compatibility.
    /// </summary>
    public class LuaForm : Form
    {
        public LuaFunction? onClose;
        public LuaFunction? onShow;
        public LuaFunction? OnClose { get => onClose; set => onClose = value; }

        public LuaForm()
        {
            Text = "Lua Form";
            Size = new Size(400, 300);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true; // Ensure form appears on top
            FormClosing += (s, e) => onClose?.Call();
            Shown += (s, e) => onShow?.Call();
        }

        // Property-style access (CheatEngine compatible)
        public string Caption { get => Text; set => Text = value; }

        // Position property - accepts both string ("poScreenCenter") and int (4) values
        public object Position
        {
            get => (int)StartPosition;
            set
            {
                if (value is string s)
                {
                    // String-based (legacy)
                    if (s == "poScreenCenter") StartPosition = FormStartPosition.CenterScreen;
                    else if (s == "poDesktopCenter") StartPosition = FormStartPosition.CenterScreen;
                    else if (s == "poDefault") StartPosition = FormStartPosition.WindowsDefaultLocation;
                    else if (s == "poDefaultPosOnly") StartPosition = FormStartPosition.WindowsDefaultLocation;
                    else if (s == "poDefaultSizeOnly") StartPosition = FormStartPosition.WindowsDefaultBounds;
                    else if (s == "poMainFormCenter" || s == "poOwnerFormCenter") StartPosition = FormStartPosition.CenterParent;
                }
                else if (value is int i)
                {
                    ApplyPositionInt(i);
                }
                else if (value is long l)
                {
                    ApplyPositionInt((int)l);
                }
            }
        }

        private void ApplyPositionInt(int pos)
        {
            StartPosition = pos switch
            {
                0 => FormStartPosition.Manual,           // poDesigned
                1 => FormStartPosition.WindowsDefaultLocation, // poDefault
                2 => FormStartPosition.WindowsDefaultLocation, // poDefaultPosOnly
                3 => FormStartPosition.WindowsDefaultBounds,   // poDefaultSizeOnly
                4 => FormStartPosition.CenterScreen,     // poScreenCenter
                5 => FormStartPosition.CenterScreen,     // poDesktopCenter
                6 => FormStartPosition.CenterParent,     // poMainFormCenter
                7 => FormStartPosition.CenterParent,     // poOwnerFormCenter
                _ => FormStartPosition.CenterScreen
            };
        }

        // BorderStyle property - accepts both string ("bsSingle") and int (1) values
        public new object BorderStyle
        {
            get => (int)FormBorderStyle;
            set
            {
                if (value is string s)
                {
                    // String-based (legacy)
                    if (s == "bsSingle" || s == "bsDialog")
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
                    else if (s == "bsNone")
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                    else if (s == "bsSizeable")
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
                    else if (s == "bsToolWindow")
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
                    else if (s == "bsSizeToolWin")
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
                }
                else if (value is int i)
                {
                    ApplyBorderStyleInt(i);
                }
                else if (value is long l)
                {
                    ApplyBorderStyleInt((int)l);
                }
            }
        }

        private void ApplyBorderStyleInt(int bs)
        {
            FormBorderStyle = bs switch
            {
                0 => System.Windows.Forms.FormBorderStyle.None,           // bsNone
                1 => System.Windows.Forms.FormBorderStyle.FixedSingle,    // bsSingle
                2 => System.Windows.Forms.FormBorderStyle.Sizable,        // bsSizeable
                3 => System.Windows.Forms.FormBorderStyle.FixedDialog,    // bsDialog
                4 => System.Windows.Forms.FormBorderStyle.FixedToolWindow,// bsToolWindow
                5 => System.Windows.Forms.FormBorderStyle.SizableToolWindow, // bsSizeToolWin
                _ => System.Windows.Forms.FormBorderStyle.Sizable
            };
        }

        // Method-style access
        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void centerScreen() => StartPosition = FormStartPosition.CenterScreen;

        public new void show()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => { Show(); BringToFront(); Activate(); }));
            }
            else
            {
                Show();
                BringToFront();
                Activate();
            }
        }

        public new void hide()
        {
            if (InvokeRequired)
                Invoke(new Action(() => Hide()));
            else
                Hide();
        }

        public new void close()
        {
            if (InvokeRequired)
                Invoke(new Action(() => Close()));
            else
                Close();
        }

        public void setVisible(bool visible) => Visible = visible;
        public bool getVisible() => Visible;
        public void Show_() => show(); // Alias for CE scripts using form.Show()

        public void AddControl(Control control)
        {
            if (InvokeRequired)
                Invoke(new Action(() => Controls.Add(control)));
            else
                Controls.Add(control);
        }
    }

    /// <summary>
    /// Base class for Lua-accessible controls.
    /// </summary>
    public abstract class LuaControlBase : Control
    {
        public LuaFunction? onClick;
        public LuaFunction? OnClick { get => onClick; set => onClick = value; }

        protected LuaControlBase()
        {
            Click += (s, e) => onClick?.Call(this);
        }

        // Property-style access (CheatEngine compatible)
        public string Caption { get => Text; set => Text = value; }

        // Method-style access
        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public bool getVisible() => Visible;
        public void setEnabled(bool enabled) => Enabled = enabled;
        public bool getEnabled() => Enabled;
    }

    public class LuaLabel : Label
    {
        public LuaFunction? onClick;
        public LuaFunction? OnClick { get => onClick; set => onClick = value; }
        private LuaFontWrapper? _font;

        public LuaLabel()
        {
            AutoSize = true;
            Click += (s, e) => onClick?.Call(this);
        }

        // Property-style access (CheatEngine compatible)
        public string Caption { get => Text; set => Text = value; }

        // Font property for CE compatibility (label.Font.Size = 12)
        public new LuaFontWrapper Font => _font ??= new LuaFontWrapper(this);

        // Method-style access
        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) { Size = new Size(width, height); AutoSize = false; }
        public void setVisible(bool visible) => Visible = visible;
    }

    public class LuaEdit : TextBox
    {
        public LuaFunction? onChange;
        public LuaFunction? OnChange { get => onChange; set => onChange = value; }

        public LuaEdit()
        {
            TextChanged += (s, e) => onChange?.Call(this);
        }

        // Property-style access
        public string Caption { get => Text; set => Text = value; }

        // Method-style access
        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setText(string text) => Text = text;
        public string getText() => Text;
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
    }

    public class LuaMemo : TextBox
    {
        public LuaFunction? onChange;
        public LuaFunction? OnChange { get => onChange; set => onChange = value; }

        public LuaMemo()
        {
            Multiline = true;
            ScrollBars = ScrollBars.Both;
            TextChanged += (s, e) => onChange?.Call(this);
        }

        // Property-style access
        public string Caption { get => Text; set => Text = value; }

        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setText(string text) => Text = text;
        public string getText() => Text;
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public void append(string text) => AppendText(text + Environment.NewLine);
        public void clear() => Clear();
    }

    public class LuaButton : Button
    {
        public LuaFunction? onClick;
        public LuaFunction? OnClick { get => onClick; set => onClick = value; }
        private LuaFontWrapper? _font;

        public LuaButton()
        {
            Click += (s, e) => onClick?.Call(this);
        }

        // Property-style access
        public string Caption { get => Text; set => Text = value; }

        // Font property for CE compatibility
        public new LuaFontWrapper Font => _font ??= new LuaFontWrapper(this);

        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
    }

    public class LuaCheckBox : CheckBox
    {
        public LuaFunction? onChange;
        public LuaFunction? OnChange { get => onChange; set => onChange = value; }
        private LuaFontWrapper? _font;

        public LuaCheckBox()
        {
            CheckedChanged += (s, e) => onChange?.Call(this);
        }

        // Property-style access
        public string Caption { get => Text; set => Text = value; }

        // Font property for CE compatibility
        public new LuaFontWrapper Font => _font ??= new LuaFontWrapper(this);

        public void setCaption(string text) => Text = text;
        public string getCaption() => Text;
        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public void setChecked(bool check) => Checked = check;
        public bool getChecked() => Checked;
    }

    public class LuaComboBox : ComboBox
    {
        public LuaFunction? onChange;
        public LuaFunction? OnChange { get => onChange; set => onChange = value; }

        public LuaComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            SelectedIndexChanged += (s, e) => onChange?.Call(this);
        }

        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public void addItem(string item) => Items.Add(item);
        public void clear() => Items.Clear();
        public int getSelectedIndex() => SelectedIndex;
        public void setSelectedIndex(int index) => SelectedIndex = index;
        public string getSelectedItem() => SelectedItem?.ToString() ?? "";
    }

    public class LuaListBox : ListBox
    {
        public LuaFunction? onChange;
        public LuaFunction? OnChange { get => onChange; set => onChange = value; }

        public LuaListBox()
        {
            SelectedIndexChanged += (s, e) => onChange?.Call(this);
        }

        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public void addItem(string item) => Items.Add(item);
        public void clear() => Items.Clear();
        public int getSelectedIndex() => SelectedIndex;
        public void setSelectedIndex(int index) => SelectedIndex = index;
        public string getSelectedItem() => SelectedItem?.ToString() ?? "";
    }

    public class LuaPanel : Panel
    {
        public LuaPanel()
        {
            BorderStyle = BorderStyle.FixedSingle;
        }

        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
    }

    public class LuaImage : PictureBox
    {
        public LuaFunction? onClick;
        public LuaFunction? OnClick { get => onClick; set => onClick = value; }

        public LuaImage()
        {
            SizeMode = PictureBoxSizeMode.StretchImage;
            Click += (s, e) => onClick?.Call(this);
        }

        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;

        public void loadFromFile(string path)
        {
            try { Image = System.Drawing.Image.FromFile(path); }
            catch { }
        }
    }

    public class LuaTimer
    {
        public System.Windows.Forms.Timer InternalTimer { get; }
        public LuaFunction? onTimer;
        public LuaFunction? OnTimer { get => onTimer; set => onTimer = value; }

        // Property-style access
        public int Interval { get => InternalTimer.Interval; set => InternalTimer.Interval = value; }
        public bool Enabled { get => InternalTimer.Enabled; set => InternalTimer.Enabled = value; }

        public LuaTimer(int interval)
        {
            InternalTimer = new System.Windows.Forms.Timer();
            InternalTimer.Interval = interval;
            InternalTimer.Tick += (s, e) => onTimer?.Call(this);
        }

        public void setInterval(int interval) => InternalTimer.Interval = interval;
        public int getInterval() => InternalTimer.Interval;
        public void start() => InternalTimer.Start();
        public void stop() => InternalTimer.Stop();
        public void setEnabled(bool enabled) => InternalTimer.Enabled = enabled;
        public bool getEnabled() => InternalTimer.Enabled;
        public void Destroy() { InternalTimer.Stop(); InternalTimer.Dispose(); }
    }

    public class LuaProgressBar : ProgressBar
    {
        public LuaProgressBar()
        {
            Minimum = 0;
            Maximum = 100;
        }

        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public void setValue(int value) => Value = Math.Min(Maximum, Math.Max(Minimum, value));
        public int getValue() => Value;
        public void setMin(int min) => Minimum = min;
        public void setMax(int max) => Maximum = max;
    }

    public class LuaTrackBar : TrackBar
    {
        public LuaFunction? onChange;
        public LuaFunction? OnChange { get => onChange; set => onChange = value; }

        public LuaTrackBar()
        {
            Minimum = 0;
            Maximum = 100;
            ValueChanged += (s, e) => onChange?.Call(this);
        }

        public void setPosition(int x, int y) => Location = new Point(x, y);
        public void setSize(int width, int height) => Size = new Size(width, height);
        public void setVisible(bool visible) => Visible = visible;
        public void setValue(int value) => Value = Math.Min(Maximum, Math.Max(Minimum, value));
        public new int getValue() => Value;
        public void setMin(int min) => Minimum = min;
        public void setMax(int max) => Maximum = max;
    }
}
