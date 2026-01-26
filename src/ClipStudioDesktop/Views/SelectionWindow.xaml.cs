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

        // Default constructor
        public SelectionWindow()
        {
            InitializeComponent();
        }

        public SelectionWindow(BitmapSource screenCapture)
        {
            InitializeComponent();
            BackgroundImage.Source = screenCapture;
            
            // Ensure focus for key events
            Loaded += (s, e) => 
            {
                Focus();
                // Initialize cursor position
                var mousePos = Mouse.GetPosition(this);
                Canvas.SetLeft(CustomCursorCanvas, mousePos.X);
                Canvas.SetTop(CustomCursorCanvas, mousePos.Y);
            };

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
            if (e.ChangedButton != MouseButton.Left) return;

            _isDragging = true;
            _startPoint = e.GetPosition(SelectionCanvas);
            
            // Reset selection
            var rect = new Rect(_startPoint, _startPoint);
            SelectionGeometry.Rect = rect;
            HoleGeometry.Rect = rect;
            
            DimensionsBorder.Visibility = Visibility.Visible;
            UpdateDimensionsText(0, 0);
            UpdateDimensionsPosition(_startPoint);
        }

        private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var currentPoint = e.GetPosition(SelectionCanvas);
            
            // Update custom cursor position
            Canvas.SetLeft(CustomCursorCanvas, currentPoint.X);
            Canvas.SetTop(CustomCursorCanvas, currentPoint.Y);

            if (!_isDragging) return;
            
            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double w = Math.Abs(currentPoint.X - _startPoint.X);
            double h = Math.Abs(currentPoint.Y - _startPoint.Y);

            var rect = new Rect(x, y, w, h);
            SelectionGeometry.Rect = rect;
            HoleGeometry.Rect = rect;
            
            UpdateDimensionsText(w, h);
            UpdateDimensionsPosition(new System.Windows.Point(x, y));
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

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
