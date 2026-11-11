using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace PdfEditor.Windows;

/// <summary>
/// Meldungen und Nachfragen im perPDF-Design (Ersatz für <see cref="System.Windows.MessageBox"/>).
/// Nachfragen tragen sprechende Schaltflächen; die erste ist die hervorgehobene Standardaktion,
/// die letzte die sichere Wahl (Esc / Schließen-✕).
/// </summary>
public partial class MeldungFenster : FluentWindow
{
    private int _gewählt;

    private MeldungFenster(string titel, string text, MessageBoxImage art, string[] knöpfe)
    {
        InitializeComponent();
        Title = titel;
        Titelleiste.Title = titel;
        Nachricht.Text = text;

        (Symbol.Symbol, var farbe) = art switch
        {
            MessageBoxImage.Error => (SymbolRegular.ErrorCircle24, "#C42B1C"),
            MessageBoxImage.Warning => (SymbolRegular.Warning24, "#F4A93B"),
            MessageBoxImage.Question => (SymbolRegular.QuestionCircle24, "#0E7C7B"),
            _ => (SymbolRegular.Info24, "#0E7C7B"),
        };
        Symbol.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(farbe));

        _gewählt = knöpfe.Length - 1; // Esc / ✕ = sichere Wahl
        for (int i = 0; i < knöpfe.Length; i++)
        {
            int index = i;
            var knopf = new Wpf.Ui.Controls.Button
            {
                Content = knöpfe[i],
                MinWidth = 100,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                Appearance = i == 0 ? ControlAppearance.Primary : ControlAppearance.Secondary,
                IsDefault = i == 0,
                IsCancel = i == knöpfe.Length - 1,
            };
            knopf.Click += (_, _) => { _gewählt = index; DialogResult = true; };
            Knöpfe.Children.Add(knopf);
        }
    }

    /// <summary>Zeigt eine Nachfrage und liefert den Index der gewählten Schaltfläche.</summary>
    public static int Fragen(string titel, string text, MessageBoxImage art, params string[] knöpfe)
        => Fragen(null, titel, text, art, knöpfe);

    /// <inheritdoc cref="Fragen(string, string, MessageBoxImage, string[])"/>
    public static int Fragen(Window? besitzer, string titel, string text, MessageBoxImage art, params string[] knöpfe)
    {
        var fenster = new MeldungFenster(titel, text, art, knöpfe);
        // Selbst messen: FluentWindow hat eine Stil-Mindestgröße (460×320) und rechnet mit
        // SizeToContent den Rahmen zu viel ein (Leerraum rechts/unten).
        var inhalt = (FrameworkElement)fenster.Content;
        inhalt.Measure(new Size(560, double.PositiveInfinity));
        fenster.Width = Math.Max(380, inhalt.DesiredSize.Width);
        fenster.Height = inhalt.DesiredSize.Height;
        besitzer ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                     ?? Application.Current?.MainWindow;
        if (besitzer is { IsLoaded: true } && !ReferenceEquals(besitzer, fenster))
            fenster.Owner = besitzer;
        else
            fenster.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        fenster.ShowDialog();
        return fenster._gewählt;
    }

    /// <summary>Zeigt einen Hinweis oder Fehler mit einer „OK“-Schaltfläche.</summary>
    public static void Zeigen(string titel, string text, MessageBoxImage art)
        => Fragen(null, titel, text, art, "OK");

    /// <inheritdoc cref="Zeigen(string, string, MessageBoxImage)"/>
    public static void Zeigen(Window? besitzer, string titel, string text, MessageBoxImage art)
        => Fragen(besitzer, titel, text, art, "OK");
}
