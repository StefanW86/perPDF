using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Services;

/// <summary>Führt zwei PDF-Byte-Puffer durch Seitenoperationen zusammen.</summary>
internal class ZusammenführDienst
{
    /// <summary>Fügt alle Seiten von <paramref name="quellBytes"/> ans Ende von <paramref name="zielBytes"/> an.</summary>
    public byte[] GanzesDokumentAnhängen(byte[] zielBytes, byte[] quellBytes)
    {
        using var ziel = PdfReader.Open(new MemoryStream(zielBytes), PdfDocumentOpenMode.Import);
        using var quelle = PdfReader.Open(new MemoryStream(quellBytes), PdfDocumentOpenMode.Import);
        var neu = new PdfDocument();
        foreach (var seite in ziel.Pages)
            neu.Pages.Add(seite);
        foreach (var seite in quelle.Pages)
            neu.Pages.Add(seite);
        return NachBytes(neu);
    }

    /// <summary>
    /// Kopiert die angegebenen Seiten aus <paramref name="quellBytes"/> und fügt sie
    /// zusammenhängend an Position <paramref name="zielIndex"/> in <paramref name="zielBytes"/> ein.
    /// Die Quell-Bytes bleiben unverändert. Die Quellseiten werden in der Reihenfolge
    /// ihres Auftretens in <paramref name="quellIndizes"/> (aufsteigend sortiert) eingefügt.
    /// </summary>
    /// <param name="zielBytes">Ziel-Dokument (wird nicht verändert).</param>
    /// <param name="zielIndex">0-basierter Einfügepunkt im Zieldokument.</param>
    /// <param name="quellBytes">Quell-Dokument (bleibt unverändert).</param>
    /// <param name="quellIndizes">0-basierte Seitenindizes im Quelldokument (werden aufsteigend sortiert).</param>
    public byte[] SeitenKopieren(byte[] zielBytes, int zielIndex, byte[] quellBytes, IReadOnlyList<int> quellIndizes)
    {
        if (quellIndizes.Count == 0)
            return zielBytes;

        using var ziel = PdfReader.Open(new MemoryStream(zielBytes), PdfDocumentOpenMode.Import);
        using var quelle = PdfReader.Open(new MemoryStream(quellBytes), PdfDocumentOpenMode.Import);

        var sortiertIndizes = quellIndizes.OrderBy(i => i).ToList();
        int einfügePunkt = Math.Clamp(zielIndex, 0, ziel.PageCount);

        var neu = new PdfDocument();
        for (int i = 0; i < einfügePunkt; i++)
            neu.Pages.Add(ziel.Pages[i]);
        foreach (int qi in sortiertIndizes)
            if (qi >= 0 && qi < quelle.PageCount)
                neu.Pages.Add(quelle.Pages[qi]);
        for (int i = einfügePunkt; i < ziel.PageCount; i++)
            neu.Pages.Add(ziel.Pages[i]);

        return NachBytes(neu);
    }

    /// <summary>
    /// Erstellt ein neues PDF nur aus den angegebenen Seiten des Quelldokuments
    /// (0-basierte Indizes, werden aufsteigend sortiert). Die Quelle bleibt unverändert.
    /// </summary>
    public byte[] SeitenExtrahieren(byte[] quellBytes, IReadOnlyList<int> quellIndizes)
    {
        using var quelle = PdfReader.Open(new MemoryStream(quellBytes), PdfDocumentOpenMode.Import);
        var neu = new PdfDocument();
        foreach (int qi in quellIndizes.Distinct().OrderBy(i => i))
            if (qi >= 0 && qi < quelle.PageCount)
                neu.Pages.Add(quelle.Pages[qi]);
        if (neu.PageCount == 0)
            throw new InvalidOperationException("Keine gültigen Seiten zum Extrahieren angegeben.");
        return NachBytes(neu);
    }

    /// <summary>
    /// Verschiebt Seite <paramref name="altIndex"/> auf Position <paramref name="neuIndex"/>
    /// innerhalb desselben Dokuments.
    /// </summary>
    public byte[] SeiteUmsortieren(byte[] bytes, int altIndex, int neuIndex)
    {
        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        if (altIndex == neuIndex || altIndex < 0 || neuIndex < 0
            || altIndex >= doc.PageCount || neuIndex >= doc.PageCount)
            return bytes;

        var reihenfolge = Enumerable.Range(0, doc.PageCount).ToList();
        reihenfolge.RemoveAt(altIndex);
        reihenfolge.Insert(Math.Clamp(neuIndex, 0, reihenfolge.Count), altIndex);

        var neu = new PdfDocument();
        foreach (int i in reihenfolge)
            neu.Pages.Add(doc.Pages[i]);
        return NachBytes(neu);
    }

    private static byte[] NachBytes(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }
}
