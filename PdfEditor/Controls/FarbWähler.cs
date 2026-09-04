using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PdfEditor.Controls;

/// <summary>
/// Kleiner RGB-Farbwähler als Popup: eine Palette gängiger Farben plus R/G/B-Schieber
/// und Hex-Anzeige. Änderungen werden live über den Rückruf gemeldet, sodass die
/// Wirkung sofort sichtbar ist.
/// </summary>
public static class FarbWähler
{
    private static readonly string[] Palette =
    {
        "#000000", "#444444", "#888888", "#CCCCCC", "#FFFFFF",
        "#D32F2F", "#E64A19", "#F4A93B", "#FBC02D", "#2E7D32",
        "#0E7C7B", "#1565C0", "#5E35B1", "#C2185B", "#795548"
    };

    /// <summary>Öffnet den Farbwähler unterhalb des Ankers und meldet jede Auswahl.</summary>
    public static void Zeigen(UIElement anker, string startHex, Action<string> beiÄnderung)
    {
        Color start = Parse(startHex);
        bool stumm = false; // unterdrückt Rückrufe während programmatischer Schieberänderung

        var rot = Schieber(start.R);
        var grün = Schieber(start.G);
        var blau = Schieber(start.B);

        var vorschau = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(start)
        };
        var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        void Aktualisiere()
        {
            var c = Color.FromRgb((byte)rot.Value, (byte)grün.Value, (byte)blau.Value);
            vorschau.Background = new SolidColorBrush(c);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            hexText.Text = hex;
            if (!stumm)
                beiÄnderung(hex);
        }

        rot.ValueChanged += (_, _) => Aktualisiere();
        grün.ValueChanged += (_, _) => Aktualisiere();
        blau.ValueChanged += (_, _) => Aktualisiere();

        // Palette
        var paletteFeld = new WrapPanel { Width = 200, Margin = new Thickness(0, 0, 0, 8) };
        foreach (string hex in Palette)
        {
            Color c = Parse(hex);
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(c),
                ToolTip = hex
            };
            swatch.Click += (_, _) =>
            {
                stumm = true;
                rot.Value = c.R;
                grün.Value = c.G;
                blau.Value = c.B;
                stumm = false;
                Aktualisiere();
            };
            paletteFeld.Children.Add(swatch);
        }

        var inhalt = new StackPanel { Margin = new Thickness(10) };
        inhalt.Children.Add(new TextBlock { Text = "Farbe wählen", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        inhalt.Children.Add(paletteFeld);
        inhalt.Children.Add(SchieberZeile("R", rot));
        inhalt.Children.Add(SchieberZeile("G", grün));
        inhalt.Children.Add(SchieberZeile("B", blau));

        var unten = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        unten.Children.Add(vorschau);
        unten.Children.Add(hexText);
        inhalt.Children.Add(unten);

        var rahmen = new Border
        {
            Background = SystemColors.WindowBrush,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = inhalt
        };

        var popup = new Popup
        {
            PlacementTarget = anker,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = true,
            Child = rahmen
        };

        hexText.Text = $"#{start.R:X2}{start.G:X2}{start.B:X2}";
        popup.IsOpen = true;
        popup.Focus();
    }

    private static Slider Schieber(byte wert) => new()
    {
        Minimum = 0,
        Maximum = 255,
        Value = wert,
        Width = 150,
        SmallChange = 1,
        LargeChange = 16,
        IsSnapToTickEnabled = true,
        TickFrequency = 1,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static UIElement SchieberZeile(string name, Slider schieber)
    {
        var zeile = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        zeile.Children.Add(new TextBlock { Text = name, Width = 16, VerticalAlignment = VerticalAlignment.Center });
        zeile.Children.Add(schieber);
        var wert = new TextBlock { Width = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        wert.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Value")
        {
            Source = schieber,
            StringFormat = "0"
        });
        zeile.Children.Add(wert);
        return zeile;
    }

    private static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Black; }
    }
}
