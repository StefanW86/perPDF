using System.Windows;

namespace PdfEditor.Services;

/// <summary>
/// Rechnet zwischen PDF-Koordinaten (Punkte, Ursprung unten links) und den
/// angezeigten Bildschirmkoordinaten (DIP, Ursprung oben links) um – unter
/// Berücksichtigung der Seitendrehung (0/90/180/270 Grad) und der Zoomstufe.
/// </summary>
public class SeitenGeometrie
{
    private readonly double _w;        // ungedrehte Seitenbreite in Punkten
    private readonly double _h;        // ungedrehte Seitenhöhe in Punkten
    private readonly double _versatzX; // MediaBox-Ursprung X
    private readonly double _versatzY; // MediaBox-Ursprung Y
    private readonly int _drehung;     // 0,1,2,3 entspricht 0/90/180/270 Grad im Uhrzeigersinn
    private readonly double _s;        // DIP pro Punkt (enthält Zoom)

    public SeitenGeometrie(SeitenInfo info, double zoom)
    {
        _versatzX = info.X1;
        _versatzY = info.Y1;
        _w = info.Breite;
        _h = info.Höhe;
        _drehung = (((info.DrehungGrad % 360) + 360) % 360) / 90;
        // 1 PDF-Punkt entspricht 96/72 DIP bei 100 % Zoom.
        _s = (96.0 / 72.0) * zoom;
    }

    /// <summary>Umrechnungsfaktor DIP pro PDF-Punkt (enthält den Zoom).</summary>
    public double DipProPunkt => _s;

    /// <summary>Breite des angezeigten Seitenbildes in DIP.</summary>
    public double AnzeigeBreite => (_drehung is 1 or 3 ? _h : _w) * _s;

    /// <summary>Höhe des angezeigten Seitenbildes in DIP.</summary>
    public double AnzeigeHöhe => (_drehung is 1 or 3 ? _w : _h) * _s;

    /// <summary>
    /// Render-Auflösung (DPI), bei der das Seitenbild erzeugt werden soll, damit
    /// die Pixelanzahl der DIP-Anzeigegröße entspricht (gestochen scharf bei 100 %).
    /// </summary>
    public int RenderDpi => Math.Max(1, (int)Math.Round(72.0 * _s));

    /// <summary>Wandelt einen PDF-Punkt in Anzeigekoordinaten (oben links) um.</summary>
    public Point PunktNachAnzeige(double xPt, double yPt)
    {
        // Zunächst in das ungedrehte Pixelraster mit Ursprung oben links.
        double ux = (xPt - _versatzX) * _s;
        double uy = (_h - (yPt - _versatzY)) * _s;
        double bw = _w * _s; // ungedrehte Bildbreite
        double bh = _h * _s; // ungedrehte Bildhöhe

        return _drehung switch
        {
            1 => new Point(bh - uy, ux),
            2 => new Point(bw - ux, bh - uy),
            3 => new Point(uy, bw - ux),
            _ => new Point(ux, uy),
        };
    }

    /// <summary>Wandelt Anzeigekoordinaten (oben links) zurück in einen PDF-Punkt um.</summary>
    public Point AnzeigeNachPunkt(double dx, double dy)
    {
        double bw = _w * _s;
        double bh = _h * _s;
        double ux, uy;

        switch (_drehung)
        {
            case 1:
                uy = bh - dx;
                ux = dy;
                break;
            case 2:
                ux = bw - dx;
                uy = bh - dy;
                break;
            case 3:
                ux = bw - dy;
                uy = dx;
                break;
            default:
                ux = dx;
                uy = dy;
                break;
        }

        double xPt = ux / _s + _versatzX;
        double yPt = _h - uy / _s + _versatzY;
        return new Point(xPt, yPt);
    }

    /// <summary>Wandelt ein PDF-Rechteck in ein Anzeige-Rechteck (oben links) um.</summary>
    public Rect RechteckNachAnzeige(double x1, double y1, double x2, double y2)
    {
        Point a = PunktNachAnzeige(x1, y1);
        Point b = PunktNachAnzeige(x2, y2);
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        return new Rect(left, top, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>Wandelt ein Anzeige-Rechteck zurück in PDF-Punkte (x1,y1,x2,y2) um.</summary>
    public (double X1, double Y1, double X2, double Y2) AnzeigeRechteckNachPunkten(Rect r)
    {
        Point a = AnzeigeNachPunkt(r.Left, r.Top);
        Point b = AnzeigeNachPunkt(r.Right, r.Bottom);
        return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
