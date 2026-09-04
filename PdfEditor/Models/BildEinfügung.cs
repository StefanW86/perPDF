using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Ein noch nicht gespeichertes, auf einer Seite platziertes Bild (aus Datei oder
/// Zwischenablage). Position und Größe beschreiben das sichtbare (bereits
/// zugeschnittene) Rechteck in PDF-Punkten (Ursprung unten links, ungedreht).
/// Anzeige- und Speicher-Pipeline sind identisch: Original zuerst um
/// <see cref="DrehungGrad"/> drehen, dann die Zuschnitt-Anteile abschneiden.
/// Die Zuschnitt-Werte sind daher Anteile (0..1) des <em>gedrehten</em> Bildes.
/// </summary>
public class BildEinfügung : BeobachtbaresObjekt, IAufSeite
{
    public BildEinfügung(byte[] pngBytes, int seitenIndex)
    {
        PngBytes = pngBytes;
        SeitenIndex = seitenIndex;
    }

    /// <summary>Das unveränderte Originalbild als PNG.</summary>
    public byte[] PngBytes { get; }

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
    /// <summary>Breite des sichtbaren Ausschnitts in PDF-Punkten.</summary>
    public double Breite { get => _breite; set => SetzeWert(ref _breite, value); }

    private double _höhe;
    /// <summary>Höhe des sichtbaren Ausschnitts in PDF-Punkten.</summary>
    public double Höhe { get => _höhe; set => SetzeWert(ref _höhe, value); }

    private int _drehungGrad;
    /// <summary>Drehung des Bildes im Uhrzeigersinn (0, 90, 180 oder 270 Grad).</summary>
    public int DrehungGrad { get => _drehungGrad; set => SetzeWert(ref _drehungGrad, value); }

    private double _zuschnittLinks;
    /// <summary>Abgeschnittener Anteil (0..1) am linken Rand des gedrehten Bildes.</summary>
    public double ZuschnittLinks { get => _zuschnittLinks; set => SetzeWert(ref _zuschnittLinks, value); }

    private double _zuschnittOben;
    /// <summary>Abgeschnittener Anteil (0..1) am oberen Rand des gedrehten Bildes.</summary>
    public double ZuschnittOben { get => _zuschnittOben; set => SetzeWert(ref _zuschnittOben, value); }

    private double _zuschnittRechts;
    /// <summary>Abgeschnittener Anteil (0..1) am rechten Rand des gedrehten Bildes.</summary>
    public double ZuschnittRechts { get => _zuschnittRechts; set => SetzeWert(ref _zuschnittRechts, value); }

    private double _zuschnittUnten;
    /// <summary>Abgeschnittener Anteil (0..1) am unteren Rand des gedrehten Bildes.</summary>
    public double ZuschnittUnten { get => _zuschnittUnten; set => SetzeWert(ref _zuschnittUnten, value); }

    /// <summary>
    /// Dreht das Bild um 90° im Uhrzeigersinn. Die Zuschnitt-Anteile wandern mit
    /// (alter unterer Rand wird zum linken usw.), Breite und Höhe tauschen um den
    /// Mittelpunkt, damit das Bild an Ort und Stelle bleibt.
    /// </summary>
    public void RechtsDrehen()
    {
        DrehungGrad = (DrehungGrad + 90) % 360;
        (ZuschnittLinks, ZuschnittOben, ZuschnittRechts, ZuschnittUnten)
            = (ZuschnittUnten, ZuschnittLinks, ZuschnittOben, ZuschnittRechts);
        GrößeTauschen();
    }

    /// <summary>Dreht das Bild um 90° gegen den Uhrzeigersinn (Umkehrung von <see cref="RechtsDrehen"/>).</summary>
    public void LinksDrehen()
    {
        DrehungGrad = (DrehungGrad + 270) % 360;
        (ZuschnittLinks, ZuschnittOben, ZuschnittRechts, ZuschnittUnten)
            = (ZuschnittOben, ZuschnittRechts, ZuschnittUnten, ZuschnittLinks);
        GrößeTauschen();
    }

    private void GrößeTauschen()
    {
        double mitteX = X + Breite / 2;
        double mitteY = Y + Höhe / 2;
        (Breite, Höhe) = (Höhe, Breite);
        X = mitteX - Breite / 2;
        Y = mitteY - Höhe / 2;
    }
}
