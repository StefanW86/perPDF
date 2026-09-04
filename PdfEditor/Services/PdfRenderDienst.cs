using System.IO;
using System.Windows.Media.Imaging;
using PDFtoImage;
using SkiaSharp;

namespace PdfEditor.Services;

/// <summary>
/// Rendert PDF-Seiten mit PDFium (über die Bibliothek PDFtoImage) zu Bildern,
/// die in WPF angezeigt werden können. Es wird aus den aktuellen Dokument-Bytes
/// gerendert, sodass strukturelle Änderungen (Drehung, Reihenfolge usw.) sofort
/// sichtbar werden.
/// </summary>
public static class PdfRenderDienst
{
    /// <summary>Rendert eine einzelne Seite in voller Anzeigegröße.</summary>
    public static BitmapSource SeiteRendern(byte[] pdf, int seitenIndex, int dpi)
    {
        var optionen = new RenderOptions
        {
            Dpi = dpi,
            WithAnnotations = true,
            // Formularfelder werden als eigene Eingabe-Steuerelemente überlagert,
            // daher hier nicht von PDFium mitgezeichnet.
            WithFormFill = false
        };
        return Rendern(pdf, seitenIndex, optionen);
    }

    /// <summary>Rendert eine Seite als kleines Miniaturbild mit fester Breite.</summary>
    public static BitmapSource MiniaturRendern(byte[] pdf, int seitenIndex, int breitePx)
    {
        var optionen = new RenderOptions
        {
            Width = breitePx,
            WithAspectRatio = true,
            WithAnnotations = true,
            WithFormFill = true
        };
        return Rendern(pdf, seitenIndex, optionen);
    }

    /// <summary>
    /// Tastet für jedes Rechteck (in absoluten PDF-Punkten) die dominante Farbe aus dem
    /// gerenderten Seitenbild ab – die Hintergrundfarbe der Fläche (Text ist gegenüber dem
    /// Untergrund in der Minderheit). Dient dem farbtreuen Überdecken eingebrannter
    /// Beschriftungen. Liefert je Rechteck einen #RRGGBB-Wert (Weiß als Rückfall).
    /// Hinweis: ohne Berücksichtigung der Seitendrehung (für ungedrehte Seiten gedacht).
    /// </summary>
    public static List<string> HintergrundFarben(byte[] pdf, int seitenIndex, SeitenInfo info,
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> rechtecke)
    {
        var ergebnis = new List<string>();
        const int dpi = 100;
        double skala = dpi / 72.0;

        SKBitmap? bm = null;
        try
        {
            bm = Conversion.ToImage(pdf, page: seitenIndex,
                options: new RenderOptions { Dpi = dpi, WithAnnotations = true, WithFormFill = false });
        }
        catch { /* Render fehlgeschlagen → Weiß */ }

        using (bm)
        {
            double oberkante = info.Y1 + info.Höhe;
            foreach (var r in rechtecke)
            {
                if (bm is null)
                {
                    ergebnis.Add("#FFFFFF");
                    continue;
                }
                int px1 = (int)Math.Floor((r.X1 - info.X1) * skala);
                int px2 = (int)Math.Ceiling((r.X2 - info.X1) * skala);
                int py1 = (int)Math.Floor((oberkante - r.Y2) * skala);
                int py2 = (int)Math.Ceiling((oberkante - r.Y1) * skala);
                ergebnis.Add(DominanteFarbe(bm, px1, py1, px2, py2));
            }
        }
        return ergebnis;
    }

    /// <summary>
    /// Häufigste Farbe in einem Pixelbereich als #RRGGBB. Zum Zusammenfassen
    /// (Antialiasing-Ränder) werden die Farben auf 8er-Stufen quantisiert <em>gruppiert</em>,
    /// zurückgegeben wird aber die <em>tatsächlich</em> abgetastete Farbe der häufigsten Gruppe –
    /// sonst würde reines Weiß (255) auf 248 abrutschen und einen leicht grauen Kasten erzeugen.
    /// </summary>
    private static string DominanteFarbe(SKBitmap bm, int x1, int y1, int x2, int y2)
    {
        x1 = Math.Clamp(x1, 0, bm.Width); x2 = Math.Clamp(x2, 0, bm.Width);
        y1 = Math.Clamp(y1, 0, bm.Height); y2 = Math.Clamp(y2, 0, bm.Height);
        if (x2 <= x1 || y2 <= y1)
            return "#FFFFFF";

        var zähler = new Dictionary<int, int>();
        var musterFarbe = new Dictionary<int, SKColor>();
        int schrittX = Math.Max(1, (x2 - x1) / 30);
        int schrittY = Math.Max(1, (y2 - y1) / 12);
        for (int y = y1; y < y2; y += schrittY)
            for (int x = x1; x < x2; x += schrittX)
            {
                SKColor c = bm.GetPixel(x, y);
                int key = (Q(c.Red) << 16) | (Q(c.Green) << 8) | Q(c.Blue);
                zähler[key] = zähler.GetValueOrDefault(key) + 1;
                musterFarbe.TryAdd(key, c); // echte Farbe der Gruppe merken
            }
        if (zähler.Count == 0)
            return "#FFFFFF";

        int häufig = zähler.Aggregate((a, b) => b.Value > a.Value ? b : a).Key;
        SKColor farbe = musterFarbe[häufig];
        return $"#{farbe.Red:X2}{farbe.Green:X2}{farbe.Blue:X2}";
    }

    private static int Q(byte v) => v & 0xF8; // nur zum Gruppieren: auf Vielfache von 8

    private static BitmapSource Rendern(byte[] pdf, int seitenIndex, RenderOptions optionen)
    {
        using var strom = new MemoryStream();
        Conversion.SavePng(strom, pdf, page: seitenIndex, options: optionen);
        strom.Position = 0;

        // Vollständig in den Speicher laden und einfrieren, damit das Bild
        // threadübergreifend nutzbar ist und der Strom freigegeben werden kann.
        var bild = new BitmapImage();
        bild.BeginInit();
        bild.CacheOption = BitmapCacheOption.OnLoad;
        bild.StreamSource = strom;
        bild.EndInit();
        bild.Freeze();
        return bild;
    }
}
