using System.IO;
using System.Text.Json;

namespace PdfEditor.Services;

public enum ScanQuelle { Flachbett, Einzug, EinzugDuplex }
public enum ScanFarbe { Farbe, Graustufen, Schwarzweiß }
public enum ScanPapier { A4, A5, Letter }

/// <summary>Zuletzt genutzte Scan-Einstellungen (inkl. Scanner).</summary>
public class ScanZustand
{
    public string? ScannerId { get; set; }
    public ScanQuelle Quelle { get; set; } = ScanQuelle.Flachbett;
    public ScanPapier Papier { get; set; } = ScanPapier.A4;
    public ScanFarbe Farbe { get; set; } = ScanFarbe.Graustufen;
    public int Dpi { get; set; } = 300;
    /// <summary>-100..100</summary>
    public int Helligkeit { get; set; }
    /// <summary>-100..100</summary>
    public int Kontrast { get; set; }
}

/// <summary>
/// Liest und schreibt die Scan-Einstellungen als JSON unter
/// <c>%AppData%\Roaming\perPDF\scan.json</c> (Muster wie <see cref="FensterEinstellungen"/>).
/// </summary>
public static class ScanEinstellungen
{
    private static string Pfad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "perPDF", "scan.json");

    /// <summary>Lädt die Einstellungen; bei fehlender/beschädigter Datei die Standardwerte.</summary>
    public static ScanZustand Laden()
    {
        try
        {
            return File.Exists(Pfad)
                ? JsonSerializer.Deserialize<ScanZustand>(File.ReadAllText(Pfad)) ?? new ScanZustand()
                : new ScanZustand();
        }
        catch
        {
            return new ScanZustand();
        }
    }

    /// <summary>Speichert die Einstellungen; Fehler werden bewusst ignoriert.</summary>
    public static void Speichern(ScanZustand zustand)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(zustand));
        }
        catch
        {
            // Einstellungen sind optional – ein Fehlschlag darf den Scan nicht stören.
        }
    }
}
