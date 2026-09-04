using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PdfEditor.ViewModels;

/// <summary>
/// Basisklasse, die das Auslösen von PropertyChanged-Ereignissen vereinfacht.
/// Wird von ViewModels und datengebundenen Modellen verwendet.
/// </summary>
public abstract class BeobachtbaresObjekt : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Meldet eine Eigenschaftsänderung an die Oberfläche.</summary>
    protected void Melde([CallerMemberName] string? eigenschaft = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(eigenschaft));

    /// <summary>Setzt ein Feld und meldet die Änderung, falls sich der Wert unterscheidet.</summary>
    protected bool SetzeWert<T>(ref T feld, T wert, [CallerMemberName] string? eigenschaft = null)
    {
        if (EqualityComparer<T>.Default.Equals(feld, wert))
            return false;
        feld = wert;
        Melde(eigenschaft);
        return true;
    }
}
