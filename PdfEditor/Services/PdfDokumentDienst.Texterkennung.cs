using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using PdfSharp.Pdf;

namespace PdfEditor.Services;

/// <summary>Schreibt OCR-Ergebnisse als unsichtbare, markier- und durchsuchbare Textebene.</summary>
public partial class PdfDokumentDienst
{
    private const string OcrSchrift = "/perPDFOcr";

    /// <summary>
    /// Legt die erkannten Wörter je Seite als unsichtbaren Text (Darstellungsmodus 3) über das
    /// Seitenbild. Jedes Wort bekommt eine eigene Textmatrix, die es exakt auf seine Box streckt.
    /// </summary>
    public void TextebeneEinfügen(IReadOnlyDictionary<int, List<OcrWort>> seiten) => Transformieren(d =>
    {
        // Standardschrift Helvetica (ohne Einbettung – unsichtbar), Breiten über das metrisch gleiche Arial.
        var schrift = new PdfDictionary(d);
        schrift.Elements["/Type"] = new PdfName("/Font");
        schrift.Elements["/Subtype"] = new PdfName("/Type1");
        schrift.Elements["/BaseFont"] = new PdfName("/Helvetica");
        schrift.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        d.Internals.AddObject(schrift);

        foreach (var (index, wörter) in seiten)
        {
            if (index < 0 || index >= d.PageCount || wörter.Count == 0)
                continue;
            var seite = d.Pages[index];
            var ressourcen = seite.Resources;
            var schriften = ressourcen.Elements.GetDictionary("/Font");
            if (schriften is null)
                ressourcen.Elements["/Font"] = schriften = new PdfDictionary(d);
            schriften.Elements[OcrSchrift] = schrift.Reference;

            // Vorhandenen Inhalt in q/Q kapseln, damit dessen Grafikzustand unsere Matrix nicht verfälscht.
            seite.Contents.PrependContent().CreateStream(Encoding.ASCII.GetBytes("q\n"));
            seite.Contents.AppendContent().CreateStream(TextebeneInhalt(wörter));
        }
    });

    /// <summary>Content-Stream der unsichtbaren Textebene; schließt zuerst das q des Seiteninhalts.</summary>
    private static byte[] TextebeneInhalt(IEnumerable<OcrWort> wörter)
    {
        var inhalt = new List<byte>();
        void Schreibe(string s) => inhalt.AddRange(Encoding.ASCII.GetBytes(s));
        static string Z(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        Schreibe($"Q\nq\nBT\n3 Tr\n{OcrSchrift} 1 Tf\n");
        foreach (var w in wörter)
        {
            byte[] zeichen = WinAnsi.GetBytes(w.Text);
            double breiteEm = BreiteEm(zeichen);
            double länge = w.Grundlinie.Length, höhe = w.Oben.Length;
            if (zeichen.Length == 0 || breiteEm <= 0 || länge <= 0 || höhe <= 0)
                continue;
            Vector richtung = w.Grundlinie / länge, oben = w.Oben / höhe;
            // Arial: Oberlänge 0,905 + Unterlänge 0,212 em füllen die Zeilenhöhe; Grundlinie über der Unterlänge.
            double em = höhe / 1.117;
            Point grundlinie = w.Ursprung + oben * (0.212 * em);
            Vector a = richtung * (länge / breiteEm), c = oben * em;
            Schreibe($"{Z(a.X)} {Z(a.Y)} {Z(c.X)} {Z(c.Y)} {Z(grundlinie.X)} {Z(grundlinie.Y)} Tm (");
            foreach (byte b in zeichen)
                Schreibe(b is (byte)'(' or (byte)')' or (byte)'\\' ? "\\" + (char)b
                    : b is < 32 or > 126 ? "\\" + Convert.ToString(b, 8).PadLeft(3, '0')
                    : ((char)b).ToString());
            Schreibe(") Tj\n");
        }
        Schreibe("ET\nQ\n");
        return inhalt.ToArray();
    }

    // shortcut: Zeichen außerhalb von Windows-1252 (z. B. Kyrillisch) werden zu „?“ – für andere Schriften Type0-Schrift mit ToUnicode nötig.
    private static readonly Encoding WinAnsi = ErzeugeWinAnsi();

    private static Encoding ErzeugeWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private static readonly GlyphTypeface? Arial =
        new Typeface("Arial").TryGetGlyphTypeface(out var g) ? g : null;

    /// <summary>Summe der Zeichenvorschübe in em (Schriftgröße 1).</summary>
    private static double BreiteEm(byte[] zeichen)
    {
        double summe = 0;
        foreach (char c in WinAnsi.GetString(zeichen))
            summe += Arial is not null && Arial.CharacterToGlyphMap.TryGetValue(c, out ushort glyph)
                ? Arial.AdvanceWidths[glyph]
                : 0.556; // mittlere Helvetica-Zeichenbreite
        return summe;
    }
}
