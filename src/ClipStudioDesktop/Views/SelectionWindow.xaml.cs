using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ClipStudioDesktop.Views
{
    public partial class SelectionWindow : Window
    {
        private System.Windows.Point _startPoint;
        private bool _isDragging;
        public Rect SelectedRegion { get; private set; }
        public bool IsConfirmed { get; private set; }

        public SelectionWindow(BitmapSource screenCapture)
        {
            InitializeComponent();
            BackgroundImage.Source = screenCapture;
            
            // Handle Escape to cancel
            KeyDown += (s, e) => 
            {
                if (e.Key == Key.Escape)
                {
                    IsConfirmed = false;
                    Close();
                }
            };
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(SelectionCanvas);
            
            // Reset selection
            SelectionGeometry.Rect = new Rect(_startPoint, _startPoint);
            DimensionsBorder.Visibility = Visibility.Visible;
            UpdateDimensionsText(0, 0);
            UpdateDimensionsPosition(_startPoint);
        }

        private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;

            var currentPoint = e.GetPosition(SelectionCanvas);
            
            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double w = Math.Abs(currentPoint.X - _startPoint.X);
            double h = Math.Abs(currentPoint.Y - _startPoint.Y);

            var rect = new Rect(x, y, w, h);
            SelectionGeometry.Rect = rect;
            
            UpdateDimensionsText(w, h);
            UpdateDimensionsPosition(new System.Windows.Point(x, y));
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            SelectedRegion = SelectionGeometry.Rect;
            
            // If selection is tiny, ignore it (accidental click)
            if (SelectedRegion.Width < 5 || SelectedRegion.Height < 5) return;

            IsConfirmed = true;
            Close();
        }

        private void UpdateDimensionsText(double w, double h)
        {
            DimensionsText.Text = $"{w:F0} x {h:F0}";
        }

        private void UpdateDimensionsPosition(System.Windows.Point p)
        {
            Canvas.SetLeft(DimensionsBorder, p.X);
            Canvas.SetTop(DimensionsBorder, p.Y - 25);
        }
    }
}
