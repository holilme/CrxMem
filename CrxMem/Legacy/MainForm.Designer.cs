namespace CrxMem
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            _process?.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            progressBarTop = new CrxMem.Controls.ModernProgressBar();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            openProcessToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            saveTableToolStripMenuItem = new ToolStripMenuItem();
            loadTableToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            exitToolStripMenuItem = new ToolStripMenuItem();
            editToolStripMenuItem = new ToolStripMenuItem();
            liveUpdatesToolStripMenuItem = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            toolsToolStripMenuItem = new ToolStripMenuItem();
            luaMonitorToolStripMenuItem = new ToolStripMenuItem();
            peAnalysisToolStripMenuItem = new ToolStripMenuItem();
            acBypassLabToolStripMenuItem = new ToolStripMenuItem();
            dllInjectionTestToolStripMenuItem = new ToolStripMenuItem();
            helpToolStripMenuItem = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            toolStrip1 = new ToolStrip();
            btnOpenProcess = new ToolStripButton();
            btnLoadTable = new ToolStripButton();
            btnSaveTable = new ToolStripButton();
            lblProcessTitle = new Label();
            panelHeader = new Panel();
            picLogo = new PictureBox();
            splitContainer1 = new SplitContainer();
            panelTop = new Panel();
            lvFoundAddresses = new ListView();
            colAddress = new ColumnHeader();
            colValue = new ColumnHeader();
            colPrevious = new ColumnHeader();
            lblFound = new Label();
            btnMemoryView = new CrxMem.Controls.ModernButton();
            panelRight = new Panel();
            btnAddAddressManually = new CrxMem.Controls.ModernButton();
            grpMemoryScanOptions = new GroupBox();
            chkFastScan = new CheckBox();
            chkActiveMemoryOnly = new CheckBox();
            chkCopyOnWrite = new CheckBox();
            chkWritable = new CheckBox();
            lblStop = new Label();
            txtStopAddress = new CrxMem.Controls.ModernTextBox();
            lblStart = new Label();
            txtStartAddress = new CrxMem.Controls.ModernTextBox();
            cmbMemoryScanOptions = new ComboBox();
            lblModule = new Label();
            cmbModule = new ComboBox();
            cmbValueType = new ComboBox();
            lblValueType = new Label();
            cmbScanType = new ComboBox();
            lblScanType = new Label();
            chkHex = new CheckBox();
            txtValue = new CrxMem.Controls.ModernTextBox();
            lblValue = new Label();
            btnUndoScan = new CrxMem.Controls.ModernButton();
            btnCancelScan = new CrxMem.Controls.ModernButton();
            btnNewScan = new CrxMem.Controls.ModernButton();
            btnNextScan = new CrxMem.Controls.ModernButton();
            btnFirstScan = new CrxMem.Controls.ModernButton();
            panelBottom = new Panel();
            lvAddressList = new ListView();
            colActive = new ColumnHeader();
            colDescription = new ColumnHeader();
            colAddr = new ColumnHeader();
            colType = new ColumnHeader();
            colVal = new ColumnHeader();
            progressBarScan = new CrxMem.Controls.ModernProgressBar();
            panelScanProgress = new Panel();
            updateTimer = new System.Windows.Forms.Timer(components);
            menuStrip1.SuspendLayout();
            toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picLogo).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            panelTop.SuspendLayout();
            panelRight.SuspendLayout();
            grpMemoryScanOptions.SuspendLayout();
            panelBottom.SuspendLayout();
            SuspendLayout();
            // 
            // progressBarTop
            // 
            progressBarTop.Dock = DockStyle.Top;
            progressBarTop.Location = new Point(0, 30);
            progressBarTop.Name = "progressBarTop";
            progressBarTop.Size = new Size(712, 4);
            progressBarTop.TabIndex = 4;
            progressBarTop.Visible = false;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, editToolStripMenuItem, toolsToolStripMenuItem, helpToolStripMenuItem });
            menuStrip1.Location = new Point(0, 34);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(712, 24);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            //
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openProcessToolStripMenuItem, toolStripSeparator1, saveTableToolStripMenuItem, loadTableToolStripMenuItem, toolStripSeparator3, exitToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "&File";
            // 
            // openProcessToolStripMenuItem
            // 
            openProcessToolStripMenuItem.Name = "openProcessToolStripMenuItem";
            openProcessToolStripMenuItem.Size = new Size(146, 22);
            openProcessToolStripMenuItem.Text = "&Open Process";
            openProcessToolStripMenuItem.Click += OpenProcess_Click;
            // 
            // toolStripSeparator1
            //
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(176, 6);
            //
            // saveTableToolStripMenuItem
            //
            saveTableToolStripMenuItem.Name = "saveTableToolStripMenuItem";
            saveTableToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.S;
            saveTableToolStripMenuItem.Size = new Size(179, 22);
            saveTableToolStripMenuItem.Text = "&Save Table";
            saveTableToolStripMenuItem.Click += SaveTable_Click;
            //
            // loadTableToolStripMenuItem
            //
            loadTableToolStripMenuItem.Name = "loadTableToolStripMenuItem";
            loadTableToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            loadTableToolStripMenuItem.Size = new Size(179, 22);
            loadTableToolStripMenuItem.Text = "&Load Table";
            loadTableToolStripMenuItem.Click += LoadTable_Click;
            //
            // toolStripSeparator3
            //
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(176, 6);
            //
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new Size(146, 22);
            exitToolStripMenuItem.Text = "E&xit";
            exitToolStripMenuItem.Click += Exit_Click;
            //
            // editToolStripMenuItem
            //
            editToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { liveUpdatesToolStripMenuItem, settingsToolStripMenuItem });
            editToolStripMenuItem.Name = "editToolStripMenuItem";
            editToolStripMenuItem.Size = new Size(39, 20);
            editToolStripMenuItem.Text = "&Edit";
            //
            // liveUpdatesToolStripMenuItem
            //
            liveUpdatesToolStripMenuItem.Checked = true;
            liveUpdatesToolStripMenuItem.CheckOnClick = true;
            liveUpdatesToolStripMenuItem.CheckState = CheckState.Checked;
            liveUpdatesToolStripMenuItem.Name = "liveUpdatesToolStripMenuItem";
            liveUpdatesToolStripMenuItem.Size = new Size(152, 22);
            liveUpdatesToolStripMenuItem.Text = "&Live Updates";
            liveUpdatesToolStripMenuItem.ToolTipText = "Toggle live value updates (reduces kernel driver activity when disabled)";
            liveUpdatesToolStripMenuItem.Click += LiveUpdates_Click;
            //
            // settingsToolStripMenuItem
            //
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new Size(152, 22);
            settingsToolStripMenuItem.Text = "&Settings";
            settingsToolStripMenuItem.Click += Settings_Click;
            //
            // toolsToolStripMenuItem
            //
            toolsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { luaMonitorToolStripMenuItem, peAnalysisToolStripMenuItem, acBypassLabToolStripMenuItem, dllInjectionTestToolStripMenuItem });
            toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            toolsToolStripMenuItem.Size = new Size(46, 20);
            toolsToolStripMenuItem.Text = "&Tools";
            //
            // luaMonitorToolStripMenuItem
            //
            luaMonitorToolStripMenuItem.Name = "luaMonitorToolStripMenuItem";
            luaMonitorToolStripMenuItem.Size = new Size(152, 22);
            luaMonitorToolStripMenuItem.Text = "&LUA Monitor";
            luaMonitorToolStripMenuItem.Click += LuaMonitor_Click;
            //
            // peAnalysisToolStripMenuItem
            //
            peAnalysisToolStripMenuItem.Name = "peAnalysisToolStripMenuItem";
            peAnalysisToolStripMenuItem.Size = new Size(152, 22);
            peAnalysisToolStripMenuItem.Text = "&PE Analysis";
            peAnalysisToolStripMenuItem.Click += PEAnalysis_Click;
            //
            // acBypassLabToolStripMenuItem
            //
            acBypassLabToolStripMenuItem.Name = "acBypassLabToolStripMenuItem";
            acBypassLabToolStripMenuItem.Size = new Size(200, 22);
            acBypassLabToolStripMenuItem.Text = "&AC Bypass Research Lab";
            acBypassLabToolStripMenuItem.Click += ACBypassLab_Click;
            //
            // dllInjectionTestToolStripMenuItem
            //
            dllInjectionTestToolStripMenuItem.Name = "dllInjectionTestToolStripMenuItem";
            dllInjectionTestToolStripMenuItem.Size = new Size(200, 22);
            dllInjectionTestToolStripMenuItem.Text = "&DLL Injection Test";
            dllInjectionTestToolStripMenuItem.Click += DllInjectionTest_Click;
            //
            // helpToolStripMenuItem
            // 
            helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { aboutToolStripMenuItem });
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(44, 20);
            helpToolStripMenuItem.Text = "&Help";
            // 
            // aboutToolStripMenuItem
            //
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new Size(107, 22);
            aboutToolStripMenuItem.Text = "&About";
            aboutToolStripMenuItem.Click += About_Click;
            // 
            // toolStrip1
            // 
            toolStrip1.Items.AddRange(new ToolStripItem[] { btnOpenProcess, btnLoadTable, btnSaveTable });
            toolStrip1.Location = new Point(0, 27);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(712, 25);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // btnOpenProcess
            //
            btnOpenProcess.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnOpenProcess.ImageTransparentColor = Color.Magenta;
            btnOpenProcess.Name = "btnOpenProcess";
            btnOpenProcess.Size = new Size(23, 22);
            btnOpenProcess.Text = "Open Process";
            btnOpenProcess.Click += OpenProcess_Click;
            //
            // btnLoadTable
            //
            btnLoadTable.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnLoadTable.ImageTransparentColor = Color.Magenta;
            btnLoadTable.Name = "btnLoadTable";
            btnLoadTable.Size = new Size(23, 22);
            btnLoadTable.Text = "Load Table";
            btnLoadTable.ToolTipText = "Load Table (Ctrl+O)";
            btnLoadTable.Click += LoadTable_Click;
            //
            // btnSaveTable
            //
            btnSaveTable.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnSaveTable.ImageTransparentColor = Color.Magenta;
            btnSaveTable.Name = "btnSaveTable";
            btnSaveTable.Size = new Size(23, 22);
            btnSaveTable.Text = "Save Table";
            btnSaveTable.ToolTipText = "Save Table (Ctrl+S)";
            btnSaveTable.Click += SaveTable_Click;
            //
            // 
            // panelHeader
            // 
            panelHeader.Controls.Add(lblProcessTitle);
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Location = new Point(0, 0);
            panelHeader.Name = "panelHeader";
            panelHeader.Size = new Size(712, 30);
            panelHeader.TabIndex = 2;
            panelHeader.BackColor = Color.FromArgb(248, 248, 248);
            // 
            // lblProcessTitle
            // 
            lblProcessTitle.AutoSize = false;
            lblProcessTitle.Dock = DockStyle.Fill;
            lblProcessTitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblProcessTitle.Location = new Point(0, 0);
            lblProcessTitle.Name = "lblProcessTitle";
            lblProcessTitle.Size = new Size(712, 30);
            lblProcessTitle.TabIndex = 0;
            lblProcessTitle.Text = "No Process Selected";
            lblProcessTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblProcessTitle.ForeColor = Color.DarkTurquoise;
            // 
            // picLogo
            // 
            picLogo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            picLogo.BackColor = Color.Transparent;
            picLogo.Location = new Point(640, 0);
            picLogo.Name = "picLogo";
            picLogo.Size = new Size(70, 70);
            picLogo.SizeMode = PictureBoxSizeMode.Zoom;
            picLogo.TabIndex = 3;
            picLogo.TabStop = false;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 77);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(panelTop);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(panelBottom);
            splitContainer1.Size = new Size(712, 684);
            splitContainer1.SplitterDistance = 482;
            splitContainer1.TabIndex = 3;
            // 
            // panelTop
            // 
            panelTop.Controls.Add(lvFoundAddresses);
            panelTop.Controls.Add(lblFound);
            panelTop.Controls.Add(btnMemoryView);
            panelTop.Controls.Add(panelRight);
            panelTop.Dock = DockStyle.Fill;
            panelTop.Location = new Point(0, 0);
            panelTop.Name = "panelTop";
            panelTop.Size = new Size(712, 482);
            panelTop.TabIndex = 0;
            // 
            // lvFoundAddresses
            // 
            lvFoundAddresses.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lvFoundAddresses.Columns.AddRange(new ColumnHeader[] { colAddress, colValue, colPrevious });
            lvFoundAddresses.Font = new Font("Courier New", 9F);
            lvFoundAddresses.FullRowSelect = true;
            lvFoundAddresses.GridLines = false;
            lvFoundAddresses.Location = new Point(3, 42);
            lvFoundAddresses.Name = "lvFoundAddresses";
            lvFoundAddresses.Size = new Size(284, 410);
            lvFoundAddresses.TabIndex = 0;
            lvFoundAddresses.UseCompatibleStateImageBehavior = false;
            lvFoundAddresses.View = View.Details;
            lvFoundAddresses.DoubleClick += LvFoundAddresses_DoubleClick;
            // 
            // colAddress
            // 
            colAddress.Text = "Address";
            colAddress.Width = 150;
            // 
            // colValue
            // 
            colValue.Text = "Value";
            colValue.Width = 150;
            // 
            // colPrevious
            // 
            colPrevious.Text = "Previous";
            colPrevious.Width = 150;
            // 
            // lblFound
            // 
            lblFound.AutoSize = true;
            lblFound.Font = new Font("Segoe UI", 9F);
            lblFound.Location = new Point(3, 6);
            lblFound.Name = "lblFound";
            lblFound.Size = new Size(53, 15);
            lblFound.TabIndex = 1;
            lblFound.Text = "Found: 0";
            // 
            // btnMemoryView
            // 
            btnMemoryView.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnMemoryView.Location = new Point(3, 456);
            btnMemoryView.Name = "btnMemoryView";
            btnMemoryView.Size = new Size(100, 23);
            btnMemoryView.TabIndex = 2;
            btnMemoryView.Text = "Memory View";
            btnMemoryView.IsPrimary = false;
            btnMemoryView.IsAccent = true;
            btnMemoryView.UseVisualStyleBackColor = false;
            btnMemoryView.Click += BtnMemoryView_Click;
            // 
            // panelRight
            // 
            panelRight.Controls.Add(btnAddAddressManually);
            panelRight.Controls.Add(grpMemoryScanOptions);
            panelRight.Controls.Add(cmbValueType);
            panelRight.Controls.Add(lblValueType);
            panelRight.Controls.Add(cmbScanType);
            panelRight.Controls.Add(lblScanType);
            panelRight.Controls.Add(chkHex);
            panelRight.Controls.Add(txtValue);
            panelRight.Controls.Add(lblValue);
            panelRight.Controls.Add(btnUndoScan);
            panelRight.Controls.Add(btnCancelScan);
            panelRight.Controls.Add(btnNewScan);
            panelRight.Controls.Add(btnNextScan);
            panelRight.Controls.Add(btnFirstScan);
            panelRight.Dock = DockStyle.Right;
            panelRight.Location = new Point(293, 0);
            panelRight.Name = "panelRight";
            panelRight.Size = new Size(419, 482);
            panelRight.TabIndex = 3;
            // 
            // btnAddAddressManually
            // 
            btnAddAddressManually.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnAddAddressManually.Location = new Point(274, 456);
            btnAddAddressManually.Name = "btnAddAddressManually";
            btnAddAddressManually.Size = new Size(142, 23);
            btnAddAddressManually.TabIndex = 11;
            btnAddAddressManually.Text = "Add Address Manually";
            btnAddAddressManually.IsAccent = true;
            btnAddAddressManually.UseVisualStyleBackColor = false;
            btnAddAddressManually.Click += BtnAddAddressManually_Click;
            //
            // grpMemoryScanOptions
            //
            grpMemoryScanOptions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpMemoryScanOptions.Controls.Add(lblModule);
            grpMemoryScanOptions.Controls.Add(cmbModule);
            grpMemoryScanOptions.Controls.Add(chkFastScan);
            grpMemoryScanOptions.Controls.Add(chkActiveMemoryOnly);
            grpMemoryScanOptions.Controls.Add(chkCopyOnWrite);
            grpMemoryScanOptions.Controls.Add(chkWritable);
            grpMemoryScanOptions.Controls.Add(lblStop);
            grpMemoryScanOptions.Controls.Add(txtStopAddress);
            grpMemoryScanOptions.Controls.Add(lblStart);
            grpMemoryScanOptions.Controls.Add(txtStartAddress);
            grpMemoryScanOptions.Controls.Add(cmbMemoryScanOptions);
            grpMemoryScanOptions.Location = new Point(3, 199);
            grpMemoryScanOptions.Name = "grpMemoryScanOptions";
            grpMemoryScanOptions.Size = new Size(413, 230);
            grpMemoryScanOptions.TabIndex = 10;
            grpMemoryScanOptions.TabStop = false;
            grpMemoryScanOptions.Text = "Memory Scan Options";
            // 
            // chkFastScan
            // 
            chkFastScan.AutoSize = true;
            chkFastScan.Checked = true;
            chkFastScan.CheckState = CheckState.Checked;
            chkFastScan.Location = new Point(6, 150);
            chkFastScan.Name = "chkFastScan";
            chkFastScan.Size = new Size(75, 19);
            chkFastScan.TabIndex = 8;
            chkFastScan.Text = "Fast Scan";
            chkFastScan.UseVisualStyleBackColor = true;
            // 
            // chkActiveMemoryOnly
            // 
            chkActiveMemoryOnly.AutoSize = true;
            chkActiveMemoryOnly.Location = new Point(6, 125);
            chkActiveMemoryOnly.Name = "chkActiveMemoryOnly";
            chkActiveMemoryOnly.Size = new Size(133, 19);
            chkActiveMemoryOnly.TabIndex = 7;
            chkActiveMemoryOnly.Text = "Active memory only";
            chkActiveMemoryOnly.UseVisualStyleBackColor = true;
            // 
            // chkCopyOnWrite
            // 
            chkCopyOnWrite.AutoSize = true;
            chkCopyOnWrite.Location = new Point(6, 100);
            chkCopyOnWrite.Name = "chkCopyOnWrite";
            chkCopyOnWrite.Size = new Size(98, 19);
            chkCopyOnWrite.TabIndex = 6;
            chkCopyOnWrite.Text = "CopyOnWrite";
            chkCopyOnWrite.ThreeState = true;
            chkCopyOnWrite.UseVisualStyleBackColor = true;
            // 
            // chkWritable
            // 
            chkWritable.AutoSize = true;
            chkWritable.Checked = true;
            chkWritable.CheckState = CheckState.Checked;
            chkWritable.Location = new Point(6, 75);
            chkWritable.Name = "chkWritable";
            chkWritable.Size = new Size(70, 19);
            chkWritable.TabIndex = 5;
            chkWritable.Text = "Writable";
            chkWritable.ThreeState = true;
            chkWritable.UseVisualStyleBackColor = true;
            // 
            // lblStop
            // 
            lblStop.AutoSize = true;
            lblStop.Location = new Point(6, 51);
            lblStop.Name = "lblStop";
            lblStop.Size = new Size(31, 15);
            lblStop.TabIndex = 4;
            lblStop.Text = "Stop";
            // 
            // txtStopAddress
            // 
            txtStopAddress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtStopAddress.Font = new Font("Courier New", 9F);
            txtStopAddress.Location = new Point(44, 48);
            txtStopAddress.Name = "txtStopAddress";
            txtStopAddress.Size = new Size(363, 30);
            txtStopAddress.TabIndex = 3;
            txtStopAddress.Text = "7FFFFFFFFFFFFFFF";
            // 
            // lblStart
            // 
            lblStart.AutoSize = true;
            lblStart.Location = new Point(6, 24);
            lblStart.Name = "lblStart";
            lblStart.Size = new Size(31, 15);
            lblStart.TabIndex = 2;
            lblStart.Text = "Start";
            // 
            // txtStartAddress
            // 
            txtStartAddress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtStartAddress.Font = new Font("Courier New", 9F);
            txtStartAddress.Location = new Point(44, 21);
            txtStartAddress.Name = "txtStartAddress";
            txtStartAddress.Size = new Size(363, 30);
            txtStartAddress.TabIndex = 1;
            txtStartAddress.Text = "0000000000000000";
            //
            // lblModule
            //
            lblModule.AutoSize = true;
            lblModule.Location = new Point(6, 175);
            lblModule.Name = "lblModule";
            lblModule.Size = new Size(48, 15);
            lblModule.TabIndex = 9;
            lblModule.Text = "Module:";
            //
            // cmbModule
            //
            cmbModule.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbModule.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbModule.FormattingEnabled = true;
            cmbModule.Location = new Point(60, 172);
            cmbModule.Name = "cmbModule";
            cmbModule.Size = new Size(347, 23);
            cmbModule.TabIndex = 10;
            cmbModule.SelectedIndexChanged += CmbModule_SelectedIndexChanged;
            //
            // cmbMemoryScanOptions
            //
            cmbMemoryScanOptions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbMemoryScanOptions.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMemoryScanOptions.FormattingEnabled = true;
            cmbMemoryScanOptions.Items.AddRange(new object[] { "All", "Writable only", "Executable only" });
            cmbMemoryScanOptions.Location = new Point(6, -3);
            cmbMemoryScanOptions.Name = "cmbMemoryScanOptions";
            cmbMemoryScanOptions.Size = new Size(401, 23);
            cmbMemoryScanOptions.TabIndex = 0;
            cmbMemoryScanOptions.Visible = false;
            // 
            // cmbValueType
            // 
            cmbValueType.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbValueType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbValueType.FormattingEnabled = true;
            cmbValueType.Items.AddRange(new object[] { "Binary", "Byte", "2 Bytes", "4 Bytes", "8 Bytes", "Float", "Double", "String", "Array of Bytes" });
            cmbValueType.Location = new Point(77, 170);
            cmbValueType.Name = "cmbValueType";
            cmbValueType.Size = new Size(339, 23);
            cmbValueType.TabIndex = 9;
            // 
            // lblValueType
            // 
            lblValueType.AutoSize = true;
            lblValueType.Location = new Point(3, 173);
            lblValueType.Name = "lblValueType";
            lblValueType.Size = new Size(62, 15);
            lblValueType.TabIndex = 8;
            lblValueType.Text = "Value Type";
            // 
            // cmbScanType
            // 
            cmbScanType.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbScanType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbScanType.FormattingEnabled = true;
            cmbScanType.Items.AddRange(new object[] { "Exact value", "Increased value", "Increased value by ...", "Decreased value", "Decreased value by ...", "Value between", "Changed value", "Unchanged value", "Unknown initial value" });
            cmbScanType.Location = new Point(77, 141);
            cmbScanType.Name = "cmbScanType";
            cmbScanType.Size = new Size(339, 23);
            cmbScanType.TabIndex = 7;
            cmbScanType.SelectedIndexChanged += CmbScanType_SelectedIndexChanged;
            // 
            // lblScanType
            // 
            lblScanType.AutoSize = true;
            lblScanType.Location = new Point(3, 144);
            lblScanType.Name = "lblScanType";
            lblScanType.Size = new Size(59, 15);
            lblScanType.TabIndex = 6;
            lblScanType.Text = "Scan Type";
            // 
            // chkHex
            // 
            chkHex.AutoSize = true;
            chkHex.Location = new Point(3, 113);
            chkHex.Name = "chkHex";
            chkHex.Size = new Size(47, 19);
            chkHex.TabIndex = 5;
            chkHex.Text = "Hex";
            chkHex.UseVisualStyleBackColor = true;
            // 
            // txtValue
            // 
            txtValue.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtValue.Font = new Font("Courier New", 9F);
            txtValue.Location = new Point(3, 86);
            txtValue.Name = "txtValue";
            txtValue.Size = new Size(413, 30);
            txtValue.TabIndex = 4;
            txtValue.KeyPress += TxtValue_KeyPress;
            // 
            // lblValue
            // 
            lblValue.AutoSize = true;
            lblValue.Location = new Point(3, 68);
            lblValue.Name = "lblValue";
            lblValue.Size = new Size(35, 15);
            lblValue.TabIndex = 3;
            lblValue.Text = "Value";
            //
            // btnUndoScan
            //
            btnUndoScan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnUndoScan.Enabled = false;
            btnUndoScan.Location = new Point(339, 3);
            btnUndoScan.Name = "btnUndoScan";
            btnUndoScan.Size = new Size(77, 30);
            btnUndoScan.TabIndex = 2;
            btnUndoScan.Text = "Undo Scan";
            btnUndoScan.UseVisualStyleBackColor = false;
            btnUndoScan.Click += BtnUndoScan_Click;
            //
            // btnCancelScan
            //
            btnCancelScan.Enabled = false;
            btnCancelScan.Location = new Point(253, 3);
            btnCancelScan.Name = "btnCancelScan";
            btnCancelScan.Size = new Size(80, 30);
            btnCancelScan.TabIndex = 12;
            btnCancelScan.Text = "Cancel";
            btnCancelScan.UseVisualStyleBackColor = false;
            btnCancelScan.Click += BtnCancelScan_Click;
            //
            // btnNewScan
            //
            btnNewScan.Location = new Point(169, 3);
            btnNewScan.Name = "btnNewScan";
            btnNewScan.Size = new Size(78, 30);
            btnNewScan.TabIndex = 10;
            btnNewScan.Text = "New Scan";
            btnNewScan.IsPrimary = true;
            btnNewScan.UseVisualStyleBackColor = false;
            btnNewScan.Click += BtnNewScan_Click;
            //
            // btnNextScan
            //
            btnNextScan.Enabled = false;
            btnNextScan.Location = new Point(86, 3);
            btnNextScan.Name = "btnNextScan";
            btnNextScan.Size = new Size(77, 30);
            btnNextScan.TabIndex = 1;
            btnNextScan.Text = "Next Scan";
            btnNextScan.UseVisualStyleBackColor = false;
            btnNextScan.Click += BtnNextScan_Click;
            //
            // btnFirstScan
            //
            btnFirstScan.Enabled = false;
            btnFirstScan.Location = new Point(3, 3);
            btnFirstScan.Name = "btnFirstScan";
            btnFirstScan.Size = new Size(77, 30);
            btnFirstScan.TabIndex = 0;
            btnFirstScan.Text = "First Scan";
            btnFirstScan.UseVisualStyleBackColor = false;
            btnFirstScan.Click += BtnFirstScan_Click;
            // 
            // panelBottom
            // 
            panelBottom.Controls.Add(lvAddressList);
            panelBottom.Dock = DockStyle.Fill;
            panelBottom.Location = new Point(0, 0);
            panelBottom.Name = "panelBottom";
            panelBottom.Size = new Size(712, 198);
            panelBottom.TabIndex = 0;
            // 
            // lvAddressList
            // 
            lvAddressList.CheckBoxes = true;
            lvAddressList.Columns.AddRange(new ColumnHeader[] { colActive, colDescription, colAddr, colType, colVal });
            lvAddressList.Dock = DockStyle.Fill;
            lvAddressList.Font = new Font("Courier New", 9F);
            lvAddressList.FullRowSelect = true;
            lvAddressList.GridLines = false;
            lvAddressList.LabelEdit = true;
            lvAddressList.Location = new Point(0, 0);
            lvAddressList.Name = "lvAddressList";
            lvAddressList.Size = new Size(712, 198);
            lvAddressList.TabIndex = 0;
            lvAddressList.UseCompatibleStateImageBehavior = false;
            lvAddressList.View = View.Details;
            lvAddressList.AfterLabelEdit += LvAddressList_AfterLabelEdit;
            lvAddressList.BeforeLabelEdit += LvAddressList_BeforeLabelEdit;
            lvAddressList.ItemCheck += LvAddressList_ItemCheck;
            lvAddressList.DoubleClick += LvAddressList_DoubleClick;
            lvAddressList.MouseClick += LvAddressList_MouseClick;
            // 
            // colActive
            // 
            colActive.Text = " ";
            colActive.Width = 50;
            // 
            // colDescription
            // 
            colDescription.Text = "Description";
            colDescription.Width = 200;
            // 
            // colAddr
            // 
            colAddr.Text = "Address";
            colAddr.Width = 120;
            // 
            // colType
            // 
            colType.Text = "Type";
            colType.Width = 80;
            // 
            // colVal
            // 
            colVal.Text = "Value";
            colVal.Width = 100;
            // 
            // panelScanStatus
            // 
            panelScanProgress.Controls.Add(progressBarScan);
            panelScanProgress.Dock = DockStyle.Top;
            panelScanProgress.Location = new Point(0, 77);
            panelScanProgress.Name = "panelScanProgress";
            panelScanProgress.Size = new Size(712, 5); // Modern thin progress bar area
            panelScanProgress.TabIndex = 5;
            panelScanProgress.Visible = false;
            // 
            // progressBarScan
            // 
            progressBarScan.Dock = DockStyle.Fill;
            progressBarScan.Location = new Point(0, 0);
            progressBarScan.Name = "progressBarScan";
            progressBarScan.Size = new Size(712, 5);
            progressBarScan.TabIndex = 0;
            // 
            // updateTimer
            // 
            updateTimer.Enabled = true;
            updateTimer.Tick += UpdateTimer_Tick;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(712, 761);
            Controls.Add(picLogo);
            Controls.Add(splitContainer1);
            Controls.Add(panelScanProgress);
            Controls.Add(toolStrip1);
            Controls.Add(menuStrip1);
            Controls.Add(progressBarTop);
            Controls.Add(panelHeader);
            MainMenuStrip = menuStrip1;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "CrxMem";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picLogo).EndInit();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            panelTop.ResumeLayout(false);
            panelTop.PerformLayout();
            panelRight.ResumeLayout(false);
            panelRight.PerformLayout();
            grpMemoryScanOptions.ResumeLayout(false);
            grpMemoryScanOptions.PerformLayout();
            panelBottom.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openProcessToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem saveTableToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem loadTableToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem editToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem liveUpdatesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem toolsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem luaMonitorToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem peAnalysisToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem acBypassLabToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem dllInjectionTestToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton btnOpenProcess;
        private System.Windows.Forms.ToolStripButton btnLoadTable;
        private System.Windows.Forms.ToolStripButton btnSaveTable;
        private System.Windows.Forms.Label lblProcessTitle;
        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.PictureBox picLogo;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.ListView lvFoundAddresses;
        private System.Windows.Forms.ColumnHeader colAddress;
        private System.Windows.Forms.ColumnHeader colValue;
        private System.Windows.Forms.ColumnHeader colPrevious;
        private System.Windows.Forms.Label lblFound;
        private CrxMem.Controls.ModernProgressBar progressBarScan;
        private System.Windows.Forms.Panel panelScanProgress;
        private CrxMem.Controls.ModernButton btnMemoryView;
        private System.Windows.Forms.Panel panelRight;
        private CrxMem.Controls.ModernButton btnFirstScan;
        private CrxMem.Controls.ModernButton btnNextScan;
        private CrxMem.Controls.ModernButton btnNewScan;
        private CrxMem.Controls.ModernButton btnCancelScan;
        private CrxMem.Controls.ModernButton btnUndoScan;
        private System.Windows.Forms.Label lblValue;
        private CrxMem.Controls.ModernTextBox txtValue;
        private System.Windows.Forms.CheckBox chkHex;
        private System.Windows.Forms.Label lblScanType;
        private System.Windows.Forms.ComboBox cmbScanType;
        private System.Windows.Forms.Label lblValueType;
        private System.Windows.Forms.ComboBox cmbValueType;
        private System.Windows.Forms.GroupBox grpMemoryScanOptions;
        private System.Windows.Forms.ComboBox cmbMemoryScanOptions;
        private CrxMem.Controls.ModernTextBox txtStartAddress;
        private System.Windows.Forms.Label lblStart;
        private System.Windows.Forms.Label lblStop;
        private CrxMem.Controls.ModernTextBox txtStopAddress;
        private System.Windows.Forms.CheckBox chkWritable;
        private System.Windows.Forms.CheckBox chkCopyOnWrite;
        private System.Windows.Forms.CheckBox chkActiveMemoryOnly;
        private System.Windows.Forms.CheckBox chkFastScan;
        private CrxMem.Controls.ModernButton btnAddAddressManually;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.ListView lvAddressList;
        private System.Windows.Forms.ColumnHeader colActive;
        private System.Windows.Forms.ColumnHeader colDescription;
        private System.Windows.Forms.ColumnHeader colAddr;
        private System.Windows.Forms.ColumnHeader colType;
        private System.Windows.Forms.ColumnHeader colVal;
        private System.Windows.Forms.Timer updateTimer;
        private CrxMem.Controls.ModernProgressBar progressBarTop;
        private System.Windows.Forms.Label lblModule;
        private System.Windows.Forms.ComboBox cmbModule;
    }
}
