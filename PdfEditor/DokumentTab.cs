using System.ComponentModel;
using System.Windows;
using System.Windows.Shapes;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.ViewModels;

namespace PdfEditor;

/// <summary>
/// Kapselt den Zustand eines geöffneten Dokuments in einem Tab –
/// ViewModel, Text-Cache und UI-Zeichenstatus.
/// </summary>
internal sealed class DokumentTab : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public HauptViewModel ViewModel { get; } = new();
    public PdfTextDienst TextDienst { get; } = new();
    public Dictionary<int, List<TextZeile>> TextCache { get; } = new();

    public DokumentTab()
    {
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HauptViewModel.Pfad) or nameof(HauptViewModel.Titel))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabTitel)));
        };
    }

    // Suche
    public List<(int Seite, int Zeile, int Start, int Ende)> SuchTreffer = new();
    public int AktiverTreffer = -1;
    public string LetzterSuchbegriff = string.Empty;

    // Leuchtstift-Zeichenstatus
    public bool MarkerZeichnet;
    public Textmarkierung? AktiverStrich;
    public Polyline? StrichVorschau;
    public bool RadiererZieht;

    // Schwebende Cursor-Vorschau im Platziermodus
    public FrameworkElement? CursorVorschau;

    /// <summary>Angezeigter Text auf dem Tab-Reiter.</summary>
    public string TabTitel => ViewModel.Pfad is null
        ? "Ohne Titel"
        : System.IO.Path.GetFileName(ViewModel.Pfad);
}
