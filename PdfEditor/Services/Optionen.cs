using System.IO;
using System.Text.Json;

namespace PdfEditor.Services;

/// <summary>Vom Nutzer im Optionsdialog festgelegte Einstellungen.</summary>
public class OptionenDaten
{
    /// <summary>Ordner, in dem neue Scan-Dokumente angelegt werden.</summary>
    public string ScanOrdner { get; set; } = Optionen.StandardScanOrdner;
    /// <summary>Dateinamen-Vorlage mit den Platzhaltern {Datum} und {Zeit}.</summary>
    public string ScanDateiname { get; set; } = Optionen.StandardScanDateiname;
}

/// <summary>
/// Liest und schreibt die Optionen als JSON unter <c>%AppData%\Roaming\perPDF\optionen.json</c>
/// (Muster wie <see cref="FensterEinstellungen"/>) und bildet daraus den Pfad neuer Scans.
/// </summary>
public static class Optionen
{
    public const string StandardScanDateiname = "SCAN_{Datum}_{Zeit}";

    public static string StandardScanOrdner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");

    private static string Pfad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "perPDF", "optionen.json");

    /// <summary>Lädt die Optionen; bei fehlender/beschädigter Datei die Standardwerte.</summary>
    public static OptionenDaten Laden()
    {
        try
        {
            return File.Exists(Pfad)
                ? JsonSerializer.Deserialize<OptionenDaten>(File.ReadAllText(Pfad)) ?? new OptionenDaten()
                : new OptionenDaten();
        }
        catch
        {
            return new OptionenDaten();
        }
    }

    /// <summary>Speichert die Optionen; wirft bei Fehlern (der Dialog meldet sie).</summary>
    public static void Speichern(OptionenDaten daten)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
        File.WriteAllText(Pfad, JsonSerializer.Serialize(daten));
    }

    /// <summary>
    /// Bildet aus der Vorlage einen gültigen Dateinamen mit Endung .pdf:
    /// {Datum} → yyyy-MM-dd, {Zeit} → HH-mm; ungültige Zeichen werden zu „_“.
    /// </summary>
    public static string ScanDateiname(string vorlage, DateTime zeit)
    {
        if (string.IsNullOrWhiteSpace(vorlage))
            vorlage = StandardScanDateiname;
        string name = vorlage.Trim()
            .Replace("{Datum}", zeit.ToString("yyyy-MM-dd"))
            .Replace("{Zeit}", zeit.ToString("HH-mm"));
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
    }

    /// <summary>Freier Pfad für einen neuen Scan (bei vorhandener Datei mit Zusatz _2, _3 …).</summary>
    public static string NeuerScanPfad(OptionenDaten o, DateTime zeit)
    {
        string ordner = string.IsNullOrWhiteSpace(o.ScanOrdner) ? StandardScanOrdner : o.ScanOrdner.Trim();
        string name = ScanDateiname(o.ScanDateiname, zeit);
        string pfad = Path.Combine(ordner, name);
        for (int n = 2; File.Exists(pfad); n++)
            pfad = Path.Combine(ordner, $"{Path.GetFileNameWithoutExtension(name)}_{n}.pdf");
        return pfad;
    }
}
