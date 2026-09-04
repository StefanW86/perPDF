using System.Windows.Media;
using PdfEditor.ViewModels;

namespace PdfEditor.Models;

/// <summary>
/// Stellt eine einzelne PDF-Seite in der Miniaturansicht-Liste dar.
/// Die Position des Elements in der Liste entspricht dem Seitenindex; das
/// eigentliche Dokument wird über Indizes angesprochen (siehe PdfDokumentDienst).
/// </summary>
public class SeitenElement : BeobachtbaresObjekt
{
    private ImageSource? _vorschau;
    /// <summary>Das gerenderte Miniaturbild der Seite.</summary>
    public ImageSource? Vorschau
    {
        get => _vorschau;
        set => SetzeWert(ref _vorschau, value);
    }

    private int _nummer;
    /// <summary>Die 1-basierte Anzeigenummer der Seite in der aktuellen Reihenfolge.</summary>
    public int Nummer
    {
        get => _nummer;
        set => SetzeWert(ref _nummer, value);
    }
}
