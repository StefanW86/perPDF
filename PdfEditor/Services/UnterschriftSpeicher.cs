using System.IO;
using PdfEditor.Models;

namespace PdfEditor.Services;

/// <summary>
/// Verwaltet die dauerhaft gespeicherten Unterschriften der Anwendung.
/// Die Bilder werden unter %AppData%\Roaming\perPDF\Signaturen abgelegt,
/// sodass sie nach einem Neustart weiterhin zur Verfügung stehen.
/// </summary>
public class UnterschriftSpeicher
{
    private static readonly string[] ErlaubteEndungen = { ".png", ".jpg", ".jpeg" };

    /// <summary>Ordner, in dem die Unterschriften abgelegt werden.</summary>
    public string Ordner { get; }

    public UnterschriftSpeicher()
    {
        string basis = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Ordner = Path.Combine(basis, "perPDF", "Signaturen");

        // Einmalige Übernahme aus dem früheren Ordner "PdfEditor" (alter Produktname),
        // damit bereits importierte Unterschriften erhalten bleiben.
        AltenOrdnerÜbernehmen(Path.Combine(basis, "PdfEditor"), Path.Combine(basis, "perPDF"));

        Directory.CreateDirectory(Ordner);
    }

    /// <summary>
    /// Verschiebt den alten Anwendungsordner auf den neuen Namen, sofern der alte
    /// existiert und der neue noch nicht. Schlägt das Umbenennen fehl, werden die
    /// Bilddateien stattdessen einzeln kopiert.
    /// </summary>
    private static void AltenOrdnerÜbernehmen(string alt, string neu)
    {
        try
        {
            if (!Directory.Exists(alt) || Directory.Exists(neu))
                return;

            Directory.Move(alt, neu);
        }
        catch
        {
            // Umbenennen kann scheitern (z. B. anderes Laufwerk/Sperre). Dann
            // die einzelnen Dateien kopieren, soweit möglich.
            try
            {
                string altSig = Path.Combine(alt, "Signaturen");
                string neuSig = Path.Combine(neu, "Signaturen");
                if (Directory.Exists(altSig))
                {
                    Directory.CreateDirectory(neuSig);
                    foreach (string datei in Directory.EnumerateFiles(altSig))
                    {
                        string ziel = Path.Combine(neuSig, Path.GetFileName(datei));
                        if (!File.Exists(ziel))
                            File.Copy(datei, ziel);
                    }
                }
            }
            catch
            {
                // Migration ist optional – im Zweifel startet die App mit leerem Ordner.
            }
        }
    }

    /// <summary>Liest alle gespeicherten Unterschriften aus dem Ordner.</summary>
    public List<Unterschrift> Laden()
    {
        return Directory.EnumerateFiles(Ordner)
            .Where(p => ErlaubteEndungen.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .OrderBy(p => Path.GetFileName(p))
            .Select(p => new Unterschrift(p))
            .ToList();
    }

    /// <summary>
    /// Kopiert eine ausgewählte Bilddatei in den Unterschriften-Ordner und gibt
    /// die neu angelegte Unterschrift zurück. Bei Namenskonflikten wird ein
    /// Zähler angehängt.
    /// </summary>
    public Unterschrift Hinzufügen(string quellpfad)
    {
        string endung = Path.GetExtension(quellpfad).ToLowerInvariant();
        if (!ErlaubteEndungen.Contains(endung))
            throw new ArgumentException("Es werden nur PNG- und JPG-Bilder unterstützt.");

        string name = Path.GetFileNameWithoutExtension(quellpfad);
        string ziel = Path.Combine(Ordner, name + endung);
        int zähler = 1;
        while (File.Exists(ziel))
        {
            ziel = Path.Combine(Ordner, $"{name} ({zähler}){endung}");
            zähler++;
        }

        File.Copy(quellpfad, ziel);
        return new Unterschrift(ziel);
    }

    /// <summary>Löscht eine gespeicherte Unterschrift dauerhaft.</summary>
    public void Entfernen(Unterschrift unterschrift)
    {
        if (File.Exists(unterschrift.Pfad))
            File.Delete(unterschrift.Pfad);
    }
}
