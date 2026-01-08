using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CrxMem.Core;
using CrxMem.LuaScripting;

namespace CrxMem
{
    /// <summary>
    /// Multi-tab Lua script editor form with syntax highlighting.
    /// Similar to CheatEngine's Lua Engine window.
    /// </summary>
    public class LuaScriptEditorForm : Form
    {
        // Controls
        private MenuStrip menuStrip = null!;
        private ToolStrip toolStrip = null!;
        private SplitContainer splitContainer = null!;
        private TabControl tabControl = null!;
        private RichTextBox outputBox = null!;

        // Engine
        private LuaEngine? _luaEngine;
        private ProcessAccess? _processAccess;
        private MainWindow? _mainWindow;

        // Script tracking
        private int _newScriptCounter = 1;
        private Dictionary<TabPage, ScriptTabInfo> _tabInfo = new();

        // Dark theme colors
        private readonly Color _bgColor = Color.FromArgb(30, 30, 30);
        private readonly Color _textColor = Color.FromArgb(220, 220, 220);
        private readonly Color _outputBgColor = Color.FromArgb(20, 20, 20);
        private readonly Color _tabBgColor = Color.FromArgb(45, 45, 45);

        // Syntax highlighting colors
        private readonly Color _keywordColor = Color.FromArgb(86, 156, 214);    // Blue
        private readonly Color _stringColor = Color.FromArgb(214, 157, 133);    // Orange
        private readonly Color _commentColor = Color.FromArgb(106, 153, 85);    // Green
        private readonly Color _numberColor = Color.FromArgb(181, 206, 168);    // Light green
        private readonly Color _functionColor = Color.FromArgb(220, 220, 170);  // Yellow
        private readonly Color _operatorColor = Color.FromArgb(180, 180, 180);  // Gray

        // Lua keywords
        private static readonly HashSet<string> LuaKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for",
            "function", "goto", "if", "in", "local", "nil", "not", "or",
            "repeat", "return", "then", "true", "until", "while"
        };

        // CrxMem Lua API functions
        private static readonly HashSet<string> ApiFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Memory
            "readByte", "readSmallInteger", "readInteger", "readQword", "readFloat",
            "readDouble", "readString", "readBytes", "readPointer",
            "writeByte", "writeSmallInteger", "writeInteger", "writeQword", "writeFloat",
            "writeDouble", "writeString", "writeBytes",
            "allocateMemory", "deAlloc",
            // Process
            "openProcess", "getOpenedProcessID", "getProcessList", "getModuleList",
            "getAddress", "pause", "unpause", "targetIs64Bit", "getProcessIDFromProcessName",
            // Utility
            "print", "showMessage", "inputQuery", "sleep", "getTickCount", "messageDialog",
            "playSound", "beep", "getCEVersion", "getCheatEngineVersion",
            // Scanning
            "AOBScan", "AOBScanUnique", "AOBScanEx",
            // Assembly
            "disassemble", "getInstructionSize", "assemble", "autoAssemble",
            // GUI
            "createForm", "createLabel", "createEdit", "createMemo", "createButton",
            "createCheckBox", "createComboBox", "createListBox", "createPanel",
            "createImage", "createTimer", "createProgressBar", "createTrackBar"
        };

        public LuaScriptEditorForm(ProcessAccess? processAccess = null, MainWindow? mainWindow = null)
        {
            _processAccess = processAccess;
            _mainWindow = mainWindow;
            InitializeComponents();
            InitializeLuaEngine();
            CreateNewTab();
        }

        private void InitializeComponents()
        {
            // Form settings
            Text = "CrxMem Lua Engine";
            Size = new Size(900, 700);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _bgColor;
            Icon = null; // Would load icon here

            // Create menu strip
            menuStrip = new MenuStrip
            {
                BackColor = _tabBgColor,
                ForeColor = _textColor,
                Renderer = new DarkMenuRenderer()
            };

            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add("&New Script", null, (s, e) => CreateNewTab());
            fileMenu.DropDownItems.Add("&Open...", null, (s, e) => OpenScript());
            fileMenu.DropDownItems.Add("&Save", null, (s, e) => SaveScript());
            fileMenu.DropDownItems.Add("Save &As...", null, (s, e) => SaveScriptAs());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("&Close Tab", null, (s, e) => CloseCurrentTab());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("E&xit", null, (s, e) => Close());

            var editMenu = new ToolStripMenuItem("&Edit");
            editMenu.DropDownItems.Add("&Undo", null, (s, e) => GetCurrentEditor()?.Undo());
            editMenu.DropDownItems.Add("&Redo", null, (s, e) => { });
            editMenu.DropDownItems.Add(new ToolStripSeparator());
            editMenu.DropDownItems.Add("Cu&t", null, (s, e) => GetCurrentEditor()?.Cut());
            editMenu.DropDownItems.Add("&Copy", null, (s, e) => GetCurrentEditor()?.Copy());
            editMenu.DropDownItems.Add("&Paste", null, (s, e) => GetCurrentEditor()?.Paste());
            editMenu.DropDownItems.Add(new ToolStripSeparator());
            editMenu.DropDownItems.Add("Select &All", null, (s, e) => GetCurrentEditor()?.SelectAll());

            var scriptMenu = new ToolStripMenuItem("&Script");
            scriptMenu.DropDownItems.Add("&Execute", null, (s, e) => ExecuteScript());
            scriptMenu.DropDownItems.Add("&Stop", null, (s, e) => StopScript());
            scriptMenu.DropDownItems.Add(new ToolStripSeparator());
            scriptMenu.DropDownItems.Add("&Clear Output", null, (s, e) => ClearOutput());
            scriptMenu.DropDownItems.Add("&Reset Engine", null, (s, e) => ResetEngine());

            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add("&API Reference", null, (s, e) => ShowApiReference());

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, scriptMenu, helpMenu });

            // Create toolbar
            toolStrip = new ToolStrip
            {
                BackColor = _tabBgColor,
                ForeColor = _textColor,
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new DarkToolStripRenderer()
            };

            var btnNew = new ToolStripButton("New") { ToolTipText = "New Script" };
            btnNew.Click += (s, e) => CreateNewTab();

            var btnOpen = new ToolStripButton("Open") { ToolTipText = "Open Script" };
            btnOpen.Click += (s, e) => OpenScript();

            var btnSave = new ToolStripButton("Save") { ToolTipText = "Save Script" };
            btnSave.Click += (s, e) => SaveScript();

            toolStrip.Items.Add(btnNew);
            toolStrip.Items.Add(btnOpen);
            toolStrip.Items.Add(btnSave);
            toolStrip.Items.Add(new ToolStripSeparator());

            var btnExecute = new ToolStripButton("▶ Execute") { ToolTipText = "Execute Script (F5)" };
            btnExecute.Click += (s, e) => ExecuteScript();

            var btnStop = new ToolStripButton("⬛ Stop") { ToolTipText = "Stop Script" };
            btnStop.Click += (s, e) => StopScript();

            toolStrip.Items.Add(btnExecute);
            toolStrip.Items.Add(btnStop);
            toolStrip.Items.Add(new ToolStripSeparator());

            var btnClear = new ToolStripButton("Clear Output") { ToolTipText = "Clear Output" };
            btnClear.Click += (s, e) => ClearOutput();
            toolStrip.Items.Add(btnClear);

            // Create split container
            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = _bgColor,
                SplitterDistance = 450,
                SplitterWidth = 6
            };

            // Create tab control for scripts
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = _bgColor,
                ForeColor = _textColor
            };
            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.DrawItem += TabControl_DrawItem;

            // Create output box
            outputBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = _outputBgColor,
                ForeColor = _textColor,
                Font = new Font("Cascadia Code", 10F),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                WordWrap = true
            };

            // Add label for output
            var outputLabel = new Label
            {
                Text = "Output",
                BackColor = _tabBgColor,
                ForeColor = _textColor,
                Dock = DockStyle.Top,
                Padding = new Padding(5, 3, 5, 3),
                Height = 22
            };

            var outputPanel = new Panel { Dock = DockStyle.Fill, BackColor = _bgColor };
            outputPanel.Controls.Add(outputBox);
            outputPanel.Controls.Add(outputLabel);

            splitContainer.Panel1.Controls.Add(tabControl);
            splitContainer.Panel2.Controls.Add(outputPanel);

            // Add controls to form
            Controls.Add(splitContainer);
            Controls.Add(toolStrip);
            Controls.Add(menuStrip);
            MainMenuStrip = menuStrip;

            // Keyboard shortcuts
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5)
                {
                    ExecuteScript();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.S)
                {
                    SaveScript();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.N)
                {
                    CreateNewTab();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.O)
                {
                    OpenScript();
                    e.Handled = true;
                }
            };
        }

        private void InitializeLuaEngine()
        {
            _luaEngine = new LuaEngine();
            _luaEngine.SetProcessAccess(_processAccess);
            _luaEngine.SetMainWindow(_mainWindow);

            _luaEngine.OnOutput += (text) =>
            {
                AppendOutput(text, Color.LightGray);
            };

            _luaEngine.OnError += (text) =>
            {
                AppendOutput(text, Color.Salmon);
            };

            _luaEngine.OnScriptStarted += () =>
            {
                AppendOutput("[Script started]", Color.Cyan);
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        private void CreateNewTab(string? filePath = null, string? content = null)
        {
            var tabPage = new TabPage();
            var editor = CreateEditor();

            if (filePath != null)
            {
                tabPage.Text = Path.GetFileName(filePath);
                _tabInfo[tabPage] = new ScriptTabInfo { FilePath = filePath, IsModified = false };

                if (content == null && File.Exists(filePath))
                {
                    content = File.ReadAllText(filePath);
                }
            }
            else
            {
                tabPage.Text = $"Script {_newScriptCounter++}";
                _tabInfo[tabPage] = new ScriptTabInfo { FilePath = null, IsModified = false };
            }

            if (content != null)
            {
                editor.Text = content;
            }
            else
            {
                // Default template
                editor.Text = @"-- CrxMem Lua Script
-- Press F5 to execute

local pid = getOpenedProcessID()
if pid > 0 then
    print(""Attached to process: "" .. pid)
else
    print(""No process attached"")
end

-- Example: Read memory
-- local value = readInteger(0x12345678)
-- print(""Value: "" .. value)

-- Example: Write memory
-- writeInteger(0x12345678, 999)
";
            }

            tabPage.Controls.Add(editor);
            tabControl.TabPages.Add(tabPage);
            tabControl.SelectedTab = tabPage;

            // Apply syntax highlighting
            ApplySyntaxHighlighting(editor);
        }

        private RichTextBox CreateEditor()
        {
            var editor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = _textColor,
                Font = new Font("Cascadia Code", 11F),
                BorderStyle = BorderStyle.None,
                AcceptsTab = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            // Track modifications
            editor.TextChanged += (s, e) =>
            {
                var tab = tabControl.SelectedTab;
                if (tab != null && _tabInfo.TryGetValue(tab, out var info))
                {
                    if (!info.IsModified)
                    {
                        info.IsModified = true;
                        if (!tab.Text.EndsWith("*"))
                            tab.Text += "*";
                    }
                }

                // Delay syntax highlighting to avoid performance issues while typing
                DelayedSyntaxHighlight(editor);
            };

            return editor;
        }

        private System.Windows.Forms.Timer? _syntaxHighlightTimer;
        private RichTextBox? _pendingHighlightEditor;

        private void DelayedSyntaxHighlight(RichTextBox editor)
        {
            _pendingHighlightEditor = editor;

            if (_syntaxHighlightTimer == null)
            {
                _syntaxHighlightTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _syntaxHighlightTimer.Tick += (s, e) =>
                {
                    _syntaxHighlightTimer.Stop();
                    if (_pendingHighlightEditor != null)
                    {
                        ApplySyntaxHighlighting(_pendingHighlightEditor);
                    }
                };
            }

            _syntaxHighlightTimer.Stop();
            _syntaxHighlightTimer.Start();
        }

        private RichTextBox? GetCurrentEditor()
        {
            var tab = tabControl.SelectedTab;
            if (tab?.Controls.Count > 0)
            {
                return tab.Controls[0] as RichTextBox;
            }
            return null;
        }

        private void CloseCurrentTab()
        {
            if (tabControl.TabPages.Count <= 1) return;

            var tab = tabControl.SelectedTab;
            if (tab == null) return;

            if (_tabInfo.TryGetValue(tab, out var info) && info.IsModified)
            {
                var result = MessageBox.Show(
                    $"Save changes to '{tab.Text.TrimEnd('*')}'?",
                    "Save Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.Yes) SaveScript();
            }

            _tabInfo.Remove(tab);
            tabControl.TabPages.Remove(tab);
        }

        // ═══════════════════════════════════════════════════════════════
        // FILE OPERATIONS
        // ═══════════════════════════════════════════════════════════════

        private void OpenScript()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Lua Scripts (*.lua)|*.lua|All Files (*.*)|*.*",
                Title = "Open Lua Script"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                CreateNewTab(dialog.FileName);
            }
        }

        private void SaveScript()
        {
            var tab = tabControl.SelectedTab;
            if (tab == null) return;

            if (_tabInfo.TryGetValue(tab, out var info) && !string.IsNullOrEmpty(info.FilePath))
            {
                var editor = GetCurrentEditor();
                if (editor != null)
                {
                    File.WriteAllText(info.FilePath, editor.Text);
                    info.IsModified = false;
                    tab.Text = Path.GetFileName(info.FilePath);
                }
            }
            else
            {
                SaveScriptAs();
            }
        }

        private void SaveScriptAs()
        {
            var tab = tabControl.SelectedTab;
            if (tab == null) return;

            using var dialog = new SaveFileDialog
            {
                Filter = "Lua Scripts (*.lua)|*.lua|All Files (*.*)|*.*",
                Title = "Save Lua Script",
                DefaultExt = "lua"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var editor = GetCurrentEditor();
                if (editor != null)
                {
                    File.WriteAllText(dialog.FileName, editor.Text);

                    if (_tabInfo.TryGetValue(tab, out var info))
                    {
                        info.FilePath = dialog.FileName;
                        info.IsModified = false;
                    }
                    else
                    {
                        _tabInfo[tab] = new ScriptTabInfo
                        {
                            FilePath = dialog.FileName,
                            IsModified = false
                        };
                    }

                    tab.Text = Path.GetFileName(dialog.FileName);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SCRIPT EXECUTION
        // ═══════════════════════════════════════════════════════════════

        private void ExecuteScript()
        {
            var editor = GetCurrentEditor();
            if (editor == null || _luaEngine == null) return;

            string script = editor.Text;
            if (string.IsNullOrWhiteSpace(script)) return;

            _luaEngine.ExecuteScript(script);
        }

        private void StopScript()
        {
            _luaEngine?.StopScript();
        }

        private void ResetEngine()
        {
            _luaEngine?.Reset();
        }

        private void ClearOutput()
        {
            outputBox.Clear();
        }

        private void AppendOutput(string text, Color color)
        {
            if (outputBox.InvokeRequired)
            {
                outputBox.Invoke(new Action(() => AppendOutput(text, color)));
                return;
            }

            outputBox.SelectionStart = outputBox.TextLength;
            outputBox.SelectionColor = color;
            outputBox.AppendText(text + Environment.NewLine);
            outputBox.ScrollToCaret();
        }

        // ═══════════════════════════════════════════════════════════════
        // SYNTAX HIGHLIGHTING
        // ═══════════════════════════════════════════════════════════════

        // Win32 API to prevent redraw
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        private const int WM_SETREDRAW = 0x0B;

        private void ApplySyntaxHighlighting(RichTextBox editor)
        {
            if (editor == null) return;

            // Save cursor position, selection, and scroll position
            int selStart = editor.SelectionStart;
            int selLength = editor.SelectionLength;

            // Get current scroll position
            int firstVisibleChar = editor.GetCharIndexFromPosition(new Point(0, 0));

            // Disable redraw to prevent flickering and scrolling
            SendMessage(editor.Handle, WM_SETREDRAW, 0, 0);

            try
            {
                // Reset all text to default color
                editor.Select(0, editor.TextLength);
                editor.SelectionColor = _textColor;

                string text = editor.Text;

                // Comments (single line --)
                HighlightPattern(editor, @"--[^\n]*", _commentColor);

                // Multi-line comments --[[ ]]
                HighlightPattern(editor, @"--\[\[[\s\S]*?\]\]", _commentColor);

                // Strings (double quotes)
                HighlightPattern(editor, "\"(?:[^\"\\\\]|\\\\.)*\"", _stringColor);

                // Strings (single quotes)
                HighlightPattern(editor, "'(?:[^'\\\\]|\\\\.)*'", _stringColor);

                // Multi-line strings [[ ]]
                HighlightPattern(editor, @"\[\[[\s\S]*?\]\]", _stringColor);

                // Numbers
                HighlightPattern(editor, @"\b0x[0-9a-fA-F]+\b", _numberColor);
                HighlightPattern(editor, @"\b\d+\.?\d*\b", _numberColor);

                // Keywords
                foreach (var keyword in LuaKeywords)
                {
                    HighlightWord(editor, keyword, _keywordColor);
                }

                // API functions
                foreach (var func in ApiFunctions)
                {
                    HighlightWord(editor, func, _functionColor);
                }
            }
            finally
            {
                // Restore cursor position
                editor.SelectionStart = selStart;
                editor.SelectionLength = selLength;
                editor.SelectionColor = _textColor;

                // Re-enable redraw
                SendMessage(editor.Handle, WM_SETREDRAW, 1, 0);

                // Scroll back to original position
                editor.Select(firstVisibleChar, 0);
                editor.ScrollToCaret();

                // Restore actual selection
                editor.SelectionStart = selStart;
                editor.SelectionLength = selLength;

                editor.Invalidate();
            }
        }

        private void HighlightPattern(RichTextBox editor, string pattern, Color color)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.Compiled);
                foreach (Match match in regex.Matches(editor.Text))
                {
                    editor.Select(match.Index, match.Length);
                    editor.SelectionColor = color;
                }
            }
            catch { }
        }

        private void HighlightWord(RichTextBox editor, string word, Color color)
        {
            try
            {
                var regex = new Regex($@"\b{Regex.Escape(word)}\b", RegexOptions.Compiled);
                foreach (Match match in regex.Matches(editor.Text))
                {
                    editor.Select(match.Index, match.Length);
                    editor.SelectionColor = color;
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // HELP
        // ═══════════════════════════════════════════════════════════════

        private void ShowApiReference()
        {
            var help = @"CrxMem Lua API Reference
═══════════════════════════════════════

MEMORY FUNCTIONS:
  readByte(address)                 - Read 1 byte
  readSmallInteger(address)         - Read 2 bytes (signed)
  readInteger(address)              - Read 4 bytes (signed)
  readQword(address)                - Read 8 bytes
  readFloat(address)                - Read float
  readDouble(address)               - Read double
  readString(address, maxLen)       - Read string
  readPointer(address)              - Read pointer

  writeByte(address, value)         - Write 1 byte
  writeSmallInteger(address, value) - Write 2 bytes
  writeInteger(address, value)      - Write 4 bytes
  writeQword(address, value)        - Write 8 bytes
  writeFloat(address, value)        - Write float
  writeDouble(address, value)       - Write double
  writeString(address, text)        - Write string
  writeBytes(address, {bytes})      - Write byte array

  allocateMemory(size)              - Allocate memory
  deAlloc(address)                  - Free memory

PROCESS FUNCTIONS:
  openProcess(pidOrName)            - Open a process
  getOpenedProcessID()              - Get current PID
  getProcessList()                  - Get all processes
  getModuleList()                   - Get modules
  getAddress(""module+offset"")     - Resolve address
  pause()                           - Suspend process
  unpause()                         - Resume process
  targetIs64Bit()                   - Check if 64-bit

SCANNING:
  AOBScan(""AA BB ?? CC"")          - Scan for bytes
  AOBScanUnique(pattern)            - Scan for unique

ASSEMBLY:
  disassemble(address)              - Disassemble
  getInstructionSize(address)       - Get instruction size
  autoAssemble(script)              - Auto-assembler

UTILITY:
  print(...)                        - Output text
  showMessage(text)                 - Show message box
  inputQuery(title, prompt)         - Input dialog
  sleep(ms)                         - Sleep
  getTickCount()                    - Get tick count

GUI CREATION:
  createForm()                      - Create form
  createLabel(form)                 - Create label
  createButton(form)                - Create button
  createEdit(form)                  - Create text box
  createCheckBox(form)              - Create checkbox
  createTimer(interval)             - Create timer

FORM/CONTROL METHODS:
  .setCaption(text)                 - Set text
  .setPosition(x, y)                - Set position
  .setSize(width, height)           - Set size
  .show() / .hide() / .close()      - Form visibility
  .onClick = function() end         - Click event
  .onTimer = function() end         - Timer event
";

            using var helpForm = new Form
            {
                Text = "API Reference",
                Size = new Size(600, 700),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = _bgColor
            };

            var textBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = _bgColor,
                ForeColor = _textColor,
                Font = new Font("Cascadia Code", 10F),
                Text = help,
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };

            helpForm.Controls.Add(textBox);
            helpForm.ShowDialog(this);
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB DRAWING
        // ═══════════════════════════════════════════════════════════════

        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            var tab = tabControl.TabPages[e.Index];
            var rect = tabControl.GetTabRect(e.Index);

            // Background
            using var bgBrush = new SolidBrush(
                tabControl.SelectedIndex == e.Index ? _bgColor : _tabBgColor);
            e.Graphics.FillRectangle(bgBrush, rect);

            // Text
            TextRenderer.DrawText(e.Graphics, tab.Text, tabControl.Font,
                rect, _textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ═══════════════════════════════════════════════════════════════
        // CLEANUP
        // ═══════════════════════════════════════════════════════════════

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Check for unsaved changes
            foreach (var kvp in _tabInfo)
            {
                if (kvp.Value.IsModified)
                {
                    var result = MessageBox.Show(
                        "You have unsaved changes. Close anyway?",
                        "Unsaved Changes",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.No)
                    {
                        e.Cancel = true;
                        return;
                    }
                    break;
                }
            }

            _luaEngine?.Dispose();
            _syntaxHighlightTimer?.Dispose();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Update the process access when the main window changes process.
        /// </summary>
        public void UpdateProcessAccess(ProcessAccess? process)
        {
            _processAccess = process;
            _luaEngine?.SetProcessAccess(process);
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER CLASSES
        // ═══════════════════════════════════════════════════════════════

        private class ScriptTabInfo
        {
            public string? FilePath { get; set; }
            public bool IsModified { get; set; }
        }

        private class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }
        }

        private class DarkToolStripRenderer : ToolStripProfessionalRenderer
        {
            public DarkToolStripRenderer() : base(new DarkColorTable()) { }
        }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
            public override Color MenuItemBorder => Color.FromArgb(80, 80, 80);
            public override Color MenuBorder => Color.FromArgb(80, 80, 80);
            public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 45);
            public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 45);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 45);
            public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 45);
            public override Color SeparatorDark => Color.FromArgb(80, 80, 80);
            public override Color SeparatorLight => Color.FromArgb(80, 80, 80);
        }
    }
}
