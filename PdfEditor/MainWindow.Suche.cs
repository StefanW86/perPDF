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

/// <summary>Suchfunktion, Treffer-Hervorhebung, Über-/Zusammenführen-Dialoge und TOC-Baum.</summary>
public partial class MainWindow
{
    // ----- Suchfunktion --------------------------------------------------------

    private static readonly Brush SuchFarbeAktiv =
        Eingefroren(Color.FromArgb(200, 0xF4, 0xA9, 0x3B));   // orange (Akzentfarbe)
    private static readonly Brush SuchFarbeInaktiv =
        Eingefroren(Color.FromArgb(130, 0xFF, 0xEB, 0x32));   // gelb

    private void SuchenÖffnen_Click(object sender, RoutedEventArgs e) => SuchPanelÖffnen();

    private void SuchPanelÖffnen()
    {
        SuchPanel.Visibility = Visibility.Visible;
        SuchFeld.Focus();
        SuchFeld.SelectAll();
    }

    private void SuchPanelSchließen_Click(object sender, RoutedEventArgs e)
    {
        SuchPanel.Visibility = Visibility.Collapsed;
        SuchErgebnisseLöschen();
    }

    private void SuchFeld_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _suchTimer.Stop();
            bool begriffUnverändert = SuchFeld.Text.Trim()
                .Equals(_letzterSuchbegriff, StringComparison.OrdinalIgnoreCase);

            // Bei unveränderter Suche zum nächsten Treffer springen (Shift+Enter zum vorigen),
            // sonst zuerst die Suche ausführen.
            if (begriffUnverändert && _suchTreffer.Count > 0)
            {
                bool rückwärts = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                int schritt = rückwärts ? -1 : 1;
                _aktiverTreffer = (_aktiverTreffer + schritt + _suchTreffer.Count) % _suchTreffer.Count;
                ZuTrefferSpringen(_aktiverTreffer);
            }
            else
            {
                DokumentSuchen();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuchPanel.Visibility = Visibility.Collapsed;
            SuchErgebnisseLöschen();
        }
    }

    private void SuchFeld_TextChanged(object sender, TextChangedEventArgs e)
    {
        _suchTimer.Stop();
        if (string.IsNullOrWhiteSpace(SuchFeld.Text))
        {
            SuchErgebnisseLöschen();
            return;
        }
        _suchTimer.Start();
    }

    private void VorigerTreffer_Click(object sender, RoutedEventArgs e)
    {
        if (_suchTreffer.Count == 0) return;
        _aktiverTreffer = (_aktiverTreffer - 1 + _suchTreffer.Count) % _suchTreffer.Count;
        ZuTrefferSpringen(_aktiverTreffer);
    }

    private void NächsterTreffer_Click(object sender, RoutedEventArgs e)
    {
        if (_suchTreffer.Count == 0) return;
        _aktiverTreffer = (_aktiverTreffer + 1) % _suchTreffer.Count;
        ZuTrefferSpringen(_aktiverTreffer);
    }

    private void DokumentSuchen()
    {
        if (_vm.Bytes is null || string.IsNullOrWhiteSpace(SuchFeld.Text))
        {
            SuchErgebnisseLöschen();
            return;
        }

        _letzterSuchbegriff = SuchFeld.Text.Trim();
        _suchTreffer = _textDienst.Suchen(
            _vm.Bytes, _letzterSuchbegriff, _vm.SeitenInfos, _textCache);

        if (_suchTreffer.Count == 0)
        {
            _aktiverTreffer = -1;
            TrefferAnzeige.Text = "Nicht gefunden";
            ZeichneAktuelleSeite();
            return;
        }

        // Zum ersten Treffer auf der aktuellen Seite springen (oder zum allerersten).
        int erster = _suchTreffer.FindIndex(t => t.Seite == _vm.AktuelleSeite);
        _aktiverTreffer = erster >= 0 ? erster : 0;
        ZuTrefferSpringen(_aktiverTreffer);
    }

    private void ZuTrefferSpringen(int index)
    {
        var (seite, zi, _, _) = _suchTreffer[index];
        _aktiverTreffer = index;
        TrefferAnzeige.Text = $"{index + 1} / {_suchTreffer.Count}";

        if (_vm.AktuelleSeite != seite)
            _vm.AktuelleSeite = seite; // löst ZeichneAktuelleSeite via AktuelleSeiteNeu aus
        else
            ZeichneAktuelleSeite(); // selbe Seite – aktiven Treffer neu hervorheben

        // Nach dem Zeichnen zur Trefferposition scrollen.
        Dispatcher.BeginInvoke(() => ScrollZuSuchTreffer(seite, zi), DispatcherPriority.Loaded);
    }

    private void ScrollZuSuchTreffer(int seite, int zeile)
    {
        if (_geo == null || !_textCache.TryGetValue(seite, out var zeilen)) return;
        if (zeile >= zeilen.Count) return;
        var z = zeilen[zeile];
        Rect r = _geo.RechteckNachAnzeige(z.X1, z.Y1, z.X2, z.Y2);
        double mitte = (r.Top + r.Bottom) / 2;
        double ziel = Math.Max(0, mitte + 16 - SeitenScroll.ViewportHeight / 2);
        SeitenScroll.ScrollToVerticalOffset(ziel);
    }

    private void SuchErgebnisseLöschen()
    {
        _suchTreffer.Clear();
        _aktiverTreffer = -1;
        _letzterSuchbegriff = string.Empty;
        TrefferAnzeige.Text = string.Empty;
        ZeichneAktuelleSeite();
    }

    /// <summary>Öffnet das „Über perPDF"-Fenster als modalen Dialog.</summary>
    private void ÜberFensterÖffnen(object sender, RoutedEventArgs e)
    {
        new Windows.ÜberFenster { Owner = this }.ShowDialog();
    }

    /// <summary>Öffnet das Zusammenführungsfenster und lädt das Ergebnis als neues Dokument.</summary>
    private void ZusammenführenÖffnen_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Bytes is null)
            return;
        var fenster = new Windows.ZusammenführenFenster(_vm.Bytes, _vm.Pfad ?? "Dokument.pdf")
        {
            Owner = this
        };
        if (fenster.ShowDialog() == true && fenster.Ergebnis != null)
        {
            _vm.BytesLaden(fenster.Ergebnis);
        }
    }

    // ----- TOC-Baum ---------------------------------------------------------

    /// <summary>
    /// Springt beim Auswählen eines TOC-Eintrags zur zugehörigen Seite.
    /// </summary>
    private void TocBaum_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is Models.InhaltsEintrag eintrag && eintrag.SeitenIndex >= 0)
        {
            _vm.ZuSeiteSpringen(eintrag.SeitenIndex);
            SeitenScroll.ScrollToVerticalOffset(0);
        }
    }

    private void ZeichneSuchTreffer(int seitenIndex, SeitenGeometrie geo)
    {
        if (!_textCache.TryGetValue(seitenIndex, out var zeilen)) return;

        for (int ti = 0; ti < _suchTreffer.Count; ti++)
        {
            var (seite, zi, start, ende) = _suchTreffer[ti];
            if (seite != seitenIndex) continue;
            if (zi >= zeilen.Count) continue;

            var zeile = zeilen[zi];
            Rect r = geo.RechteckNachAnzeige(zeile.X1, zeile.Y1, zeile.X2, zeile.Y2);

            double x1, x2;
            double breitePt = zeile.X2 - zeile.X1;
            if (breitePt > 0 && ende <= zeile.ZeichenX.Length && start < zeile.ZeichenX.Length)
            {
                int startIdx = Math.Clamp(start, 0, zeile.ZeichenX.Length - 1);
                int endeIdx = Math.Clamp(ende, 0, zeile.ZeichenX.Length - 1);
                x1 = r.Left + (zeile.ZeichenX[startIdx] - zeile.X1) / breitePt * r.Width;
                x2 = r.Left + (zeile.ZeichenX[endeIdx] - zeile.X1) / breitePt * r.Width;
            }
            else
            {
                x1 = r.Left;
                x2 = r.Right;
            }

            bool istAktiv = ti == _aktiverTreffer;
            var rechteck = new Rectangle
            {
                Width = Math.Max(2, Math.Abs(x2 - x1)),
                Height = r.Height,
                Fill = istAktiv ? SuchFarbeAktiv : SuchFarbeInaktiv,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rechteck, Math.Min(x1, x2));
            Canvas.SetTop(rechteck, r.Top);
            OverlayCanvas.Children.Add(rechteck);
        }
    }

}
