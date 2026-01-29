using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfMedia = System.Windows.Media;
using WpfPoint = System.Windows.Point;

namespace ClipStudioDesktop.Views
{
    /// <summary>
    /// Ventana del Modo Dibujo.
    /// </summary>
    public partial class DrawingWindow : Window
    {
        private enum DrawingTool { None, Circle, Rectangle, Line, Arrow, Pencil, Eraser }

        // Estado actual
        private DrawingTool _currentTool = DrawingTool.None;
        private WpfMedia.Color _currentColor = WpfMedia.Colors.Red;
        private double _currentSize = 3;
        private bool _isDrawing;
        private WpfPoint _startPoint;
        
        private Shape? _currentShape;
        private Polyline? _currentPolyline;
        
        // Arrastrar toolbar
        private bool _isDraggingToolbar;
        private WpfPoint _toolbarDragStart;
        
        // Selector de color
        private bool _isDraggingHue;
        private bool _hueSliderVisible;
        
        // Elementos dibujados (agrupados para borrar conjuntos)
        private readonly List<List<UIElement>> _drawnElementGroups = new();
        
        // Imagen de fondo original para capturas
        private BitmapSource? _originalScreenshot;
        
        // Carpeta para guardar capturas
        private string _screenshotFolder = string.Empty;

        // Evento para notificar capturas guardadas
        public event Action<string>? ScreenshotSaved;

        /// <summary>
        /// Constructor por defecto. Inicializa los componentes y la barra de herramientas.
        /// </summary>
        public DrawingWindow()
        {
            InitializeComponent();
            InitializeToolbar();
        }

        /// <summary>
        /// Constructor principal para el modo de captura.
        /// Configura la imagen de fondo con la captura de pantalla y establece el foco.
        /// </summary>
        /// <param name="screenCapture">La imagen capturada de la pantalla.</param>
        /// <param name="screenshotFolder">La ruta donde se guardarán las capturas editadas.</param>
        public DrawingWindow(BitmapSource screenCapture, string screenshotFolder)
        {
            InitializeComponent();
            _originalScreenshot = screenCapture;
            _screenshotFolder = screenshotFolder;
            BackgroundImage.Source = screenCapture;
            InitializeToolbar();
            
            Loaded += (s, e) => 
            {
                Activate();
                Focus();
                Keyboard.Focus(this);
                UpdateBorderPositions();
            };

            PreviewKeyDown += (s, e) => 
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
                else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    Undo();
                }
                else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    Redo();
                }
            };

            SizeChanged += (s, e) => UpdateBorderPositions();
        }

        /// <summary>
        /// Restaura el estado inicial de la barra de herramientas.
        /// Deselecciona herramientas y actualiza la etiqueta de tamaño.
        /// </summary>
        private void InitializeToolbar()
        {
            SelectTool(null);
            UpdateSizeLabel();
        }

        /// <summary>
        /// Convierte una cadena hexadecimal de color a un objeto Color de WPF.
        /// </summary>
        /// <param name="hex">Código de color hexadecimal (ej. #FF0000).</param>
        /// <returns>Objeto Color correspondiente.</returns>
        private WpfMedia.Color ParseColor(string hex)
        {
            return (WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(hex);
        }

        #region Arrastrar Toolbar
        /// <summary>
        /// Inicia el arrastre de la barra de herramientas al hacer clic izquierdo.
        /// </summary>
        private void Toolbar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDraggingToolbar = true;
                _toolbarDragStart = e.GetPosition(this);
                Toolbar.CaptureMouse(); // Captura el ratón para asegurar que el evento MouseMove se reciba incluso si el cursor sale del control
                e.Handled = true;
            }
        }

        /// <summary>
        /// Mueve la barra de herramientas siguiendo el cursor mientras se arrastra.
        /// </summary>
        private void Toolbar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingToolbar)
            {
                var currentPos = e.GetPosition(this);
                var delta = currentPos - _toolbarDragStart;
                
                // Actualiza la posición modificando el margen izquierdo y superior
                Toolbar.Margin = new Thickness(
                    Toolbar.Margin.Left + delta.X,
                    Toolbar.Margin.Top + delta.Y,
                    0, 0);
                
                _toolbarDragStart = currentPos; // Actualizar punto de referencia para el siguiente movimiento delta
                e.Handled = true;
            }
        }

        /// <summary>
        /// Finaliza el arrastre de la barra de herramientas.
        /// </summary>
        private void Toolbar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingToolbar)
            {
                _isDraggingToolbar = false;
                Toolbar.ReleaseMouseCapture();
                e.Handled = true;
            }
        }
        #endregion

        #region Herramientas
        /// <summary>
        /// Maneja la selección de herramientas de dibujo.
        /// Identifica la herramienta por el Tag del borde clickeado.
        /// </summary>
        private void Tool_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string toolName)
            {
                _currentTool = toolName switch
                {
                    "Circle" => DrawingTool.Circle,
                    "Rectangle" => DrawingTool.Rectangle,
                    "Line" => DrawingTool.Line,
                    "Arrow" => DrawingTool.Arrow,
                    "Pencil" => DrawingTool.Pencil,
                    "Eraser" => DrawingTool.Eraser,
                    _ => DrawingTool.Circle
                };
                
                SelectTool(border);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Actualiza visualmente la herramienta seleccionada.
        /// Restablece el fondo de todos los botones y resalta el seleccionado.
        /// </summary>
        /// <param name="selectedBtn">El botón de la herramienta seleccionada (o null para ninguna).</param>
        private void SelectTool(Border? selectedBtn)
        {
            // Color de fondo desactivado (gris oscuro)
            var defaultBg = new WpfMedia.SolidColorBrush(ParseColor("#2A2A2A"));
            BtnCircle.Background = defaultBg;
            BtnRectangle.Background = defaultBg;
            BtnLine.Background = defaultBg;
            BtnArrow.Background = defaultBg;
            BtnPencil.Background = defaultBg;
            BtnEraser.Background = defaultBg;
            
            if (selectedBtn != null)
            {
                selectedBtn.Background = new WpfMedia.SolidColorBrush(ParseColor("#00BFFF"));
            }
        }
        #endregion

        #region Selector de Tamaño (+/-)
        /// <summary>
        /// Disminuye el tamaño del pincel/grosor de línea. Mínimo 1px.
        /// </summary>
        private void SizeDecrease_Click(object sender, MouseButtonEventArgs e)
        {
            if (_currentSize > 1)
            {
                _currentSize--;
                UpdateSizeLabel();
            }
            e.Handled = true;
        }

        /// <summary>
        /// Aumenta el tamaño del pincel/grosor de línea. Máximo 20px.
        /// </summary>
        private void SizeIncrease_Click(object sender, MouseButtonEventArgs e)
        {
            if (_currentSize < 20)
            {
                _currentSize++;
                UpdateSizeLabel();
            }
            e.Handled = true;
        }

        /// <summary>
        /// Actualiza el texto de la etiqueta que muestra el tamaño actual en píxeles.
        /// </summary>
        private void UpdateSizeLabel()
        {
            if (SizeLabel != null)
            {
                SizeLabel.Text = $"{(int)_currentSize} px";
            }
        }
        #endregion

        #region Selector de Color (Hue Slider)
        /// <summary>
        /// Alterna la visibilidad del selector de tono (Hue Slider).
        /// </summary>
        private void ColorIndicator_Click(object sender, MouseButtonEventArgs e)
        {
            _hueSliderVisible = !_hueSliderVisible;
            HueSliderContainer.Visibility = _hueSliderVisible ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }

        /// <summary>
        /// Inicia el cambio de color al hacer clic en la barra de tono.
        /// </summary>
        private void HueBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDraggingHue = true;
                HueSliderContainer.CaptureMouse();
                UpdateHueFromMouse(e.GetPosition(HueSliderContainer));
                e.Handled = true;
            }
        }

        /// <summary>
        /// Actualiza el color mientras se arrastra por la barra de tono.
        /// </summary>
        private void HueBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingHue)
            {
                UpdateHueFromMouse(e.GetPosition(HueSliderContainer));
                e.Handled = true;
            }
        }

        /// <summary>
        /// Finaliza la selección de color.
        /// </summary>
        private void HueBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingHue)
            {
                _isDraggingHue = false;
                HueSliderContainer.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Calcula el nuevo color basado en la posición del ratón en la barra de tono.
        /// Actualiza _currentColor y el indicador visual.
        /// </summary>
        /// <param name="pos">Posición del ratón relativa al contenedor.</param>
        private void UpdateHueFromMouse(WpfPoint pos)
        {
            double width = HueSliderContainer.ActualWidth;
            if (width <= 0) return;

            double x = Math.Max(0, Math.Min(pos.X, width));
            double hue = (x / width) * 360;

            HueIndicator.Margin = new Thickness(x - 3, -2, 0, -2);

            _currentColor = HsvToRgb(hue, 1.0, 1.0);
            ColorIndicator.Background = new WpfMedia.SolidColorBrush(_currentColor);
        }

        /// <summary>
        /// Convierte valores HSV a un color RGB de WPF.
        /// Algoritmo estándar de conversión de color.
        /// </summary>
        /// <param name="h">Hue (0-360)</param>
        /// <param name="s">Saturation (0-1)</param>
        /// <param name="v">Value (0-1)</param>
        /// <returns>Color resultante.</returns>
        private WpfMedia.Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r = 0, g = 0, b = 0;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return WpfMedia.Color.FromRgb(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }
        #endregion

        #region Captura de Pantalla con Dibujos

        
        /// <summary>
        /// Maneja el clic en el botón de captura. Inicia el proceso de guardado.
        /// </summary>
        private void Screenshot_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            CaptureScreenWithDrawings();
        }

        /// <summary>
        /// Realiza la captura de pantalla incluyendo los dibujos realizados.
        /// Oculta la UI, captura la región de la ventana y guarda la imagen.
        /// </summary>
        private void CaptureScreenWithDrawings()
        {
            // Ocultar elementos de UI temporalmente para que no salgan en la foto
            Toolbar.Visibility = Visibility.Collapsed;
            ModeIndicator.Visibility = Visibility.Collapsed;
            BorderCanvas.Visibility = Visibility.Collapsed;

            // Forzar actualización del layout para asegurar que la UI está oculta visualmente
            UpdateLayout();
            
            // Usar DispatcherTimer para dar un pequeño margen al renderizado de WPF antes de capturar
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                
                try
                {
                    // Obtener las coordenadas de la ventana en la pantalla (absolutas)
                    var windowLocation = new System.Drawing.Point((int)Left, (int)Top);
                    var windowSize = new System.Drawing.Size((int)ActualWidth, (int)ActualHeight);
                    
                    if (windowSize.Width <= 0 || windowSize.Height <= 0)
                    {
                        throw new InvalidOperationException("Las dimensiones de la ventana no son válidas");
                    }

                    // Capturar usando GDI+ (CopyFromScreen) para obtener los píxeles reales de la pantalla
                    using (var bitmap = new System.Drawing.Bitmap(windowSize.Width, windowSize.Height))
                    {
                        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                        {
                            graphics.CopyFromScreen(windowLocation, System.Drawing.Point.Empty, windowSize);
                        }

                        // Usar la carpeta configurada o fallback a Imágenes
                        string clipStudioPath = !string.IsNullOrEmpty(_screenshotFolder) 
                            ? _screenshotFolder 
                            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ClipStudio", "Screenshots");

                        Directory.CreateDirectory(clipStudioPath);

                        string fileName = $"Captura_Modo_Dibujo_{DateTime.Now:dd_MM_yyyy_HH_mm_ss}.png";
                        string fullPath = System.IO.Path.Combine(clipStudioPath, fileName);

                        // Guardar como PNG
                        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

                        // Reproducir sonido de confirmación
                        PlayCaptureSound();

                        // Notificar guardado exitoso (Visual In-App + Apertura de carpeta)
                        this.Dispatcher.Invoke(() => ShowInAppNotification(fullPath));

                        ScreenshotSaved?.Invoke(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al capturar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // Restaurar elementos de UI siempre, incluso si falla la captura
                    Toolbar.Visibility = Visibility.Visible;
                    ModeIndicator.Visibility = Visibility.Visible;
                    BorderCanvas.Visibility = Visibility.Visible;
                }
            };
            
            timer.Start();
        }

        /// <summary>
        /// Reproduce el sonido de "obturador" o notificación si el archivo existe.
        /// </summary>
        private void PlayCaptureSound()
        {
            try
            {
                string soundPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Notification_sound.wav");
                if (File.Exists(soundPath))
                {
                    using (var player = new System.Media.SoundPlayer(soundPath))
                    {
                        player.Play();
                    }
                }
            }
            catch { }
        }
        
        // Timer para ocultar notificación
        private System.Windows.Threading.DispatcherTimer? _notificationTimer;
        private string _lastNotificationPath = string.Empty;

        /// <summary>
        /// Muestra una notificación visual en la parte superior de la ventana.
        /// </summary>
        /// <param name="path">Ruta del archivo guardado para abrirlo al hacer click.</param>
        private void ShowInAppNotification(string path)
        {
            _lastNotificationPath = path;
            NotificationBanner.Visibility = Visibility.Visible;
            
            if (_notificationTimer == null)
            {
                _notificationTimer = new System.Windows.Threading.DispatcherTimer();
                _notificationTimer.Tick += (s, e) => 
                {
                    NotificationBanner.Visibility = Visibility.Collapsed;
                    _notificationTimer.Stop();
                };
            }
            
            _notificationTimer.Interval = TimeSpan.FromSeconds(5);
            _notificationTimer.Stop(); // Reiniciar si ya estaba corriendo
            _notificationTimer.Start();
        }

        /// <summary>
        /// Maneja el clic en el banner de notificación.
        /// Abre el archivo en el explorador de Windows utilizando la instancia de App.
        /// </summary>
        private async void NotificationBanner_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !string.IsNullOrEmpty(_lastNotificationPath))
            {
                try
                {
                    if (System.Windows.Application.Current is App app)
                    {
                        app.ShowFileInExplorer(_lastNotificationPath);
                    }
                    
                    // Esperar 1 segundo antes de cerrar para feedback visual
                    await Task.Delay(1000);
                    NotificationBanner.Visibility = Visibility.Collapsed;
                }
                catch (Exception)
                {
                    // Ignorar errores al abrir explorador
                }
                e.Handled = true;
            }
        }
        #endregion

        #region Undo/Redo
        /// <summary>
        /// Interfaz base para las acciones de dibujo reversibles (Command Pattern).
        /// </summary>
        private interface IDrawingAction
        {
            /// <summary>
            /// Deshace la acción realizada.
            /// </summary>
            void Undo(Canvas canvas, List<List<UIElement>> groups);

            /// <summary>
            /// Rehace la acción previamente deshecha.
            /// </summary>
            void Redo(Canvas canvas, List<List<UIElement>> groups);
        }

        /// <summary>
        /// Acción de agregar nuevos elementos al lienzo (Dibujar).
        /// </summary>
        private class DrawAction : IDrawingAction
        {
            private readonly List<UIElement> _elements;

            public DrawAction(List<UIElement> elements)
            {
                _elements = elements;
            }

            public void Undo(Canvas canvas, List<List<UIElement>> groups)
            {
                foreach (var elem in _elements)
                {
                    canvas.Children.Remove(elem);
                }
                groups.Remove(_elements);
            }

            public void Redo(Canvas canvas, List<List<UIElement>> groups)
            {
                foreach (var elem in _elements)
                {
                    canvas.Children.Add(elem);
                }
                groups.Add(_elements);
            }
        }

        /// <summary>
        /// Acción de eliminar elementos existentes del lienzo (Borrar).
        /// </summary>
        private class EraseAction : IDrawingAction
        {
            private readonly List<List<UIElement>> _removedGroups;

            public EraseAction(List<List<UIElement>> removedGroups)
            {
                _removedGroups = removedGroups;
            }

            public void Undo(Canvas canvas, List<List<UIElement>> groups)
            {
                foreach (var group in _removedGroups)
                {
                    foreach (var elem in group)
                    {
                        canvas.Children.Add(elem);
                    }
                    groups.Add(group);
                }
            }

            public void Redo(Canvas canvas, List<List<UIElement>> groups)
            {
                foreach (var group in _removedGroups)
                {
                    foreach (var elem in group)
                    {
                        canvas.Children.Remove(elem);
                    }
                    groups.Remove(group);
                }
            }
        }


        private readonly Stack<IDrawingAction> _undoStack = new();
        private readonly Stack<IDrawingAction> _redoStack = new();

        private void AddAction(IDrawingAction action)
        {
            _undoStack.Push(action);
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var action = _undoStack.Pop();
                action.Undo(DrawingCanvas, _drawnElementGroups);
                _redoStack.Push(action);
            }
        }

        private void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var action = _redoStack.Pop();
                action.Redo(DrawingCanvas, _drawnElementGroups);
                _undoStack.Push(action);
            }
        }
        #endregion

        #region Dibujo
        /// <summary>
        /// Inicia el trazo de dibujo.
        /// Captura el ratón y prepara la geometría inicial según la herramienta activa.
        /// </summary>
        private void DrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (_currentTool == DrawingTool.None) return;
            
            _isDrawing = true;
            _startPoint = e.GetPosition(DrawingCanvas);
            DrawingCanvas.CaptureMouse();

            switch (_currentTool)
            {
                case DrawingTool.Circle:
                    _currentShape = new Ellipse
                    {
                        Stroke = new WpfMedia.SolidColorBrush(_currentColor),
                        StrokeThickness = _currentSize,
                        Fill = WpfMedia.Brushes.Transparent
                    };
                    Canvas.SetLeft(_currentShape, _startPoint.X);
                    Canvas.SetTop(_currentShape, _startPoint.Y);
                    DrawingCanvas.Children.Add(_currentShape);
                    break;

                case DrawingTool.Rectangle:
                    _currentShape = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = new WpfMedia.SolidColorBrush(_currentColor),
                        StrokeThickness = _currentSize,
                        Fill = WpfMedia.Brushes.Transparent
                    };
                    Canvas.SetLeft(_currentShape, _startPoint.X);
                    Canvas.SetTop(_currentShape, _startPoint.Y);
                    DrawingCanvas.Children.Add(_currentShape);
                    break;

                case DrawingTool.Line:
                case DrawingTool.Arrow:
                    _currentShape = new Line
                    {
                        Stroke = new WpfMedia.SolidColorBrush(_currentColor),
                        StrokeThickness = _currentSize,
                        X1 = _startPoint.X,
                        Y1 = _startPoint.Y,
                        X2 = _startPoint.X,
                        Y2 = _startPoint.Y
                    };
                    DrawingCanvas.Children.Add(_currentShape);
                    break;

                case DrawingTool.Pencil:
                    _currentPolyline = new Polyline
                    {
                        Stroke = new WpfMedia.SolidColorBrush(_currentColor),
                        StrokeThickness = _currentSize,
                        StrokeLineJoin = WpfMedia.PenLineJoin.Round,
                        StrokeStartLineCap = WpfMedia.PenLineCap.Round,
                        StrokeEndLineCap = WpfMedia.PenLineCap.Round
                    };
                    _currentPolyline.Points.Add(_startPoint);
                    DrawingCanvas.Children.Add(_currentPolyline);
                    break;

                case DrawingTool.Eraser:
                    TryEraseAt(_startPoint);
                    break;
            }
        }

        /// <summary>
        /// Actualiza la geometría del dibujo en tiempo real mientras se arrastra el ratón.
        /// </summary>
        private void DrawingCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDrawing) return;

            var currentPoint = e.GetPosition(DrawingCanvas);

            switch (_currentTool)
            {
                case DrawingTool.Circle:
                case DrawingTool.Rectangle:
                    if (_currentShape != null)
                    {
                        double x = Math.Min(currentPoint.X, _startPoint.X);
                        double y = Math.Min(currentPoint.Y, _startPoint.Y);
                        double w = Math.Abs(currentPoint.X - _startPoint.X);
                        double h = Math.Abs(currentPoint.Y - _startPoint.Y);

                        Canvas.SetLeft(_currentShape, x);
                        Canvas.SetTop(_currentShape, y);
                        _currentShape.Width = w;
                        _currentShape.Height = h;
                    }
                    break;

                case DrawingTool.Line:
                case DrawingTool.Arrow:
                    if (_currentShape is Line line)
                    {
                        line.X2 = currentPoint.X;
                        line.Y2 = currentPoint.Y;
                    }
                    break;

                case DrawingTool.Pencil:
                    _currentPolyline?.Points.Add(currentPoint);
                    break;

                case DrawingTool.Eraser:
                    TryEraseAt(currentPoint);
                    break;
            }
        }

        /// <summary>
        /// Finaliza el trazo actual.
        /// Procesa formas complejas (como flechas), guarda el historial de Deshacer y libera el ratón.
        /// </summary>
        private void DrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            DrawingCanvas.ReleaseMouseCapture();
            
            List<UIElement> newElements = new();

            if (_currentTool == DrawingTool.Arrow && _currentShape is Line arrowLine)
            {
                var arrowHead = CreateArrowHead(arrowLine);
                DrawingCanvas.Children.Add(arrowHead);
                
                newElements.Add(_currentShape);
                newElements.Add(arrowHead);
                
                _drawnElementGroups.Add(newElements);
                AddAction(new DrawAction(newElements)); // Registrar acción
                _currentShape = null;
            }
            else if (_currentShape != null)
            {
                newElements.Add(_currentShape);
                
                _drawnElementGroups.Add(newElements);
                AddAction(new DrawAction(newElements)); // Registrar acción
                _currentShape = null;
            }
            
            if (_currentPolyline != null)
            {
                newElements.Add(_currentPolyline);
                
                _drawnElementGroups.Add(newElements);
                AddAction(new DrawAction(newElements)); // Registrar acción
                _currentPolyline = null;
            }

            _isDrawing = false;
        }

        /// <summary>
        /// Genera un polígono triangular para representar la punta de una flecha.
        /// Calcula la rotación y posición basándose en la línea del cuerpo de la flecha.
        /// </summary>
        /// <param name="line">Línea principal de la flecha.</param>
        /// <returns>Polígono de la cabeza de flecha.</returns>
        private Polygon CreateArrowHead(Line line)
        {
            double angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1);
            
            // Escalar la cabeza proporcionalmente al grosor de la línea
            // Para tamaños pequeños (1-3px), usar un ancho mínimo más generoso
            double headLength = _currentSize * 3.5;
            double headWidth = _currentSize <= 3 ? Math.Max(8, _currentSize * 3.5) : _currentSize * 2.0;
            double headAngle = Math.Atan2(headWidth / 2, headLength);

            // Guardar el punto final original
            double tipX = line.X2;
            double tipY = line.Y2;

            // Acortar la línea para que termine donde empieza la cabeza
            line.X2 = tipX - headLength * Math.Cos(angle);
            line.Y2 = tipY - headLength * Math.Sin(angle);

            // Calcular los puntos de la cabeza
            double x1 = tipX - headLength * Math.Cos(angle - headAngle);
            double y1 = tipY - headLength * Math.Sin(angle - headAngle);
            double x2 = tipX - headLength * Math.Cos(angle + headAngle);
            double y2 = tipY - headLength * Math.Sin(angle + headAngle);

            return new Polygon
            {
                Fill = new WpfMedia.SolidColorBrush(_currentColor),
                Points = new WpfMedia.PointCollection
                {
                    new WpfPoint(tipX, tipY),
                    new WpfPoint(x1, y1),
                    new WpfPoint(x2, y2)
                }
            };
        }

        /// <summary>
        /// Intenta borrar elementos del lienzo que estén cerca del punto especificado.
        /// Utiliza una detección de colisiones basada en distancia.
        /// </summary>
        /// <param name="point">Punto donde se aplica la goma de borrar.</param>
        private void TryEraseAt(WpfPoint point)
        {
            var groupsToRemove = new List<List<UIElement>>();
            // PRECISIÓN MEJORADA: Reducido de 8 a 3 para evitar borrados accidentales
            double eraserRadius = 3; 

            foreach (var group in _drawnElementGroups)
            {
                foreach (var element in group)
                {
                    if (IsNearPoint(element, point, eraserRadius))
                    {
                        groupsToRemove.Add(group);
                        break;
                    }
                }
            }

            if (groupsToRemove.Count > 0)
            {
                foreach (var group in groupsToRemove)
                {
                    foreach (var elem in group)
                    {
                        DrawingCanvas.Children.Remove(elem);
                    }
                    _drawnElementGroups.Remove(group);
                }
                
                AddAction(new EraseAction(groupsToRemove)); // Registrar acción de borrado
            }
        }

        /// <summary>
        /// Verifica si un elemento gráfico colisiona o está cerca de un punto.
        /// Soporta detecciones específicas para Elipse, Rectángulo, Línea, Polilínea y Polígono.
        /// </summary>
        /// <param name="element">El elemento gráfico a comprobar.</param>
        /// <param name="point">El punto de prueba (ej. posición del borrador).</param>
        /// <param name="radius">Radio de tolerancia para la "goma".</param>
        private bool IsNearPoint(UIElement element, WpfPoint point, double radius)
        {
            if (element is Ellipse ellipse)
            {
                double cx = Canvas.GetLeft(ellipse) + ellipse.Width / 2;
                double cy = Canvas.GetTop(ellipse) + ellipse.Height / 2;
                return Distance(point, new WpfPoint(cx, cy)) < radius + Math.Max(ellipse.Width, ellipse.Height) / 2;
            }
            else if (element is System.Windows.Shapes.Rectangle rect)
            {
                double x = Canvas.GetLeft(rect);
                double y = Canvas.GetTop(rect);
                var bounds = new Rect(x, y, rect.Width, rect.Height);
                bounds.Inflate(radius, radius);
                return bounds.Contains(point);
            }
            else if (element is Line line)
            {
                return DistanceToLine(point, new WpfPoint(line.X1, line.Y1), new WpfPoint(line.X2, line.Y2)) < radius;
            }
            else if (element is Polyline polyline)
            {
                for (int i = 0; i < polyline.Points.Count - 1; i++)
                {
                    if (DistanceToLine(point, polyline.Points[i], polyline.Points[i + 1]) < radius)
                        return true;
                }
            }
            else if (element is Polygon polygon)
            {
                double cx = 0, cy = 0;
                foreach (var p in polygon.Points)
                {
                    cx += p.X;
                    cy += p.Y;
                }
                cx /= polygon.Points.Count;
                cy /= polygon.Points.Count;
                return Distance(point, new WpfPoint(cx, cy)) < radius * 2;
            }

            return false;
        }

        /// <summary>
        /// Calcula la distancia euclidiana entre dos puntos.
        /// </summary>
        private double Distance(WpfPoint p1, WpfPoint p2)
        {
            return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }

        /// <summary>
        /// Calcula la distancia mínima desde un punto a un segmento de línea.
        /// Útil para borrar líneas con precisión.
        /// </summary>
        private double DistanceToLine(WpfPoint point, WpfPoint lineStart, WpfPoint lineEnd)
        {
            double A = point.X - lineStart.X;
            double B = point.Y - lineStart.Y;
            double C = lineEnd.X - lineStart.X;
            double D = lineEnd.Y - lineStart.Y;

            double dot = A * C + B * D;
            double lenSq = C * C + D * D;
            double param = lenSq != 0 ? dot / lenSq : -1;

            double xx, yy;

            if (param < 0) { xx = lineStart.X; yy = lineStart.Y; }
            else if (param > 1) { xx = lineEnd.X; yy = lineEnd.Y; }
            else { xx = lineStart.X + param * C; yy = lineStart.Y + param * D; }

            return Distance(point, new WpfPoint(xx, yy));
        }
        #endregion

        #region Bordes
        /// <summary>
        /// Ajusta los rectángulos transparentes usados para redimensionar la ventana.
        /// </summary>
        private void UpdateBorderPositions()
        {
            double w = ActualWidth;
            double h = ActualHeight;

            TopBorder.X2 = w;
            BottomBorder.X2 = w;
            Canvas.SetTop(BottomBorder, h - 2);
            LeftBorder.Y2 = h;
            RightBorder.Y2 = h;
            Canvas.SetLeft(RightBorder, w - 2);
        }
        #endregion
    }
}
