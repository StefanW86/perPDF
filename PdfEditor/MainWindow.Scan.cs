using System.IO;
using System.Windows;

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
            _vm.ScanAlsNeuesDokument(pdf); // leerer aktiver Tab wird genutzt
            return;
        }

        string name = _vm.Pfad is null ? "Ohne Titel" : Path.GetFileName(_vm.Pfad);
        while (true)
        {
            var antwort = MessageBox.Show(this,
                $"Sollen die {fenster.SeitenAnzahl} gescannten Seite(n) an „{name}“ angehängt werden?\n\n" +
                "Ja = an das geöffnete Dokument anhängen\nNein = als neues Dokument öffnen\n" +
                "Abbrechen = Scan verwerfen",
                "Scannen", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            switch (antwort)
            {
                case MessageBoxResult.Yes:
                    _vm.ScanAnhängen(pdf);
                    return;
                case MessageBoxResult.No:
                    TabNeuErstellen().ViewModel.ScanAlsNeuesDokument(pdf);
                    return;
                default:
                    // Kein versehentlicher Verlust: Verwerfen nochmals bestätigen.
                    if (MessageBox.Show(this, "Gescannte Seiten wirklich verwerfen?", "Scannen",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        return;
                    break;
            }
        }
    }
}
