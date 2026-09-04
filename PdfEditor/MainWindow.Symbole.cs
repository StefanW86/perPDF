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

/// <summary>Symbolmarkierungen (Kreuz, Häkchen, Punkt, Umrandung, Durchstreichung) und ihre Overlays.</summary>
public partial class MainWindow
{
    // ----- Symbolmarkierungen (Kreuz, Häkchen, Punkt, Umranden, Durchstreichen) -----

    /// <summary>Wird vom Dropdown „Markierung“ aufgerufen und fügt das gewählte Symbol ein.</summary>
    private void SymbolMenü_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: SymbolArt art })
            _vm.SymbolEinfügen(art);
    }

    private void FeldMenü_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: EntwurfFeldTyp typ })
            _vm.FeldErstellenStarten(typ);
    }

    private void FeldAuswahlSchließen_Click(object sender, RoutedEventArgs e)
        => _vm.AusgewähltesFeld = null;

    private void FeldLöschen_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.AusgewähltesFeld is { } feld)
            _vm.FeldEntfernen(feld);
    }

    private void FügeSymbolHinzu(Symbolmarkierung s, SeitenGeometrie geo)
    {
        Rect r = geo.RechteckNachAnzeige(s.X, s.Y, s.X + s.Breite, s.Y + s.Höhe);
        var behälter = ErzeugeBehälter(r);
        behälter.Focusable = true;

        double dicke = Math.Max(1, s.Strichbreite * geo.DipProPunkt);
        var farbe = new SolidColorBrush(WpfFarbe(s.Farbe));
        farbe.Freeze();
        bool gefüllt = s.Art == SymbolArt.Punkt;

        var form = new Path
        {
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        if (gefüllt)
            form.Fill = farbe;
        else
        {
            form.Stroke = farbe;
            form.StrokeThickness = dicke;
        }
        behälter.Children.Add(form);

        void FormAktualisieren()
            => form.Data = SymbolGeometrie(s.Art, behälter.Width, behälter.Height, dicke);
        FormAktualisieren();

        // Farbe live auf die Form anwenden (ohne Neuzeichnen, damit der Fokus bleibt).
        void FarbeAnwenden(string hex)
        {
            s.Farbe = hex;
            var neueFarbe = new SolidColorBrush(WpfFarbe(hex));
            neueFarbe.Freeze();
            if (gefüllt) form.Fill = neueFarbe;
            else form.Stroke = neueFarbe;
        }

        // Rahmen ist zunächst unsichtbar – erscheint nur bei Fokus.
        var rahmen = AuswahlRahmen();
        rahmen.Visibility = Visibility.Collapsed;
        behälter.Children.Add(rahmen);

        // Schwebende Farbleiste – als direktes Canvas-Geschwister, NICHT als Kind des
        // (u. U. winzigen) Symbol-Containers: Ein Grid-Kind würde bei zu kleiner Zelle
        // auf deren Breite beschnitten, sodass das Farbfeld verschwindet. Auf dem Canvas
        // behält die Leiste stets ihre volle, natürliche Größe – unabhängig von der
        // Markierungsgröße.
        var farbLeiste = ErstelleSymbolFarbLeiste(s, FarbeAnwenden);
        farbLeiste.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(farbLeiste, 998);
        OverlayCanvas.Children.Add(farbLeiste);

        void PositioniereLeiste()
        {
            farbLeiste.UpdateLayout();
            double bh = farbLeiste.ActualHeight > 0 ? farbLeiste.ActualHeight : 30;
            double links = Canvas.GetLeft(behälter);
            double oben = Canvas.GetTop(behälter);
            double y = oben - bh - 4;
            if (y < 0)
                y = oben + behälter.Height + 4; // am oberen Seitenrand: unter das Symbol
            Canvas.SetLeft(farbLeiste, links);
            Canvas.SetTop(farbLeiste, y);
        }

        StatteAdornerAus(behälter, geo, ganzflächigesZiehen: true,
            aktualisieren: (l, o, b, h) =>
            {
                SetzeRechteck(geo, l, o, b, h, (x, y, w, hh) =>
                {
                    s.X = x; s.Y = y; s.Breite = w; s.Höhe = hh;
                });
                FormAktualisieren();
                PositioniereLeiste(); // Leiste beim Verschieben/Skalieren mitführen
            },
            entfernen: () => _vm.SymbolEntfernen(s),
            seitenverhältnisHalten: AspektHalten(s.Art),
            nurBeiFokus: rahmen);

        behälter.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                farbLeiste.Visibility = Visibility.Visible;
                PositioniereLeiste();
            }
            else
            {
                farbLeiste.Visibility = Visibility.Collapsed;
            }
        };

        OverlayCanvas.Children.Add(behälter);
    }

    /// <summary>
    /// Symbolmarkierungen behalten beim nachträglichen Skalieren ihr ursprüngliches
    /// Seitenverhältnis bei – außer die Umrandung (Rechteck), die sich frei in Breite
    /// und Höhe ziehen lassen soll.
    /// </summary>
    private static bool AspektHalten(SymbolArt art) => art != SymbolArt.Umranden;

    /// <summary>
    /// Baut die Vektorform eines Symbols in einem Rechteck (Anzeigekoordinaten,
    /// Ursprung oben links). <paramref name="pad"/> hält Strichränder vom Rand frei.
    /// Identische relative Formeln wie beim Einzeichnen ins PDF
    /// (<c>PdfDokumentDienst.SymboleZeichnen</c>).
    /// </summary>
    private static Geometry SymbolGeometrie(SymbolArt art, double w, double h, double pad)
    {
        pad = Math.Min(pad, Math.Min(w, h) / 2);
        double iw = Math.Max(0, w - 2 * pad);
        double ih = Math.Max(0, h - 2 * pad);

        switch (art)
        {
            case SymbolArt.Häkchen:
            {
                var fig = new PathFigure { StartPoint = new Point(pad, pad + ih * 0.55) };
                fig.Segments.Add(new LineSegment(new Point(pad + iw * 0.4, h - pad), true));
                fig.Segments.Add(new LineSegment(new Point(w - pad, pad), true));
                var pg = new PathGeometry();
                pg.Figures.Add(fig);
                return pg;
            }
            case SymbolArt.Punkt:
                return new EllipseGeometry(new Point(w / 2, h / 2), iw / 2, ih / 2);
            case SymbolArt.Umranden:
            {
                double radius = Math.Min(iw, ih) * 0.15;
                return new RectangleGeometry(new Rect(pad, pad, iw, ih), radius, radius);
            }
            case SymbolArt.Durchstreichen:
                return new LineGeometry(new Point(pad, h / 2), new Point(w - pad, h / 2));
            default: // Kreuz
            {
                var g = new GeometryGroup();
                g.Children.Add(new LineGeometry(new Point(pad, pad), new Point(w - pad, h - pad)));
                g.Children.Add(new LineGeometry(new Point(w - pad, pad), new Point(pad, h - pad)));
                return g;
            }
        }
    }

}
