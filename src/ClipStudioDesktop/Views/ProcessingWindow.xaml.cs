using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace ClipStudioDesktop.Views
{
    /// <summary>
    /// Ventana flotante que muestra el progreso de conversión/procesamiento.
    /// Incluye barra de progreso, tiempo estimado y protección contra cierres accidentales.
    /// </summary>
    public partial class ProcessingWindow : Window
    {
        private bool _wasTopmost = true;
        private bool _isClosingProgrammatically = false;

        /// <summary>
        /// Evento lanzado cuando el usuario confirma la cancelación de la conversión.
        /// </summary>
        public event EventHandler? CancellationRequested;

        public ProcessingWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Posicionar ventana en el centro de la pantalla principal
            var primaryScreen = Screen.PrimaryScreen;
            if (primaryScreen != null)
            {
                var workingArea = primaryScreen.WorkingArea;
                Left = workingArea.Left + (workingArea.Width - Width) / 2;
                Top = workingArea.Top + (workingArea.Height - Height) / 2;
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Cuando el usuario pincha fuera, mantenerlo detrás pero visible (sin TopMost)
            Topmost = false;
            _wasTopmost = false;
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // Al volver a enfocar, restaurar TopMost
            if (!_wasTopmost)
            {
                Topmost = true;
                _wasTopmost = true;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Si se cierra programáticamente (conversión terminada), permitirlo
            if (_isClosingProgrammatically)
            {
                return;
            }

            // El usuario intenta cerrar manualmente - mostrar confirmación
            var result = MessageBox.Show(
                "¿Estás seguro de que deseas cancelar la conversión?\n\nSi cancelas, perderás el contenido grabado.",
                "Cancelar Conversión",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                // Usuario confirmó - lanzar evento de cancelación
                CancellationRequested?.Invoke(this, EventArgs.Empty);
                _isClosingProgrammatically = true;
            }
            else
            {
                // Usuario canceló el cierre - mantener ventana
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Cierra la ventana sin confirmación (usado al terminar la conversión con éxito).
        /// </summary>
        public void CloseWithoutConfirmation()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => CloseWithoutConfirmation());
                return;
            }
            _isClosingProgrammatically = true;
            Close();
        }

        /// <summary>
        /// Actualiza la visualización de progreso con porcentaje y tiempo restante.
        /// </summary>
        /// <param name="percent">Porcentaje de progreso (0-100).</param>
        /// <param name="remaining">Tiempo estimado restante (opcional).</param>
        public void UpdateProgress(double percent, TimeSpan? remaining)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(percent, remaining));
                return;
            }

            percent = Math.Max(0, Math.Min(100, percent));         
            ProgressBar.Value = percent;
            PercentText.Text = $"{percent:F0}%";

            if (remaining.HasValue)
            {
                if (remaining.Value.TotalHours >= 1)
                {
                    TimeRemainingText.Text = $"~{remaining.Value:h\\:mm\\:ss} restante";
                }
                else if (remaining.Value.TotalMinutes >= 1)
                {
                    TimeRemainingText.Text = $"~{remaining.Value:m\\:ss} restante";
                }
                else
                {
                    TimeRemainingText.Text = $"~{remaining.Value.Seconds}s restante";
                }
            }
            else
            {
                TimeRemainingText.Text = "Calculando...";
            }
        }

        /// <summary>
        /// Establece el texto del título de la ventana de proceso.
        /// </summary>
        public void SetTitle(string title)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetTitle(title));
                return;
            }
            TitleText.Text = title;
        }
    }
}