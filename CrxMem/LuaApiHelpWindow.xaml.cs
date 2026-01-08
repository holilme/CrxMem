using System.Windows;
using System.Windows.Input;

namespace CrxMem
{
    /// <summary>
    /// WPF-based Lua API Help dialog with themed styling matching the main application.
    /// </summary>
    public partial class LuaApiHelpWindow : Window
    {
        public LuaApiHelpWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Show/hide placeholder based on text content
            if (txtSearchPlaceholder != null)
            {
                txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // Simple search - scroll to first matching section header
            string searchText = txtSearch.Text.ToLowerInvariant();
            if (!string.IsNullOrEmpty(searchText))
            {
                foreach (var child in contentPanel.Children)
                {
                    if (child is System.Windows.Controls.TextBlock tb &&
                        tb.Text.ToLowerInvariant().Contains(searchText))
                    {
                        tb.BringIntoView();
                        break;
                    }
                }
            }
        }
    }
}
