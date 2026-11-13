using SkiaSharp;

namespace PdfEditor.Services;

/// <summary>
/// Selbstprüfung der Texterkennung (nur Debug, <c>perPDF.exe --selbsttest</c>): Scan mit Text →
/// Windows-OCR → unsichtbare Textebene → mit PdfPig zurücklesen, auch auf um 90/180/270° gedrehten
/// Seiten (prüft zugleich, dass <see cref="PdfTextDienst.ZeilenLesen"/> dort richtig sitzt).
/// </summary>
internal static class OcrSelbsttest
{
    private const string Satz = "Rechnung Nummer 4711 vom Montag";

    // Ohne SynchronizationContext laufen lassen, sonst blockiert das Warten auf die OCR den UI-Thread.
    internal static void Ausführen() => Task.Run(Prüfung).GetAwaiter().GetResult();

    private static async Task Prüfung()
    {
        var erkennung = OcrDienst.ErkennungErstellen();
        Prüfen(erkennung is not null, "keine Windows-OCR-Sprache installiert");

        // Seite i wird um −90°·i gezeichnet und per /Rotate 90°·i wieder aufgerichtet.
        int[] drehungen = [0, 90, 180, 270];
        byte[] pdf = ScanPdf.Erstellen(drehungen.Select(g => (TextPng(g), ScanFarbe.Graustufen, 300)).ToList());
        var dienst = new PdfDokumentDienst();
        dienst.ÖffnenAusBytes(pdf, null);
        for (int i = 1; i < drehungen.Length; i++)
            dienst.SeitenDrehen([i], drehungen[i]);
        var infos = dienst.SeitenInfosLesen();
        var text = new PdfTextDienst();
        int n = drehungen.Length;
        Prüfen(text.ZeichenJeSeite(dienst.AktuelleBytes!, n).All(z => z == 0), "Scan hat vorher keinen Text");

        var wörter = new Dictionary<int, List<OcrWort>>();
        for (int i = 0; i < n; i++)
            wörter[i] = await OcrDienst.SeiteErkennen(erkennung!, dienst.AktuelleBytes!, i, infos[i]);
        dienst.TextebeneEinfügen(wörter);

        for (int i = 0; i < n; i++)
        {
            var zeilen = text.ZeilenLesen(dienst.AktuelleBytes!, i, infos[i]);
            string gelesen = string.Join(" ", zeilen.Select(z => z.Text));
            Prüfen(gelesen.Contains("Rechnung") && gelesen.Contains("4711"), $"Seite {i + 1}: Text „{gelesen}“");

            // Wie die Textauswahl: Zeile über SeitenGeometrie in die Anzeige (hier 300-DPI-Pixel) –
            // sie muss waagerecht oben links bei (200, 300) beginnen.
            var z = zeilen.First(z => z.Text.Contains("Rechnung"));
            var r = new SeitenGeometrie(infos[i], 300 / 96.0).RechteckNachAnzeige(z.X1, z.Y1, z.X2, z.Y2);
            string lage = $"({r.Left:0}, {r.Top:0}) {r.Width:0}×{r.Height:0} px";
            Prüfen(r.Width > 4 * r.Height, $"Seite {i + 1} ({drehungen[i]}°): Zeile waagerecht {lage}");
            Prüfen(Math.Abs(r.Left - 200) < 40 && Math.Abs(r.Top - 300) < 60, $"Seite {i + 1} ({drehungen[i]}°): Lage {lage}");
            // Zeichengrenzen in Leserichtung aufsteigend, das „N“ von „Nummer“ etwa auf 30–40 % der Zeile.
            Prüfen(z.ZeichenX.Zip(z.ZeichenX.Skip(1)).All(p => p.Second >= p.First - 0.01), $"Seite {i + 1}: Zeichengrenzen aufsteigend");
            int nIdx = z.Text.IndexOf("Nummer", StringComparison.Ordinal);
            double anteil = nIdx < 0 ? -1 : (z.ZeichenX[nIdx] - z.X1) / (z.X2 - z.X1);
            Prüfen(anteil is > 0.2 and < 0.5, $"Seite {i + 1}: „Nummer“ bei Anteil {anteil:0.00}");
        }
        Prüfen(text.ZeichenJeSeite(dienst.AktuelleBytes!, n).All(z => z >= 20), "Seiten gelten danach als „hat Text“");
    }

    /// <summary>
    /// A4 bei 300 DPI; der Satz steht, nachdem die Seite um <paramref name="drehung"/>° im
    /// Uhrzeigersinn gedreht angezeigt wird, aufrecht oben links bei (200, 300).
    /// </summary>
    private static byte[] TextPng(int drehung)
    {
        const int b = 2480, h = 3508;
        using var bmp = new SKBitmap(b, h);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            // Anzeige → ungedrehtes Bild: um −drehung drehen, Anzeige-Ursprung auf die passende Ecke.
            var (ex, ey) = drehung switch { 90 => (0, h), 180 => (b, h), 270 => (b, 0), _ => (0, 0) };
            c.Translate(ex, ey);
            c.RotateDegrees(-drehung);
            using var schrift = new SKFont(SKTypeface.FromFamilyName("Arial"), 80);
            using var stift = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            c.DrawText(Satz, 200, 380, schrift, stift);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Prüfen(bool ok, string was)
    {
        if (!ok) throw new InvalidOperationException("OCR-Selbsttest fehlgeschlagen: " + was);
    }
}
