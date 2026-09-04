namespace PdfEditor.Models;

/// <summary>
/// Ausstehende Abdeckung auf einer Seite: verdeckt die im Seiteninhalt eingebrannte
/// Beschriftung eines gelöschten Optionsfeldes. Im Editor ein rein passives farbiges
/// Rechteck über dem Seitenbild (nicht anklick- oder verschiebbar); beim Speichern
/// wird sie dauerhaft in den Seiteninhalt gezeichnet.
/// </summary>
public class SeitenAbdeckung : IAufSeite
{
    public SeitenAbdeckung(int seitenIndex, BeschriftungsAbdeckung bereich)
    {
        SeitenIndex = seitenIndex;
        Bereich = bereich;
    }

    public int SeitenIndex { get; set; }

    /// <summary>Rechteck (PDF-Punkte, Ursprung unten links) und Hintergrundfarbe.</summary>
    public BeschriftungsAbdeckung Bereich { get; }
}
