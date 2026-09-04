namespace PdfEditor.Services;

/// <summary>
/// Leichtgewichtige Geometrieinformationen einer PDF-Seite (ohne Live-Referenz
/// auf das Dokument). Dient als Grundlage für die Koordinatenumrechnung.
/// </summary>
/// <param name="X1">X-Ursprung der MediaBox in Punkten.</param>
/// <param name="Y1">Y-Ursprung der MediaBox in Punkten.</param>
/// <param name="Breite">Seitenbreite in Punkten (ungedreht).</param>
/// <param name="Höhe">Seitenhöhe in Punkten (ungedreht).</param>
/// <param name="DrehungGrad">Seitendrehung in Grad (0, 90, 180 oder 270).</param>
public record SeitenInfo(double X1, double Y1, double Breite, double Höhe, int DrehungGrad);
