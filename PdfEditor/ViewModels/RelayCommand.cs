using System.Windows.Input;

namespace PdfEditor.ViewModels;

/// <summary>
/// Einfache ICommand-Implementierung, um Schaltflächen-Befehle aus dem
/// ViewModel heraus bereitzustellen (klassisches MVVM-Muster).
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _ausführen;
    private readonly Func<bool>? _kannAusführen;

    public RelayCommand(Action ausführen, Func<bool>? kannAusführen = null)
    {
        _ausführen = ausführen ?? throw new ArgumentNullException(nameof(ausführen));
        _kannAusführen = kannAusführen;
    }

    public bool CanExecute(object? parameter) => _kannAusführen?.Invoke() ?? true;

    public void Execute(object? parameter) => _ausführen();

    public event EventHandler? CanExecuteChanged;

    /// <summary>Meldet der Oberfläche, dass sich die Ausführbarkeit geändert hat.</summary>
    public void AusführbarkeitAktualisieren() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
