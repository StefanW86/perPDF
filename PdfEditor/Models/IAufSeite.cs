namespace PdfEditor.Models;

/// <summary>
/// Gemeinsame Schnittstelle für alle Elemente, die einer bestimmten Seite
/// zugeordnet sind (Unterschriften, Textnotizen, Textmarkierungen). Erlaubt das
/// einheitliche Verschieben der Seitenzuordnung bei Strukturänderungen.
/// </summary>
public interface IAufSeite
{
    /// <summary>0-basierter Index der Seite, auf der das Element liegt.</summary>
    int SeitenIndex { get; set; }
}
