using System.Runtime.InteropServices;

namespace PdfEditor.Services;

public record ScannerInfo(string Id, string Name);

/// <summary>Scanfehler mit der Anzahl der vor dem Fehler bereits gelieferten Seiten.</summary>
public class ScanFehler(string meldung, int bisherigeSeiten) : Exception(meldung)
{
    public int BisherigeSeiten { get; } = bisherigeSeiten;
}

/// <summary>
/// Scannt über WIA 2.0 (<c>WIA.DeviceManager</c>, wiaaut.dll von Windows). Spät gebunden
/// über <c>dynamic</c>, damit keine Interop-DLL ausgeliefert werden muss. Alle WIA-Aufrufe
/// laufen auf einem eigenen STA-Thread, damit die Oberfläche bedienbar bleibt.
/// </summary>
public static class ScanDienst
{
    // WIA-Eigenschafts-IDs (wiadef.h)
    private const int DokumentFähigkeiten = 3086;   // WIA_DPS_DOCUMENT_HANDLING_CAPABILITIES
    private const int DokumentAuswahl = 3088;       // WIA_DPS_DOCUMENT_HANDLING_SELECT
    private const int Seiten = 3096;                // WIA_DPS_PAGES
    private const int Absicht = 6146;               // WIA_IPS_CUR_INTENT
    private const int DpiX = 6147, DpiY = 6148;
    private const int StartX = 6149, StartY = 6150;
    private const int BreitePx = 6151, HöhePx = 6152;
    private const int Helligkeit = 6154, Kontrast = 6155;

    private const int Einzug = 0x001, Flachbett = 0x002, Duplex = 0x004;
    private const string FormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

    private const int PapierLeer = unchecked((int)0x80210003);

    /// <summary>Alle in Windows eingerichteten WIA-Scanner; leer, wenn WIA fehlt.</summary>
    public static List<ScannerInfo> ScannerAuflisten() => AufSta(() =>
    {
        var liste = new List<ScannerInfo>();
        dynamic? manager = Manager();
        if (manager is null)
            return liste;
        foreach (dynamic info in manager.DeviceInfos)
        {
            // Nur Kameras/Video ausschließen: eSCL-Netzwerkscanner melden Typ 65535 statt 1.
            int typ = (int)info.Type;
            if (typ == 2 || typ == 3)
                continue;
            string name = Wert(info.Properties, 7) as string ?? (string)info.DeviceID; // WIA_DIP_DEV_NAME
            liste.Add(new ScannerInfo((string)info.DeviceID, name));
        }
        return liste;
    });

    /// <summary>Die vom Scanner unterstützten Quellen (mindestens Flachbett).</summary>
    public static List<ScanQuelle> QuellenErmitteln(string scannerId) => AufSta(() =>
    {
        var quellen = new List<ScanQuelle>();
        try
        {
            dynamic gerät = Verbinden(scannerId);
            object? wert = Wert(gerät.Properties, DokumentFähigkeiten);
            int fähig = wert is null ? Flachbett : Convert.ToInt32(wert);
            if ((fähig & Flachbett) != 0) quellen.Add(ScanQuelle.Flachbett);
            if ((fähig & Einzug) != 0) quellen.Add(ScanQuelle.Einzug);
            if ((fähig & Einzug) != 0 && (fähig & Duplex) != 0) quellen.Add(ScanQuelle.EinzugDuplex);
        }
        catch (COMException)
        {
            // Gerät nicht erreichbar → nur Flachbett anbieten; der Scan meldet den Fehler.
        }
        if (quellen.Count == 0)
            quellen.Add(ScanQuelle.Flachbett);
        return quellen;
    });

    /// <summary>
    /// Scannt eine Seite (Flachbett) bzw. alle Blätter im Einzug. <paramref name="onSeite"/>
    /// erhält jede fertige Seite als PNG (auf dem Scan-Thread), damit Seiten vor einem
    /// Fehler erhalten bleiben. Wirft <see cref="ScanFehler"/>.
    /// </summary>
    public static Task ScannenAsync(string scannerId, ScanZustand e, Action<byte[]> onSeite,
                                    CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            int anzahl = 0;
            try
            {
                dynamic gerät = Verbinden(scannerId);
                bool istEinzug = e.Quelle != ScanQuelle.Flachbett;
                int auswahl = e.Quelle switch
                {
                    ScanQuelle.Einzug => Einzug,
                    ScanQuelle.EinzugDuplex => Einzug | Duplex,
                    _ => Flachbett
                };
                Setzen(gerät.Properties, DokumentAuswahl, auswahl);
                if (istEinzug)
                    Setzen(gerät.Properties, Seiten, 1);

                dynamic? item = null;
                foreach (dynamic i in gerät.Items) { item = i; break; }
                if (item is null)
                    throw new ScanFehler("Der Scanner bietet keine Scanfläche an.", 0);
                ItemEinrichten(item, e);

                do
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] png;
                    try
                    {
                        dynamic bild = item.Transfer(FormatPng);
                        png = (byte[])bild.FileData.BinaryData;
                    }
                    catch (COMException ex) when (istEinzug && ex.HResult == PapierLeer && anzahl > 0)
                    {
                        break; // Einzug leer → normales Ende
                    }
                    onSeite(png);
                    anzahl++;
                } while (istEinzug);
                tcs.SetResult();
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(ct);
            }
            catch (ScanFehler ex)
            {
                tcs.SetException(ex);
            }
            catch (COMException ex)
            {
                tcs.SetException(new ScanFehler(Meldung(ex.HResult), anzahl));
            }
            catch (Exception ex)
            {
                tcs.SetException(new ScanFehler("Scanfehler: " + ex.Message, anzahl));
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static string Meldung(int hresult) => (uint)hresult switch
    {
        0x80210003 => "Kein Papier im Einzug.",
        0x80210002 => "Papierstau im Scanner.",
        0x80210016 => "Der Scannerdeckel ist offen.",
        0x80210005 => "Der Scanner ist nicht erreichbar (ausgeschaltet oder getrennt).",
        0x80210015 => "Der Scanner ist nicht erreichbar (ausgeschaltet oder getrennt).",
        0x80210006 => "Der Scanner ist belegt.",
        0x80210001 => "Der Scanner meldet einen allgemeinen Fehler.",
        _ => $"Scanfehler (0x{hresult:X8})."
    };

    private static void ItemEinrichten(dynamic item, ScanZustand e)
    {
        dynamic p = item.Properties;
        Setzen(p, Absicht, e.Farbe switch
        {
            ScanFarbe.Farbe => 1,
            ScanFarbe.Schwarzweiß => 4,
            _ => 2
        });
        Setzen(p, DpiX, e.Dpi);
        Setzen(p, DpiY, e.Dpi);
        Setzen(p, StartX, 0);
        Setzen(p, StartY, 0);
        (double breiteZoll, double höheZoll) = e.Papier switch
        {
            ScanPapier.A5 => (5.83, 8.27),
            ScanPapier.Letter => (8.5, 11.0),
            _ => (8.27, 11.69)
        };
        Setzen(p, BreitePx, (int)Math.Round(breiteZoll * e.Dpi));
        Setzen(p, HöhePx, (int)Math.Round(höheZoll * e.Dpi));
        SetzenSkaliert(p, Helligkeit, e.Helligkeit);
        SetzenSkaliert(p, Kontrast, e.Kontrast);
    }

    // ----- WIA-Helfer ------------------------------------------------------

    private static dynamic? Manager()
    {
        Type? typ = Type.GetTypeFromProgID("WIA.DeviceManager");
        return typ is null ? null : Activator.CreateInstance(typ);
    }

    private static dynamic Verbinden(string scannerId)
    {
        dynamic manager = Manager() ?? throw new ScanFehler("Windows-Bilderfassung (WIA) ist nicht verfügbar.", 0);
        foreach (dynamic info in manager.DeviceInfos)
            if ((string)info.DeviceID == scannerId)
                return info.Connect();
        throw new ScanFehler("Der gewählte Scanner wurde nicht gefunden.", 0);
    }

    private static dynamic? Eigenschaft(dynamic props, int id)
    {
        foreach (dynamic p in props)
            if ((int)p.PropertyID == id)
                return p;
        return null;
    }

    private static object? Wert(dynamic props, int id) => Eigenschaft(props, id)?.Value;

    /// <summary>Setzt eine Eigenschaft, sofern vorhanden, geklemmt auf ihren Bereich.</summary>
    private static void Setzen(dynamic props, int id, int wert)
    {
        dynamic? p = Eigenschaft(props, id);
        if (p is null)
            return;
        try
        {
            if ((int)p.SubType == 1) // RangeSubType
                wert = Math.Clamp(wert, (int)p.SubTypeMin, (int)p.SubTypeMax);
            p.Value = wert;
        }
        catch (COMException)
        {
            // Treiber lehnt den Wert ab → Gerätestandard behalten.
        }
    }

    /// <summary>Bildet -100..100 linear auf den Bereich der Eigenschaft ab.</summary>
    private static void SetzenSkaliert(dynamic props, int id, int wert)
    {
        dynamic? p = Eigenschaft(props, id);
        if (p is null || (int)p.SubType != 1)
            return;
        int min = (int)p.SubTypeMin, max = (int)p.SubTypeMax;
        Setzen(props, id, (int)Math.Round(min + (wert + 100) / 200.0 * (max - min)));
    }

    private static T AufSta<T>(Func<T> f)
    {
        T ergebnis = default!;
        Exception? fehler = null;
        var t = new Thread(() => { try { ergebnis = f(); } catch (Exception ex) { fehler = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (fehler is not null)
            throw fehler;
        return ergebnis;
    }
}
