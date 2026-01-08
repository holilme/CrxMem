using System;
using System.Drawing;
using System.Windows.Forms;
using ReaLTaiizor.Forms;
using ReaLTaiizor.Controls;

// Aliases to avoid ambiguity
using WinFormsPanel = System.Windows.Forms.Panel;
using WinFormsTabPage = System.Windows.Forms.TabPage;

namespace CrxMem
{
    /// <summary>
    /// Prototype Lua Editor form using ReaLTaiizor for modern UI preview.
    /// This is a test form to evaluate the design before full implementation.
    /// </summary>
    public class LuaEditorPrototype : MaterialForm
    {
        // ReaLTaiizor Controls
        private MaterialTabControl tabControl = null!;
        private WinFormsTabPage tabScript1 = null!;
        private WinFormsTabPage tabScript2 = null!;
        private RichTextBox editorBox = null!;
        private RichTextBox outputBox = null!;
        private WinFormsPanel toolbarPanel = null!;
        private WinFormsPanel outputPanel = null!;
        private SplitContainer splitContainer = null!;

        // Buttons
        private MaterialButton btnNew = null!;
        private MaterialButton btnOpen = null!;
        private MaterialButton btnSave = null!;
        private MaterialButton btnExecute = null!;
        private MaterialButton btnStop = null!;
        private MaterialButton btnClear = null!;

        // Colors for dark theme
        private readonly Color _bgDark = Color.FromArgb(30, 30, 30);
        private readonly Color _bgMedium = Color.FromArgb(45, 45, 45);
        private readonly Color _textColor = Color.FromArgb(220, 220, 220);
        private readonly Color _accentColor = Color.FromArgb(0, 122, 204);

        public LuaEditorPrototype()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // Form settings (MaterialForm provides modern title bar)
            Text = "CrxMem Lua Engine - ReaLTaiizor Preview";
            Size = new Size(1000, 750);
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _bgDark;

            // Create toolbar panel
            toolbarPanel = new WinFormsPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = _bgMedium,
                Padding = new Padding(8, 8, 8, 8)
            };

            // Create Material Buttons for toolbar
            btnNew = CreateMaterialButton("New", 0);
            btnOpen = CreateMaterialButton("Open", 80);
            btnSave = CreateMaterialButton("Save", 160);

            // Separator space
            btnExecute = CreateMaterialButton("▶ Execute", 280, true);
            btnStop = CreateMaterialButton("⬛ Stop", 380);
            btnClear = CreateMaterialButton("Clear", 480);

            toolbarPanel.Controls.AddRange(new Control[] {
                btnNew, btnOpen, btnSave, btnExecute, btnStop, btnClear
            });

            // Create split container for editor/output
            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 450,
                SplitterWidth = 6,
                BackColor = _bgDark,
                Panel1MinSize = 150,
                Panel2MinSize = 100
            };

            // Create tab control for scripts
            tabControl = new MaterialTabControl
            {
                Dock = DockStyle.Fill
            };

            // Create script tabs
            tabScript1 = new WinFormsTabPage("Script 1");
            tabScript1.BackColor = _bgDark;

            tabScript2 = new WinFormsTabPage("Script 2 *");
            tabScript2.BackColor = _bgDark;

            // Create editor RichTextBox
            editorBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = _bgDark,
                ForeColor = _textColor,
                Font = new Font("Cascadia Code", 11F),
                BorderStyle = BorderStyle.None,
                AcceptsTab = true,
                WordWrap = false,
                Text = GetSampleScript()
            };

            var editor2 = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = _bgDark,
                ForeColor = _textColor,
                Font = new Font("Cascadia Code", 11F),
                BorderStyle = BorderStyle.None,
                AcceptsTab = true,
                WordWrap = false,
                Text = "-- Script 2\n-- Another script tab\n\nprint(\"Hello from Script 2!\")"
            };

            tabScript1.Controls.Add(editorBox);
            tabScript2.Controls.Add(editor2);

            tabControl.TabPages.Add(tabScript1);
            tabControl.TabPages.Add(tabScript2);

            // Create output panel with header
            outputPanel = new WinFormsPanel
            {
                Dock = DockStyle.Fill,
                BackColor = _bgDark
            };

            var outputHeader = new WinFormsPanel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = _bgMedium
            };

            var outputLabel = new Label
            {
                Text = "  Output",
                ForeColor = _textColor,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            outputHeader.Controls.Add(outputLabel);

            // Create output RichTextBox
            outputBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = _textColor,
                Font = new Font("Cascadia Code", 10F),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Text = "[Script started]\nAttached to process: 12345\nCurrent health: 100\nHealth set to 999!\n[Script completed]"
            };

            outputPanel.Controls.Add(outputBox);
            outputPanel.Controls.Add(outputHeader);

            // Add to split container
            splitContainer.Panel1.Controls.Add(tabControl);
            splitContainer.Panel2.Controls.Add(outputPanel);

            // Add controls to form
            Controls.Add(splitContainer);
            Controls.Add(toolbarPanel);

            // Wire up button clicks
            btnNew.Click += (s, e) => MessageBox.Show("New Script clicked!", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnOpen.Click += (s, e) => MessageBox.Show("Open Script clicked!", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnSave.Click += (s, e) => MessageBox.Show("Save Script clicked!", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnExecute.Click += (s, e) => {
                outputBox.AppendText("\n[Executing script...]\n");
                outputBox.AppendText("Script executed successfully!\n");
            };
            btnStop.Click += (s, e) => outputBox.AppendText("\n[Script stopped]\n");
            btnClear.Click += (s, e) => outputBox.Clear();
        }

        private MaterialButton CreateMaterialButton(string text, int xPos, bool isPrimary = false)
        {
            var btn = new MaterialButton
            {
                Text = text,
                Location = new Point(xPos, 8),
                Size = new Size(text.Length > 8 ? 90 : 70, 34),
                AutoSize = false,
                Type = isPrimary ? MaterialButton.MaterialButtonType.Contained : MaterialButton.MaterialButtonType.Outlined,
                UseAccentColor = isPrimary
            };
            return btn;
        }

        private string GetSampleScript()
        {
            return @"-- CrxMem Lua Script
-- Press F5 to execute

local pid = getOpenedProcessID()
if pid > 0 then
    print(""Attached to process: "" .. pid)
else
    print(""No process attached"")
end

-- Example: Read memory
local health = readInteger(""game.exe+0x12345"")
print(""Current health: "" .. health)

-- Example: Write memory
writeInteger(""game.exe+0x12345"", 999)
print(""Health set to 999!"")

-- Example: Create GUI
local form = createForm()
form.Caption = ""My Trainer""
form.setSize(300, 200)

local btn = createButton(form)
btn.Caption = ""Max Health""
btn.setPosition(10, 10)
btn.onClick = function()
    writeInteger(""game.exe+0x12345"", 99999)
end

form.show()
";
        }

        /// <summary>
        /// Show the prototype form for preview.
        /// </summary>
        public static void ShowPrototype()
        {
            var form = new LuaEditorPrototype();
            form.Show();
        }
    }
}
