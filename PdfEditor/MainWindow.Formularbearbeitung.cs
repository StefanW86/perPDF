using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.ViewModels;

namespace PdfEditor;

/// <summary>
/// Overlays für den Bearbeitungsmodus vorhandener Formularfelder: ausfüllbare Felder
/// werden als auswählbare Felder dargestellt, ein Klick übernimmt sie zur Bearbeitung
/// (Umwandlung in einen Entwurf), das ✕ merkt sie zum Löschen vor.
/// </summary>
public partial class MainWindow
{
    /// <summary>Ob ein vorhandenes Feld im Bearbeitungsmodus geändert/gelöscht werden kann.</summary>
    private static bool FeldBearbeitbar(FormularFeld feld) =>
        feld.Typ is FeldTyp.Text or FeldTyp.Kontrollkästchen
            or FeldTyp.Auswahlliste or FeldTyp.Optionsfeld;

    private static string FeldTypBezeichnung(FeldTyp t) => t switch
    {
        FeldTyp.Kontrollkästchen => "Kontrollkästchen",
        FeldTyp.Auswahlliste => "Auswahlliste",
        FeldTyp.Optionsfeld => "Optionsfeld",
        _ => "Textfeld"
    };

    /// <summary>
    /// Stellt ein vorhandenes Formularfeld im Bearbeitungsmodus als auswählbares Feld dar:
    /// gestrichelter Rahmen, typgerechte (nicht interaktive) Vorschau, ein ✕ zum Löschen.
    /// Ein Klick übernimmt das Feld zur Bearbeitung (<see cref="HauptViewModel.FeldBearbeiten"/>):
    /// Es wird zum Entwurf und ab dann wie ein neu angelegtes Feld verschieb-/skalier-/konfigurierbar.
    /// </summary>
    private void FügeBearbeitbaresFeldHinzu(FormularFeld feld, SeitenGeometrie geo)
    {
        // Bei Optionsfeldern die danebenstehenden Beschriftungen mit einfassen: das
        // Feldrechteck selbst umfasst nur die Schaltflächen (die Texte sind Seiteninhalt).
        var (x1, y1, x2, y2) = feld.Typ == FeldTyp.Optionsfeld
            ? _vm.OptionsfeldGesamtBox(feld)
            : (feld.X1, feld.Y1, feld.X2, feld.Y2);
        Rect r = geo.RechteckNachAnzeige(x1, y1, x2, y2);
        var behälter = ErzeugeBehälter(r);
        behälter.Background = Brushes.Transparent; // ganze Fläche anklickbar
        behälter.Cursor = Cursors.Hand;
        behälter.ToolTip = $"{FeldTypBezeichnung(feld.Typ)}: {feld.FeldName} – klicken zum Bearbeiten, ✕ zum Löschen";

        var vorschau = feld.Typ == FeldTyp.Optionsfeld
            ? ErstelleOptionsfeldVorschau(feld, geo, r)
            : ErstelleVorhandenVorschau(feld, r);
        vorschau.IsHitTestVisible = false;
        behälter.Children.Add(vorschau);

        behälter.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = Akzent,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        });

        // Löschen-Schaltfläche markiert das Feld zum Entfernen beim Speichern.
        behälter.Children.Add(LöschSchaltfläche(() => _vm.FeldVorhandenLöschen(feld)));

        // Klick auf die Fläche (nicht das ✕) übernimmt das Feld zur Bearbeitung.
        behälter.MouseLeftButtonDown += (_, e) =>
        {
            _vm.FeldBearbeiten(feld);
            e.Handled = true;
        };

        OverlayCanvas.Children.Add(behälter);
    }

    /// <summary>Erzeugt eine nicht interaktive Vorschau eines vorhandenen Feldes (Bearbeitungsmodus).</summary>
    private FrameworkElement ErstelleVorhandenVorschau(FormularFeld feld, Rect r)
    {
        switch (feld.Typ)
        {
            case FeldTyp.Kontrollkästchen:
            {
                double k = Math.Max(8, Math.Min(r.Width, r.Height) - 2);
                return new Border
                {
                    Width = k,
                    Height = k,
                    Background = FeldHintergrund,
                    BorderBrush = Akzent,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = feld.IstAngehakt ? "✓" : string.Empty,
                        Foreground = Akzent,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
            }

            case FeldTyp.Auswahlliste:
            {
                double fs = Math.Clamp(r.Height * 0.5, 8, 15);
                var pfeil = new TextBlock
                {
                    Text = "⌄",
                    FontSize = fs + 3,
                    Foreground = Akzent,
                    Margin = new Thickness(2, 0, 5, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(pfeil, Dock.Right);
                var dock = new DockPanel { Background = FeldHintergrund, LastChildFill = true };
                dock.Children.Add(pfeil);
                dock.Children.Add(new TextBlock
                {
                    Text = feld.Wert,
                    FontSize = fs,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                return dock;
            }

            default: // Text
            {
                double fs = Math.Clamp(r.Height * 0.55, 8, 16);
                return new Border
                {
                    Background = FeldHintergrund,
                    Child = new TextBlock
                    {
                        Text = feld.Wert,
                        FontSize = fs,
                        Foreground = Brushes.Black,
                        Margin = new Thickness(3, 1, 3, 1),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };
            }
        }
    }

    /// <summary>
    /// Nicht interaktive Vorschau einer Optionsfeld-Gruppe (Bearbeitungsmodus): je
    /// Schaltfläche ein Ring an ihrer Position, die gewählte mit Punkt. Die Beschriftungen
    /// stehen bereits als Seiteninhalt im Hintergrundbild.
    /// </summary>
    private FrameworkElement ErstelleOptionsfeldVorschau(FormularFeld feld, SeitenGeometrie geo, Rect behälterRect)
    {
        var canvas = new Canvas { Background = Brushes.Transparent };
        foreach (var opt in feld.Optionsschaltflächen)
        {
            Rect or = geo.RechteckNachAnzeige(opt.X1, opt.Y1, opt.X2, opt.Y2);
            double d = Math.Max(8, Math.Min(or.Width, or.Height));

            var ring = new Grid { Width = d, Height = d };
            ring.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Stroke = Akzent,
                StrokeThickness = 1,
                Fill = FeldHintergrund
            });
            if (opt.Export == feld.Wert)
                ring.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = d * 0.5,
                    Height = d * 0.5,
                    Fill = Akzent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

            Canvas.SetLeft(ring, or.Left - behälterRect.Left);
            Canvas.SetTop(ring, or.Top - behälterRect.Top);
            canvas.Children.Add(ring);
        }
        return canvas;
    }
}
