using System.Windows;

namespace PdfEditor.ViewModels;

/// <summary>Kennwortschutz des Dokuments setzen bzw. entfernen.</summary>
public partial class HauptViewModel
{
    public RelayCommand KennwortSchützenBefehl { get; }
    public RelayCommand KennwortEntfernenBefehl { get; }

    private void KennwortSchützen()
    {
        string? kennwort = Windows.KennwortFenster.Abfragen("Mit Kennwort schützen",
            "Die Datei wird verschlüsselt gespeichert und lässt sich nur noch mit diesem Kennwort öffnen.",
            "Schützen", wiederholen: true);
        if (kennwort is not null)
            KennwortÄndern(kennwort, "Datei mit Kennwort geschützt.");
    }

    private void KennwortEntfernen() => KennwortÄndern(null, "Kennwortschutz entfernt.");

    /// <summary>Setzt das Kennwort und speichert sofort; bei Abbruch bleibt der alte Schutz.</summary>
    private void KennwortÄndern(string? neu, string meldung)
    {
        string? alt = _dienst.Kennwort;
        _dienst.Kennwort = neu;
        if (!Speichern(false))
        {
            _dienst.Kennwort = alt;
            return;
        }
        Status = meldung;
        BefehleAktualisieren();
    }
}
