namespace PdfEditor.Models;

/// <summary>
/// Stabiler Schlüssel zum eindeutigen Wiederfinden eines vorhandenen Formularfeldes –
/// Name <b>und</b> Rechteck (in PDF-Punkten, auf 0,1 pt gerastert). Der Name allein genügt
/// nicht: manche PDFs vergeben denselben <c>/T</c>-Namen an mehrere Felder verschiedenen
/// Typs (z. B. ein Optionsfeld und ein Textfeld „Feld1"). Das Rechteck ist über
/// Seitenumsortierungen/-einfügungen hinweg stabil (anders als der Seitenindex), sodass
/// vorgemerkte Bearbeitungen/Löschungen auch nach Strukturänderungen das richtige Feld treffen.
/// </summary>
public readonly record struct FeldKennung(string Name, int X1, int Y1, int X2, int Y2)
{
    public static FeldKennung Erzeugen(string name, double x1, double y1, double x2, double y2)
        => new(name, R(x1), R(y1), R(x2), R(y2));

    public static FeldKennung Von(FormularFeld feld)
        => Erzeugen(feld.FeldName, feld.X1, feld.Y1, feld.X2, feld.Y2);

    /// <summary>Rastert eine Punktkoordinate auf 0,1 pt (toleriert Rundungsrauschen beim erneuten Lesen).</summary>
    private static int R(double v) => (int)Math.Round(v * 10);
}
