using System.Collections.ObjectModel;
using System.IO;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>
/// ViewModel für das Zusammenführungsfenster. Verwaltet die linke (aktuelle)
/// und rechte (neue) Seitenliste und bietet Operationen zum Zusammenführen an.
/// </summary>
public class ZusammenführViewModel : BeobachtbaresObjekt
{
    private readonly ZusammenführDienst _dienst = new();

    private byte[] _linkeBytes;
    private byte[] _rechteBytes;

    private string _rechteDateiname;

    // ----- Öffentliche Eigenschaften ----------------------------------------

    /// <summary>Dateiname (ohne Pfad) des linken (aktuellen) Dokuments.</summary>
    public string LinkeDateiname { get; }

    /// <summary>Dateiname (ohne Pfad) des rechten (zu importierenden) Dokuments.</summary>
    public string RechteDateiname
    {
        get => _rechteDateiname;
        private set => SetzeWert(ref _rechteDateiname, value);
    }

    /// <summary>Seitenliste des linken Dokuments.</summary>
    public ObservableCollection<ZusammenführSeite> LinkeSeiten { get; } = new();

    /// <summary>Seitenliste des rechten Dokuments.</summary>
    public ObservableCollection<ZusammenführSeite> RechteSeiten { get; } = new();

    /// <summary>
    /// Das zusammengeführte Ergebnis als Byte-Array. Wird gesetzt, wenn der
    /// Benutzer „Zusammenführen" klickt. <c>null</c> solange noch nicht bestätigt.
    /// </summary>
    public byte[]? Ergebnis { get; private set; }

    // ----- Konstruktor ------------------------------------------------------

    public ZusammenführViewModel(byte[] aktuelleBytes, string dateiname)
    {
        _linkeBytes = aktuelleBytes;
        _rechteBytes = Array.Empty<byte>();
        LinkeDateiname = Path.GetFileName(dateiname);
        _rechteDateiname = "(noch kein Dokument geladen)";

        LinkeVorschauenLaden();
    }

    // ----- Öffentliche Methoden ---------------------------------------------

    /// <summary>Lädt ein zweites PDF-Dokument aus dem angegebenen Pfad.</summary>
    public void ZweitesDokumentLaden(string pfad)
    {
        try
        {
            _rechteBytes = File.ReadAllBytes(pfad);
            RechteDateiname = Path.GetFileName(pfad);
            RechteVorschauenLaden();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Die Datei konnte nicht geladen werden.\n\nDetails: {ex.Message}",
                "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Hängt alle Seiten des rechten Dokuments ans Ende des linken an.
    /// Die rechte Liste bleibt unverändert (Kopier-Semantik).
    /// </summary>
    public void GanzesDokumentAnhängen()
    {
        if (_rechteBytes.Length == 0)
            return;
        _linkeBytes = _dienst.GanzesDokumentAnhängen(_linkeBytes, _rechteBytes);
        LinkeVorschauenLaden();
        // Rechte Liste und rechte Bytes bleiben erhalten (Quelle wird nicht verändert).
    }

    /// <summary>
    /// Kopiert die angegebenen Seiten aus der rechten Liste ins linke Dokument.
    /// <paramref name="quellListenIndizes"/> sind die Listenindizes in RechteSeiten
    /// (werden nach PDF-Seitenindex aufsteigend sortiert).
    /// <paramref name="zielIndex"/> ist der Einfügepunkt in LinkeSeiten (0-basiert).
    /// Die rechte Liste bleibt unverändert.
    /// </summary>
    public void SeitenVonRechtsKopieren(IReadOnlyList<int> quellListenIndizes, int zielIndex)
    {
        if (quellListenIndizes.Count == 0 || _rechteBytes.Length == 0)
            return;

        // Quellindizes in PDF-Seitenindizes übersetzen und aufsteigend sortieren.
        var quellPdfIndizes = quellListenIndizes
            .Where(i => i >= 0 && i < RechteSeiten.Count)
            .Select(i => RechteSeiten[i].SeitenIndex)
            .OrderBy(i => i)
            .ToList();

        if (quellPdfIndizes.Count == 0)
            return;

        int einfügePunkt = Math.Clamp(zielIndex, 0, LinkeSeiten.Count);
        _linkeBytes = _dienst.SeitenKopieren(_linkeBytes, einfügePunkt, _rechteBytes, quellPdfIndizes);

        // Nur linke Vorschauen neu laden; rechte bleiben unangetastet.
        LinkeVorschauenLaden();
    }

    /// <summary>Verschiebt eine Seite innerhalb der linken Liste per Drag &amp; Drop.</summary>
    public void LinkeSeiteUmsortieren(int altListenIndex, int neuListenIndex)
    {
        if (altListenIndex == neuListenIndex) return;
        if (altListenIndex < 0 || altListenIndex >= LinkeSeiten.Count) return;
        neuListenIndex = Math.Clamp(neuListenIndex, 0, LinkeSeiten.Count - 1);
        _linkeBytes = _dienst.SeiteUmsortieren(_linkeBytes, altListenIndex, neuListenIndex);
        LinkeVorschauenLaden();
    }

    /// <summary>Löscht die Seite am angegebenen Listenindex aus dem linken Dokument.</summary>
    public void LinkeSeiteLöschen(int listenIndex)
    {
        if (LinkeSeiten.Count <= 1)
        {
            System.Windows.MessageBox.Show(
                "Das Dokument muss mindestens eine Seite behalten.",
                "Hinweis", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }
        if (listenIndex < 0 || listenIndex >= LinkeSeiten.Count) return;

        using var ms = new System.IO.MemoryStream(_linkeBytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(ms, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var neu = new PdfSharp.Pdf.PdfDocument();
        for (int i = 0; i < doc.PageCount; i++)
            if (i != listenIndex)
                neu.Pages.Add(doc.Pages[i]);
        using var ziel = new System.IO.MemoryStream();
        neu.Save(ziel, false);
        _linkeBytes = ziel.ToArray();

        LinkeVorschauenLaden();
    }

    /// <summary>Bestätigt das Zusammenführen: setzt <see cref="Ergebnis"/> auf die aktuellen linken Bytes.</summary>
    public void Zusammenführen()
    {
        Ergebnis = _linkeBytes;
    }

    // ----- Private Hilfsmethoden --------------------------------------------

    private void LinkeVorschauenLaden()
    {
        LinkeSeiten.Clear();
        if (_linkeBytes.Length == 0)
            return;

        int anzahl = SeitenAnzahl(_linkeBytes);
        for (int i = 0; i < anzahl; i++)
        {
            var seite = new ZusammenführSeite { SeitenIndex = i };
            LinkeSeiten.Add(seite);
            int idx = i;
            byte[] kopie = _linkeBytes;
            Task.Run(() =>
            {
                try
                {
                    var bild = PdfRenderDienst.MiniaturRendern(kopie, idx, 120);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        // Prüfen ob die Seite noch in der Liste ist (kein Race).
                        if (idx < LinkeSeiten.Count && LinkeSeiten[idx].SeitenIndex == idx)
                            LinkeSeiten[idx].Vorschau = bild;
                    });
                }
                catch { /* Vorschau bleibt leer */ }
            });
        }
    }

    private void RechteVorschauenLaden()
    {
        RechteSeiten.Clear();
        if (_rechteBytes.Length == 0)
            return;

        int anzahl = SeitenAnzahl(_rechteBytes);
        for (int i = 0; i < anzahl; i++)
        {
            var seite = new ZusammenführSeite { SeitenIndex = i };
            RechteSeiten.Add(seite);
            int idx = i;
            byte[] kopie = _rechteBytes;
            Task.Run(() =>
            {
                try
                {
                    var bild = PdfRenderDienst.MiniaturRendern(kopie, idx, 120);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (idx < RechteSeiten.Count && RechteSeiten[idx].SeitenIndex == idx)
                            RechteSeiten[idx].Vorschau = bild;
                    });
                }
                catch { /* Vorschau bleibt leer */ }
            });
        }
    }

    private static int SeitenAnzahl(byte[] bytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            using var doc = PdfSharp.Pdf.IO.PdfReader.Open(ms, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch
        {
            return 0;
        }
    }
}
