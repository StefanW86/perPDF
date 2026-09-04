using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

/// <summary>Befehlsimplementierungen: Dokument-, Seiten- und Annotationsbefehle.</summary>
public partial class HauptViewModel
{
    // ----- Befehlsimplementierungen ----------------------------------------

    private async void Öffnen()
    {
        var dlg = new OpenFileDialog
        {
            Title = "PDF-Datei öffnen",
            Filter = "PDF-Dateien (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog() != true)
            return;

        _istLaden = true;
        IstGeändert = false;
        LadestatusGeändert?.Invoke(true, "Lade Dokument...");
        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(dlg.FileName));
            _dienst.ÖffnenAusBytes(bytes, dlg.FileName);
            AusstehendeZurücksetzen();
            FelderNeuLaden();
            await NachStrukturänderungAsync(0);
            Status = $"Geöffnet: {Path.GetFileName(dlg.FileName)}";
            Melde(nameof(Titel));
        }
        catch (Exception ex)
        {
            Fehler("Die Datei konnte nicht geöffnet werden.", ex);
        }
        finally
        {
            LadestatusGeändert?.Invoke(false, "");
            _istLaden = false;
        }
    }

    /// <summary>
    /// Öffnet eine PDF-Datei direkt per Pfad (z.B. beim Start via "Öffnen mit" oder
    /// Kommandozeilenargument) ohne Dateiauswahl-Dialog.
    /// </summary>
    public async void DateiLaden(string pfad)
    {
        _istLaden = true;
        IstGeändert = false;
        LadestatusGeändert?.Invoke(true, "Lade Dokument...");
        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(pfad));
            _dienst.ÖffnenAusBytes(bytes, pfad);
            AusstehendeZurücksetzen();
            FelderNeuLaden();
            await NachStrukturänderungAsync(0);
            Status = $"Geöffnet: {Path.GetFileName(pfad)}";
            Melde(nameof(Titel));
        }
        catch (Exception ex)
        {
            Fehler("Die Datei konnte nicht geöffnet werden.", ex);
        }
        finally
        {
            LadestatusGeändert?.Invoke(false, "");
            _istLaden = false;
        }
    }

    /// <summary>
    /// Lädt bereits vorhandene PDF-Bytes als aktives Dokument (z. B. nach dem Zusammenführen).
    /// Der bisherige Dateipfad bleibt erhalten; Annotationen werden zurückgesetzt.
    /// </summary>
    public async void BytesLaden(byte[] bytes)
    {
        _istLaden = true;
        IstGeändert = false;
        LadestatusGeändert?.Invoke(true, "Dokument wird übernommen...");
        try
        {
            _dienst.ÖffnenAusBytes(bytes, _dienst.Pfad ?? "zusammengeführt.pdf");
            AusstehendeZurücksetzen();
            FelderNeuLaden();
            await NachStrukturänderungAsync(0);
            Status = "Zusammengeführtes Dokument geladen.";
            Melde(nameof(Titel));
        }
        catch (Exception ex)
        {
            Fehler("Das zusammengeführte Dokument konnte nicht geladen werden.", ex);
        }
        finally
        {
            LadestatusGeändert?.Invoke(false, "");
            _istLaden = false;
        }
    }

    private void Speichern(bool neuerPfad)
    {
        string? ziel = _dienst.Pfad;
        if (neuerPfad || ziel is null)
        {
            var dlg = new SaveFileDialog
            {
                Title = "PDF-Datei speichern",
                Filter = "PDF-Dateien (*.pdf)|*.pdf",
                FileName = ziel is null ? "Dokument.pdf" : Path.GetFileName(ziel)
            };
            if (dlg.ShowDialog() != true)
                return;
            ziel = dlg.FileName;
        }
        else
        {
            // Vor dem Überschreiben des aktuellen Dokuments rückfragen.
            var antwort = MessageBox.Show(
                $"Das aktuelle Dokument \"{Path.GetFileName(ziel)}\" wirklich überschreiben?",
                "Speichern bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (antwort != MessageBoxResult.Yes)
                return;
        }

        try
        {
            _dienst.Speichern(ziel, Felder, Platzierungen, Bilder, Textnotizen,
                Textmarkierungen, Symbolmarkierungen, Entwurfsfelder, _zuLöschendeFelder,
                Seitenabdeckungen);
            // Alles ist nun fest eingezeichnet bzw. als echtes AcroForm-Feld gebacken –
            // Ausstehendes zurücksetzen und die frischen Felder sofort wieder als
            // ausfüllbare Formularfelder übernehmen (ohne erneutes Öffnen der Datei).
            AusstehendeZurücksetzen();
            FelderNeuLaden();
            NachStrukturänderung(AktuelleSeite);
            // NachStrukturänderung markiert das Dokument als geändert – direkt nach dem
            // Speichern ist es das aber nicht, daher zuletzt zurücksetzen.
            IstGeändert = false;
            Status = $"Gespeichert: {Path.GetFileName(ziel)}";
            Melde(nameof(Titel));
        }
        catch (Exception ex)
        {
            Fehler("Die Datei konnte nicht gespeichert werden.", ex);
        }
    }

    private void Drucken()
    {
        if (!_dienst.HatDokument)
            return;
        try
        {
            // WYSIWYG: den aktuellen Bearbeitungsstand (inkl. noch nicht gespeicherter
            // Annotationen und Formularwerte) in temporäre Bytes backen – ohne zu speichern.
            var bytes = _dienst.NachBytesBacken(Felder, Platzierungen, Bilder, Textnotizen,
                Textmarkierungen, Symbolmarkierungen, Entwurfsfelder, _zuLöschendeFelder,
                Seitenabdeckungen);
            string name = _dienst.Pfad is null
                ? "Dokument"
                : Path.GetFileNameWithoutExtension(_dienst.Pfad);
            bool gedruckt = DruckDienst.Drucken(bytes, Seiten.Count, name);
            Status = gedruckt ? "Dokument an den Drucker gesendet." : "Drucken abgebrochen.";
        }
        catch (Exception ex)
        {
            Fehler("Das Dokument konnte nicht gedruckt werden.", ex);
        }
    }

    private void SeiteHinzufügen()
    {
        // An der Position nach der aktuellen Seite einfügen (oder am Anfang).
        int index = _dienst.HatDokument ? AktuelleSeite + 1 : 0;
        try
        {
            _dienst.SeiteEinfügen(index);
            AnnotationenVerschieben(s => s >= index ? s + 1 : s);
            FelderNeuLaden();
            NachStrukturänderung(index);
            Status = "Leere Seite hinzugefügt.";
        }
        catch (Exception ex)
        {
            Fehler("Die Seite konnte nicht hinzugefügt werden.", ex);
        }
    }

    private void SeitenImportieren()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seiten aus PDF importieren",
            Filter = "PDF-Dateien (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog() != true)
            return;

        int index = AktuelleSeite + 1;
        int vorher = Seiten.Count;
        try
        {
            _dienst.SeitenImportieren(index, dlg.FileName);
            FelderNeuLaden();
            int hinzu = (_dienst.SeitenInfosLesen().Count) - vorher;
            AnnotationenVerschieben(s => s >= index ? s + hinzu : s);
            NachStrukturänderung(index);
            Status = $"{hinzu} Seite(n) importiert.";
        }
        catch (Exception ex)
        {
            Fehler("Die Seiten konnten nicht importiert werden.", ex);
        }
    }

    // Wirken auf die Miniatur-Mehrfachauswahl, sonst auf die aktuelle Seite
    // (siehe HauptViewModel.Seiten.cs).
    private void SeiteLöschen() => SeitenLöschen(ZielSeiten());

    private void Drehen(int delta) => SeitenDrehen(ZielSeiten(), delta);

    /// <summary>
    /// Wird vom Hauptfenster aufgerufen, wenn die Reihenfolge per Drag-and-drop
    /// geändert wurde. Verschiebt die Seite auch im PDF-Dokument.
    /// </summary>
    public void SeiteVerschoben(int altIndex, int neuIndex)
    {
        if (altIndex == neuIndex)
            return;
        try
        {
            _dienst.SeiteVerschieben(altIndex, neuIndex);
            AnnotationenVerschieben(s => IndexNachVerschieben(s, altIndex, neuIndex));
            FelderNeuLaden();
            KommentareNeuLaden();
            _seitenInfos = _dienst.SeitenInfosLesen();
            // Die Miniatur selbst mitverschieben (gleiche Index-Semantik wie
            // MovePage: nach dem Entfernen an neuIndex einfügen). Ohne dieses Move
            // bleibt die Liste optisch unverändert – die Reihenfolge "passierte" nur
            // in den Bytes, nicht sichtbar in der Miniaturansicht.
            Seiten.Move(altIndex, neuIndex);
            NummernAktualisieren();
            // Vollständig neu zeichnen, damit die Auswahl der verschobenen Seite folgt.
            AktuelleSeite = neuIndex;
            AktuelleSeiteNeu?.Invoke();
            // Wie jede Strukturänderung als ungespeichert markieren (vgl. NachStrukturänderung).
            if (!_istLaden) IstGeändert = true;
            Status = "Seitenreihenfolge geändert.";
        }
        catch (Exception ex)
        {
            Fehler("Die Reihenfolge konnte nicht geändert werden.", ex);
        }
    }

    private void ZoomÄndern(double delta)
    {
        Zoom = Math.Clamp(Zoom + delta, 0.25, 4.0);
        AktuelleSeiteNeu?.Invoke();
    }

    private void UnterschriftHinzufügen()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Unterschrift-Bild auswählen",
            Filter = "Bilder (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var u = _speicher.Hinzufügen(dlg.FileName);
            Unterschriften.Add(u);
            Status = $"Unterschrift \"{u.Name}\" importiert – im Dropdown auswaehlbar.";
        }
        catch (Exception ex)
        {
            Fehler("Die Unterschrift konnte nicht importiert werden.", ex);
        }
    }

    private void UnterschriftEinfügen(Unterschrift u)
    {
        if (!_dienst.HatDokument || AktuelleSeite < 0)
        {
            Status = "Bitte zuerst eine PDF öffnen oder eine Seite hinzufügen.";
            return;
        }
        _ausstehenderUnterschrift = u;
        EinfügeModus = EinfügeArt.Unterschrift;
        Status = "Unterschrift – auf die Stelle klicken, an der sie erscheinen soll. Esc bricht ab.";
    }

    /// <summary>Platziert die ausstehende Unterschrift an der angegebenen PDF-Koordinate (Mittelpunkt).</summary>
    public void UnterschriftAnPosition(double pdfX, double pdfY)
    {
        var u = _ausstehenderUnterschrift;
        if (u is null || AktuelleSeite < 0) { EinfügeModusBeenden(); return; }

        double breite = Math.Min(180, _seitenInfos[AktuelleSeite].Breite * 0.4);
        double höhe = breite / Bildwerkzeuge.Seitenverhältnis(u.Pfad);
        var platzierung = new UnterschriftPlatzierung(u, AktuelleSeite)
        {
            X = pdfX - breite / 2,
            Y = pdfY - höhe / 2,
            Breite = breite,
            Höhe = höhe
        };
        Platzierungen.Add(platzierung);
        EinfügeModusBeenden();
        AktuelleSeiteNeu?.Invoke();
        Status = "Unterschrift platziert – mit der Maus verschieben oder skalieren.";
    }

    /// <summary>Entfernt eine platzierte Unterschrift wieder (aus dem Hauptfenster aufgerufen).</summary>
    public void PlatzierungEntfernen(UnterschriftPlatzierung platzierung)
    {
        Platzierungen.Remove(platzierung);
        AktuelleSeiteNeu?.Invoke();
    }

    private void TextEinfügen()
    {
        if (!_dienst.HatDokument || AktuelleSeite < 0)
        {
            Status = "Bitte zuerst eine PDF öffnen oder eine Seite hinzufügen.";
            return;
        }
        EinfügeModus = EinfügeArt.Text;
        Status = "Textfeld – auf die Stelle klicken, an der es erscheinen soll. Esc bricht ab.";
    }

    /// <summary>Platziert ein Textfeld an der angegebenen PDF-Koordinate (obere linke Ecke).</summary>
    public void TextAnPosition(double pdfX, double pdfY)
    {
        if (AktuelleSeite < 0) { EinfügeModusBeenden(); return; }
        var info = _seitenInfos[AktuelleSeite];
        double breite = Math.Min(220, info.Breite * 0.6);
        const double höhe = 24;
        var notiz = new TextNotiz(AktuelleSeite)
        {
            Text = "Neuer Text",
            FontGröße = 12,
            Breite = breite,
            Höhe = höhe,
            X = pdfX,
            Y = pdfY - höhe
        };
        Textnotizen.Add(notiz);
        ZuletztEingefügteNotiz = notiz;
        EinfügeModusBeenden();
        AktuelleSeiteNeu?.Invoke();
        Status = "Text eingefügt – tippen zum Bearbeiten, Leiste zum Formatieren, Griff zum Verschieben.";
    }

    /// <summary>Entfernt eine Textnotiz wieder (aus dem Hauptfenster aufgerufen).</summary>
    public void TextNotizEntfernen(TextNotiz notiz)
    {
        Textnotizen.Remove(notiz);
        AktuelleSeiteNeu?.Invoke();
    }

    /// <summary>
    /// Aktiviert den Platziermodus für ein Symbol der angegebenen Art.
    /// Das Symbol wird beim nächsten Klick auf die Seite eingefügt.
    /// </summary>
    public void SymbolEinfügen(SymbolArt art)
    {
        if (!_dienst.HatDokument || AktuelleSeite < 0)
        {
            Status = "Bitte zuerst eine PDF öffnen oder eine Seite hinzufügen.";
            return;
        }
        _ausstehendesSymbol = art;
        EinfügeModus = EinfügeArt.Symbol;
        Status = $"{Bezeichnung(art)} – auf die Stelle klicken, an der es erscheinen soll. Esc bricht ab.";
    }

    /// <summary>Platziert das ausstehende Symbol an der angegebenen PDF-Koordinate (Mittelpunkt).</summary>
    public void SymbolAnPosition(double pdfX, double pdfY)
    {
        if (AktuelleSeite < 0) { EinfügeModusBeenden(); return; }
        SymbolArt art = _ausstehendesSymbol;
        (double breite, double höhe) = StandardGröße(art);
        var s = new Symbolmarkierung(art, AktuelleSeite)
        {
            Breite = breite,
            Höhe = höhe,
            X = pdfX - breite / 2,
            Y = pdfY - höhe / 2
        };
        Symbolmarkierungen.Add(s);
        EinfügeModusBeenden();
        AktuelleSeiteNeu?.Invoke();
        Status = $"{Bezeichnung(art)} eingefügt – mit der Maus verschieben oder skalieren.";
    }

    /// <summary>Entfernt eine Symbolmarkierung wieder (aus dem Hauptfenster aufgerufen).</summary>
    public void SymbolEntfernen(Symbolmarkierung s)
    {
        Symbolmarkierungen.Remove(s);
        AktuelleSeiteNeu?.Invoke();
    }

    /// <summary>Standardgröße (in PDF-Punkten) für eine frisch eingefügte Symbolmarkierung.</summary>
    private static (double Breite, double Höhe) StandardGröße(SymbolArt art) => art switch
    {
        SymbolArt.Umranden => (140, 44),
        SymbolArt.Durchstreichen => (140, 16),
        SymbolArt.Punkt => (16, 16),
        SymbolArt.Häkchen => (28, 28),
        _ => (26, 26) // Kreuz
    };

    private static string Bezeichnung(SymbolArt art) => art switch
    {
        SymbolArt.Kreuz => "Kreuz",
        SymbolArt.Häkchen => "Häkchen",
        SymbolArt.Punkt => "Punkt",
        SymbolArt.Umranden => "Umrandung",
        SymbolArt.Durchstreichen => "Durchstreichung",
        _ => "Markierung"
    };

    /// <summary>Entfernt eine Textmarkierung wieder (aus dem Hauptfenster aufgerufen).</summary>
    public void TextmarkierungEntfernen(Textmarkierung markierung)
    {
        Textmarkierungen.Remove(markierung);
        AktuelleSeiteNeu?.Invoke();
    }
}
