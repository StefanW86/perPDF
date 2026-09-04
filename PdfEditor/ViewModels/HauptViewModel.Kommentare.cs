using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>Kommentar-Funktionen: Entwurf, Speichern, Status, Antworten, Auswahl.</summary>
public partial class HauptViewModel
{
    // ----- Kommentare -------------------------------------------------------

    /// <summary>Schaltet die Sichtbarkeit der Kommentar-Seitenleiste um.</summary>
    public void KommentareUmschalten() => KommentareSichtbar = !KommentareSichtbar;

    /// <summary>
    /// Beginnt einen neuen Kommentar zum markierten Text (QuadPoints in PDF-Punkten).
    /// Öffnet die Seitenleiste mit dem Eingabefeld; gespeichert wird erst bei Bestätigung.
    /// </summary>
    public void KommentarEntwurfBeginnen(int seitenIndex,
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)> quads, string markierterText)
    {
        if (!_dienst.HatDokument || quads.Count == 0)
            return;
        Entwurf = new Kommentar
        {
            SeitenIndex = seitenIndex,
            Rechtecke = quads.ToList(),
            MarkierterText = markierterText,
            Autor = _autor,
            Erstellt = DateTime.Now,
            ObjektNummer = -1,
            Status = KommentarStatus.Offen,
            Inhalt = string.Empty
        };
        _ausgewählterKommentar = null;
        Melde(nameof(AusgewählterKommentar));
        KommentareSichtbar = true;
        AktuelleSeiteNeu?.Invoke();
        Status = "Kommentar eingeben und mit „Speichern“ bestätigen (Esc verwirft).";
    }

    /// <summary>Speichert den aktuellen Entwurf als echte PDF-Kommentar-Annotation.</summary>
    public void EntwurfSpeichern(string text)
    {
        var e = Entwurf;
        if (e is null)
            return;
        if (string.IsNullOrWhiteSpace(text))
        {
            EntwurfVerwerfen();
            return;
        }
        try
        {
            string kennung = _dienst.KommentarHinzufügen(
                e.SeitenIndex, e.Rechtecke, text.Trim(), _autor);
            Entwurf = null;
            KommentareNeuLaden(kennung);
            AktuelleSeiteNeu?.Invoke();
            Status = "Kommentar hinzugefügt.";
        }
        catch (Exception ex)
        {
            Fehler("Der Kommentar konnte nicht gespeichert werden.", ex);
        }
    }

    /// <summary>Verwirft den aktuellen Kommentar-Entwurf ohne zu speichern.</summary>
    public void EntwurfVerwerfen()
    {
        if (Entwurf is null)
            return;
        Entwurf = null;
        AktuelleSeiteNeu?.Invoke();
        Status = "Kommentar verworfen.";
    }

    /// <summary>
    /// Schaltet einen Reviewstatus um: erneutes Klicken auf denselben Status setzt
    /// wieder auf „Offen“ zurück.
    /// </summary>
    public void KommentarStatusUmschalten(Kommentar k, KommentarStatus ziel)
    {
        if (k is null)
            return;
        KommentarStatus neu = k.Status == ziel ? KommentarStatus.Offen : ziel;
        try
        {
            _dienst.KommentarStatusSetzen(k.ObjektNummer, neu, _autor);
            KommentareNeuLaden(k.Kennung);
            AktuelleSeiteNeu?.Invoke();
            Status = neu switch
            {
                KommentarStatus.Erledigt => "Kommentar als erledigt markiert.",
                KommentarStatus.Abgelehnt => "Kommentar als abgelehnt markiert.",
                _ => "Status zurückgesetzt."
            };
        }
        catch (Exception ex)
        {
            Fehler("Der Status konnte nicht geändert werden.", ex);
        }
    }

    /// <summary>Löscht einen Kommentar dauerhaft aus dem Dokument.</summary>
    public void KommentarLöschen(Kommentar k)
    {
        if (k is null)
            return;
        try
        {
            _dienst.KommentarLöschen(k.ObjektNummer);
            if (ReferenceEquals(_ausgewählterKommentar, k))
                _ausgewählterKommentar = null;
            KommentareNeuLaden();
            AktuelleSeiteNeu?.Invoke();
            Status = "Kommentar gelöscht.";
        }
        catch (Exception ex)
        {
            Fehler("Der Kommentar konnte nicht gelöscht werden.", ex);
        }
    }

    /// <summary>Fügt einem Kommentar eine Antwort hinzu.</summary>
    public void KommentarAntwortHinzufügen(Kommentar k, string text)
    {
        if (k is null || string.IsNullOrWhiteSpace(text))
            return;
        try
        {
            _dienst.KommentarAntwortHinzufügen(k.ObjektNummer, text.Trim(), _autor);
            KommentareNeuLaden(k.Kennung);
            AktuelleSeiteNeu?.Invoke();
            Status = "Antwort hinzugefügt.";
        }
        catch (Exception ex)
        {
            Fehler("Die Antwort konnte nicht gespeichert werden.", ex);
        }
    }

    /// <summary>Wählt einen Kommentar aus und blättert zu seiner Seite.</summary>
    public void KommentarAuswählen(Kommentar? k)
    {
        if (!SetzeWert(ref _ausgewählterKommentar, k, nameof(AusgewählterKommentar)))
            return;
        if (k is not null && k.SeitenIndex != AktuelleSeite
            && k.SeitenIndex >= 0 && k.SeitenIndex < Seiten.Count)
            AktuelleSeite = k.SeitenIndex; // löst Neuzeichnen aus
        else
            AktuelleSeiteNeu?.Invoke();
    }

    /// <summary>Liest das Inhaltsverzeichnis (Outlines) frisch aus dem Dokument.</summary>
    private void InhaltsverzeichnisNeuLaden()
    {
        if (_dienst.AktuelleBytes is null)
        {
            Inhaltsverzeichnis = new List<Models.InhaltsEintrag>();
        }
        else
        {
            var tocDienst = new Services.InhaltsverzeichnisDienst();
            Inhaltsverzeichnis = tocDienst.Extrahieren(_dienst.AktuelleBytes);
        }
        Melde(nameof(HatInhaltsverzeichnis));
        Melde(nameof(Inhaltsverzeichnis));
        // Standard: TOC anzeigen wenn vorhanden, sonst Seitenvorschauen.
        ZeigeInhaltsverzeichnis = HatInhaltsverzeichnis;
    }

    /// <summary>Liest die Kommentare frisch aus dem Dokument und stellt die Auswahl wieder her.</summary>
    private void KommentareNeuLaden(string? auswahlKennung = null)
    {
        Kommentare.Clear();
        foreach (var k in _dienst.KommentareLesen())
            Kommentare.Add(k);

        _ausgewählterKommentar = auswahlKennung is null
            ? null
            : Kommentare.FirstOrDefault(k => k.Kennung == auswahlKennung);
        Melde(nameof(AusgewählterKommentar));
    }
}
