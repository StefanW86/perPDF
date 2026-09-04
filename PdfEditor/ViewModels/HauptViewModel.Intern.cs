using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>Interne Hilfsfunktionen: Neuaufbau, Strukturänderung, Annotationen-Remapping.</summary>
public partial class HauptViewModel
{
    // ----- interne Hilfsfunktionen -----------------------------------------

    private void FelderNeuLaden()
    {
        // Bisherige Eingaben merken (nach vollständigem Feldnamen) und Beobachtung lösen.
        var alteWerte = new Dictionary<string, FormularFeld>();
        foreach (var f in Felder)
        {
            alteWerte[f.FeldName] = f;
            f.PropertyChanged -= FormularfeldGeändert;
        }

        var neu = _dienst.FormularfelderLesen();
        // Felder, die gelöscht oder zum Bearbeiten in einen Entwurf übernommen wurden,
        // nicht erneut als ausfüllbare Felder einblenden (sie sind noch in den Bytes, bis
        // gespeichert wird). So bleiben sie auch über Strukturänderungen hinweg ausgeblendet.
        if (_zuLöschendeFelder.Count > 0)
            neu.RemoveAll(f => _zuLöschendeFelder.Contains(FeldKennung.Von(f)));
        // Werte vor dem Beobachten übernehmen, damit das Wiederherstellen nicht als
        // Benutzeränderung zählt.
        foreach (var f in neu)
        {
            if (alteWerte.TryGetValue(f.FeldName, out var alt))
            {
                f.Wert = alt.Wert;
                f.IstAngehakt = alt.IstAngehakt;
            }
        }

        Felder.Clear();
        foreach (var f in neu)
        {
            Felder.Add(f);
            // Ausfüllen eines Feldes markiert das Dokument als ungespeichert geändert.
            f.PropertyChanged += FormularfeldGeändert;
        }
    }

    /// <summary>
    /// Markiert das Dokument als geändert, sobald der Benutzer ein Formularfeld ausfüllt
    /// (Texteingabe, Kontrollkästchen, Auswahl-/Optionsfeld). So spiegeln Titel-Sternchen,
    /// Tab-Markierung und die Schließen-Nachfrage auch reine Formulareingaben wider.
    /// </summary>
    private void FormularfeldGeändert(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_istLaden)
            return;
        if (e.PropertyName is nameof(FormularFeld.Wert) or nameof(FormularFeld.IstAngehakt))
            IstGeändert = true;
    }

    private void NachStrukturänderung(int neueAuswahl)
    {
        if (!_istLaden) IstGeändert = true;
        _markerModus = _radiererModus = false;
        Melde(nameof(MarkerModus));
        Melde(nameof(RadiererModus));
        _felderBearbeitenModus = false;
        Melde(nameof(FelderBearbeitenModus));
        EinfügeModusBeenden();
        FeldModusBeenden();
        Entwurf = null;
        _seitenInfos = _dienst.SeitenInfosLesen();
        SeitenNeuAufbauen();
        _aktuelleSeite = Math.Clamp(neueAuswahl, 0, Math.Max(0, Seiten.Count - 1));
        Melde(nameof(AktuelleSeite));
        Melde(nameof(SeitenStatus));
        Melde(nameof(HatDokument));
        // Inhaltsverzeichnis aus dem Dokument lesen.
        InhaltsverzeichnisNeuLaden();
        // Kommentare leben als Annotationen in den Bytes – nach jeder Strukturänderung
        // frisch einlesen (sie wandern automatisch mit ihren Seiten mit).
        KommentareNeuLaden();
        if (Kommentare.Count > 0)
            KommentareSichtbar = true;
        BefehleAktualisieren();
        AnsichtKomplettNeu?.Invoke();
    }

    /// <summary>
    /// Asynchrone Variante von NachStrukturänderung für den Ladevorgang:
    /// rendert Thumbnails seitenweise im Hintergrund und meldet Fortschritt.
    /// </summary>
    private async Task NachStrukturänderungAsync(int neueAuswahl)
    {
        if (!_istLaden) IstGeändert = true;
        _markerModus = _radiererModus = false;
        Melde(nameof(MarkerModus));
        Melde(nameof(RadiererModus));
        _felderBearbeitenModus = false;
        Melde(nameof(FelderBearbeitenModus));
        EinfügeModusBeenden();
        FeldModusBeenden();
        Entwurf = null;
        _seitenInfos = _dienst.SeitenInfosLesen();
        await SeitenNeuAufbauenAsync();
        _aktuelleSeite = Math.Clamp(neueAuswahl, 0, Math.Max(0, Seiten.Count - 1));
        Melde(nameof(AktuelleSeite));
        Melde(nameof(SeitenStatus));
        Melde(nameof(HatDokument));
        // Inhaltsverzeichnis aus dem Dokument lesen.
        InhaltsverzeichnisNeuLaden();
        KommentareNeuLaden();
        if (Kommentare.Count > 0)
            KommentareSichtbar = true;
        BefehleAktualisieren();
        AnsichtKomplettNeu?.Invoke();
    }

    private void SeitenNeuAufbauen()
    {
        Seiten.Clear();
        if (Bytes is null)
            return;
        for (int i = 0; i < _seitenInfos.Count; i++)
        {
            var el = new SeitenElement { Nummer = i + 1 };
            try { el.Vorschau = PdfRenderDienst.MiniaturRendern(Bytes, i, 150); }
            catch { /* Miniatur bleibt leer, falls Rendern fehlschlägt */ }
            Seiten.Add(el);
        }
    }

    /// <summary>
    /// Asynchrone Variante: legt Seiten-Platzhalter sofort an und rendert
    /// Thumbnails seitenweise im Hintergrund; löst SeiteGerendert-Events aus.
    /// </summary>
    private async Task SeitenNeuAufbauenAsync()
    {
        Seiten.Clear();
        if (Bytes is null)
            return;

        int gesamt = _seitenInfos.Count;
        // Platzhalter sofort eintragen, damit die Liste nicht leer wirkt.
        for (int i = 0; i < gesamt; i++)
            Seiten.Add(new SeitenElement { Nummer = i + 1 });

        // Thumbnails seitenweise im Hintergrund rendern.
        var byteKopie = Bytes; // Snapshot, damit kein Race auf AktuelleBytes
        for (int i = 0; i < gesamt; i++)
        {
            int idx = i;
            SeiteGerendert?.Invoke(idx + 1, gesamt);
            try
            {
                var vorschau = await Task.Run(() => PdfRenderDienst.MiniaturRendern(byteKopie, idx, 150));
                Seiten[idx].Vorschau = vorschau;
            }
            catch { /* Miniatur bleibt leer */ }
        }
        SeiteGerendert?.Invoke(gesamt, gesamt);
    }

    private void MiniaturNeuZeichnen(int index)
    {
        if (Bytes is null || index < 0 || index >= Seiten.Count)
            return;
        try { Seiten[index].Vorschau = PdfRenderDienst.MiniaturRendern(Bytes, index, 150); }
        catch { /* ignorieren */ }
    }

    private void NummernAktualisieren()
    {
        for (int i = 0; i < Seiten.Count; i++)
            Seiten[i].Nummer = i + 1;
    }

    /// <summary>
    /// Setzt alle ausstehenden Overlays und Feld-Vormerkungen zurück – nach dem Laden
    /// eines Dokuments (alter Zustand gehört nicht zum neuen Dokument) und nach dem
    /// Speichern (alles ist nun fest eingebrannt bzw. gebacken).
    /// </summary>
    private void AusstehendeZurücksetzen()
    {
        Platzierungen.Clear();
        Bilder.Clear();
        Textnotizen.Clear();
        Textmarkierungen.Clear();
        Symbolmarkierungen.Clear();
        Entwurfsfelder.Clear();
        Seitenabdeckungen.Clear();
        _zuLöschendeFelder.Clear();
        AusgewähltesFeld = null;
    }

    private IEnumerable<IAufSeite> AlleAnnotationen()
        => Platzierungen.Cast<IAufSeite>().Concat(Bilder).Concat(Textnotizen)
            .Concat(Textmarkierungen).Concat(Symbolmarkierungen).Concat(Entwurfsfelder)
            .Concat(Seitenabdeckungen);

    /// <summary>Verschiebt die Seitenzuordnung aller Annotationen (Unterschriften, Texte, Marker).</summary>
    private void AnnotationenVerschieben(Func<int, int> neuerIndex)
    {
        foreach (var a in AlleAnnotationen())
            a.SeitenIndex = neuerIndex(a.SeitenIndex);
    }

    /// <summary>Entfernt Annotationen einer gelöschten Seite und verschiebt nachfolgende.</summary>
    private void AnnotationenBeiLöschen(int index)
    {
        EntferneOderVerschiebe(Platzierungen, index);
        EntferneOderVerschiebe(Bilder, index);
        EntferneOderVerschiebe(Textnotizen, index);
        EntferneOderVerschiebe(Textmarkierungen, index);
        EntferneOderVerschiebe(Symbolmarkierungen, index);
        EntferneOderVerschiebe(Entwurfsfelder, index);
        EntferneOderVerschiebe(Seitenabdeckungen, index);
        if (_ausgewähltesFeld is not null && !Entwurfsfelder.Contains(_ausgewähltesFeld))
            AusgewähltesFeld = null;
    }

    private static void EntferneOderVerschiebe<T>(ObservableCollection<T> sammlung, int index)
        where T : IAufSeite
    {
        for (int i = sammlung.Count - 1; i >= 0; i--)
        {
            if (sammlung[i].SeitenIndex == index)
                sammlung.RemoveAt(i);
            else if (sammlung[i].SeitenIndex > index)
                sammlung[i].SeitenIndex--;
        }
    }

    private static int IndexNachVerschieben(int x, int alt, int neu)
    {
        if (x == alt) return neu;
        int t = x;
        if (x > alt) t--;
        if (t >= neu) t++;
        return t;
    }

    private void BefehleAktualisieren()
    {
        SpeichernBefehl.AusführbarkeitAktualisieren();
        SpeichernUnterBefehl.AusführbarkeitAktualisieren();
        DruckenBefehl.AusführbarkeitAktualisieren();
        SeiteImportierenBefehl.AusführbarkeitAktualisieren();
        SeiteLöschenBefehl.AusführbarkeitAktualisieren();
        RechtsDrehenBefehl.AusführbarkeitAktualisieren();
        LinksDrehenBefehl.AusführbarkeitAktualisieren();
        ZoomPlusBefehl.AusführbarkeitAktualisieren();
        ZoomMinusBefehl.AusführbarkeitAktualisieren();
        TextEinfügenBefehl.AusführbarkeitAktualisieren();
        BildEinfügenBefehl.AusführbarkeitAktualisieren();
    }

    private void Fehler(string nachricht, Exception ex)
    {
        Status = nachricht;
        MessageBox.Show($"{nachricht}\n\nDetails: {ex.Message}", "Fehler",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
