using System.Windows;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>Texterkennung (OCR) des ganzen Dokuments über die Windows-OCR, nur auf Knopfdruck.</summary>
public partial class HauptViewModel
{
    // Ab so vielen Zeichen echten Texts gilt eine Seite als „hat Text“ (ein Stempel o. Ä. zählt nicht).
    private const int TextSchwelle = 20;

    private RelayCommand? _texterkennungBefehl;
    public RelayCommand TexterkennungBefehl => _texterkennungBefehl ??=
        new RelayCommand(Texterkennung, () => _dienst.HatDokument && _ocrAbbruch is null);

    private CancellationTokenSource? _ocrAbbruch;

    /// <summary>True, solange die Texterkennung läuft (zeigt den Abbrechen-Knopf im Lade-Overlay).</summary>
    public bool TexterkennungLäuft => _ocrAbbruch is not null;

    /// <summary>Bricht die laufende Texterkennung nach der aktuellen Seite ab.</summary>
    public void TexterkennungAbbrechen() => _ocrAbbruch?.Cancel();

    private async void Texterkennung()
    {
        var bytes = _dienst.AktuelleBytes;
        if (bytes is null)
            return;
        var erkennung = OcrDienst.ErkennungErstellen();
        if (erkennung is null)
        {
            Windows.MeldungFenster.Zeigen("Texterkennung nicht verfügbar",
                "Für keine der Windows-Sprachen ist eine Texterkennung installiert.\n\n" +
                "Sie lässt sich in Windows unter Einstellungen → Zeit und Sprache → Sprache " +
                "nachinstallieren (Sprache hinzufügen bzw. Sprachoptionen).",
                MessageBoxImage.Information);
            return;
        }

        _ocrAbbruch = new CancellationTokenSource();
        var abbruch = _ocrAbbruch.Token;
        TexterkennungBefehl.AusführbarkeitAktualisieren();
        var infos = _seitenInfos.ToList();
        var ergebnis = new Dictionary<int, List<OcrWort>>();
        int mitText = 0;
        try
        {
            LadestatusGeändert?.Invoke(true, "Texterkennung wird vorbereitet...");
            var zeichen = await Task.Run(() => _textDienst.ZeichenJeSeite(bytes, infos.Count));
            for (int i = 0; i < infos.Count && !abbruch.IsCancellationRequested; i++)
            {
                if (zeichen[i] >= TextSchwelle)
                {
                    mitText++;
                    continue;
                }
                LadestatusGeändert?.Invoke(true, $"Texterkennung – Seite {i + 1} von {infos.Count}");
                ergebnis[i] = await OcrDienst.SeiteErkennen(erkennung, bytes, i, infos[i]);
            }
            if (abbruch.IsCancellationRequested)
            {
                Status = "Texterkennung abgebrochen – das Dokument ist unverändert.";
                return;
            }

            int erkannt = ergebnis.Count(e => e.Value.Count > 0);
            if (erkannt > 0)
            {
                _dienst.TextebeneEinfügen(ergebnis);
                NachStrukturänderung(AktuelleSeite);
            }
            string meldung = $"{erkannt} Seite(n) mit erkanntem Text."
                + (mitText > 0 ? $"\n{mitText} Seite(n) enthielten bereits Text und wurden übersprungen." : "")
                + (ergebnis.Count - erkannt > 0 ? $"\nAuf {ergebnis.Count - erkannt} Seite(n) wurde kein Text gefunden." : "");
            Status = $"Texterkennung abgeschlossen: {erkannt} Seite(n) erkannt.";
            Windows.MeldungFenster.Zeigen("Texterkennung abgeschlossen", meldung, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Fehler("Die Texterkennung ist fehlgeschlagen.", ex);
        }
        finally
        {
            _ocrAbbruch = null;
            LadestatusGeändert?.Invoke(false, "");
            TexterkennungBefehl.AusführbarkeitAktualisieren();
        }
    }
}
