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

/// <summary>Freihand-Zeichnen/Radierer und gemeinsame Bausteine für verschieb-/skalierbare Overlays.</summary>
public partial class MainWindow
{
    // ----- Freihand-Zeichnen des Leuchtstifts ------------------------------

    private void OverlayMausUnten(object sender, MouseButtonEventArgs e)
    {
        if (_geo is null)
            return;

        // Platziermodus: Klick auf Seite → Objekt an dieser Stelle einfügen.
        if (_vm.EinfügeModus != null)
        {
            Point canvas = e.GetPosition(OverlayCanvas);
            Point pdf = _geo.AnzeigeNachPunkt(canvas.X, canvas.Y);
            switch (_vm.EinfügeModus)
            {
                case EinfügeArt.Text:
                    _vm.TextAnPosition(pdf.X, pdf.Y);
                    break;
                case EinfügeArt.Unterschrift:
                    _vm.UnterschriftAnPosition(pdf.X, pdf.Y);
                    break;
                case EinfügeArt.Symbol:
                    _vm.SymbolAnPosition(pdf.X, pdf.Y);
                    break;
                case EinfügeArt.Bild:
                    _vm.BildAnPosition(pdf.X, pdf.Y);
                    break;
            }
            e.Handled = true;
            return;
        }

        // Formularfeld-Platziermodus: Klick legt ein neues Entwurfsfeld an.
        if (_vm.NeuesFeldTyp != null)
        {
            Point canvas = e.GetPosition(OverlayCanvas);
            Point pdf = _geo.AnzeigeNachPunkt(canvas.X, canvas.Y);
            _vm.FeldAnPosition(pdf.X, pdf.Y);
            e.Handled = true;
            return;
        }

        if (_vm.RadiererModus)
        {
            _radiererZieht = true;
            OverlayCanvas.CaptureMouse();
            RadiererAnwenden(Begrenzen(e.GetPosition(OverlayCanvas)));
            e.Handled = true;
            return;
        }

        if (!_vm.MarkerModus)
        {
            // Klick auf die leere Seitenfläche hebt eine bestehende Auswahl auf.
            if (ReferenceEquals(e.OriginalSource, OverlayCanvas))
            {
                OverlayCanvas.Focus();
                _vm.AusgewähltesFeld = null;
            }
            return;
        }

        int seite = _vm.AktuelleSeite;
        if (seite < 0)
            return;

        _aktiverStrich = new Textmarkierung(seite);
        Point p = Begrenzen(e.GetPosition(OverlayCanvas));
        _aktiverStrich.Punkte.Add(_geo.AnzeigeNachPunkt(p.X, p.Y));

        _strichVorschau = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 230, 0)),
            StrokeThickness = _aktiverStrich.Strichbreite * _geo.DipProPunkt,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        _strichVorschau.Points.Add(p);
        OverlayCanvas.Children.Add(_strichVorschau);

        _markerZeichnet = true;
        OverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OverlayMausBewegung(object sender, MouseEventArgs e)
    {
        if (_radiererZieht && _geo is not null)
        {
            RadiererAnwenden(Begrenzen(e.GetPosition(OverlayCanvas)));
            e.Handled = true;
            return;
        }

        if (!_markerZeichnet || _geo is null || _aktiverStrich is null || _strichVorschau is null)
            return;

        Point p = Begrenzen(e.GetPosition(OverlayCanvas));
        // Nur bei etwas Abstand übernehmen – das glättet den Strich und spart Punkte.
        if (_strichVorschau.Points.Count > 0)
        {
            Point letzt = _strichVorschau.Points[^1];
            if (Math.Abs(letzt.X - p.X) < 2 && Math.Abs(letzt.Y - p.Y) < 2)
                return;
        }
        _strichVorschau.Points.Add(p);
        _aktiverStrich.Punkte.Add(_geo.AnzeigeNachPunkt(p.X, p.Y));
        e.Handled = true;
    }

    private void OverlayMausOben(object sender, MouseButtonEventArgs e)
    {
        if (_radiererZieht)
        {
            _radiererZieht = false;
            OverlayCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (!_markerZeichnet)
            return;

        _markerZeichnet = false;
        OverlayCanvas.ReleaseMouseCapture();
        if (_strichVorschau is not null)
        {
            OverlayCanvas.Children.Remove(_strichVorschau);
            _strichVorschau = null;
        }
        var strich = _aktiverStrich;
        _aktiverStrich = null;
        e.Handled = true;

        if (strich is null || strich.Punkte.Count < 2)
            return; // bloßer Klick – kein Strich

        _vm.Textmarkierungen.Add(strich);
        // Als verwaltetes, auswähl- und verschiebbares Overlay neu aufbauen.
        ZeichneAktuelleSeite();
    }

    private Point Begrenzen(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(0, OverlayCanvas.Width)),
        Math.Clamp(p.Y, 0, Math.Max(0, OverlayCanvas.Height)));

    /// <summary>Entfernt den Markierungsstrich, der unter dem Punkt liegt (Radiergummi).</summary>
    private void RadiererAnwenden(Point p)
    {
        if (_geo is null)
            return;
        int index = _vm.AktuelleSeite;

        Textmarkierung? treffer = null;
        foreach (var h in _vm.Textmarkierungen)
        {
            if (h.SeitenIndex != index || h.Punkte.Count < 2)
                continue;

            double toleranz = h.Strichbreite * _geo.DipProPunkt / 2 + 3;
            var pts = h.Punkte.Select(q => _geo.PunktNachAnzeige(q.X, q.Y)).ToList();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (PunktSegmentAbstand(p, pts[i], pts[i + 1]) <= toleranz)
                {
                    treffer = h;
                    break;
                }
            }
            if (treffer is not null)
                break;
        }

        if (treffer is not null)
            _vm.TextmarkierungEntfernen(treffer); // löst Neuzeichnen aus
    }

    /// <summary>Kürzester Abstand eines Punktes zu einer Strecke.</summary>
    private static double PunktSegmentAbstand(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0)
            return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        var nächster = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - nächster).Length;
    }

    // ----- gemeinsame Bausteine für verschieb-/skalierbare Overlays ---------

    private static Grid ErzeugeBehälter(Rect r)
    {
        var g = new Grid { Width = r.Width, Height = r.Height };
        Canvas.SetLeft(g, r.Left);
        Canvas.SetTop(g, r.Top);
        return g;
    }

    private static Border AuswahlRahmen() => new()
    {
        BorderBrush = Akzent,
        BorderThickness = new Thickness(1),
        IsHitTestVisible = false
    };

    /// <summary>
    /// Fügt Ziehgriffe zum Verschieben, Skalieren und eine Löschen-Schaltfläche hinzu.
    /// Wenn <paramref name="nurBeiFokus"/> übergeben wird, sind Skalieren, Löschen und das
    /// übergebene Extra-Element (z. B. Auswahlrahmen) nur bei Tastaturfokus sichtbar –
    /// das Klicken auf den Behälter gibt ihm automatisch den Fokus.
    /// </summary>
    private void StatteAdornerAus(Grid behälter, SeitenGeometrie geo, bool ganzflächigesZiehen,
        Action<double, double, double, double> aktualisieren, Action entfernen,
        bool seitenverhältnisHalten = false, UIElement? nurBeiFokus = null, bool randZiehen = false)
    {
        if (randZiehen)
            FügeRandGriffHinzu(behälter, aktualisieren);
        else
            FügeZiehGriffHinzu(behälter, ganzflächigesZiehen, aktualisieren);

        var skalieren = new Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Background = Akzent
        };
        double aspekt = behälter.Height > 0 ? behälter.Width / behälter.Height : 1.0;
        skalieren.DragDelta += (_, e) =>
        {
            double neueBreite = Math.Max(20, behälter.Width + e.HorizontalChange);
            double neueHöhe = seitenverhältnisHalten
                ? neueBreite / aspekt
                : Math.Max(10, behälter.Height + e.VerticalChange);
            behälter.Width = neueBreite;
            behälter.Height = neueHöhe;
            aktualisieren(Canvas.GetLeft(behälter), Canvas.GetTop(behälter), behälter.Width, behälter.Height);
        };
        behälter.Children.Add(skalieren);

        var löschen = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 18,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        löschen.Click += (_, _) => entfernen();
        behälter.Children.Add(löschen);

        if (nurBeiFokus != null)
        {
            // Skalieren und Löschen starten ebenfalls unsichtbar.
            skalieren.Visibility = Visibility.Collapsed;
            löschen.Visibility = Visibility.Collapsed;

            // Klick auf Behälter → Fokus setzen → Chrome einblenden.
            // Preview (tunnelnd), weil der ganzflächige Zieh-Thumb das bubbelnde
            // MouseLeftButtonDown als behandelt markiert und es so den Behälter nie erreicht.
            behälter.PreviewMouseLeftButtonDown += (_, _) => behälter.Focus();
            behälter.IsKeyboardFocusWithinChanged += (_, e) =>
            {
                var v = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
                nurBeiFokus.Visibility = v;
                skalieren.Visibility = v;
                löschen.Visibility = v;
            };

            // Entf-Taste entfernt nur die aktuell angeklickte (fokussierte) Markierung.
            behälter.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Delete)
                {
                    entfernen();
                    e.Handled = true;
                }
            };
        }
    }

    /// <summary>Fügt einen Ziehgriff hinzu: ganzflächig unsichtbar oder als kleiner Griff oben links.</summary>
    private void FügeZiehGriffHinzu(Grid behälter, bool ganzflächigesZiehen,
        Action<double, double, double, double> aktualisieren)
    {
        var ziehen = new Thumb { Cursor = Cursors.SizeAll };
        if (ganzflächigesZiehen)
        {
            // Unsichtbarer, ganzflächiger Griff (für Unterschriften/Symbole).
            ziehen.Opacity = 0;
            ziehen.Background = Brushes.Transparent;
        }
        else
        {
            // Kleiner sichtbarer Griff oben links (für Textfelder).
            ziehen.Width = 14;
            ziehen.Height = 14;
            ziehen.HorizontalAlignment = HorizontalAlignment.Left;
            ziehen.VerticalAlignment = VerticalAlignment.Top;
            ziehen.Background = Akzent;
        }
        ziehen.DragDelta += (_, e) =>
        {
            double links = Math.Clamp(Canvas.GetLeft(behälter) + e.HorizontalChange,
                0, Math.Max(0, OverlayCanvas.Width - behälter.Width));
            double oben = Math.Clamp(Canvas.GetTop(behälter) + e.VerticalChange,
                0, Math.Max(0, OverlayCanvas.Height - behälter.Height));
            Canvas.SetLeft(behälter, links);
            Canvas.SetTop(behälter, oben);
            aktualisieren(links, oben, behälter.Width, behälter.Height);
        };
        behälter.Children.Add(ziehen);
    }

    /// <summary>
    /// Fügt einen umlaufenden Rand-Ziehgriff hinzu: nur der Rahmenbereich ist anklickbar
    /// (transparenter Rahmen, leere Mitte), sodass sich das Element überall am Rand
    /// verschieben lässt, die Mitte (z. B. anklickbare Optionen) aber frei bleibt.
    /// </summary>
    private void FügeRandGriffHinzu(Grid behälter, Action<double, double, double, double> aktualisieren)
    {
        var griff = new Border
        {
            BorderBrush = Brushes.Transparent, // anklickbar, aber unsichtbar
            BorderThickness = new Thickness(7),
            Background = null,                 // Mitte nicht trefferaktiv → Klicks gehen durch
            Cursor = Cursors.SizeAll
        };

        Point startMaus = default;
        double startL = 0, startO = 0;
        bool zieht = false;
        griff.MouseLeftButtonDown += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, griff))
                return; // Klick in der Mitte → nicht verschieben
            startMaus = e.GetPosition(OverlayCanvas);
            startL = Canvas.GetLeft(behälter);
            startO = Canvas.GetTop(behälter);
            zieht = true;
            griff.CaptureMouse();
            e.Handled = true;
        };
        griff.MouseMove += (_, e) =>
        {
            if (!zieht)
                return;
            Point pos = e.GetPosition(OverlayCanvas);
            double links = Math.Clamp(startL + pos.X - startMaus.X,
                0, Math.Max(0, OverlayCanvas.Width - behälter.Width));
            double oben = Math.Clamp(startO + pos.Y - startMaus.Y,
                0, Math.Max(0, OverlayCanvas.Height - behälter.Height));
            Canvas.SetLeft(behälter, links);
            Canvas.SetTop(behälter, oben);
            aktualisieren(links, oben, behälter.Width, behälter.Height);
        };
        griff.MouseLeftButtonUp += (_, e) =>
        {
            if (!zieht)
                return;
            zieht = false;
            griff.ReleaseMouseCapture();
            e.Handled = true;
        };
        behälter.Children.Add(griff);
    }

    /// <summary>Rechnet ein Anzeige-Rechteck in PDF-Punkte um und übergibt sie an den Setzer.</summary>
    private static void SetzeRechteck(SeitenGeometrie geo, double links, double oben,
        double breite, double höhe, Action<double, double, double, double> setzePunkte)
    {
        var (x1, y1, x2, y2) = geo.AnzeigeRechteckNachPunkten(new Rect(links, oben, breite, höhe));
        setzePunkte(x1, y1, x2 - x1, y2 - y1);
    }

}
