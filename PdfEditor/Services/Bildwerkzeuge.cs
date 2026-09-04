using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace PdfEditor.Services;

/// <summary>
/// Hilfsfunktionen für die Bildverarbeitung von Unterschriften. Macht den
/// (üblicherweise weißen) Hintergrund einer Unterschrift transparent, damit
/// darunterliegender PDF-Inhalt nicht überdeckt wird.
/// </summary>
public static class Bildwerkzeuge
{
    // Schwellenwert: Pixel, deren Rot/Grün/Blau alle darüber liegen, gelten als
    // "Hintergrund" und werden transparent gesetzt.
    private const byte Schwelle = 235;

    private static readonly Dictionary<string, byte[]> _zwischenspeicher = new();

    /// <summary>
    /// Liefert das Bild als PNG-Bytes, bei dem nahezu weiße Pixel transparent sind.
    /// Das Ergebnis wird pro Datei zwischengespeichert.
    /// </summary>
    public static byte[] WeißTransparentPng(string pfad)
    {
        if (_zwischenspeicher.TryGetValue(pfad, out var vorhanden))
            return vorhanden;

        using var quelle = SKBitmap.Decode(pfad);
        var info = new SKImageInfo(quelle.Width, quelle.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var ziel = new SKBitmap(info);

        for (int y = 0; y < quelle.Height; y++)
        {
            for (int x = 0; x < quelle.Width; x++)
            {
                SKColor c = quelle.GetPixel(x, y);
                bool istHintergrund = c.Red >= Schwelle && c.Green >= Schwelle && c.Blue >= Schwelle;
                ziel.SetPixel(x, y, istHintergrund
                    ? SKColors.Transparent
                    : new SKColor(c.Red, c.Green, c.Blue, c.Alpha));
            }
        }

        using var bild = SKImage.FromBitmap(ziel);
        using var daten = bild.Encode(SKEncodedImageFormat.Png, 100);
        byte[] ergebnis = daten.ToArray();
        _zwischenspeicher[pfad] = ergebnis;
        return ergebnis;
    }

    /// <summary>
    /// Liefert das Seitenverhältnis (Breite/Höhe) einer Bilddatei, damit eine
    /// Unterschrift unverzerrt platziert werden kann. Bei Fehlern wird 2,5
    /// zurückgegeben.
    /// </summary>
    public static double Seitenverhältnis(string pfad)
    {
        try
        {
            using var bm = SKBitmap.Decode(pfad);
            if (bm is { Height: > 0, Width: > 0 })
                return (double)bm.Width / bm.Height;
        }
        catch { /* Rückfallwert verwenden */ }
        return 2.5;
    }

    /// <summary>
    /// Wandelt beliebige Bilddaten (PNG, JPEG, BMP, GIF, WebP …) in PNG-Bytes um.
    /// Bereits vorliegende PNGs werden unverändert zurückgegeben.
    /// </summary>
    public static byte[] PngNormalisieren(byte[] roh)
    {
        // PNG-Signatur: 89 50 4E 47
        if (roh.Length > 4 && roh[0] == 0x89 && roh[1] == 0x50 && roh[2] == 0x4E && roh[3] == 0x47)
            return roh;

        using var quelle = SKBitmap.Decode(roh)
            ?? throw new InvalidOperationException("Das Bildformat wird nicht unterstützt.");
        using var bild = SKImage.FromBitmap(quelle);
        using var daten = bild.Encode(SKEncodedImageFormat.Png, 100);
        return daten.ToArray();
    }

    /// <summary>Liefert das Seitenverhältnis (Breite/Höhe) von Bild-Bytes (1,0 als Rückfall).</summary>
    public static double Seitenverhältnis(byte[] bytes)
    {
        try
        {
            using var bm = SKBitmap.Decode(bytes);
            if (bm is { Height: > 0, Width: > 0 })
                return (double)bm.Width / bm.Height;
        }
        catch { /* Rückfallwert verwenden */ }
        return 1.0;
    }

    /// <summary>
    /// Wendet Drehung (im Uhrzeigersinn) und anschließenden Zuschnitt (Anteile 0..1
    /// des gedrehten Bildes) auf PNG-Bytes an und liefert das Ergebnis als PNG –
    /// dieselbe Pipeline wie die Bildschirmdarstellung eines eingefügten Bildes,
    /// damit Anzeige und gespeichertes PDF übereinstimmen.
    /// </summary>
    public static byte[] TransformiertesPng(byte[] png, int drehungGrad,
        double zuschnittLinks, double zuschnittOben, double zuschnittRechts, double zuschnittUnten)
    {
        int drehung = ((drehungGrad % 360) + 360) % 360;
        bool ohneZuschnitt = zuschnittLinks <= 0 && zuschnittOben <= 0
            && zuschnittRechts <= 0 && zuschnittUnten <= 0;
        if (drehung == 0 && ohneZuschnitt)
            return png;

        using var quelle = SKBitmap.Decode(png)
            ?? throw new InvalidOperationException("Das Bild konnte nicht gelesen werden.");

        // Erst drehen: Zielgröße bei 90/270 Grad vertauscht.
        int gw = drehung is 90 or 270 ? quelle.Height : quelle.Width;
        int gh = drehung is 90 or 270 ? quelle.Width : quelle.Height;
        using var gedreht = new SKBitmap(new SKImageInfo(gw, gh, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var leinwand = new SKCanvas(gedreht))
        {
            leinwand.Clear(SKColors.Transparent);
            leinwand.Translate(gw / 2f, gh / 2f);
            leinwand.RotateDegrees(drehung);
            leinwand.DrawBitmap(quelle, -quelle.Width / 2f, -quelle.Height / 2f);
        }

        // Dann zuschneiden (Anteile des gedrehten Bildes, an den Pixelraster geklemmt).
        int x = Math.Clamp((int)Math.Round(zuschnittLinks * gw), 0, gw - 1);
        int y = Math.Clamp((int)Math.Round(zuschnittOben * gh), 0, gh - 1);
        int w = Math.Clamp(gw - x - (int)Math.Round(zuschnittRechts * gw), 1, gw - x);
        int h = Math.Clamp(gh - y - (int)Math.Round(zuschnittUnten * gh), 1, gh - y);

        using var ausschnitt = new SKBitmap();
        SKBitmap ergebnisBitmap = gedreht;
        if (x != 0 || y != 0 || w != gw || h != gh)
        {
            if (gedreht.ExtractSubset(ausschnitt, new SKRectI(x, y, x + w, y + h)))
                ergebnisBitmap = ausschnitt;
        }

        using var bild = SKImage.FromBitmap(ergebnisBitmap);
        using var daten = bild.Encode(SKEncodedImageFormat.Png, 100);
        return daten.ToArray();
    }

    /// <summary>Liefert eine WPF-Bildquelle mit transparentem Hintergrund (für die Vorschau).</summary>
    public static BitmapSource TransparentesBild(string pfad)
    {
        byte[] png = WeißTransparentPng(pfad);
        using var strom = new MemoryStream(png);
        var bild = new BitmapImage();
        bild.BeginInit();
        bild.CacheOption = BitmapCacheOption.OnLoad;
        bild.StreamSource = strom;
        bild.EndInit();
        bild.Freeze();
        return bild;
    }
}
