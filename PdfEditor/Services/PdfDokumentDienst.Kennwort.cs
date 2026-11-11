using System.IO;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Services;

/// <summary>
/// Kennwortschutz: Geschützte Dateien werden beim Öffnen einmal entschlüsselt –
/// <see cref="AktuelleBytes"/> sind immer unverschlüsselt, damit alle übrigen Operationen
/// unverändert funktionieren. Erst beim Schreiben der Datei wird wieder verschlüsselt.
/// </summary>
public partial class PdfDokumentDienst
{
    /// <summary>Kennwort, mit dem die Datei gespeichert wird; <c>null</c> = ungeschützt.</summary>
    public string? Kennwort { get; set; }

    /// <summary>Prüft, ob die PDF-Bytes zum Öffnen ein Kennwort verlangen.</summary>
    public static bool IstVerschlüsselt(byte[] bytes)
    {
        bool verlangt = false;
        try
        {
            using var _ = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify,
                a => { verlangt = true; a.Abort = true; });
        }
        catch (Exception) when (verlangt) { }
        return verlangt;
    }

    /// <summary>
    /// Entschlüsselt die PDF-Bytes mit dem Kennwort. Bei falschem Kennwort wirft
    /// PDFsharp eine <see cref="PdfReaderException"/>.
    /// </summary>
    public static byte[] Entschlüsseln(byte[] bytes, string kennwort)
    {
        using var d = PdfReader.Open(new MemoryStream(bytes), kennwort, PdfDocumentOpenMode.Modify);
        d.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
        return NachBytes(d);
    }

    /// <summary>Verschlüsselt die PDF-Bytes (AES-256) mit dem Kennwort zum Öffnen.</summary>
    private static byte[] Verschlüsseln(byte[] bytes, string kennwort)
    {
        using var d = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        d.SecuritySettings.UserPassword = kennwort;
        d.SecuritySettings.OwnerPassword = kennwort;
        d.SecurityHandler.SetEncryptionToV5();
        return NachBytes(d);
    }
}
