using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Windows;

namespace PdfEditor;

/// <summary>Dialoge zum Extrahieren und Entfernen von Seiten (Seitenauswahl-Fenster).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Öffnet den Extrahieren-Dialog, speichert die gewählten Seiten als neues PDF
    /// und bietet an, es direkt in einem neuen Tab zu öffnen.
    /// </summary>
    private void SeitenExtrahierenÖffnen_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Bytes is null)
            return;

        var fenster = new SeitenAuswahlFenster(_vm.Bytes, SeitenAuswahlModus.Extrahieren)
        {
            Owner = this
        };
        if (fenster.ShowDialog() != true || fenster.Ergebnis is null)
            return;

        string basisName = Path.GetFileNameWithoutExtension(_vm.Pfad ?? "Dokument");
        var dlg = new SaveFileDialog
        {
            Title = "Extrahierte Seiten speichern",
            Filter = "PDF-Dateien (*.pdf)|*.pdf",
            FileName = $"{basisName} – Auszug.pdf"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dlg.FileName, fenster.Ergebnis);
        }
        catch (Exception ex)
        {
            MeldungFenster.Zeigen(this, "Fehler",
                $"Die Datei konnte nicht gespeichert werden.\n\nDetails: {ex.Message}",
                MessageBoxImage.Error);
            return;
        }

        int anzahl = fenster.AusgewählteIndizes.Count;
        _vm.Status = anzahl == 1
            ? $"1 Seite extrahiert nach {Path.GetFileName(dlg.FileName)}."
            : $"{anzahl} Seiten extrahiert nach {Path.GetFileName(dlg.FileName)}.";

        int antwort = MeldungFenster.Fragen(this, "Seiten extrahieren",
            "Das extrahierte Dokument wurde gespeichert.",
            MessageBoxImage.Information, "In neuem Tab öffnen", "Schließen");
        if (antwort == 0)
        {
            var tab = TabNeuErstellen();
            tab.ViewModel.DateiLaden(dlg.FileName);
        }
    }

    /// <summary>
    /// Öffnet den Entfernen-Dialog und löscht die gewählten Seiten aus dem aktuellen
    /// Dokument (ohne weitere Rückfrage – der Dialog ist die Bestätigung).
    /// </summary>
    private void SeitenEntfernenÖffnen_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Bytes is null)
            return;

        var fenster = new SeitenAuswahlFenster(_vm.Bytes, SeitenAuswahlModus.Entfernen)
        {
            Owner = this
        };
        if (fenster.ShowDialog() != true || fenster.AusgewählteIndizes.Count == 0)
            return;

        _vm.SeitenLöschen(fenster.AusgewählteIndizes, rückfragen: false);
    }
}
