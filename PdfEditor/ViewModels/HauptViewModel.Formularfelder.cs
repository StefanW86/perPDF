using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>Formular-Designer: Anlegen und Verwalten von Entwurfsfeldern.</summary>
public partial class HauptViewModel
{
    /// <summary>
    /// Ausstehende Abdeckungen der Beschriftungen gelöschter Optionsfelder – rein passiv
    /// (nicht anklickbar), beim Speichern eingebrannt, danach geleert.
    /// </summary>
    public ObservableCollection<SeitenAbdeckung> Seitenabdeckungen { get; } = new();

    /// <summary>Auswahlmöglichkeiten des ausgewählten Feldes als Text (eine Option je Zeile).</summary>
    public string FeldOptionenText
    {
        get => _ausgewähltesFeld is null ? string.Empty
            : string.Join(Environment.NewLine, _ausgewähltesFeld.Optionen);
        set
        {
            if (_ausgewähltesFeld is null)
                return;
            var alt = _ausgewähltesFeld.Optionen.ToList();
            _ausgewähltesFeld.Optionen.Clear();
            foreach (var zeile in (value ?? string.Empty).Split('\n'))
            {
                string t = zeile.Trim('\r', ' ', '\t');
                if (t.Length > 0)
                    _ausgewähltesFeld.Optionen.Add(t);
            }
            // Kein Melde(FeldOptionenText) hier: Das schriebe den normalisierten Text in
            // die TextBox zurück und störte die laufende Eingabe (die Bindung übernimmt
            // per LostFocus; beim erneuten Auswählen zeigt der Getter die Liste ohnehin
            // normalisiert).
            OptionenUmbenanntNachführen(_ausgewähltesFeld, alt);
            FeldGrößeAnpassen(_ausgewähltesFeld); // Rahmen wächst mit dem Inhalt
            AktuelleSeiteNeu?.Invoke();
        }
    }

    /// <summary>Maximale Zeichenanzahl des ausgewählten Feldes als Text (leer = unbegrenzt).</summary>
    public string FeldMaxZeichenText
    {
        get => _ausgewähltesFeld?.MaxZeichen?.ToString() ?? string.Empty;
        set
        {
            if (_ausgewähltesFeld is null)
                return;
            _ausgewähltesFeld.MaxZeichen = int.TryParse(value, out int n) && n > 0 ? n : null;
            Melde(nameof(FeldMaxZeichenText));
        }
    }

    // ----- Formular-Designer (Felder erstellen) ----------------------------

    /// <summary>
    /// Aktiviert den Platziermodus für ein neues Formularfeld des angegebenen Typs.
    /// Beim nächsten Klick auf die Seite wird das Feld dort angelegt.
    /// </summary>
    public void FeldErstellenStarten(EntwurfFeldTyp typ)
    {
        if (!_dienst.HatDokument || AktuelleSeite < 0)
        {
            Status = "Bitte zuerst eine PDF öffnen oder eine Seite hinzufügen.";
            Melde(nameof(NeuesFeldTyp));
            return;
        }
        _markerModus = _radiererModus = false;
        Melde(nameof(MarkerModus));
        Melde(nameof(RadiererModus));
        FelderBearbeitenModus = false;
        EinfügeModusBeenden();
        NeuesFeldTyp = typ;
        Status = $"{FeldBezeichnung(typ)} – auf die Stelle klicken, an der es beginnen soll. Esc bricht ab.";
    }

    /// <summary>Beendet den Formularfeld-Platziermodus ohne ein Feld zu platzieren.</summary>
    public void FeldModusBeenden()
    {
        if (_neuesFeldTyp is not null)
            NeuesFeldTyp = null;
    }

    /// <summary>Legt ein neues Entwurfsfeld an der angegebenen PDF-Koordinate (obere linke Ecke) an.</summary>
    public void FeldAnPosition(double pdfX, double pdfY)
    {
        var typ = _neuesFeldTyp;
        if (typ is null || AktuelleSeite < 0)
        {
            FeldModusBeenden();
            return;
        }

        (double breite, double höhe) = FeldStandardGröße(typ.Value);
        var feld = new FormularEntwurf(typ.Value, AktuelleSeite)
        {
            FeldName = EindeutigerFeldName(),
            Breite = breite,
            Höhe = höhe,
            X = pdfX,
            Y = pdfY - höhe
        };
        if (typ is EntwurfFeldTyp.Dropdown or EntwurfFeldTyp.Listenfeld)
            feld.Optionen.AddRange(new[] { "Option 1", "Option 2", "Option 3" });
        else if (typ is EntwurfFeldTyp.Optionsfeld)
            feld.Optionen.AddRange(new[] { "Option 1", "Option 2" });

        Entwurfsfelder.Add(feld);
        AusgewähltesFeld = feld;
        FeldModusBeenden();
        if (!_istLaden) IstGeändert = true;
        AktuelleSeiteNeu?.Invoke();
        Status = $"{FeldBezeichnung(typ.Value)} angelegt – rechts konfigurieren, mit der Maus verschieben/skalieren.";
    }

    /// <summary>
    /// Liefert einen Feldnamen <c>FeldN</c>, der weder von einem bereits im Dokument
    /// vorhandenen AcroForm-Feld noch von einem anderen Entwurfsfeld belegt ist.
    /// Verhindert, dass zwei Felder denselben <c>/T</c>-Namen tragen (sonst gelten sie in
    /// PDF als ein gemeinsames Feld und teilen sich ihren Wert).
    /// </summary>
    private string EindeutigerFeldName(string präfix = "Feld")
    {
        var belegt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Felder) belegt.Add(f.FeldName);
        foreach (var e in Entwurfsfelder) belegt.Add(e.FeldName);

        int n = 1;
        string name;
        do { name = $"{präfix}{n++}"; } while (belegt.Contains(name));
        return name;
    }

    /// <summary>Entfernt ein Entwurfsfeld wieder (aus dem Hauptfenster aufgerufen).</summary>
    public void FeldEntfernen(FormularEntwurf feld)
    {
        Entwurfsfelder.Remove(feld);
        // Bearbeitete Optionsfelder: Das Original bleibt zum Löschen vorgemerkt – seine
        // eingebrannten Beschriftungen abdecken, damit sie nach dem Speichern nicht
        // ohne Schaltflächen stehenbleiben.
        foreach (var a in feld.Abdeckungen)
            if (a is not null)
                Seitenabdeckungen.Add(new SeitenAbdeckung(feld.SeitenIndex, a));
        if (ReferenceEquals(_ausgewähltesFeld, feld))
            AusgewähltesFeld = null;
        AktuelleSeiteNeu?.Invoke();
    }

    /// <summary>
    /// Übernimmt beim Verlassen des Bearbeitenmodus alle ausstehenden Feldänderungen
    /// sofort ins Dokument (neue/bearbeitete Entwürfe, vorgemerkte Löschungen,
    /// Beschriftungs-Abdeckungen). Die Felder erscheinen danach als normale ausfüllbare
    /// Formularfelder; die Datei wird weiterhin erst mit „Speichern" geschrieben.
    /// </summary>
    private void AusstehendeFelderEinbacken()
    {
        if (Entwurfsfelder.Count == 0 && _zuLöschendeFelder.Count == 0 && Seitenabdeckungen.Count == 0)
            return;
        try
        {
            _dienst.FeldÄnderungenEinbacken(Entwurfsfelder, _zuLöschendeFelder, Seitenabdeckungen);
            Entwurfsfelder.Clear();
            Seitenabdeckungen.Clear();
            _zuLöschendeFelder.Clear();
            FelderNeuLaden();
            NachStrukturänderung(AktuelleSeite);
            Status = "Feldänderungen ins Dokument übernommen – zum Sichern der Datei speichern.";
        }
        catch (Exception ex)
        {
            Fehler("Die Feldänderungen konnten nicht übernommen werden.", ex);
        }
    }

    // ----- Vorhandene Felder bearbeiten/löschen (Bearbeitungsmodus) --------

    /// <summary>
    /// Übernimmt ein vorhandenes Formularfeld zur Bearbeitung: Es wird originaltreu in einen
    /// <see cref="FormularEntwurf"/> übersetzt (alle abbildbaren Eigenschaften), das Original
    /// zum Entfernen beim Speichern vorgemerkt und aus der Live-Liste genommen. Ab dann lässt
    /// es sich wie ein neu angelegtes Feld verschieben, skalieren und konfigurieren; beim
    /// Speichern entsteht es unter demselben Namen neu.
    /// </summary>
    public void FeldBearbeiten(FormularFeld feld)
    {
        var entwurf = _dienst.FeldZuEntwurf(feld);
        if (entwurf is null)
        {
            Status = "Dieses Feld kann nicht bearbeitet werden.";
            return;
        }

        // Optionsfelder: die im Seiteninhalt eingebrannten Beschriftungen werden zu den
        // Optionen des Entwurfs; die Originale deckt beim Speichern ein Rechteck in der
        // abgetasteten Hintergrundfarbe ab, und das Feld zeichnet die (änderbaren)
        // Beschriftungen selbst neu neben die Schaltflächen. Das Entwurfsrechteck umfasst
        // Schaltflächen und Beschriftungen, damit das neue Layout den Platz des Originals
        // einnimmt.
        if (feld.Typ == FeldTyp.Optionsfeld)
        {
            OptionsfeldBeschriftungenÜbernehmen(feld, entwurf);
            var (gx1, gy1, gx2, gy2) = OptionsfeldGesamtBox(feld);
            entwurf.X = gx1;
            entwurf.Y = gy1;
            entwurf.Breite = gx2 - gx1;
            entwurf.Höhe = gy2 - gy1;
        }

        _zuLöschendeFelder.Add(FeldKennung.Von(feld));
        feld.PropertyChanged -= FormularfeldGeändert;
        Felder.Remove(feld);

        Entwurfsfelder.Add(entwurf);
        AusgewähltesFeld = entwurf;
        if (!_istLaden) IstGeändert = true;
        AktuelleSeiteNeu?.Invoke();
        Status = feld.Typ == FeldTyp.Optionsfeld
            ? "Optionsfeld in Bearbeitung – Beschriftungen rechts unter „Optionen“ ändern; Feld verschieben/skalieren."
            : "Feld in Bearbeitung – rechts konfigurieren, mit der Maus verschieben/skalieren.";
    }

    /// <summary>
    /// Findet je Optionsschaltfläche eines Optionsfeldes den abzudeckenden Bereich samt
    /// abgetasteter Hintergrundfarbe: das Vereinigungsrechteck aus Schaltfläche und – wo
    /// erkannt – der danebenstehenden Beschriftung. Die Schaltfläche gehört mit dazu,
    /// weil manche PDFs die Knopf-Optik (Ringe, Punkte, Klammern) als Seiteninhalt
    /// zeichnen; nach dem Entfernen des Widgets bliebe sie sonst als Rückstand stehen.
    /// Ergebnis positionsgleich zu <see cref="FormularFeld.Optionsschaltflächen"/>;
    /// leer, wenn kein Dokument geladen ist.
    /// </summary>
    private List<(Beschriftung? Beschriftung, BeschriftungsAbdeckung Abdeckung)> OptionsfeldAbdeckungenFinden(FormularFeld feld)
    {
        var ergebnis = new List<(Beschriftung?, BeschriftungsAbdeckung)>();
        if (Bytes is null || feld.SeitenIndex < 0 || feld.SeitenIndex >= _seitenInfos.Count
            || feld.Optionsschaltflächen.Count == 0)
            return ergebnis;

        var knöpfe = feld.Optionsschaltflächen
            .Select(o => (o.X1, o.Y1, o.X2, o.Y2)).ToList();
        var info = _seitenInfos[feld.SeitenIndex];
        var beschriftungen = _textDienst.Beschriftungen(Bytes, feld.SeitenIndex, info.X1, info.Y1, knöpfe);

        const double rand = 1.5; // leicht erweitern, damit das Original sicher überdeckt wird
        var bereiche = new List<(double X1, double Y1, double X2, double Y2)>();
        for (int i = 0; i < knöpfe.Count; i++)
        {
            var k = knöpfe[i];
            var b = beschriftungen[i];
            bereiche.Add((
                Math.Min(k.X1, b?.X1 ?? k.X1) - rand,
                Math.Min(k.Y1, b?.Y1 ?? k.Y1) - rand,
                Math.Max(k.X2, b?.X2 ?? k.X2) + rand,
                Math.Max(k.Y2, b?.Y2 ?? k.Y2) + rand));
        }
        var farben = PdfRenderDienst.HintergrundFarben(Bytes, feld.SeitenIndex, info, bereiche);

        for (int i = 0; i < knöpfe.Count; i++)
        {
            string farbe = i < farben.Count ? farben[i] : "#FFFFFF";
            var (x1, y1, x2, y2) = bereiche[i];
            ergebnis.Add((beschriftungen[i], new BeschriftungsAbdeckung(x1, y1, x2, y2, farbe)));
        }
        return ergebnis;
    }

    /// <summary>
    /// Liest die danebenstehenden Beschriftungen eines Optionsfeldes aus dem Seiteninhalt,
    /// übernimmt ihre Texte als Optionen des Entwurfs (statt der technischen Exportwerte)
    /// und legt je erkannter Beschriftung ein Abdeck-Rechteck in der abgetasteten
    /// Hintergrundfarbe an (<see cref="FormularEntwurf.Abdeckungen"/>, positionsgleich zu
    /// den Optionen). Beim Speichern verdeckt das Rechteck die eingebrannte Original-
    /// Beschriftung, und das Feld zeichnet den (ggf. geänderten) Text selbst neu – die
    /// Beschriftung bleibt so dem Feld zugeordnet und folgt ihm beim Verschieben.
    /// </summary>
    private void OptionsfeldBeschriftungenÜbernehmen(FormularFeld feld, FormularEntwurf entwurf)
    {
        var gefunden = OptionsfeldAbdeckungenFinden(feld);
        int optIndex = -1;
        for (int i = 0; i < gefunden.Count; i++)
        {
            // Die Entwurfs-Optionen entstanden aus den Schaltflächen mit Exportwert
            // (FeldZuEntwurf) – denselben Zähler mitführen, damit Abdeckung und Option
            // positionsgleich bleiben.
            bool hatOption = !string.IsNullOrEmpty(feld.Optionsschaltflächen[i].Export);
            if (hatOption)
                optIndex++;
            var (beschriftung, abdeckung) = gefunden[i];

            if (hatOption && optIndex < entwurf.Optionen.Count && beschriftung is not null)
            {
                entwurf.Abdeckungen.Add(abdeckung);
                if (entwurf.Standardwert == entwurf.Optionen[optIndex])
                    entwurf.Standardwert = beschriftung.Text;
                entwurf.Optionen[optIndex] = beschriftung.Text;
                continue;
            }

            // Ohne erkannte Beschriftung (oder ohne zugehörige Option) zeichnet das Feld
            // für diese Zeile keine neue Beschriftung – der Schaltflächenbereich wird aber
            // unabhängig vom Entwurf abgedeckt, weil das Widget gelöscht wird und als
            // Inhalt gezeichnete Knopf-Optik sonst als Rückstand stehen bliebe.
            if (hatOption && optIndex < entwurf.Optionen.Count)
                entwurf.Abdeckungen.Add(null);
            Seitenabdeckungen.Add(new SeitenAbdeckung(feld.SeitenIndex, abdeckung));
        }
    }

    /// <summary>
    /// Führt nach einer Änderung der Optionsliste in der Seitenleiste den Standardwert
    /// nach: Wird die vorausgewählte Option umbenannt, folgt er dem neuen Text
    /// (positionsgleicher Alt/Neu-Vergleich), sonst ginge die Vorauswahl verloren.
    /// </summary>
    private static void OptionenUmbenanntNachführen(FormularEntwurf feld, List<string> alteOptionen)
    {
        int n = Math.Min(alteOptionen.Count, feld.Optionen.Count);
        for (int i = 0; i < n; i++)
            if (alteOptionen[i] != feld.Optionen[i] && feld.Standardwert == alteOptionen[i])
                feld.Standardwert = feld.Optionen[i];
    }

    /// <summary>
    /// Vergrößert ein Auswahlfeld nach einer Optionsänderung, damit der Inhalt hineinpasst:
    /// die Höhe für alle Optionen (Optionsfeld/Listenfeld; Dropdown bleibt einzeilig) und
    /// die Breite für die längste Option. Es wird nur gewachsen, nie geschrumpft – eine
    /// von Hand größer gezogene Box bleibt erhalten. Die Oberkante und die linke Kante
    /// bleiben fest; gewachsen wird nach unten und rechts.
    /// </summary>
    private static void FeldGrößeAnpassen(FormularEntwurf feld)
    {
        if (feld.Typ is not (EntwurfFeldTyp.Optionsfeld or EntwurfFeldTyp.Listenfeld or EntwurfFeldTyp.Dropdown))
            return;
        int n = Math.Max(1, feld.Optionen.Count);
        double fsListe = feld.Schriftgröße > 0 ? feld.Schriftgröße : 11;

        double höheNeu = feld.Typ switch
        {
            // 20 pt je Zeile: Schaltfläche (16 pt) + Luft – entspricht der Dichte beim Backen.
            EntwurfFeldTyp.Optionsfeld => Math.Max(feld.Höhe, n * 20),
            EntwurfFeldTyp.Listenfeld => Math.Max(feld.Höhe, n * fsListe * 1.4 + 4),
            _ => feld.Höhe
        };

        // Schrift wie beim Backen (Optionsfeld: OptionsfeldErstellen; sonst /DA-Größe).
        double fs = feld.Typ == EntwurfFeldTyp.Optionsfeld
            ? Math.Clamp(höheNeu / n * 0.6, 7, 12)
            : fsListe;
        double textBreite = 0;
        foreach (var o in feld.Optionen)
            textBreite = Math.Max(textBreite, TextBreitePunkte(o, fs));
        double zusatz = feld.Typ switch
        {
            EntwurfFeldTyp.Optionsfeld => 16 + 4 + 8, // Schaltfläche + Abstand + Rand
            EntwurfFeldTyp.Dropdown => 24,            // Aufklapp-Pfeil + Innenabstände
            _ => 12                                   // Listenfeld-Innenabstand
        };
        double breiteNeu = Math.Max(feld.Breite, textBreite + zusatz);

        feld.Y -= höheNeu - feld.Höhe; // Oberkante halten → nach unten wachsen
        feld.Höhe = höheNeu;
        feld.Breite = breiteNeu;
    }

    /// <summary>Breite eines Textes in PDF-Punkten (Arial, wie beim Backen der Beschriftungen).</summary>
    private static double TextBreitePunkte(string text, double schriftgröße)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var ft = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface("Arial"),
            schriftgröße, // em-Größe in Punkten → Ergebnis ist ebenfalls in Punkten
            System.Windows.Media.Brushes.Black,
            1.0);
        return ft.WidthIncludingTrailingWhitespace;
    }

    /// <summary>
    /// Gesamtrechteck eines Optionsfeldes einschließlich der danebenstehenden (im
    /// Seiteninhalt eingebrannten) Beschriftungen, in PDF-Punkten. Der Auswahlrahmen im
    /// Bearbeitungsmodus fasst damit auch die Texte mit ein, nicht nur die Schaltflächen.
    /// Je Feldobjekt zwischengespeichert, weil die Beschriftungssuche die Seite mit
    /// PdfPig parst und bei jedem Neuzeichnen anfällt; die Feldobjekte werden bei jedem
    /// Neuladen der Felder neu erzeugt, wodurch der Cache von selbst verfällt.
    /// </summary>
    public (double X1, double Y1, double X2, double Y2) OptionsfeldGesamtBox(FormularFeld feld)
    {
        if (_gesamtBoxen.TryGetValue(feld, out var g))
            return (g.X1, g.Y1, g.X2, g.Y2);

        double x1 = feld.X1, y1 = feld.Y1, x2 = feld.X2, y2 = feld.Y2;
        if (Bytes is not null && feld.SeitenIndex >= 0 && feld.SeitenIndex < _seitenInfos.Count
            && feld.Optionsschaltflächen.Count > 0)
        {
            var info = _seitenInfos[feld.SeitenIndex];
            var knöpfe = feld.Optionsschaltflächen
                .Select(o => (o.X1, o.Y1, o.X2, o.Y2)).ToList();
            foreach (var b in _textDienst.Beschriftungen(Bytes, feld.SeitenIndex, info.X1, info.Y1, knöpfe))
            {
                if (b is null)
                    continue;
                x1 = Math.Min(x1, b.X1); y1 = Math.Min(y1, b.Y1);
                x2 = Math.Max(x2, b.X2); y2 = Math.Max(y2, b.Y2);
            }
        }
        _gesamtBoxen.Add(feld, new GesamtBox(x1, y1, x2, y2));
        return (x1, y1, x2, y2);
    }

    private readonly ConditionalWeakTable<FormularFeld, GesamtBox> _gesamtBoxen = new();

    private sealed record GesamtBox(double X1, double Y1, double X2, double Y2);

    /// <summary>
    /// Merkt ein vorhandenes Formularfeld zum Löschen vor (wird beim Speichern aus dem
    /// Dokument entfernt) und nimmt es sofort aus der Live-Liste.
    /// </summary>
    public void FeldVorhandenLöschen(FormularFeld feld)
    {
        _zuLöschendeFelder.Add(FeldKennung.Von(feld));
        feld.PropertyChanged -= FormularfeldGeändert;
        Felder.Remove(feld);

        // Optionsfelder: Schaltflächenbereiche und eingebrannte Beschriftungen sofort
        // abdecken – beides ist (teils) Seiteninhalt und verschwände sonst nicht mit
        // dem gelöschten Feld.
        if (feld.Typ == FeldTyp.Optionsfeld)
            foreach (var (_, abdeckung) in OptionsfeldAbdeckungenFinden(feld))
                Seitenabdeckungen.Add(new SeitenAbdeckung(feld.SeitenIndex, abdeckung));

        if (!_istLaden) IstGeändert = true;
        AktuelleSeiteNeu?.Invoke();
        Status = $"Feld \"{feld.FeldName}\" wird beim Speichern entfernt.";
    }

    /// <summary>Standardgröße (in PDF-Punkten) für ein frisch angelegtes Feld je Typ.</summary>
    public static (double Breite, double Höhe) FeldStandardGröße(EntwurfFeldTyp typ) => typ switch
    {
        EntwurfFeldTyp.MehrzeiligesTextfeld => (200, 60),
        EntwurfFeldTyp.Kontrollkästchen => (15, 15),
        EntwurfFeldTyp.Optionsfeld => (120, 60),
        EntwurfFeldTyp.Dropdown => (150, 20),
        EntwurfFeldTyp.Listenfeld => (150, 72),
        EntwurfFeldTyp.Datum => (90, 18),
        EntwurfFeldTyp.Zahl => (90, 18),
        EntwurfFeldTyp.Unterschrift => (170, 40),
        _ => (170, 18) // Textfeld
    };

    /// <summary>Deutsche Bezeichnung eines Feldtyps (für Statusmeldungen).</summary>
    public static string FeldBezeichnung(EntwurfFeldTyp typ) => typ switch
    {
        EntwurfFeldTyp.Textfeld => "Textfeld",
        EntwurfFeldTyp.MehrzeiligesTextfeld => "Mehrzeiliges Textfeld",
        EntwurfFeldTyp.Kontrollkästchen => "Kontrollkästchen",
        EntwurfFeldTyp.Optionsfeld => "Optionsfeld-Gruppe",
        EntwurfFeldTyp.Dropdown => "Dropdown",
        EntwurfFeldTyp.Listenfeld => "Listenfeld",
        EntwurfFeldTyp.Datum => "Datumsfeld",
        EntwurfFeldTyp.Zahl => "Zahlenfeld",
        EntwurfFeldTyp.Unterschrift => "Unterschriftsfeld",
        _ => "Feld"
    };
}
