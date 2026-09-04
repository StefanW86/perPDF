using System.Globalization;
using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Eine Antwort auf einen Kommentar (PDF-Annotation mit <c>/IRT</c> auf den Kommentar).
/// Antworten aus anderen Programmen (z. B. Adobe Reader) werden ebenfalls eingelesen.
/// </summary>
public sealed class KommentarAntwort
{
    public string Autor { get; init; } = string.Empty;
    public DateTime Erstellt { get; init; }
    public string Inhalt { get; init; } = string.Empty;

    /// <summary>Autor + Zeitpunkt als Kopfzeile.</summary>
    public string Kopfzeile
    {
        get
        {
            string wer = string.IsNullOrWhiteSpace(Autor) ? "Unbekannt" : Autor;
            string wann = Erstellt == default
                ? string.Empty
                : Erstellt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
            return string.IsNullOrEmpty(wann) ? wer : $"{wer} · {wann}";
        }
    }
}

/// <summary>Bearbeitungsstatus eines Kommentars (auf den PDF-Reviewstatus abgebildet).</summary>
public enum KommentarStatus
{
    /// <summary>Noch nicht bearbeitet (kein Reviewstatus bzw. „None“).</summary>
    Offen,
    /// <summary>Erledigt (PDF-Reviewstatus „Completed“) – grüner Haken.</summary>
    Erledigt,
    /// <summary>Abgelehnt (PDF-Reviewstatus „Rejected“) – rotes X.</summary>
    Abgelehnt
}

/// <summary>
/// Ein PDF-Standardkommentar: eine Textmarkup-Annotation (gelbe Hervorhebung,
/// <c>/Highlight</c>) mit zugehörigem Kommentartext, Autor, Erstellungsdatum und
/// Reviewstatus. Kommentare leben – anders als die übrigen Annotationen dieser App –
/// als echte Annotationsobjekte im PDF (nicht in die Seite gebacken), damit sie in
/// anderen Betrachtern wie Adobe Reader angezeigt und bearbeitet werden können und
/// umgekehrt dort erstellte Kommentare hier erscheinen.
/// <para>
/// Die Geometrie (<see cref="Rechtecke"/>) liegt in PDF-Punkten (Ursprung unten
/// links, <c>Y1 &lt; Y2</c>) – passend zu <c>SeitenGeometrie</c>.
/// </para>
/// </summary>
public sealed class Kommentar : BeobachtbaresObjekt
{
    /// <summary>0-basierter Seitenindex, auf dem der Kommentar liegt.</summary>
    public int SeitenIndex { get; init; }

    /// <summary>
    /// PDFsharp-Objektnummer der zugehörigen Annotation im aktuell geladenen Stand.
    /// Dient als Laufzeit-Handle zum Ändern/Löschen und wird nach jeder Änderung neu
    /// eingelesen (die Objektnummern bleiben innerhalb unveränderter Bytes stabil).
    /// </summary>
    public int ObjektNummer { get; init; }

    /// <summary>
    /// Stabile Kennung (<c>/NM</c> der Annotation), die das erneute Auswählen nach dem
    /// Neuladen ermöglicht. Für selbst erstellte Kommentare eine GUID; bei fremden
    /// Annotationen ggf. leer.
    /// </summary>
    public string Kennung { get; init; } = string.Empty;

    /// <summary>Die hervorgehobenen Textbereiche (QuadPoints) in PDF-Punkten.</summary>
    public IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Rechtecke { get; init; }
        = Array.Empty<(double, double, double, double)>();

    /// <summary>Der markierte Originaltext (sofern beim Erstellen erfasst).</summary>
    public string MarkierterText { get; init; } = string.Empty;

    /// <summary>Antworten auf diesen Kommentar (zeitlich aufsteigend sortiert).</summary>
    public IReadOnlyList<KommentarAntwort> Antworten { get; init; } = Array.Empty<KommentarAntwort>();

    /// <summary>Gibt an, ob Antworten vorhanden sind (für die Datenbindung).</summary>
    public bool HatAntworten => Antworten.Count > 0;

    private string _inhalt = string.Empty;
    /// <summary>Der eigentliche Kommentartext (<c>/Contents</c>).</summary>
    public string Inhalt
    {
        get => _inhalt;
        set => SetzeWert(ref _inhalt, value);
    }

    /// <summary>Autor des Kommentars (<c>/T</c>).</summary>
    public string Autor { get; init; } = string.Empty;

    /// <summary>Erstellungszeitpunkt (<c>/CreationDate</c>).</summary>
    public DateTime Erstellt { get; init; }

    private KommentarStatus _status;
    /// <summary>Reviewstatus (Offen/Erledigt/Abgelehnt).</summary>
    public KommentarStatus Status
    {
        get => _status;
        set
        {
            if (SetzeWert(ref _status, value))
            {
                Melde(nameof(IstErledigt));
                Melde(nameof(IstAbgelehnt));
            }
        }
    }

    /// <summary>Für die Datenbindung: Haken-Schaltfläche aktiv.</summary>
    public bool IstErledigt => Status == KommentarStatus.Erledigt;

    /// <summary>Für die Datenbindung: X-Schaltfläche aktiv.</summary>
    public bool IstAbgelehnt => Status == KommentarStatus.Abgelehnt;

    /// <summary>Autor + Zeitpunkt als Kopfzeile für die Seitenleiste.</summary>
    public string Kopfzeile
    {
        get
        {
            string wer = string.IsNullOrWhiteSpace(Autor) ? "Unbekannt" : Autor;
            string wann = Erstellt == default
                ? string.Empty
                : Erstellt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
            return string.IsNullOrEmpty(wann) ? wer : $"{wer} · {wann}";
        }
    }

    /// <summary>Seitenangabe für die Seitenleiste (1-basiert).</summary>
    public string SeitenText => $"Seite {SeitenIndex + 1}";
}
