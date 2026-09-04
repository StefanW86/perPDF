using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.ViewModels;

namespace PdfEditor;

/// <summary>Tab-Verwaltung: Erstellen, Aktivieren, Schließen und zugehörige UI-Handler.</summary>
public partial class MainWindow
{
    // ===== Tab-Verwaltung =====================================================

    /// <summary>Erstellt einen neuen leeren Tab und aktiviert ihn.</summary>
    private DokumentTab TabNeuErstellen()
    {
        var tab = new DokumentTab();
        _tabs.Add(tab);
        TabAktivieren(tab);
        return tab;
    }

    /// <summary>Aktiviert den angegebenen Tab – bindet VM-Ereignisse und aktualisiert die Ansicht.</summary>
    private void TabAktivieren(DokumentTab tab)
    {
        if (ReferenceEquals(_aktiverTab, tab))
            return;

        // Ereignisse des alten Tabs abmelden.
        if (_aktiverTab != null)
            TabEreignisseAbmelden(_aktiverTab.ViewModel);

        _aktiverTab = tab;
        _vm = tab.ViewModel;
        DataContext = _vm;

        // Ereignisse des neuen Tabs anmelden.
        TabEreignisseAnmelden(tab.ViewModel);

        // Tab-Leiste optisch aktualisieren.
        TabLeisteAktualisieren();

        // Ansicht neu aufbauen.
        SeitenScroll.ScrollToVerticalOffset(0);
        ZeichneAktuelleSeite();

        // Fenstertitel aktualisieren.
        Title = _vm.Titel;
    }

    private void TabEreignisseAnmelden(HauptViewModel vm)
    {
        vm.AnsichtKomplettNeu += TabAnsichtKomplettNeu;
        vm.AktuelleSeiteNeu += TabAktuelleSeiteNeu;
        vm.AktuelleSeiteNeu += TabMiniaturScrollen;
        vm.LadestatusGeändert += TabLadestatusGeändert;
        vm.SeiteGerendert += TabSeiteGerendert;
        vm.PropertyChanged += TabPropertyChanged;
    }

    private void TabEreignisseAbmelden(HauptViewModel vm)
    {
        vm.AnsichtKomplettNeu -= TabAnsichtKomplettNeu;
        vm.AktuelleSeiteNeu -= TabAktuelleSeiteNeu;
        vm.AktuelleSeiteNeu -= TabMiniaturScrollen;
        vm.LadestatusGeändert -= TabLadestatusGeändert;
        vm.SeiteGerendert -= TabSeiteGerendert;
        vm.PropertyChanged -= TabPropertyChanged;
    }

    private void TabAnsichtKomplettNeu()
    {
        if (_aktiverTab != null) _aktiverTab.TextCache.Clear();
        ZeichneAktuelleSeite();
        TabLeisteAktualisieren(); // Titeländerung nach Laden
        Title = _vm.Titel;
    }

    private void TabAktuelleSeiteNeu() => ZeichneAktuelleSeite();

    private void TabMiniaturScrollen()
        => Dispatcher.BeginInvoke(
            () => MiniaturListe.ScrollIntoView(MiniaturListe.SelectedItem),
            System.Windows.Threading.DispatcherPriority.Loaded);

    private void TabLadestatusGeändert(bool sichtbar, string text)
    {
        LadeOverlay.Visibility = sichtbar ? Visibility.Visible : Visibility.Collapsed;
        LadeText.Text = text;
        LadeDetails.Text = "";
        LadeBalken.IsIndeterminate = sichtbar;
    }

    private void TabSeiteGerendert(int aktuelle, int gesamt)
    {
        if (aktuelle < gesamt)
        {
            LadeBalken.IsIndeterminate = false;
            LadeBalken.Minimum = 0;
            LadeBalken.Maximum = gesamt;
            LadeBalken.Value = aktuelle;
            LadeText.Text = "Vorschau wird aufgebaut...";
            LadeDetails.Text = $"Seite {aktuelle} von {gesamt}";
        }
        else
        {
            LadeDetails.Text = "";
        }
    }

    private void TabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HauptViewModel.MarkerModus)
            or nameof(HauptViewModel.RadiererModus)
            or nameof(HauptViewModel.EinfügeModus)
            or nameof(HauptViewModel.NeuesFeldTyp)
            or nameof(HauptViewModel.FelderBearbeitenModus))
            ModusAktualisieren();
        else if (e.PropertyName == nameof(HauptViewModel.HatEntwurf) && _vm.HatEntwurf)
            Dispatcher.BeginInvoke(() => { EntwurfText.Focus(); EntwurfText.SelectAll(); },
                System.Windows.Threading.DispatcherPriority.Loaded);
        else if (e.PropertyName == nameof(HauptViewModel.AusgewählterKommentar))
            Dispatcher.BeginInvoke(() =>
            {
                if (_vm.AusgewählterKommentar is { } k)
                {
                    if (_vm.Kommentare.Contains(k)) KommentarListe.ScrollIntoView(k);
                    ScrollZuKommentar(k);
                }
                ZeichneVerbindungslinie();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        else if (e.PropertyName == nameof(HauptViewModel.ZeigeInhaltsverzeichnis)
                 && !_vm.ZeigeInhaltsverzeichnis)
            // Beim Wechsel zurück auf "Seiten" die aktuelle Seite einscrollen: ein
            // vorheriges ScrollIntoView (z. B. nach TOC-Sprung) lief ins Leere, weil
            // die Miniaturliste da noch ausgeblendet war.
            TabMiniaturScrollen();
        else if (e.PropertyName is nameof(HauptViewModel.IstGeändert) or nameof(HauptViewModel.Pfad))
        {
            TabLeisteAktualisieren();
            Title = _vm.Titel;
        }
    }

    /// <summary>Aktualisiert die visuelle Darstellung der Tab-Leiste.</summary>
    private void TabLeisteAktualisieren()
    {
        // Bei Titeländerungen (kein CollectionChanged) Container-Präsentationen invalidieren.
        foreach (var item in TabLeiste.Items)
        {
            var container = TabLeiste.ItemContainerGenerator
                .ContainerFromItem(item) as FrameworkElement;
            container?.InvalidateVisual();
        }
        TabLeisteHervorheben();
    }

    /// <summary>Hebt den aktiven Tab-Reiter visuell hervor (hellerer Hintergrund, Akzentrahmen).</summary>
    private void TabLeisteHervorheben()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            var cp = TabLeiste.ItemContainerGenerator.ContainerFromIndex(i)
                as System.Windows.Controls.ContentPresenter;
            if (cp == null) continue;
            cp.ApplyTemplate();

            // Border im DataTemplate per VisualTreeHelper suchen.
            var border = FindVisualChild<Border>(cp, "TabRahmen");
            if (border == null) continue;

            bool istAktiv = ReferenceEquals(_tabs[i], _aktiverTab);
            border.Background = istAktiv
                ? (Brush)(TryFindResource("ApplicationBackgroundBrush") ?? Brushes.White)
                : (Brush)(TryFindResource("ControlFillColorDefaultBrush") ?? Brushes.LightGray);
            border.BorderBrush = istAktiv
                ? Akzent
                : (Brush)(TryFindResource("ControlElevationBorderBrush") ?? Brushes.Gray);
        }
    }

    /// <summary>Sucht ein benanntes Kind-Element im Visual-Baum.</summary>
    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name)
                return fe;
            var result = FindVisualChild<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>Versucht einen Tab zu schließen; fragt bei ungespeicherten Änderungen nach.</summary>
    /// <returns>true wenn der Tab geschlossen wurde, false wenn der Benutzer abgebrochen hat.</returns>
    private bool TabSchließenVersuchen(DokumentTab tab)
    {
        if (tab.ViewModel.IstGeändert && tab.ViewModel.HatDokument)
        {
            // Aktiven Tab anzeigen, damit der Benutzer sieht, worum es geht.
            TabAktivieren(tab);

            var datei = tab.ViewModel.Pfad is null
                ? "Ohne Titel"
                : System.IO.Path.GetFileName(tab.ViewModel.Pfad);
            var ergebnis = MessageBox.Show(
                $"„{datei}“ hat ungespeicherte Änderungen.\n\nMöchten Sie die Änderungen speichern?",
                "Dokument schließen",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (ergebnis == MessageBoxResult.Cancel)
                return false;
            if (ergebnis == MessageBoxResult.Yes)
                tab.ViewModel.SpeichernBefehl.Execute(null);
        }

        // Tab entfernen und Ereignisse abmelden.
        if (ReferenceEquals(_aktiverTab, tab))
            TabEreignisseAbmelden(tab.ViewModel);

        _tabs.Remove(tab);

        if (ReferenceEquals(_aktiverTab, tab))
        {
            _aktiverTab = null;
            if (_tabs.Count > 0)
                TabAktivieren(_tabs[^1]);
            else
            {
                // Kein Tab mehr offen – leeren Zustand anzeigen.
                _vm = new HauptViewModel();
                DataContext = _vm;
                SeitenBild.Source = null;
                OverlayCanvas.Children.Clear();
                Title = "perPDF";
            }
        }
        return true;
    }

    // ----- UI-Handler für Tabs -----------------------------------------------

    /// <summary>Öffnet eine PDF-Datei immer in einem neuen Tab.</summary>
    private void ÖffnenInNeuemTab_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "PDF-Datei öffnen",
            Filter = "PDF-Dateien (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog() != true)
            return;
        var tab = TabNeuErstellen();
        tab.ViewModel.DateiLaden(dlg.FileName);
    }

    /// <summary>
    /// Zeigt beim Ziehen von PDF-Dateien über das Fenster den Kopieren-Cursor.
    /// Interne Drags (z. B. Seiten-Umsortierung per gong-wpf-dragdrop) tragen kein
    /// FileDrop-Format und bleiben unberührt.
    /// </summary>
    private void Fenster_DragOver(object sender, DragEventArgs e)
    {
        if (GezogenePdfDateien(e).Count == 0)
            return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>Öffnet per Drag &amp; Drop auf das Fenster gezogene PDF-Dateien in je einem neuen Tab.</summary>
    private void Fenster_Drop(object sender, DragEventArgs e)
    {
        var pdfs = GezogenePdfDateien(e);
        if (pdfs.Count == 0)
            return;
        e.Handled = true;
        foreach (var pfad in pdfs)
        {
            var tab = TabNeuErstellen();
            tab.ViewModel.DateiLaden(pfad);
        }
    }

    /// <summary>Liefert die PDF-Dateipfade eines Drag-&amp;-Drop-Vorgangs (leer, wenn keine dabei sind).</summary>
    private static List<string> GezogenePdfDateien(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
        && e.Data.GetData(DataFormats.FileDrop) is string[] dateien
            ? dateien.Where(d => d.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();

    private void TabReiter_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DokumentTab tab)
            TabAktivieren(tab);
    }

    private void TabSchließen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DokumentTab tab)
            TabSchließenVersuchen(tab);
    }

    /// <summary>Beim Schließen des Fensters alle Tabs prüfen.</summary>
    private void FensterSchließt(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        FensterZustandSpeichern();

        // Alle Tabs schließen (in umgekehrter Reihenfolge).
        var zuSchließen = _tabs.ToList();
        foreach (var tab in zuSchließen)
        {
            if (!TabSchließenVersuchen(tab))
            {
                e.Cancel = true; // Benutzer hat abgebrochen
                return;
            }
        }
    }
}
