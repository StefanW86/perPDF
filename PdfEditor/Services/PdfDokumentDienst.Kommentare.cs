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

/// <summary>PDF-Kommentare (Standard-Annotationen): Lesen, Hinzufügen, Status, Antworten, Löschen.</summary>
public partial class PdfDokumentDienst
{
    // ----- Kommentare (PDF-Standard-Annotationen) --------------------------

    /// <summary>
    /// Liest alle vorhandenen Kommentare (Textmarkup- und Notiz-Annotationen) aus dem
    /// Dokument – einschließlich solcher, die in anderen Betrachtern (z. B. Adobe
    /// Reader) erstellt wurden. Antworten/Statusmarkierungen (mit <c>/IRT</c>) werden
    /// nicht als eigene Kommentare gelistet, sondern bestimmen den Reviewstatus des
    /// zugehörigen Kommentars.
    /// </summary>
    public List<Kommentar> KommentareLesen()
    {
        var ergebnis = new List<Kommentar>();
        if (!HatDokument)
            return ergebnis;

        using var d = Laden();
        for (int seite = 0; seite < d.PageCount; seite++)
        {
            var annots = d.Pages[seite].Elements.GetArray("/Annots");
            if (annots is null)
                continue;

            // Alle Annotationen der Seite nach Objektnummer auffindbar machen.
            var alle = new Dictionary<int, PdfDictionary>();
            foreach (var (nr, dict) in Annotationen(annots))
                alle[nr] = dict;

            // Status-Antworten je Eltern-Annotation sammeln.
            var status = new Dictionary<int, (KommentarStatus Status, DateTime Wann)>();
            foreach (var dict in alle.Values)
            {
                int irt = IRTNummer(dict);
                if (irt < 0)
                    continue;
                string? sm = TextWert(dict, "/StateModel");
                string? st = TextWert(dict, "/State");
                if (sm is null || st is null)
                    continue;
                if (!"Review".Equals(sm, StringComparison.OrdinalIgnoreCase))
                    continue;
                var s = StatusAusText(st);
                DateTime wann = DatumLesen(TextWert(dict, "/M") ?? TextWert(dict, "/CreationDate"));
                if (!status.TryGetValue(irt, out var alt) || wann >= alt.Wann)
                    status[irt] = (s, wann);
            }

            // Text-Antworten (auch aus anderen Programmen) je Wurzelkommentar sammeln.
            // Eine Antwort hat /IRT und /Contents, aber kein /StateModel (das wären Statusträger).
            var antworten = new Dictionary<int, List<KommentarAntwort>>();
            foreach (var dict in alle.Values)
            {
                if (IRTNummer(dict) < 0)
                    continue;
                if (TextWert(dict, "/StateModel") is not null)
                    continue;
                string inhalt = TextWert(dict, "/Contents") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(inhalt))
                    continue;
                int wurzel = WurzelNummer(dict, alle);
                if (wurzel < 0)
                    continue;
                if (!antworten.TryGetValue(wurzel, out var liste))
                    antworten[wurzel] = liste = new List<KommentarAntwort>();
                liste.Add(new KommentarAntwort
                {
                    Autor = TextWert(dict, "/T") ?? string.Empty,
                    Inhalt = inhalt,
                    Erstellt = DatumLesen(TextWert(dict, "/CreationDate") ?? TextWert(dict, "/M"))
                });
            }

            // Dann die eigentlichen Kommentare.
            foreach (var (nummer, dict) in Annotationen(annots))
            {
                if (IRTNummer(dict) >= 0)
                    continue; // Antwort/Status, kein eigenständiger Kommentar
                string? sub = NameWert(dict, "/Subtype");
                if (sub is null || !IstKommentarTyp(sub))
                    continue;

                var rechtecke = QuadRechtecke(dict);
                if (rechtecke.Count == 0)
                    continue;

                var k = new Kommentar
                {
                    SeitenIndex = seite,
                    ObjektNummer = nummer,
                    Rechtecke = rechtecke,
                    Kennung = TextWert(dict, "/NM") ?? string.Empty,
                    Inhalt = TextWert(dict, "/Contents") ?? string.Empty,
                    Autor = TextWert(dict, "/T") ?? string.Empty,
                    Erstellt = DatumLesen(TextWert(dict, "/CreationDate") ?? TextWert(dict, "/M")),
                    Status = status.TryGetValue(nummer, out var s) ? s.Status : KommentarStatus.Offen,
                    Antworten = antworten.TryGetValue(nummer, out var ant)
                        ? ant.OrderBy(a => a.Erstellt).ToList()
                        : Array.Empty<KommentarAntwort>()
                };
                ergebnis.Add(k);
            }
        }
        return ergebnis;
    }

    /// <summary>
    /// Fügt einen Kommentar als gelbe Hervorhebungs-Annotation (<c>/Highlight</c>) über
    /// den angegebenen Textbereichen (QuadPoints in PDF-Punkten) hinzu.
    /// </summary>
    public string KommentarHinzufügen(int seitenIndex,
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> quads,
        string inhalt, string autor)
    {
        string kennung = Guid.NewGuid().ToString();
        Transformieren(d =>
    {
        if (seitenIndex < 0 || seitenIndex >= d.PageCount || quads.Count == 0)
            return;
        var seite = d.Pages[seitenIndex];

        var annot = new PdfDictionary(d);
        annot.Elements["/Type"] = new PdfName("/Annot");
        annot.Elements["/Subtype"] = new PdfName("/Highlight");

        double minX = quads.Min(q => q.X1), minY = quads.Min(q => q.Y1);
        double maxX = quads.Max(q => q.X2), maxY = quads.Max(q => q.Y2);
        annot.Elements["/Rect"] = ZahlenFeld(d, minX, minY, maxX, maxY);

        var qp = new PdfArray(d);
        foreach (var q in quads)
            // Reihenfolge laut Spezifikation: oben-links, oben-rechts, unten-links, unten-rechts.
            foreach (double v in new[] { q.X1, q.Y2, q.X2, q.Y2, q.X1, q.Y1, q.X2, q.Y1 })
                qp.Elements.Add(new PdfReal(v));
        annot.Elements["/QuadPoints"] = qp;

        annot.Elements["/Contents"] = new PdfString(inhalt ?? string.Empty, PdfStringEncoding.Unicode);
        if (!string.IsNullOrEmpty(autor))
            annot.Elements["/T"] = new PdfString(autor, PdfStringEncoding.Unicode);
        string datum = PdfDatum(DateTime.Now);
        annot.Elements["/CreationDate"] = new PdfString(datum);
        annot.Elements["/M"] = new PdfString(datum);
        annot.Elements["/NM"] = new PdfString(kennung);
        // Gelb (#FFEB3B) als Hervorhebungsfarbe.
        var farbe = new PdfArray(d);
        foreach (double v in new[] { 1.0, 0.92, 0.23 })
            farbe.Elements.Add(new PdfReal(v));
        annot.Elements["/C"] = farbe;
        annot.Elements["/F"] = new PdfInteger(4); // druckbar
        annot.Elements["/P"] = seite.Reference;

        d.Internals.AddObject(annot);
        AnnotationAnhängen(seite, annot);
    });
        return kennung;
    }

    /// <summary>Ändert den Kommentartext (<c>/Contents</c>) eines Kommentars.</summary>
    public void KommentarInhaltSetzen(int objektNummer, string inhalt) => Transformieren(d =>
    {
        var (_, annot) = FindeAnnotation(d, objektNummer);
        if (annot is null)
            return;
        annot.Elements["/Contents"] = new PdfString(inhalt ?? string.Empty, PdfStringEncoding.Unicode);
        annot.Elements["/M"] = new PdfString(PdfDatum(DateTime.Now));
    });

    /// <summary>
    /// Fügt einem Kommentar eine Antwort hinzu (PDF-Annotation <c>/Text</c> mit <c>/IRT</c>
    /// auf den Kommentar) – kompatibel zu Adobe Reader und anderen Betrachtern.
    /// </summary>
    public void KommentarAntwortHinzufügen(int elternObjektNummer, string inhalt, string autor) =>
        Transformieren(d =>
    {
        var (seite, eltern) = FindeAnnotation(d, elternObjektNummer);
        if (eltern is null || seite is null)
            return;

        string datum = PdfDatum(DateTime.Now);
        var reply = new PdfDictionary(d);
        reply.Elements["/Type"] = new PdfName("/Annot");
        reply.Elements["/Subtype"] = new PdfName("/Text");
        reply.Elements["/IRT"] = eltern.Reference;
        reply.Elements["/Contents"] = new PdfString(inhalt ?? string.Empty, PdfStringEncoding.Unicode);
        if (!string.IsNullOrEmpty(autor))
            reply.Elements["/T"] = new PdfString(autor, PdfStringEncoding.Unicode);
        reply.Elements["/CreationDate"] = new PdfString(datum);
        reply.Elements["/M"] = new PdfString(datum);
        reply.Elements["/NM"] = new PdfString(Guid.NewGuid().ToString());
        reply.Elements["/Rect"] = RechteckKopie(d, eltern);
        reply.Elements["/P"] = seite.Reference;

        d.Internals.AddObject(reply);
        AnnotationAnhängen(seite, reply);
    });

    /// <summary>
    /// Setzt den Reviewstatus eines Kommentars (Adobe-kompatibel als Status-Antwort mit
    /// <c>/State</c> + <c>/StateModel = Review</c>). Vorhandene Statusangaben desselben
    /// Autors werden aktualisiert.
    /// </summary>
    public void KommentarStatusSetzen(int objektNummer, KommentarStatus status, string autor) =>
        Transformieren(d =>
    {
        var (seite, annot) = FindeAnnotation(d, objektNummer);
        if (annot is null || seite is null)
            return;

        string stateStr = status switch
        {
            KommentarStatus.Erledigt => "Completed",
            KommentarStatus.Abgelehnt => "Rejected",
            _ => "None"
        };
        string datum = PdfDatum(DateTime.Now);

        var vorhandene = FindeStatusAntwort(seite, objektNummer, autor);
        if (vorhandene is not null)
        {
            vorhandene.Elements["/State"] = new PdfString(stateStr);
            vorhandene.Elements["/StateModel"] = new PdfString("Review");
            vorhandene.Elements["/Contents"] = new PdfString(stateStr, PdfStringEncoding.Unicode);
            vorhandene.Elements["/M"] = new PdfString(datum);
            return;
        }

        var reply = new PdfDictionary(d);
        reply.Elements["/Type"] = new PdfName("/Annot");
        reply.Elements["/Subtype"] = new PdfName("/Text");
        reply.Elements["/IRT"] = annot.Reference;
        reply.Elements["/State"] = new PdfString(stateStr);
        reply.Elements["/StateModel"] = new PdfString("Review");
        reply.Elements["/Contents"] = new PdfString(stateStr, PdfStringEncoding.Unicode);
        if (!string.IsNullOrEmpty(autor))
            reply.Elements["/T"] = new PdfString(autor, PdfStringEncoding.Unicode);
        reply.Elements["/CreationDate"] = new PdfString(datum);
        reply.Elements["/M"] = new PdfString(datum);
        reply.Elements["/NM"] = new PdfString(Guid.NewGuid().ToString());
        reply.Elements["/F"] = new PdfInteger(2); // verborgen (nur Statusträger)
        reply.Elements["/Rect"] = RechteckKopie(d, annot);
        reply.Elements["/P"] = seite.Reference;

        d.Internals.AddObject(reply);
        AnnotationAnhängen(seite, reply);
    });

    /// <summary>Löscht einen Kommentar samt zugehörigem Popup und allen Antworten/Status.</summary>
    public void KommentarLöschen(int objektNummer) => Transformieren(d =>
    {
        var (seite, annot) = FindeAnnotation(d, objektNummer);
        if (annot is null || seite is null)
            return;

        var annots = seite.Elements.GetArray("/Annots");
        if (annots is null)
            return;

        int popupNr = ReferenzNummer(annot, "/Popup");
        var zuEntfernen = new List<PdfItem>();
        foreach (var item in annots.Elements.ToArray())
        {
            if (Auflösen(item) is not PdfDictionary dict)
                continue;
            int nr = ReferenzNummerVon(item, dict);
            if (nr == objektNummer || IRTNummer(dict) == objektNummer || (popupNr >= 0 && nr == popupNr))
                zuEntfernen.Add(item);
        }
        foreach (var item in zuEntfernen)
            annots.Elements.Remove(item);
    });

    // --- Kommentar-Hilfsfunktionen ---

    private static readonly HashSet<string> KommentarTypen = new()
    {
        "/Highlight", "/Underline", "/StrikeOut", "/Squiggly", "/Text"
    };

    private static bool IstKommentarTyp(string subtype) => KommentarTypen.Contains(subtype);

    /// <summary>
    /// Folgt der <c>/IRT</c>-Kette einer Antwort bis zur obersten Eltern-Annotation
    /// (dem eigentlichen Kommentar) und gibt deren Objektnummer zurück (oder -1).
    /// </summary>
    private static int WurzelNummer(PdfDictionary antwort, Dictionary<int, PdfDictionary> alle)
    {
        var besucht = new HashSet<int>();
        int aktuell = IRTNummer(antwort);
        int wurzel = -1;
        while (aktuell >= 0 && besucht.Add(aktuell) && alle.TryGetValue(aktuell, out var d))
        {
            wurzel = aktuell;
            int weiter = IRTNummer(d);
            if (weiter < 0)
                break; // d hat keinen Elternverweis mehr → Wurzelkommentar
            aktuell = weiter;
        }
        return wurzel;
    }

    /// <summary>Liefert (Objektnummer, Dictionary) aller Annotationen eines /Annots-Feldes.</summary>
    private static IEnumerable<(int Nummer, PdfDictionary Dict)> Annotationen(PdfArray annots)
    {
        foreach (var item in annots.Elements)
        {
            if (Auflösen(item) is not PdfDictionary dict)
                continue;
            yield return (ReferenzNummerVon(item, dict), dict);
        }
    }

    private static void AnnotationAnhängen(PdfPage seite, PdfDictionary annot)
    {
        var annots = seite.Elements.GetArray("/Annots");
        if (annots is null)
        {
            annots = new PdfArray(seite.Owner);
            seite.Elements["/Annots"] = annots;
        }
        annots.Elements.Add(annot.Reference!);
    }

    /// <summary>Sucht eine Annotation im gesamten Dokument anhand ihrer Objektnummer.</summary>
    private static (PdfPage? Seite, PdfDictionary? Annot) FindeAnnotation(PdfDocument d, int objektNummer)
    {
        for (int i = 0; i < d.PageCount; i++)
        {
            var annots = d.Pages[i].Elements.GetArray("/Annots");
            if (annots is null)
                continue;
            foreach (var (nummer, dict) in Annotationen(annots))
                if (nummer == objektNummer)
                    return (d.Pages[i], dict);
        }
        return (null, null);
    }

    /// <summary>Findet die Status-Antwort eines Autors zu einer Eltern-Annotation.</summary>
    private static PdfDictionary? FindeStatusAntwort(PdfPage seite, int elternNummer, string autor)
    {
        var annots = seite.Elements.GetArray("/Annots");
        if (annots is null)
            return null;
        foreach (var (_, dict) in Annotationen(annots))
        {
            if (IRTNummer(dict) != elternNummer)
                continue;
            if (TextWert(dict, "/StateModel") is null)
                continue;
            string a = TextWert(dict, "/T") ?? string.Empty;
            if (string.Equals(a, autor ?? string.Empty, StringComparison.Ordinal))
                return dict;
        }
        return null;
    }

    /// <summary>Objektnummer der <c>/IRT</c>-Referenz (oder -1).</summary>
    private static int IRTNummer(PdfDictionary dict) => ReferenzNummer(dict, "/IRT");

    /// <summary>Objektnummer einer Referenz unter <paramref name="schlüssel"/> (oder -1).</summary>
    private static int ReferenzNummer(PdfDictionary dict, string schlüssel)
    {
        var r = dict.Elements.GetReference(schlüssel);
        return r?.ObjectID.ObjectNumber ?? -1;
    }

    private static int ReferenzNummerVon(PdfItem item, PdfDictionary dict)
        => (item as PdfReference)?.ObjectID.ObjectNumber ?? dict.Reference?.ObjectID.ObjectNumber ?? -1;

    /// <summary>Liest die QuadPoints (oder ersatzweise das /Rect) als PDF-Punkt-Rechtecke.</summary>
    private static List<(double X1, double Y1, double X2, double Y2)> QuadRechtecke(PdfDictionary dict)
    {
        var liste = new List<(double, double, double, double)>();
        var qp = dict.Elements.GetArray("/QuadPoints");
        if (qp is not null && qp.Elements.Count >= 8)
        {
            for (int i = 0; i + 7 < qp.Elements.Count; i += 8)
            {
                double[] x = { Zahl(qp.Elements[i]), Zahl(qp.Elements[i + 2]), Zahl(qp.Elements[i + 4]), Zahl(qp.Elements[i + 6]) };
                double[] y = { Zahl(qp.Elements[i + 1]), Zahl(qp.Elements[i + 3]), Zahl(qp.Elements[i + 5]), Zahl(qp.Elements[i + 7]) };
                liste.Add((x.Min(), y.Min(), x.Max(), y.Max()));
            }
            return liste;
        }

        // Notiz-Annotationen ohne QuadPoints: das /Rect verwenden.
        var rect = dict.Elements.GetArray("/Rect");
        if (rect is not null && rect.Elements.Count >= 4)
        {
            double x1 = Zahl(rect.Elements[0]), y1 = Zahl(rect.Elements[1]);
            double x2 = Zahl(rect.Elements[2]), y2 = Zahl(rect.Elements[3]);
            liste.Add((Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)));
        }
        return liste;
    }

    private static PdfArray ZahlenFeld(PdfDocument d, params double[] werte)
    {
        var a = new PdfArray(d);
        foreach (double v in werte)
            a.Elements.Add(new PdfReal(v));
        return a;
    }

    private static PdfArray RechteckKopie(PdfDocument d, PdfDictionary annot)
    {
        var rect = annot.Elements.GetArray("/Rect");
        if (rect is not null && rect.Elements.Count >= 4)
            return ZahlenFeld(d, Zahl(rect.Elements[0]), Zahl(rect.Elements[1]),
                Zahl(rect.Elements[2]), Zahl(rect.Elements[3]));
        return ZahlenFeld(d, 0, 0, 0, 0);
    }

    private static double Zahl(PdfItem? item) => item switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        PdfReference rf => Zahl(rf.Value),
        _ => 0
    };

    private static string? TextWert(PdfDictionary dict, string schlüssel)
    {
        if (!dict.Elements.ContainsKey(schlüssel))
            return null;
        return Auflösen(dict.Elements[schlüssel]) switch
        {
            PdfString s => s.Value,
            PdfName n => n.Value,
            _ => null
        };
    }

    private static string? NameWert(PdfDictionary dict, string schlüssel)
        => Auflösen(dict.Elements.GetValue(schlüssel)) is PdfName n ? n.Value : null;

    private static KommentarStatus StatusAusText(string state) => state switch
    {
        "Completed" => KommentarStatus.Erledigt,
        "Rejected" => KommentarStatus.Abgelehnt,
        _ => KommentarStatus.Offen
    };

    /// <summary>Formatiert ein Datum im PDF-Format <c>D:YYYYMMDDHHmmSS+hh'mm'</c>.</summary>
    private static string PdfDatum(DateTime dt)
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(dt);
        char vz = off < TimeSpan.Zero ? '-' : '+';
        return $"D:{dt:yyyyMMddHHmmss}{vz}{Math.Abs(off.Hours):00}'{Math.Abs(off.Minutes):00}'";
    }

    /// <summary>Liest ein PDF-Datum (<c>D:YYYYMMDD...</c>); bei Fehlern <c>default</c>.</summary>
    private static DateTime DatumLesen(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return default;
        string t = s.StartsWith("D:") ? s[2..] : s;
        try
        {
            int Jahr = int.Parse(t.Substring(0, 4), CultureInfo.InvariantCulture);
            int Mon = t.Length >= 6 ? int.Parse(t.Substring(4, 2), CultureInfo.InvariantCulture) : 1;
            int Tag = t.Length >= 8 ? int.Parse(t.Substring(6, 2), CultureInfo.InvariantCulture) : 1;
            int Std = t.Length >= 10 ? int.Parse(t.Substring(8, 2), CultureInfo.InvariantCulture) : 0;
            int Min = t.Length >= 12 ? int.Parse(t.Substring(10, 2), CultureInfo.InvariantCulture) : 0;
            int Sek = t.Length >= 14 ? int.Parse(t.Substring(12, 2), CultureInfo.InvariantCulture) : 0;
            return new DateTime(Jahr, Mon, Tag, Std, Min, Sek);
        }
        catch
        {
            return default;
        }
    }

}
