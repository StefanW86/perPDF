using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using PdfEditor.Models;
using PdfEditor.Services;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace PdfEditor;

/// <summary>
/// Schwebende Leisten über Overlays (Text, Bild, Symbol) und ihre Knöpfe. Farben kommen
/// über Ressourcen-Verweise aus dem Hell-/Dunkel-Design, Symbole aus den Fluent-Icons.
/// </summary>
public partial class MainWindow
{
    /// <summary>Baut die schwebende Leiste mit Schriftgröße, Auszeichnungen und Farben.</summary>
    private FrameworkElement ErstelleFormatLeiste(TextNotiz t, System.Windows.Controls.TextBox tb, SeitenGeometrie geo)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var schrift = new ComboBox
        {
            Style = (Style)FindResource("SchriftAuswahl"),
            ItemsSource = Schriftarten.Installiert,
            SelectedItem = Schriftarten.Installiert.FirstOrDefault(
                f => string.Equals(f, t.Schriftart, StringComparison.OrdinalIgnoreCase)),
            ToolTip = "Schriftart"
        };
        schrift.SelectionChanged += (_, _) =>
        {
            if (schrift.SelectedItem is not string name)
                return;
            t.Schriftart = name;
            TextStilAnwenden(tb, t, geo);
        };
        panel.Children.Add(schrift);
        panel.Children.Add(Trenner());

        panel.Children.Add(KleineSchaltfläche(SymbolRegular.FontDecrease24, "Schrift kleiner",
            () => SchriftgrößeÄndern(t, tb, geo, -2)));
        panel.Children.Add(KleineSchaltfläche(SymbolRegular.FontIncrease24, "Schrift größer",
            () => SchriftgrößeÄndern(t, tb, geo, +2)));
        panel.Children.Add(Trenner());

        panel.Children.Add(KleinerUmschalter(SymbolRegular.TextBold24, "Fett", t.Fett,
            an => { t.Fett = an; TextStilAnwenden(tb, t, geo); }));
        panel.Children.Add(KleinerUmschalter(SymbolRegular.TextItalic24, "Kursiv", t.Kursiv,
            an => { t.Kursiv = an; TextStilAnwenden(tb, t, geo); }));
        panel.Children.Add(KleinerUmschalter(SymbolRegular.TextUnderline24, "Unterstrichen", t.Unterstrichen,
            an => { t.Unterstrichen = an; TextStilAnwenden(tb, t, geo); }));
        panel.Children.Add(Trenner());

        panel.Children.Add(FarbButton(() => t.Farbe, hex =>
        {
            t.Farbe = hex;
            TextStilAnwenden(tb, t, geo);
        }));

        return Leistenrahmen(panel);
    }

    private static void SchriftgrößeÄndern(TextNotiz t, System.Windows.Controls.TextBox tb, SeitenGeometrie geo, double delta)
    {
        t.FontGröße = Math.Clamp(t.FontGröße + delta, 6, 96);
        TextStilAnwenden(tb, t, geo);
    }

    /// <summary>
    /// Kleiner Farbknopf, der die aktuelle Farbe als Feld zeigt und beim Klick den
    /// RGB-Farbwähler öffnet. <paramref name="holen"/> liefert die aktuelle Farbe,
    /// <paramref name="setzen"/> übernimmt die neue.
    /// </summary>
    private System.Windows.Controls.Button FarbButton(Func<string> holen, Action<string> setzen)
    {
        var feld = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(WpfFarbe(holen()))
        };
        feld.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var knopf = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("LeistenKnopf"),
            Content = feld,
            ToolTip = "Farbe ändern (RGB)",
            // Nicht fokussierbar: ein Klick darf den Fokus des umgebenden Elements nicht
            // verlieren, sonst würde die nur-bei-Fokus sichtbare Leiste vorzeitig verschwinden.
            Focusable = false
        };
        knopf.Click += (_, _) => Controls.FarbWähler.Zeigen(knopf, holen(), hex =>
        {
            setzen(hex);
            feld.Background = new SolidColorBrush(WpfFarbe(hex));
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
            Margin = new Thickness(4, 0, 4, 0),
            FontSize = 11
        });
        panel.Children.Add(FarbButton(() => s.Farbe, farbeAnwenden));
        return Leistenrahmen(panel);
    }

    /// <summary>Hintergrund der schwebenden Leisten: Designfläche mit dezentem Rand.</summary>
    private static Border Leistenrahmen(UIElement inhalt)
    {
        var rahmen = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = inhalt,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        rahmen.SetResourceReference(Border.BackgroundProperty, "PpLeistenFläche");
        rahmen.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        rahmen.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        return rahmen;
    }

    private static SymbolIcon Symbol(SymbolRegular s) => new() { Symbol = s, FontSize = 16 };

    private System.Windows.Controls.Button KleineSchaltfläche(SymbolRegular symbol, string tipp, Action aktion)
    {
        var b = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("LeistenKnopf"),
            Content = Symbol(symbol),
            ToolTip = tipp
        };
        b.Click += (_, _) => aktion();
        return b;
    }

    private ToggleButton KleinerUmschalter(SymbolRegular symbol, string tipp, bool an, Action<bool> setzen)
    {
        var b = new ToggleButton
        {
            Style = (Style)FindResource("LeistenUmschalter"),
            Content = Symbol(symbol),
            ToolTip = tipp,
            IsChecked = an
        };
        b.Checked += (_, _) => setzen(true);
        b.Unchecked += (_, _) => setzen(false);
        return b;
    }

    private static Border Trenner()
    {
        var b = new Border { Width = 1, Margin = new Thickness(3, 3, 3, 3) };
        b.SetResourceReference(Border.BackgroundProperty, "ControlStrokeColorDefaultBrush");
        return b;
    }

    private System.Windows.Controls.Button LöschSchaltfläche(Action entfernen)
    {
        var b = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("LeistenKnopf"),
            Content = new SymbolIcon { Symbol = SymbolRegular.Dismiss16, FontSize = 11 },
            Width = 20,
            Height = 20,
            ToolTip = "Entfernen",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        // Sichtbare Fläche, da der Knopf direkt über dem Seiteninhalt liegt.
        b.SetResourceReference(BackgroundProperty, "PpLeistenFläche");
        b.SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        b.Click += (_, _) => entfernen();
        return b;
    }
}
