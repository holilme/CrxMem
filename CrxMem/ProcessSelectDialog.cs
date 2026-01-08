using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace CrxMem
{
    public class ProcessSelectDialog : Form
    {
        private TabControl tabControl;
        private TabPage? tabGunz;
        private ListView lvProcesses;
        private ListView lvApplications;
        private ListView lvWindows;
        private ListView lvGunz;
        private Button btnSelect;
        private Button btnCancel;
        private ImageList imageList;

        public Process? SelectedProcess { get; private set; }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public ProcessSelectDialog()
        {
            InitializeComponents();
            LoadProcesses();
        }

        private void InitializeComponents()
        {
            this.Text = "Process List";
            this.Size = new Size(420, 550);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            // Image list for process icons
            imageList = new ImageList
            {
                ImageSize = new Size(16, 16),
                ColorDepth = ColorDepth.Depth32Bit
            };

            // Tab control
            tabControl = new TabControl
            {
                Location = new Point(10, 10),
                Size = new Size(385, 430),
                Font = new Font("Segoe UI", 9F),
                DrawMode = TabDrawMode.OwnerDrawFixed
            };
            tabControl.DrawItem += TabControl_DrawItem;

            // Gunz tab (special highlighted tab - will be added if Gunz processes found)
            lvGunz = CreateListView();

            // Applications tab
            var tabApplications = new TabPage("Applications");
            lvApplications = CreateListView();
            tabApplications.Controls.Add(lvApplications);
            tabControl.TabPages.Add(tabApplications);

            // Processes tab
            var tabProcesses = new TabPage("Processes");
            lvProcesses = CreateListView();
            tabProcesses.Controls.Add(lvProcesses);
            tabControl.TabPages.Add(tabProcesses);

            // Windows tab
            var tabWindows = new TabPage("Windows");
            lvWindows = CreateListView();
            tabWindows.Controls.Add(lvWindows);
            tabControl.TabPages.Add(tabWindows);

            // Open button
            btnSelect = new Button
            {
                Text = "Open",
                Location = new Point(135, 450),
                Size = new Size(110, 30),
                Font = new Font("Segoe UI", 9F)
            };
            btnSelect.Click += (s, e) => SelectProcess();

            // Cancel button
            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(255, 450),
                Size = new Size(110, 30),
                Font = new Font("Segoe UI", 9F),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[] {
                tabControl, btnSelect, btnCancel
            });

            this.AcceptButton = btnSelect;
            this.CancelButton = btnCancel;
        }

        private ListView CreateListView()
        {
            var lv = new ListView
            {
                Location = new Point(3, 3),
                Size = new Size(372, 390),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                Font = new Font("Segoe UI", 9F),
                HeaderStyle = ColumnHeaderStyle.None,
                SmallImageList = imageList,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };

            lv.Columns.Add("Process", 365);
            lv.DoubleClick += (s, e) => SelectProcess();

            return lv;
        }

        private void LoadProcesses()
        {
            lvProcesses.Items.Clear();
            lvApplications.Items.Clear();
            lvWindows.Items.Clear();
            lvGunz.Items.Clear();

            // Remove Gunz tab if it exists
            if (tabGunz != null && tabControl.TabPages.Contains(tabGunz))
            {
                tabControl.TabPages.Remove(tabGunz);
                tabGunz = null;
            }

            var processes = Process.GetProcesses()
                .OrderBy(p => p.ProcessName)
                .ToList();

            bool foundGunzProcess = false;

            foreach (var process in processes)
            {
                try
                {
                    // Get process icon
                    Icon? processIcon = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(process.MainModule?.FileName))
                        {
                            processIcon = Icon.ExtractAssociatedIcon(process.MainModule.FileName);
                        }
                    }
                    catch { }

                    int imageIndex = -1;
                    if (processIcon != null)
                    {
                        imageList.Images.Add(processIcon);
                        imageIndex = imageList.Images.Count - 1;
                    }

                    // Format: XXXXXXXX-ProcessName like Cheat Engine
                    string displayText = $"{process.Id:X8}-{process.ProcessName}";

                    // Check if this is a Gunz-related process
                    bool isGunzProcess = process.ProcessName.ToLower().Contains("gunz") ||
                                        process.ProcessName.ToLower().Contains("theduel");

                    if (isGunzProcess)
                    {
                        foundGunzProcess = true;
                        var gunzItem = new ListViewItem(displayText)
                        {
                            Tag = process,
                            ImageIndex = imageIndex,
                            ForeColor = Color.FromArgb(255, 140, 0) // Orange highlight
                        };
                        lvGunz.Items.Add(gunzItem);
                    }

                    // Add to Processes tab (all processes)
                    var processItem = new ListViewItem(displayText)
                    {
                        Tag = process,
                        ImageIndex = imageIndex
                    };
                    lvProcesses.Items.Add(processItem);

                    // Add to Applications tab (processes with windows)
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        var appItem = new ListViewItem(displayText)
                        {
                            Tag = process,
                            ImageIndex = imageIndex
                        };
                        lvApplications.Items.Add(appItem);
                    }

                    // Add to Windows tab (processes with visible windows)
                    if (!string.IsNullOrEmpty(process.MainWindowTitle) && process.MainWindowHandle != IntPtr.Zero)
                    {
                        string windowText = $"{process.Id:X8}-{process.MainWindowTitle}";
                        var windowItem = new ListViewItem(windowText)
                        {
                            Tag = process,
                            ImageIndex = imageIndex
                        };
                        lvWindows.Items.Add(windowItem);
                    }
                }
                catch
                {
                    // Skip processes we can't access
                }
            }

            // Add Gunz tab at the beginning if Gunz processes were found
            if (foundGunzProcess)
            {
                tabGunz = new TabPage("🎯 GUNZ DETECTED");
                tabGunz.BackColor = Color.FromArgb(40, 40, 40);
                tabGunz.Controls.Add(lvGunz);
                tabControl.TabPages.Insert(0, tabGunz);
                tabControl.SelectedIndex = 0; // Auto-select Gunz tab
            }
        }

        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            TabControl tc = (TabControl)sender!;
            TabPage page = tc.TabPages[e.Index];

            // Special highlighting for Gunz tab
            bool isGunzTab = page.Text.Contains("GUNZ");

            if (isGunzTab)
            {
                // Draw glowing orange background for Gunz tab
                using (var brush = new LinearGradientBrush(
                    e.Bounds,
                    Color.FromArgb(255, 69, 0),    // Dark orange
                    Color.FromArgb(255, 140, 0),   // Orange
                    90F))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }

                // Draw white text with bold font
                using (var font = new Font(e.Font!, FontStyle.Bold))
                {
                    e.Graphics.DrawString(page.Text, font, Brushes.White,
                        e.Bounds.X + 3, e.Bounds.Y + 3);
                }
            }
            else
            {
                // Normal tab drawing
                bool selected = tc.SelectedIndex == e.Index;
                Color backColor = selected ? Color.FromArgb(60, 60, 60) : Color.FromArgb(45, 45, 48);

                using (var brush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }

                e.Graphics.DrawString(page.Text, e.Font!, Brushes.White,
                    e.Bounds.X + 3, e.Bounds.Y + 3);
            }
        }

        private void SelectProcess()
        {
            ListView currentList = tabControl.SelectedTab.Controls[0] as ListView;
            if (currentList?.SelectedItems.Count > 0)
            {
                SelectedProcess = currentList.SelectedItems[0].Tag as Process;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }
}
