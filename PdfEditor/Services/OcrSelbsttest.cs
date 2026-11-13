using SkiaSharp;

namespace PdfEditor.Services;

/// <summary>
/// Selbstprüfung der Texterkennung (nur Debug, <c>perPDF.exe --selbsttest</c>): Scan mit Text →
/// Windows-OCR → unsichtbare Textebene → mit PdfPig zurücklesen, auch auf einer gedrehten Seite.
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

        // Seite 1 normal, Seite 2 um −90° gezeichnet und per /Rotate 90 aufgerichtet.
        byte[] pdf = ScanPdf.Erstellen([(TextPng(0), ScanFarbe.Graustufen, 300),
                                        (TextPng(-90), ScanFarbe.Graustufen, 300)]);
        var dienst = new PdfDokumentDienst();
        dienst.ÖffnenAusBytes(pdf, null);
        dienst.SeitenDrehen([1], 90);
        var infos = dienst.SeitenInfosLesen();
        var text = new PdfTextDienst();
        Prüfen(text.ZeichenJeSeite(dienst.AktuelleBytes!, 2) is [0, 0], "Scan hat vorher keinen Text");

        var wörter = new Dictionary<int, List<OcrWort>>();
        for (int i = 0; i < 2; i++)
            wörter[i] = await OcrDienst.SeiteErkennen(erkennung!, dienst.AktuelleBytes!, i, infos[i]);
        dienst.TextebeneEinfügen(wörter);

        for (int i = 0; i < 2; i++)
        {
            var zeilen = text.ZeilenLesen(dienst.AktuelleBytes!, i, infos[i].X1, infos[i].Y1);
            string gelesen = string.Join(" ", zeilen.Select(z => z.Text));
            Prüfen(gelesen.Contains("Rechnung") && gelesen.Contains("4711"), $"Seite {i + 1}: Text „{gelesen}“");

            // PdfPig liefert gedrehte Seiten bereits in Anzeigeausrichtung (Ursprung unten links).
            // In Testbild-Pixeln (300 DPI) muss die Zeile waagerecht oben links bei (200, 300) beginnen.
            var z = zeilen.First(z => z.Text.Contains("Rechnung"));
            double anzeigeHöhe = infos[i].DrehungGrad % 180 == 0 ? infos[i].Höhe : infos[i].Breite;
            double px = 300 / 72.0, links = z.X1 * px, oben = (anzeigeHöhe - z.Y2) * px;
            string lage = $"({links:0}, {oben:0}) {z.X2 - z.X1:0}×{z.Y2 - z.Y1:0} pt";
            Prüfen(z.X2 - z.X1 > 4 * (z.Y2 - z.Y1), $"Seite {i + 1}: Zeile waagerecht {lage}");
            Prüfen(Math.Abs(links - 200) < 40 && Math.Abs(oben - 300) < 60, $"Seite {i + 1}: Lage {lage}");
        }
        Prüfen(text.ZeichenJeSeite(dienst.AktuelleBytes!, 2).All(n => n >= 20), "Seiten gelten danach als „hat Text“");
    }

    /// <summary>A4 bei 300 DPI; der Satz steht (nach Rückdrehung um −<paramref name="grad"/>) oben links bei (200, 300).</summary>
    private static byte[] TextPng(float grad)
    {
        const int b = 2480, h = 3508;
        using var bmp = new SKBitmap(b, h);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            if (grad == -90)
            {
                // Inhalt so drehen, dass er nach 90° im Uhrzeigersinn aufrecht im Querformat steht.
                c.Translate(0, h);
                c.RotateDegrees(-90);
            }
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
