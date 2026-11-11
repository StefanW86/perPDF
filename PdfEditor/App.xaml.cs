using System.Windows;
using System.Windows.Media;
using PdfSharp.Fonts;
using Wpf.Ui.Appearance;

namespace PdfEditor;

/// <summary>
/// Einstiegspunkt der Anwendung. Die eigentliche Logik liegt im Hauptfenster
/// und in den Diensten; hier wird nur das WPF-Application-Objekt bereitgestellt.
/// </summary>
public partial class App : Application
{
    /// <summary>Primäre Markenfarbe (Teal) aus dem perPDF-Logo.</summary>
    public static readonly Color MarkenTeal = Color.FromRgb(0x0E, 0x7C, 0x7B);

    public App()
    {
        // PDFsharp benötigt zum Lesen/Schreiben von Formularfeldern Schriftarten.
        // Unter Windows die System-Schriftarten verwenden.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    /// <summary>Gibt an, ob gerade das dunkle Design aktiv ist.</summary>
    public static bool IstDunkel { get; private set; }

    /// <summary>
    /// Ausdrückliche Wahl des Nutzers (Umschalter „Dunkel“); <c>null</c> = dem Windows-Design folgen.
    /// </summary>
    public static bool? GewähltesDesign { get; private set; }

    /// <summary>Wird nach jedem Designwechsel ausgelöst (z. B. live durch Windows), damit Fenster nachziehen.</summary>
    public static event Action? DesignGeändert;

    /// <summary>Liest das Windows-Design für Apps (Einstellungen › Personalisierung › Farben).</summary>
    private static bool WindowsIstDunkel()
    {
        using var schlüssel = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return schlüssel?.GetValue("AppsUseLightTheme") is int hell && hell == 0;
    }

    /// <summary>Nutzer hat umgeschaltet: diese Wahl gilt ab jetzt statt des Windows-Designs.</summary>
    public static void DesignWählen(bool dunkel)
    {
        GewähltesDesign = dunkel;
        DesignAnwenden(dunkel);
    }

    /// <summary>Windows-Design geändert: nachziehen, solange der Nutzer nichts Eigenes gewählt hat.</summary>
    private static void SystemEinstellungGeändert(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || GewähltesDesign is not null)
            return;
        Current.Dispatcher.BeginInvoke(() =>
        {
            bool dunkel = WindowsIstDunkel();
            if (dunkel != IstDunkel)
                DesignAnwenden(dunkel);
        });
    }

    /// <summary>
    /// Schaltet zwischen hellem und dunklem Design: WPF-UI-Theme (Fenster, Steuerelemente),
    /// Akzentfarbe und die perPDF-Farbdatei (Themes/PerPdf*.xaml) werden gemeinsam getauscht.
    /// Die PDF-Seite selbst bleibt immer weiß.
    /// </summary>
    public static void DesignAnwenden(bool dunkel)
    {
        IstDunkel = dunkel;
        var theme = dunkel ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, false);
        ApplicationAccentColorManager.Apply(
            dunkel ? Color.FromRgb(0x2F, 0xB5, 0xA9) : Color.FromRgb(0x15, 0xA3, 0x9A), theme);

        var dicts = Current.Resources.MergedDictionaries;
        var alt = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("PerPdf") == true);
        if (alt is not null)
            dicts.Remove(alt);
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri(dunkel ? "Themes/PerPdfDunkel.xaml" : "Themes/PerPdfHell.xaml", UriKind.Relative)
        });
        DesignGeändert?.Invoke();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        if (e.Args.Contains("--selbsttest"))
        {
            Services.ScanPdf.Selbsttest(); // wirft bei Fehlschlag → Exitcode ≠ 0
            Shutdown(0);
            return;
        }
#endif
        // Design: eigene Wahl aus den Einstellungen, sonst das Windows-Design (live nachgeführt).
        GewähltesDesign = Services.FensterEinstellungen.Laden()?.DunkelGewählt;
        DesignAnwenden(GewähltesDesign ?? WindowsIstDunkel());
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEinstellungGeändert;
        // Die PDF-Dateizuordnung übernimmt ausschließlich das Setup (scope-abhängig),
        // daher registriert sich die App beim Start nicht mehr selbst.

        // Hauptfenster anzeigen (ohne StartupUri, direkt hier erzeugt).
        new MainWindow().Show();
    }
}
