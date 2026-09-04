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

/// <summary>Formular-Designer: Erzeugt AcroForm-Felder aus Entwürfen (rohe PdfDictionary-Objekte).</summary>
public partial class PdfDokumentDienst
{
    // ----- Formularfelder erstellen (Formular-Designer) --------------------

    /// <summary>
    /// Backt die im Designer angelegten Entwurfsfelder als echte AcroForm-Felder in
    /// das Dokument. PdfSharp 6.2 besitzt keine Fluent-Erstell-API, daher werden die
    /// Feld-/Widget-Dictionaries direkt aufgebaut (vgl. <see cref="KommentarHinzufügen"/>)
    /// und in das AcroForm sowie die <c>/Annots</c> der Seite eingehängt.
    /// </summary>
    private static void EntwurfsfelderErstellen(PdfDocument d, IEnumerable<FormularEntwurf> entwürfe)
    {
        var liste = entwürfe as ICollection<FormularEntwurf> ?? entwürfe.ToList();
        if (liste.Count == 0)
            return;

        var acro = FormularSicherstellen(d);
        var felder = acro.Elements.GetArray("/Fields")!;

        // Bereits im Dokument vergebene Feldnamen sammeln. Zwei Felder mit gleichem /T
        // gelten in PDF als ein gemeinsames Feld (geteilter Wert); deshalb wird jeder
        // Entwurfsname dagegen – und gegen die in diesem Durchlauf vergebenen – eindeutig
        // gemacht. Die UI vergibt zwar bereits eindeutige Namen, doch hier wird es zur
        // letzten verbindlichen Instanz, auch bei manuell gesetzten Namen.
        var belegt = VorhandeneFeldnamen(felder);

        foreach (var e in liste)
        {
            if (e.SeitenIndex < 0 || e.SeitenIndex >= d.PageCount)
                continue;
            var seite = d.Pages[e.SeitenIndex];

            string name = EindeutigerName(FeldNameSicher(e), belegt);
            belegt.Add(name);

            if (e.Typ == EntwurfFeldTyp.Optionsfeld)
                OptionsfeldErstellen(d, felder, seite, e, name);
            else
                EinfachesFeldErstellen(d, felder, seite, e, name);
        }
    }

    /// <summary>Liest die <c>/T</c>-Namen der bereits in der AcroForm eingetragenen Felder.</summary>
    private static HashSet<string> VorhandeneFeldnamen(PdfArray felder)
    {
        var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in felder.Elements)
        {
            var dict = (item as PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary;
            string? t = dict?.Elements.GetString("/T");
            if (!string.IsNullOrEmpty(t))
                namen.Add(t);
        }
        return namen;
    }

    /// <summary>
    /// Macht <paramref name="name"/> gegen <paramref name="belegt"/> eindeutig, indem bei
    /// einer Kollision <c>_2</c>, <c>_3</c> … angehängt wird.
    /// </summary>
    private static string EindeutigerName(string name, HashSet<string> belegt)
    {
        if (!belegt.Contains(name))
            return name;
        for (int i = 2; ; i++)
        {
            string kandidat = $"{name}_{i}";
            if (!belegt.Contains(kandidat))
                return kandidat;
        }
    }

    /// <summary>Liefert das AcroForm-Dictionary und legt es bei Bedarf an (samt /DR und /DA).</summary>
    private static PdfDictionary FormularSicherstellen(PdfDocument d)
    {
        var catalog = d.Internals.Catalog;
        PdfDictionary acro;
        if (catalog.Elements.GetDictionary("/AcroForm") is { } vorhanden)
        {
            acro = vorhanden;
        }
        else
        {
            acro = new PdfDictionary(d);
            d.Internals.AddObject(acro);
            catalog.Elements["/AcroForm"] = acro.Reference!;
        }

        if (acro.Elements.GetArray("/Fields") is null)
            acro.Elements["/Fields"] = new PdfArray(d);
        // Betrachter anweisen, das Erscheinungsbild der Felder selbst zu erzeugen.
        acro.Elements["/NeedAppearances"] = new PdfBoolean(true);

        if (!acro.Elements.ContainsKey("/DA"))
            acro.Elements["/DA"] = new PdfString("/Helv 0 Tf 0 g");

        var dr = acro.Elements.GetDictionary("/DR");
        if (dr is null)
        {
            dr = new PdfDictionary(d);
            acro.Elements["/DR"] = dr;
        }
        var fonts = dr.Elements.GetDictionary("/Font");
        if (fonts is null)
        {
            fonts = new PdfDictionary(d);
            dr.Elements["/Font"] = fonts;
        }
        if (!fonts.Elements.ContainsKey("/Helv"))
        {
            var helv = new PdfDictionary(d);
            helv.Elements["/Type"] = new PdfName("/Font");
            helv.Elements["/Subtype"] = new PdfName("/Type1");
            helv.Elements["/BaseFont"] = new PdfName("/Helvetica");
            helv.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
            d.Internals.AddObject(helv);
            fonts.Elements["/Helv"] = helv.Reference!;
        }

        return acro;
    }

    /// <summary>Erstellt ein Feld mit genau einem Widget (Text, Kästchen, Auswahl, Unterschrift).</summary>
    private static void EinfachesFeldErstellen(PdfDocument d, PdfArray felder, PdfPage seite, FormularEntwurf e, string name)
    {
        var feld = new PdfDictionary(d);
        feld.Elements["/Type"] = new PdfName("/Annot");
        feld.Elements["/Subtype"] = new PdfName("/Widget");
        feld.Elements["/FT"] = new PdfName(FeldTypName(e.Typ));
        feld.Elements["/T"] = new PdfString(name, PdfStringEncoding.Unicode);
        if (!string.IsNullOrEmpty(e.QuickInfo))
            feld.Elements["/TU"] = new PdfString(e.QuickInfo, PdfStringEncoding.Unicode);
        feld.Elements["/Rect"] = ZahlenFeld(d, e.X, e.Y, e.X + e.Breite, e.Y + e.Höhe);
        feld.Elements["/P"] = seite.Reference!;
        feld.Elements["/F"] = new PdfInteger(4); // druckbar
        feld.Elements["/DA"] = new PdfString(DefaultErscheinung(e));
        if (e.Ausrichtung != TextAusrichtung.Links)
            feld.Elements["/Q"] = new PdfInteger((int)e.Ausrichtung);

        int flags = FeldFlags(e);
        if (flags != 0)
            feld.Elements["/Ff"] = new PdfInteger(flags);

        ErscheinungsmerkmaleSetzen(d, feld, e);

        switch (e.Typ)
        {
            case EntwurfFeldTyp.Kontrollkästchen:
            {
                bool an = IstWahr(e.Standardwert);
                feld.Elements["/V"] = new PdfName(an ? "/Ja" : "/Off");
                feld.Elements["/DV"] = new PdfName(an ? "/Ja" : "/Off");
                feld.Elements["/AS"] = new PdfName(an ? "/Ja" : "/Off");
                KästchenErscheinung(d, feld, e, e.Breite, e.Höhe, "Ja");
                break;
            }
            case EntwurfFeldTyp.Dropdown:
            case EntwurfFeldTyp.Listenfeld:
            {
                feld.Elements["/Opt"] = OptionenArray(d, e.Optionen);
                if (!string.IsNullOrEmpty(e.Standardwert))
                {
                    feld.Elements["/V"] = new PdfString(e.Standardwert, PdfStringEncoding.Unicode);
                    WertErscheinung(d, feld, e, e.Breite, e.Höhe, e.Standardwert, mehrzeilig: false);
                }
                break;
            }
            case EntwurfFeldTyp.Unterschrift:
                // Leeres Signaturfeld – keine Vorbelegung.
                break;
            default: // Textfeld, MehrzeiligesTextfeld, Datum, Zahl
            {
                if (e.MaxZeichen is int max && max > 0)
                    feld.Elements["/MaxLen"] = new PdfInteger(max);
                if (!string.IsNullOrEmpty(e.Standardwert))
                {
                    feld.Elements["/V"] = new PdfString(e.Standardwert, PdfStringEncoding.Unicode);
                    feld.Elements["/DV"] = new PdfString(e.Standardwert, PdfStringEncoding.Unicode);
                    WertErscheinung(d, feld, e, e.Breite, e.Höhe, e.Standardwert,
                        mehrzeilig: e.Typ == EntwurfFeldTyp.MehrzeiligesTextfeld);
                }
                FormatAktionSetzen(d, feld, e);
                break;
            }
        }

        d.Internals.AddObject(feld);
        felder.Elements.Add(feld.Reference!);
        AnnotationAnhängen(seite, feld);
    }

    /// <summary>
    /// Erstellt eine Optionsfeld-Gruppe (Radio): ein Elternfeld mit gemeinsamem Namen und
    /// je Option ein Widget-Kind, vertikal im gezeichneten Rechteck gestapelt. Die
    /// Beschriftungen werden als statischer Text neben die Schaltflächen gezeichnet.
    /// </summary>
    private static void OptionsfeldErstellen(PdfDocument d, PdfArray felder, PdfPage seite, FormularEntwurf e, string name)
    {
        var optionen = e.Optionen.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
        if (optionen.Count == 0)
            optionen = new List<string> { "Option 1", "Option 2" };

        var eltern = new PdfDictionary(d);
        eltern.Elements["/FT"] = new PdfName("/Btn");
        eltern.Elements["/T"] = new PdfString(name, PdfStringEncoding.Unicode);
        if (!string.IsNullOrEmpty(e.QuickInfo))
            eltern.Elements["/TU"] = new PdfString(e.QuickInfo, PdfStringEncoding.Unicode);
        int flags = (1 << 15) | (1 << 14); // Radio | NoToggleToOff
        if (e.Pflichtfeld) flags |= 1 << 1;
        if (e.Schreibgeschützt) flags |= 1 << 0;
        eltern.Elements["/Ff"] = new PdfInteger(flags);
        // ZapfDingbats: Betrachter, die das Erscheinungsbild neu aufbauen, zeichnen damit den Punkt.
        eltern.Elements["/DA"] = new PdfString("/ZaDb 0 Tf 0 g");
        StandardSchrift(d, "/ZaDb"); // Schrift im /DR registrieren

        string? gewählt = optionen.FirstOrDefault(o => o == e.Standardwert);
        if (gewählt is not null)
            eltern.Elements["/V"] = new PdfName("/" + ExportName(gewählt));

        d.Internals.AddObject(eltern);
        felder.Elements.Add(eltern.Reference!);

        var mb = seite.MediaBox;
        using var gfx = XGraphics.FromPdfPage(seite);
        var schrift = new XFont("Arial", Math.Clamp(e.Höhe / optionen.Count * 0.6, 7, 12));

        // Bei bearbeiteten Feldern zuerst die eingebrannten Original-Beschriftungen mit
        // Rechtecken in der abgetasteten Hintergrundfarbe abdecken – die (ggf. geänderten)
        // Beschriftungen werden danach neben den neuen Schaltflächen frisch gezeichnet.
        foreach (var a in e.Abdeckungen)
        {
            if (a is null)
                continue;
            gfx.DrawRectangle(new XSolidBrush(FarbeAusHex(a.Farbe)),
                new XRect(a.X1 - mb.X1, (mb.Y1 + mb.Height) - a.Y2, a.X2 - a.X1, a.Y2 - a.Y1));
        }

        var kinder = new PdfArray(d);
        double zeilenHöhe = e.Höhe / optionen.Count;
        double größe = Math.Min(zeilenHöhe * 0.8, 16);
        for (int i = 0; i < optionen.Count; i++)
        {
            string export = ExportName(optionen[i]);
            double y2 = e.Y + e.Höhe - i * zeilenHöhe;
            double y1 = y2 - größe;

            var kind = new PdfDictionary(d);
            kind.Elements["/Type"] = new PdfName("/Annot");
            kind.Elements["/Subtype"] = new PdfName("/Widget");
            kind.Elements["/Parent"] = eltern.Reference!;
            kind.Elements["/Rect"] = ZahlenFeld(d, e.X, y1, e.X + größe, y2);
            kind.Elements["/P"] = seite.Reference!;
            kind.Elements["/F"] = new PdfInteger(4);
            bool an = optionen[i] == e.Standardwert;
            kind.Elements["/AS"] = new PdfName(an ? "/" + export : "/Off");
            ErscheinungsmerkmaleSetzen(d, kind, e);
            OptionErscheinung(d, kind, e, größe, größe, export);
            d.Internals.AddObject(kind);
            AnnotationAnhängen(seite, kind);
            kinder.Elements.Add(kind.Reference!);

            // Beschriftung rechts neben die Schaltfläche (in XGraphics-Koordinaten).
            // Ausgenommen sind Optionen eines bearbeiteten Feldes, deren Original-
            // Beschriftung nicht erkannt (und damit nicht abgedeckt) wurde – dort
            // bliebe das Original stehen und eine neue daneben wäre doppelt.
            if (e.BeschriftungZeichnen(i))
            {
                double links = (e.X + größe + 4) - mb.X1;
                double oben = (mb.Y1 + mb.Height) - y2;
                gfx.DrawString(optionen[i], schrift, XBrushes.Black,
                    new XRect(links, oben, Math.Max(0, e.Breite - größe - 4), größe), XStringFormats.CenterLeft);
            }
        }
        eltern.Elements["/Kids"] = kinder;
    }

    private static string FeldTypName(EntwurfFeldTyp t) => t switch
    {
        EntwurfFeldTyp.Kontrollkästchen => "/Btn",
        EntwurfFeldTyp.Optionsfeld => "/Btn",
        EntwurfFeldTyp.Dropdown => "/Ch",
        EntwurfFeldTyp.Listenfeld => "/Ch",
        EntwurfFeldTyp.Unterschrift => "/Sig",
        _ => "/Tx" // Textfeld, MehrzeiligesTextfeld, Datum, Zahl
    };

    /// <summary>Setzt die Feld-Flags (/Ff) aus den Eigenschaften des Entwurfs zusammen.</summary>
    private static int FeldFlags(FormularEntwurf e)
    {
        int f = 0;
        if (e.Schreibgeschützt) f |= 1 << 0;  // ReadOnly
        if (e.Pflichtfeld) f |= 1 << 1;        // Required

        switch (e.Typ)
        {
            case EntwurfFeldTyp.MehrzeiligesTextfeld:
                f |= 1 << 12; // Multiline
                break;
            case EntwurfFeldTyp.Dropdown:
                f |= 1 << 17; // Combo
                f |= 1 << 18; // Edit (frei editierbar)
                break;
            case EntwurfFeldTyp.Listenfeld:
                if (e.MehrfachAuswahl) f |= 1 << 21; // MultiSelect
                break;
            case EntwurfFeldTyp.Textfeld:
            case EntwurfFeldTyp.Datum:
            case EntwurfFeldTyp.Zahl:
                if (e.AlsKästchen && e.MaxZeichen is > 0) f |= 1 << 24; // Comb
                break;
        }
        return f;
    }

    /// <summary>Erzeugt die Default-Erscheinung (/DA): Schrift, Größe und Textfarbe.</summary>
    private static string DefaultErscheinung(FormularEntwurf e)
    {
        var (r, g, b) = RgbAusHex(e.TextFarbe) ?? (0, 0, 0);
        double größe = e.Schriftgröße > 0 ? e.Schriftgröße : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"/Helv {größe:0.##} Tf {r:0.###} {g:0.###} {b:0.###} rg");
    }

    /// <summary>Setzt Rahmen- und Hintergrundfarbe (/MK) sowie die Rahmenbreite (/BS).</summary>
    private static void ErscheinungsmerkmaleSetzen(PdfDocument d, PdfDictionary widget, FormularEntwurf e)
    {
        var rahmen = RgbAusHex(e.RahmenFarbe);
        var hintergrund = RgbAusHex(e.HintergrundFarbe);
        if (rahmen is null && hintergrund is null)
            return;

        var mk = new PdfDictionary(d);
        if (rahmen is { } rc)
            mk.Elements["/BC"] = ZahlenFeld(d, rc.R, rc.G, rc.B);
        if (hintergrund is { } bg)
            mk.Elements["/BG"] = ZahlenFeld(d, bg.R, bg.G, bg.B);
        widget.Elements["/MK"] = mk;

        if (rahmen is not null)
        {
            var bs = new PdfDictionary(d);
            bs.Elements["/W"] = new PdfInteger(1);
            bs.Elements["/S"] = new PdfName("/S"); // durchgezogen
            widget.Elements["/BS"] = bs;
        }
    }

    /// <summary>
    /// Hinterlegt Format-/Prüf-JavaScript (/AA) für Datum-, Zahlen- und Formatfelder.
    /// Die Auswertung übernimmt der Betrachter (Acrobat/Reader); andere Betrachter
    /// ignorieren es, ohne dass das Feld unbrauchbar wird.
    /// </summary>
    private static void FormatAktionSetzen(PdfDocument d, PdfDictionary feld, FormularEntwurf e)
    {
        string? format = null, tastendruck = null, prüfung = null;

        switch (e.Typ)
        {
            case EntwurfFeldTyp.Datum:
                format = "AFDate_FormatEx(\"dd.mm.yyyy\");";
                tastendruck = "AFDate_KeystrokeEx(\"dd.mm.yyyy\");";
                break;
            case EntwurfFeldTyp.Zahl:
                format = "AFNumber_Format(2, 0, 0, 0, \"\", true);";
                tastendruck = "AFNumber_Keystroke(2, 0, 0, 0, \"\", true);";
                break;
        }

        prüfung = e.Format switch
        {
            FeldFormat.Email => PrüfSkript(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", "Bitte eine gültige E-Mail-Adresse eingeben."),
            FeldFormat.PLZ => PrüfSkript(@"^\d{5}$", "Bitte eine fünfstellige Postleitzahl eingeben."),
            FeldFormat.Telefon => PrüfSkript(@"^[\d\s/+()\-]{3,}$", "Bitte eine gültige Telefonnummer eingeben."),
            _ => null
        };

        if (format is null && tastendruck is null && prüfung is null)
            return;

        var aa = new PdfDictionary(d);
        if (format is not null) aa.Elements["/F"] = JavaScriptAktion(d, format);
        if (tastendruck is not null) aa.Elements["/K"] = JavaScriptAktion(d, tastendruck);
        if (prüfung is not null) aa.Elements["/V"] = JavaScriptAktion(d, prüfung);
        feld.Elements["/AA"] = aa;
    }

    private static string PrüfSkript(string regex, string meldung) =>
        $"if(event.value && !(/{regex}/).test(event.value)){{app.alert(\"{meldung}\");event.rc=false;}}";

    private static PdfDictionary JavaScriptAktion(PdfDocument d, string js)
    {
        var a = new PdfDictionary(d);
        a.Elements["/Type"] = new PdfName("/Action");
        a.Elements["/S"] = new PdfName("/JavaScript");
        a.Elements["/JS"] = new PdfString(js, PdfStringEncoding.Unicode);
        return a;
    }

    private static PdfArray OptionenArray(PdfDocument d, IEnumerable<string> optionen)
    {
        var arr = new PdfArray(d);
        foreach (var o in optionen.Where(o => !string.IsNullOrWhiteSpace(o)))
            arr.Elements.Add(new PdfString(o, PdfStringEncoding.Unicode));
        return arr;
    }

    /// <summary>Liefert einen nicht-leeren Feldnamen; fehlt einer, wird ein eindeutiger erzeugt.</summary>
    private static string FeldNameSicher(FormularEntwurf e)
    {
        string name = (e.FeldName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(name))
            return name;
        return $"{e.Typ}_{Guid.NewGuid():N}"[..^26]; // Typname + 6 Hex-Zeichen
    }

    /// <summary>Wandelt einen Anzeigetext in einen gültigen PDF-Namenstoken (Exportwert).</summary>
    private static string ExportName(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string r = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(r) ? "Opt" : r;
    }

    private static bool IstWahr(string? s) =>
        s is not null && (s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || s.Equals("1", StringComparison.Ordinal)
            || s.Equals("x", StringComparison.OrdinalIgnoreCase));

    /// <summary>Wandelt #RRGGBB in normierte RGB-Anteile (0..1) um; null bei leer/ungültig.</summary>
    private static (double R, double G, double B)? RgbAusHex(string? hex)
    {
        string h = (hex ?? string.Empty).TrimStart('#');
        if (h.Length == 6
            && byte.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return (r / 255.0, g / 255.0, b / 255.0);
        return null;
    }

}
