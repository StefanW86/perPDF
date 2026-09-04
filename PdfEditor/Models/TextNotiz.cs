using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Ein vom Benutzer eingefügter, frei platzierbarer und nachträglich
/// editierbarer Text. Position und Größe werden in PDF-Punkten (Ursprung unten
/// links, ungedreht) gespeichert; beim Speichern wird der Text in die Seite
/// eingezeichnet.
/// </summary>
public class TextNotiz : BeobachtbaresObjekt, IAufSeite
{
    public TextNotiz(int seitenIndex)
    {
        _seitenIndex = seitenIndex;
    }

    private int _seitenIndex;
    public int SeitenIndex
    {
        get => _seitenIndex;
        set => SetzeWert(ref _seitenIndex, value);
    }

    private string _text = "Text";
    /// <summary>Der eingegebene Text (jederzeit editierbar).</summary>
    public string Text
    {
        get => _text;
        set => SetzeWert(ref _text, value);
    }

    private double _fontGröße = 12;
    /// <summary>Schriftgröße in PDF-Punkten.</summary>
    public double FontGröße
    {
        get => _fontGröße;
        set => SetzeWert(ref _fontGröße, value);
    }

    private bool _fett;
    /// <summary>Fette Schrift.</summary>
    public bool Fett
    {
        get => _fett;
        set => SetzeWert(ref _fett, value);
    }

    private bool _kursiv;
    /// <summary>Kursive Schrift.</summary>
    public bool Kursiv
    {
        get => _kursiv;
        set => SetzeWert(ref _kursiv, value);
    }

    private bool _unterstrichen;
    /// <summary>Unterstrichene Schrift.</summary>
    public bool Unterstrichen
    {
        get => _unterstrichen;
        set => SetzeWert(ref _unterstrichen, value);
    }

    private string _farbe = "#000000";
    /// <summary>Textfarbe als Hex-Wert (#RRGGBB).</summary>
    public string Farbe
    {
        get => _farbe;
        set => SetzeWert(ref _farbe, value);
    }

    private string _hintergrund = string.Empty;
    /// <summary>
    /// Optionale Hintergrundfarbe als Hex-Wert (#RRGGBB); leer = transparent. Wird hinter
    /// dem Text als gefülltes Rechteck gezeichnet – u. a. um eingebrannten Seiteninhalt
    /// (z. B. die Original-Beschriftung eines bearbeiteten Optionsfeldes) zu überdecken.
    /// </summary>
    public string Hintergrund
    {
        get => _hintergrund;
        set => SetzeWert(ref _hintergrund, value);
    }

    private double _x;
    /// <summary>X-Position der unteren linken Ecke in PDF-Punkten.</summary>
    public double X { get => _x; set => SetzeWert(ref _x, value); }

    private double _y;
    /// <summary>Y-Position der unteren linken Ecke in PDF-Punkten.</summary>
    public double Y { get => _y; set => SetzeWert(ref _y, value); }

    private double _breite;
    /// <summary>Breite des Textfeldes in PDF-Punkten.</summary>
    public double Breite { get => _breite; set => SetzeWert(ref _breite, value); }

    private double _höhe;
    /// <summary>Höhe des Textfeldes in PDF-Punkten.</summary>
    public double Höhe { get => _höhe; set => SetzeWert(ref _höhe, value); }
}
