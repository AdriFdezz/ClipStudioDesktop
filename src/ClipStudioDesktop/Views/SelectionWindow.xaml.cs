using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ClipStudioDesktop.Views
{
    /// <summary>
    /// Ventana de selección de área de pantalla.
    /// Permite al usuario arrastrar para definir una región rectangular (ROI) sobre una captura de pantalla congelada.
    /// </summary>
    public partial class SelectionWindow : Window
    {
        private System.Windows.Point _startPoint;
        private bool _isDragging;

        /// <summary>
        /// Región seleccionada por el usuario (coordenadas relativas a la pantalla).
        /// </summary>
        public Rect SelectedRegion { get; private set; }

        /// <summary>
        /// Indica si el usuario confirmó la selección (soltó el clic tras arrastrar).
        /// </summary>
        public bool IsConfirmed { get; private set; }

        // Constructor por defecto
        public SelectionWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Inicializa la ventana con una captura de pantalla de fondo.
        /// </summary>
        /// <param name="screenCapture">Imagen de la pantalla completa para simular congelamiento.</param>
        public SelectionWindow(BitmapSource screenCapture)
        {
            InitializeComponent();
            BackgroundImage.Source = screenCapture;
            
            // Asegurar el foco para eventos de teclado
            Loaded += (s, e) => 
            {
                Activate();
                Focus();
                Keyboard.Focus(this);
                // Inicializar posición del cursor personalizado
                var mousePos = Mouse.GetPosition(this);
                Canvas.SetLeft(CustomCursorCanvas, mousePos.X);
                Canvas.SetTop(CustomCursorCanvas, mousePos.Y);
            };

            // Manejar Escape para cancelar (usando PreviewKeyDown para mayor prioridad)
            PreviewKeyDown += (s, e) => 
            {
                if (e.Key == Key.Escape)
                {
                    IsConfirmed = false;
                    e.Handled = true;
                    Close();
                }
            };
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            _isDragging = true;
            _startPoint = e.GetPosition(SelectionCanvas);
            
            // Reiniciar selección
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
            
            // Actualizar posición del cursor personalizado
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
            
            // Si la selección es muy pequeña, ignorarla (clic accidental)
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
