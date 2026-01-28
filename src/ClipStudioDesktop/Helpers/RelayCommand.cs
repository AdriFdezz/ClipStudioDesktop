using System;
using System.Windows.Input;

namespace ClipStudioDesktop.Helpers
{
    /// <summary>
    /// Implementación básica de ICommand para el patrón MVVM.
    /// <para>Permite delegar la lógica de ejecución y verificación de comandos a métodos (delegados) en el ViewModel.</para>
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="RelayCommand"/>.
        /// </summary>
        /// <param name="execute">La acción a ejecutar cuando se lanza el comando.</param>
        /// <param name="canExecute">La función opcional (predicado) para verificar si el comando puede ejecutarse. Si es null, siempre se puede ejecutar.</param>
        /// <exception cref="ArgumentNullException">Se lanza si el parámetro execute es null.</exception>
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Determina si el comando puede ejecutarse en el estado actual.
        /// </summary>
        /// <param name="parameter">Datos opcionales utilizados por el comando. Puede ser null.</param>
        /// <returns><c>true</c> si el comando puede ejecutarse; de lo contrario, <c>false</c>.</returns>
        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// Ejecuta la lógica del comando.
        /// </summary>
        /// <param name="parameter">Datos opcionales utilizados por el comando. Puede ser null.</param>
        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// Evento que ocurre cuando detecta cambios que podrían afectar la ejecución del comando.
        /// <para>Se conecta automáticamente al <see cref="CommandManager.RequerySuggested"/> de WPF.</para>
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Fuerza manualmente una re-evaluación del método <see cref="CanExecute"/> para actualizar el estado de los controles de UI.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
