using System.Windows;

namespace ClipStudioDesktop.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Instead of closing, just hide the window so the app keeps running in tray
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }
    }
}
