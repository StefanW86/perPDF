using System.Printing;
using System.Printing.Interop;
using System.Runtime.InteropServices;
using System.Windows.Controls;

namespace PdfEditor.Services;

/// <summary>
/// Druckt ein PDF (als Bytes) über den Standard-Windows-Druckdialog.
/// Die Seiten werden mit PDFium vektoriell direkt in den GDI-Druckerkontext gezeichnet
/// (<see cref="DruckInterop.FPDF_RenderPage"/>), sodass Text und Linien als Vektoren
/// gedruckt werden statt als gerasterte Bilder. Die Auswahl des Druckers, der Kopien,
/// des Seitenbereichs und der Ausrichtung erfolgt im nativen <see cref="PrintDialog"/>;
/// diese Einstellungen werden via <see cref="PrintTicketConverter"/> in ein DEVMODE
/// übersetzt und an <c>CreateDC</c> übergeben.
/// </summary>
/// <remarks>
/// Hinweis zu Formularfeldern: Gezeichnet werden Felder mit eigenem Erscheinungsbild
/// (/AP). Werte, die nur über <c>/NeedAppearances</c> erzeugt werden, hängen wie beim
/// Speichern vom Erscheinungsbild ab – der Druck verhält sich also wie die gespeicherte Datei.
/// </remarks>
public static class DruckDienst
{
    /// <summary>
    /// Zeigt den Druckdialog und druckt die ausgewählten Seiten. Liefert <c>false</c>,
    /// wenn der Benutzer abbricht.
    /// </summary>
    public static bool Drucken(byte[] pdfBytes, int seitenAnzahl, string dokumentName)
    {
        var dlg = new PrintDialog
        {
            UserPageRangeEnabled = true,
            MinPage = 1,
            MaxPage = (uint)Math.Max(1, seitenAnzahl)
        };
        if (dlg.ShowDialog() != true)
            return false;

        byte[] devMode = DevModeErzeugen(dlg);
        var seiten = SeitenIndizes(dlg, seitenAnzahl);

        SeitenDrucken(pdfBytes, seiten, devMode, dlg.PrintQueue.FullName, dokumentName);
        return true;
    }

    /// <summary>Übersetzt die Dialogeinstellungen (Drucker, Papier, Ausrichtung, Kopien) in ein DEVMODE.</summary>
    private static byte[] DevModeErzeugen(PrintDialog dlg)
    {
        var konverter = new PrintTicketConverter(dlg.PrintQueue.FullName,
            PrintTicketConverter.MaxPrintSchemaVersion);
        return konverter.ConvertPrintTicketToDevMode(dlg.PrintTicket, BaseDevModeType.UserDefault);
    }

    /// <summary>Liefert die zu druckenden 0-basierten Seitenindizes gemäß Dialogauswahl.</summary>
    private static List<int> SeitenIndizes(PrintDialog dlg, int seitenAnzahl)
    {
        var liste = new List<int>();
        if (dlg.PageRangeSelection == PageRangeSelection.UserPages)
        {
            int von = Math.Max(1, dlg.PageRange.PageFrom);
            int bis = Math.Min(seitenAnzahl, dlg.PageRange.PageTo);
            for (int s = von; s <= bis; s++)
                liste.Add(s - 1);
        }
        else
        {
            for (int s = 0; s < seitenAnzahl; s++)
                liste.Add(s);
        }
        return liste;
    }

    private static void SeitenDrucken(byte[] pdfBytes, IReadOnlyList<int> seiten,
        byte[] devMode, string druckerName, string dokumentName)
    {
        var bytesHandle = GCHandle.Alloc(pdfBytes, GCHandleType.Pinned);
        var devModeHandle = GCHandle.Alloc(devMode, GCHandleType.Pinned);
        IntPtr dokument = IntPtr.Zero;
        IntPtr hdc = IntPtr.Zero;
        bool gestartet = false;
        try
        {
            dokument = DruckInterop.FPDF_LoadMemDocument(
                bytesHandle.AddrOfPinnedObject(), pdfBytes.Length, null);
            if (dokument == IntPtr.Zero)
                throw new InvalidOperationException("Das Dokument konnte für den Druck nicht geladen werden.");

            hdc = DruckInterop.CreateDC("WINSPOOL", druckerName, null, devModeHandle.AddrOfPinnedObject());
            if (hdc == IntPtr.Zero)
                throw new InvalidOperationException("Der Druckerkontext konnte nicht erstellt werden.");

            int dpiX = DruckInterop.GetDeviceCaps(hdc, DruckInterop.LOGPIXELSX);
            int dpiY = DruckInterop.GetDeviceCaps(hdc, DruckInterop.LOGPIXELSY);
            int flächeBreite = DruckInterop.GetDeviceCaps(hdc, DruckInterop.HORZRES);
            int flächeHöhe = DruckInterop.GetDeviceCaps(hdc, DruckInterop.VERTRES);
            int seitenImDok = DruckInterop.FPDF_GetPageCount(dokument);

            var info = new DruckInterop.DOCINFO
            {
                cbSize = Marshal.SizeOf<DruckInterop.DOCINFO>(),
                lpszDocName = dokumentName
            };
            if (DruckInterop.StartDoc(hdc, ref info) <= 0)
                throw new InvalidOperationException("Der Druckauftrag konnte nicht gestartet werden.");
            gestartet = true;

            foreach (int index in seiten)
            {
                if (index < 0 || index >= seitenImDok)
                    continue;
                SeiteAufDruckerZeichnen(hdc, dokument, index,
                    dpiX, dpiY, flächeBreite, flächeHöhe);
            }

            DruckInterop.EndDoc(hdc);
            gestartet = false;
        }
        finally
        {
            if (gestartet && hdc != IntPtr.Zero)
                DruckInterop.AbortDoc(hdc);
            if (dokument != IntPtr.Zero)
                DruckInterop.FPDF_CloseDocument(dokument);
            if (hdc != IntPtr.Zero)
                DruckInterop.DeleteDC(hdc);
            if (devModeHandle.IsAllocated)
                devModeHandle.Free();
            if (bytesHandle.IsAllocated)
                bytesHandle.Free();
        }
    }

    /// <summary>Zeichnet eine Seite zentriert und seitenverhältnistreu in den druckbaren Bereich.</summary>
    private static void SeiteAufDruckerZeichnen(IntPtr hdc, IntPtr dokument, int index,
        int dpiX, int dpiY, int flächeBreite, int flächeHöhe)
    {
        IntPtr seite = DruckInterop.FPDF_LoadPage(dokument, index);
        if (seite == IntPtr.Zero)
            return;
        try
        {
            double breitePunkte = DruckInterop.FPDF_GetPageWidth(seite);
            double höhePunkte = DruckInterop.FPDF_GetPageHeight(seite);
            if (breitePunkte <= 0 || höhePunkte <= 0)
                return;

            // Seitengröße in Druckerpixeln bei 100 %, dann auf den druckbaren Bereich einpassen.
            double pxBreite = breitePunkte / 72.0 * dpiX;
            double pxHöhe = höhePunkte / 72.0 * dpiY;
            double skala = Math.Min(flächeBreite / pxBreite, flächeHöhe / pxHöhe);

            int zielBreite = (int)Math.Round(pxBreite * skala);
            int zielHöhe = (int)Math.Round(pxHöhe * skala);
            int startX = (flächeBreite - zielBreite) / 2;
            int startY = (flächeHöhe - zielHöhe) / 2;

            DruckInterop.StartPage(hdc);
            DruckInterop.FPDF_RenderPage(hdc, seite, startX, startY, zielBreite, zielHöhe,
                0, DruckInterop.FPDF_ANNOT | DruckInterop.FPDF_PRINTING);
            DruckInterop.EndPage(hdc);
        }
        finally
        {
            DruckInterop.FPDF_ClosePage(seite);
        }
    }
}
