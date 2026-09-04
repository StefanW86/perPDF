using System.Globalization;
using PdfEditor.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;

namespace PdfEditor.Services;

/// <summary>
/// Bearbeiten und Löschen bereits vorhandener AcroForm-Felder. Ein vorhandenes Feld
/// wird zum Bearbeiten originaltreu in einen <see cref="FormularEntwurf"/> übersetzt
/// (alle vom Designer abgebildeten Eigenschaften werden übernommen); beim Speichern
/// entfernt <see cref="FelderEntfernen"/> das Originalfeld, und der Entwurf wird unter
/// demselben Namen neu gebacken. Reines Löschen entfernt das Feld nur.
/// </summary>
public partial class PdfDokumentDienst
{
    // ----- Vorhandenes Feld originaltreu in einen Entwurf übersetzen -------

    /// <summary>
    /// Liest das vorhandene Formularfeld mit dem Namen von <paramref name="feld"/> aus dem
    /// Dokument und baut daraus einen vollständig konfigurierten <see cref="FormularEntwurf"/>:
    /// Typ, Beschriftungen, Flags (Pflicht/Schreibschutz/Mehrzeilig/Comb/Mehrfachauswahl),
    /// maximale Zeichen, Ausrichtung, Schriftgröße/-farbe, Rahmen-/Hintergrundfarbe und
    /// Optionen. Die Geometrie wird von <paramref name="feld"/> übernommen (bereits in
    /// PDF-Punkten). Gibt <c>null</c> zurück, wenn das Feld nicht gefunden wird oder sein
    /// Typ nicht im Designer abbildbar ist.
    /// </summary>
    public FormularEntwurf? FeldZuEntwurf(FormularFeld feld)
    {
        if (!HatDokument)
            return null;

        using var d = Laden();
        var form = FormularHolen(d);
        if (form is null)
            return null;

        // Feld über Name UND Geometrie finden – der Name allein ist nicht eindeutig
        // (manche PDFs vergeben ihn mehrfach, z. B. Optionsfeld und Textfeld „Feld1").
        var gesucht = FeldKennung.Von(feld);
        PdfAcroField? treffer = null;
        DurchlaufeFelder(form.Fields, string.Empty, (vollerName, f) =>
        {
            if (treffer is null && KennungBerechnen(vollerName, f) == gesucht)
                treffer = f;
        });
        if (treffer is null)
            return null;

        var typ = EntwurfTypBestimmen(treffer);
        if (typ is null)
            return null;

        int ff = treffer.Elements.GetInteger("/Ff"); // 0, falls nicht vorhanden

        var entwurf = new FormularEntwurf(typ.Value, feld.SeitenIndex)
        {
            FeldName = feld.FeldName,
            X = feld.X1,
            Y = feld.Y1,
            Breite = feld.X2 - feld.X1,
            Höhe = feld.Y2 - feld.Y1,
            QuickInfo = treffer.Elements.GetString("/TU") ?? string.Empty,
            Pflichtfeld = (ff & (1 << 1)) != 0,
            Schreibgeschützt = (ff & (1 << 0)) != 0,
            Ausrichtung = (TextAusrichtung)Math.Clamp(treffer.Elements.GetInteger("/Q"), 0, 2),
            Standardwert = typ is EntwurfFeldTyp.Kontrollkästchen
                ? (feld.IstAngehakt ? "ja" : string.Empty)
                : feld.Wert
        };

        // Erscheinung (/DA): Schriftgröße und Textfarbe; bei leerem Feld-/DA das des Formulars.
        string da = treffer.Elements.GetString("/DA");
        if (string.IsNullOrEmpty(da))
            da = form.Elements.GetString("/DA");
        var (größe, textfarbe) = ErscheinungLesen(da);
        if (größe is double g) entwurf.Schriftgröße = g;
        if (textfarbe is string tf) entwurf.TextFarbe = tf;

        // Rahmen- und Hintergrundfarbe (/MK /BC, /MK /BG).
        var mk = treffer.Elements.GetDictionary("/MK");
        if (mk is not null)
        {
            entwurf.RahmenFarbe = FarbeAusArray(mk.Elements.GetArray("/BC")) ?? string.Empty;
            entwurf.HintergrundFarbe = FarbeAusArray(mk.Elements.GetArray("/BG")) ?? string.Empty;
        }
        else
        {
            // Kein Rahmen-/Hintergrundeintrag: keine Rahmenfarbe annehmen (sonst entstünde
            // beim Neubacken ein Rahmen, den das Original nicht hatte).
            entwurf.RahmenFarbe = string.Empty;
        }

        // Typabhängige Eigenschaften.
        switch (typ.Value)
        {
            case EntwurfFeldTyp.Textfeld:
            case EntwurfFeldTyp.MehrzeiligesTextfeld:
            case EntwurfFeldTyp.Datum:
            case EntwurfFeldTyp.Zahl:
            {
                int max = treffer.Elements.GetInteger("/MaxLen");
                if (max > 0) entwurf.MaxZeichen = max;
                entwurf.AlsKästchen = (ff & (1 << 24)) != 0; // Comb
                break;
            }
            case EntwurfFeldTyp.Listenfeld:
                entwurf.MehrfachAuswahl = (ff & (1 << 21)) != 0; // MultiSelect
                OptionenLesen(treffer, entwurf.Optionen);
                break;
            case EntwurfFeldTyp.Dropdown:
                OptionenLesen(treffer, entwurf.Optionen);
                break;
            case EntwurfFeldTyp.Optionsfeld:
                // Auswahlmöglichkeiten sind die Exportwerte der einzelnen Schaltflächen
                // (die sichtbaren Beschriftungen stehen als Seiteninhalt, nicht im Feld).
                foreach (var schaltfläche in feld.Optionsschaltflächen)
                    if (!string.IsNullOrEmpty(schaltfläche.Export))
                        entwurf.Optionen.Add(schaltfläche.Export);
                break;
        }

        return entwurf;
    }

    /// <summary>
    /// Bestimmt den Designer-Feldtyp eines vorhandenen Feldes. Textfelder werden anhand
    /// des Mehrzeilig-Flags bzw. hinterlegter Datums-/Zahlen-Formatierung (/AA) feiner
    /// unterschieden; Auswahlfelder anhand des Combo-Flags. Nicht abbildbare Typen
    /// (Optionsfelder, Signaturen, Schaltflächen) liefern <c>null</c>.
    /// </summary>
    private static EntwurfFeldTyp? EntwurfTypBestimmen(PdfAcroField feld)
    {
        switch (feld)
        {
            case PdfTextField:
            {
                int ff = feld.Elements.GetInteger("/Ff");
                var (datum, zahl) = FormatErkennen(feld);
                if (datum) return EntwurfFeldTyp.Datum;
                if (zahl) return EntwurfFeldTyp.Zahl;
                return (ff & (1 << 12)) != 0 // Multiline
                    ? EntwurfFeldTyp.MehrzeiligesTextfeld
                    : EntwurfFeldTyp.Textfeld;
            }
            case PdfCheckBoxField:
                return EntwurfFeldTyp.Kontrollkästchen;
            case PdfRadioButtonField:
                return EntwurfFeldTyp.Optionsfeld;
            case PdfChoiceField:
            {
                int ff = feld.Elements.GetInteger("/Ff");
                return (ff & (1 << 17)) != 0 // Combo
                    ? EntwurfFeldTyp.Dropdown
                    : EntwurfFeldTyp.Listenfeld;
            }
            default:
                return null; // Optionsfeld (Radio), Signatur, Schaltfläche
        }
    }

    /// <summary>
    /// Erkennt anhand des Format-JavaScripts (/AA /F), ob ein Textfeld als Datums- oder
    /// Zahlenfeld angelegt wurde (Acrobat-Funktionen AFDate_/AFNumber_).
    /// </summary>
    private static (bool Datum, bool Zahl) FormatErkennen(PdfAcroField feld)
    {
        var aa = feld.Elements.GetDictionary("/AA");
        var f = aa?.Elements.GetDictionary("/F");
        string js = f?.Elements.GetString("/JS") ?? string.Empty;
        return (js.Contains("AFDate", StringComparison.Ordinal),
                js.Contains("AFNumber", StringComparison.Ordinal));
    }

    /// <summary>Liest Schriftgröße und Textfarbe aus einer Default-Erscheinung (/DA), z. B. "/Helv 11 Tf 0 0 0 rg".</summary>
    private static (double? Größe, string? Farbe) ErscheinungLesen(string? da)
    {
        if (string.IsNullOrWhiteSpace(da))
            return (null, null);

        var teile = da.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double? größe = null;
        string? farbe = null;

        for (int i = 0; i < teile.Length; i++)
        {
            switch (teile[i])
            {
                case "Tf" when i >= 1 && Zahl(teile[i - 1]) is double s:
                    größe = s;
                    break;
                case "rg" when i >= 3
                    && Zahl(teile[i - 3]) is double r && Zahl(teile[i - 2]) is double g && Zahl(teile[i - 1]) is double b:
                    farbe = HexAusRgb(r, g, b);
                    break;
                case "g" when i >= 1 && Zahl(teile[i - 1]) is double grau:
                    farbe = HexAusRgb(grau, grau, grau);
                    break;
            }
        }
        return (größe, farbe);
    }

    private static double? Zahl(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    /// <summary>Wandelt ein PDF-Farbarray (1 = Graustufe, 3 = RGB) in einen #RRGGBB-Hexwert um; null sonst.</summary>
    private static string? FarbeAusArray(PdfArray? arr)
    {
        if (arr is null || arr.Elements.Count == 0)
            return null;
        double Wert(int i) => arr.Elements[i] is PdfReal r ? r.Value
            : arr.Elements[i] is PdfInteger n ? n.Value : 0;

        if (arr.Elements.Count >= 3)
            return HexAusRgb(Wert(0), Wert(1), Wert(2));
        double grau = Wert(0);
        return HexAusRgb(grau, grau, grau);
    }

    private static string HexAusRgb(double r, double g, double b)
    {
        int K(double v) => Math.Clamp((int)Math.Round(v * 255), 0, 255);
        return $"#{K(r):X2}{K(g):X2}{K(b):X2}";
    }

    /// <summary>
    /// Backt ausstehende Feldänderungen sofort in die Dokument-Bytes: vorgemerkte
    /// Löschungen anwenden, Abdeckungen einbrennen, Entwürfe als echte AcroForm-Felder
    /// erstellen (gleiche Reihenfolge wie beim Speichern). Schreibt keine Datei –
    /// nur <see cref="AktuelleBytes"/> wird ersetzt. Wird beim Verlassen des
    /// Bearbeitenmodus aufgerufen, damit die Felder sofort ausfüllbar sind.
    /// </summary>
    public void FeldÄnderungenEinbacken(IEnumerable<FormularEntwurf> entwürfe,
        IEnumerable<FeldKennung> zuLöschendeFelder,
        IEnumerable<SeitenAbdeckung> abdeckungen)
    {
        Transformieren(d =>
        {
            FelderEntfernen(d, zuLöschendeFelder);
            AbdeckungenZeichnen(d, abdeckungen);
            EntwurfsfelderErstellen(d, entwürfe);
        });
    }

    // ----- Vorhandene Felder löschen --------------------------------------

    /// <summary>
    /// Berechnet die <see cref="FeldKennung"/> (Name + Rechteck) eines Feldes genauso, wie
    /// <see cref="FormularfelderLesen"/> die Geometrie liefert: das eigene <c>/Rect</c> oder –
    /// bei Optionsfeld-Gruppen ohne eigenes Rechteck – das Begrenzungsrechteck der Kind-Widgets.
    /// </summary>
    private static FeldKennung KennungBerechnen(string vollerName, PdfAcroField feld)
    {
        if (feld.Elements.ContainsKey("/Rect"))
        {
            var r = feld.Elements.GetRectangle("/Rect");
            return FeldKennung.Erzeugen(vollerName, r.X1, r.Y1, r.X2, r.Y2);
        }

        var kinder = feld.Elements.GetArray("/Kids");
        if (kinder is not null)
        {
            double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
            bool gefunden = false;
            foreach (var el in kinder.Elements)
                if (Auflösen(el) is PdfDictionary kd && kd.Elements.ContainsKey("/Rect"))
                {
                    var r = kd.Elements.GetRectangle("/Rect");
                    x1 = Math.Min(x1, r.X1); y1 = Math.Min(y1, r.Y1);
                    x2 = Math.Max(x2, r.X2); y2 = Math.Max(y2, r.Y2);
                    gefunden = true;
                }
            if (gefunden)
                return FeldKennung.Erzeugen(vollerName, x1, y1, x2, y2);
        }

        return FeldKennung.Erzeugen(vollerName, 0, 0, 0, 0);
    }

    /// <summary>
    /// Entfernt die Felder mit den angegebenen vollständigen Namen aus dem AcroForm-Feldbaum
    /// (<c>/Fields</c>) sowie deren Widgets aus den <c>/Annots</c> der jeweiligen Seiten.
    /// Wird beim Speichern vor dem Backen der Entwürfe aufgerufen, damit ein bearbeitetes
    /// Feld unter demselben Namen neu entstehen kann.
    /// </summary>
    private static void FelderEntfernen(PdfDocument d, IEnumerable<FeldKennung> kennungen)
    {
        var menge = kennungen as ISet<FeldKennung> ?? new HashSet<FeldKennung>(kennungen);
        if (menge.Count == 0)
            return;
        var form = FormularHolen(d);
        if (form is null)
            return;

        var feldIds = new HashSet<PdfObjectID>();
        var widgetIds = new HashSet<PdfObjectID>();

        DurchlaufeFelder(form.Fields, string.Empty, (vollerName, feld) =>
        {
            if (!menge.Contains(KennungBerechnen(vollerName, feld)))
                return;
            if (feld.Reference is { } fr)
                feldIds.Add(fr.ObjectID);

            // Die zu lösenden Widget-Annotationen sammeln: das Feld selbst (zusammengeführt)
            // oder seine Kind-Widgets.
            if (feld.Elements.ContainsKey("/Rect"))
            {
                if (feld.Reference is { } wr)
                    widgetIds.Add(wr.ObjectID);
            }
            else
            {
                var kinder = feld.Elements.GetArray("/Kids");
                if (kinder is not null)
                    foreach (var el in kinder.Elements)
                        if (Auflösen(el) is PdfDictionary kd && kd.Reference is { } kr)
                            widgetIds.Add(kr.ObjectID);
            }
        });

        if (feldIds.Count == 0)
            return;

        var fields = form.Elements.GetArray("/Fields");
        if (fields is not null)
            ReferenzenEntfernen(fields, feldIds, rekursivFelder: true);

        for (int i = 0; i < d.PageCount; i++)
        {
            var annots = d.Pages[i].Elements.GetArray("/Annots");
            if (annots is not null)
                ReferenzenEntfernen(annots, widgetIds, rekursivFelder: false);
        }
    }

    /// <summary>
    /// Entfernt aus <paramref name="arr"/> alle Einträge, deren aufgelöstes Objekt eine ID
    /// aus <paramref name="ids"/> trägt. Bei <paramref name="rekursivFelder"/> wird zusätzlich
    /// in die <c>/Kids</c> reiner Strukturfelder (ohne eigenes <c>/FT</c>) abgestiegen, um
    /// verschachtelte Feldhierarchien zu erreichen.
    /// </summary>
    private static void ReferenzenEntfernen(PdfArray arr, ISet<PdfObjectID> ids, bool rekursivFelder)
    {
        for (int i = arr.Elements.Count - 1; i >= 0; i--)
        {
            var dict = Auflösen(arr.Elements[i]) as PdfDictionary;
            PdfObjectID? id = dict?.Reference?.ObjectID;
            if (id is { } oid && ids.Contains(oid))
            {
                arr.Elements.RemoveAt(i);
                continue;
            }
            if (rekursivFelder && dict is not null && !dict.Elements.ContainsKey("/FT"))
            {
                var kids = dict.Elements.GetArray("/Kids");
                if (kids is not null)
                    ReferenzenEntfernen(kids, ids, true);
            }
        }
    }
}
