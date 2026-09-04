using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfEditor.Services;

namespace PdfEditor.Controls;

/// <summary>
/// Transparente Ebene über dem Seitenbild, die den vorhandenen PDF-Text wie in
/// einem normalen Textfenster markier- und kopierbar macht – auch über mehrere
/// Zeilen hinweg. Anders als eine Ansammlung einzelner Textfelder verwaltet diese
/// Ebene die Auswahl selbst: Mit gedrückter Maustaste wird von einem Anker- zu
/// einem Cursorpunkt aufgezogen, alle dazwischenliegenden Zeilen werden markiert,
/// und Strg+C kopiert den Bereich zeilenweise in die Zwischenablage.
/// </summary>
public sealed class TextAuswahlEbene : Canvas
{
    /// <summary>Eine Zeile in Anzeigekoordinaten samt Zeichen-X-Positionen.</summary>
    private sealed record Zeile(Rect Rechteck, string Text, double[] X);

    // Teal-getönte Markierung wie der WPF-Selektionston der App.
    private static readonly Brush Markierung = Eingefroren(Color.FromArgb(110, 0x15, 0xA3, 0x9A));

    private readonly List<Zeile> _zeilen;
    private readonly SeitenGeometrie _geo;

    // Auswahl als (Zeile, Zeichen). Anker = Startpunkt der Geste, Cursor = aktueller Punkt.
    private int _ankerZ, _ankerC, _cursorZ, _cursorC;
    private bool _hatAuswahl;
    private bool _zieht;

    /// <summary>
    /// Wird ausgelöst, wenn der Benutzer zum markierten Text einen Kommentar anlegen
    /// möchte. Liefert die markierten Bereiche als QuadPoints (PDF-Punkte) und den Text.
    /// </summary>
    public event Action<IReadOnlyList<(double X1, double Y1, double X2, double Y2)>, string>? KommentarAngefordert;

    public TextAuswahlEbene(IReadOnlyList<TextZeile> zeilen, SeitenGeometrie geo)
    {
        _geo = geo;
        Width = geo.AnzeigeBreite;
        Height = geo.AnzeigeHöhe;
        Background = Brushes.Transparent;
        // Kein fester Cursor: Der I-Balken erscheint nur direkt über einer Textzeile
        // (siehe OnMouseMove), sonst gilt der normale Pfeil.
        Focusable = true;
        ContextMenu = ErstelleKontextmenü();

        // Zeilen in Anzeigekoordinaten umrechnen und in Lesereihenfolge sortieren
        // (oben nach unten, dann links nach rechts).
        _zeilen = zeilen
            .Select(z => BaueZeile(z, geo))
            .Where(z => z.Rechteck is { Width: >= 1, Height: >= 1 })
            .OrderBy(z => Math.Round(z.Rechteck.Top))
            .ThenBy(z => z.Rechteck.Left)
            .ToList();
    }

    /// <summary>Rechnet eine Zeile in Anzeigekoordinaten samt Zeichenpositionen um.</summary>
    private static Zeile BaueZeile(TextZeile z, SeitenGeometrie geo)
    {
        Rect r = geo.RechteckNachAnzeige(z.X1, z.Y1, z.X2, z.Y2);
        return new Zeile(r, z.Text, ZeichenPositionen(z, r));
    }

    /// <summary>
    /// Bildet die echten PDF-Zeichengrenzen (<see cref="TextZeile.ZeichenX"/>) auf
    /// Anzeige-X-Positionen ab. Da die Grenzen aus den tatsächlichen Glyphen-Vorschüben
    /// stammen, folgt die Markierung den Buchstaben exakt – ohne die PDF-Schrift schätzen
    /// zu müssen.
    /// </summary>
    private static double[] ZeichenPositionen(TextZeile z, Rect r)
    {
        int n = z.Text.Length + 1;
        var pos = new double[n];
        double breitePt = z.X2 - z.X1;

        if (breitePt <= 0 || z.ZeichenX.Length < n)
        {
            for (int i = 0; i < n; i++)
                pos[i] = r.Left;
            return pos;
        }

        // PDF-X relativ zur Zeilenbreite in die Anzeige-X abbilden (skalierungssicher).
        for (int i = 0; i < n; i++)
            pos[i] = r.Left + (z.ZeichenX[i] - z.X1) / breitePt * r.Width;
        return pos;
    }

    /// <summary>Kontextmenü mit „Kopieren“, „Kommentar hinzufügen“ und „Alles auswählen“.</summary>
    private ContextMenu ErstelleKontextmenü()
    {
        var kopieren = new MenuItem { Header = "Kopieren", InputGestureText = "Strg+C" };
        kopieren.Click += (_, _) => Kopieren();

        var kommentar = new MenuItem { Header = "Kommentar hinzufügen" };
        kommentar.Click += (_, _) => KommentarErstellen();

        var alles = new MenuItem { Header = "Alles auswählen", InputGestureText = "Strg+A" };
        alles.Click += (_, _) => AllesMarkieren();

        var menü = new ContextMenu();
        menü.Items.Add(kopieren);
        menü.Items.Add(kommentar);
        menü.Items.Add(new Separator());
        menü.Items.Add(alles);
        // Beim Öffnen: ohne Text kein Menü; „Kopieren“/„Kommentar“ nur bei Auswahl.
        menü.Opened += (_, _) =>
        {
            kopieren.IsEnabled = _hatAuswahl;
            kommentar.IsEnabled = _hatAuswahl;
        };
        ContextMenuOpening += (_, e) =>
        {
            if (_zeilen.Count == 0)
                e.Handled = true; // kein Menü auf einer Seite ohne Text
        };
        return menü;
    }

    // ----- Mauseingabe ------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_zeilen.Count == 0)
            return;

        Focus();
        (_ankerZ, _ankerC) = Treffer(e.GetPosition(this));
        _cursorZ = _ankerZ;
        _cursorC = _ankerC;
        _hatAuswahl = false;
        _zieht = true;
        CaptureMouse();
        AuswahlZeichnen();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);

        // I-Balken nur direkt über einer Textzeile (oder während des Aufziehens);
        // null lässt den Cursor des umgebenden Elements (normaler Pfeil) gelten.
        Cursor = _zieht || ÜberText(p) ? Cursors.IBeam : null;

        if (!_zieht)
            return;

        (_cursorZ, _cursorC) = Treffer(p);
        _hatAuswahl = _cursorZ != _ankerZ || _cursorC != _ankerC;
        AuswahlZeichnen();
    }

    /// <summary>Ob der Punkt (Anzeigekoordinaten) über einer Textzeile liegt (mit kleiner Toleranz).</summary>
    private bool ÜberText(Point p)
    {
        foreach (var z in _zeilen)
        {
            Rect r = z.Rechteck;
            r.Inflate(2, 2);
            if (r.Contains(p))
                return true;
        }
        return false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_zieht)
            return;
        _zieht = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key == Key.C)
        {
            Kopieren();
            e.Handled = true;
        }
        else if (e.Key == Key.A)
        {
            AllesMarkieren();
            e.Handled = true;
        }
    }

    // ----- Auswahl ----------------------------------------------------------

    /// <summary>
    /// Ermittelt die nächstgelegene Zeichengrenze (Zeile, Zeichen) zu einem Punkt.
    /// Auf einer Zeilenhöhe können mehrere getrennte Zeilen nebeneinander liegen
    /// (z. B. Tabellenspalten einer Rechnung); daher entscheidet bei (nahezu)
    /// gleichem vertikalem Abstand der horizontale Abstand, sonst würde immer die
    /// linkeste Zeile der Reihe gewinnen und die Spalten rechts wären unmarkierbar.
    /// </summary>
    private (int Zeile, int Zeichen) Treffer(Point p)
    {
        // 1. Durchgang: kleinster vertikaler Abstand zur Zeile.
        double minDy = double.MaxValue;
        foreach (var z in _zeilen)
        {
            Rect r = z.Rechteck;
            double dy = p.Y < r.Top ? r.Top - p.Y : p.Y > r.Bottom ? p.Y - r.Bottom : 0;
            minDy = Math.Min(minDy, dy);
        }

        // 2. Durchgang: unter den vertikal (fast) gleich nahen Zeilen die horizontal nächste.
        const double toleranz = 2.0;
        int beste = 0;
        double besterDx = double.MaxValue;
        for (int i = 0; i < _zeilen.Count; i++)
        {
            Rect r = _zeilen[i].Rechteck;
            double dy = p.Y < r.Top ? r.Top - p.Y : p.Y > r.Bottom ? p.Y - r.Bottom : 0;
            if (dy > minDy + toleranz)
                continue;
            double dx = p.X < r.Left ? r.Left - p.X : p.X > r.Right ? p.X - r.Right : 0;
            if (dx < besterDx)
            {
                besterDx = dx;
                beste = i;
            }
        }

        return (beste, NächstesZeichen(_zeilen[beste], p.X));
    }

    private static int NächstesZeichen(Zeile z, double x)
    {
        int beste = 0;
        double besterAbstand = double.MaxValue;
        for (int i = 0; i < z.X.Length; i++)
        {
            double d = Math.Abs(z.X[i] - x);
            if (d < besterAbstand)
            {
                besterAbstand = d;
                beste = i;
            }
        }
        return beste;
    }

    /// <summary>Normalisiert Anker/Cursor zu Start ≤ Ende in Lesereihenfolge.</summary>
    private (int SZ, int SC, int EZ, int EC) Normalisiert()
    {
        bool ankerZuerst = _ankerZ < _cursorZ || (_ankerZ == _cursorZ && _ankerC <= _cursorC);
        return ankerZuerst
            ? (_ankerZ, _ankerC, _cursorZ, _cursorC)
            : (_cursorZ, _cursorC, _ankerZ, _ankerC);
    }

    private void AllesMarkieren()
    {
        if (_zeilen.Count == 0)
            return;
        _ankerZ = 0;
        _ankerC = 0;
        _cursorZ = _zeilen.Count - 1;
        _cursorC = _zeilen[^1].Text.Length;
        _hatAuswahl = true;
        AuswahlZeichnen();
    }

    private void AuswahlZeichnen()
    {
        Children.Clear();
        if (!_hatAuswahl)
            return;

        foreach (Rect r in AuswahlRechtecke())
        {
            var rechteck = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Fill = Markierung,
                IsHitTestVisible = false
            };
            SetLeft(rechteck, r.Left);
            SetTop(rechteck, r.Top);
            Children.Add(rechteck);
        }
    }

    /// <summary>Die markierten Bereiche als Anzeige-Rechtecke (ein Rechteck pro Zeile).</summary>
    private List<Rect> AuswahlRechtecke()
    {
        var liste = new List<Rect>();
        if (!_hatAuswahl)
            return liste;

        var (sz, sc, ez, ec) = Normalisiert();
        for (int i = sz; i <= ez; i++)
        {
            Zeile z = _zeilen[i];
            int von = i == sz ? sc : 0;
            int bis = i == ez ? ec : z.Text.Length;
            von = Math.Clamp(von, 0, z.Text.Length);
            bis = Math.Clamp(bis, 0, z.Text.Length);
            double x1 = z.X[von];
            double x2 = z.X[bis];
            if (x2 < x1)
                (x1, x2) = (x2, x1);
            if (x2 - x1 < 0.5)
                continue;
            liste.Add(new Rect(x1, z.Rechteck.Top, x2 - x1, z.Rechteck.Height));
        }
        return liste;
    }

    /// <summary>Wandelt die aktuelle Auswahl in QuadPoints (PDF-Punkte) und meldet sie.</summary>
    private void KommentarErstellen()
    {
        if (!_hatAuswahl)
            return;
        var quads = AuswahlRechtecke()
            .Select(r => _geo.AnzeigeRechteckNachPunkten(r))
            .ToList();
        if (quads.Count == 0)
            return;
        KommentarAngefordert?.Invoke(quads, AuswahlText());
    }

    private string AuswahlText()
    {
        if (!_hatAuswahl)
            return string.Empty;

        var (sz, sc, ez, ec) = Normalisiert();
        if (sz == ez)
        {
            string t = _zeilen[sz].Text;
            int a = Math.Clamp(sc, 0, t.Length);
            int b = Math.Clamp(ec, 0, t.Length);
            return t[Math.Min(a, b)..Math.Max(a, b)];
        }

        var sb = new StringBuilder();
        string erste = _zeilen[sz].Text;
        sb.Append(erste[Math.Clamp(sc, 0, erste.Length)..]);
        for (int i = sz + 1; i < ez; i++)
        {
            sb.Append('\n');
            sb.Append(_zeilen[i].Text);
        }
        string letzte = _zeilen[ez].Text;
        sb.Append('\n');
        sb.Append(letzte[..Math.Clamp(ec, 0, letzte.Length)]);
        return sb.ToString();
    }

    private void Kopieren()
    {
        string t = AuswahlText();
        if (string.IsNullOrEmpty(t))
            return;
        try { Clipboard.SetText(t); }
        catch { /* Zwischenablage kann kurzzeitig belegt sein – still ignorieren */ }
    }

    private static Brush Eingefroren(Color farbe)
    {
        var b = new SolidColorBrush(farbe);
        b.Freeze();
        return b;
    }
}
