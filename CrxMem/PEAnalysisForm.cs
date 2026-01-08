using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using CrxMem.Core;

namespace CrxMem
{
    public partial class PEAnalysisForm : Form
    {
        private readonly ProcessAccess _process;
        private PEAnalyzer.PEInfo? _peInfo;
        private AntiCheatDetector.DetectionResult? _acResult;

        // UI Controls
        private TabControl tabControl = null!;
        private Panel summaryPanel = null!;
        private Label lblArchitecture = null!;
        private Label lblASLR = null!;
        private Label lblDEP = null!;
        private Label lblCFG = null!;
        private Label lblSEH = null!;
        private Label lblSigned = null!;
        private Label lblCompiled = null!;
        private Label lblSubsystem = null!;
        private Label lblThreatLevel = null!;
        private Label lblTLS = null!;
        private Label lblPacked = null!;

        private ListView lvImports = null!;
        private ListView lvExports = null!;
        private ListView lvSections = null!;
        private ListView lvAntiCheats = null!;
        private TextBox txtInfo = null!;
        private TextBox txtSecurity = null!;

        private Button btnExportTxt = null!;
        private Button btnExportJson = null!;
        private Button btnExportHtml = null!;
        private Button btnRefresh = null!;

        public PEAnalysisForm(ProcessAccess process)
        {
            _process = process;

            InitializeComponent();
            AnalyzeProcess();
        }

        private void InitializeComponent()
        {
            Text = $"PE Analysis - {_process.Target.ProcessName}";
            Size = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterParent;

            // Summary panel - Increased height for more info
            summaryPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 140,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(10)
            };

            int labelY = 10;
            int labelSpacing = 18;

            // Column 1 - Security features
            lblArchitecture = CreateLabel("Architecture: Analyzing...", 10, labelY);
            lblASLR = CreateLabel("ASLR: ...", 10, labelY + labelSpacing);
            lblDEP = CreateLabel("DEP: ...", 10, labelY + labelSpacing * 2);
            lblCFG = CreateLabel("CFG: ...", 10, labelY + labelSpacing * 3);
            lblSEH = CreateLabel("SEH: ...", 10, labelY + labelSpacing * 4);

            // Column 2 - File info
            lblCompiled = CreateLabel("Compiled: ...", 280, labelY);
            lblSubsystem = CreateLabel("Subsystem: ...", 280, labelY + labelSpacing);
            lblSigned = CreateLabel("Signed: ...", 280, labelY + labelSpacing * 2);
            lblTLS = CreateLabel("TLS Callbacks: ...", 280, labelY + labelSpacing * 3);

            // Column 3 - Threat info
            lblThreatLevel = CreateLabel("Threat Level: ...", 550, labelY + labelSpacing);
            lblThreatLevel.Font = new Font(lblThreatLevel.Font, FontStyle.Bold);
            lblThreatLevel.ForeColor = Color.Green;
            lblPacked = CreateLabel("Packer: ...", 550, labelY + labelSpacing * 2);

            summaryPanel.Controls.AddRange(new Control[] {
                lblArchitecture, lblASLR, lblDEP, lblCFG, lblSEH,
                lblCompiled, lblSubsystem, lblSigned, lblTLS,
                lblThreatLevel, lblPacked
            });

            // Tab control
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // Imports tab
            var importTab = new TabPage("Imports");
            lvImports = CreateListView(new[] {
                ("DLL Name", 200),
                ("Category", 120),
                ("Functions", 500)
            });
            lvImports.Dock = DockStyle.Fill;
            importTab.Controls.Add(lvImports);

            // Exports tab
            var exportTab = new TabPage("Exports");
            lvExports = CreateListView(new[] {
                ("Function Name", 300),
                ("Ordinal", 80),
                ("RVA", 100)
            });
            lvExports.Dock = DockStyle.Fill;
            exportTab.Controls.Add(lvExports);

            // Sections tab
            var sectionTab = new TabPage("Sections");
            lvSections = CreateListView(new[] {
                ("Name", 100),
                ("Virtual Address", 120),
                ("Virtual Size", 100),
                ("Raw Size", 100),
                ("Entropy", 80),
                ("Flags", 200)
            });
            lvSections.Dock = DockStyle.Fill;
            sectionTab.Controls.Add(lvSections);

            // Anti-Cheat tab
            var acTab = new TabPage("Anti-Cheat");
            lvAntiCheats = CreateListView(new[] {
                ("Name", 200),
                ("Type", 100),
                ("Severity", 100),
                ("Components", 300),
                ("Description", 250)
            });
            lvAntiCheats.Dock = DockStyle.Fill;
            acTab.Controls.Add(lvAntiCheats);

            // Security tab
            var securityTab = new TabPage("Security");
            txtSecurity = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9)
            };
            securityTab.Controls.Add(txtSecurity);

            // Info tab
            var infoTab = new TabPage("PE Info");
            txtInfo = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9)
            };
            infoTab.Controls.Add(txtInfo);

            tabControl.TabPages.AddRange(new[] {
                importTab, exportTab, sectionTab, acTab, securityTab, infoTab
            });

            // Bottom panel with buttons
            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(10)
            };

            btnExportTxt = new Button
            {
                Text = "Export to TXT",
                Location = new Point(10, 10),
                Size = new Size(120, 30)
            };
            btnExportTxt.Click += BtnExportTxt_Click;

            btnExportJson = new Button
            {
                Text = "Export to JSON",
                Location = new Point(140, 10),
                Size = new Size(120, 30)
            };
            btnExportJson.Click += BtnExportJson_Click;

            btnExportHtml = new Button
            {
                Text = "Export to HTML",
                Location = new Point(270, 10),
                Size = new Size(120, 30)
            };
            btnExportHtml.Click += BtnExportHtml_Click;

            btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new Point(400, 10),
                Size = new Size(120, 30)
            };
            btnRefresh.Click += BtnRefresh_Click;

            buttonPanel.Controls.AddRange(new Control[] { btnExportTxt, btnExportJson, btnExportHtml, btnRefresh });

            // Add all to form
            Controls.Add(tabControl);
            Controls.Add(summaryPanel);
            Controls.Add(buttonPanel);
        }

        private Label CreateLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true
            };
        }

        private ListView CreateListView((string Name, int Width)[] columns)
        {
            var lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };

            foreach (var col in columns)
            {
                lv.Columns.Add(col.Name, col.Width);
            }

            return lv;
        }

        private void AnalyzeProcess()
        {
            try
            {
                // Get main module path
                string? exePath = _process.Target.MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    MessageBox.Show("Could not get process executable path.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Analyze PE
                _peInfo = PEAnalyzer.AnalyzeFile(exePath);

                if (!_peInfo.IsValid)
                {
                    MessageBox.Show("Failed to analyze PE file.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Detect anti-cheats
                _acResult = AntiCheatDetector.DetectInProcess(_process);

                // Update UI
                UpdateSummary();
                UpdateImports();
                UpdateExports();
                UpdateSections();
                UpdateAntiCheats();
                UpdateSecurity();
                UpdateInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing process: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateSummary()
        {
            if (_peInfo == null) return;

            // Column 1 - Security features
            lblArchitecture.Text = $"Architecture: {(_peInfo.Is64Bit ? "x64 (64-bit)" : "x86 (32-bit)")}";
            lblASLR.Text = $"ASLR: {(_peInfo.HasASLR ? "✓ Enabled" : "✗ Disabled")}";
            lblASLR.ForeColor = _peInfo.HasASLR ? Color.Green : Color.Red;

            lblDEP.Text = $"DEP: {(_peInfo.HasDEP ? "✓ Enabled" : "✗ Disabled")}";
            lblDEP.ForeColor = _peInfo.HasDEP ? Color.Green : Color.Red;

            lblCFG.Text = $"CFG: {(_peInfo.HasCFG ? "✓ Enabled" : "✗ Disabled")}";
            lblCFG.ForeColor = _peInfo.HasCFG ? Color.Green : Color.Red;

            lblSEH.Text = $"SEH: {(_peInfo.HasSEH ? "✓ Enabled" : "✗ Disabled")}";
            lblSEH.ForeColor = _peInfo.HasSEH ? Color.Green : Color.Red;

            // Column 2 - File info
            lblCompiled.Text = $"Compiled: {_peInfo.CompilationTime:yyyy-MM-dd HH:mm}";
            lblSubsystem.Text = $"Subsystem: {_peInfo.SubsystemName}";
            lblSigned.Text = $"Signed: {_peInfo.SignatureInfo}";
            lblSigned.ForeColor = _peInfo.HasAuthenticode ? Color.Green : Color.Gray;

            lblTLS.Text = $"TLS Callbacks: {(_peInfo.HasTLSCallbacks ? "⚠ YES (Anti-Debug)" : "No")}";
            lblTLS.ForeColor = _peInfo.HasTLSCallbacks ? Color.Orange : Color.Green;

            // Column 3 - Threat analysis
            if (_peInfo.DetectedPackers.Count > 0)
            {
                string firstPacker = _peInfo.DetectedPackers[0];
                if (firstPacker.Length > 30)
                    firstPacker = firstPacker.Substring(0, 27) + "...";
                lblPacked.Text = $"Packer: {firstPacker}";
                lblPacked.ForeColor = Color.Orange;
            }
            else
            {
                lblPacked.Text = "Packer: None detected";
                lblPacked.ForeColor = Color.Green;
            }

            if (_acResult != null)
            {
                lblThreatLevel.Text = $"Threat Level: {_acResult.OverallThreat}";
                lblThreatLevel.ForeColor = _acResult.OverallThreat switch
                {
                    "None" => Color.Green,
                    "Low" => Color.Blue,
                    "Medium (User-Mode)" => Color.Orange,
                    "High (Kernel-Mode)" => Color.Red,
                    _ => Color.DarkRed
                };
            }
        }

        private void UpdateImports()
        {
            if (_peInfo == null) return;

            lvImports.Items.Clear();

            foreach (var import in _peInfo.Imports.OrderBy(i => i.Category).ThenBy(i => i.DLLName))
            {
                string functions = import.Functions.Count > 5
                    ? $"{string.Join(", ", import.Functions.Take(5))}... ({import.Functions.Count} total)"
                    : string.Join(", ", import.Functions);

                var item = new ListViewItem(new[] {
                    import.DLLName,
                    import.Category,
                    functions
                });

                lvImports.Items.Add(item);
            }
        }

        private void UpdateExports()
        {
            if (_peInfo == null) return;

            lvExports.Items.Clear();

            if (_peInfo.Exports.Count == 0)
            {
                var item = new ListViewItem(new[] { "(No exports - this is an EXE, not a DLL)", "", "" });
                lvExports.Items.Add(item);
                return;
            }

            foreach (var export in _peInfo.Exports.OrderBy(e => e.Name))
            {
                var item = new ListViewItem(new[] {
                    export.Name,
                    export.Ordinal.ToString(),
                    $"0x{export.RVA:X8}"
                });

                lvExports.Items.Add(item);
            }
        }

        private void UpdateSections()
        {
            if (_peInfo == null) return;

            lvSections.Items.Clear();

            foreach (var section in _peInfo.Sections)
            {
                var item = new ListViewItem(new[] {
                    section.Name,
                    $"0x{section.VirtualAddress:X8}",
                    $"0x{section.VirtualSize:X}",
                    $"0x{section.RawSize:X}",
                    $"{section.Entropy:F2}",
                    section.Flags
                });

                // Highlight suspicious high-entropy sections
                if (section.Entropy > 7.2)
                {
                    item.BackColor = Color.Yellow;
                }

                lvSections.Items.Add(item);
            }
        }

        private void UpdateAntiCheats()
        {
            if (_acResult == null) return;

            lvAntiCheats.Items.Clear();

            if (_acResult.DetectedAntiCheats.Count == 0)
            {
                var item = new ListViewItem(new[] {
                    "(No anti-cheat detected)", "", "", "", ""
                });
                item.ForeColor = Color.Green;
                lvAntiCheats.Items.Add(item);
                return;
            }

            foreach (var ac in _acResult.DetectedAntiCheats.OrderByDescending(a => a.Severity))
            {
                var item = new ListViewItem(new[] {
                    ac.Name,
                    ac.Type,
                    ac.Severity,
                    string.Join(", ", ac.DetectedComponents),
                    ac.Description
                });

                item.ForeColor = ac.Severity switch
                {
                    "Extreme" => Color.DarkRed,
                    "High" => Color.Red,
                    "Medium" => Color.Orange,
                    _ => Color.Blue
                };

                lvAntiCheats.Items.Add(item);
            }
        }

        private void UpdateSecurity()
        {
            if (_peInfo == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("  SECURITY FEATURES ANALYSIS");
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine($"ASLR (Address Space Layout Randomization): {(_peInfo.HasASLR ? "✓ ENABLED" : "✗ DISABLED")}");
            sb.AppendLine(_peInfo.HasASLR
                ? "  → Memory addresses are randomized, making exploits harder."
                : "  → WARNING: Predictable memory layout makes exploitation easier!");
            sb.AppendLine();

            sb.AppendLine($"DEP (Data Execution Prevention): {(_peInfo.HasDEP ? "✓ ENABLED" : "✗ DISABLED")}");
            sb.AppendLine(_peInfo.HasDEP
                ? "  → Prevents code execution from data sections."
                : "  → WARNING: Code can execute from data/stack!");
            sb.AppendLine();

            sb.AppendLine($"SEH (Structured Exception Handling): {(_peInfo.HasSEH ? "✓ ENABLED" : "✗ DISABLED")}");
            sb.AppendLine(_peInfo.HasSEH
                ? "  → Exception handlers are protected."
                : "  → No SEH protection (NO_SEH flag set).");
            sb.AppendLine();

            sb.AppendLine($"CFG (Control Flow Guard): {(_peInfo.HasCFG ? "✓ ENABLED" : "✗ DISABLED")}");
            sb.AppendLine(_peInfo.HasCFG
                ? "  → Function pointers are validated, preventing hijacking."
                : "  → No control flow validation.");
            sb.AppendLine();

            sb.AppendLine($"Digital Signature: {_peInfo.SignatureInfo}");
            sb.AppendLine(_peInfo.HasAuthenticode
                ? "  → File has Authenticode signature (verify with sigcheck)."
                : "  → File is not digitally signed.");
            sb.AppendLine();

            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine("  PACKER/PROTECTOR DETECTION");
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine();

            if (_peInfo.DetectedPackers.Count > 0)
            {
                sb.AppendLine("⚠ DETECTED PACKERS/PROTECTORS:");
                foreach (var packer in _peInfo.DetectedPackers)
                {
                    sb.AppendLine($"  • {packer}");
                }
                sb.AppendLine();
                sb.AppendLine("Packed executables may contain obfuscated or encrypted code.");
            }
            else
            {
                sb.AppendLine("✓ No known packers detected.");
                sb.AppendLine("  Code appears to be unpacked/unprotected.");
            }
            sb.AppendLine();

            if (_peInfo.HasOverlay)
            {
                sb.AppendLine($"⚠ Overlay detected: {_peInfo.OverlaySize} bytes after PE sections");
                sb.AppendLine("  → May contain resources, data, or hidden payloads.");
            }
            sb.AppendLine();

            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine("  SUSPICIOUS INDICATORS");
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine();

            if (_peInfo.SuspiciousIndicators.Count > 0)
            {
                sb.AppendLine("⚠ SUSPICIOUS BEHAVIORS DETECTED:");
                foreach (var indicator in _peInfo.SuspiciousIndicators.Take(20)) // Limit to 20
                {
                    sb.AppendLine($"  • {indicator}");
                }
                if (_peInfo.SuspiciousIndicators.Count > 20)
                {
                    sb.AppendLine($"  ... and {_peInfo.SuspiciousIndicators.Count - 20} more");
                }
                sb.AppendLine();
                sb.AppendLine("These indicators may suggest malicious intent or evasion techniques.");
            }
            else
            {
                sb.AppendLine("✓ No suspicious indicators detected.");
            }

            txtSecurity.Text = sb.ToString();
        }

        private void UpdateInfo()
        {
            if (_peInfo == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("  PE FILE INFORMATION");
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine($"File Path:         {_peInfo.FilePath}");
            sb.AppendLine($"Architecture:      {(_peInfo.Is64Bit ? "x64 (64-bit)" : "x86 (32-bit)")}");
            sb.AppendLine($"Image Base:        0x{_peInfo.ImageBase:X}");
            sb.AppendLine($"Entry Point RVA:   0x{_peInfo.EntryPoint:X}");
            sb.AppendLine($"Subsystem:         {_peInfo.SubsystemName}");
            sb.AppendLine($"Compiled:          {_peInfo.CompilationTime:yyyy-MM-dd HH:mm:ss UTC}");
            sb.AppendLine($"Digital Signature: {_peInfo.SignatureInfo}");
            sb.AppendLine($"Import Hash:       {_peInfo.ImpHash}");
            sb.AppendLine();

            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine($"Total Imports:     {_peInfo.Imports.Count} DLLs, {_peInfo.Imports.Sum(i => i.Functions.Count)} functions");
            sb.AppendLine($"Total Exports:     {_peInfo.Exports.Count} functions");
            sb.AppendLine($"Total Sections:    {_peInfo.Sections.Count}");
            sb.AppendLine($"TLS Callbacks:     {(_peInfo.HasTLSCallbacks ? "YES (Anti-debugging)" : "No")}");
            if (_peInfo.HasOverlay)
            {
                sb.AppendLine($"Overlay Data:      {_peInfo.OverlaySize} bytes");
            }
            sb.AppendLine();

            if (_peInfo.DetectedPackers.Count > 0)
            {
                sb.AppendLine("───────────────────────────────────────────────");
                sb.AppendLine("  DETECTED PACKERS/PROTECTORS");
                sb.AppendLine("───────────────────────────────────────────────");
                sb.AppendLine();
                foreach (var packer in _peInfo.DetectedPackers)
                {
                    sb.AppendLine($"  • {packer}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine("  IMPORT CATEGORIES");
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine();

            var categories = _peInfo.Imports.GroupBy(i => i.Category).OrderByDescending(g => g.Count());
            foreach (var cat in categories)
            {
                sb.AppendLine($"  {cat.Key,-20} {cat.Count()} DLLs");
            }

            if (_peInfo.SuspiciousIndicators.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("───────────────────────────────────────────────");
                sb.AppendLine($"  SUSPICIOUS INDICATORS ({_peInfo.SuspiciousIndicators.Count})");
                sb.AppendLine("───────────────────────────────────────────────");
                sb.AppendLine();
                sb.AppendLine("See Security tab for full list.");
            }

            txtInfo.Text = sb.ToString();
        }

        private void BtnExportTxt_Click(object? sender, EventArgs e)
        {
            if (_peInfo == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = $"{_process.Target.ProcessName}_analysis.txt"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("PE ANALYSIS REPORT");
                    sb.AppendLine($"Generated: {DateTime.Now}");
                    sb.AppendLine($"Process: {_process.Target.ProcessName}");
                    sb.AppendLine("═══════════════════════════════════════════════");
                    sb.AppendLine();

                    sb.AppendLine(txtInfo.Text);
                    sb.AppendLine();
                    sb.AppendLine(txtSecurity.Text);
                    sb.AppendLine();

                    if (_acResult != null && _acResult.DetectedAntiCheats.Count > 0)
                    {
                        sb.AppendLine("═══════════════════════════════════════════════");
                        sb.AppendLine("  DETECTED ANTI-CHEATS");
                        sb.AppendLine("═══════════════════════════════════════════════");
                        sb.AppendLine();

                        foreach (var ac in _acResult.DetectedAntiCheats)
                        {
                            sb.AppendLine($"Name:        {ac.Name}{(ac.IsCustom ? " [CUSTOM/HOMEBREW]" : "")}");
                            sb.AppendLine($"Type:        {ac.Type}{(ac.IsCustom ? " (Homebrew/Custom)" : "")}");
                            sb.AppendLine($"Severity:    {ac.Severity}");
                            sb.AppendLine($"Version:     {ac.Version}");
                            sb.AppendLine($"Description: {ac.Description}");
                            sb.AppendLine($"Components:");
                            foreach (var comp in ac.DetectedComponents)
                            {
                                sb.AppendLine($"  • {comp}");
                            }
                            sb.AppendLine();
                        }
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString());
                    MessageBox.Show("Export successful!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExportJson_Click(object? sender, EventArgs e)
        {
            if (_peInfo == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"{_process.Target.ProcessName}_analysis.json"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var data = new
                    {
                        timestamp = DateTime.Now,
                        process = _process.Target.ProcessName,
                        peInfo = _peInfo,
                        antiCheat = _acResult
                    };

                    string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, json);
                    MessageBox.Show("Export successful!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExportHtml_Click(object? sender, EventArgs e)
        {
            if (_peInfo == null || _acResult == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "HTML Files (*.html)|*.html",
                FileName = $"{_process.Target.ProcessName}_analysis.html"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string html = HtmlExporter.GeneratePEAnalysisReport(_peInfo, _acResult, _process.Target.ProcessName);
                    File.WriteAllText(sfd.FileName, html);
                    MessageBox.Show($"HTML report generated successfully!\n\nSaved to: {sfd.FileName}",
                        "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"HTML export failed: {ex.Message}\n\n{ex.StackTrace}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnRefresh_Click(object? sender, EventArgs e)
        {
            AnalyzeProcess();
        }
    }
}
