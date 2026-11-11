# perPDF

**perPDF** (personalPDF) ist eine schlanke Windows-Desktop-Anwendung zum
**Betrachten und Bearbeiten von PDF-Dateien**. Oberfläche und Quellcode sind
durchgehend auf Deutsch. Das Erscheinungsbild orientiert sich am grünen
(Teal-)perPDF-Logo.

## Funktionen

**Dokumente & Seiten**
- **Mehrere PDFs in Tabs** öffnen – per Dialog oder Drag-and-drop ins Fenster
- Miniaturansicht, Inhaltsverzeichnis, **Zoom** (Symbolleiste oder **Strg + Mausrad**)
- **Volltextsuche** im Dokument (**Strg+F**)
- **Seiten hinzufügen, importieren, löschen** (auch per Seitenbereich, z. B. `3-10`)
  und **drehen** (90°-Schritte)
- **Reihenfolge per Drag-and-drop** in der Miniaturansicht ändern
- **PDFs zusammenführen** und **Seiten als neues PDF extrahieren**
- **Drucken** (**Strg+P**) – vektoriell, mit allen Anmerkungen

**Scannen**
- **Direkt aus perPDF scannen** – über WIA, auch mit Netzwerkscannern
- Vorlagenglas oder **Einzug**, Papierformat, **Farbe / Graustufen /
  Schwarzweiß**, Auflösung (DPI), Helligkeit und Kontrast
- **Vorschau** (Vorlagenglas) und **mehrseitige Scans**: nach jedem Durchgang
  „Weitere scannen“ oder „Fertig“
- Scan **an das geöffnete Dokument anhängen** oder als **neues Dokument**
  öffnen – neue Scans werden automatisch als PDF im Scan-Ordner gespeichert
  (Standard: `Dokumente\Scans\SCAN_{Datum}_{Zeit}.pdf`, unter *Optionen*
  anpassbar)

**Anmerken**
- **Text einfügen** mit Schriftgröße, Fett, Kursiv, Unterstrichen und Textfarbe
- **Textmarker** und **Stift** als Freihand-Striche, **Radiergummi** zum Entfernen
- **Symbole**: Kreuz, Häkchen, Punkt, Umrandung, Durchstreichung
- **Bilder einfügen** aus Datei oder per **Strg+V** aus der Zwischenablage –
  drehen und zuschneiden direkt auf der Seite
- **Unterschriften** (PNG/JPG) mit automatisch transparentem Hintergrund;
  sie werden dauerhaft unter `%AppData%\Roaming\PdfEditor\Signaturen` gespeichert
- **Kommentare**: Text markieren, per Rechtsklick kommentieren, beantworten und
  als erledigt/abgelehnt markieren

**Formulare**
- **Formulare ausfüllen** – die Eingabefelder erscheinen direkt auf der Seite
- **Formular-Designer**: Textfeld, mehrzeiliges Textfeld, Kontrollkästchen,
  Optionsfelder, Dropdown, Listenfeld, Datums-, Zahlen- und Unterschriftsfeld
  anlegen – mit Eigenschaften wie Pflichtfeld, Standardwert, Tooltip, Farben,
  max. Zeichen
- **Vorhandene Felder bearbeiten**: verschieben, Eigenschaften ändern oder löschen

**Sonstiges**
- **Vorhandenen PDF-Text markieren & kopieren** (**Strg+C**) – ohne eigenen Modus,
  **Doppelklick markiert ein Wort**
- **Helles und dunkles Design**
- Kostenlos, ohne Konto, ohne Werbung

## Technik

- **C# / WPF** auf **.NET 8** (`net8.0-windows`, `win-x64`)
- Modernes Fluent-Design über **WPF-UI**
- PDF-Anzeige über **PDFtoImage** (PDFium), Bearbeitung über **PDFsharp**
- Textextraktion (für die Textauswahl) über **PdfPig**
- Scannen über **WIA 2.0** (Windows Image Acquisition, in Windows enthalten)
- Drag-and-drop über **gong-wpf-dragdrop**

## Voraussetzungen

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (nur zum Bauen)

## Bauen und starten

```powershell
dotnet build PdfEditor.sln -c Release
dotnet run --project PdfEditor -c Release
```

Die fertige Anwendung liegt anschließend unter
`PdfEditor\bin\Release\net8.0-windows\win-x64\perPDF.exe`.

Die Versionsnummer stammt ausschließlich aus dem Git-Tag `vX.Y.Z` – ohne
erreichbaren Tag schlägt der Build fehl.

## Installation

### Microsoft Store (empfohlen)

perPDF wird im **Microsoft Store** bereitgestellt – dort ist die App von
Microsoft **signiert**, wird automatisch aktualisiert und lässt sich ohne
Administratorrechte installieren:
[perPDF im Microsoft Store](https://apps.microsoft.com/detail/9NZQ8GX88724)

## Hinweise

- „Unterschrift“ bezeichnet ein eingefügtes Bild, **keine** kryptografische
  Signatur.
- Formularwerte werden beim Speichern in die PDF geschrieben; Betrachter ohne
  automatische Formulardarstellung benötigen ggf. die Option „Formular
  fixieren“ in einem PDF-Reader, das Erscheinungsbild wird über
  `NeedAppearances` angefordert.
