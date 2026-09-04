using System.Runtime.InteropServices;

namespace PdfEditor.Services;

/// <summary>
/// P/Invoke-Schicht für den Vektordruck: PDFium (GDI-Render­pfad <c>FPDF_RenderPage</c>)
/// und die GDI-Druck-API (<c>winspool</c>/<c>gdi32</c>). PDFium wird nicht selbst
/// initialisiert – beim Drucken ist bereits ein Dokument geöffnet und gerendert worden,
/// sodass PDFtoImage die Bibliothek geladen hat. Alle Aufrufe laufen serialisiert auf dem
/// UI-Thread (PDFium ist nicht threadsicher).
/// </summary>
internal static class DruckInterop
{
    // ----- PDFium ----------------------------------------------------------

    /// <summary>Rendert Annotationen mit (Widget-/Stempel- usw. Erscheinungsbilder).</summary>
    public const int FPDF_ANNOT = 0x01;
    /// <summary>Für den Druck optimiert rendern (Druck-Erscheinungsbilder statt Bildschirm).</summary>
    public const int FPDF_PRINTING = 0x800;

    [DllImport("pdfium.dll")]
    public static extern IntPtr FPDF_LoadMemDocument(IntPtr datenZeiger, int größe,
        [MarshalAs(UnmanagedType.LPStr)] string? passwort);

    [DllImport("pdfium.dll")]
    public static extern void FPDF_CloseDocument(IntPtr dokument);

    [DllImport("pdfium.dll")]
    public static extern int FPDF_GetPageCount(IntPtr dokument);

    [DllImport("pdfium.dll")]
    public static extern IntPtr FPDF_LoadPage(IntPtr dokument, int seitenIndex);

    [DllImport("pdfium.dll")]
    public static extern void FPDF_ClosePage(IntPtr seite);

    [DllImport("pdfium.dll")]
    public static extern double FPDF_GetPageWidth(IntPtr seite);

    [DllImport("pdfium.dll")]
    public static extern double FPDF_GetPageHeight(IntPtr seite);

    /// <summary>Zeichnet die Seite vektoriell in einen GDI-Gerätekontext (z. B. Drucker-HDC).</summary>
    [DllImport("pdfium.dll")]
    public static extern void FPDF_RenderPage(IntPtr hdc, IntPtr seite,
        int startX, int startY, int breite, int höhe, int drehung, int flags);

    // ----- GDI / Druck -----------------------------------------------------

    // GetDeviceCaps-Indizes
    public const int HORZRES = 8;     // druckbare Breite in Pixeln
    public const int VERTRES = 10;    // druckbare Höhe in Pixeln
    public const int LOGPIXELSX = 88; // DPI horizontal
    public const int LOGPIXELSY = 90; // DPI vertikal

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDC(string? treiber, string gerät, string? ausgabe, IntPtr devMode);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int StartDoc(IntPtr hdc, ref DOCINFO di);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int StartPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int EndDoc(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int AbortDoc(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOCINFO
    {
        public int cbSize;
        public string lpszDocName;
        public string? lpszOutput;
        public string? lpszDatatype;
        public int fwType;
    }
}
