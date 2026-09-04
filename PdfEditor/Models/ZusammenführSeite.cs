using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace PdfEditor.Models;

/// <summary>Repräsentiert eine Seite im Zusammenführungsfenster.</summary>
public class ZusammenführSeite : INotifyPropertyChanged
{
    /// <summary>0-basierter Seitenindex im jeweiligen Dokument.</summary>
    public int SeitenIndex { get; set; }

    /// <summary>1-basierte Anzeigenummer.</summary>
    public int AnzeigeNummer => SeitenIndex + 1;

    private BitmapSource? _vorschau;

    /// <summary>Miniaturvorschau der Seite.</summary>
    public BitmapSource? Vorschau
    {
        get => _vorschau;
        set
        {
            _vorschau = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vorschau)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
