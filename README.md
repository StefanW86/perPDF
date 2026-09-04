# perPDF

**perPDF** (personalPDF) ist eine schlanke Windows-Desktop-Anwendung zum
**Betrachten und Bearbeiten von PDF-Dateien**. Oberfläche und Quellcode sind
durchgehend auf Deutsch. Das Erscheinungsbild orientiert sich am grünen
(Teal-)perPDF-Logo.

## Funktionen

- **Anzeigen** mehrseitiger PDFs mit Miniaturansicht und Zoom
- **Seiten hinzufügen** (leere A4-Seite) und **Seiten aus anderen PDFs importieren**
- **Seiten entfernen**
- **Seiten drehen** (links/rechts in 90°-Schritten)
- **Reihenfolge per Drag-and-drop** in der Miniaturansicht ändern
- **Formulare ausfüllen** – die Eingabefelder erscheinen direkt auf der Seite
- **Unterschriften einfügen** (PNG/JPG), frei verschieben und skalieren –
  der (üblicherweise weiße) Hintergrund wird automatisch transparent gemacht
- **Mehrere Unterschriften** werden dauerhaft unter
  `%AppData%\Roaming\PdfEditor\Signaturen` gespeichert
- **Text einfügen** an beliebiger Stelle – jederzeit editierbar. Über die
  schwebende Leiste lassen sich **Schriftgröße, Fett, Kursiv, Unterstrichen und
  Textfarbe** einstellen. Das Textfeld **wächst automatisch** mit der Schrift;
  zum **Verschieben** den Rahmenrand greifen. Der Rahmen erscheint nur beim
  Bearbeiten bzw. Verschieben.
- **Textmarker** als **Freihand-Strich** mit der Maus über den Text ziehen
  (kein fester Block). Der fertige Strich ist frei verschiebbar; der
  Auswahlrahmen erscheint erst beim Anklicken. Mit **erneutem Klick auf die
  Schaltfläche oder Esc** wird der Modus beendet.
- **Radiergummi** – über einen Markierungsstrich ziehen, um ihn zu entfernen.
- **Vorhandenen PDF-Text markieren & kopieren** – einfach mit der Maus über den
  Text fahren (der Zeiger wird zum Textcursor) und markieren, dann **Strg+C**.
  Kein Modus, kein Rahmen – wie in einem Texteingabefeld (zeilenweise Auswahl).
- **Unterschriften** behalten beim Skalieren das **Seitenverhältnis** bei.
- **Zoom** über die Symbolleiste oder per **Strg + Mausrad**.

## Technik

- **C# / WPF** auf **.NET 8** (`net8.0-windows`), nur für Windows 11
- Modernes Fluent-Design über **WPF-UI**
- PDF-Anzeige über **PDFtoImage** (PDFium), Bearbeitung über **PDFsharp**
- Textextraktion (für die Textauswahl) über **PdfPig**
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
`PdfEditor\bin\Release\net8.0-windows\perPDF.exe`.

## Hinweise

- „Unterschrift“ bezeichnet ein eingefügtes Bild, **keine** kryptografische
  Signatur.
- Formularwerte werden beim Speichern in die PDF geschrieben; Betrachter ohne
  automatische Formulardarstellung benötigen ggf. die Option „Formular
  fixieren“ in einem PDF-Reader, das Erscheinungsbild wird über
  `NeedAppearances` angefordert.
