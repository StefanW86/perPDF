using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PdfEditor.Windows;

/// <summary>Vorschaufläche: A4-Hochformat, Zoom per Mausrad, Verschieben per Ziehen.</summary>
public partial class ScanFenster
{
    private const double A4Verhältnis = 297.0 / 210.0;
    private const double ZoomMax = 8;

    private double _zoom = 1;
    private Point? _ziehStart;
    private Point _ziehOffset;

    /// <summary>Zeigt ein Bild in der Vorschau und setzt den Zoom auf „ganze Seite“ zurück.</summary>
    private void VorschauZeigen(ImageSource bild)
    {
        VorschauBild.Source = bild;
        ZoomSetzen(1, new Point(0, 0));
    }

    /// <summary>Passt den Rahmen in den verfügbaren Platz ein, im Seitenverhältnis von A4.</summary>
    private void VorschauPlatz_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double breite = Math.Min(VorschauPlatz.ActualWidth, VorschauPlatz.ActualHeight / A4Verhältnis);
        VorschauRahmen.Width = Math.Max(0, breite);
        VorschauRahmen.Height = Math.Max(0, breite * A4Verhältnis);
        VorschauRahmen.UpdateLayout();
        ZoomSetzen(_zoom, new Point(0, 0));
    }

    private void Vorschau_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (VorschauBild.Source is null)
            return;
        double faktor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        ZoomSetzen(_zoom * faktor, e.GetPosition(VorschauScroll));
    }

    private void Vorschau_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ZoomSetzen(1, new Point(0, 0));
            e.Handled = true;
            return;
        }
        if (_zoom <= 1)
            return;
        _ziehStart = e.GetPosition(VorschauScroll);
        _ziehOffset = new Point(VorschauScroll.HorizontalOffset, VorschauScroll.VerticalOffset);
        VorschauScroll.CaptureMouse();
        VorschauScroll.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Vorschau_MouseMove(object sender, MouseEventArgs e)
    {
        if (_ziehStart is not { } start)
            return;
        Vector d = e.GetPosition(VorschauScroll) - start;
        VorschauScroll.ScrollToHorizontalOffset(_ziehOffset.X - d.X);
        VorschauScroll.ScrollToVerticalOffset(_ziehOffset.Y - d.Y);
    }

    private void Vorschau_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_ziehStart is null)
            return;
        _ziehStart = null;
        VorschauScroll.ReleaseMouseCapture();
        VorschauScroll.Cursor = _zoom > 1 ? Cursors.Hand : null;
    }

    /// <summary>Setzt den Zoom (1 = ganze Seite) und hält den Punkt unter <paramref name="fix"/> fest.</summary>
    private void ZoomSetzen(double zoom, Point fix)
    {
        double sichtB = VorschauScroll.ViewportWidth > 0 ? VorschauScroll.ViewportWidth : VorschauScroll.ActualWidth;
        double sichtH = VorschauScroll.ViewportHeight > 0 ? VorschauScroll.ViewportHeight : VorschauScroll.ActualHeight;
        if (sichtB <= 0 || sichtH <= 0)
            return;
        zoom = Math.Clamp(zoom, 1, ZoomMax);
        // Inhaltspunkt unter dem Mauszeiger, relativ zum bisherigen Zoom
        double inhaltX = (VorschauScroll.HorizontalOffset + fix.X) / _zoom;
        double inhaltY = (VorschauScroll.VerticalOffset + fix.Y) / _zoom;
        _zoom = zoom;
        VorschauBild.Width = sichtB * zoom;
        VorschauBild.Height = sichtH * zoom;
        VorschauScroll.UpdateLayout();
        VorschauScroll.ScrollToHorizontalOffset(inhaltX * zoom - fix.X);
        VorschauScroll.ScrollToVerticalOffset(inhaltY * zoom - fix.Y);
        VorschauScroll.Cursor = zoom > 1 ? Cursors.Hand : null;
        ZoomAnzeige.Visibility = zoom > 1 ? Visibility.Visible : Visibility.Collapsed;
        ZoomText.Text = $"{zoom * 100:0} %";
    }
}
