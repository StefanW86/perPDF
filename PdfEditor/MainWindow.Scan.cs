using System.IO;
using System.Windows;
using PdfEditor.Services;
using PdfEditor.ViewModels;

namespace PdfEditor;

/// <summary>Scannen: Dialog öffnen und den Scan anhängen oder als neues Dokument laden.</summary>
public partial class MainWindow
{
    private void Scannen_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LädtGerade)
            return;
        var fenster = new Windows.ScanFenster { Owner = this };
        if (fenster.ShowDialog() != true || fenster.Ergebnis is not { } pdf)
            return;

        if (!_vm.HatDokument)
        {
            ScanAlsNeuesDokument(pdf);
            return;
        }

        string name = _vm.Pfad is null ? "Ohne Titel" : Path.GetFileName(_vm.Pfad);
        while (true)
        {
            int antwort = Windows.MeldungFenster.Fragen(this, "Scannen",
                $"{SeitenText(fenster.SeitenAnzahl)} gescannt.\n\nZu „{name}“ hinzufügen?",
                MessageBoxImage.Question, "Anhängen", "Neues Dokument", "Verwerfen");
            switch (antwort)
            {
                case 0:
                    _vm.ScanAnhängen(pdf);
                    return;
                case 1:
                    ScanAlsNeuesDokument(pdf);
                    return;
                default:
                    // Kein versehentlicher Verlust: Verwerfen nochmals bestätigen.
                    if (Windows.MeldungFenster.Fragen(this, "Scannen",
                            $"{SeitenText(fenster.SeitenAnzahl)} verwerfen?",
                            MessageBoxImage.Warning, "Verwerfen", "Zurück") == 0)
                        return;
                    break;
            }
        }
    }

    /// <summary>
    /// Lädt den Scan als neues Dokument: in den aktiven Tab, wenn dieser leer ist, sonst in einen
    /// neuen. Ohne Tab ist <c>_vm</c> nur ein Platzhalter ohne angemeldete Ereignisse – darin
    /// geladen würde die Seitenansicht nie gezeichnet, daher dann immer einen Tab anlegen.
    /// </summary>
    private void ScanAlsNeuesDokument(byte[] pdf)
    {
        HauptViewModel ziel = _aktiverTab is { } tab && !tab.ViewModel.HatDokument
            ? tab.ViewModel
            : TabNeuErstellen().ViewModel;

        // Neue Scans landen direkt als Datei im Scan-Ordner (siehe Optionen) und werden geöffnet.
        string pfad;
        try
        {
            pfad = Optionen.NeuerScanPfad(Optionen.Laden(), DateTime.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.WriteAllBytes(pfad, pdf);
        }
        catch (Exception ex)
        {
            Windows.MeldungFenster.Zeigen(this, "Scannen",
                "Der Scan konnte nicht im Scan-Ordner gespeichert werden:\n" + ex.Message +
                "\n\nEr wird als ungespeichertes Dokument geöffnet.",
                MessageBoxImage.Warning);
            ziel.ScanAlsNeuesDokument(pdf);
            return;
        }
        ziel.DateiLaden(pfad);
    }

    private static string SeitenText(int anzahl) => anzahl == 1 ? "1 Seite" : $"{anzahl} Seiten";
}
