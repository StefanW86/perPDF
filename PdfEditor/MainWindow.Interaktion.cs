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

/// <summary>Interaktionsmodi, Cursor-Vorschau, Zoom und Drag-and-drop der Seiten.</summary>
public partial class MainWindow
{
    private void ModusAktualisieren()
    {
        // Vorherige Cursor-Vorschau entfernen und neu aufbauen.
        EntferneCursorVorschau();

        if (_vm.NeuesFeldTyp != null)
        {
            // Formularfeld-Platziermodus: Geist des Feldes am Mauszeiger zeigen.
            _cursorVorschau = ErstelleFeldVorschau(_vm.NeuesFeldTyp.Value);
            if (_cursorVorschau != null)
            {
                _cursorVorschau.IsHitTestVisible = false;
                _cursorVorschau.Opacity = 0.8;
                _cursorVorschau.Visibility = Visibility.Hidden; // erst bei MouseEnter sichtbar
                OverlayCanvas.Children.Add(_cursorVorschau);
                Canvas.SetZIndex(_cursorVorschau, 999);
                OverlayCanvas.Cursor = Cursors.Cross;
            }
        }
        else if (_vm.EinfügeModus != null)
        {
            // Platziermodus: Cursor + Vorschau-Element aufbauen.
            _cursorVorschau = ErstelleCursorVorschau(_vm.EinfügeModus.Value);
            if (_cursorVorschau != null)
            {
                _cursorVorschau.IsHitTestVisible = false;
                _cursorVorschau.Opacity = 0.75;
                _cursorVorschau.Visibility = Visibility.Hidden; // erst bei MouseEnter sichtbar
                OverlayCanvas.Children.Add(_cursorVorschau);
                Canvas.SetZIndex(_cursorVorschau, 999);
                // Cursor ausblenden, damit nur die Vorschau sichtbar ist (außer bei Text-Modus).
                OverlayCanvas.Cursor = _vm.EinfügeModus == EinfügeArt.Text
                    ? Cursors.IBeam
                    : Cursors.None;
            }
        }
        else
        {
            OverlayCanvas.Cursor = _vm.MarkerModus ? Cursors.Pen
                : _vm.RadiererModus ? Cursors.Cross
                : null; // Standard-Cursor erben
        }

        if (_vm.MarkerModus || _vm.RadiererModus)
            OverlayCanvas.Focus();

        // "Text einfügen"-Schaltfläche farblich hervorheben, solange der Text-Platziermodus aktiv ist.
        if (_vm.EinfügeModus == EinfügeArt.Text)
        {
            TextEinfügenButton.Background =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E7C7B"));
            TextEinfügenButton.Foreground = Brushes.White;
        }
        else
        {
            TextEinfügenButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            TextEinfügenButton.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        }

        ZeichneAktuelleSeite();
    }

    private void EntferneCursorVorschau()
    {
        if (_cursorVorschau == null) return;
        OverlayCanvas.Children.Remove(_cursorVorschau);
        _cursorVorschau = null;
    }

    /// <summary>Erstellt das schwebende Vorschau-Element für den jeweiligen Einfügemodus.</summary>
    private FrameworkElement? ErstelleCursorVorschau(EinfügeArt art)
    {
        switch (art)
        {
            case EinfügeArt.Text:
                return null; // IBeam-Systemcursor genügt

            case EinfügeArt.Unterschrift:
            {
                var u = _vm.AusstehenderUnterschrift;
                if (u is null) return null;
                return new Image
                {
                    Source = Bildwerkzeuge.TransparentesBild(u.Pfad),
                    Width = 100,
                    Height = 50,
                    Stretch = Stretch.Uniform
                };
            }

            case EinfügeArt.Bild:
            {
                var png = _vm.AusstehendesBild;
                if (png is null) return null;
                var quelle = BitmapAusBytes(png);
                double breite = 120;
                double höhe = quelle.PixelHeight > 0
                    ? breite * quelle.PixelHeight / quelle.PixelWidth
                    : breite;
                return new Image
                {
                    Source = quelle,
                    Width = breite,
                    Height = höhe,
                    Stretch = Stretch.Uniform
                };
            }

            case EinfügeArt.Symbol:
            {
                SymbolArt symbolArt = _vm.AusstehendesSymbol;
                (double w, double h) = SymbolVorschauGröße(symbolArt);
                double dicke = 2.5;
                var farbe = new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x7B));
                farbe.Freeze();
                bool gefüllt = symbolArt == SymbolArt.Punkt;
                return new Path
                {
                    Width = w,
                    Height = h,
                    Stretch = Stretch.None,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stroke = gefüllt ? null : farbe,
                    Fill = gefüllt ? farbe : null,
                    StrokeThickness = dicke,
                    Data = SymbolGeometrie(symbolArt, w, h, dicke),
                    IsHitTestVisible = false
                };
            }

            default:
                return null;
        }
    }

    private static (double W, double H) SymbolVorschauGröße(SymbolArt art) => art switch
    {
        SymbolArt.Umranden => (90, 28),
        SymbolArt.Durchstreichen => (90, 10),
        SymbolArt.Punkt => (22, 22),
        SymbolArt.Häkchen => (28, 28),
        _ => (28, 28) // Kreuz
    };

    private void VorschauBewegen(object sender, MouseEventArgs e)
    {
        if (_cursorVorschau == null)
            return;
        Point p = e.GetPosition(OverlayCanvas);

        // Formularfeld-Geist: obere linke Ecke am Mauszeiger (wie beim Platzieren).
        if (_vm.NeuesFeldTyp != null)
        {
            _cursorVorschau.Visibility = Visibility.Visible;
            Canvas.SetLeft(_cursorVorschau, p.X);
            Canvas.SetTop(_cursorVorschau, p.Y);
            return;
        }

        if (_vm.EinfügeModus == null || _vm.EinfügeModus == EinfügeArt.Text)
            return;
        _cursorVorschau.Visibility = Visibility.Visible;
        Canvas.SetLeft(_cursorVorschau, p.X - _cursorVorschau.Width / 2);
        Canvas.SetTop(_cursorVorschau, p.Y - _cursorVorschau.Height / 2);
    }

    /// <summary>Erstellt den am Mauszeiger schwebenden Geist eines neuen Formularfeldes.</summary>
    private FrameworkElement? ErstelleFeldVorschau(EntwurfFeldTyp typ)
    {
        if (_geo is null)
            return null;
        var (breitePt, höhePt) = HauptViewModel.FeldStandardGröße(typ);
        return new Border
        {
            Width = breitePt * _geo.DipProPunkt,
            Height = höhePt * _geo.DipProPunkt,
            Background = FeldHintergrund,
            BorderBrush = Akzent,
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = HauptViewModel.FeldBezeichnung(typ),
                Foreground = Akzent,
                FontSize = 10,
                Margin = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
    }

    /// <summary>
    /// Lässt das Mausrad über den Seitenrand hinaus zur nächsten bzw. vorigen
    /// Seite blättern: Innerhalb der Seite wird normal gescrollt; erst am unteren
    /// Rand schaltet ein weiteres Abwärtsrad zur nächsten Seite (oben am Rand ein
    /// Aufwärtsrad zur vorigen, dort ans Seitenende springend).
    /// </summary>
    private void BlätterBeiRand(MouseWheelEventArgs e)
    {
        if (_vm.SeitenInfos.Count == 0)
            return;

        const double rand = 1.0;
        bool obenAmRand = SeitenScroll.VerticalOffset <= rand;
        bool untenAmRand = SeitenScroll.VerticalOffset >= SeitenScroll.ScrollableHeight - rand;

        if (e.Delta < 0 && untenAmRand && _vm.AktuelleSeite < _vm.SeitenInfos.Count - 1)
        {
            _vm.AktuelleSeite++;
            SeitenScroll.ScrollToVerticalOffset(0);
            e.Handled = true;
        }
        else if (e.Delta > 0 && obenAmRand && _vm.AktuelleSeite > 0)
        {
            _vm.AktuelleSeite--;
            // Erst nach dem Neuzeichnen ans Ende der nun vorigen Seite springen.
            Dispatcher.BeginInvoke(new Action(() =>
                SeitenScroll.ScrollToVerticalOffset(SeitenScroll.ScrollableHeight)),
                System.Windows.Threading.DispatcherPriority.Loaded);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Zoomt die Seitenansicht so, dass der Inhaltspunkt direkt unter dem
    /// Mauszeiger nach dem Zoom an derselben Bildschirmposition verbleibt
    /// (klassisches „zoom to cursor"-Verhalten).
    /// </summary>
    /// <remarks>
    /// Das ScrollViewer-Padding von 16 px ist fester Randabstand und skaliert
    /// NICHT mit dem Zoomfaktor. Daher wird der Inhaltspunkt in zwei Anteile
    /// zerlegt: den skalierenden Seitenanteil (Maus-X minus 16 px Padding plus
    /// HorizontalOffset minus 16 px Padding) und den konstanten Padding-Anteil.
    /// Nach dem Zoom wird nur der Seitenanteil mit verhältnis = neuerZoom /
    /// alterZoom skaliert, das Padding bleibt unverändert.
    /// </remarks>
    private void ZumZeigerZoomen(MouseWheelEventArgs e)
    {
        const double padding = 16.0; // Padding des SeitenScroll-ScrollViewers

        // Position des Mauszeigers relativ zum Viewport des ScrollViewers.
        Point maus = e.GetPosition(SeitenScroll);

        // Aktuellen Scroll-Offset und Zoomfaktor vor der Änderung merken.
        double offsetXVorher = SeitenScroll.HorizontalOffset;
        double offsetYVorher = SeitenScroll.VerticalOffset;
        double zoomVorher = _vm.Zoom;

        // Zoom ausführen. ZoomDurchRad → ZoomÄndern → Zoom-Setter + AktuelleSeiteNeu.
        // ZeichneAktuelleSeite wird synchron aufgerufen und setzt SeitenBild.Width/Height.
        _vm.ZoomDurchRad(e.Delta);

        double zoomNachher = _vm.Zoom;

        // Wenn der Zoom durch Klemmung (0.25–4.0) nicht verändert wurde, nicht scrollen.
        if (Math.Abs(zoomNachher - zoomVorher) < 1e-9)
            return;

        double verhältnis = zoomNachher / zoomVorher;

        // Seitenanteil des Inhaltspunkts unter dem Mauszeiger (ohne Padding):
        //   inhaltX = offsetXVorher + maus.X  →  gesamter Inhaltspunkt im gescrollten Koordinatensystem
        //   davon wird padding abgezogen, um den reinen Seitenanteil zu erhalten.
        double seitenInhaltXVorher = (offsetXVorher + maus.X) - padding;
        double seitenInhaltYVorher = (offsetYVorher + maus.Y) - padding;

        // Nach dem Zoom skaliert der Seitenanteil proportional zum Zoomverhältnis.
        double seitenInhaltXNachher = seitenInhaltXVorher * verhältnis;
        double seitenInhaltYNachher = seitenInhaltYVorher * verhältnis;

        // Neuer Scroll-Offset: der Mauszeiger soll denselben Inhaltspunkt zeigen.
        //   neuerOffset = (padding + seitenInhaltXNachher) - maus.X
        double neuerOffsetX = padding + seitenInhaltXNachher - maus.X;
        double neuerOffsetY = padding + seitenInhaltYNachher - maus.Y;

        // Layout muss vollständig durchgelaufen sein, damit ScrollableWidth/Height
        // die neue Seitengröße widerspiegeln. ZeichneAktuelleSeite hat die
        // Größen bereits gesetzt, aber der ScrollViewer kennt sie erst nach
        // UpdateLayout.
        SeitenScroll.UpdateLayout();

        SeitenScroll.ScrollToHorizontalOffset(Math.Max(0, neuerOffsetX));
        SeitenScroll.ScrollToVerticalOffset(Math.Max(0, neuerOffsetY));
    }

    // ----- Drag-and-drop der Seiten ----------------------------------------

    // Explizite Implementierung von IDropTarget, damit keine UIElement-Member verdeckt werden.
    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is SeitenElement)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not SeitenElement)
            return;

        int von = dropInfo.DragInfo.SourceIndex;
        int nach = dropInfo.InsertIndex;
        // Beim Ziehen nach unten verschiebt sich der Zielindex nach dem Entfernen.
        if (nach > von)
            nach--;
        nach = Math.Clamp(nach, 0, _vm.Seiten.Count - 1);
        _vm.SeiteVerschoben(von, nach);
    }

    // Restliche IDropTarget-Mitglieder werden nicht benötigt.
    void IDropTarget.DragEnter(IDropInfo dropInfo) { }
    void IDropTarget.DragLeave(IDropInfo dropInfo) { }
    void IDropTarget.DropHint(IDropHintInfo dropHintInfo) { }

}
