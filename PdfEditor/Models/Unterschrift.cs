using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfEditor.Models;

/// <summary>
/// Eine gespeicherte Unterschrift (PNG- oder JPG-Bild), die dauerhaft im
/// AppData-Ordner der Anwendung abgelegt ist und mehrfach verwendet werden kann.
/// </summary>
public class Unterschrift
{
    public Unterschrift(string pfad)
    {
        Pfad = pfad;
        Name = Path.GetFileNameWithoutExtension(pfad);
    }

    /// <summary>Anzeigename (Dateiname ohne Endung).</summary>
    public string Name { get; }

    /// <summary>Vollständiger Pfad zur Bilddatei im AppData-Ordner.</summary>
    public string Pfad { get; }

    /// <summary>Lädt das Bild für die Vorschau-Galerie (zwischengespeichert).</summary>
    public ImageSource Bild
    {
        get
        {
            // Bild vollständig in den Speicher laden, damit die Datei nicht gesperrt bleibt.
            var bild = new BitmapImage();
            bild.BeginInit();
            bild.CacheOption = BitmapCacheOption.OnLoad;
            bild.UriSource = new Uri(Pfad);
            bild.EndInit();
            bild.Freeze();
            return bild;
        }
    }
}
