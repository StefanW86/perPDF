using System.Windows;

namespace PdfEditor.ViewModels;

/// <summary>
/// Mehrseiten-Operationen: Löschen und Drehen einer Auswahl von Seiten. Die
/// Auswahl kommt aus der Miniaturliste des Hauptfensters (Extended-Selection)
/// und wird hier gespiegelt, damit auch die Symbolleisten-Befehle auf die
/// gesamte Auswahl wirken.
/// </summary>
public partial class HauptViewModel
{
    /// <summary>Aktuell in der Miniaturliste ausgewählte Seitenindizes (aufsteigend).</summary>
    private readonly List<int> _ausgewählteSeiten = new();

    /// <summary>Übernimmt die Mehrfachauswahl der Miniaturliste aus dem Hauptfenster.</summary>
    public void AusgewählteSeitenSetzen(IEnumerable<int> indizes)
    {
        _ausgewählteSeiten.Clear();
        _ausgewählteSeiten.AddRange(indizes.Where(i => i >= 0).Distinct().OrderBy(i => i));
    }

    /// <summary>
    /// Die Zielseiten für Seitenbefehle: die Mehrfachauswahl, sonst die aktuelle Seite.
    /// </summary>
    private List<int> ZielSeiten()
    {
        if (_ausgewählteSeiten.Count > 0)
            return new List<int>(_ausgewählteSeiten);
        return AktuelleSeite >= 0 ? new List<int> { AktuelleSeite } : new List<int>();
    }

    /// <summary>
    /// Löscht die angegebenen Seiten (mit Rückfrage ab zwei Seiten; entfällt mit
    /// <paramref name="rückfragen"/> = false, wenn der Aufrufer bereits bestätigt hat,
    /// z. B. der Seiten-entfernen-Dialog). Mindestens eine Seite muss im Dokument verbleiben.
    /// </summary>
    public void SeitenLöschen(IReadOnlyCollection<int> indizes, bool rückfragen = true)
    {
        // Absteigend sortiert, damit die Indizes beim Entfernen gültig bleiben.
        var ziele = indizes.Where(i => i >= 0 && i < Seiten.Count)
                           .Distinct().OrderByDescending(i => i).ToList();
        if (ziele.Count == 0)
            return;
        if (ziele.Count >= Seiten.Count)
        {
            Status = "Mindestens eine Seite muss im Dokument verbleiben.";
            return;
        }
        if (rückfragen && ziele.Count > 1)
        {
            var antwort = MessageBox.Show(
                $"Sollen die {ziele.Count} ausgewählten Seiten gelöscht werden?",
                "Seiten löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (antwort != MessageBoxResult.Yes)
                return;
        }

        try
        {
            _dienst.SeitenLöschen(ziele);
            // Annotationen je gelöschter Seite entfernen, nachfolgende verschieben –
            // in derselben absteigenden Reihenfolge wie das Löschen selbst.
            foreach (int i in ziele)
                AnnotationenBeiLöschen(i);
            FelderNeuLaden();
            _ausgewählteSeiten.Clear();
            NachStrukturänderung(Math.Max(0, ziele[^1] - 1));
            Status = ziele.Count == 1 ? "Seite gelöscht." : $"{ziele.Count} Seiten gelöscht.";
        }
        catch (Exception ex)
        {
            Fehler("Die Seiten konnten nicht gelöscht werden.", ex);
        }
    }

    /// <summary>Dreht die angegebenen Seiten um den Winkel (z. B. +90 oder -90 Grad).</summary>
    public void SeitenDrehen(IReadOnlyCollection<int> indizes, int delta)
    {
        var ziele = indizes.Where(i => i >= 0 && i < Seiten.Count).Distinct().ToList();
        if (ziele.Count == 0)
            return;

        try
        {
            _dienst.SeitenDrehen(ziele, delta);
            // Drehung verändert die Feldrechtecke in PDF-Punkten nicht; nur neu zeichnen.
            _seitenInfos = _dienst.SeitenInfosLesen();
            foreach (int i in ziele)
                MiniaturNeuZeichnen(i);
            AktuelleSeiteNeu?.Invoke();
            if (!_istLaden) IstGeändert = true;
            Status = ziele.Count == 1 ? "Seite gedreht." : $"{ziele.Count} Seiten gedreht.";
        }
        catch (Exception ex)
        {
            Fehler("Die Seiten konnten nicht gedreht werden.", ex);
        }
    }
}
