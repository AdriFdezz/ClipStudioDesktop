using System.Windows;
using System.Windows.Media.Animation;

namespace ClipStudioDesktop.Views
{
    /// <summary>
    /// Ventana de identificación de monitores.
    /// Muestra un número grande en cada pantalla para ayudar al usuario a identificarlas.
    /// Es transparente, sin bordes y no interactuable.
    /// </summary>
    public partial class IdentifyWindow : Window
    {
        /// <summary>
        /// Inicializa una nueva instancia de la ventana de identificación.
        /// </summary>
        /// <param name="number">El número a mostrar en esta pantalla (índice 1-based).</param>
        public IdentifyWindow(int number)
        {
            InitializeComponent();
            NumberText.Text = number.ToString();
            Loaded += IdentifyWindow_Loaded;
        }

        /// <summary>
        /// Se ejecuta cuando la ventana se ha cargado completamente.
        /// Inicia la animación de aparición/desaparición y cierra la ventana al finalizar.
        /// </summary>
        private void IdentifyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var storyboard = (Storyboard)FindResource("IdentifyAnimation");
            if (storyboard != null)
            {
                // Cerrar automáticamente la ventana cuando termine la animación
                storyboard.Completed += (s, ev) => Close();
                storyboard.Begin();
            }
        }
    }
}
