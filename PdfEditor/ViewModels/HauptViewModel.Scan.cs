namespace PdfEditor.ViewModels;

/// <summary>Übernahme gescannter Seiten: an das Dokument anhängen oder als neues Dokument laden.</summary>
public partial class HauptViewModel
{
    /// <summary>True, solange ein Dokument geladen wird (Scan-Übernahme dann gesperrt).</summary>
    public bool LädtGerade => _istLaden;

    /// <summary>
    /// Hängt die gescannten Seiten am Ende an. Ausstehende Overlays bleiben unverändert,
    /// da sich kein vorhandener Seitenindex verschiebt.
    /// </summary>
    public void ScanAnhängen(byte[] pdfBytes)
    {
        int vorher = Seiten.Count;
        try
        {
            _dienst.SeitenAnhängen(pdfBytes);
            FelderNeuLaden();
            NachStrukturänderung(vorher); // springt zur ersten gescannten Seite
            IstGeändert = true;
            Status = $"{Seiten.Count - vorher} Seite(n) gescannt und angehängt.";
        }
        catch (Exception ex)
        {
            Fehler("Die gescannten Seiten konnten nicht angehängt werden.", ex);
        }
    }

    /// <summary>Lädt den Scan als neues, noch ungespeichertes Dokument (ohne Pfad).</summary>
    public async void ScanAlsNeuesDokument(byte[] pdfBytes)
    {
        _istLaden = true;
        LadestatusGeändert?.Invoke(true, "Scan wird übernommen...");
        try
        {
            _dienst.ÖffnenAusBytes(pdfBytes, null);
            AusstehendeZurücksetzen();
            FelderNeuLaden();
            await NachStrukturänderungAsync(0);
            Status = $"{Seiten.Count} Seite(n) gescannt.";
            Melde(nameof(Titel));
        }
        catch (Exception ex)
        {
            Fehler("Der Scan konnte nicht übernommen werden.", ex);
        }
        finally
        {
            LadestatusGeändert?.Invoke(false, "");
            _istLaden = false;
        }
        IstGeändert = true; // ungespeichert → Rückfrage beim Schließen
    }
}
