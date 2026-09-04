using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>Eingefügte Bilder (aus Datei oder Zwischenablage): Befehle und Platzierung.</summary>
public partial class HauptViewModel
{
    /// <summary>Noch nicht gespeicherte, auf Seiten platzierte Bilder.</summary>
    public ObservableCollection<BildEinfügung> Bilder { get; } = new();

    public RelayCommand BildEinfügenBefehl { get; }

    private byte[]? _ausstehendesBild;
    /// <summary>Beim nächsten Klick einzufügendes Bild (nur gültig wenn EinfügeModus == Bild).</summary>
    public byte[]? AusstehendesBild => _ausstehendesBild;

    /// <summary>Öffnet den Dateidialog und startet den Platziermodus für das gewählte Bild.</summary>
    private void BildEinfügen()
    {
        if (!_dienst.HatDokument || AktuelleSeite < 0)
        {
            Status = "Bitte zuerst eine PDF öffnen oder eine Seite hinzufügen.";
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Bild einfügen",
            Filter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var png = Bildwerkzeuge.PngNormalisieren(File.ReadAllBytes(dlg.FileName));
            _ausstehendesBild = png;
            EinfügeModus = EinfügeArt.Bild;
            Status = "Bild – auf die Stelle klicken, an der es erscheinen soll. Esc bricht ab.";
        }
        catch (Exception ex)
        {
            Fehler("Das Bild konnte nicht geladen werden.", ex);
        }
    }

    /// <summary>
    /// Fügt ein Bild aus der Zwischenablage mittig auf der aktuellen Seite ein
    /// (Strg+V im Hauptfenster).
    /// </summary>
    public void BildAusZwischenablage()
    {
        if (!_dienst.HatDokument || AktuelleSeite < 0)
        {
            Status = "Bitte zuerst eine PDF öffnen oder eine Seite hinzufügen.";
            return;
        }

        try
        {
            var quelle = Clipboard.GetImage();
            if (quelle is null)
            {
                Status = "Die Zwischenablage enthält kein Bild.";
                return;
            }
            var kodierer = new PngBitmapEncoder();
            kodierer.Frames.Add(BitmapFrame.Create(quelle));
            using var strom = new MemoryStream();
            kodierer.Save(strom);

            var info = _seitenInfos[AktuelleSeite];
            BildPlatzieren(strom.ToArray(),
                info.X1 + info.Breite / 2, info.Y1 + info.Höhe / 2);
            Status = "Bild aus der Zwischenablage eingefügt – verschieben, skalieren, zuschneiden oder drehen.";
        }
        catch (Exception ex)
        {
            Fehler("Das Bild aus der Zwischenablage konnte nicht eingefügt werden.", ex);
        }
    }

    /// <summary>Platziert das ausstehende Bild an der angegebenen PDF-Koordinate (Mittelpunkt).</summary>
    public void BildAnPosition(double pdfX, double pdfY)
    {
        var png = _ausstehendesBild;
        if (png is null || AktuelleSeite < 0) { EinfügeModusBeenden(); return; }

        BildPlatzieren(png, pdfX, pdfY);
        EinfügeModusBeenden();
        Status = "Bild platziert – verschieben, skalieren, zuschneiden oder drehen.";
    }

    /// <summary>Legt das Bild mittig um die angegebene PDF-Koordinate an und zeigt es an.</summary>
    private void BildPlatzieren(byte[] png, double pdfX, double pdfY)
    {
        var info = _seitenInfos[AktuelleSeite];
        double verhältnis = Bildwerkzeuge.Seitenverhältnis(png);

        // Standardgröße: gut sichtbar, aber nie größer als gut die halbe Seite.
        double breite = Math.Min(300, info.Breite * 0.6);
        double höhe = breite / verhältnis;
        if (höhe > info.Höhe * 0.6)
        {
            höhe = info.Höhe * 0.6;
            breite = höhe * verhältnis;
        }

        var bild = new BildEinfügung(png, AktuelleSeite)
        {
            X = pdfX - breite / 2,
            Y = pdfY - höhe / 2,
            Breite = breite,
            Höhe = höhe
        };
        Bilder.Add(bild);
        if (!_istLaden) IstGeändert = true;
        AktuelleSeiteNeu?.Invoke();
    }

    /// <summary>Entfernt ein platziertes Bild wieder (aus dem Hauptfenster aufgerufen).</summary>
    public void BildEntfernen(BildEinfügung bild)
    {
        Bilder.Remove(bild);
        AktuelleSeiteNeu?.Invoke();
    }
}
