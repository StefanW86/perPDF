using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.ViewModels;

namespace PdfEditor;

/// <summary>
/// Hauptfenster der Anwendung. Zeigt die aktuelle Seite an, baut die
/// Formularfeld- und Unterschriften-Overlays auf und behandelt das Umsortieren
/// der Seiten per Drag-and-drop.
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow, IDropTarget
{
    private HauptViewModel _vm = null!; // zeigt immer auf _aktiverTab.ViewModel

    // Geometrie der aktuell gezeichneten Seite (für das Umrechnen beim Zeichnen).
    private SeitenGeometrie? _geo;

    // ----- Tab-Verwaltung -------------------------------------------------------
    private readonly System.Collections.ObjectModel.ObservableCollection<DokumentTab> _tabs = new();
    private DokumentTab? _aktiverTab;

    // Kurzreferenzen auf Tab-State (Delegation auf den aktiven Tab).
    private PdfTextDienst _textDienst => _aktiverTab?.TextDienst ?? _leerTextDienst;
    private Dictionary<int, List<TextZeile>> _textCache => _aktiverTab?.TextCache ?? _leerTextCache;
    private List<(int Seite, int Zeile, int Start, int Ende)> _suchTreffer
    {
        get => _aktiverTab?.SuchTreffer ?? _leerSuchTreffer;
        set { if (_aktiverTab != null) _aktiverTab.SuchTreffer = value; }
    }
    private int _aktiverTreffer
    {
        get => _aktiverTab?.AktiverTreffer ?? -1;
        set { if (_aktiverTab != null) _aktiverTab.AktiverTreffer = value; }
    }
    private string _letzterSuchbegriff
    {
        get => _aktiverTab?.LetzterSuchbegriff ?? string.Empty;
        set { if (_aktiverTab != null) _aktiverTab.LetzterSuchbegriff = value; }
    }

    // Marker-/Radierer-State – delegiert auf aktiven Tab.
    private bool _markerZeichnet
    {
        get => _aktiverTab?.MarkerZeichnet ?? false;
        set { if (_aktiverTab != null) _aktiverTab.MarkerZeichnet = value; }
    }
    private Textmarkierung? _aktiverStrich
    {
        get => _aktiverTab?.AktiverStrich;
        set { if (_aktiverTab != null) _aktiverTab.AktiverStrich = value; }
    }
    private Polyline? _strichVorschau
    {
        get => _aktiverTab?.StrichVorschau;
        set { if (_aktiverTab != null) _aktiverTab.StrichVorschau = value; }
    }
    private bool _radiererZieht
    {
        get => _aktiverTab?.RadiererZieht ?? false;
        set { if (_aktiverTab != null) _aktiverTab.RadiererZieht = value; }
    }
    private FrameworkElement? _cursorVorschau
    {
        get => _aktiverTab?.CursorVorschau;
        set { if (_aktiverTab != null) _aktiverTab.CursorVorschau = value; }
    }

    // Fallback-Objekte wenn kein Tab aktiv ist.
    private static readonly PdfTextDienst _leerTextDienst = new();
    private static readonly Dictionary<int, List<TextZeile>> _leerTextCache = new();
    private static readonly List<(int, int, int, int)> _leerSuchTreffer = new();

    // ----- Suche ---------------------------------------------------------------
    private readonly DispatcherTimer _suchTimer;

    /// <summary>Ein Sondermodus (Marker/Radierer/Felder bearbeiten) ist aktiv.</summary>
    private bool InSpezialModus => _vm.MarkerModus || _vm.RadiererModus || _vm.FelderBearbeitenModus;

    // Markenfarben (perPDF-Logo) für Auswahlrahmen und Griffe.
    private static readonly Brush Akzent = Eingefroren(Color.FromRgb(0x0E, 0x7C, 0x7B));
    private static readonly Brush FeldHintergrund = Eingefroren(Color.FromArgb(40, 0x15, 0xA3, 0x9A));

    private static Brush Eingefroren(Color farbe)
    {
        var b = new SolidColorBrush(farbe);
        b.Freeze();
        return b;
    }

    public MainWindow()
    {
        InitializeComponent();
        FensterZustandWiederherstellen();
        Closing += FensterSchließt;

        // Tab-Leiste binden.
        TabLeiste.ItemsSource = _tabs;

        // Leer-VM als Platzhalter bis zum ersten Tab.
        _vm = new HauptViewModel();
        DataContext = _vm;

        // Suchtimer (Debounce 400 ms nach Texteingabe).
        _suchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _suchTimer.Tick += (_, _) => { _suchTimer.Stop(); DokumentSuchen(); };

        // Strg + Mausrad zoomt zum Mauszeiger; sonst blättert das Rad am Seitenrand weiter.
        SeitenScroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ZumZeigerZoomen(e);
                e.Handled = true;
                return;
            }
            BlätterBeiRand(e);
        };

        // Esc beendet jeden Sondermodus; Strg+F öffnet Suche.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _vm.MarkerModus = false;
                _vm.RadiererModus = false;
                _vm.FelderBearbeitenModus = false;
                _vm.EinfügeModusBeenden();
                _vm.FeldModusBeenden();
                return;
            }
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                SuchPanelÖffnen();
                e.Handled = true;
            }
            // Strg+V fügt ein Bild aus der Zwischenablage ein – aber nur, wenn der
            // Fokus nicht in einem Textfeld liegt (dort gilt das normale Einfügen).
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0
                && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase
                && _vm.HatDokument && ZwischenablageHatBild())
            {
                _vm.BildAusZwischenablage();
                e.Handled = true;
            }
        };

        // Marker-/Radierer-Gesten direkt auf der Seitenfläche behandeln.
        OverlayCanvas.Focusable = true;
        OverlayCanvas.PreviewMouseLeftButtonDown += OverlayMausUnten;
        OverlayCanvas.PreviewMouseMove += OverlayMausBewegung;
        OverlayCanvas.PreviewMouseLeftButtonUp += OverlayMausOben;

        // Rechtsklick beendet jeden aktiven Modus (Marker, Radierer, Einfügen).
        OverlayCanvas.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_vm.MarkerModus || _vm.RadiererModus || _vm.FelderBearbeitenModus
                || _vm.EinfügeModus != null || _vm.NeuesFeldTyp != null)
            {
                _vm.MarkerModus = false;
                _vm.RadiererModus = false;
                _vm.FelderBearbeitenModus = false;
                _vm.EinfügeModusBeenden();
                _vm.FeldModusBeenden();
                e.Handled = true;
            }
        };

        // Vorschau-Bewegung immer auf der Canvas verfolgen.
        OverlayCanvas.MouseMove += VorschauBewegen;
        OverlayCanvas.MouseLeave += (_, _) =>
        {
            if (_cursorVorschau != null) _cursorVorschau.Visibility = Visibility.Hidden;
        };
        OverlayCanvas.MouseEnter += (_, _) =>
        {
            if (_cursorVorschau != null) _cursorVorschau.Visibility = Visibility.Visible;
        };

        // Die Verbindungslinie folgt dem Scrollen von Seite und Kommentarliste.
        SeitenScroll.ScrollChanged += (_, _) => ZeichneVerbindungslinie();
        SizeChanged += (_, _) => ZeichneVerbindungslinie();

        // Dieses Fenster als Ziel für das Umsortieren der Miniaturen registrieren.
        GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(MiniaturListe, this);

        // Beim Start übergebene Dateien laden (z.B. via "Öffnen mit" oder Kommandozeile,
        // perPDF.exe Pfad1.pdf [Pfad2.pdf ...]). Jede vorhandene Datei öffnet einen Tab.
        StartdateienLaden(Environment.GetCommandLineArgs());
    }

    /// <summary>
    /// Öffnet die beim Programmstart als Argumente übergebenen PDF-Dateien (jeweils in
    /// einem eigenen Tab). Das erste Argument (der Programmpfad) und Optionsschalter
    /// (mit „-"/„/" beginnend) werden übersprungen.
    /// </summary>
    private void StartdateienLaden(string[] args)
    {
        foreach (var arg in args.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-') || arg.StartsWith('/'))
                continue;
            if (!System.IO.File.Exists(arg))
                continue;
            var tab = TabNeuErstellen();
            tab.ViewModel.DateiLaden(arg);
        }
    }

    /// <summary>Prüft absturzsicher, ob die Zwischenablage ein Bild enthält.</summary>
    private static bool ZwischenablageHatBild()
    {
        try { return Clipboard.ContainsImage(); }
        catch { return false; } // Zwischenablage kann von anderem Prozess gesperrt sein
    }

    /// <summary>Stellt die zuletzt gespeicherte Fenstergröße/-position wieder her.</summary>
    private void FensterZustandWiederherstellen()
    {
        var z = FensterEinstellungen.Laden();
        if (z is null || z.Breite < 200 || z.Höhe < 150)
            return;

        // Immer die Größe übernehmen.
        Width = z.Breite;
        Height = z.Höhe;

        // Position nur übernehmen, wenn sie ausreichend im sichtbaren Bereich liegt
        // (sonst – z. B. nach geändertem Monitor-Setup – zentriert das Fenster wie gehabt).
        if (PositionSichtbar(z))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = z.Links;
            Top = z.Oben;
        }

        if (z.Maximiert)
            WindowState = WindowState.Maximized;
    }

    /// <summary>Prüft, ob ein nennenswerter Teil des Fensters auf einem Bildschirm läge.</summary>
    private static bool PositionSichtbar(FensterZustand z)
    {
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;
        double sichtbarX = Math.Min(z.Links + z.Breite, vr) - Math.Max(z.Links, vl);
        double sichtbarY = Math.Min(z.Oben + z.Höhe, vb) - Math.Max(z.Oben, vt);
        return sichtbarX > 100 && sichtbarY > 100;
    }

    /// <summary>Speichert die aktuelle Fenstergeometrie beim Schließen.</summary>
    private void FensterZustandSpeichern()
    {
        var z = new FensterZustand();
        if (WindowState == WindowState.Maximized)
        {
            // Im maximierten Zustand die Wiederherstellungsgröße sichern.
            z.Maximiert = true;
            var rb = RestoreBounds;
            z.Links = rb.Left;
            z.Oben = rb.Top;
            z.Breite = rb.Width;
            z.Höhe = rb.Height;
        }
        else
        {
            z.Links = Left;
            z.Oben = Top;
            z.Breite = Width;
            z.Höhe = Height;
        }
        FensterEinstellungen.Speichern(z);
    }

}
