using System.Windows;
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
    /// PdfPig liefert Anzeige-Punkte (siehe <see cref="NachPdf"/>); die Zeilen werden in
    /// absolute, ungedrehte PDF-Punkte umgerechnet, damit sie zu <see cref="SeitenGeometrie"/>
    /// passen und eine spätere Seitendrehung überstehen. <see cref="TextZeile.ZeichenX"/>
    /// bleibt dabei Anteil der Zeilenlänge in Leserichtung, abgebildet auf X1..X2.
    /// </para>
    /// </summary>
    public List<TextZeile> ZeilenLesen(byte[] pdf, int seitenIndex, SeitenInfo info)
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

                    var b = zeile.BoundingBox;
                    double x1 = Math.Min(b.Left, b.Right);
                    double x2 = Math.Max(b.Left, b.Right);
                    double y1 = Math.Min(b.Bottom, b.Top);
                    double y2 = Math.Max(b.Bottom, b.Top);

                    // Glyphenboxen enden oft an der Grundlinie (keine/ungenaue Unterlängen),
                    // die Markierung sähe unten abgeschnitten aus. Daher auf die typische
                    // Schrifthöhe (Unterlänge ~25 %, Oberlänge ~80 % der Schriftgröße) erweitern.
                    var buchstaben = zeilenWörter.SelectMany(w => w.Letters).ToList();
                    if (buchstaben.Count > 0 && buchstaben.All(l => l.TextOrientation == TextOrientation.Horizontal))
                    {
                        double größe = buchstaben.Max(l => l.PointSize);
                        double grundlinie = buchstaben.Min(l => l.StartBaseLine.Y);
                        y1 = Math.Min(y1, grundlinie - 0.25 * größe);
                        y2 = Math.Max(y2, grundlinie + 0.8 * größe);
                    }
                    var r = RechteckNachPdf(info, x1, y1, x2, y2);
                    for (int i = 0; i < zeichenX.Length; i++)
                        zeichenX[i] = r.X1 + (zeichenX[i] - x1) / Math.Max(1e-9, x2 - x1) * (r.X2 - r.X1);
                    ergebnis.Add(new TextZeile(r.X1, r.Y1, r.X2, r.Y2, text, zeichenX));
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
    /// Anzahl sichtbarer Zeichen echten Texts je Seite (für die Texterkennung: Seiten mit Text
    /// überspringen). Bei nicht lesbarem Dokument 0 für alle Seiten.
    /// </summary>
    public int[] ZeichenJeSeite(byte[] pdf, int seitenAnzahl)
    {
        var ergebnis = new int[seitenAnzahl];
        try
        {
            using var doc = PdfDocument.Open(pdf);
            for (int i = 0; i < Math.Min(seitenAnzahl, doc.NumberOfPages); i++)
                ergebnis[i] = doc.GetPage(i + 1).Letters.Count(l => !string.IsNullOrWhiteSpace(l.Value));
        }
        catch
        {
            // Nicht lesbar → alle Seiten gelten als ohne Text.
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
    public List<Beschriftung?> Beschriftungen(byte[] pdf, int seitenIndex, SeitenInfo info,
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> schaltflächen)
    {
        var ergebnis = new List<Beschriftung?>(new Beschriftung?[schaltflächen.Count]);
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (seitenIndex < 0 || seitenIndex >= doc.NumberOfPages)
                return ergebnis;
            var seite = doc.GetPage(seitenIndex + 1);

            // Gesucht wird in PdfPigs Anzeige-Punkten („rechts daneben“ = wie angezeigt);
            // Schaltflächen hinein-, Ergebnisse zurückrechnen.
            var wörter = seite.GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => (
                    Text: w.Text,
                    Links: Math.Min(w.BoundingBox.Left, w.BoundingBox.Right),
                    Rechts: Math.Max(w.BoundingBox.Left, w.BoundingBox.Right),
                    Unten: Math.Min(w.BoundingBox.Bottom, w.BoundingBox.Top),
                    Oben: Math.Max(w.BoundingBox.Bottom, w.BoundingBox.Top),
                    Größe: w.Letters.Count > 0 ? w.Letters.Select(l => l.PointSize).OrderBy(p => p).ElementAt(w.Letters.Count / 2) : 0))
                .ToList();

            for (int i = 0; i < schaltflächen.Count; i++)
            {
                var b = RechteckNachPdfPig(info, schaltflächen[i]);
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

                var r = RechteckNachPdf(info, links, unten, rechts, oben);
                ergebnis[i] = new Beschriftung(string.Join(" ", teile), r.X1, r.Y1, r.X2, r.Y2, größe);
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
                zeilen = ZeilenLesen(pdf, s, seitenInfos[s]);
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
    /// PdfPig liefert Koordinaten gedrehter Seiten bereits in Anzeigeausrichtung: Punkte,
    /// Ursprung unten links der <em>angezeigten</em> Seite, ohne MediaBox-Versatz. Rechnet
    /// einen solchen Punkt in absolute, ungedrehte PDF-Punkte um.
    /// </summary>
    internal static Point NachPdf(SeitenInfo info, double x, double y)
    {
        var geo = new SeitenGeometrie(info, 72.0 / 96.0); // 1 DIP = 1 pt
        return geo.AnzeigeNachPunkt(x, geo.AnzeigeHöhe - y);
    }

    /// <summary>Umkehrung von <see cref="NachPdf"/>: PDF-Punkt → PdfPig-Anzeige-Punkt.</summary>
    internal static Point NachPdfPig(SeitenInfo info, double x, double y)
    {
        var geo = new SeitenGeometrie(info, 72.0 / 96.0);
        var p = geo.PunktNachAnzeige(x, y);
        return new Point(p.X, geo.AnzeigeHöhe - p.Y);
    }

    private static (double X1, double Y1, double X2, double Y2) RechteckNachPdf(
        SeitenInfo info, double x1, double y1, double x2, double y2) =>
        Umschließend(NachPdf(info, x1, y1), NachPdf(info, x2, y2));

    private static (double X1, double Y1, double X2, double Y2) RechteckNachPdfPig(
        SeitenInfo info, (double X1, double Y1, double X2, double Y2) r) =>
        Umschließend(NachPdfPig(info, r.X1, r.Y1), NachPdfPig(info, r.X2, r.Y2));

    private static (double X1, double Y1, double X2, double Y2) Umschließend(Point a, Point b) =>
        (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

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
