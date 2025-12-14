using ClipStudioDesktop.ViewModels;
using System.Windows;

namespace ClipStudioDesktop.Views
{
    public partial class MainWindow : Window
    {
        // Default constructor for designer and XAML
        public MainWindow()
        {
            InitializeComponent();
        }

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Instead of closing, just hide the window so the app keeps running in tray
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            HotkeysGrid.UnselectAll();
        }

        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            // Store original value
            if (textBox.Tag == null)
            {
                textBox.Tag = textBox.Text;
            }

            textBox.Text = "Presiona teclas...";
            textBox.Foreground = System.Windows.Media.Brushes.Gray;
        }

        private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            // If user didn't press any valid key combination, revert
            if (textBox.Text == "Presiona teclas...")
            {
                textBox.Text = textBox.Tag as string ?? "";
            }
            
            textBox.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            textBox.Tag = null; // Reset tag
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            e.Handled = true;

            // Handle Escape to cancel
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                textBox.Text = textBox.Tag as string ?? "";
                // Move focus away to trigger LostFocus and end editing
                System.Windows.Input.Keyboard.ClearFocus();
                return;
            }

            // Get modifiers
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            
            // Get key
            var key = e.Key;
            if (key == System.Windows.Input.Key.System)
            {
                key = e.SystemKey;
            }

            // Ignore modifier keys by themselves
            if (key == System.Windows.Input.Key.LeftCtrl || 
                key == System.Windows.Input.Key.RightCtrl || 
                key == System.Windows.Input.Key.LeftAlt || 
                key == System.Windows.Input.Key.RightAlt || 
                key == System.Windows.Input.Key.LeftShift || 
                key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin || 
                key == System.Windows.Input.Key.RWin)
            {
                return;
            }

            // Build string
            var sb = new System.Text.StringBuilder();
            
            if ((modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                sb.Append("Ctrl+");
            if ((modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift)
                sb.Append("Shift+");
            if ((modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt)
                sb.Append("Alt+");

            // Handle digits specifically if needed, but Key.ToString() usually works well enough for basic keys
            // For D1, D2... we might want just 1, 2...
            string keyName = key.ToString();
            if (keyName.StartsWith("D") && keyName.Length == 2 && char.IsDigit(keyName[1]))
            {
                keyName = keyName[1].ToString();
            }
            
            sb.Append(keyName);

            textBox.Text = sb.ToString();
            textBox.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            
            // Force binding update
            var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            binding?.UpdateSource();
        }
    }
}
