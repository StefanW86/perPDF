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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Akzentfarbe der Oberfläche an das grüne perPDF-Logo anpassen.
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x15, 0xA3, 0x9A), ApplicationTheme.Light);
        // Die PDF-Dateizuordnung übernimmt ausschließlich das Setup (scope-abhängig),
        // daher registriert sich die App beim Start nicht mehr selbst.

        // Hauptfenster anzeigen (ohne StartupUri, direkt hier erzeugt).
        new MainWindow().Show();
    }
}
