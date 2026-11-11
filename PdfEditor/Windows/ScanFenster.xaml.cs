using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PdfEditor.Services;

namespace PdfEditor.Windows;

/// <summary>
/// Scan-Dialog („Neuer Scan“): Scanner/Quelle/Papier/Farbe/DPI/Helligkeit/Kontrast wählen,
/// Vorschau (nur Flachbett) und Scannen in beliebig vielen Durchgängen. Nach „Fertig“ steht
/// das PDF aller Seiten in Scanreihenfolge in <see cref="Ergebnis"/>.
/// </summary>
public partial class ScanFenster : Wpf.Ui.Controls.FluentWindow
{
    private record Eintrag<T>(T Wert, string Text);

    private static readonly int[] DpiWerte = [150, 200, 300, 600];

    private readonly List<(byte[] Png, ScanFarbe Farbe, int Dpi)> _seiten = new();
    private readonly ScanZustand _zustand = ScanEinstellungen.Laden();
    private List<ScannerInfo> _scanner = new();
    private ScannerInfo? _scannerAktiv;
    private bool _scanntGerade;

    /// <summary>Das PDF aller gescannten Seiten (nach <c>DialogResult == true</c>).</summary>
    public byte[]? Ergebnis { get; private set; }

    /// <summary>Anzahl der gescannten Seiten.</summary>
    public int SeitenAnzahl => _seiten.Count;

    public ScanFenster()
    {
        InitializeComponent();
        PapierAuswahl.ItemsSource = new[]
        {
            new Eintrag<ScanPapier>(ScanPapier.A4, "A4 (210 × 297 mm)"),
            new Eintrag<ScanPapier>(ScanPapier.A5, "A5 (148 × 210 mm)"),
            new Eintrag<ScanPapier>(ScanPapier.Letter, "Letter (8,5 × 11 in)")
        };
        FarbeAuswahl.ItemsSource = new[]
        {
            new Eintrag<ScanFarbe>(ScanFarbe.Farbe, "Farbe"),
            new Eintrag<ScanFarbe>(ScanFarbe.Graustufen, "Graustufen"),
            new Eintrag<ScanFarbe>(ScanFarbe.Schwarzweiß, "Schwarzweiß")
        };
        DpiAuswahl.ItemsSource = DpiWerte;

        Auswählen(PapierAuswahl, _zustand.Papier);
        Auswählen(FarbeAuswahl, _zustand.Farbe);
        DpiAuswahl.SelectedItem = DpiWerte.Contains(_zustand.Dpi) ? _zustand.Dpi : 300;
        HelligkeitRegler.Value = Math.Clamp(_zustand.Helligkeit, -100, 100);
        KontrastRegler.Value = Math.Clamp(_zustand.Kontrast, -100, 100);
        Bedienbar(false);
    }

    // ----- Scanner und Quellen ---------------------------------------------

    private async void Fenster_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _scanner = await Task.Run(ScanDienst.ScannerAuflisten);
        }
        catch (Exception)
        {
            _scanner = new(); // WIA-Dienst nicht verfügbar → wie „kein Scanner“
        }
        ScannerWählen(_scanner.FirstOrDefault(s => s.Id == _zustand.ScannerId) ?? _scanner.FirstOrDefault());
    }

    private async void ScannerWählen(ScannerInfo? scanner)
    {
        _scannerAktiv = scanner;
        if (scanner is null)
        {
            ScannerText.Text = "Kein Scanner gefunden";
            ScannerText.ToolTip = null;
            QuelleAuswahl.ItemsSource = null;
            Bedienbar(false);
            ScannerÄndernKnopf.IsEnabled = _scanner.Count > 0;
            return;
        }
        ScannerText.Text = "Scanner: " + scanner.Name;
        ScannerText.ToolTip = scanner.Name;
        Bedienbar(false);
        List<ScanQuelle> quellen = await Task.Run(() => ScanDienst.QuellenErmitteln(scanner.Id));
        if (!ReferenceEquals(_scannerAktiv, scanner))
            return; // inzwischen anderer Scanner gewählt
        QuelleAuswahl.ItemsSource = quellen.Select(q => new Eintrag<ScanQuelle>(q, q switch
        {
            ScanQuelle.Einzug => "Einzug (einseitig)",
            ScanQuelle.EinzugDuplex => "Einzug (beidseitig)",
            _ => "Flachbett"
        })).ToList();
        Auswählen(QuelleAuswahl, quellen.Contains(_zustand.Quelle) ? _zustand.Quelle : quellen[0]);
        Bedienbar(true);
    }

    private void ScannerÄndern_Click(object sender, RoutedEventArgs e)
    {
        var menü = new ContextMenu { PlacementTarget = ScannerÄndernKnopf };
        foreach (ScannerInfo s in _scanner)
        {
            var eintrag = new MenuItem
            {
                Header = s.Name,
                IsCheckable = true,
                IsChecked = ReferenceEquals(s, _scannerAktiv)
            };
            eintrag.Click += (_, _) => ScannerWählen(s);
            menü.Items.Add(eintrag);
        }
        menü.IsOpen = true;
    }

    private void Quelle_SelectionChanged(object sender, SelectionChangedEventArgs e) => VorschauFreigeben();

    private void VorschauFreigeben()
    {
        bool flachbett = Gewählt<ScanQuelle>(QuelleAuswahl) == ScanQuelle.Flachbett;
        VorschauKnopf.IsEnabled = !_scanntGerade && _scannerAktiv is not null
                                  && QuelleAuswahl.SelectedItem is not null && flachbett;
        VorschauKnopf.ToolTip = flachbett ? null : "Vorschau nur mit Flachbett möglich";
    }

    // ----- Vorschau und Scannen --------------------------------------------

    private async void Vorschau_Click(object sender, RoutedEventArgs e)
    {
        ScanZustand z = ZustandAusOberfläche();
        z.Dpi = 75;
        byte[]? vorschau = null;
        await ScanAusführen(z, png => vorschau = png);
        if (vorschau is not null)
            VorschauBild.Source = Bild(vorschau);
    }

    private async void Scannen_Click(object sender, RoutedEventArgs e)
    {
        ScanZustand z = ZustandAusOberfläche();
        int vorher = _seiten.Count;
        await ScanAusführen(z, png =>
        {
            _seiten.Add((png, z.Farbe, z.Dpi));
            VorschauBild.Source = Bild(png);
            SeitenTextAktualisieren();
        });
        if (_seiten.Count == vorher)
            return; // nichts gescannt (Fehler wurde bereits gemeldet)

        var antwort = MessageBox.Show(this,
            $"{_seiten.Count} Seite(n) gescannt.\n\nWeitere Seiten scannen?\n\n" +
            "Ja = weitere Seiten einlegen und erneut scannen\nNein = Scan abschließen",
            "Scannen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (antwort == MessageBoxResult.No)
            Abschließen();
    }

    /// <summary>Führt einen Scan-Durchgang aus; <paramref name="onSeite"/> läuft im UI-Thread.</summary>
    private async Task ScanAusführen(ScanZustand z, Action<byte[]> onSeite)
    {
        if (_scannerAktiv is null)
            return;
        _scanntGerade = true;
        Bedienbar(false);
        ScanAnzeige.Visibility = Visibility.Visible;
        try
        {
            await ScanDienst.ScannenAsync(_scannerAktiv.Id, z, png => Dispatcher.Invoke(() => onSeite(png)));
        }
        catch (ScanFehler ex)
        {
            string text = ex.Message;
            if (ex.BisherigeSeiten > 0)
                text += $"\n\nDie {ex.BisherigeSeiten} bereits gescannten Seite(n) dieses Durchgangs wurden " +
                        "übernommen. Bitte die restlichen Blätter neu einlegen.";
            MessageBox.Show(this, text, "Scannen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ScanAnzeige.Visibility = Visibility.Collapsed;
            _scanntGerade = false;
            Bedienbar(true);
        }
    }

    private void Abschließen()
    {
        try
        {
            Ergebnis = ScanPdf.Erstellen(_seiten);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Das PDF konnte nicht erstellt werden:\n" + ex.Message, "Scannen",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Einstellungenspeichern();
        DialogResult = true; // schließt das Fenster (Closing lässt es ohne Rückfrage durch)
    }

    // ----- Abbrechen / Schließen -------------------------------------------

    private void Abbrechen_Click(object sender, RoutedEventArgs e) => Close();

    private void Fenster_Closing(object? sender, CancelEventArgs e)
    {
        if (Ergebnis is not null)
            return;
        if (_scanntGerade)
        {
            e.Cancel = true; // während eines Scans nicht schließen
            return;
        }
        if (_seiten.Count > 0 && MessageBox.Show(this,
                $"{_seiten.Count} gescannte Seite(n) verwerfen?", "Scannen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        Einstellungenspeichern();
    }

    // ----- Hilfen ----------------------------------------------------------

    private void Bedienbar(bool an)
    {
        bool ok = an && !_scanntGerade && _scannerAktiv is not null && QuelleAuswahl.SelectedItem is not null;
        EinstellungenBereich.IsEnabled = !_scanntGerade;
        ScannenKnopf.IsEnabled = ok;
        AbbrechenKnopf.IsEnabled = !_scanntGerade;
        VorschauFreigeben();
    }

    private void SeitenTextAktualisieren() => SeitenText.Text = $"{_seiten.Count} Seite(n) gescannt.";

    private ScanZustand ZustandAusOberfläche() => new()
    {
        ScannerId = _scannerAktiv?.Id,
        Quelle = Gewählt<ScanQuelle>(QuelleAuswahl),
        Papier = Gewählt<ScanPapier>(PapierAuswahl),
        Farbe = Gewählt<ScanFarbe>(FarbeAuswahl),
        Dpi = DpiAuswahl.SelectedItem is int dpi ? dpi : 300,
        Helligkeit = (int)HelligkeitRegler.Value,
        Kontrast = (int)KontrastRegler.Value
    };

    private void Einstellungenspeichern()
    {
        if (_scannerAktiv is not null && QuelleAuswahl.SelectedItem is not null)
            ScanEinstellungen.Speichern(ZustandAusOberfläche());
    }

    private static T Gewählt<T>(ComboBox box) => box.SelectedItem is Eintrag<T> e ? e.Wert : default!;

    private static void Auswählen<T>(ComboBox box, T wert) =>
        box.SelectedItem = box.ItemsSource?.Cast<Eintrag<T>>().FirstOrDefault(e => Equals(e.Wert, wert));

    private static BitmapImage Bild(byte[] png)
    {
        var bild = new BitmapImage();
        bild.BeginInit();
        bild.CacheOption = BitmapCacheOption.OnLoad;
        bild.DecodePixelWidth = 1000; // Vorschau braucht keine volle Auflösung
        bild.StreamSource = new MemoryStream(png);
        bild.EndInit();
        bild.Freeze();
        return bild;
    }
}
