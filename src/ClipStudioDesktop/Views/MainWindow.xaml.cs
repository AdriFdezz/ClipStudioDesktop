using ClipStudioDesktop.ViewModels;
using System.Windows;
using System.Media;
using System.IO;

namespace ClipStudioDesktop.Views
{
    /// <summary>
    /// Ventana principal de la aplicación.
    /// Define la interfaz, gestiona hotkeys globales y el comportamiento de la ventana.
    /// </summary>
    public partial class MainWindow : Window
    {
        // Constructor por defecto para el diseñador XAML y tiempo de ejecución
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Inicializa la ventana con el ViewModel inyectado.
        /// Configura el centrado en el monitor principal y la intercepción de teclas especiales.
        /// </summary>
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Asegura que la ventana se centre en el monitor principal al hacerse visible
            this.IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    CenterOnPrimaryMonitor();
                }
            };

            // Manejar PreviewKeyDown a nivel ventana para deshabilitar comportamiento por defecto de la tecla ALT (AccessKeys)
            this.PreviewKeyDown += (s, e) =>
            {
                // Si se presiona ALT (Tecla de Sistema)
                if (e.Key == System.Windows.Input.Key.System && 
                    (e.SystemKey == System.Windows.Input.Key.LeftAlt || e.SystemKey == System.Windows.Input.Key.RightAlt || e.SystemKey == System.Windows.Input.Key.F10))
                {
                    // Verificar si el foco está en un TextBox de Hotkey. Si es así, permitir que pase para grabar "Alt+..."
                    var focusedElement = System.Windows.Input.Keyboard.FocusedElement as System.Windows.Controls.TextBox;
                    if (focusedElement != null && focusedElement.Name != null && focusedElement.Name.Contains("HotkeyTextBox"))
                    {
                        return;
                    }

                    // De lo contrario, consumir el evento para prevenir que Windows enfoque el menú/teclas de acceso
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// Centra la ventana en el área de trabajo del monitor principal.
        /// </summary>
        private void CenterOnPrimaryMonitor()
        {
            try
            {
                // Obtener dimensiones del área de trabajo (excluyendo barra de tareas)
                double screenWidth = SystemParameters.WorkArea.Width;
                double screenHeight = SystemParameters.WorkArea.Height;
                double windowWidth = this.ActualWidth;
                double windowHeight = this.ActualHeight;

                // Si ActualWidth es 0 (primera carga), usar Width/Height definido en XAML
                if (windowWidth == 0) windowWidth = this.Width;
                if (windowHeight == 0) windowHeight = this.Height;

                this.Left = (screenWidth / 2) - (windowWidth / 2);
                this.Top = (screenHeight / 2) - (windowHeight / 2);
            }
            catch
            {
                // Fallback: comportamiento de centrado estándar
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // En lugar de cerrar, ocultamos la ventana para mantener la app en la bandeja del sistema (Tray)
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Deseleccionar filas del DataGrid al perder foco visual
            HotkeysGrid.UnselectAll();
        }

        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            // Guardar valor original por si se cancela
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

            // Si el usuario no presionó una combinación válida, revertir
            if (textBox.Text == "Presiona teclas...")
            {
                textBox.Text = textBox.Tag as string ?? "";
            }
            
            textBox.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            textBox.Tag = null; // Resetear tag
        }

        private void HotkeyTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;
            
            // Auto-foco al iniciar edición
            textBox.Focus();
        }

        /// <summary>
        /// Maneja la captura de teclas para definir nuevos atajos.
        /// Construye la cadena (ej. "Ctrl+Alt+S") basada en las teclas presionadas.
        /// </summary>
        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            e.Handled = true;

            // Manejar Escape para cancelar
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                textBox.Text = textBox.Tag as string ?? "";
                // Quitar foco para terminar edición
                System.Windows.Input.Keyboard.ClearFocus();
                return;
            }

            // Obtener modificadores
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            
            // Obtener tecla
            var key = e.Key;
            if (key == System.Windows.Input.Key.System)
            {
                key = e.SystemKey;
            }

            // Ignorar teclas modificadoras por sí solas (esperar a que se presione otra tecla)
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

            // Construir cadena
            var sb = new System.Text.StringBuilder();
            
            if ((modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                sb.Append("Ctrl+");
            if ((modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift)
                sb.Append("Shift+");
            if ((modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt)
                sb.Append("Alt+");

            // Manejar dígitos numéricos (Key.D1 -> "1", etc.)
            string keyName = key.ToString();
            if (keyName.StartsWith("D") && keyName.Length == 2 && char.IsDigit(keyName[1]))
            {
                keyName = keyName[1].ToString();
            }
            
            sb.Append(keyName);

            textBox.Text = sb.ToString();
            textBox.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            
            // Forzar actualización del Binding manualmente
            var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            binding?.UpdateSource();
        }

        private void TestAudioDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Reproducir sonido de notificación para probar dispositivo
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
                    player.PlaySync(); // Usar PlaySync en lugar de Play
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

        private void ComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox && !comboBox.IsDropDownOpen)
            {
                e.Handled = true;
            }
        }
    }
}
