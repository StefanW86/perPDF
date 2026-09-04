using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.ViewModels;

namespace PdfEditor;

/// <summary>Overlays für Entwurfsfelder des Formular-Designers (neu angelegte/bearbeitete Felder).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Baut das Overlay eines neu angelegten Formularfeldes: eine typgerechte
    /// (nicht interaktive) Darstellung mit gestricheltem Rahmen, verschieb- und
    /// skalierbar; ein Klick wählt das Feld für die Eigenschaften-Seitenleiste aus.
    /// </summary>
    private void FügeEntwurfHinzu(FormularEntwurf feld, SeitenGeometrie geo)
    {
        // Abdeck-Vorschau: die Original-Beschriftungen eines bearbeiteten Optionsfeldes
        // schon im Editor verdecken (wie beim Speichern), sonst stünde der alte Text
        // sichtbar hinter dem neuen.
        foreach (var a in feld.Abdeckungen)
            if (a is not null)
                ZeichneAbdeckung(a, geo);

        Rect r = geo.RechteckNachAnzeige(feld.X, feld.Y, feld.X + feld.Breite, feld.Y + feld.Höhe);
        var behälter = ErzeugeBehälter(r);
        behälter.Focusable = true;
        behälter.ClipToBounds = true; // Vorschau bleibt im Feldrechteck (kein Überlaufen)
        behälter.ToolTip = $"{HauptViewModel.FeldBezeichnung(feld.Typ)}: {feld.FeldName}";

        // Optionsfelder sind im Editor direkt anklickbar (Auswahl = Standardoption);
        // dann wird über einen kleinen Griff verschoben statt ganzflächig.
        bool interaktiv = feld.Typ == EntwurfFeldTyp.Optionsfeld;
        var darstellung = ErstelleEntwurfDarstellung(feld, r);
        if (!interaktiv)
            darstellung.IsHitTestVisible = false; // Ziehen/Auswählen übernimmt der Griff darüber
        else
            behälter.Background = Brushes.Transparent; // Klick auf freie Fläche wählt das Feld aus
        behälter.Children.Add(darstellung);

        // Gestrichelter Rahmen kennzeichnet ein noch nicht gespeichertes Entwurfsfeld.
        behälter.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = Akzent,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        });

        // Durchgezogener Rahmen nur bei Auswahl (Tastaturfokus).
        var auswahl = new System.Windows.Shapes.Rectangle
        {
            Stroke = Akzent,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        behälter.Children.Add(auswahl);

        // Klick wählt das Feld aus (befüllt die Eigenschaften-Seitenleiste).
        behälter.PreviewMouseLeftButtonDown += (_, _) => _vm.AusgewähltesFeld = feld;

        // Optionsfelder: Verschieben am gesamten Rand (Mitte bleibt zum Anklicken der
        // Optionen frei). Übrige Felder: ganzflächiges Ziehen wie bei Unterschriften.
        StatteAdornerAus(behälter, geo, ganzflächigesZiehen: !interaktiv,
            aktualisieren: (l, o, b, h) => SetzeRechteck(geo, l, o, b, h, (x, y, w, hh) =>
            {
                feld.X = x; feld.Y = y; feld.Breite = w; feld.Höhe = hh;
            }),
            entfernen: () => _vm.FeldEntfernen(feld),
            seitenverhältnisHalten: false,
            nurBeiFokus: auswahl,
            randZiehen: interaktiv);

        OverlayCanvas.Children.Add(behälter);
    }

    /// <summary>
    /// Zeichnet eine Beschriftungs-Abdeckung als rein passives farbiges Rechteck über
    /// das Seitenbild (nicht anklickbar) – die Bildschirm-Entsprechung des beim
    /// Speichern eingebrannten Abdeck-Rechtecks.
    /// </summary>
    private void ZeichneAbdeckung(BeschriftungsAbdeckung a, SeitenGeometrie geo)
    {
        Rect ar = geo.RechteckNachAnzeige(a.X1, a.Y1, a.X2, a.Y2);
        var deck = new System.Windows.Shapes.Rectangle
        {
            Width = ar.Width,
            Height = ar.Height,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(a.Farbe)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(deck, ar.Left);
        Canvas.SetTop(deck, ar.Top);
        OverlayCanvas.Children.Add(deck);
    }

    /// <summary>Erzeugt die typgerechte Darstellung eines Entwurfsfeldes.</summary>
    private FrameworkElement ErstelleEntwurfDarstellung(FormularEntwurf feld, Rect r)
    {
        // Die Darstellungen sind reine, größenrobuste Vorschauen (keine gethemten
        // Steuerelemente, die bei kleinen Feldhöhen Text abschneiden würden). Klicks
        // gehen an den darüberliegenden Auswahl-/Ziehgriff (Feld auswählen/verschieben).
        switch (feld.Typ)
        {
            case EntwurfFeldTyp.Kontrollkästchen:
            {
                double k = Math.Max(8, Math.Min(r.Width, r.Height) - 2);
                return new Border
                {
                    Width = k,
                    Height = k,
                    Background = FeldHintergrund,
                    BorderBrush = Akzent,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = EntwurfAngehakt(feld.Standardwert) ? "✓" : string.Empty,
                        Foreground = Akzent,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
            }

            case EntwurfFeldTyp.Optionsfeld:
            {
                // Anklickbare Optionen: die gewählte wird als Standardoption übernommen.
                // Innenabstand hält die Optionen frei vom umlaufenden Ziehrand.
                int n = Math.Max(1, feld.Optionen.Count);
                double fs = Math.Clamp(r.Height / n * 0.42, 8, 12);
                string gruppe = "feldgruppe_" + feld.GetHashCode().ToString("X");
                var sp = new StackPanel { Margin = new Thickness(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Top };
                for (int i = 0; i < feld.Optionen.Count; i++)
                {
                    string option = feld.Optionen[i];
                    var rb = new RadioButton
                    {
                        // Wie beim Speichern: keine Beschriftung für Optionen, deren
                        // nicht erkanntes (unabgedecktes) Original sichtbar bleibt.
                        Content = feld.BeschriftungZeichnen(i) ? option : null,
                        GroupName = gruppe,
                        FontSize = fs,
                        Foreground = Brushes.Black,
                        Margin = new Thickness(0, 1, 0, 1),
                        IsChecked = option == feld.Standardwert
                    };
                    rb.Checked += (_, _) => feld.Standardwert = option;
                    sp.Children.Add(rb);
                }
                return sp;
            }

            case EntwurfFeldTyp.Dropdown:
            {
                double fs = Math.Clamp(r.Height * 0.5, 7, 14);
                string txt = !string.IsNullOrEmpty(feld.Standardwert)
                    ? feld.Standardwert
                    : feld.Optionen.FirstOrDefault() ?? "Auswahl";
                var dock = new DockPanel { Background = FeldHintergrund, LastChildFill = true };
                var pfeil = new TextBlock
                {
                    Text = "⌄",
                    FontSize = fs,
                    Foreground = Akzent,
                    Margin = new Thickness(2, 0, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(pfeil, Dock.Right);
                dock.Children.Add(pfeil);
                dock.Children.Add(new TextBlock
                {
                    Text = txt,
                    FontSize = fs,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                return dock;
            }

            case EntwurfFeldTyp.Listenfeld:
            {
                var sp = new StackPanel { Margin = new Thickness(3, 2, 2, 2), VerticalAlignment = VerticalAlignment.Top };
                foreach (var o in feld.Optionen)
                    sp.Children.Add(new TextBlock
                    {
                        Text = o,
                        FontSize = 11,
                        Foreground = Brushes.Black,
                        Margin = new Thickness(0, 1, 0, 1),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                return new Border { Background = FeldHintergrund, Child = sp };
            }

            case EntwurfFeldTyp.Unterschrift:
                return new Border
                {
                    Background = FeldHintergrund,
                    Child = new TextBlock
                    {
                        Text = "Unterschrift",
                        Foreground = Akzent,
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

            default: // Textfeld, MehrzeiligesTextfeld, Datum, Zahl
            {
                bool mehrzeilig = feld.Typ == EntwurfFeldTyp.MehrzeiligesTextfeld;
                bool hatWert = !string.IsNullOrEmpty(feld.Standardwert);
                double fs = mehrzeilig ? 12 : Math.Clamp(r.Height * 0.55, 8, 16);
                return new Border
                {
                    Background = FeldHintergrund,
                    Child = new TextBlock
                    {
                        Text = hatWert ? feld.Standardwert : EntwurfPlatzhalter(feld.Typ),
                        Foreground = hatWert ? Brushes.Black : Brushes.Gray,
                        FontSize = fs,
                        Margin = new Thickness(3, 1, 3, 1),
                        TextWrapping = mehrzeilig ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        TextTrimming = mehrzeilig ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                        VerticalAlignment = mehrzeilig ? VerticalAlignment.Top : VerticalAlignment.Center
                    }
                };
            }
        }
    }

    private static string EntwurfPlatzhalter(EntwurfFeldTyp typ) => typ switch
    {
        EntwurfFeldTyp.Datum => "TT.MM.JJJJ",
        EntwurfFeldTyp.Zahl => "0,00",
        _ => "Text"
    };

    /// <summary>Ob ein Kontrollkästchen-Standardwert als „angehakt" gilt (ja/true/1/x).</summary>
    private static bool EntwurfAngehakt(string? s) => s is not null
        && (s.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s.Equals("1", StringComparison.Ordinal)
            || s.Equals("x", StringComparison.OrdinalIgnoreCase));
}
