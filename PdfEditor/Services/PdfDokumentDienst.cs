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

/// <summary>
/// Kapselt alle Bearbeitungs- und Lesevorgänge am PDF-Dokument auf Basis von
/// PDFsharp. Da PDFsharp ein Dokument nach dem Speichern nicht weiter bearbeiten
/// kann, wird der aktuelle Stand als Byte-Feld gehalten: Jede Operation lädt das
/// Dokument aus diesen Bytes, ändert es und speichert es wieder als Bytes.
/// </summary>
public partial class PdfDokumentDienst
{
    /// <summary>Der aktuelle Stand des Dokuments als PDF-Bytes (Quelle der Wahrheit).</summary>
    public byte[]? AktuelleBytes { get; private set; }

    /// <summary>Der Dateipfad, unter dem zuletzt geöffnet/gespeichert wurde.</summary>
    public string? Pfad { get; private set; }

    /// <summary>Gibt an, ob aktuell ein Dokument geladen ist.</summary>
    public bool HatDokument => AktuelleBytes is { Length: > 0 };

    // ----- Laden und Anlegen -----------------------------------------------

    /// <summary>Öffnet eine PDF-Datei.</summary>
    public void Öffnen(string pfad)
    {
        AktuelleBytes = File.ReadAllBytes(pfad);
        Pfad = pfad;
    }

    /// <summary>
    /// Übernimmt bereits gelesene Bytes als aktives Dokument (für asynchrones Laden).
    /// </summary>
    public void ÖffnenAusBytes(byte[] bytes, string pfad)
    {
        AktuelleBytes = bytes;
        Pfad = pfad;
    }

    /// <summary>Legt ein neues, leeres Dokument mit einer A4-Seite an.</summary>
    public void Neu()
    {
        using var d = new PdfDocument();
        var p = d.AddPage();
        p.Size = PageSize.A4;
        AktuelleBytes = NachBytes(d);
        Pfad = null;
    }

    // ----- Seitenoperationen (Byte-Transformationen) -----------------------

    /// <summary>Fügt eine leere A4-Seite an der angegebenen Position ein.</summary>
    public void SeiteEinfügen(int index) => Transformieren(d =>
    {
        PdfPage seite = index >= d.PageCount ? d.Pages.Add() : d.Pages.Insert(index);
        seite.Size = PageSize.A4;
    });

    /// <summary>Importiert alle Seiten einer anderen PDF an der angegebenen Position.</summary>
    public void SeitenImportieren(int index, string quellpfad) => Transformieren(d =>
    {
        using var quelle = PdfReader.Open(quellpfad, PdfDocumentOpenMode.Import);
        int an = Math.Clamp(index, 0, d.PageCount);
        d.Pages.InsertRange(an, quelle);
    });

    /// <summary>Entfernt die Seite mit dem angegebenen Index.</summary>
    public void SeiteLöschen(int index) => Transformieren(d => d.Pages.RemoveAt(index));

    /// <summary>
    /// Entfernt mehrere Seiten in einer einzigen Transformation. Es wird absteigend
    /// gelöscht, damit sich die Indizes nicht gegenseitig verschieben.
    /// </summary>
    public void SeitenLöschen(IEnumerable<int> indizes) => Transformieren(d =>
    {
        foreach (int i in indizes.Distinct().OrderByDescending(i => i))
            d.Pages.RemoveAt(i);
    });

    /// <summary>Dreht die Seite um den angegebenen Winkel (z. B. +90 oder -90 Grad).</summary>
    public void SeiteDrehen(int index, int deltaGrad) => Transformieren(d =>
    {
        var seite = d.Pages[index];
        seite.Rotate = (((seite.Rotate + deltaGrad) % 360) + 360) % 360;
    });

    /// <summary>Dreht mehrere Seiten in einer einzigen Transformation.</summary>
    public void SeitenDrehen(IEnumerable<int> indizes, int deltaGrad) => Transformieren(d =>
    {
        foreach (int i in indizes.Distinct())
        {
            var seite = d.Pages[i];
            seite.Rotate = (((seite.Rotate + deltaGrad) % 360) + 360) % 360;
        }
    });

    /// <summary>Verschiebt eine Seite von einer Position an eine andere.</summary>
    public void SeiteVerschieben(int altIndex, int neuIndex) =>
        Transformieren(d => d.Pages.MovePage(altIndex, neuIndex));

    // ----- Geometrie / Seiteninfos -----------------------------------------

    /// <summary>Liest Größe und Drehung aller Seiten für die Koordinatenumrechnung.</summary>
    public List<SeitenInfo> SeitenInfosLesen()
    {
        var liste = new List<SeitenInfo>();
        if (!HatDokument)
            return liste;

        using var d = Laden();
        for (int i = 0; i < d.PageCount; i++)
        {
            var mb = d.Pages[i].MediaBox;
            liste.Add(new SeitenInfo(mb.X1, mb.Y1, mb.Width, mb.Height, d.Pages[i].Rotate));
        }
        return liste;
    }

    // ----- Formularfelder ---------------------------------------------------

    /// <summary>Liest alle ausfüllbaren Formularfelder samt Position und aktuellem Wert.</summary>
    public List<FormularFeld> FormularfelderLesen()
    {
        var ergebnis = new List<FormularFeld>();
        if (!HatDokument)
            return ergebnis;

        using var d = Laden();
        var form = FormularHolen(d);
        if (form is null)
            return ergebnis;

        var seitenIndex = SeitenIndexZuordnung(d);

        DurchlaufeFelder(form.Fields, string.Empty, (vollerName, feld) =>
        {
            FeldTyp? typ = FeldTypBestimmen(feld);
            if (typ is null)
                return;

            // Optionsfelder (Radio) bündeln mehrere Widgets zu einer Gruppe – gesondert lesen.
            if (typ == FeldTyp.Optionsfeld)
            {
                OptionsfeldLesen(vollerName, feld, seitenIndex, ergebnis);
                return;
            }

            foreach (var (rect, seite) in WidgetGeometrie(feld, seitenIndex))
            {
                var ff = new FormularFeld(vollerName, typ.Value, seite,
                    rect.X1, rect.Y1, rect.X2, rect.Y2);

                switch (feld)
                {
                    case PdfTextField t:
                        ff.Wert = t.Text ?? string.Empty;
                        break;
                    case PdfCheckBoxField c:
                        ff.IstAngehakt = c.Checked;
                        break;
                    case PdfChoiceField:
                        ff.Wert = ItemText(feld.Value);
                        OptionenLesen(feld, ff.Optionen);
                        break;
                }

                ergebnis.Add(ff);
            }
        });

        return ergebnis;
    }


    // ----- interne Hilfsfunktionen -----------------------------------------

    private PdfDocument Laden()
        => PdfReader.Open(new MemoryStream(AktuelleBytes!), PdfDocumentOpenMode.Modify);

    private static byte[] NachBytes(PdfDocument d)
    {
        using var ms = new MemoryStream();
        d.Save(ms);
        return ms.ToArray();
    }

    private void Transformieren(Action<PdfDocument> operation)
    {
        if (!HatDokument)
            Neu();
        using var d = Laden();
        operation(d);
        AktuelleBytes = NachBytes(d);
    }

    /// <summary>Bildet jede Seitenreferenz auf ihren 0-basierten Index ab.</summary>
    private static Dictionary<PdfObjectID, int> SeitenIndexZuordnung(PdfDocument d)
    {
        var map = new Dictionary<PdfObjectID, int>();
        for (int i = 0; i < d.PageCount; i++)
        {
            var r = d.Pages[i].Reference;
            if (r is not null)
                map[r.ObjectID] = i;
        }
        return map;
    }

    /// <summary>Durchläuft den Feldbaum rekursiv und besucht alle Endfelder.</summary>
    private static void DurchlaufeFelder(PdfAcroField.PdfAcroFieldCollection felder,
        string präfix, Action<string, PdfAcroField> besuchen)
    {
        for (int i = 0; i < felder.Count; i++)
        {
            var feld = felder[i];
            string name = feld.Name ?? string.Empty;
            string vollerName = string.IsNullOrEmpty(präfix)
                ? name
                : string.IsNullOrEmpty(name) ? präfix : $"{präfix}.{name}";

            // Felder mit eigenem Feldtyp (/FT) sind Endfelder; andere haben Unterfelder.
            if (feld.Elements.ContainsKey("/FT"))
                besuchen(vollerName, feld);
            else if (feld.Fields.Count > 0)
                DurchlaufeFelder(feld.Fields, vollerName, besuchen);
        }
    }

    private static FeldTyp? FeldTypBestimmen(PdfAcroField feld) => feld switch
    {
        PdfTextField => FeldTyp.Text,
        PdfCheckBoxField => FeldTyp.Kontrollkästchen,
        PdfRadioButtonField => FeldTyp.Optionsfeld,
        PdfChoiceField => FeldTyp.Auswahlliste, // umfasst Combo- und Listenfelder
        _ => null                               // sonstige Schaltflächen, Signaturen werden übersprungen
    };

    /// <summary>
    /// Liest eine Optionsfeld-Gruppe (Radio) als ein <see cref="FormularFeld"/> je Seite:
    /// Begrenzungsrechteck aller Optionen, deren Exportwerte samt Einzelrechtecken und die
    /// aktuell gewählte Option (Eltern-<c>/V</c>). So lassen sich die Optionen als
    /// anklickbare Overlays darstellen.
    /// </summary>
    private static void OptionsfeldLesen(string name, PdfAcroField feld,
        Dictionary<PdfObjectID, int> seitenIndex, List<FormularFeld> ziel)
    {
        var kinder = feld.Elements.GetArray("/Kids");
        if (kinder is null)
            return;
        string gewählt = ItemText(feld.Elements["/V"]);

        var nachSeite = new Dictionary<int, List<(string Export, PdfRectangle Rect)>>();
        foreach (var element in kinder.Elements)
        {
            if (Auflösen(element) is not PdfDictionary kd || !kd.Elements.ContainsKey("/Rect"))
                continue;
            int seite = SeiteVonDictionary(kd, seitenIndex);
            if (seite < 0)
                continue;
            if (!nachSeite.TryGetValue(seite, out var l))
                nachSeite[seite] = l = new();
            l.Add((OptionExport(kd), kd.Elements.GetRectangle("/Rect")));
        }

        foreach (var (seite, optionen) in nachSeite)
        {
            double x1 = optionen.Min(o => o.Rect.X1), y1 = optionen.Min(o => o.Rect.Y1);
            double x2 = optionen.Max(o => o.Rect.X2), y2 = optionen.Max(o => o.Rect.Y2);
            var ff = new FormularFeld(name, FeldTyp.Optionsfeld, seite, x1, y1, x2, y2)
            {
                Wert = gewählt
            };
            foreach (var (export, rect) in optionen)
                ff.Optionsschaltflächen.Add(
                    new Optionsschaltfläche(export, rect.X1, rect.Y1, rect.X2, rect.Y2));
            ziel.Add(ff);
        }
    }

    /// <summary>Ermittelt den Exportwert einer Options-Schaltfläche (aus <c>/AP /N</c>, sonst <c>/AS</c>).</summary>
    private static string OptionExport(PdfDictionary widget)
    {
        var n = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        if (n is not null)
            foreach (var key in n.Elements.Keys)
                if (key != "/Off")
                    return key.TrimStart('/');
        return ItemText(widget.Elements["/AS"]);
    }

    /// <summary>Ermittelt Rechteck und Seitenindex aller Widgets eines Feldes.</summary>
    private static List<(PdfRectangle Rect, int Seite)> WidgetGeometrie(
        PdfAcroField feld, Dictionary<PdfObjectID, int> seitenIndex)
    {
        var liste = new List<(PdfRectangle, int)>();

        if (feld.Elements.ContainsKey("/Rect"))
        {
            int seite = SeiteVonDictionary(feld, seitenIndex);
            if (seite >= 0)
                liste.Add((feld.Elements.GetRectangle("/Rect"), seite));
        }
        else if (feld.Elements.ContainsKey("/Kids"))
        {
            var kinder = feld.Elements.GetArray("/Kids");
            if (kinder is not null)
            {
                foreach (var element in kinder.Elements)
                {
                    if (Auflösen(element) is not PdfDictionary kd)
                        continue;
                    if (!kd.Elements.ContainsKey("/Rect"))
                        continue;
                    int seite = SeiteVonDictionary(kd, seitenIndex);
                    if (seite >= 0)
                        liste.Add((kd.Elements.GetRectangle("/Rect"), seite));
                }
            }
        }

        return liste;
    }

    private static int SeiteVonDictionary(PdfDictionary d, Dictionary<PdfObjectID, int> seitenIndex)
    {
        var p = d.Elements.GetReference("/P");
        if (p is not null && seitenIndex.TryGetValue(p.ObjectID, out int index))
            return index;
        return -1;
    }

    private static PdfItem? Auflösen(PdfItem? element)
        => element is PdfReference r ? r.Value : element;

    private static string ItemText(PdfItem? item) => item switch
    {
        PdfString s => s.Value,
        PdfName n => n.Value.TrimStart('/'),
        _ => string.Empty
    };

    /// <summary>Liest die Auswahlmöglichkeiten (/Opt) einer Auswahlliste aus.</summary>
    private static void OptionenLesen(PdfAcroField feld, List<string> ziel)
    {
        if (!feld.Elements.ContainsKey("/Opt"))
            return;
        var opt = feld.Elements.GetArray("/Opt");
        if (opt is null)
            return;

        foreach (var element in opt.Elements)
        {
            var aufgelöst = Auflösen(element);
            if (aufgelöst is PdfArray arr && arr.Elements.Count > 0)
            {
                // Form [Exportwert, Anzeigetext] – Anzeigetext bevorzugen.
                var anzeige = arr.Elements.Count > 1 ? arr.Elements[1] : arr.Elements[0];
                ziel.Add(ItemText(anzeige));
            }
            else
            {
                ziel.Add(ItemText(aufgelöst));
            }
        }
    }
}
