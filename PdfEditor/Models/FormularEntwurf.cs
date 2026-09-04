using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>Die im Formular-Designer erstellbaren Feldtypen.</summary>
public enum EntwurfFeldTyp
{
    Textfeld,
    MehrzeiligesTextfeld,
    Kontrollkästchen,
    Optionsfeld,
    Dropdown,
    Listenfeld,
    Datum,
    Zahl,
    Unterschrift
}

/// <summary>Textausrichtung innerhalb eines Feldes (entspricht dem PDF-Schlüssel /Q).</summary>
public enum TextAusrichtung
{
    Links = 0,
    Mitte = 1,
    Rechts = 2
}

/// <summary>
/// Eingabeformat/-prüfung eines Feldes. Wird beim Speichern als Acrobat-JavaScript
/// hinterlegt; die Auswertung hängt vom Betrachter ab (Acrobat/Reader werten es aus).
/// </summary>
public enum FeldFormat
{
    Keiner,
    Email,
    PLZ,
    Telefon,
    Datum,
    Zahl
}

/// <summary>
/// Abdeck-Rechteck über der im Seiteninhalt eingebrannten Original-Beschriftung einer
/// bearbeiteten Optionsfeld-Gruppe (PDF-Punkte, Ursprung unten links) samt der vom
/// gerenderten Seitenbild abgetasteten Hintergrundfarbe (#RRGGBB). Wird beim Speichern
/// vor den neuen Beschriftungen gefüllt und im Editor als Vorschau angezeigt.
/// </summary>
public sealed record BeschriftungsAbdeckung(double X1, double Y1, double X2, double Y2, string Farbe);

/// <summary>
/// Entwurf eines neu anzulegenden Formularfeldes. Im Gegensatz zum schreibgeschützten
/// <see cref="FormularFeld"/> (das vorhandene Felder zum Ausfüllen abbildet) sind hier
/// alle Eigenschaften veränderbar, da das Feld im Designer verschoben, in der Größe
/// geändert und konfiguriert wird. Geometrie in PDF-Punkten (Ursprung unten links,
/// ungedreht); beim Speichern wird daraus ein echtes AcroForm-Feld gebacken.
/// </summary>
public class FormularEntwurf : BeobachtbaresObjekt, IAufSeite
{
    public FormularEntwurf(EntwurfFeldTyp typ, int seitenIndex)
    {
        _typ = typ;
        _seitenIndex = seitenIndex;
    }

    private int _seitenIndex;
    public int SeitenIndex
    {
        get => _seitenIndex;
        set => SetzeWert(ref _seitenIndex, value);
    }

    private EntwurfFeldTyp _typ;
    /// <summary>Der Feldtyp (bestimmt das gebackene AcroForm-Feld und das Overlay-Control).</summary>
    public EntwurfFeldTyp Typ
    {
        get => _typ;
        set => SetzeWert(ref _typ, value);
    }

    private string _feldName = string.Empty;
    /// <summary>Eindeutiger Feldname (/T) – zum Ausfüllen und Wiederfinden des Feldes.</summary>
    public string FeldName
    {
        get => _feldName;
        set => SetzeWert(ref _feldName, value);
    }

    // ----- Geometrie in PDF-Punkten (untere linke Ecke + Größe) -----

    private double _x;
    public double X { get => _x; set => SetzeWert(ref _x, value); }

    private double _y;
    public double Y { get => _y; set => SetzeWert(ref _y, value); }

    private double _breite;
    public double Breite { get => _breite; set => SetzeWert(ref _breite, value); }

    private double _höhe;
    public double Höhe { get => _höhe; set => SetzeWert(ref _höhe, value); }

    // ----- Allgemeine Eigenschaften -----

    private string _standardwert = string.Empty;
    /// <summary>Vorbelegter Wert (/DV und /V) bzw. bei Auswahl die vorausgewählte Option.</summary>
    public string Standardwert
    {
        get => _standardwert;
        set => SetzeWert(ref _standardwert, value);
    }

    private bool _pflichtfeld;
    /// <summary>Muss ausgefüllt werden (Flag Required).</summary>
    public bool Pflichtfeld
    {
        get => _pflichtfeld;
        set => SetzeWert(ref _pflichtfeld, value);
    }

    private bool _schreibgeschützt;
    /// <summary>Nicht änderbar (Flag ReadOnly) – z. B. für vorbelegte Hinweise.</summary>
    public bool Schreibgeschützt
    {
        get => _schreibgeschützt;
        set => SetzeWert(ref _schreibgeschützt, value);
    }

    private string _quickInfo = string.Empty;
    /// <summary>QuickInfo/Tooltip (/TU), wird beim Überfahren angezeigt.</summary>
    public string QuickInfo
    {
        get => _quickInfo;
        set => SetzeWert(ref _quickInfo, value);
    }

    private TextAusrichtung _ausrichtung = TextAusrichtung.Links;
    /// <summary>Textausrichtung im Feld (/Q).</summary>
    public TextAusrichtung Ausrichtung
    {
        get => _ausrichtung;
        set => SetzeWert(ref _ausrichtung, value);
    }

    private double _schriftgröße = 11;
    /// <summary>Schriftgröße in Punkten (0 = automatisch).</summary>
    public double Schriftgröße
    {
        get => _schriftgröße;
        set => SetzeWert(ref _schriftgröße, value);
    }

    private string _textFarbe = "#000000";
    /// <summary>Textfarbe als Hex-Wert (#RRGGBB).</summary>
    public string TextFarbe
    {
        get => _textFarbe;
        set => SetzeWert(ref _textFarbe, value);
    }

    private string _rahmenFarbe = "#1C2B2A";
    /// <summary>Rahmenfarbe als Hex-Wert (#RRGGBB); leer = kein Rahmen.</summary>
    public string RahmenFarbe
    {
        get => _rahmenFarbe;
        set => SetzeWert(ref _rahmenFarbe, value);
    }

    private string _hintergrundFarbe = string.Empty;
    /// <summary>Hintergrundfarbe als Hex-Wert (#RRGGBB); leer = transparent.</summary>
    public string HintergrundFarbe
    {
        get => _hintergrundFarbe;
        set => SetzeWert(ref _hintergrundFarbe, value);
    }

    // ----- Textfeld-spezifisch -----

    private int? _maxZeichen;
    /// <summary>Maximale Zeichenanzahl (/MaxLen); null = unbegrenzt. Nur Textfeld.</summary>
    public int? MaxZeichen
    {
        get => _maxZeichen;
        set => SetzeWert(ref _maxZeichen, value);
    }

    private bool _alsKästchen;
    /// <summary>Comb-Feld: gleichmäßig verteilte Kästchen je Zeichen (benötigt <see cref="MaxZeichen"/>).</summary>
    public bool AlsKästchen
    {
        get => _alsKästchen;
        set => SetzeWert(ref _alsKästchen, value);
    }

    private FeldFormat _format = FeldFormat.Keiner;
    /// <summary>Eingabeformat/-prüfung (als Betrachter-JavaScript hinterlegt). Nur Textfeld.</summary>
    public FeldFormat Format
    {
        get => _format;
        set => SetzeWert(ref _format, value);
    }

    // ----- Auswahl-/Optionsfeld-spezifisch -----

    /// <summary>Auswahlmöglichkeiten für Dropdown, Listenfeld und Optionsfeld-Gruppe.</summary>
    public List<string> Optionen { get; } = new();

    /// <summary>
    /// Nur aus einem vorhandenen Feld bearbeitete Optionsfeld-Gruppen: je Option das
    /// Abdeck-Rechteck ihrer Original-Beschriftung, positionsgleich zu <see cref="Optionen"/>
    /// (<c>null</c>, wo keine Beschriftung erkannt wurde – dort bleibt das Original stehen
    /// und das Feld zeichnet keine neue). Für alle übrigen Optionen (neue Felder, erkannte
    /// oder nachträglich ergänzte Optionen) zeichnet das Feld die Beschriftung beim
    /// Speichern selbst – Textänderungen wirken damit direkt auf das Dokument.
    /// </summary>
    public List<BeschriftungsAbdeckung?> Abdeckungen { get; } = new();

    private bool _mehrfachAuswahl;
    /// <summary>Mehrfachauswahl erlauben (nur Listenfeld, Flag MultiSelect).</summary>
    public bool MehrfachAuswahl
    {
        get => _mehrfachAuswahl;
        set => SetzeWert(ref _mehrfachAuswahl, value);
    }

    /// <summary>
    /// Ob die Beschriftung der Option <paramref name="index"/> beim Speichern (und in der
    /// Vorschau) vom Feld selbst gezeichnet wird: immer, außer die Original-Beschriftung
    /// eines bearbeiteten Feldes wurde nicht erkannt (dann bleibt sie unabgedeckt stehen
    /// und eine neue daneben wäre doppelt).
    /// </summary>
    public bool BeschriftungZeichnen(int index) =>
        index >= Abdeckungen.Count || Abdeckungen[index] is not null;
}
