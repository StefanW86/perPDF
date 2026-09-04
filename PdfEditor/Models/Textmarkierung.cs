using System.Windows;
using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Ein vom Benutzer frei mit der Maus gezeichneter Leuchtstift-Strich (Highlight).
/// Der Strich wird als Folge von Punkten in PDF-Punkten (Ursprung unten links,
/// ungedreht) gespeichert und beim Speichern als halbtransparente, dicke Linie
/// in die Seite eingezeichnet.
/// </summary>
public class Textmarkierung : BeobachtbaresObjekt, IAufSeite
{
    public Textmarkierung(int seitenIndex)
    {
        _seitenIndex = seitenIndex;
    }

    private int _seitenIndex;
    public int SeitenIndex
    {
        get => _seitenIndex;
        set => SetzeWert(ref _seitenIndex, value);
    }

    /// <summary>Die Stützpunkte des Striches in PDF-Punkten (Ursprung unten links).</summary>
    public List<Point> Punkte { get; } = new();

    private double _strichbreite = 14;
    /// <summary>Strichbreite (Leuchtstift-Dicke) in PDF-Punkten.</summary>
    public double Strichbreite
    {
        get => _strichbreite;
        set => SetzeWert(ref _strichbreite, value);
    }

    /// <summary>Verschiebt den gesamten Strich um den angegebenen Versatz (in PDF-Punkten).</summary>
    public void Verschieben(double dx, double dy)
    {
        for (int i = 0; i < Punkte.Count; i++)
            Punkte[i] = new Point(Punkte[i].X + dx, Punkte[i].Y + dy);
    }
}
