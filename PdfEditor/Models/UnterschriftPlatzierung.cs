using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Eine noch nicht gespeicherte Platzierung einer Unterschrift auf einer Seite.
/// Position und Größe werden in PDF-Punkten (Ursprung unten links, ungedreht)
/// gespeichert, damit sie beim Speichern korrekt eingezeichnet werden können.
/// Die Zielseite wird über ihren Index angesprochen.
/// </summary>
public class UnterschriftPlatzierung : BeobachtbaresObjekt, IAufSeite
{
    public UnterschriftPlatzierung(Unterschrift unterschrift, int seitenIndex)
    {
        Unterschrift = unterschrift;
        SeitenIndex = seitenIndex;
    }

    public Unterschrift Unterschrift { get; }

    private int _seitenIndex;
    /// <summary>0-basierter Index der Zielseite.</summary>
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
}
