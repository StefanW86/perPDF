using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Art einer einfachen Symbolmarkierung (Stempel), die auf eine Seite gesetzt
/// werden kann. Legt die beim Zeichnen verwendete Vektorform fest.
/// </summary>
public enum SymbolArt
{
    /// <summary>Diagonales Kreuz (Ecke zu Ecke).</summary>
    Kreuz,
    /// <summary>Häkchen.</summary>
    Häkchen,
    /// <summary>Ausgefüllter Punkt.</summary>
    Punkt,
    /// <summary>Abgerundete Umrandung (Rahmen).</summary>
    Umranden,
    /// <summary>Waagerechte Durchstreichung.</summary>
    Durchstreichen
}

/// <summary>
/// Eine frei platzierbare Symbolmarkierung (Kreuz, Häkchen, Punkt, Umrandung oder
/// Durchstreichung). Position und Größe werden – wie bei den übrigen Annotationen –
/// in PDF-Punkten (Ursprung unten links, ungedreht) gehalten und erst beim
/// Speichern als Vektorform in die Seite eingezeichnet.
/// </summary>
public class Symbolmarkierung : BeobachtbaresObjekt, IAufSeite
{
    public Symbolmarkierung(SymbolArt art, int seitenIndex)
    {
        Art = art;
        _seitenIndex = seitenIndex;
    }

    /// <summary>Die Art des Symbols (legt die gezeichnete Form fest).</summary>
    public SymbolArt Art { get; }

    private int _seitenIndex;
    public int SeitenIndex
    {
        get => _seitenIndex;
        set => SetzeWert(ref _seitenIndex, value);
    }

    private double _x;
    /// <summary>X-Position der unteren linken Ecke in PDF-Punkten.</summary>
    public double X { get => _x; set => SetzeWert(ref _x, value); }

    private double _y;
    /// <summary>Y-Position der unteren linken Ecke in PDF-Punkten.</summary>
    public double Y { get => _y; set => SetzeWert(ref _y, value); }

    private double _breite;
    /// <summary>Breite in PDF-Punkten.</summary>
    public double Breite { get => _breite; set => SetzeWert(ref _breite, value); }

    private double _höhe;
    /// <summary>Höhe in PDF-Punkten.</summary>
    public double Höhe { get => _höhe; set => SetzeWert(ref _höhe, value); }

    private double _strichbreite = 2.0;
    /// <summary>Strichstärke in PDF-Punkten (bei <see cref="SymbolArt.Punkt"/> ohne Wirkung).</summary>
    public double Strichbreite { get => _strichbreite; set => SetzeWert(ref _strichbreite, value); }

    private string _farbe = "#000000";
    /// <summary>Farbe als Hex-Wert (#RRGGBB).</summary>
    public string Farbe { get => _farbe; set => SetzeWert(ref _farbe, value); }
}
