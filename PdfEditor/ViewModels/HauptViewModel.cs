using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>
/// Zentrales ViewModel der Anwendung. Hält den Dokumentzustand, die
/// Formularfelder, die Unterschriften-Galerie und die Befehle der Symbolleiste.
/// Das Hauptfenster zeichnet auf Anforderung die aktuelle Seite samt Overlays.
/// </summary>
public partial class HauptViewModel : BeobachtbaresObjekt
{
    private readonly PdfDokumentDienst _dienst = new();
    private readonly UnterschriftSpeicher _speicher = new();
    private readonly PdfTextDienst _textDienst = new();

    public HauptViewModel()
    {
        // Befehle einrichten.
        ÖffnenBefehl = new RelayCommand(Öffnen);
        SpeichernBefehl = new RelayCommand(() => Speichern(false), () => _dienst.HatDokument);
        SpeichernUnterBefehl = new RelayCommand(() => Speichern(true), () => _dienst.HatDokument);
        DruckenBefehl = new RelayCommand(Drucken, () => _dienst.HatDokument);
        SeiteHinzufügenBefehl = new RelayCommand(SeiteHinzufügen);
        SeiteImportierenBefehl = new RelayCommand(SeitenImportieren, () => _dienst.HatDokument);
        SeiteLöschenBefehl = new RelayCommand(SeiteLöschen, () => _dienst.HatDokument && Seiten.Count > 1);
        RechtsDrehenBefehl = new RelayCommand(() => Drehen(90), () => _dienst.HatDokument);
        LinksDrehenBefehl = new RelayCommand(() => Drehen(-90), () => _dienst.HatDokument);
        ZoomPlusBefehl = new RelayCommand(() => ZoomÄndern(0.25), () => _dienst.HatDokument);
        ZoomMinusBefehl = new RelayCommand(() => ZoomÄndern(-0.25), () => _dienst.HatDokument);
        UnterschriftHinzufügenBefehl = new RelayCommand(UnterschriftHinzufügen);
        TextEinfügenBefehl = new RelayCommand(TextEinfügen, () => _dienst.HatDokument);
        BildEinfügenBefehl = new RelayCommand(BildEinfügen, () => _dienst.HatDokument);

        // Gespeicherte Unterschriften laden.
        foreach (var u in _speicher.Laden())
            Unterschriften.Add(u);
    }

    // ----- Sammlungen -------------------------------------------------------

    public ObservableCollection<SeitenElement> Seiten { get; } = new();
    public ObservableCollection<FormularFeld> Felder { get; } = new();
    public ObservableCollection<Unterschrift> Unterschriften { get; } = new();
    public ObservableCollection<UnterschriftPlatzierung> Platzierungen { get; } = new();
    public ObservableCollection<TextNotiz> Textnotizen { get; } = new();
    public ObservableCollection<Textmarkierung> Textmarkierungen { get; } = new();
    public ObservableCollection<Symbolmarkierung> Symbolmarkierungen { get; } = new();

    /// <summary>Im Formular-Designer neu angelegte Felder (werden beim Speichern ins PDF gebacken).</summary>
    public ObservableCollection<FormularEntwurf> Entwurfsfelder { get; } = new();

    /// <summary>
    /// Kennungen (Name + Geometrie) vorhandener Formularfelder, die beim Speichern aus dem
    /// Dokument entfernt werden – weil sie gelöscht oder (zum Bearbeiten) durch einen
    /// gleichnamigen Entwurf in <see cref="Entwurfsfelder"/> ersetzt wurden. Solange ein Feld
    /// hier steht, erscheint es nicht mehr als ausfüllbares Feld (siehe FelderNeuLaden). Die
    /// Geometrie statt nur des Namens als Schlüssel, weil PDFs Namen mehrfach vergeben können.
    /// </summary>
    private readonly HashSet<FeldKennung> _zuLöschendeFelder = new();

    /// <summary>Die im Dokument vorhandenen PDF-Kommentare (Standard-Annotationen).</summary>
    public ObservableCollection<Kommentar> Kommentare { get; } = new();

    /// <summary>Autor für neu erstellte Kommentare (Windows-Anmeldename).</summary>
    private readonly string _autor =
        string.IsNullOrWhiteSpace(Environment.UserName) ? "Benutzer" : Environment.UserName;

    private bool _istLaden;

    private bool _istGeändert;
    /// <summary>Gibt an, ob das Dokument seit dem letzten Speichern geändert wurde.</summary>
    public bool IstGeändert
    {
        get => _istGeändert;
        private set
        {
            if (SetzeWert(ref _istGeändert, value))
            {
                Melde(nameof(Titel));
                Melde(nameof(SpeicherHinweis));
            }
        }
    }

    /// <summary>Hinweistext in der Statusleiste bei ungespeicherten Änderungen (sonst leer).</summary>
    public string SpeicherHinweis => IstGeändert ? "● Nicht gespeicherte Änderungen" : string.Empty;

    private List<SeitenInfo> _seitenInfos = new();
    public IReadOnlyList<SeitenInfo> SeitenInfos => _seitenInfos;

    /// <summary>Aktuelle Dokument-Bytes (für das Rendern der Seiten).</summary>
    public byte[]? Bytes => _dienst.AktuelleBytes;

    /// <summary>Aktueller Dateipfad des geladenen Dokuments (oder <c>null</c> bei neuem Dokument).</summary>
    public string? Pfad => _dienst.Pfad;

    // ----- Befehle ----------------------------------------------------------

    public RelayCommand ÖffnenBefehl { get; }
    public RelayCommand SpeichernBefehl { get; }
    public RelayCommand SpeichernUnterBefehl { get; }
    public RelayCommand DruckenBefehl { get; }
    public RelayCommand SeiteHinzufügenBefehl { get; }
    public RelayCommand SeiteImportierenBefehl { get; }
    public RelayCommand SeiteLöschenBefehl { get; }
    public RelayCommand RechtsDrehenBefehl { get; }
    public RelayCommand LinksDrehenBefehl { get; }
    public RelayCommand ZoomPlusBefehl { get; }
    public RelayCommand ZoomMinusBefehl { get; }
    public RelayCommand UnterschriftHinzufügenBefehl { get; }
    public RelayCommand TextEinfügenBefehl { get; }

    // ----- Ereignisse an die Ansicht ---------------------------------------

    /// <summary>Fordert das vollständige Neuaufbauen (Miniaturen + aktuelle Seite) an.</summary>
    public event Action? AnsichtKomplettNeu;

    /// <summary>Fordert das Neuzeichnen nur der aktuellen Seite samt Overlays an.</summary>
    public event Action? AktuelleSeiteNeu;

    /// <summary>
    /// Wird ausgelöst, wenn sich der Ladestatus ändert.
    /// Parameter: (sichtbar, Ladetext) – sichtbar=true zeigt das Overlay, false versteckt es.
    /// </summary>
    public event Action<bool, string>? LadestatusGeändert;

    /// <summary>
    /// Wird während des Thumbnail-Aufbaus seitenweise ausgelöst.
    /// Parameter: (aktuelleSeite, gesamtSeiten)
    /// </summary>
    public event Action<int, int>? SeiteGerendert;

    // ----- Gebundene Eigenschaften -----------------------------------------

    private int _aktuelleSeite = -1;
    /// <summary>0-basierter Index der angezeigten Seite (an die Auswahl gebunden).</summary>
    public int AktuelleSeite
    {
        get => _aktuelleSeite;
        set
        {
            if (SetzeWert(ref _aktuelleSeite, value))
            {
                Melde(nameof(SeitenStatus));
                AktuelleSeiteNeu?.Invoke();
            }
        }
    }

    private double _zoom = 1.0;
    public double Zoom
    {
        get => _zoom;
        private set
        {
            if (SetzeWert(ref _zoom, value))
                Melde(nameof(ZoomText));
        }
    }

    public string ZoomText => $"{Zoom * 100:0} %";

    private string _status = "Bereit – öffnen Sie eine PDF-Datei oder fügen Sie eine Seite hinzu.";
    public string Status
    {
        get => _status;
        set => SetzeWert(ref _status, value);
    }

    public string Titel
    {
        get
        {
            string datei = _dienst.Pfad is null ? "Ohne Titel" : Path.GetFileName(_dienst.Pfad);
            string marker = IstGeändert ? "*" : string.Empty;
            return $"{marker}{datei} – perPDF";
        }
    }

    public string SeitenStatus =>
        Seiten.Count == 0 ? "Keine Seite" : $"Seite {AktuelleSeite + 1} von {Seiten.Count}";

    /// <summary>Gibt an, ob ein Dokument geladen ist (für Schaltflächen-Zustände).</summary>
    public bool HatDokument => _dienst.HatDokument;

    private bool _kommentareSichtbar;
    /// <summary>Steuert die Sichtbarkeit der Kommentar-Seitenleiste.</summary>
    public bool KommentareSichtbar
    {
        get => _kommentareSichtbar;
        set => SetzeWert(ref _kommentareSichtbar, value);
    }

    // ----- Inhaltsverzeichnis (TOC) ----------------------------------------

    /// <summary>Alle Gliederungseinträge des aktuellen Dokuments (leer wenn keines geladen oder kein TOC vorhanden).</summary>
    public List<Models.InhaltsEintrag> Inhaltsverzeichnis { get; private set; } = new();

    /// <summary>Gibt an, ob das Dokument ein Inhaltsverzeichnis (Outlines) besitzt.</summary>
    public bool HatInhaltsverzeichnis => Inhaltsverzeichnis.Count > 0;

    private bool _zeigeInhaltsverzeichnis;
    /// <summary>
    /// Steuert, was die linke Seitenleiste zeigt:
    /// <c>true</c> = Inhaltsverzeichnis, <c>false</c> = Seitenvorschauen (Thumbnails).
    /// </summary>
    public bool ZeigeInhaltsverzeichnis
    {
        get => _zeigeInhaltsverzeichnis;
        set => SetzeWert(ref _zeigeInhaltsverzeichnis, value);
    }

    /// <summary>Springt zur angegebenen Seite (0-basierter Index).</summary>
    public void ZuSeiteSpringen(int seitenIndex)
    {
        if (seitenIndex >= 0 && seitenIndex < Seiten.Count)
            AktuelleSeite = seitenIndex;
    }

    private Kommentar? _entwurf;
    /// <summary>
    /// Noch nicht gespeicherter Kommentar-Entwurf (Texteingabe in der Seitenleiste).
    /// Trägt bereits die markierten Bereiche, damit Hervorhebung und Verbindungslinie
    /// sofort sichtbar sind.
    /// </summary>
    public Kommentar? Entwurf
    {
        get => _entwurf;
        private set
        {
            if (SetzeWert(ref _entwurf, value))
                Melde(nameof(HatEntwurf));
        }
    }

    /// <summary>Gibt an, ob gerade ein Kommentar-Entwurf bearbeitet wird.</summary>
    public bool HatEntwurf => _entwurf is not null;

    private Kommentar? _ausgewählterKommentar;
    /// <summary>
    /// Der in der Seitenleiste ausgewählte Kommentar. Beim Auswählen wird zur Seite des
    /// Kommentars geblättert und die Verbindungslinie gezeichnet.
    /// </summary>
    public Kommentar? AusgewählterKommentar
    {
        get => _ausgewählterKommentar;
        set => KommentarAuswählen(value);
    }

    private bool _markerModus;
    /// <summary>
    /// Gibt an, ob der Leuchtstift-Zeichenmodus aktiv ist. Solange er aktiv ist,
    /// zeichnet ein Mausziehen auf der Seite einen Markierungsstrich.
    /// </summary>
    public bool MarkerModus
    {
        get => _markerModus;
        set
        {
            if (value && !HatDokument)
            {
                Melde(nameof(MarkerModus)); // gebundene Schaltfläche zurücksetzen
                return;
            }
            if (SetzeWert(ref _markerModus, value) && value)
            {
                RadiererModus = false;
                FelderBearbeitenModus = false;
                EinfügeModusBeenden();
                FeldModusBeenden();
                Status = "Leuchtstift aktiv – mit der Maus über den Text ziehen. Erneut klicken oder Esc zum Beenden.";
            }
        }
    }

    private bool _radiererModus;
    /// <summary>
    /// Gibt an, ob der Radiergummi aktiv ist. Ein Klick bzw. Ziehen über einen
    /// Markierungsstrich entfernt diesen.
    /// </summary>
    public bool RadiererModus
    {
        get => _radiererModus;
        set
        {
            if (value && !HatDokument)
            {
                Melde(nameof(RadiererModus));
                return;
            }
            if (SetzeWert(ref _radiererModus, value) && value)
            {
                MarkerModus = false;
                FelderBearbeitenModus = false;
                EinfügeModusBeenden();
                FeldModusBeenden();
                Status = "Radiergummi aktiv – über einen Markierungsstrich ziehen, um ihn zu entfernen. Esc beendet.";
            }
        }
    }

    private bool _felderBearbeitenModus;
    /// <summary>
    /// Gibt an, ob der Modus zum Bearbeiten vorhandener Formularfelder aktiv ist. Solange er
    /// aktiv ist, werden ausfüllbare Felder als auswählbare Felder dargestellt: Anklicken
    /// übernimmt das Feld zur Bearbeitung (verschieben/skalieren/Eigenschaften), das ✕ bzw.
    /// die Entf-Taste markiert es zum Löschen. Gegenseitig ausschließend mit Marker/Radierer.
    /// </summary>
    public bool FelderBearbeitenModus
    {
        get => _felderBearbeitenModus;
        set
        {
            if (value && !HatDokument)
            {
                Melde(nameof(FelderBearbeitenModus)); // gebundene Schaltfläche zurücksetzen
                return;
            }
            if (SetzeWert(ref _felderBearbeitenModus, value))
            {
                if (value)
                {
                    MarkerModus = false;
                    RadiererModus = false;
                    EinfügeModusBeenden();
                    FeldModusBeenden();
                    Status = "Felder bearbeiten – vorhandenes Feld anklicken zum Verschieben/Skalieren/Ändern; ✕ oder Entf löscht. Esc beendet.";
                }
                else
                {
                    AusgewähltesFeld = null;
                    // Beim Verlassen des Modus alle ausstehenden Feldänderungen sofort ins
                    // Dokument übernehmen – die Felder sind danach normal ausfüllbar.
                    AusstehendeFelderEinbacken();
                }
                // Das Hauptfenster zeichnet über ModusAktualisieren neu (wie bei Marker/Radierer).
            }
        }
    }

    private EinfügeArt? _einfügeModus;
    /// <summary>
    /// Aktiver Einfügemodus: Sobald gesetzt, wird beim nächsten Klick auf die Seite
    /// das entsprechende Objekt platziert. <c>null</c> bedeutet kein Modus aktiv.
    /// </summary>
    public EinfügeArt? EinfügeModus
    {
        get => _einfügeModus;
        private set
        {
            if (SetzeWert(ref _einfügeModus, value) && value is not null)
                FeldModusBeenden();
        }
    }

    private EntwurfFeldTyp? _neuesFeldTyp;
    /// <summary>
    /// Aktiver Formularfeld-Platziermodus: Sobald gesetzt, wird beim nächsten Klick auf
    /// die Seite ein neues Feld dieses Typs angelegt. <c>null</c> = kein Modus aktiv.
    /// </summary>
    public EntwurfFeldTyp? NeuesFeldTyp
    {
        get => _neuesFeldTyp;
        private set => SetzeWert(ref _neuesFeldTyp, value);
    }

    private FormularEntwurf? _ausgewähltesFeld;
    /// <summary>Das im Designer ausgewählte Entwurfsfeld – steuert die Eigenschaften-Seitenleiste.</summary>
    public FormularEntwurf? AusgewähltesFeld
    {
        get => _ausgewähltesFeld;
        set
        {
            if (SetzeWert(ref _ausgewähltesFeld, value))
            {
                Melde(nameof(HatAusgewähltesFeld));
                Melde(nameof(AusgewähltesFeld));
                Melde(nameof(FeldOptionenText));
                Melde(nameof(FeldMaxZeichenText));
                Melde(nameof(FeldHatOptionen));
                Melde(nameof(FeldIstListenfeld));
                Melde(nameof(FeldIstText));
                Melde(nameof(FeldFormatRelevant));
            }
        }
    }

    /// <summary>Gibt an, ob aktuell ein Entwurfsfeld ausgewählt ist (Sichtbarkeit der Seitenleiste).</summary>
    public bool HatAusgewähltesFeld => _ausgewähltesFeld is not null;

    /// <summary>Hat das ausgewählte Feld Auswahlmöglichkeiten (Dropdown/Liste/Optionsfeld)?</summary>
    public bool FeldHatOptionen => _ausgewähltesFeld?.Typ
        is EntwurfFeldTyp.Dropdown or EntwurfFeldTyp.Listenfeld or EntwurfFeldTyp.Optionsfeld;

    /// <summary>Ist das ausgewählte Feld ein Listenfeld (zeigt die Mehrfachauswahl-Option)?</summary>
    public bool FeldIstListenfeld => _ausgewähltesFeld?.Typ == EntwurfFeldTyp.Listenfeld;

    /// <summary>Ist das ausgewählte Feld ein Textfeld-Typ (zeigt max. Zeichen/Comb)?</summary>
    public bool FeldIstText => _ausgewähltesFeld?.Typ
        is EntwurfFeldTyp.Textfeld or EntwurfFeldTyp.MehrzeiligesTextfeld
        or EntwurfFeldTyp.Datum or EntwurfFeldTyp.Zahl;

    /// <summary>Ist für das ausgewählte Feld die Format-/Prüf-Auswahl relevant (einfaches Textfeld)?</summary>
    public bool FeldFormatRelevant => _ausgewähltesFeld?.Typ == EntwurfFeldTyp.Textfeld;

    /// <summary>Verfügbare Ausrichtungen für die Auswahl in der Seitenleiste.</summary>
    public Array AusrichtungWerte => Enum.GetValues(typeof(TextAusrichtung));

    /// <summary>Verfügbare Eingabeformate für die Auswahl in der Seitenleiste.</summary>
    public Array FormatWerte => Enum.GetValues(typeof(FeldFormat));

    private SymbolArt _ausstehendesSymbol;
    /// <summary>Beim nächsten Klick einzufügendes Symbol (nur gültig wenn EinfügeModus == Symbol).</summary>
    public SymbolArt AusstehendesSymbol => _ausstehendesSymbol;

    private Unterschrift? _ausstehenderUnterschrift;
    /// <summary>Beim nächsten Klick einzufügende Unterschrift (nur gültig wenn EinfügeModus == Unterschrift).</summary>
    public Unterschrift? AusstehenderUnterschrift => _ausstehenderUnterschrift;

    /// <summary>Beendet den Einfügemodus ohne ein Objekt zu platzieren.</summary>
    public void EinfügeModusBeenden()
    {
        if (_einfügeModus is null) return;
        _ausstehenderUnterschrift = null;
        _ausstehendesBild = null;
        EinfügeModus = null;
    }

    /// <summary>Ändert den Zoom in Schritten (für Strg+Mausrad).</summary>
    public void ZoomDurchRad(int radDelta) => ZoomÄndern(radDelta > 0 ? 0.1 : -0.1);

    /// <summary>
    /// Zuletzt eingefügte Textnotiz – das Hauptfenster setzt den Eingabefokus
    /// darauf und löscht den Verweis anschließend wieder.
    /// </summary>
    public TextNotiz? ZuletztEingefügteNotiz { get; set; }

    private Unterschrift? _zuEinfügendeUnterschrift;
    /// <summary>
    /// An das Dropdown in der Symbolleiste gebunden. Sobald eine Unterschrift
    /// ausgewählt wird, fügt sie sich automatisch in die aktuelle Seite ein;
    /// danach wird die Auswahl zurückgesetzt, damit dieselbe Unterschrift erneut
    /// gewählt werden kann.
    /// </summary>
    public Unterschrift? ZuEinfügendeUnterschrift
    {
        get => _zuEinfügendeUnterschrift;
        set
        {
            if (SetzeWert(ref _zuEinfügendeUnterschrift, value) && value is not null)
            {
                UnterschriftEinfügen(value);
                _zuEinfügendeUnterschrift = null;
                Melde(nameof(ZuEinfügendeUnterschrift));
            }
        }
    }
}
