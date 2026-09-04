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

/// <summary>Zeichnen der aktuellen Seite, Textauswahl-Ebene und Kommentar-Seitenleiste.</summary>
public partial class MainWindow
{
    // ----- Zeichnen der aktuellen Seite ------------------------------------

    private void ZeichneAktuelleSeite()
    {
        OverlayCanvas.Children.Clear();

        int index = _vm.AktuelleSeite;
        if (_vm.Bytes is null || index < 0 || index >= _vm.SeitenInfos.Count)
        {
            SeitenBild.Source = null;
            return;
        }

        var info = _vm.SeitenInfos[index];
        var geo = new SeitenGeometrie(info, _vm.Zoom);
        _geo = geo;

        try
        {
            SeitenBild.Source = PdfRenderDienst.SeiteRendern(_vm.Bytes, index, geo.RenderDpi);
        }
        catch
        {
            SeitenBild.Source = null;
        }

        SeitenBild.Width = geo.AnzeigeBreite;
        SeitenBild.Height = geo.AnzeigeHöhe;
        OverlayCanvas.Width = geo.AnzeigeBreite;
        OverlayCanvas.Height = geo.AnzeigeHöhe;

        // Markierbare Textebene ganz unten (über dem Seitenbild, unter den
        // Annotationen): außerhalb der Zeichenmodi ist der vorhandene PDF-Text so
        // direkt mit dem Mauszeiger markier- und kopierbar (Cursor wird zum
        // Textcursor), während Anmerkungen darüber weiterhin anklickbar bleiben.
        if (!InSpezialModus)
            FügeTextauswahlEbene(index, geo);

        // Kommentar-Hervorhebungen (über Textebene, unter Annotationen).
        ZeichneKommentarHighlights(index, geo);

        // Suchtrefferhighlights (über Textebene, unter Annotationen).
        if (_suchTreffer.Count > 0)
            ZeichneSuchTreffer(index, geo);

        // Abdeckungen der Beschriftungen gelöschter Optionsfelder – rein passiv,
        // unter allen Annotationen (entspricht dem Einbrennen beim Speichern).
        foreach (var abdeckung in _vm.Seitenabdeckungen)
        {
            if (abdeckung.SeitenIndex == index)
                ZeichneAbdeckung(abdeckung.Bereich, geo);
        }

        // Reihenfolge der Overlays: Markierungen, dann Unterschriften,
        // dann Texte, zuletzt die Formularfelder (Eingaben oben).
        foreach (var markierung in _vm.Textmarkierungen)
        {
            if (markierung.SeitenIndex == index)
                FügeTextmarkerHinzu(markierung, geo);
        }

        foreach (var bildEinfügung in _vm.Bilder)
        {
            if (bildEinfügung.SeitenIndex == index)
                FügeBildHinzu(bildEinfügung, geo);
        }

        foreach (var platzierung in _vm.Platzierungen)
        {
            if (platzierung.SeitenIndex == index)
                FügeUnterschriftHinzu(platzierung, geo);
        }

        foreach (var notiz in _vm.Textnotizen)
        {
            if (notiz.SeitenIndex == index)
                FügeTextHinzu(notiz, geo);
        }

        foreach (var symbol in _vm.Symbolmarkierungen)
        {
            if (symbol.SeitenIndex == index)
                FügeSymbolHinzu(symbol, geo);
        }

        foreach (var feld in _vm.Felder)
        {
            if (feld.SeitenIndex != index)
                continue;
            // Im Bearbeitungsmodus werden abbildbare Felder als auswählbare Felder gezeigt
            // (Anklicken übernimmt sie zur Bearbeitung); sonst normal als ausfüllbare Felder.
            if (_vm.FelderBearbeitenModus && FeldBearbeitbar(feld))
                FügeBearbeitbaresFeldHinzu(feld, geo);
            else
                FügeFeldHinzu(feld, geo);
        }

        // Neu angelegte Entwurfsfelder (Formular-Designer) zuoberst.
        foreach (var entwurf in _vm.Entwurfsfelder)
        {
            if (entwurf.SeitenIndex == index)
                FügeEntwurfHinzu(entwurf, geo);
        }

        // Cursor-Vorschau des Platziermodus ganz oben – nach jedem Neuzeichnen
        // wieder hinzufügen, da Children.Clear() sie entfernt hat.
        if (_cursorVorschau != null && (_vm.EinfügeModus != null || _vm.NeuesFeldTyp != null))
        {
            OverlayCanvas.Children.Add(_cursorVorschau);
            Canvas.SetZIndex(_cursorVorschau, 999);
        }

        // Verbindungslinie nach Abschluss des Layouts zeichnen.
        Dispatcher.BeginInvoke(ZeichneVerbindungslinie, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Legt eine einzige, transparente Auswahlebene über die Seite, die den
    /// vorhandenen PDF-Text wie in einem Textfenster markierbar macht – auch über
    /// mehrere Zeilen hinweg – und mit Strg+C kopierbar (siehe
    /// <see cref="Controls.TextAuswahlEbene"/>).
    /// </summary>
    private void FügeTextauswahlEbene(int index, SeitenGeometrie geo)
    {
        if (_vm.Bytes is null)
            return;

        if (!_textCache.TryGetValue(index, out var zeilen))
        {
            // PdfPig normalisiert auf den MediaBox-Ursprung – diesen wieder
            // aufaddieren, damit der Text zur restlichen Geometrie (PdfSharp) passt.
            var info = _vm.SeitenInfos[index];
            zeilen = _textDienst.ZeilenLesen(_vm.Bytes, index, info.X1, info.Y1);
            _textCache[index] = zeilen;
        }

        if (zeilen.Count == 0)
            return;

        var ebene = new Controls.TextAuswahlEbene(zeilen, geo);
        ebene.KommentarAngefordert += (quads, text) =>
            _vm.KommentarEntwurfBeginnen(index, quads, text);
        Canvas.SetLeft(ebene, 0);
        Canvas.SetTop(ebene, 0);
        OverlayCanvas.Children.Add(ebene);
    }

    // ----- Kommentare: Hervorhebung, Verbindungslinie, Seitenleiste --------

    private static readonly Brush KommentarNormal = Eingefroren(Color.FromArgb(80, 0xFF, 0xE6, 0x00));
    private static readonly Brush KommentarAktiv = Eingefroren(Color.FromArgb(140, 0xFF, 0xC8, 0x1E));

    /// <summary>Zeichnet die gelben Hervorhebungen aller Kommentare der Seite (aktiver stärker).</summary>
    private void ZeichneKommentarHighlights(int index, SeitenGeometrie geo)
    {
        var aktiv = _vm.Entwurf ?? _vm.AusgewählterKommentar;
        foreach (var k in _vm.Kommentare)
        {
            if (k.SeitenIndex != index)
                continue;
            ZeichneKommentarFlächen(k.Rechtecke, geo, ReferenceEquals(k, aktiv));
        }
        if (_vm.Entwurf is { } e && e.SeitenIndex == index)
            ZeichneKommentarFlächen(e.Rechtecke, geo, true);
    }

    private void ZeichneKommentarFlächen(
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> rechtecke,
        SeitenGeometrie geo, bool aktiv)
    {
        Brush füllung = aktiv ? KommentarAktiv : KommentarNormal;
        foreach (var q in rechtecke)
        {
            Rect r = geo.RechteckNachAnzeige(q.X1, q.Y1, q.X2, q.Y2);
            var rect = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Fill = füllung,
                IsHitTestVisible = false
            };
            if (aktiv)
            {
                rect.Stroke = Akzent;
                rect.StrokeThickness = 1;
            }
            Canvas.SetLeft(rect, r.Left);
            Canvas.SetTop(rect, r.Top);
            OverlayCanvas.Children.Add(rect);
        }
    }

    /// <summary>
    /// Zeichnet die gestrichelte Verbindungslinie vom markierten Text zur zugehörigen
    /// Kommentarkarte (bzw. zum Eingabefeld des Entwurfs). Wird bei Auswahl, Scrollen
    /// und Größenänderung neu berechnet.
    /// </summary>
    private void ZeichneVerbindungslinie()
    {
        VerbindungsEbene.Children.Clear();

        var ziel = _vm.Entwurf ?? _vm.AusgewählterKommentar;
        if (ziel is null || _geo is null || !_vm.KommentareSichtbar)
            return;
        if (ziel.SeitenIndex != _vm.AktuelleSeite || ziel.Rechtecke.Count == 0)
            return;

        FrameworkElement? karte = ReferenceEquals(ziel, _vm.Entwurf)
            ? EntwurfText
            : KommentarListe.ItemContainerGenerator.ContainerFromItem(ziel) as FrameworkElement;
        if (karte is null || !karte.IsVisible || karte.ActualWidth < 1)
            return;

        // Ankerpunkt am Text: rechte Mitte des ersten Hervorhebungsrechtecks.
        var q = ziel.Rechtecke[0];
        Rect r = _geo.RechteckNachAnzeige(q.X1, q.Y1, q.X2, q.Y2);
        Point anker = OverlayCanvas.TransformToVisual(VerbindungsEbene)
            .Transform(new Point(r.Right, r.Top + r.Height / 2));

        // Nur zeichnen, wenn der Anker im sichtbaren Seitenbereich liegt.
        Point vp = SeitenScroll.TransformToVisual(VerbindungsEbene).Transform(new Point(0, 0));
        double oben = vp.Y, unten = vp.Y + SeitenScroll.ViewportHeight;
        if (anker.Y < oben - 4 || anker.Y > unten + 4)
            return;
        anker.X = Math.Min(anker.X, vp.X + SeitenScroll.ViewportWidth - 2);

        Point kartePunkt = karte.TransformToVisual(VerbindungsEbene)
            .Transform(new Point(0, karte.ActualHeight / 2));

        var linie = new System.Windows.Shapes.Path
        {
            Stroke = Akzent,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        };
        var geom = new PathGeometry();
        var fig = new PathFigure { StartPoint = anker };
        double midX = (anker.X + kartePunkt.X) / 2;
        fig.Segments.Add(new BezierSegment(
            new Point(midX, anker.Y), new Point(midX, kartePunkt.Y), kartePunkt, true));
        geom.Figures.Add(fig);
        linie.Data = geom;
        VerbindungsEbene.Children.Add(linie);

        var punkt = new Ellipse { Width = 7, Height = 7, Fill = Akzent };
        Canvas.SetLeft(punkt, anker.X - 3.5);
        Canvas.SetTop(punkt, anker.Y - 3.5);
        VerbindungsEbene.Children.Add(punkt);
    }

    private void KommentarListe_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => ZeichneVerbindungslinie();

    /// <summary>Scrollt die Seitenansicht zum markierten Text des Kommentars.</summary>
    private void ScrollZuKommentar(Kommentar k)
    {
        if (_geo is null || k.SeitenIndex != _vm.AktuelleSeite || k.Rechtecke.Count == 0)
            return;
        var q = k.Rechtecke[0];
        Rect r = _geo.RechteckNachAnzeige(q.X1, q.Y1, q.X2, q.Y2);
        // +16 berücksichtigt das Padding des ScrollViewers.
        double mitteY = (r.Top + r.Bottom) / 2 + 16;
        double mitteX = (r.Left + r.Right) / 2 + 16;
        SeitenScroll.ScrollToVerticalOffset(Math.Max(0, mitteY - SeitenScroll.ViewportHeight / 2));
        SeitenScroll.ScrollToHorizontalOffset(Math.Max(0, mitteX - SeitenScroll.ViewportWidth / 2));
    }

    private void KommentareSchließen_Click(object sender, RoutedEventArgs e)
        => _vm.KommentareSichtbar = false;

    private void EntwurfText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.EntwurfVerwerfen();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _vm.EntwurfSpeichern(EntwurfText.Text);
            e.Handled = true;
        }
    }

    private void EntwurfSpeichern_Click(object sender, RoutedEventArgs e)
        => _vm.EntwurfSpeichern(EntwurfText.Text);

    private void EntwurfAbbrechen_Click(object sender, RoutedEventArgs e)
        => _vm.EntwurfVerwerfen();

    private void KommentarLöschen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Kommentar k)
            _vm.KommentarLöschen(k);
    }

    private void Erledigt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Kommentar k)
            _vm.KommentarStatusUmschalten(k, KommentarStatus.Erledigt);
    }

    private void Abgelehnt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Kommentar k)
            _vm.KommentarStatusUmschalten(k, KommentarStatus.Abgelehnt);
    }

    private void AntwortText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
            && sender is TextBox tb && tb.Tag is Kommentar k)
        {
            _vm.KommentarAntwortHinzufügen(k, tb.Text);
            tb.Clear();
            e.Handled = true;
        }
    }

    private void AntwortSenden_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Kommentar k } knopf)
            return;
        // Die Eingabe-TextBox liegt als Geschwisterelement im selben StackPanel.
        var box = (knopf.Parent as StackPanel)?.Children.OfType<TextBox>().FirstOrDefault();
        if (box is null)
            return;
        _vm.KommentarAntwortHinzufügen(k, box.Text);
        box.Clear();
    }

}
