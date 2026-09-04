using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor;

/// <summary>
/// Overlays für eingefügte Bilder: verschieben (ganzflächig), skalieren (Eckgriff,
/// seitenverhältnistreu), zuschneiden (Randgriffe im Zuschnittmodus) und drehen
/// (90°-Schritte über die schwebende Werkzeugleiste).
/// </summary>
public partial class MainWindow
{
    // Dekodierte Originalbilder je Modell-Objekt zwischenspeichern, damit nicht
    // bei jedem Neuzeichnen der Seite erneut dekodiert wird.
    private static readonly ConditionalWeakTable<BildEinfügung, BitmapImage> _bildBasisCache = new();

    private const double BildMindestGröße = 20; // kleinste Anzeigegröße in DIP

    private static readonly Brush ZuschnittGriffFarbe = Eingefroren(Color.FromRgb(0xF4, 0xA9, 0x3B));

    private static BitmapImage BildBasis(BildEinfügung b)
    {
        if (_bildBasisCache.TryGetValue(b, out var vorhanden))
            return vorhanden;
        var bild = BitmapAusBytes(b.PngBytes);
        _bildBasisCache.Add(b, bild);
        return bild;
    }

    private static BitmapImage BitmapAusBytes(byte[] png)
    {
        using var strom = new MemoryStream(png);
        var bild = new BitmapImage();
        bild.BeginInit();
        bild.CacheOption = BitmapCacheOption.OnLoad;
        bild.StreamSource = strom;
        bild.EndInit();
        bild.Freeze();
        return bild;
    }

    /// <summary>
    /// Baut die Anzeige-Quelle eines Bildes auf: erst um <see cref="BildEinfügung.DrehungGrad"/>
    /// drehen, dann die Zuschnitt-Anteile abschneiden – dieselbe Pipeline wie
    /// <see cref="Bildwerkzeuge.TransformiertesPng"/> beim Speichern.
    /// </summary>
    private static BitmapSource BildQuelle(BildEinfügung b)
    {
        BitmapSource quelle = BildBasis(b);
        if (b.DrehungGrad != 0)
        {
            var gedreht = new TransformedBitmap(quelle, new RotateTransform(b.DrehungGrad));
            gedreht.Freeze();
            quelle = gedreht;
        }

        int pb = quelle.PixelWidth, ph = quelle.PixelHeight;
        int x = Math.Clamp((int)Math.Round(b.ZuschnittLinks * pb), 0, pb - 1);
        int y = Math.Clamp((int)Math.Round(b.ZuschnittOben * ph), 0, ph - 1);
        int w = Math.Clamp(pb - x - (int)Math.Round(b.ZuschnittRechts * pb), 1, pb - x);
        int h = Math.Clamp(ph - y - (int)Math.Round(b.ZuschnittUnten * ph), 1, ph - y);
        if (x == 0 && y == 0 && w == pb && h == ph)
            return quelle;

        var ausschnitt = new CroppedBitmap(quelle, new Int32Rect(x, y, w, h));
        ausschnitt.Freeze();
        return ausschnitt;
    }

    private void FügeBildHinzu(BildEinfügung b, SeitenGeometrie geo)
    {
        Rect r = geo.RechteckNachAnzeige(b.X, b.Y, b.X + b.Breite, b.Y + b.Höhe);
        var behälter = ErzeugeBehälter(r);
        behälter.Focusable = true;

        var bild = new Image
        {
            Source = BildQuelle(b),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        behälter.Children.Add(bild);

        // Modell aus der aktuellen Anzeige-Geometrie nachführen.
        void Nachführen() => SetzeRechteck(geo,
            Canvas.GetLeft(behälter), Canvas.GetTop(behälter), behälter.Width, behälter.Height,
            (x, y, w, h) => { b.X = x; b.Y = y; b.Breite = w; b.Höhe = h; });

        // Ganzflächiger, unsichtbarer Ziehgriff zum Verschieben.
        var ziehen = new Thumb { Cursor = Cursors.SizeAll, Opacity = 0, Background = Brushes.Transparent };
        ziehen.DragDelta += (_, e) =>
        {
            double links = Math.Clamp(Canvas.GetLeft(behälter) + e.HorizontalChange,
                0, Math.Max(0, OverlayCanvas.Width - behälter.Width));
            double oben = Math.Clamp(Canvas.GetTop(behälter) + e.VerticalChange,
                0, Math.Max(0, OverlayCanvas.Height - behälter.Height));
            Canvas.SetLeft(behälter, links);
            Canvas.SetTop(behälter, oben);
            Nachführen();
        };
        behälter.Children.Add(ziehen);

        // Auswahlrahmen – nur bei Fokus sichtbar.
        var rahmen = AuswahlRahmen();
        rahmen.Visibility = Visibility.Collapsed;
        behälter.Children.Add(rahmen);

        // Eckgriff: skaliert unter Beibehaltung des aktuellen Seitenverhältnisses
        // (das sich durch Zuschneiden und Drehen ändern kann – daher je Zug neu lesen).
        var skalieren = new Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Background = Akzent
        };
        skalieren.DragDelta += (_, e) =>
        {
            double aspekt = behälter.Height > 0 ? behälter.Width / behälter.Height : 1.0;
            double neueBreite = Math.Max(BildMindestGröße, behälter.Width + e.HorizontalChange);
            behälter.Width = neueBreite;
            behälter.Height = Math.Max(BildMindestGröße / 4, neueBreite / aspekt);
            Nachführen();
        };
        behälter.Children.Add(skalieren);

        var löschen = LöschSchaltfläche(() => _vm.BildEntfernen(b));
        behälter.Children.Add(löschen);

        // Zuschnittgriffe an den vier Rändern (nur im Zuschnittmodus sichtbar).
        var griffe = new[]
        {
            ZuschnittGriff(b, behälter, bild, Nachführen, kante: 0), // links
            ZuschnittGriff(b, behälter, bild, Nachführen, kante: 1), // oben
            ZuschnittGriff(b, behälter, bild, Nachführen, kante: 2), // rechts
            ZuschnittGriff(b, behälter, bild, Nachführen, kante: 3)  // unten
        };
        foreach (var g in griffe)
            behälter.Children.Add(g);

        // Schwebende Werkzeugleiste (drehen, zuschneiden) – als Canvas-Geschwister,
        // damit sie bei kleinen Bildern nicht auf die Behältergröße beschnitten wird.
        bool zuschnittModus = false;
        var zuschneiden = new ToggleButton
        {
            Content = "Zuschneiden",
            Height = 22,
            FontSize = 11,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Focusable = false,
            ToolTip = "Zuschnittmodus: an den orangefarbenen Randgriffen ziehen"
        };
        var leiste = ErstelleBildLeiste(zuschneiden,
            drehen: linksHerum =>
            {
                if (linksHerum) b.LinksDrehen(); else b.RechtsDrehen();
                // Geometrie und Bildinhalt in-place aktualisieren, damit Fokus
                // und Werkzeugleiste erhalten bleiben (kein Neuzeichnen der Seite).
                Rect nr = geo.RechteckNachAnzeige(b.X, b.Y, b.X + b.Breite, b.Y + b.Höhe);
                Canvas.SetLeft(behälter, nr.Left);
                Canvas.SetTop(behälter, nr.Top);
                behälter.Width = nr.Width;
                behälter.Height = nr.Height;
                bild.Source = BildQuelle(b);
                Nachführen(); // klemmt ggf. an den Seitenrand zurückgerechnete Werte fest
            });
        leiste.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(leiste, 998);
        OverlayCanvas.Children.Add(leiste);

        void PositioniereLeiste()
        {
            leiste.UpdateLayout();
            double lh = leiste.ActualHeight > 0 ? leiste.ActualHeight : 30;
            double links = Canvas.GetLeft(behälter);
            double oben = Canvas.GetTop(behälter);
            double y = oben - lh - 4;
            if (y < 0)
                y = oben + behälter.Height + 4; // am oberen Seitenrand: unter das Bild
            Canvas.SetLeft(leiste, links);
            Canvas.SetTop(leiste, y);
        }

        void ZuschnittGriffeAktualisieren()
        {
            bool sichtbar = zuschnittModus && behälter.IsKeyboardFocusWithin;
            foreach (var g in griffe)
                g.Visibility = sichtbar ? Visibility.Visible : Visibility.Collapsed;
        }

        zuschneiden.Checked += (_, _) => { zuschnittModus = true; ZuschnittGriffeAktualisieren(); };
        zuschneiden.Unchecked += (_, _) => { zuschnittModus = false; ZuschnittGriffeAktualisieren(); };

        behälter.LayoutUpdated += (_, _) =>
        {
            if (leiste.Visibility == Visibility.Visible)
                PositioniereLeiste();
        };

        // Chrome nur bei Fokus; Klick auf den Behälter fokussiert ihn.
        skalieren.Visibility = Visibility.Collapsed;
        löschen.Visibility = Visibility.Collapsed;
        behälter.PreviewMouseLeftButtonDown += (_, _) => behälter.Focus();
        behälter.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            var v = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            rahmen.Visibility = v;
            skalieren.Visibility = v;
            löschen.Visibility = v;
            leiste.Visibility = v;
            if ((bool)e.NewValue)
                PositioniereLeiste();
            ZuschnittGriffeAktualisieren();
        };
        behälter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete)
            {
                _vm.BildEntfernen(b);
                e.Handled = true;
            }
        };

        OverlayCanvas.Children.Add(behälter);
    }

    /// <summary>
    /// Erzeugt einen Randgriff für den Zuschnitt (Kante 0=links, 1=oben, 2=rechts,
    /// 3=unten). Ziehen nach innen schneidet ab, nach außen stellt bis zum
    /// Originalrand wieder her; das übrige Bild bleibt dabei an Ort und Stelle.
    /// </summary>
    private static Thumb ZuschnittGriff(BildEinfügung b, Grid behälter, Image bild,
        Action nachführen, int kante)
    {
        bool horizontal = kante is 0 or 2; // Griff verändert die Breite
        var griff = new Thumb
        {
            Background = ZuschnittGriffFarbe,
            Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            Visibility = Visibility.Collapsed
        };
        if (horizontal)
        {
            griff.Width = 7;
            griff.VerticalAlignment = VerticalAlignment.Stretch;
            griff.HorizontalAlignment = kante == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        else
        {
            griff.Height = 7;
            griff.HorizontalAlignment = HorizontalAlignment.Stretch;
            griff.VerticalAlignment = kante == 1 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }

        griff.DragDelta += (_, e) =>
        {
            double größe = horizontal ? behälter.Width : behälter.Height;
            double änderung = horizontal ? e.HorizontalChange : e.VerticalChange;
            double vorn = horizontal ? b.ZuschnittLinks : b.ZuschnittOben;   // Anteil an der Zieh-Kante…
            double hinten = horizontal ? b.ZuschnittRechts : b.ZuschnittUnten; // …bzw. gegenüber
            if (kante is 2 or 3)
                (vorn, hinten) = (hinten, vorn);

            double sichtbar = 1 - vorn - hinten;
            if (sichtbar <= 0)
                return;
            // Anzeigegröße des ungeschnittenen (gedrehten) Bildes – bleibt beim Zuschneiden konstant.
            double voll = größe / sichtbar;

            // An der vorderen Kante (links/oben) verschiebt Ziehen die Kante selbst,
            // an der hinteren wirkt die Mausbewegung entgegengesetzt.
            double delta = kante is 0 or 1 ? änderung : -änderung;
            delta = Math.Clamp(delta, -vorn * voll, größe - BildMindestGröße);
            if (Math.Abs(delta) < 0.01)
                return;

            vorn += delta / voll;
            if (kante == 0) b.ZuschnittLinks = vorn;
            else if (kante == 1) b.ZuschnittOben = vorn;
            else if (kante == 2) b.ZuschnittRechts = vorn;
            else b.ZuschnittUnten = vorn;

            if (horizontal)
            {
                if (kante == 0)
                    Canvas.SetLeft(behälter, Canvas.GetLeft(behälter) + delta);
                behälter.Width = größe - delta;
            }
            else
            {
                if (kante == 1)
                    Canvas.SetTop(behälter, Canvas.GetTop(behälter) + delta);
                behälter.Height = größe - delta;
            }

            bild.Source = BildQuelle(b);
            nachführen();
        };
        return griff;
    }

    /// <summary>Schwebende Werkzeugleiste eines Bildes: links/rechts drehen und Zuschnittmodus.</summary>
    private static FrameworkElement ErstelleBildLeiste(ToggleButton zuschneiden, Action<bool> drehen)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var linksDrehen = KleineSchaltfläche("↺", () => drehen(true));
        linksDrehen.ToolTip = "90° nach links drehen";
        linksDrehen.Focusable = false;
        var rechtsDrehen = KleineSchaltfläche("↻", () => drehen(false));
        rechtsDrehen.ToolTip = "90° nach rechts drehen";
        rechtsDrehen.Focusable = false;

        panel.Children.Add(linksDrehen);
        panel.Children.Add(rechtsDrehen);
        panel.Children.Add(Trenner());
        panel.Children.Add(zuschneiden);

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
}
