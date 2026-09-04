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

/// <summary>Overlays für Unterschriften, Textnotizen und Textmarker samt Format-/Farbleisten.</summary>
public partial class MainWindow
{
    // ----- Unterschrift-Overlays (verschieb- und skalierbar) ---------------

    private void FügeUnterschriftHinzu(UnterschriftPlatzierung p, SeitenGeometrie geo)
    {
        Rect r = geo.RechteckNachAnzeige(p.X, p.Y, p.X + p.Breite, p.Y + p.Höhe);
        var behälter = ErzeugeBehälter(r);
        behälter.Focusable = true;

        behälter.Children.Add(new Image
        {
            // Vorschau mit transparentem Hintergrund (wie beim Speichern).
            Source = Bildwerkzeuge.TransparentesBild(p.Unterschrift.Pfad),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        });

        // Auswahlrahmen nur sichtbar, solange die Unterschrift angeklickt (fokussiert) ist.
        var rahmen = AuswahlRahmen();
        rahmen.Visibility = Visibility.Collapsed;
        behälter.Children.Add(rahmen);

        StatteAdornerAus(behälter, geo, ganzflächigesZiehen: true,
            aktualisieren: (l, o, b, h) => SetzeRechteck(geo, l, o, b, h, (x, y, w, hh) =>
            {
                p.X = x; p.Y = y; p.Breite = w; p.Höhe = hh;
            }),
            entfernen: () => _vm.PlatzierungEntfernen(p),
            seitenverhältnisHalten: true,
            nurBeiFokus: rahmen);

        OverlayCanvas.Children.Add(behälter);
    }

    private void FügeTextHinzu(TextNotiz t, SeitenGeometrie geo)
    {
        Rect r = geo.RechteckNachAnzeige(t.X, t.Y, t.X + t.Breite, t.Y + t.Höhe);

        // Der Rahmen umgibt den Textbereich mit einem 5px-Greifrand. Der Behälter
        // ist daher um 2*rand größer als der Textbereich; die Oberkante bleibt fix.
        const double rand = 5;
        var behälter = new Grid { Width = Math.Max(20, r.Width) + 2 * rand };
        Canvas.SetLeft(behälter, r.Left - rand);
        Canvas.SetTop(behälter, r.Top - rand);

        // Der Rahmen dient zugleich als Greifrand zum Verschieben.
        var rahmen = new Border
        {
            BorderBrush = Akzent,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(5),
            Cursor = Cursors.SizeAll,
            Focusable = true
        };

        var tb = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(1)
        };
        TextStilAnwenden(tb, t, geo);
        tb.SetBinding(TextBox.TextProperty, new Binding(nameof(TextNotiz.Text))
        {
            Source = t,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        rahmen.Child = tb;
        behälter.Children.Add(rahmen);

        // Modell aus der aktuellen Anzeige-Geometrie nachführen (Position/Größe).
        // Der Greifrand wird abgezogen, damit das Modell dem Textbereich entspricht
        // und der gespeicherte Text zur Anzeige passt.
        void Nachführen()
        {
            double w = (behälter.ActualWidth > 0 ? behälter.ActualWidth : behälter.Width) - 2 * rand;
            double h = behälter.ActualHeight - 2 * rand;
            if (w <= 0 || h <= 0)
                return;
            SetzeRechteck(geo, Canvas.GetLeft(behälter) + rand, Canvas.GetTop(behälter) + rand, w, h,
                (x, y, bw, bh) => { t.X = x; t.Y = y; t.Breite = bw; t.Höhe = bh; });
        }
        // Wächst das Feld (größere Schrift/mehr Text), bleibt die Oberkante fix.
        behälter.SizeChanged += (_, _) => Nachführen();

        // Verschieben durch Ziehen am Rahmenrand (nicht im Textbereich).
        Point startMaus = default;
        double startL = 0, startO = 0;
        bool zieht = false;
        rahmen.MouseLeftButtonDown += (_, e) =>
        {
            if (InSpezialModus)
                return;
            if (!ReferenceEquals(e.OriginalSource, rahmen))
                return; // Klick im Textbereich → bearbeiten statt verschieben
            rahmen.Focus();
            startMaus = e.GetPosition(OverlayCanvas);
            startL = Canvas.GetLeft(behälter);
            startO = Canvas.GetTop(behälter);
            zieht = true;
            rahmen.CaptureMouse();
            e.Handled = true;
        };
        rahmen.MouseMove += (_, e) =>
        {
            if (!zieht)
                return;
            Point pos = e.GetPosition(OverlayCanvas);
            double nl = Math.Clamp(startL + pos.X - startMaus.X,
                0, Math.Max(0, OverlayCanvas.Width - behälter.ActualWidth));
            double no = Math.Clamp(startO + pos.Y - startMaus.Y,
                0, Math.Max(0, OverlayCanvas.Height - behälter.ActualHeight));
            Canvas.SetLeft(behälter, nl);
            Canvas.SetTop(behälter, no);
            Nachführen();
        };
        rahmen.MouseLeftButtonUp += (_, e) =>
        {
            if (!zieht)
                return;
            zieht = false;
            rahmen.ReleaseMouseCapture();
            Nachführen();
            e.Handled = true;
        };

        // Schmaler Griff am rechten Rand zum Anpassen der Breite (Höhe wächst selbst).
        var breiteGriff = new Thumb
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Cursor = Cursors.SizeWE,
            Background = Akzent,
            Opacity = 0.6
        };
        breiteGriff.DragDelta += (_, e) =>
        {
            double aktuell = behälter.ActualWidth > 0 ? behälter.ActualWidth : behälter.Width;
            behälter.Width = Math.Max(30, aktuell + e.HorizontalChange);
            Nachführen();
        };

        // Formatierungsleiste über dem Feld (am oberen Seitenrand darunter).
        var leiste = ErstelleFormatLeiste(t, tb, geo);
        if (r.Top >= 36)
        {
            leiste.VerticalAlignment = VerticalAlignment.Top;
            leiste.Margin = new Thickness(0, -34, 0, 0);
        }
        else
        {
            leiste.VerticalAlignment = VerticalAlignment.Bottom;
            leiste.Margin = new Thickness(0, 0, 0, -34);
        }

        var löschen = LöschSchaltfläche(() => _vm.TextNotizEntfernen(t));

        behälter.Children.Add(breiteGriff);
        behälter.Children.Add(leiste);
        behälter.Children.Add(löschen);

        // Rahmen und Griffe nur sichtbar, solange der Fokus im Element liegt.
        UIElement[] chrome = { breiteGriff, leiste, löschen };
        foreach (var c in chrome)
            c.Visibility = Visibility.Collapsed;
        behälter.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            bool an = (bool)e.NewValue;
            foreach (var c in chrome)
                c.Visibility = an ? Visibility.Visible : Visibility.Collapsed;
            rahmen.BorderThickness = new Thickness(an ? 1 : 0);
        };

        OverlayCanvas.Children.Add(behälter);

        // Frisch eingefügten Text gleich zum Tippen fokussieren.
        if (ReferenceEquals(_vm.ZuletztEingefügteNotiz, t))
        {
            _vm.ZuletztEingefügteNotiz = null;
            tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        }
    }

    private static void TextStilAnwenden(TextBox tb, TextNotiz t, SeitenGeometrie geo)
    {
        tb.FontSize = Math.Max(6, t.FontGröße * geo.DipProPunkt);
        tb.FontWeight = t.Fett ? FontWeights.Bold : FontWeights.Normal;
        tb.FontStyle = t.Kursiv ? FontStyles.Italic : FontStyles.Normal;
        tb.TextDecorations = t.Unterstrichen ? TextDecorations.Underline : null;
        tb.Foreground = new SolidColorBrush(WpfFarbe(t.Farbe));
        // Optionaler Hintergrund (z. B. zum Überdecken einer eingebrannten Beschriftung).
        tb.Background = string.IsNullOrEmpty(t.Hintergrund)
            ? Brushes.Transparent
            : new SolidColorBrush(WpfFarbe(t.Hintergrund));
    }

    /// <summary>Baut die schwebende Leiste mit Schriftgröße, Auszeichnungen und Farben.</summary>
    private FrameworkElement ErstelleFormatLeiste(TextNotiz t, TextBox tb, SeitenGeometrie geo)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        panel.Children.Add(KleineSchaltfläche("A−", () => SchriftgrößeÄndern(t, tb, geo, -2)));
        panel.Children.Add(KleineSchaltfläche("A+", () => SchriftgrößeÄndern(t, tb, geo, +2)));
        panel.Children.Add(Trenner());

        var fett = new ToggleButton
        {
            Content = "F",
            IsChecked = t.Fett,
            Width = 24,
            Height = 22,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0)
        };
        fett.Checked += (_, _) => { t.Fett = true; TextStilAnwenden(tb, t, geo); };
        fett.Unchecked += (_, _) => { t.Fett = false; TextStilAnwenden(tb, t, geo); };

        var kursiv = new ToggleButton
        {
            Content = "K",
            IsChecked = t.Kursiv,
            Width = 24,
            Height = 22,
            FontStyle = FontStyles.Italic,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0)
        };
        kursiv.Checked += (_, _) => { t.Kursiv = true; TextStilAnwenden(tb, t, geo); };
        kursiv.Unchecked += (_, _) => { t.Kursiv = false; TextStilAnwenden(tb, t, geo); };

        var unterstrichen = new ToggleButton
        {
            Content = new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline },
            IsChecked = t.Unterstrichen,
            Width = 24,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0)
        };
        unterstrichen.Checked += (_, _) => { t.Unterstrichen = true; TextStilAnwenden(tb, t, geo); };
        unterstrichen.Unchecked += (_, _) => { t.Unterstrichen = false; TextStilAnwenden(tb, t, geo); };

        panel.Children.Add(fett);
        panel.Children.Add(kursiv);
        panel.Children.Add(unterstrichen);
        panel.Children.Add(Trenner());

        panel.Children.Add(FarbButton(() => t.Farbe, hex =>
        {
            t.Farbe = hex;
            TextStilAnwenden(tb, t, geo);
        }));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = Akzent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
    }

    private static void SchriftgrößeÄndern(TextNotiz t, TextBox tb, SeitenGeometrie geo, double delta)
    {
        t.FontGröße = Math.Clamp(t.FontGröße + delta, 6, 96);
        TextStilAnwenden(tb, t, geo);
    }

    /// <summary>
    /// Kleiner Farbknopf, der die aktuelle Farbe zeigt und beim Klick den RGB-Farbwähler
    /// öffnet. <paramref name="holen"/> liefert die aktuelle Farbe, <paramref name="setzen"/>
    /// übernimmt die neue.
    /// </summary>
    private System.Windows.Controls.Button FarbButton(Func<string> holen, Action<string> setzen)
    {
        var knopf = new System.Windows.Controls.Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            Background = new SolidColorBrush(WpfFarbe(holen())),
            ToolTip = "Farbe ändern (RGB)",
            // Nicht fokussierbar: ein Klick darf den Fokus des umgebenden Elements nicht
            // verlieren, sonst würde die nur-bei-Fokus sichtbare Leiste vorzeitig verschwinden.
            Focusable = false
        };
        knopf.Click += (_, _) => Controls.FarbWähler.Zeigen(knopf, holen(), hex =>
        {
            setzen(hex);
            knopf.Background = new SolidColorBrush(WpfFarbe(hex));
        });
        return knopf;
    }

    /// <summary>Schwebende Leiste mit Farbknopf über einer Symbolmarkierung (analog zur Textleiste).</summary>
    private FrameworkElement ErstelleSymbolFarbLeiste(Symbolmarkierung s, Action<string> farbeAnwenden)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = "Farbe",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 5, 0),
            FontSize = 11
        });
        panel.Children.Add(FarbButton(() => s.Farbe, farbeAnwenden));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = Akzent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
    }

    private static System.Windows.Controls.Button KleineSchaltfläche(string inhalt, Action aktion)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = inhalt,
            Width = 24,
            Height = 22,
            FontSize = 11,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0)
        };
        b.Click += (_, _) => aktion();
        return b;
    }

    private static Border Trenner() => new()
    {
        Width = 1,
        Margin = new Thickness(3, 1, 3, 1),
        Background = Brushes.LightGray
    };

    private static System.Windows.Controls.Button LöschSchaltfläche(Action entfernen)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 18,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        b.Click += (_, _) => entfernen();
        return b;
    }

    private static Color WpfFarbe(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Black; }
    }

    private void FügeTextmarkerHinzu(Textmarkierung h, SeitenGeometrie geo)
    {
        if (h.Punkte.Count < 2)
            return;

        // Strichpunkte in Anzeigekoordinaten umrechnen und Bounding-Box bilden.
        var anzeige = h.Punkte.Select(p => geo.PunktNachAnzeige(p.X, p.Y)).ToList();
        double minX = anzeige.Min(p => p.X);
        double minY = anzeige.Min(p => p.Y);
        double maxX = anzeige.Max(p => p.X);
        double maxY = anzeige.Max(p => p.Y);

        double dicke = h.Strichbreite * geo.DipProPunkt;
        double rand = dicke / 2 + 2;
        double links = minX - rand;
        double oben = minY - rand;

        var behälter = new Grid
        {
            Width = (maxX - minX) + 2 * rand,
            Height = (maxY - minY) + 2 * rand,
            Focusable = true
        };
        Canvas.SetLeft(behälter, links);
        Canvas.SetTop(behälter, oben);
        var transform = new TranslateTransform();
        behälter.RenderTransform = transform;

        var linie = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 230, 0)),
            StrokeThickness = dicke,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Cursor = Cursors.SizeAll
        };
        foreach (var p in anzeige)
            linie.Points.Add(new Point(p.X - links, p.Y - oben));
        behälter.Children.Add(linie);

        // Auswahlrahmen und Löschen-Schaltfläche – nur bei Auswahl sichtbar.
        var rahmen = new Border
        {
            BorderBrush = Akzent,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        var löschen = LöschSchaltfläche(() => _vm.TextmarkierungEntfernen(h));
        löschen.Visibility = Visibility.Collapsed;
        behälter.Children.Add(rahmen);
        behälter.Children.Add(löschen);

        behälter.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            var sichtbar = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            rahmen.Visibility = sichtbar;
            löschen.Visibility = sichtbar;
        };

        // Verschieben des gesamten Striches durch Ziehen am Strich.
        Point start = default;
        bool zieht = false;
        linie.MouseLeftButtonDown += (_, e) =>
        {
            if (_vm.MarkerModus)
                return; // im Zeichenmodus wird gezeichnet, nicht verschoben
            behälter.Focus();
            start = e.GetPosition(OverlayCanvas);
            zieht = true;
            linie.CaptureMouse();
            e.Handled = true;
        };
        linie.MouseMove += (_, e) =>
        {
            if (!zieht)
                return;
            Point akt = e.GetPosition(OverlayCanvas);
            transform.X = akt.X - start.X;
            transform.Y = akt.Y - start.Y;
        };
        linie.MouseLeftButtonUp += (_, e) =>
        {
            if (!zieht)
                return;
            zieht = false;
            linie.ReleaseMouseCapture();
            // Bildschirm-Versatz in einen PDF-Punkt-Versatz umrechnen.
            Point a = geo.AnzeigeNachPunkt(0, 0);
            Point b = geo.AnzeigeNachPunkt(transform.X, transform.Y);
            h.Verschieben(b.X - a.X, b.Y - a.Y);
            transform.X = 0;
            transform.Y = 0;
            ZeichneAktuelleSeite();
            e.Handled = true;
        };

        OverlayCanvas.Children.Add(behälter);
    }

}
