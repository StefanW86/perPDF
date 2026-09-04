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

/// <summary>Overlays für ausfüllbare Formularfelder (Entwurfsfelder: MainWindow.Formularentwurf.cs).</summary>
public partial class MainWindow
{
    private void FügeFeldHinzu(FormularFeld feld, SeitenGeometrie geo)
    {
        Rect r = geo.RechteckNachAnzeige(feld.X1, feld.Y1, feld.X2, feld.Y2);
        FrameworkElement steuerelement = feld.Typ switch
        {
            FeldTyp.Kontrollkästchen => ErstelleKontrollkästchen(feld),
            FeldTyp.Auswahlliste => ErstelleAuswahl(feld, r.Height),
            FeldTyp.Optionsfeld => ErstelleOptionsfeld(feld, geo, r),
            _ => ErstelleTextfeld(feld, r.Height)
        };

        steuerelement.Width = r.Width;
        steuerelement.Height = r.Height;
        Canvas.SetLeft(steuerelement, r.Left);
        Canvas.SetTop(steuerelement, r.Top);
        OverlayCanvas.Children.Add(steuerelement);
    }

    private static TextBox ErstelleTextfeld(FormularFeld feld, double höhe)
    {
        var tb = new TextBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(1),
            FontSize = Math.Clamp(höhe * 0.6, 8, 22),
            Background = FeldHintergrund
        };
        tb.SetBinding(TextBox.TextProperty, new Binding(nameof(FormularFeld.Wert))
        {
            Source = feld,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        return tb;
    }

    /// <summary>
    /// Kontrollkästchen-Overlay als größenrobuste Eigendarstellung (Rahmen + Häkchen).
    /// Die gethemte <see cref="CheckBox"/> wird absichtlich nicht verwendet: in das kleine
    /// Feldrechteck gezwängt schneidet ihr Vorlagenbild ab. Klick schaltet den Wert um.
    /// </summary>
    private FrameworkElement ErstelleKontrollkästchen(FormularFeld feld)
    {
        var haken = new TextBlock
        {
            Text = feld.IstAngehakt ? "✓" : string.Empty,
            Foreground = Akzent,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var kästchen = new Border
        {
            Background = FeldHintergrund,
            BorderBrush = Akzent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = haken
        };
        var rahmen = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = kästchen };

        // Kästchen quadratisch an die jeweilige Feldgröße anpassen.
        rahmen.SizeChanged += (_, _) =>
        {
            double k = Math.Max(8, Math.Min(rahmen.ActualWidth, rahmen.ActualHeight) - 2);
            kästchen.Width = k;
            kästchen.Height = k;
            haken.FontSize = Math.Clamp(k * 0.8, 8, 40);
        };
        rahmen.MouseLeftButtonDown += (_, e) =>
        {
            if (InSpezialModus)
                return;
            feld.IstAngehakt = !feld.IstAngehakt;
            haken.Text = feld.IstAngehakt ? "✓" : string.Empty;
            e.Handled = true;
        };
        return rahmen;
    }

    /// <summary>
    /// Auswahllisten-Overlay als saubere Eigendarstellung (Wert + Pfeil) mit Auswahl-Popup.
    /// Die gethemte <see cref="ComboBox"/> schneidet ihren Text in der geringen Feldhöhe ab,
    /// daher wird der Wert selbst gezeichnet; ein Klick öffnet die Optionsliste.
    /// </summary>
    private FrameworkElement ErstelleAuswahl(FormularFeld feld, double höhe)
    {
        double fs = Math.Clamp(höhe * 0.5, 8, 15);
        var wert = new TextBlock
        {
            Text = feld.Wert,
            FontSize = fs,
            Foreground = Brushes.Black,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var pfeil = new TextBlock
        {
            Text = "⌄",
            FontSize = fs + 3,
            Foreground = Akzent,
            Margin = new Thickness(2, 0, 5, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(pfeil, Dock.Right);
        var dock = new DockPanel { LastChildFill = true, Background = FeldHintergrund };
        dock.Children.Add(pfeil);
        dock.Children.Add(wert);
        var anzeige = new Border
        {
            BorderBrush = Akzent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Cursor = Cursors.Hand,
            Child = dock
        };

        var liste = new ListBox { ItemsSource = feld.Optionen, FontSize = fs };
        var popup = new Popup
        {
            PlacementTarget = anzeige,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = Akzent,
                BorderThickness = new Thickness(1),
                MaxHeight = 220,
                Child = liste
            }
        };

        bool unterdrücke = false;
        var zuletztGeschlossen = DateTime.MinValue;
        popup.Closed += (_, _) => zuletztGeschlossen = DateTime.UtcNow;

        // Den Mausklick verbrauchen, damit er nicht die Feldauswahl o.Ä. auslöst.
        anzeige.MouseLeftButtonDown += (_, e) =>
        {
            if (!InSpezialModus)
                e.Handled = true;
        };
        // Erst beim Loslassen öffnen: Wird ein StaysOpen=false-Popup noch bei gedrückter
        // Maustaste geöffnet, deutet es den zugehörigen MouseUp als Klick außerhalb und
        // schließt sich sofort wieder. Beim MouseUp ist die Taste bereits losgelassen.
        anzeige.MouseLeftButtonUp += (_, e) =>
        {
            if (InSpezialModus)
                return;
            e.Handled = true;
            if (popup.IsOpen)
            {
                popup.IsOpen = false;
                return;
            }
            // Hat genau dieser Klick (sein MouseDown außerhalb) das Popup eben geschlossen,
            // nicht sofort wieder öffnen – sonst ließe es sich nie per Klick schließen.
            if ((DateTime.UtcNow - zuletztGeschlossen).TotalMilliseconds < 250)
                return;
            unterdrücke = true;
            liste.SelectedItem = feld.Wert;
            unterdrücke = false;
            popup.Width = anzeige.ActualWidth;
            popup.IsOpen = true;
        };
        liste.SelectionChanged += (_, _) =>
        {
            if (unterdrücke)
                return;
            if (liste.SelectedItem is string s)
            {
                feld.Wert = s;
                wert.Text = s;
            }
            popup.IsOpen = false;
        };

        var behälter = new Grid();
        behälter.Children.Add(anzeige);
        behälter.Children.Add(popup);
        return behälter;
    }

    /// <summary>
    /// Optionsfeld-Overlay (Radio) für ein wiedergeladenes Feld: je Optionsschaltfläche
    /// ein anklickbarer Ring an ihrer Widget-Position; die gewählte trägt einen Punkt.
    /// Beschriftungen stehen bereits als Seiteninhalt im Hintergrundbild.
    /// </summary>
    private FrameworkElement ErstelleOptionsfeld(FormularFeld feld, SeitenGeometrie geo, Rect behälterRect)
    {
        var canvas = new Canvas { Background = Brushes.Transparent };
        var punkte = new List<(Ellipse Punkt, string Export)>();

        foreach (var opt in feld.Optionsschaltflächen)
        {
            Rect or = geo.RechteckNachAnzeige(opt.X1, opt.Y1, opt.X2, opt.Y2);
            double d = Math.Max(8, Math.Min(or.Width, or.Height));

            var punkt = new Ellipse
            {
                Width = d * 0.5,
                Height = d * 0.5,
                Fill = Akzent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = opt.Export == feld.Wert ? Visibility.Visible : Visibility.Collapsed
            };
            var ring = new Grid
            {
                Width = d,
                Height = d,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            ring.Children.Add(new Ellipse
            {
                Stroke = Akzent,
                StrokeThickness = 1,
                Fill = FeldHintergrund
            });
            ring.Children.Add(punkt);

            string export = opt.Export;
            ring.MouseLeftButtonDown += (_, e) =>
            {
                if (InSpezialModus)
                    return;
                feld.Wert = export;
                foreach (var (p, ex) in punkte)
                    p.Visibility = ex == export ? Visibility.Visible : Visibility.Collapsed;
                e.Handled = true;
            };

            Canvas.SetLeft(ring, or.Left - behälterRect.Left);
            Canvas.SetTop(ring, or.Top - behälterRect.Top);
            canvas.Children.Add(ring);
            punkte.Add((punkt, export));
        }

        return canvas;
    }
}
