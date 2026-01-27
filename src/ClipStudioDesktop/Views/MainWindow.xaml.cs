using ClipStudioDesktop.ViewModels;
using System.Windows;
using System.Media;
using System.IO;

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

            // Ensure the window centers on the primary monitor every time it becomes visible
            this.IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    CenterOnPrimaryMonitor();
                }
            };

            // Handle PreviewKeyDown at window level to disable ALT key default behavior (AccessKeys)
            this.PreviewKeyDown += (s, e) =>
            {
                // If ALT is pressed (System key)
                if (e.Key == System.Windows.Input.Key.System && 
                    (e.SystemKey == System.Windows.Input.Key.LeftAlt || e.SystemKey == System.Windows.Input.Key.RightAlt || e.SystemKey == System.Windows.Input.Key.F10))
                {
                    // Check if focus is on a HotkeyTextBox. If so, let it pass so the user can record "Alt+..."
                    var focusedElement = System.Windows.Input.Keyboard.FocusedElement as System.Windows.Controls.TextBox;
                    if (focusedElement != null && focusedElement.Name != null && focusedElement.Name.Contains("HotkeyTextBox"))
                    {
                        return;
                    }

                    // Otherwise, swallow the key to prevent Windows from focusing the menu/access keys
                    e.Handled = true;
                }
            };
        }

        private void CenterOnPrimaryMonitor()
        {
            try
            {
                double screenWidth = SystemParameters.WorkArea.Width;
                double screenHeight = SystemParameters.WorkArea.Height;
                double windowWidth = this.ActualWidth;
                double windowHeight = this.ActualHeight;

                // If ActualWidth is 0 (first load), use Width/Height from XAML
                if (windowWidth == 0) windowWidth = this.Width;
                if (windowHeight == 0) windowHeight = this.Height;

                this.Left = (screenWidth / 2) - (windowWidth / 2);
                this.Top = (screenHeight / 2) - (windowHeight / 2);
            }
            catch
            {
                // Fallback to center screen behavior if calculation fails
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
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

        private void HotkeyTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;
            
            // Auto-focus when editing starts
            textBox.Focus();
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

        private void TestAudioDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Play notification sound to test audio device
                string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Notification_sound.wav");
                
                if (!File.Exists(soundPath))
                {
                    System.Windows.MessageBox.Show(
                        $"No se encontró el archivo de sonido:\n{soundPath}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                
                using (var player = new SoundPlayer(soundPath))
                {
                    player.PlaySync(); // Use PlaySync instead of Play
                }
                
                System.Windows.MessageBox.Show(
                    "Si escuchaste el sonido, tu dispositivo de audio de Windows está funcionando.\n\n" +
                    "IMPORTANTE: Para grabar el audio del sistema, debes seleccionar un dispositivo " +
                    "con 'VoiceMeeter' en el nombre, o habilitar 'Mezcla estéreo' en Windows.\n\n" +
                    "Los dispositivos que NO tienen 'VoiceMeeter' solo capturan micrófono.",
                    "Test de Audio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error al reproducir el sonido: {ex.Message}\n\nPath: {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Notification_sound.wav")}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
