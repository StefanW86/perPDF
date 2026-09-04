using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace PdfEditor.Services;

/// <summary>
/// Eine erkannte Textzeile einer PDF-Seite mit ihrem umschließenden Rechteck
/// (in PDF-Punkten, Ursprung unten links) und dem enthaltenen Text.
/// <para>
/// <see cref="ZeichenX"/> enthält für jede Zeichengrenze die linke X-Position in
/// PDF-Punkten (Länge = <c>Text.Length + 1</c>; der letzte Wert ist die rechte
/// Kante des letzten Zeichens). Diese Werte stammen aus den <em>echten</em>
/// Glyphen-Vorschüben (PdfPig), sodass die Markierung exakt den Buchstaben folgt –
/// unabhängig von der verwendeten PDF-Schrift.
/// </para>
/// </summary>
public record TextZeile(double X1, double Y1, double X2, double Y2, string Text,
    double[] ZeichenX);

/// <summary>
/// Eine aus dem Seiteninhalt gelesene Beschriftung (z. B. neben einer Optionsfeld-Schaltfläche):
/// Text, umschließendes Rechteck (absolute PDF-Punkte, Ursprung unten links) und Schriftgröße.
/// </summary>
public record Beschriftung(string Text, double X1, double Y1, double X2, double Y2, double Schriftgröße);

/// <summary>
/// Liest den vorhandenen Text einer PDF-Seite mitsamt Position aus (über PdfPig).
/// Damit lässt sich über der gerenderten Seite eine markier- und kopierbare
/// Textebene legen. PDFsharp/PDFium liefern keine Textpositionen, daher PdfPig.
/// </summary>
public class PdfTextDienst
{
    /// <summary>
    /// Liest die Textzeilen der angegebenen Seite (0-basiert). Gibt bei Fehlern
    /// oder Seiten ohne extrahierbaren Text eine leere Liste zurück.
    /// <para>
    /// PdfPig normalisiert seine Koordinaten auf den MediaBox-Ursprung (0,0). Damit
    /// sie zur restlichen App passen (die mit dem absoluten PDF-Raum von PdfSharp
    /// rechnet, vgl. <see cref="SeitenGeometrie"/>), wird die MediaBox-Ecke
    /// (<paramref name="versatzX"/>/<paramref name="versatzY"/>) wieder aufaddiert.
    /// Andernfalls landet der Text auf Seiten mit MediaBox-Ursprung ≠ 0 versetzt.
    /// </para>
    /// </summary>
    public List<TextZeile> ZeilenLesen(byte[] pdf, int seitenIndex,
        double versatzX = 0, double versatzY = 0)
    {
        var ergebnis = new List<TextZeile>();
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (seitenIndex < 0 || seitenIndex >= doc.NumberOfPages)
                return ergebnis;

            var seite = doc.GetPage(seitenIndex + 1); // PdfPig zählt ab 1
            var wörter = seite.GetWords(NearestNeighbourWordExtractor.Instance);
            var blöcke = DocstrumBoundingBoxes.Instance.GetBlocks(wörter);

            foreach (var block in blöcke)
            {
                foreach (var zeile in block.TextLines)
                {
                    var zeilenWörter = zeile.Words.Where(w => !string.IsNullOrEmpty(w.Text)).ToList();
                    if (zeilenWörter.Count == 0)
                        continue;

                    // Zeilentext aus den Wörtern mit einfachen Leerzeichen zusammensetzen
                    // und parallel die Zeichengrenzen aus den echten Glyphen aufbauen.
                    string text = string.Join(" ", zeilenWörter.Select(w => w.Text));
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    double[] zeichenX = ZeichengrenzenBauen(zeilenWörter, text.Length);
                    for (int i = 0; i < zeichenX.Length; i++)
                        zeichenX[i] += versatzX;

                    var b = zeile.BoundingBox;
                    double x1 = Math.Min(b.Left, b.Right) + versatzX;
                    double x2 = Math.Max(b.Left, b.Right) + versatzX;
                    double y1 = Math.Min(b.Bottom, b.Top) + versatzY;
                    double y2 = Math.Max(b.Bottom, b.Top) + versatzY;
                    ergebnis.Add(new TextZeile(x1, y1, x2, y2, text, zeichenX));
                }
            }
        }
        catch
        {
            // Bei nicht lesbaren/gesicherten Dokumenten bleibt die Ebene leer.
        }
        return ergebnis;
    }

    /// <summary>
    /// Liest für jede übergebene Schaltfläche (Widget-Rechteck in absoluten PDF-Punkten) die
    /// danebenstehende Beschriftung aus dem Seiteninhalt: die Wörter rechts der Schaltfläche
    /// auf derselben Zeile, bis eine große horizontale Lücke kommt. Damit lassen sich die
    /// (im Seiteninhalt eingebrannten) Optionsfeld-Beschriftungen als editierbare Textnotizen
    /// übernehmen. Das Ergebnis ist positionsgleich zur Eingabe; ohne erkannte Beschriftung
    /// steht <c>null</c>.
    /// </summary>
    public List<Beschriftung?> Beschriftungen(byte[] pdf, int seitenIndex,
        double versatzX, double versatzY,
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> schaltflächen)
    {
        var ergebnis = new List<Beschriftung?>(new Beschriftung?[schaltflächen.Count]);
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (seitenIndex < 0 || seitenIndex >= doc.NumberOfPages)
                return ergebnis;
            var seite = doc.GetPage(seitenIndex + 1);

            // Wörter in absolute PDF-Punkte überführen (wie ZeilenLesen).
            var wörter = seite.GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => (
                    Text: w.Text,
                    Links: Math.Min(w.BoundingBox.Left, w.BoundingBox.Right) + versatzX,
                    Rechts: Math.Max(w.BoundingBox.Left, w.BoundingBox.Right) + versatzX,
                    Unten: Math.Min(w.BoundingBox.Bottom, w.BoundingBox.Top) + versatzY,
                    Oben: Math.Max(w.BoundingBox.Bottom, w.BoundingBox.Top) + versatzY,
                    Größe: w.Letters.Count > 0 ? w.Letters.Select(l => l.PointSize).OrderBy(p => p).ElementAt(w.Letters.Count / 2) : 0))
                .ToList();

            for (int i = 0; i < schaltflächen.Count; i++)
            {
                var b = schaltflächen[i];
                double mitteY = (b.Y1 + b.Y2) / 2;
                double höhe = b.Y2 - b.Y1;
                double toleranz = Math.Max(6, höhe * 0.6);

                // Kandidaten: rechts der Schaltfläche, auf gleicher Zeilenhöhe, nach X sortiert.
                var reihe = wörter
                    .Where(w => w.Links >= b.X2 - 1 && Math.Abs((w.Unten + w.Oben) / 2 - mitteY) <= toleranz)
                    .OrderBy(w => w.Links)
                    .ToList();
                if (reihe.Count == 0)
                    continue;

                double größe = reihe[0].Größe > 0 ? reihe[0].Größe : Math.Max(8, höhe * 0.8);
                double lückeMax = größe * 2.5;

                var teile = new List<string> { reihe[0].Text };
                double links = reihe[0].Links, rechts = reihe[0].Rechts;
                double unten = reihe[0].Unten, oben = reihe[0].Oben;
                for (int k = 1; k < reihe.Count; k++)
                {
                    if (reihe[k].Links - rechts > lückeMax)
                        break; // große Lücke → nächste Spalte, gehört nicht mehr zur Beschriftung
                    teile.Add(reihe[k].Text);
                    rechts = Math.Max(rechts, reihe[k].Rechts);
                    unten = Math.Min(unten, reihe[k].Unten);
                    oben = Math.Max(oben, reihe[k].Oben);
                }

                ergebnis[i] = new Beschriftung(string.Join(" ", teile), links, unten, rechts, oben, größe);
            }
        }
        catch
        {
            // Bei nicht lesbaren Dokumenten bleiben die Beschriftungen leer (null).
        }
        return ergebnis;
    }

    /// <summary>
    /// Sucht einen Begriff (Groß-/Kleinschreibung ignorierend, Teilwortsuche) über alle
    /// Seiten des Dokuments. Bereits vorhandene Einträge im <paramref name="cache"/> werden
    /// genutzt; fehlende Seiten werden geladen und nachgetragen. Gibt eine Liste von
    /// (Seite, ZeilenIndex, Zeichenstart, Zeichenende) zurück.
    /// </summary>
    public List<(int Seite, int Zeile, int Start, int Ende)> Suchen(
        byte[] pdf, string suchbegriff, IReadOnlyList<SeitenInfo> seitenInfos,
        Dictionary<int, List<TextZeile>> cache)
    {
        var ergebnisse = new List<(int, int, int, int)>();
        if (string.IsNullOrEmpty(suchbegriff) || seitenInfos.Count == 0)
            return ergebnisse;

        for (int s = 0; s < seitenInfos.Count; s++)
        {
            if (!cache.TryGetValue(s, out var zeilen))
            {
                var info = seitenInfos[s];
                zeilen = ZeilenLesen(pdf, s, info.X1, info.Y1);
                cache[s] = zeilen;
            }

            for (int zi = 0; zi < zeilen.Count; zi++)
            {
                string text = zeilen[zi].Text;
                int pos = 0;
                while (true)
                {
                    int fund = text.IndexOf(suchbegriff, pos, StringComparison.OrdinalIgnoreCase);
                    if (fund < 0) break;
                    ergebnisse.Add((s, zi, fund, fund + suchbegriff.Length));
                    pos = fund + 1;
                }
            }
        }
        return ergebnisse;
    }

    /// <summary>
    /// Baut die linke X-Position jeder Zeichengrenze einer Zeile (Länge+1 Werte) aus
    /// den tatsächlichen Glyphen-Vorschüben auf. Wörter werden durch ein Leerzeichen
    /// getrennt, dessen Zelle den realen Wortabstand überspannt. Stimmt für ein Wort
    /// die Buchstabenzahl nicht mit dem Text überein (z. B. Ligaturen), wird dieses
    /// Wort linear über seine Wort-Box verteilt.
    /// </summary>
    private static double[] ZeichengrenzenBauen(List<Word> wörter, int textLänge)
    {
        var grenzen = new List<double>(textLänge + 1);
        double letztesRechts = 0;

        for (int wi = 0; wi < wörter.Count; wi++)
        {
            Word w = wörter[wi];

            // Leerzeichen-Zelle vor diesem Wort: linke Kante = rechte Kante des Vorworts.
            if (wi > 0)
                grenzen.Add(letztesRechts);

            (double[] linkeKanten, double rechts) = WortKanten(w);
            grenzen.AddRange(linkeKanten);
            letztesRechts = rechts;
        }

        // Rechte Kante des letzten Zeichens abschließen.
        grenzen.Add(letztesRechts);

        // Absicherung: exakt textLänge+1 Werte (sollte stets zutreffen).
        while (grenzen.Count < textLänge + 1)
            grenzen.Add(letztesRechts);
        if (grenzen.Count > textLänge + 1)
            grenzen.RemoveRange(textLänge + 1, grenzen.Count - (textLänge + 1));

        return grenzen.ToArray();
    }

    /// <summary>Linke X-Kante jedes Buchstabens eines Wortes plus die rechte Wortkante (PDF-Punkte).</summary>
    private static (double[] LinkeKanten, double Rechts) WortKanten(Word w)
    {
        string t = w.Text;
        if (w.Letters.Count == t.Length && t.Length > 0)
        {
            var kanten = new double[t.Length];
            for (int i = 0; i < t.Length; i++)
                kanten[i] = w.Letters[i].StartBaseLine.X;
            return (kanten, w.Letters[^1].EndBaseLine.X);
        }

        // Fallback ohne 1:1-Glyphen: linear über die Wort-Box verteilen.
        double l = Math.Min(w.BoundingBox.Left, w.BoundingBox.Right);
        double r = Math.Max(w.BoundingBox.Left, w.BoundingBox.Right);
        var arr = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
            arr[i] = l + (r - l) * i / Math.Max(1, t.Length);
        return (arr, r);
    }
}
