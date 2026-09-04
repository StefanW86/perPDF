using System.Globalization;
using System.IO;
using System.Text;
using PdfEditor.Models;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Services;

/// <summary>Speichern: Formularwerte anwenden und Annotationen ins PDF zeichnen.</summary>
public partial class PdfDokumentDienst
{
    // ----- Speichern --------------------------------------------------------

    /// <summary>
    /// Schreibt die Formularwerte, zeichnet Markierungen, Unterschriften und
    /// Texte ein und speichert das Dokument unter dem angegebenen Pfad.
    /// </summary>
    public void Speichern(string pfad, IEnumerable<FormularFeld> felder,
        IEnumerable<UnterschriftPlatzierung> platzierungen,
        IEnumerable<BildEinfügung> bilder,
        IEnumerable<TextNotiz> texte,
        IEnumerable<Textmarkierung> markierungen,
        IEnumerable<Symbolmarkierung> symbole,
        IEnumerable<FormularEntwurf> entwürfe,
        IEnumerable<FeldKennung> zuLöschendeFelder,
        IEnumerable<SeitenAbdeckung> abdeckungen)
    {
        var bytes = NachBytesBacken(felder, platzierungen, bilder, texte, markierungen,
            symbole, entwürfe, zuLöschendeFelder, abdeckungen);
        File.WriteAllBytes(pfad, bytes);

        // Das Dokument ist nach dem Speichern nicht mehr verwendbar – frische Bytes übernehmen.
        AktuelleBytes = bytes;
        Pfad = pfad;
    }

    /// <summary>
    /// Bäckt die ausstehenden Overlays (Formularwerte, Unterschriften, Texte, Markierungen,
    /// Symbole, Entwurfsfelder, gelöschte Felder) ins PDF und gibt das Ergebnis als neue
    /// Bytes zurück – <em>ohne</em> die Datei zu schreiben, <see cref="AktuelleBytes"/> zu
    /// ändern oder die übergebenen Sammlungen zu leeren. Genutzt fürs WYSIWYG-Drucken, bei
    /// dem der aktuelle Bearbeitungsstand gedruckt, aber nicht gespeichert werden soll.
    /// </summary>
    public byte[] NachBytesBacken(IEnumerable<FormularFeld> felder,
        IEnumerable<UnterschriftPlatzierung> platzierungen,
        IEnumerable<BildEinfügung> bilder,
        IEnumerable<TextNotiz> texte,
        IEnumerable<Textmarkierung> markierungen,
        IEnumerable<Symbolmarkierung> symbole,
        IEnumerable<FormularEntwurf> entwürfe,
        IEnumerable<FeldKennung> zuLöschendeFelder,
        IEnumerable<SeitenAbdeckung> abdeckungen)
    {
        using var d = Laden();
        FormularwerteAnwenden(d, felder);
        // Vorhandene Felder entfernen, die gelöscht oder (zum Bearbeiten) durch einen
        // gleichnamigen Entwurf ersetzt werden – vor dem Backen, damit der Name frei ist.
        FelderEntfernen(d, zuLöschendeFelder);
        // Beschriftungen gelöschter Optionsfelder abdecken – vor allem anderen, damit
        // alles Weitere (neue Beschriftungen, Annotationen) darüber liegt.
        AbdeckungenZeichnen(d, abdeckungen);
        // Neu angelegte bzw. bearbeitete Formularfelder als echte AcroForm-Felder ins PDF backen.
        EntwurfsfelderErstellen(d, entwürfe);
        // Reihenfolge: Markierungen unten, darüber Bilder, Unterschriften, Texte und Symbole.
        MarkierungenZeichnen(d, markierungen);
        BilderZeichnen(d, bilder);
        UnterschriftenZeichnen(d, platzierungen);
        TexteZeichnen(d, texte);
        SymboleZeichnen(d, symbole);

        using var strom = new MemoryStream();
        d.Save(strom, closeStream: false);
        return strom.ToArray();
    }

    private static void FormularwerteAnwenden(PdfDocument d, IEnumerable<FormularFeld> felder)
    {
        var form = FormularHolen(d);
        if (form is null)
            return;

        // Felder nach vollständigem Namen auffindbar machen.
        var nachName = new Dictionary<string, PdfAcroField>();
        DurchlaufeFelder(form.Fields, string.Empty, (name, feld) => nachName[name] = feld);

        foreach (var ff in felder)
        {
            if (!nachName.TryGetValue(ff.FeldName, out var feld))
                continue;

            switch (feld)
            {
                case PdfTextField t:
                    t.Text = ff.Wert;
                    break;
                case PdfCheckBoxField c:
                    c.Checked = ff.IstAngehakt;
                    break;
                case PdfRadioButtonField:
                    OptionsAuswahlSetzen(feld, ff.Wert);
                    break;
                case PdfChoiceField:
                    feld.Value = new PdfString(ff.Wert);
                    break;
            }
        }

        // Viewer anweisen, das Erscheinungsbild der Felder neu zu erzeugen.
        form.Elements["/NeedAppearances"] = new PdfBoolean(true);
    }

    /// <summary>
    /// Setzt die Auswahl einer Optionsfeld-Gruppe (Radio): das Eltern-<c>/V</c> und den
    /// <c>/AS</c>-Zustand jedes Kind-Widgets. Viewer-unabhängig über die rohen Elemente,
    /// da PdfSharp 6.2 keinen verlässlichen Radio-Wertsetzer bietet.
    /// </summary>
    private static void OptionsAuswahlSetzen(PdfAcroField feld, string? export)
    {
        bool gewählt = !string.IsNullOrEmpty(export);
        feld.Elements["/V"] = new PdfName(gewählt ? "/" + export : "/Off");

        var kinder = feld.Elements.GetArray("/Kids");
        if (kinder is null)
            return;
        foreach (var element in kinder.Elements)
        {
            if (Auflösen(element) is not PdfDictionary kd)
                continue;
            bool trifft = gewählt && OptionExport(kd) == export;
            kd.Elements["/AS"] = new PdfName(trifft ? "/" + export : "/Off");
        }
    }

    /// <summary>
    /// Liefert das AcroForm des Dokuments oder <c>null</c>, falls keines vorhanden
    /// ist. PDFsharp wirft beim Zugriff auf <c>AcroForm</c> eine Ausnahme, wenn das
    /// Dokument kein Formular enthält – diese wird hier abgefangen.
    /// </summary>
    private static PdfAcroForm? FormularHolen(PdfDocument d)
    {
        try
        {
            return d.AcroForm;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }


    private static void UnterschriftenZeichnen(PdfDocument d,
        IEnumerable<UnterschriftPlatzierung> platzierungen)
    {
        foreach (var p in platzierungen)
        {
            if (p.SeitenIndex < 0 || p.SeitenIndex >= d.PageCount)
                continue;
            if (!File.Exists(p.Unterschrift.Pfad))
                continue;

            var seite = d.Pages[p.SeitenIndex];
            var mb = seite.MediaBox;

            using var gfx = XGraphics.FromPdfPage(seite);
            // Weißen Hintergrund der Unterschrift transparent machen, damit darunter
            // liegender PDF-Inhalt sichtbar bleibt.
            using var strom = new MemoryStream(Bildwerkzeuge.WeißTransparentPng(p.Unterschrift.Pfad));
            using var bild = XImage.FromStream(strom);

            // Umrechnung von PDF-Koordinaten (Ursprung unten links) in das
            // XGraphics-System (Ursprung oben links, ungedreht).
            double links = p.X - mb.X1;
            double oben = (mb.Y1 + mb.Height) - (p.Y + p.Höhe);
            gfx.DrawImage(bild, links, oben, p.Breite, p.Höhe);
        }
    }

    /// <summary>
    /// Zeichnet die eingefügten Bilder in die jeweilige Seite. Drehung und Zuschnitt
    /// werden vorab per <see cref="Bildwerkzeuge.TransformiertesPng"/> in die
    /// Bilddaten eingerechnet – dieselbe Pipeline wie die Bildschirmdarstellung.
    /// </summary>
    private static void BilderZeichnen(PdfDocument d, IEnumerable<BildEinfügung> bilder)
    {
        foreach (var b in bilder)
        {
            if (b.SeitenIndex < 0 || b.SeitenIndex >= d.PageCount)
                continue;

            var seite = d.Pages[b.SeitenIndex];
            var mb = seite.MediaBox;

            using var gfx = XGraphics.FromPdfPage(seite);
            using var strom = new MemoryStream(Bildwerkzeuge.TransformiertesPng(
                b.PngBytes, b.DrehungGrad,
                b.ZuschnittLinks, b.ZuschnittOben, b.ZuschnittRechts, b.ZuschnittUnten));
            using var bild = XImage.FromStream(strom);

            // Umrechnung von PDF-Koordinaten (Ursprung unten links) in das
            // XGraphics-System (Ursprung oben links, ungedreht).
            double links = b.X - mb.X1;
            double oben = (mb.Y1 + mb.Height) - (b.Y + b.Höhe);
            gfx.DrawImage(bild, links, oben, b.Breite, b.Höhe);
        }
    }

    /// <summary>Brennt die Abdeckungen gelöschter Optionsfeld-Beschriftungen in den Seiteninhalt.</summary>
    private static void AbdeckungenZeichnen(PdfDocument d, IEnumerable<SeitenAbdeckung> abdeckungen)
    {
        foreach (var s in abdeckungen)
        {
            if (s.SeitenIndex < 0 || s.SeitenIndex >= d.PageCount)
                continue;
            var seite = d.Pages[s.SeitenIndex];
            var mb = seite.MediaBox;
            using var gfx = XGraphics.FromPdfPage(seite);
            var a = s.Bereich;
            gfx.DrawRectangle(new XSolidBrush(FarbeAusHex(a.Farbe)),
                new XRect(a.X1 - mb.X1, (mb.Y1 + mb.Height) - a.Y2, a.X2 - a.X1, a.Y2 - a.Y1));
        }
    }

    private static void TexteZeichnen(PdfDocument d, IEnumerable<TextNotiz> texte)
    {
        foreach (var t in texte)
        {
            if (t.SeitenIndex < 0 || t.SeitenIndex >= d.PageCount)
                continue;
            bool hatHintergrund = !string.IsNullOrEmpty(t.Hintergrund);
            if (string.IsNullOrEmpty(t.Text) && !hatHintergrund)
                continue;

            var seite = d.Pages[t.SeitenIndex];
            var mb = seite.MediaBox;

            using var gfx = XGraphics.FromPdfPage(seite);

            double links = t.X - mb.X1;
            double oben = (mb.Y1 + mb.Height) - (t.Y + t.Höhe);
            var bereich = new XRect(links, oben, t.Breite, t.Höhe);

            // Hintergrund (z. B. zum Überdecken der eingebrannten Original-Beschriftung)
            // zuerst füllen, dann den Text darüber zeichnen.
            if (hatHintergrund)
                gfx.DrawRectangle(new XSolidBrush(FarbeAusHex(t.Hintergrund)), bereich);

            if (string.IsNullOrEmpty(t.Text))
                continue;

            // Stil aus den gewählten Auszeichnungen zusammensetzen.
            var stil = XFontStyleEx.Regular;
            if (t.Fett) stil |= XFontStyleEx.Bold;
            if (t.Kursiv) stil |= XFontStyleEx.Italic;
            if (t.Unterstrichen) stil |= XFontStyleEx.Underline;
            var schrift = new XFont("Arial", t.FontGröße, stil);
            var pinsel = new XSolidBrush(FarbeAusHex(t.Farbe));
            gfx.DrawString(t.Text, schrift, pinsel, bereich, XStringFormats.TopLeft);
        }
    }

    private static void MarkierungenZeichnen(PdfDocument d, IEnumerable<Textmarkierung> markierungen)
    {
        // Halbtransparentes Gelb, damit der Text darunter sichtbar bleibt.
        var farbe = XColor.FromArgb(90, 255, 230, 0);

        foreach (var h in markierungen)
        {
            if (h.SeitenIndex < 0 || h.SeitenIndex >= d.PageCount)
                continue;
            if (h.Punkte.Count < 2)
                continue;

            var seite = d.Pages[h.SeitenIndex];
            var mb = seite.MediaBox;

            using var gfx = XGraphics.FromPdfPage(seite);
            var stift = new XPen(farbe, h.Strichbreite)
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round
            };

            // Punkte aus PDF-Koordinaten (unten links) in das XGraphics-System
            // (oben links, ungedreht) umrechnen.
            var punkte = h.Punkte
                .Select(p => new XPoint(p.X - mb.X1, (mb.Y1 + mb.Height) - p.Y))
                .ToArray();
            gfx.DrawLines(stift, punkte);
        }
    }

    /// <summary>
    /// Zeichnet die Symbolmarkierungen (Kreuz, Häkchen, Punkt, Umrandung,
    /// Durchstreichung) als Vektorform in die jeweilige Seite. Die relativen
    /// Formeln entsprechen exakt der Bildschirmdarstellung
    /// (<c>MainWindow.SymbolGeometrie</c>), damit gespeicherte und angezeigte Form
    /// übereinstimmen.
    /// </summary>
    private static void SymboleZeichnen(PdfDocument d, IEnumerable<Symbolmarkierung> symbole)
    {
        foreach (var s in symbole)
        {
            if (s.SeitenIndex < 0 || s.SeitenIndex >= d.PageCount)
                continue;

            var seite = d.Pages[s.SeitenIndex];
            var mb = seite.MediaBox;

            using var gfx = XGraphics.FromPdfPage(seite);
            var farbe = FarbeAusHex(s.Farbe);
            var stift = new XPen(farbe, s.Strichbreite)
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round
            };

            // Umrechnung von PDF-Koordinaten (Ursprung unten links) in das
            // XGraphics-System (Ursprung oben links, ungedreht).
            double links = s.X - mb.X1;
            double oben = (mb.Y1 + mb.Height) - (s.Y + s.Höhe);
            double w = s.Breite, h = s.Höhe;
            double pad = Math.Min(s.Strichbreite, Math.Min(w, h) / 2);
            double iw = Math.Max(0, w - 2 * pad);
            double ih = Math.Max(0, h - 2 * pad);

            switch (s.Art)
            {
                case SymbolArt.Häkchen:
                    gfx.DrawLines(stift, new[]
                    {
                        new XPoint(links + pad, oben + pad + ih * 0.55),
                        new XPoint(links + pad + iw * 0.4, oben + h - pad),
                        new XPoint(links + w - pad, oben + pad)
                    });
                    break;
                case SymbolArt.Punkt:
                    gfx.DrawEllipse(new XSolidBrush(farbe), links + pad, oben + pad, iw, ih);
                    break;
                case SymbolArt.Umranden:
                {
                    double radius = Math.Min(iw, ih) * 0.3;
                    gfx.DrawRoundedRectangle(stift, links + pad, oben + pad, iw, ih, radius, radius);
                    break;
                }
                case SymbolArt.Durchstreichen:
                    gfx.DrawLine(stift, links + pad, oben + h / 2, links + w - pad, oben + h / 2);
                    break;
                default: // Kreuz
                    gfx.DrawLine(stift, links + pad, oben + pad, links + w - pad, oben + h - pad);
                    gfx.DrawLine(stift, links + w - pad, oben + pad, links + pad, oben + h - pad);
                    break;
            }
        }
    }

    /// <summary>Wandelt einen Hex-Farbwert (#RRGGBB) in eine XColor um (Schwarz als Rückfall).</summary>
    private static XColor FarbeAusHex(string? hex)
    {
        string h = (hex ?? string.Empty).TrimStart('#');
        if (h.Length == 6
            && byte.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return XColor.FromArgb(r, g, b);
        return XColors.Black;
    }

}
