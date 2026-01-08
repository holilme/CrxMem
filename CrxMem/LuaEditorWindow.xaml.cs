using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CrxMem.Core;
using CrxMem.LuaScripting;

// Aliases to avoid ambiguity between WPF and WinForms
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CrxMem
{
    /// <summary>
    /// WPF-based Lua Script Editor Window with modern UI matching MainWindow theme.
    /// Features: Line numbers, multi-tab editing.
    /// Uses simple TextBox for fast, responsive editing.
    /// </summary>
    public partial class LuaEditorWindow : Window
    {
        private LuaEngine? _luaEngine;
        private ProcessAccess? _processAccess;
        private MainWindow? _mainWindow;
        private int _newScriptCounter = 1;
        private Dictionary<TabItem, ScriptTabData> _tabData = new();

        private static readonly SolidColorBrush DefaultBrush = new(WpfColor.FromRgb(230, 237, 243));

        public LuaEditorWindow(ProcessAccess? processAccess = null, MainWindow? mainWindow = null)
        {
            InitializeComponent();
            _processAccess = processAccess;
            _mainWindow = mainWindow;

            InitializeLuaEngine();
            CreateNewTab();

            // Keyboard shortcuts
            KeyDown += Window_KeyDown;
        }

        private void InitializeLuaEngine()
        {
            _luaEngine = new LuaEngine();
            _luaEngine.SetProcessAccess(_processAccess);
            _luaEngine.SetMainWindow(_mainWindow);

            _luaEngine.OnOutput += text => AppendOutput(text, DefaultBrush);
            _luaEngine.OnError += text => AppendOutput(text, new SolidColorBrush(WpfColor.FromRgb(248, 81, 73)));
            _luaEngine.OnScriptStarted += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    statusIndicator.Fill = new SolidColorBrush(WpfColor.FromRgb(63, 185, 80));
                    txtStatus.Text = "Running...";
                });
                AppendOutput("[Script started]", new SolidColorBrush(WpfColor.FromRgb(57, 197, 207)));
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        private void CreateNewTab(string? filePath = null, string? content = null)
        {
            var tabItem = new TabItem();

            // Create the editor container with line numbers
            var editorGrid = new Grid();
            editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Line numbers panel
            var lineNumbersScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Background = new SolidColorBrush(WpfColor.FromRgb(22, 27, 34)),
                IsHitTestVisible = false
            };

            var lineNumbers = new TextBlock
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(22, 27, 34)),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(110, 118, 129)),
                FontFamily = new WpfFontFamily("Cascadia Code, Consolas"),
                FontSize = 12,
                Padding = new Thickness(8, 8, 8, 8),
                TextAlignment = TextAlignment.Right,
                Text = "1"
            };
            lineNumbersScroll.Content = lineNumbers;
            Grid.SetColumn(lineNumbersScroll, 0);

            // Code editor - use simple TextBox for speed
            var editor = new WpfTextBox
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(13, 17, 23)),
                Foreground = DefaultBrush,
                FontFamily = new WpfFontFamily("Cascadia Code, Consolas"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                AcceptsTab = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                CaretBrush = new SolidColorBrush(WpfColor.FromRgb(230, 237, 243)),
                SelectionBrush = new SolidColorBrush(WpfColor.FromArgb(100, 88, 166, 255))
            };
            Grid.SetColumn(editor, 1);

            // Set initial content
            editor.Text = content ?? GetDefaultScript();

            // Event handlers
            editor.TextChanged += (s, e) => Editor_TextChanged(editor, lineNumbers, tabItem);
            editor.SelectionChanged += (s, e) => Editor_SelectionChanged(editor);
            editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => SyncLineNumbersScroll(editor, lineNumbersScroll)));

            editorGrid.Children.Add(lineNumbersScroll);
            editorGrid.Children.Add(editor);

            // Tab header
            string tabTitle = filePath != null ? Path.GetFileName(filePath) : $"Script {_newScriptCounter++}";
            tabItem.Header = tabTitle;
            tabItem.Content = editorGrid;
            tabItem.Tag = editor;

            // Track tab data
            _tabData[tabItem] = new ScriptTabData
            {
                FilePath = filePath,
                IsModified = false,
                Editor = editor,
                LineNumbers = lineNumbers,
                LineNumbersScroll = lineNumbersScroll
            };

            tabScripts.Items.Add(tabItem);
            tabScripts.SelectedItem = tabItem;

            // Initial line numbers
            UpdateLineNumbers(editor, lineNumbers);
        }

        private string GetDefaultScript()
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
-- local health = readInteger(""game.exe+0x12345"")
-- print(""Health: "" .. health)

-- Example: Write memory
-- writeInteger(""game.exe+0x12345"", 999)
";
        }

        // ═══════════════════════════════════════════════════════════════
        // LINE NUMBERS
        // ═══════════════════════════════════════════════════════════════

        private void UpdateLineNumbers(WpfTextBox editor, TextBlock lineNumbers)
        {
            try
            {
                int lineCount = editor.LineCount;
                if (lineCount < 1) lineCount = 1;

                var lines = new System.Text.StringBuilder();
                for (int i = 1; i <= lineCount; i++)
                {
                    lines.AppendLine(i.ToString());
                }
                lineNumbers.Text = lines.ToString().TrimEnd();
            }
            catch { }
        }

        private void SyncLineNumbersScroll(WpfTextBox editor, ScrollViewer lineNumbersScroll)
        {
            try
            {
                // Find the ScrollViewer inside the TextBox
                var scrollViewer = FindVisualChild<ScrollViewer>(editor);
                if (scrollViewer != null)
                {
                    lineNumbersScroll.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
                }
            }
            catch { }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ═══════════════════════════════════════════════════════════════

        private void Editor_TextChanged(WpfTextBox editor, TextBlock lineNumbers, TabItem tab)
        {
            UpdateLineNumbers(editor, lineNumbers);

            // Mark as modified
            if (_tabData.TryGetValue(tab, out var data) && !data.IsModified)
            {
                data.IsModified = true;
                if (tab.Header is string header && !header.EndsWith("*"))
                {
                    tab.Header = header + "*";
                }
            }
        }

        private void Editor_SelectionChanged(WpfTextBox editor)
        {
            try
            {
                int line = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex) + 1;
                int col = editor.CaretIndex - editor.GetCharacterIndexFromLineIndex(line - 1) + 1;
                txtLineCol.Text = $"Ln {line}, Col {col}";
            }
            catch { }
        }

        private void Window_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                ExecuteScript_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveScript_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                NewScript_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenScript_Click(sender, e);
                e.Handled = true;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // TOOLBAR ACTIONS
        // ═══════════════════════════════════════════════════════════════

        private void NewScript_Click(object sender, RoutedEventArgs e) => CreateNewTab();

        private void OpenScript_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Filter = "Lua Scripts (*.lua)|*.lua|All Files (*.*)|*.*",
                Title = "Open Lua Script"
            };

            if (dialog.ShowDialog() == true)
            {
                string content = File.ReadAllText(dialog.FileName);
                CreateNewTab(dialog.FileName, content);
            }
        }

        private void SaveScript_Click(object sender, RoutedEventArgs e)
        {
            if (tabScripts.SelectedItem is not TabItem tab) return;
            if (!_tabData.TryGetValue(tab, out var data)) return;

            if (!string.IsNullOrEmpty(data.FilePath))
            {
                File.WriteAllText(data.FilePath, data.Editor.Text);
                data.IsModified = false;
                tab.Header = Path.GetFileName(data.FilePath);
            }
            else
            {
                SaveScriptAs();
            }
        }

        private void SaveScriptAs()
        {
            if (tabScripts.SelectedItem is not TabItem tab) return;
            if (!_tabData.TryGetValue(tab, out var data)) return;

            var dialog = new WpfSaveFileDialog
            {
                Filter = "Lua Scripts (*.lua)|*.lua|All Files (*.*)|*.*",
                Title = "Save Lua Script",
                DefaultExt = "lua"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, data.Editor.Text);
                data.FilePath = dialog.FileName;
                data.IsModified = false;
                tab.Header = Path.GetFileName(dialog.FileName);
            }
        }

        private void ExecuteScript_Click(object sender, RoutedEventArgs e)
        {
            if (tabScripts.SelectedItem is not TabItem tab) return;
            if (!_tabData.TryGetValue(tab, out var data)) return;

            string text = data.Editor.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            txtStatus.Text = "Running...";
            statusIndicator.Fill = new SolidColorBrush(WpfColor.FromRgb(63, 185, 80));

            _luaEngine?.ExecuteScript(text);

            txtStatus.Text = "Ready";
        }

        private void StopScript_Click(object sender, RoutedEventArgs e)
        {
            _luaEngine?.StopScript();
            txtStatus.Text = "Stopped";
            statusIndicator.Fill = new SolidColorBrush(WpfColor.FromRgb(248, 81, 73));
            AppendOutput("[Script stopped]", new SolidColorBrush(WpfColor.FromRgb(248, 81, 73)));
        }

        private void ClearOutput_Click(object sender, RoutedEventArgs e)
        {
            txtOutput.Inlines.Clear();
        }

        private LuaApiHelpWindow? _apiHelpWindow;

        private void ShowApiHelp_Click(object sender, RoutedEventArgs e)
        {
            if (_apiHelpWindow != null && _apiHelpWindow.IsLoaded)
            {
                _apiHelpWindow.Activate();
            }
            else
            {
                _apiHelpWindow = new LuaApiHelpWindow();
                _apiHelpWindow.Owner = this;
                _apiHelpWindow.Closed += (s, ev) => _apiHelpWindow = null;
                _apiHelpWindow.Show();
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton btn && btn.DataContext is TabItem tab)
            {
                CloseTab(tab);
            }
            else if (tabScripts.SelectedItem is TabItem selectedTab)
            {
                CloseTab(selectedTab);
            }
        }

        private void CloseTab(TabItem tab)
        {
            if (tabScripts.Items.Count <= 1) return;

            if (_tabData.TryGetValue(tab, out var data) && data.IsModified)
            {
                var result = WpfMessageBox.Show(
                    "Save changes before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes) SaveScript_Click(this, new RoutedEventArgs());
            }

            _tabData.Remove(tab);
            tabScripts.Items.Remove(tab);
        }

        // ═══════════════════════════════════════════════════════════════
        // OUTPUT
        // ═══════════════════════════════════════════════════════════════

        private void AppendOutput(string text, WpfBrush color)
        {
            Dispatcher.Invoke(() =>
            {
                txtOutput.Inlines.Add(new Run(text + "\n") { Foreground = color });
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // WINDOW CHROME
        // ═══════════════════════════════════════════════════════════════

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            btnMaximize.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Width + e.HorizontalChange > MinWidth)
                Width += e.HorizontalChange;
            if (Height + e.VerticalChange > MinHeight)
                Height += e.VerticalChange;
        }

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC METHODS
        // ═══════════════════════════════════════════════════════════════

        public void UpdateProcessAccess(ProcessAccess? process)
        {
            _processAccess = process;
            _luaEngine?.SetProcessAccess(process);
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER CLASS
        // ═══════════════════════════════════════════════════════════════

        private class ScriptTabData
        {
            public string? FilePath { get; set; }
            public bool IsModified { get; set; }
            public WpfTextBox Editor { get; set; } = null!;
            public TextBlock LineNumbers { get; set; } = null!;
            public ScrollViewer LineNumbersScroll { get; set; } = null!;
        }
    }
}
