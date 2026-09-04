using System.IO;
using System.Text.Json;

namespace PdfEditor.Services;

/// <summary>Gespeicherte Fenstergeometrie (Position, Größe, Maximierung).</summary>
public class FensterZustand
{
    public double Links { get; set; }
    public double Oben { get; set; }
    public double Breite { get; set; }
    public double Höhe { get; set; }
    public bool Maximiert { get; set; }
}

/// <summary>
/// Liest und schreibt die zuletzt genutzte Fenstergeometrie, damit das Fenster beim
/// erneuten Öffnen in gleicher Größe (und Position) erscheint. Abgelegt als JSON unter
/// <c>%AppData%\Roaming\perPDF\fenster.json</c>.
/// </summary>
public static class FensterEinstellungen
{
    private static string Pfad
    {
        get
        {
            string basis = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(basis, "perPDF", "fenster.json");
        }
    }

    /// <summary>Lädt die gespeicherte Geometrie oder <c>null</c>, falls keine vorhanden/lesbar ist.</summary>
    public static FensterZustand? Laden()
    {
        try
        {
            return File.Exists(Pfad)
                ? JsonSerializer.Deserialize<FensterZustand>(File.ReadAllText(Pfad))
                : null;
        }
        catch
        {
            return null; // beschädigte Datei o. Ä. → Standardgröße verwenden
        }
    }

    /// <summary>Speichert die Geometrie; Fehler werden bewusst ignoriert (optionale Funktion).</summary>
    public static void Speichern(FensterZustand zustand)
    {
        try
        {
            string? ordner = Path.GetDirectoryName(Pfad);
            if (ordner is not null)
                Directory.CreateDirectory(ordner);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(zustand));
        }
        catch
        {
            // Einstellungen sind optional – ein Fehlschlag darf den Ablauf nicht stören.
        }
    }
}
