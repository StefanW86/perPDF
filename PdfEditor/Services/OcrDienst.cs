using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using PDFtoImage;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace PdfEditor.Services;

/// <summary>
/// Ein erkanntes Wort in absoluten PDF-Punkten: <see cref="Ursprung"/> = linke untere Ecke der
/// Zeile, <see cref="Grundlinie"/> = Vektor entlang der Wortbreite, <see cref="Oben"/> = Vektor
/// über die Zeilenhöhe. Als Vektoren statt Rechteck, weil Text auf gedrehten Seiten im
/// PDF-Raum senkrecht oder kopfüber verläuft.
/// </summary>
public record OcrWort(string Text, Point Ursprung, Vector Grundlinie, Vector Oben);

/// <summary>
/// Texterkennung über die in Windows integrierte OCR (<c>Windows.Media.Ocr</c>). Sprache ist
/// stets die erste Windows-Profilsprache, für die eine Texterkennung installiert ist.
/// </summary>
public static class OcrDienst
{
    /// <summary>Erzeugt die Erkennung für die Windows-Sprache oder <c>null</c>, wenn keine installiert ist.</summary>
    public static OcrEngine? ErkennungErstellen() => OcrEngine.TryCreateFromUserProfileLanguages();

    /// <summary>
    /// Rendert eine Seite (wie angezeigt, also gedreht), erkennt den Text und rechnet jedes Wort
    /// zurück in PDF-Punkte. Leere Liste, wenn nichts erkannt wurde.
    /// </summary>
    public static async Task<List<OcrWort>> SeiteErkennen(OcrEngine erkennung, byte[] pdf,
        int seitenIndex, SeitenInfo info)
    {
        // 300 DPI ist für Scans ein guter Kompromiss; große Seiten auf das OCR-Limit verkleinern.
        double längsteSeitePt = Math.Max(info.Breite, info.Höhe);
        int dpi = (int)Math.Min(300, OcrEngine.MaxImageDimension * 72.0 / längsteSeitePt);

        var bild = await Task.Run(() => BildErzeugen(pdf, seitenIndex, dpi));
        var ergebnis = await erkennung.RecognizeAsync(bild);
        bild.Dispose();

        // Bei zoom = dpi/96 entspricht ein Pixel genau einer Anzeige-DIP.
        var geo = new SeitenGeometrie(info, dpi / 96.0);
        var wörter = new List<OcrWort>();
        foreach (var zeile in ergebnis.Lines)
        {
            if (zeile.Words.Count == 0)
                continue;
            // Einheitliche Höhe je Zeile: Wortboxen sind je nach Ober-/Unterlängen unterschiedlich hoch.
            double oben = zeile.Words.Min(w => w.BoundingRect.Top);
            double unten = zeile.Words.Max(w => w.BoundingRect.Bottom);
            foreach (var w in zeile.Words)
            {
                double links = w.BoundingRect.Left, rechts = w.BoundingRect.Right;
                Point ursprung = geo.AnzeigeNachPunkt(links, unten);
                wörter.Add(new OcrWort(w.Text, ursprung,
                    geo.AnzeigeNachPunkt(rechts, unten) - ursprung,
                    geo.AnzeigeNachPunkt(links, oben) - ursprung));
            }
        }
        return wörter;
    }

    private static SoftwareBitmap BildErzeugen(byte[] pdf, int seitenIndex, int dpi)
    {
        // Ohne Formularfelder: deren Werte sind bereits echter Text und würden sonst doppelt erkannt.
        using var gerendert = Conversion.ToImage(pdf, page: seitenIndex,
            options: new RenderOptions { Dpi = dpi, WithAnnotations = true, WithFormFill = false });
        using var bgra = gerendert.ColorType == SKColorType.Bgra8888
            ? null
            : gerendert.Copy(SKColorType.Bgra8888);
        var quelle = bgra ?? gerendert;
        return SoftwareBitmap.CreateCopyFromBuffer(quelle.Bytes.AsBuffer(),
            BitmapPixelFormat.Bgra8, quelle.Width, quelle.Height, BitmapAlphaMode.Premultiplied);
    }
}
