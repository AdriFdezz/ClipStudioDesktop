using System.Windows;
using System.Windows.Forms;

namespace ClipStudioDesktop.Views
{
    public partial class ProcessingWindow : Window
    {
        // Default constructor
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
    }
}