using System.Globalization;
using System.Text;
using PdfEditor.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfEditor.Services;

/// <summary>
/// Erzeugt Erscheinungsbilder (<c>/AP</c>) für die im Designer angelegten Felder.
/// PDFsharp generiert Appearance-Streams nur für Textfelder selbst; Schaltflächen
/// (Kontrollkästchen/Optionsfeld) und Auswahllisten bleiben ohne <c>/AP</c> und werden
/// dann je nach Betrachter falsch dargestellt (Adobe zeigt sie z. B. als Dropdown).
/// Daher werden hier vollständige Form-XObjects als Erscheinungsbild hinterlegt –
/// zusätzlich zu <c>/MK /CA</c> und <c>/DA</c>, damit auch Betrachter, die wegen
/// <c>/NeedAppearances</c> neu rendern, das Feld korrekt aufbauen können.
/// </summary>
public partial class PdfDokumentDienst
{
    // ZapfDingbats: 0x34 ('4') = Häkchen, 0x6C ('l') = ausgefüllter Kreis.
    private const char Häkchen = '4';
    private const char Punkt = 'l';

    /// <summary>Hinterlegt das An/Aus-Erscheinungsbild eines Kontrollkästchens.</summary>
    private static void KästchenErscheinung(PdfDocument d, PdfDictionary widget,
        FormularEntwurf e, double w, double h, string anName)
        => SchaltflächeErscheinung(d, widget, e, w, h, anName, Häkchen);

    /// <summary>Hinterlegt das An/Aus-Erscheinungsbild eines Optionsfeld-Widgets.</summary>
    private static void OptionErscheinung(PdfDocument d, PdfDictionary widget,
        FormularEntwurf e, double w, double h, string anName)
        => SchaltflächeErscheinung(d, widget, e, w, h, anName, Punkt);

    /// <summary>
    /// Baut <c>/AP /N &lt;&lt; /anName ... /Off ... &gt;&gt;</c> für eine Schaltfläche samt
    /// <c>/MK /CA</c> (Beschriftungszeichen) und <c>/DA</c> auf ZapfDingbats.
    /// </summary>
    private static void SchaltflächeErscheinung(PdfDocument d, PdfDictionary widget,
        FormularEntwurf e, double w, double h, string anName, char zeichen)
    {
        var zaDb = StandardSchrift(d, "/ZaDb");
        string an = RahmenUndHintergrund(e, w, h, rahmenErzwingen: true) + ZeichenInhalt(w, h, zeichen);
        string aus = RahmenUndHintergrund(e, w, h, rahmenErzwingen: true);

        var n = new PdfDictionary(d);
        n.Elements["/" + anName] = FormXObjekt(d, w, h, "/ZaDb", zaDb, Latin1(an));
        n.Elements["/Off"] = FormXObjekt(d, w, h, "/ZaDb", zaDb, Latin1(aus));

        var ap = new PdfDictionary(d);
        ap.Elements["/N"] = n;
        widget.Elements["/AP"] = ap;

        var mk = widget.Elements.GetDictionary("/MK") ?? NeuesMk(d, widget);
        mk.Elements["/CA"] = new PdfString(zeichen.ToString());
        widget.Elements["/DA"] = new PdfString("/ZaDb 0 Tf 0 g");
    }

    /// <summary>
    /// Hinterlegt ein Erscheinungsbild für den Wert eines Text-/Auswahlfeldes, damit der
    /// vorbelegte Wert in jedem Betrachter sauber (richtige Größe, beschnitten) erscheint.
    /// </summary>
    private static void WertErscheinung(PdfDocument d, PdfDictionary widget,
        FormularEntwurf e, double w, double h, string wert, bool mehrzeilig)
    {
        if (string.IsNullOrEmpty(wert))
            return;

        var helv = StandardSchrift(d, "/Helv");
        var (r, g, b) = RgbAusHex(e.TextFarbe) ?? (0, 0, 0);
        double größe = e.Schriftgröße > 0 ? e.Schriftgröße : Math.Clamp(h - 4, 6, 12);
        const double rand = 2;

        var sb = new StringBuilder();
        sb.Append(RahmenUndHintergrund(e, w, h, rahmenErzwingen: false));
        sb.Append("/Tx BMC q ");
        sb.Append($"{Z(rand)} {Z(rand)} {Z(Math.Max(0, w - 2 * rand))} {Z(Math.Max(0, h - 2 * rand))} re W n ");
        sb.Append($"BT /Helv {Z(größe)} Tf {Z(r)} {Z(g)} {Z(b)} rg ");

        if (mehrzeilig)
        {
            var zeilen = wert.Replace("\r", "").Split('\n');
            double durchschuss = größe * 1.15;
            sb.Append($"{Z(durchschuss)} TL {Z(rand)} {Z(h - rand - größe)} Td ");
            sb.Append($"({LiteralEscape(zeilen[0])}) Tj");
            for (int i = 1; i < zeilen.Length; i++)
                sb.Append($" ({LiteralEscape(zeilen[i])}) '");
            sb.Append(" ET Q EMC");
        }
        else
        {
            string text = wert.Replace("\r", " ").Replace("\n", " ");
            double breite = text.Length * größe * 0.5;
            double tx = e.Ausrichtung switch
            {
                TextAusrichtung.Mitte => Math.Max(rand, (w - breite) / 2),
                TextAusrichtung.Rechts => Math.Max(rand, w - rand - breite),
                _ => rand
            };
            double ty = (h - größe) / 2 + größe * 0.25;
            sb.Append($"{Z(tx)} {Z(ty)} Td ({LiteralEscape(text)}) Tj ET Q EMC");
        }

        var ap = new PdfDictionary(d);
        ap.Elements["/N"] = FormXObjekt(d, w, h, "/Helv", helv, Latin1(sb.ToString()));
        widget.Elements["/AP"] = ap;
    }

    // ----- Bausteine -------------------------------------------------------

    /// <summary>Zeichnet (zentriert) ein ZapfDingbats-Zeichen in das Feldrechteck.</summary>
    private static string ZeichenInhalt(double w, double h, char zeichen)
    {
        double größe = Math.Min(w, h) * 0.8;
        double tx = (w - größe * 0.7) / 2;
        double ty = (h - größe) / 2 + größe * 0.18;
        return $"q 0 g BT /ZaDb {Z(größe)} Tf {Z(tx)} {Z(ty)} Td ({zeichen}) Tj ET Q\n";
    }

    /// <summary>Liefert Inhalt für Hintergrundfüllung und Rahmen aus den Feldfarben.</summary>
    private static string RahmenUndHintergrund(FormularEntwurf e, double w, double h, bool rahmenErzwingen)
    {
        var sb = new StringBuilder();
        if (RgbAusHex(e.HintergrundFarbe) is { } bg)
            sb.Append($"{Z(bg.R)} {Z(bg.G)} {Z(bg.B)} rg 0 0 {Z(w)} {Z(h)} re f\n");

        var rahmen = RgbAusHex(e.RahmenFarbe);
        if (rahmen is { } rc || rahmenErzwingen)
        {
            var (r, g, b) = rahmen ?? (0.4, 0.4, 0.4);
            const double lw = 1;
            sb.Append($"{Z(r)} {Z(g)} {Z(b)} RG {Z(lw)} w ");
            sb.Append($"{Z(lw / 2)} {Z(lw / 2)} {Z(w - lw)} {Z(h - lw)} re S\n");
        }
        return sb.ToString();
    }

    /// <summary>Erzeugt ein Form-XObject (Appearance-Stream) und gibt seine Referenz zurück.</summary>
    private static PdfReference FormXObjekt(PdfDocument d, double w, double h,
        string schriftName, PdfReference schrift, byte[] inhalt)
    {
        var xobj = new PdfDictionary(d);
        xobj.Elements["/Type"] = new PdfName("/XObject");
        xobj.Elements["/Subtype"] = new PdfName("/Form");
        xobj.Elements["/FormType"] = new PdfInteger(1);
        xobj.Elements["/BBox"] = ZahlenFeld(d, 0, 0, w, h);

        var fonts = new PdfDictionary(d);
        fonts.Elements[schriftName] = schrift;
        var res = new PdfDictionary(d);
        res.Elements["/Font"] = fonts;
        xobj.Elements["/Resources"] = res;

        xobj.CreateStream(inhalt);
        d.Internals.AddObject(xobj);
        return xobj.Reference!;
    }

    /// <summary>Get-or-create einer Standard-14-Schrift im <c>/DR /Font</c> des Formulars.</summary>
    private static PdfReference StandardSchrift(PdfDocument d, string name)
    {
        var acro = d.Internals.Catalog.Elements.GetDictionary("/AcroForm")!;
        var dr = acro.Elements.GetDictionary("/DR")!;
        var fonts = dr.Elements.GetDictionary("/Font")!;
        if (fonts.Elements.GetReference(name) is { } vorhanden)
            return vorhanden;

        var f = new PdfDictionary(d);
        f.Elements["/Type"] = new PdfName("/Font");
        f.Elements["/Subtype"] = new PdfName("/Type1");
        f.Elements["/BaseFont"] = new PdfName(name == "/ZaDb" ? "/ZapfDingbats" : "/Helvetica");
        if (name != "/ZaDb")
            f.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        d.Internals.AddObject(f);
        fonts.Elements[name] = f.Reference!;
        return f.Reference!;
    }

    private static PdfDictionary NeuesMk(PdfDocument d, PdfDictionary widget)
    {
        var mk = new PdfDictionary(d);
        widget.Elements["/MK"] = mk;
        return mk;
    }

    // ----- Kodierung -------------------------------------------------------

    private static string Z(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Maskiert die Sonderzeichen eines PDF-Literalstrings <c>(...)</c>.</summary>
    private static string LiteralEscape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("(", "\\(")
        .Replace(")", "\\)");

    /// <summary>
    /// Kodiert einen Inhaltsstrom als Bytes. Die ASCII-Operatoren und der über
    /// WinAnsi/Latin-1 darstellbare Text (inkl. deutscher Umlaute) werden 1:1 abgebildet;
    /// nicht darstellbare Zeichen werden zu '?'.
    /// </summary>
    private static byte[] Latin1(string s)
    {
        var b = new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
            b[i] = s[i] <= 0xFF ? (byte)s[i] : (byte)'?';
        return b;
    }
}
