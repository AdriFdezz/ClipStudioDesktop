using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace ClipStudioDesktop.Views
{
    public partial class ProcessingWindow : Window
    {
        private bool _wasTopmost = true;
        private bool _isClosingProgrammatically = false;

        /// <summary>
        /// Event raised when user confirms cancellation of the conversion.
        /// </summary>
        public event EventHandler? CancellationRequested;

        public ProcessingWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position window in the center of the primary screen
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
            // When user clicks another window, go behind but don't disappear
            Topmost = false;
            _wasTopmost = false;
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // When user clicks back on this window, restore topmost
            if (!_wasTopmost)
            {
                Topmost = true;
                _wasTopmost = true;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // If closing programmatically (conversion finished), allow it
            if (_isClosingProgrammatically)
            {
                return;
            }

            // User is trying to close manually - show confirmation
            var result = MessageBox.Show(
                "¿Estás seguro de que deseas cancelar la conversión?\n\nSi cancelas, perderás el contenido grabado.",
                "Cancelar Conversión",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                // User confirmed - raise cancellation event
                CancellationRequested?.Invoke(this, EventArgs.Empty);
                _isClosingProgrammatically = true;
            }
            else
            {
                // User cancelled - don't close
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Closes the window without showing confirmation (for programmatic close after conversion).
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
        /// Updates the progress display with percentage and time remaining.
        /// </summary>
        /// <param name="percent">Progress percentage (0-100)</param>
        /// <param name="remaining">Estimated time remaining, or null if unknown</param>
        public void UpdateProgress(double percent, TimeSpan? remaining)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(percent, remaining));
                return;
            }

            // Clamp percent
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
        /// Sets the title text of the processing window.
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