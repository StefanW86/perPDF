using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Services;

namespace PdfEditor.Windows;

/// <summary>Optionsdialog von perPDF (derzeit: Ordner und Dateiname neuer Scans).</summary>
public partial class OptionenFenster : Wpf.Ui.Controls.FluentWindow
{
    public OptionenFenster()
    {
        InitializeComponent();
        OptionenDaten o = Optionen.Laden();
        OrdnerFeld.Text = o.ScanOrdner;
        DateinameFeld.Text = o.ScanDateiname;
    }

    private void Eingabe_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (BeispielText is null)
            return; // während InitializeComponent
        string ordner = string.IsNullOrWhiteSpace(OrdnerFeld.Text) ? Optionen.StandardScanOrdner : OrdnerFeld.Text.Trim();
        BeispielText.Text = "Beispiel: " + Path.Combine(ordner, Optionen.ScanDateiname(DateinameFeld.Text, DateTime.Now));
    }

    private void Durchsuchen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Ordner für neue Scans wählen" };
        if (Directory.Exists(OrdnerFeld.Text))
            dlg.InitialDirectory = OrdnerFeld.Text;
        if (dlg.ShowDialog(this) == true)
            OrdnerFeld.Text = dlg.FolderName;
    }

    private void Standard_Click(object sender, RoutedEventArgs e)
    {
        OrdnerFeld.Text = Optionen.StandardScanOrdner;
        DateinameFeld.Text = Optionen.StandardScanDateiname;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string ordner = OrdnerFeld.Text.Trim();
        if (!Path.IsPathFullyQualified(ordner) || ordner.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            MessageBox.Show(this, "Bitte einen vollständigen Ordnerpfad angeben, z. B. C:\\Users\\Name\\Documents\\Scans.",
                "Optionen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Optionen.Speichern(new OptionenDaten { ScanOrdner = ordner, ScanDateiname = DateinameFeld.Text.Trim() });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Die Optionen konnten nicht gespeichert werden:\n" + ex.Message,
                "Optionen", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        DialogResult = true;
    }
}
