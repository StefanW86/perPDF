using System.Text.RegularExpressions;
using System.Windows.Media;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace PdfEditor.Services;

/// <summary>
/// Installierte Schriftarten und die Erkennung der im Dokument verwendeten Schrift,
/// damit eingefügter Text standardmäßig zum vorhandenen Text passt.
/// </summary>
public static class Schriftarten
{
    public const string Standard = "Arial";

    private static List<string>? _installiert;

    /// <summary>Alle installierten Schriftfamilien, alphabetisch (einmalig ermittelt).</summary>
    public static IReadOnlyList<string> Installiert => _installiert ??= Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>
    /// Ermittelt Schrift und -größe des Wortes, das <paramref name="x"/>/<paramref name="y"/>
    /// (absolute PDF-Punkte) am nächsten liegt. <c>Familie</c> ist der zugeordnete
    /// installierte Familienname oder <c>null</c>, wenn die Schrift nicht installiert ist;
    /// <c>PdfName</c> der bereinigte Name aus dem PDF. <c>null</c> = kein Text auf der Seite.
    /// </summary>
    public static (string? Familie, string PdfName, double Größe)? AnPosition(
        byte[] pdf, int seitenIndex, SeitenInfo info, double x, double y)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (seitenIndex < 0 || seitenIndex >= doc.NumberOfPages)
                return null;
            // PdfPig rechnet in Anzeige-Punkten (vgl. PdfTextDienst.NachPdf).
            var p = PdfTextDienst.NachPdfPig(info, x, y);
            double px = p.X, py = p.Y;

            var wort = doc.GetPage(seitenIndex + 1)
                .GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(w => w.Letters.Count > 0 && !string.IsNullOrWhiteSpace(w.Text))
                .MinBy(w =>
                {
                    var b = w.BoundingBox;
                    double dx = Math.Max(0, Math.Max(Math.Min(b.Left, b.Right) - px, px - Math.Max(b.Left, b.Right)));
                    double dy = Math.Max(0, Math.Max(Math.Min(b.Bottom, b.Top) - py, py - Math.Max(b.Bottom, b.Top)));
                    return dx * dx + dy * dy;
                });
            if (wort is null)
                return null;

            var größen = wort.Letters.Select(l => l.PointSize).OrderBy(p => p).ToList();
            string pdfName = NameBereinigen(wort.Letters[0].FontName ?? string.Empty);
            return (Zuordnen(pdfName), pdfName, Math.Round(größen[größen.Count / 2]));
        }
        catch
        {
            return null; // nicht lesbares Dokument → Standardschrift
        }
    }

    /// <summary>
    /// "ABCDEF+Montserrat-BoldItalic" → "Montserrat"; "TimesNewRomanPSMT" → "TimesNewRoman";
    /// "Arial,Bold" → "Arial". Untergruppen-Präfix und Schnitt-Angaben werden entfernt.
    /// </summary>
    public static string NameBereinigen(string pdfName)
    {
        string n = Regex.Replace(pdfName, @"^[A-Z]{6}\+", "");
        n = Regex.Split(n, "[-,]")[0];
        n = Regex.Replace(n, "(PSMT|PS|MT)$", "");
        return n.Trim();
    }

    /// <summary>
    /// Sucht die installierte Familie zu einem bereinigten PDF-Namen. Verglichen wird ohne
    /// Leerzeichen ("TimesNewRoman" ↔ "Times New Roman"); ein angehängter Schnitt
    /// ("MontserratBold") wird über den längsten passenden Präfix gefunden.
    /// </summary>
    public static string? Zuordnen(string bereinigt)
    {
        string schlüssel = Kompakt(bereinigt);
        if (schlüssel.Length == 0)
            return null;
        return Installiert
            .Where(f => schlüssel.StartsWith(Kompakt(f), StringComparison.OrdinalIgnoreCase))
            .MaxBy(f => Kompakt(f).Length);
    }

    private static string Kompakt(string s) => s.Replace(" ", "");
}
