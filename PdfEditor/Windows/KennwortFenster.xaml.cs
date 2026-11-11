using System.Windows;
using Wpf.Ui.Controls;

namespace PdfEditor.Windows;

/// <summary>
/// Fragt ein Kennwort ab – zum Öffnen einer geschützten PDF oder (mit Wiederholung)
/// zum Festlegen eines neuen Kennworts.
/// </summary>
public partial class KennwortFenster : FluentWindow
{
    private readonly bool _wiederholen;

    private KennwortFenster(string titel, string text, string knopf, bool wiederholen)
    {
        InitializeComponent();
        Title = titel;
        Titelleiste.Title = titel;
        Nachricht.Text = text;
        OkKnopf.Content = knopf;
        _wiederholen = wiederholen;
        WiederholenBereich.Visibility = wiederholen ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Kennwort1.Focus();
    }

    /// <summary>
    /// Zeigt den Dialog und liefert das eingegebene Kennwort, oder <c>null</c> bei „Abbrechen“.
    /// Mit <paramref name="wiederholen"/> muss das Kennwort zweimal gleich eingegeben werden.
    /// </summary>
    public static string? Abfragen(string titel, string text, string knopf, bool wiederholen)
    {
        var fenster = new KennwortFenster(titel, text, knopf, wiederholen);
        // Wie MeldungFenster selbst messen (FluentWindow-Mindestgröße umgehen).
        var inhalt = (FrameworkElement)fenster.Content;
        fenster.Width = 420;
        inhalt.Measure(new Size(420, double.PositiveInfinity));
        // Platz für die (anfangs ausgeblendete) Fehlerzeile reservieren.
        fenster.Height = inhalt.DesiredSize.Height + 28;
        var besitzer = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current?.MainWindow;
        if (besitzer is { IsLoaded: true })
            fenster.Owner = besitzer;
        else
            fenster.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return fenster.ShowDialog() == true ? fenster.Kennwort1.Password : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? fehler = Kennwort1.Password.Length == 0 ? "Bitte ein Kennwort eingeben."
            : _wiederholen && Kennwort1.Password != Kennwort2.Password ? "Die Kennwörter stimmen nicht überein."
            : null;
        if (fehler is null)
        {
            DialogResult = true;
            return;
        }
        Hinweis.Text = fehler;
        Hinweis.Visibility = Visibility.Visible;
    }
}
