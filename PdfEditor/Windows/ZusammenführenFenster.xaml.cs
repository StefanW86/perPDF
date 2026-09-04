using System.Collections;
using System.Windows;
using System.Windows.Controls;
using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.ViewModels;

namespace PdfEditor.Windows;

/// <summary>
/// Modales Fenster für den PDF-Zusammenführungsmodus. Zeigt zwei Seitenlisten
/// nebeneinander und ermöglicht Drag &amp; Drop zwischen ihnen.
/// Nach Bestätigung steht das Ergebnis in <see cref="Ergebnis"/>.
/// </summary>
public partial class ZusammenführenFenster : Wpf.Ui.Controls.FluentWindow, IDropTarget
{
    private readonly ZusammenführViewModel _vm;

    /// <summary>Das fertig zusammengeführte PDF als Byte-Array (nach „Zusammenführen").</summary>
    public byte[]? Ergebnis => _vm.Ergebnis;

    public ZusammenführenFenster(byte[] aktuelleBytes, string dateiname)
    {
        InitializeComponent();
        _vm = new ZusammenführViewModel(aktuelleBytes, dateiname);
        DataContext = _vm;

        // gong-wpf-dragdrop: dieses Fenster als Drop-Handler für beide Listen.
        GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(LinkeListBox, this);
        GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(RechteListBox, this);
    }

    // ----- Schaltflächen-Handler -------------------------------------------

    private void ZweitesDokumentÖffnen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Zweites PDF-Dokument öffnen",
            Filter = "PDF-Dateien (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog(this) != true)
            return;
        _vm.ZweitesDokumentLaden(dlg.FileName);
    }

    private void GanzesDokumentAnhängen_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.RechteSeiten.Count == 0)
        {
            MessageBox.Show(
                "Bitte zuerst ein zweites Dokument laden.",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _vm.GanzesDokumentAnhängen();
        StatusText.Text = $"Alle {_vm.RechteSeiten.Count} Seiten des zweiten Dokuments wurden ans erste Dokument angehängt.";
    }

    private void Zusammenführen_Click(object sender, RoutedEventArgs e)
    {
        _vm.Zusammenführen();
        DialogResult = true;
        Close();
    }

    private void Abbrechen_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ----- Explorer-Drop (PDF auf das Fenster ziehen) ----------------------

    private void Fenster_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Fenster_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var dateien = (string[])e.Data.GetData(DataFormats.FileDrop);
        var pdf = dateien.FirstOrDefault(f =>
            f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        if (pdf != null)
        {
            _vm.ZweitesDokumentLaden(pdf);
            StatusText.Text = $"Zweites Dokument geladen: {System.IO.Path.GetFileName(pdf)}";
        }
    }

    // ----- IDropTarget (gong-wpf-dragdrop zwischen den Listen) ------------

    /// <summary>
    /// Liefert die gezogenen <see cref="ZusammenführSeite"/>-Objekte aus dem Drop-Info.
    /// gong-wpf-dragdrop liefert bei Mehrfachauswahl eine <see cref="IEnumerable"/>,
    /// bei Einzelauswahl direkt das Objekt.
    /// </summary>
    private static List<ZusammenführSeite> GezogeneSeiten(IDropInfo dropInfo)
    {
        if (dropInfo.Data is ZusammenführSeite einzelSeite)
            return [einzelSeite];
        if (dropInfo.Data is IEnumerable aufzählung)
            return aufzählung.OfType<ZusammenführSeite>().ToList();
        return [];
    }

    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        var seiten = GezogeneSeiten(dropInfo);
        if (seiten.Count == 0)
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }

        bool nachLinks = dropInfo.VisualTarget is ListBox lb && lb.Name == "LinkeListBox";

        // Erlaubte Fälle:
        // (a) Links→Links: einzelne Seite aus der linken Liste umsortieren
        // (b) Rechts→Links: eine oder mehrere rechte Seiten ins linke Dokument kopieren
        bool vonLinks = seiten.All(s => _vm.LinkeSeiten.Contains(s));
        bool vonRechts = seiten.All(s => _vm.RechteSeiten.Contains(s));

        if (nachLinks && (vonLinks && seiten.Count == 1 || vonRechts))
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = vonRechts ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else
        {
            dropInfo.Effects = DragDropEffects.None;
        }
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        var seiten = GezogeneSeiten(dropInfo);
        if (seiten.Count == 0)
            return;

        bool nachLinks = dropInfo.VisualTarget is ListBox lb && lb.Name == "LinkeListBox";
        if (!nachLinks)
            return;

        bool vonLinks = seiten.Count == 1 && _vm.LinkeSeiten.Contains(seiten[0]);
        bool vonRechts = seiten.All(s => _vm.RechteSeiten.Contains(s));

        if (vonLinks)
        {
            // (a) Links→Links: Einzelseite umsortieren
            var seite = seiten[0];
            int altIndex = _vm.LinkeSeiten.IndexOf(seite);
            int neuIndex = dropInfo.InsertIndex;
            if (neuIndex > altIndex) neuIndex--;
            _vm.LinkeSeiteUmsortieren(altIndex, Math.Clamp(neuIndex, 0, _vm.LinkeSeiten.Count - 1));
            StatusText.Text = "Seitenreihenfolge geändert.";
        }
        else if (vonRechts)
        {
            // (b) Rechts→Links: eine oder mehrere Seiten kopieren (Quelle bleibt erhalten)
            var quellListenIndizes = seiten
                .Select(s => _vm.RechteSeiten.IndexOf(s))
                .Where(i => i >= 0)
                .ToList();
            _vm.SeitenVonRechtsKopieren(quellListenIndizes, dropInfo.InsertIndex);
            string meldung = seiten.Count == 1
                ? "1 Seite ins erste Dokument kopiert."
                : $"{seiten.Count} Seiten ins erste Dokument kopiert.";
            StatusText.Text = meldung;
        }
    }

    void IDropTarget.DragEnter(IDropInfo dropInfo) { }
    void IDropTarget.DragLeave(IDropInfo dropInfo) { }
    void IDropTarget.DropHint(IDropHintInfo dropHintInfo) { }

    // ----- Kontextmenü linke Liste -----------------------------------------

    private void LinkeSeiteKontextLöschen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ZusammenführSeite seite })
        {
            int index = _vm.LinkeSeiten.IndexOf(seite);
            if (index >= 0)
                _vm.LinkeSeiteLöschen(index);
        }
    }
}
