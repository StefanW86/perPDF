using System.Collections.ObjectModel;
using System.Text;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>
/// ViewModel für das Seitenauswahl-Fenster (Extrahieren/Entfernen): hält die
/// Miniaturliste des aktuellen Dokuments und übersetzt zwischen einer
/// Seitenbereich-Eingabe (z. B. „3-10“ oder „1,3,5-8“) und Seitenindizes.
/// </summary>
public class SeitenAuswahlViewModel
{
    private readonly byte[] _bytes;

    /// <summary>Seitenliste des Dokuments (mit asynchron nachgeladenen Vorschauen).</summary>
    public ObservableCollection<ZusammenführSeite> Seiten { get; } = new();

    public SeitenAuswahlViewModel(byte[] bytes)
    {
        _bytes = bytes;
        VorschauenLaden();
    }

    /// <summary>Erstellt ein neues PDF nur aus den angegebenen Seiten (0-basiert).</summary>
    public byte[] Extrahieren(IReadOnlyList<int> indizes)
        => new ZusammenführDienst().SeitenExtrahieren(_bytes, indizes);

    // ----- Seitenbereich-Eingabe --------------------------------------------

    /// <summary>
    /// Parst eine Bereichseingabe wie „3-10“ oder „1, 3, 5-8“ in 0-basierte
    /// Seitenindizes. Unvollständige oder ungültige Teile (z. B. „3-“ während des
    /// Tippens) werden ignoriert; Seitenzahlen außerhalb des Dokuments ebenfalls.
    /// </summary>
    public static List<int> BereichParsen(string? text, int seitenAnzahl)
    {
        var indizes = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(text) || seitenAnzahl <= 0)
            return new List<int>();

        foreach (string teil in text.Split(',', ';'))
        {
            string t = teil.Trim();
            if (t.Length == 0)
                continue;

            int strich = t.IndexOf('-');
            if (strich < 0)
            {
                if (int.TryParse(t, out int nr) && nr >= 1 && nr <= seitenAnzahl)
                    indizes.Add(nr - 1);
                continue;
            }

            if (!int.TryParse(t[..strich].Trim(), out int von)
                || !int.TryParse(t[(strich + 1)..].Trim(), out int bis))
                continue;
            if (von > bis)
                (von, bis) = (bis, von);
            von = Math.Max(1, von);
            bis = Math.Min(seitenAnzahl, bis);
            for (int nr = von; nr <= bis; nr++)
                indizes.Add(nr - 1);
        }
        return indizes.ToList();
    }

    /// <summary>
    /// Formatiert 0-basierte Seitenindizes als kompakte Bereichsangabe,
    /// z. B. [0,1,2,4] → „1-3, 5“.
    /// </summary>
    public static string BereichFormatieren(IEnumerable<int> indizes)
    {
        var sortiert = indizes.Distinct().OrderBy(i => i).ToList();
        if (sortiert.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        int start = sortiert[0], ende = sortiert[0];
        for (int i = 1; i <= sortiert.Count; i++)
        {
            if (i < sortiert.Count && sortiert[i] == ende + 1)
            {
                ende = sortiert[i];
                continue;
            }
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(ende == start ? $"{start + 1}" : $"{start + 1}-{ende + 1}");
            if (i < sortiert.Count)
                start = ende = sortiert[i];
        }
        return sb.ToString();
    }

    // ----- Vorschauen ---------------------------------------------------------

    /// <summary>Füllt die Seitenliste und lädt die Miniaturen asynchron nach (vgl. Zusammenführen).</summary>
    private void VorschauenLaden()
    {
        int anzahl = SeitenAnzahl(_bytes);
        for (int i = 0; i < anzahl; i++)
        {
            var seite = new ZusammenführSeite { SeitenIndex = i };
            Seiten.Add(seite);
            int idx = i;
            Task.Run(() =>
            {
                try
                {
                    var bild = PdfRenderDienst.MiniaturRendern(_bytes, idx, 120);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (idx < Seiten.Count)
                            Seiten[idx].Vorschau = bild;
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
