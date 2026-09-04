using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfEditor.Models;

namespace PdfEditor;

/// <summary>
/// Interaktion der Miniatur-Seitenleiste: Mehrfachauswahl (Extended-Selection),
/// Kontextmenü und Entf-Taste zum Löschen bzw. Drehen der ausgewählten Seiten.
/// </summary>
public partial class MainWindow
{
    /// <summary>Seitenindizes der aktuellen Miniatur-Auswahl (aufsteigend).</summary>
    private List<int> AusgewählteSeitenIndizes()
    {
        var indizes = new List<int>();
        foreach (var element in MiniaturListe.SelectedItems.OfType<SeitenElement>())
        {
            int i = _vm.Seiten.IndexOf(element);
            if (i >= 0)
                indizes.Add(i);
        }
        indizes.Sort();
        return indizes;
    }

    /// <summary>
    /// Spiegelt die Auswahl ins ViewModel, damit auch die Symbolleisten-Befehle
    /// (Seite löschen, Drehen) auf die gesamte Auswahl wirken.
    /// </summary>
    private void MiniaturListe_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _vm.AusgewählteSeitenSetzen(AusgewählteSeitenIndizes());

    /// <summary>
    /// Rechtsklick wählt die Kachel unter dem Zeiger aus, sofern sie nicht schon
    /// Teil der Auswahl ist – so wirkt das Kontextmenü immer auf die richtige Auswahl.
    /// </summary>
    private void MiniaturListe_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject quelle)
            return;
        if (ItemsControl.ContainerFromElement(MiniaturListe, quelle) is not ListBoxItem kachel
            || kachel.IsSelected)
            return;
        MiniaturListe.SelectedItem = kachel.DataContext; // ersetzt die bisherige Auswahl
    }

    private void MiniaturListe_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;
        _vm.SeitenLöschen(AusgewählteSeitenIndizes());
        e.Handled = true;
    }

    private void MiniaturLöschen_Click(object sender, RoutedEventArgs e)
        => _vm.SeitenLöschen(AusgewählteSeitenIndizes());

    private void MiniaturLinksDrehen_Click(object sender, RoutedEventArgs e)
        => _vm.SeitenDrehen(AusgewählteSeitenIndizes(), -90);

    private void MiniaturRechtsDrehen_Click(object sender, RoutedEventArgs e)
        => _vm.SeitenDrehen(AusgewählteSeitenIndizes(), 90);
}
