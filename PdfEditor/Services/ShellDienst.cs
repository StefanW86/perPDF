namespace PdfEditor.Services;

/// <summary>Hilfsfunktionen rund um die Windows-Shell.</summary>
internal static class ShellDienst
{
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    /// <summary>
    /// Teilt der Windows-Shell mit, dass sich Dateizuordnungen geändert haben, damit
    /// der Explorer die „Öffnen mit"-Liste und Symbole sofort neu einliest. Wird vom
    /// Setup über den Schalter <c>--dateizuordnung-aktualisieren</c> aufgerufen,
    /// nachdem dieses die Registry-Einträge geschrieben hat.
    /// </summary>
    public static void ShellBenachrichtigen()
    {
        // SHCNE_ASSOCCHANGED = 0x8000000, SHCNF_IDLIST = 0x0000
        SHChangeNotify(0x8000000, 0x0000, nint.Zero, nint.Zero);
    }
}
