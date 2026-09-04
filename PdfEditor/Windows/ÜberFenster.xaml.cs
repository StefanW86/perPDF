using System.Windows;

namespace PdfEditor.Windows;

/// <summary>
/// Zeigt Versionsinformation, Copyright-Hinweis, Lizenztext und eine
/// Übersicht der verwendeten Drittanbieter-Bibliotheken an.
/// </summary>
public partial class ÜberFenster : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>Initialisiert das Fenster und trägt die aktuelle Versionsnummer ein.</summary>
    public ÜberFenster()
    {
        InitializeComponent();
        VersionNummer.Text = BuildInfo.Version;
        CopyrightHinweis.Text = $"© {BuildInfo.Jahr} Alle Rechte vorbehalten";
    }

    private void SchließenKlick(object sender, RoutedEventArgs e) => Close();
}
