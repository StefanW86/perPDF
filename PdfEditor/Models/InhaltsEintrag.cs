namespace PdfEditor.Models;

/// <summary>
/// Repräsentiert einen Eintrag im Inhaltsverzeichnis (PDF-Outline / Lesezeichen).
/// </summary>
public class InhaltsEintrag
{
    /// <summary>Anzeigetitel des Eintrags.</summary>
    public string Titel { get; set; } = "";

    /// <summary>0-basierter Seitenindex des Ziels; -1 wenn unbekannt.</summary>
    public int SeitenIndex { get; set; } = -1;

    /// <summary>Einrückungstiefe: 0 = oberste Ebene.</summary>
    public int Tiefe { get; set; } = 0;

    /// <summary>Untergeordnete Einträge (für hierarchische Gliederungen).</summary>
    public List<InhaltsEintrag> Kinder { get; set; } = new();

    /// <summary>Gibt an, ob der Knoten im TOC-Baum aufgeklappt ist.</summary>
    public bool IstAufgeklappt { get; set; } = true;
}
