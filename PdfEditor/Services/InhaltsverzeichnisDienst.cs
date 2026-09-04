using System.IO;
using PdfEditor.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Services;

/// <summary>
/// Extrahiert das Inhaltsverzeichnis (Outlines / Lesezeichen) aus einem PDF-Dokument.
/// </summary>
internal class InhaltsverzeichnisDienst
{
    /// <summary>
    /// Liest alle Gliederungseinträge aus dem Dokument und gibt sie als
    /// flache Liste mit Tiefenangabe zurück.
    /// </summary>
    public List<InhaltsEintrag> Extrahieren(byte[] pdfBytes)
    {
        var ergebnis = new List<InhaltsEintrag>();
        try
        {
            using var stream = new MemoryStream(pdfBytes);
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            if (doc.Outlines == null || doc.Outlines.Count == 0)
                return ergebnis;

            DurchlaufeOutlines(doc.Outlines, ergebnis, 0, doc);
        }
        catch
        {
            // Fehler beim Lesen der Outlines sind nicht kritisch – leere Liste zurückgeben.
        }
        return ergebnis;
    }

    private static void DurchlaufeOutlines(
        PdfOutlineCollection outlines,
        List<InhaltsEintrag> ziel,
        int tiefe,
        PdfDocument doc)
    {
        foreach (PdfOutline outline in outlines)
        {
            int seite = -1;
            if (outline.DestinationPage != null)
            {
                // Seitenindex durch Vergleich mit allen Seiten ermitteln.
                for (int i = 0; i < doc.PageCount; i++)
                {
                    if (ReferenceEquals(doc.Pages[i], outline.DestinationPage))
                    {
                        seite = i;
                        break;
                    }
                }
            }

            var eintrag = new InhaltsEintrag
            {
                Titel = string.IsNullOrEmpty(outline.Title) ? "(Ohne Titel)" : outline.Title,
                SeitenIndex = seite,
                Tiefe = tiefe,
                IstAufgeklappt = true
            };
            ziel.Add(eintrag);

            if (outline.Outlines != null && outline.Outlines.Count > 0)
                DurchlaufeOutlines(outline.Outlines, eintrag.Kinder, tiefe + 1, doc);
        }
    }
}
