using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>Unterscheidet die unterstützten Formularfeld-Typen.</summary>
public enum FeldTyp
{
    Text,
    Kontrollkästchen,
    Auswahlliste,
    Optionsfeld
}

/// <summary>
/// Eine einzelne Schaltfläche einer Optionsfeld-Gruppe (Radio): ihr Exportwert
/// und ihr Widget-Rechteck in PDF-Punkten (Ursprung unten links).
/// </summary>
public record Optionsschaltfläche(string Export, double X1, double Y1, double X2, double Y2);

/// <summary>
/// Beschreibt ein ausfüllbares Formularfeld einer PDF: Typ, vollständiger
/// Feldname, Position (in PDF-Punkten, Ursprung unten links) und der vom
/// Benutzer eingegebene Wert. Es werden nur Werte gespeichert, keine
/// Live-Referenzen auf das PDF, da diese nach dem Speichern ungültig würden.
/// </summary>
public class FormularFeld : BeobachtbaresObjekt
{
    public FormularFeld(string feldName, FeldTyp typ, int seitenIndex,
        double x1, double y1, double x2, double y2)
    {
        FeldName = feldName;
        Typ = typ;
        SeitenIndex = seitenIndex;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>Vollständiger Feldname (zum Wiederfinden des Feldes beim Speichern).</summary>
    public string FeldName { get; }

    public FeldTyp Typ { get; }

    /// <summary>0-basierter Index der Seite, auf der das Feld liegt.</summary>
    public int SeitenIndex { get; }

    // Feldrechteck in PDF-Punkten (untere linke und obere rechte Ecke).
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }

    /// <summary>Auswahlmöglichkeiten bei Auswahllisten.</summary>
    public List<string> Optionen { get; } = new();

    /// <summary>
    /// Die einzelnen Schaltflächen einer Optionsfeld-Gruppe (Radio). Der ausgewählte
    /// Exportwert steht in <see cref="Wert"/> (leer = keine Auswahl).
    /// </summary>
    public List<Optionsschaltfläche> Optionsschaltflächen { get; } = new();

    private string _wert = string.Empty;
    /// <summary>Der eingegebene bzw. ausgewählte Textwert (für Text- und Auswahlfelder).</summary>
    public string Wert
    {
        get => _wert;
        set => SetzeWert(ref _wert, value);
    }

    private bool _istAngehakt;
    /// <summary>Der Zustand eines Kontrollkästchens.</summary>
    public bool IstAngehakt
    {
        get => _istAngehakt;
        set => SetzeWert(ref _istAngehakt, value);
    }
}
