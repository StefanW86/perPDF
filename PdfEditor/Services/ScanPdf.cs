using System.Diagnostics;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace PdfEditor.Services;

/// <summary>Baut aus gescannten Seitenbildern ein PDF (eine Seite je Bild, in Listenreihenfolge).</summary>
public static class ScanPdf
{
    /// <summary>
    /// Seitengröße = Pixel ÷ DPI. Farbe/Graustufen werden als JPEG (Qualität 85) eingebettet,
    /// Schwarzweiß verlustfrei als Graustufen-PNG (Flate).
    /// </summary>
    public static byte[] Erstellen(IReadOnlyList<(byte[] Png, ScanFarbe Farbe, int Dpi)> seiten)
    {
        using var dok = new PdfDocument();
        foreach (var (png, farbe, dpi) in seiten)
        {
            using var original = SKBitmap.Decode(png)
                ?? throw new InvalidOperationException("Das gescannte Bild ist ungültig.");
            using SKBitmap bild = farbe == ScanFarbe.Farbe ? original.Copy() : Grau(original);
            using SKData kodiert = farbe == ScanFarbe.Schwarzweiß
                ? bild.Encode(SKEncodedImageFormat.Png, 100)
                : bild.Encode(SKEncodedImageFormat.Jpeg, 85);

            PdfPage seite = dok.AddPage();
            seite.Width = XUnit.FromPoint(bild.Width * 72.0 / dpi);
            seite.Height = XUnit.FromPoint(bild.Height * 72.0 / dpi);
            using var strom = new MemoryStream(kodiert.ToArray());
            using var ximage = XImage.FromStream(strom);
            using var gfx = XGraphics.FromPdfPage(seite);
            gfx.DrawImage(ximage, 0, 0, seite.Width.Point, seite.Height.Point);
        }
        using var ziel = new MemoryStream();
        dok.Save(ziel);
        return ziel.ToArray();
    }

    /// <summary>Einkanalige Graustufenkopie (hält JPEG/PNG klein).</summary>
    private static SKBitmap Grau(SKBitmap quelle)
    {
        var grau = new SKBitmap(quelle.Width, quelle.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var c = new SKCanvas(grau);
        c.Clear(SKColors.White); // transparente Bereiche weiß
        c.DrawBitmap(quelle, 0, 0);
        return grau;
    }

    /// <summary>Selbstprüfung (nur Debug, Aufruf: <c>perPDF.exe --selbsttest</c>); wirft bei Fehlschlag.</summary>
    [Conditional("DEBUG")]
    internal static void Selbsttest()
    {
        // 2480×3508 px bei 300 DPI = A4 → 595,3 × 842,0 pt
        byte[] png = TestPng(2480, 3508);
        byte[] pdf = Erstellen([(png, ScanFarbe.Graustufen, 300), (png, ScanFarbe.Graustufen, 300)]);
        using var d = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Prüfen(d.PageCount == 2, "Seitenzahl");
        Prüfen(Math.Abs(d.Pages[0].Width.Point - 595.3) < 1, "Breite A4");
        Prüfen(Math.Abs(d.Pages[0].Height.Point - 842.0) < 1, "Höhe A4");
        Prüfen(pdf.Length < 600_000, $"Größe Graustufen ({pdf.Length} B)");

        byte[] sw = Erstellen([(TestPng(2480, 3508), ScanFarbe.Schwarzweiß, 300)]);
        Prüfen(sw.Length < 600_000, $"Größe Schwarzweiß ({sw.Length} B)");

        // Bildinhalt überlebt die Graustufen-Umwandlung (erster Balken beginnt bei 150/200).
        using (var quelle = SKBitmap.Decode(png))
        using (var grau = Grau(quelle))
        {
            Prüfen(grau.GetPixel(160, 210).Red < 64, "Graustufen: Balken ist dunkel");
            Prüfen(grau.GetPixel(100, 100).Red > 192, "Graustufen: Rand ist hell");
        }

        // Erstellen läuft auch abseits des UI-Threads (Dialog erstellt das PDF im Hintergrund).
        byte[] hinten = ScanDienst.AufStaAsync(() => Erstellen([(png, ScanFarbe.Farbe, 300)])).GetAwaiter().GetResult();
        Prüfen(hinten.Length > 0, "Erstellen im Hintergrund");
    }

    private static void Prüfen(bool ok, string was)
    {
        if (!ok) throw new InvalidOperationException("ScanPdf-Selbsttest fehlgeschlagen: " + was);
    }

    private static byte[] TestPng(int b, int h)
    {
        using var bmp = new SKBitmap(b, h);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            // „Textzeilen“ als Balkenmuster mit Lücken, ähnlich einem Brief
            using var stift = new SKPaint { Color = SKColors.Black };
            for (int y = 200; y < h - 200; y += 120)
                for (int x = 150; x < b - 300; x += 90 + (x * 7 + y) % 60)
                    c.DrawRect(x, y, 40 + (x + y) % 50, 45, stift);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
