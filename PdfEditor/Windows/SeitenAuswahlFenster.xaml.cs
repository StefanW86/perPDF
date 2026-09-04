using System.Windows;
using System.Windows.Controls;
using PdfEditor.Models;
using PdfEditor.ViewModels;

namespace PdfEditor.Windows;

/// <summary>Wofür die Seitenauswahl verwendet wird – bestimmt Titel, Knopftext und Prüfungen.</summary>
public enum SeitenAuswahlModus
{
    /// <summary>Ausgewählte Seiten als neues PDF extrahieren.</summary>
    Extrahieren,
    /// <summary>Ausgewählte Seiten aus dem Dokument entfernen.</summary>
    Entfernen
}

/// <summary>
/// Modales Fenster zum Auswählen von Seiten des aktuellen Dokuments – per Klick auf
/// die Miniaturen (Mehrfachauswahl) oder per Bereichseingabe („3-10“, „1,3,5-8“).
/// Beide Eingabewege sind bidirektional synchronisiert. Nach Bestätigung stehen die
/// gewählten Indizes in <see cref="AusgewählteIndizes"/>; im Extrahieren-Modus
/// zusätzlich das neue PDF in <see cref="Ergebnis"/>.
/// </summary>
public partial class SeitenAuswahlFenster : Wpf.Ui.Controls.FluentWindow
{
    private readonly SeitenAuswahlViewModel _vm;
    private readonly SeitenAuswahlModus _modus;

    /// <summary>Verhindert Rückkopplung zwischen Bereichsfeld und Listenauswahl.</summary>
    private bool _syncLäuft;

    /// <summary>Die bestätigten Seitenindizes (0-basiert, aufsteigend).</summary>
    public List<int> AusgewählteIndizes { get; private set; } = new();

    /// <summary>Das extrahierte PDF (nur im Extrahieren-Modus nach Bestätigung).</summary>
    public byte[]? Ergebnis { get; private set; }

    public SeitenAuswahlFenster(byte[] aktuelleBytes, SeitenAuswahlModus modus)
    {
        InitializeComponent();
        _modus = modus;
        _vm = new SeitenAuswahlViewModel(aktuelleBytes);
        DataContext = _vm;

        string titel = modus == SeitenAuswahlModus.Extrahieren
            ? "Seiten extrahieren"
            : "Seiten entfernen";
        Title = titel;
        TitelLeiste.Title = titel;
        AktionKnopf.Content = modus == SeitenAuswahlModus.Extrahieren ? "Extrahieren" : "Entfernen";
    }

    // ----- Synchronisierung Bereichsfeld ↔ Listenauswahl --------------------

    private void BereichFeld_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncLäuft)
            return;
        _syncLäuft = true;
        try
        {
            var indizes = SeitenAuswahlViewModel.BereichParsen(BereichFeld.Text, _vm.Seiten.Count);
            SeitenListe.SelectedItems.Clear();
            foreach (int i in indizes)
                SeitenListe.SelectedItems.Add(_vm.Seiten[i]);
            StatusAktualisieren();
        }
        finally
        {
            _syncLäuft = false;
        }
    }

    private void SeitenListe_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncLäuft)
            return;
        _syncLäuft = true;
        try
        {
            BereichFeld.Text = SeitenAuswahlViewModel.BereichFormatieren(AktuelleAuswahl());
            StatusAktualisieren();
        }
        finally
        {
            _syncLäuft = false;
        }
    }

    private void AlleAuswählen_Click(object sender, RoutedEventArgs e)
    {
        SeitenListe.SelectAll();
    }

    /// <summary>Die aktuell in der Liste ausgewählten Seitenindizes (aufsteigend).</summary>
    private List<int> AktuelleAuswahl()
        => SeitenListe.SelectedItems.OfType<ZusammenführSeite>()
            .Select(s => s.SeitenIndex)
            .OrderBy(i => i)
            .ToList();

    private void StatusAktualisieren()
    {
        int n = SeitenListe.SelectedItems.Count;
        StatusText.Text = n switch
        {
            0 => "Seiten anklicken (Strg/Umschalt für Mehrfachauswahl) oder oben einen Bereich eingeben.",
            1 => "1 Seite ausgewählt.",
            _ => $"{n} Seiten ausgewählt."
        };
    }

    // ----- Schaltflächen ------------------------------------------------------

    private void Aktion_Click(object sender, RoutedEventArgs e)
    {
        var auswahl = AktuelleAuswahl();
        if (auswahl.Count == 0)
        {
            MessageBox.Show("Bitte mindestens eine Seite auswählen.",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_modus == SeitenAuswahlModus.Entfernen && auswahl.Count >= _vm.Seiten.Count)
        {
            MessageBox.Show("Das Dokument muss mindestens eine Seite behalten.",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_modus == SeitenAuswahlModus.Extrahieren)
        {
            try
            {
                Ergebnis = _vm.Extrahieren(auswahl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Die Seiten konnten nicht extrahiert werden.\n\nDetails: {ex.Message}",
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        AusgewählteIndizes = auswahl;
        DialogResult = true;
        Close();
    }

    private void Abbrechen_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
