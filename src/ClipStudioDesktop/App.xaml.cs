using System;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;

namespace ClipStudioDesktop
{
    public partial class App : Application
    {
        private TaskbarIcon? _taskbarIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create the TaskbarIcon
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.ToolTipText = "Clip Studio Desktop";
            
            // Use a default system icon since we don't have a custom one yet
            _taskbarIcon.Icon = SystemIcons.Application;

            // Create Context Menu
            var contextMenu = new System.Windows.Controls.ContextMenu();
            
            var configItem = new System.Windows.Controls.MenuItem();
            configItem.Header = "Configuración";
            configItem.Click += (s, args) => OpenConfiguration();
            contextMenu.Items.Add(configItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem();
            exitItem.Header = "Salir";
            exitItem.Click += (s, args) => Shutdown();
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;
            
            // Handle double click to open config
            _taskbarIcon.TrayMouseDoubleClick += (s, args) => OpenConfiguration();
        }

        private void OpenConfiguration()
        {
            foreach (Window window in Windows)
            {
                if (window is Views.MainWindow)
                {
                    window.Show();
                    window.Activate();
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    return;
                }
            }

            var mainWindow = new Views.MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _taskbarIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
