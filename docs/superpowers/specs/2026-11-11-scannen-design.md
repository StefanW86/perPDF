# Scannen – Entwurf

Stand: 2026-11-11

## Absicht

Der Nutzer scannt Briefe und Dokumente, mal eine Seite, mal mehrere, und will dafür nicht in eine andere App wechseln. perPDF bekommt deshalb einen eigenen Scan-Dialog, der sich an den klassischen Windows-Dialog „Neuer Scan“ anlehnt. Das Ergebnis ist immer ein PDF: Entweder werden die Seiten an das geöffnete Dokument angehängt, oder es entsteht ein neues Dokument.

## Ziele

- Button **„Scannen“** im Ribbon-Tab **Start**, Gruppe „Datei“, öffnet den Scan-Dialog.
- Es werden die in Windows eingerichteten Scanner verwendet, ohne zusätzliche Treiber.
- Dialog-Layout wie der Windows-Dialog „Neuer Scan“: links die Einstellungen, rechts eine große Vorschaufläche, unten die Buttons **Vorschau**, **Scannen** und **Abbrechen**.
- **Scanner:** oben angezeigt, mit einem Button „Ändern…“ zum Wechseln. Der zuletzt benutzte Scanner ist beim nächsten Öffnen vorausgewählt.
- **Quelle:** nur die Optionen, die der Scanner unterstützt:
  - Flachbett
  - Einzug (einseitig)
  - Einzug (beidseitig), nur wenn der Scanner Duplex kann
- **Papierformat:** A4 (Standard), A5, Letter. Gescannt wird immer die ganze Seite in diesem Format.
- **Farbmodus:** Farbe, Graustufen, Schwarzweiß. Standard ist Graustufen.
- **Auflösung (DPI):** Standard 300.
- **Helligkeit** und **Kontrast:** je Schieberegler mit Zahlenfeld, Standard 0.
- Alle Einstellungen (Scanner, Quelle, Papierformat, Farbmodus, Auflösung, Helligkeit, Kontrast) werden gespeichert und beim nächsten Öffnen wiederhergestellt.
- **Vorschau** scannt eine Seite in niedriger Auflösung und zeigt sie rechts an. Sie dient nur zur Kontrolle und wird nicht ins PDF übernommen.
- **Scannen** scannt mit den gewählten Einstellungen. Beim Einzug kommen alle eingelegten Blätter (bei Duplex Vorder- und Rückseiten) in einem Durchgang.
- Nach **jedem** Scan-Durchgang, auch nach einem Einzug, fragt der Dialog: „Weitere Seiten scannen?“ bzw. „Fertig“. Bei „Weitere“ kann der Nutzer neue Seiten oder einen weiteren Stapel einlegen und erneut scannen. Dazwischen darf er Einstellungen ändern, z. B. die Quelle.
- Die Seiten erscheinen im PDF **in Scanreihenfolge**, über alle Durchgänge hinweg.
- Bei „Fertig“:
  - **Ist ein PDF geöffnet:** Abfrage, ob die gescannten Seiten **an das geöffnete Dokument angehängt** werden (am Ende) oder ein **neues Dokument** entsteht.
  - **Ist keins geöffnet:** Es entsteht ein neues Dokument.
  - Ein neues Dokument öffnet sich in einem **neuen Tab**, ohne Dateipfad. „Speichern“ fragt dann wie gewohnt nach dem Speicherort.

## Nicht-Ziele

- **Kein „Profil“**, kein Dateityp, keine Checkbox „als separate Dateien“. Es entsteht immer ein PDF.
- **Kein Ausschnitt** in der Vorschau. Es wird immer die ganze Seite gescannt.
- **Keine OCR.** Das ist laut Projektvorgaben ausdrücklich ausgeschlossen. Gescannte Seiten haben keinen auswählbaren Text.
- Keine automatische Leerseiten-Erkennung, kein automatisches Geraderichten, kein Zuschneiden.
- Kein TWAIN, keine Netzwerk-/eSCL-Scanner, die Windows nicht als WIA-Gerät führt.

## Anti-Ziele (wäre ein Fehlschlag, selbst wenn es technisch funktioniert)

- Gescannte Seiten in falscher Reihenfolge.
- Der Scan blockiert die App so, dass sie „Keine Rückmeldung“ zeigt.
- Seiten, die schon gescannt sind, gehen durch ein Versehen verloren (z. B. Abbrechen ohne Rückfrage).
- Riesige PDFs: Eine A4-Seite in 300 DPI Graustufen soll im Bereich von einigen hundert KB liegen, nicht bei mehreren MB.
- Ein angehängter Scan verändert oder verliert offene, noch nicht gespeicherte Änderungen am Dokument (Annotationen, Formularwerte).

## Rahmenbedingungen

- Projektkonventionen aus `CLAUDE.md`: alles Deutsch (Code, Bezeichner, UI), keine Code-Datei über 500 Zeilen (sonst `partial class`), byte-array-Dokumentmodell (`PdfDokumentDienst.AktuelleBytes` ist die einzige Wahrheit, Änderungen laufen über `Transformieren`).
- Das Dialogfenster ist ein `Wpf.Ui.Controls.FluentWindow` wie die vorhandenen Fenster in `Windows/` (z. B. `ZusammenführenFenster`) und unterstützt Hell- und Dunkelmodus.
- Die App wird als MSIX verteilt. Der Scan-Zugriff muss mit der vorhandenen Capability `runFullTrust` funktionieren, ohne neue Restricted Capabilities.
- Keine neue NuGet-Abhängigkeit, wenn es sich vermeiden lässt. Falls doch eine hinzukommt, braucht sie einen Eintrag in `Windows/ÜberFenster.xaml`.
- Es gibt keine Testprojekte. „Verifizieren“ heißt: bauen und mit einem echten Scanner von Hand durchspielen (Flachbett, Einzug, Duplex, falls vorhanden).

## Bereits festgelegte Teile des Wie

- **Scanner-Schnittstelle: WIA 2.0** (Windows Image Acquisition, `WIA.DeviceManager`), über die auch die Windows-Scan-Apps laufen. *(Meine Wahl, vom Nutzer nicht widersprochen.)*
- **Einstellungen** werden als JSON unter `%AppData%\Roaming\perPDF\scan.json` gespeichert. Muster ist `Services/FensterEinstellungen.cs`: Fehler beim Lesen führen zu Standardwerten, Fehler beim Schreiben werden ignoriert. Unter MSIX ist das package-privat virtualisiert, das ist in Ordnung.
- **Neues Dokument:** neuer Tab über `MainWindow.TabNeuErstellen()`, danach werden die PDF-Bytes in dessen ViewModel geladen, mit `Pfad = null`. So greift beim Speichern automatisch der „Speichern unter“-Dialog. Das vorhandene `BytesLaden` setzt den Pfad auf `_dienst.Pfad ?? "zusammengeführt.pdf"`. Für den Scan wird eine Variante ohne Pfad gebraucht, und die Statusmeldung muss passen (nicht „Zusammengeführtes Dokument geladen“). Der Tab ist als geändert markiert, damit beim Schließen nachgefragt wird.
- **Anhängen:** neue Byte-Transformation in `PdfDokumentDienst` analog zu `SeitenImportieren`, die die Scan-Seiten aus einem PDF-`byte[]` (statt aus einem Dateipfad) **ans Ende** einfügt. Ausstehende Overlays (Annotationen, Entwürfe, Formularwerte) bleiben erhalten, weil sich beim Anhängen am Ende kein vorhandener Seitenindex verschiebt. Danach `FelderNeuLaden()` und `NachStrukturänderung(...)`, Statuszeile „n Seite(n) gescannt und angehängt.“, Dokument als geändert markiert.

## Dem Umsetzenden überlassen

- Aufteilung in Dateien. Vorschlag: `Windows/ScanFenster.xaml(.cs)`, `Services/ScanDienst.cs` (WIA-Zugriff), `Services/ScanEinstellungen.cs` (JSON) und die PDF-Erzeugung aus den Seitenbildern, entweder in `ScanDienst` oder als kleiner Helfer.
- WIA-Anbindung: per COM-Referenz auf `wiaaut.dll` oder spät gebunden über `Type.GetTypeFromProgID("WIA.DeviceManager")` mit `dynamic`. Hauptsache, es kommt keine zusätzliche DLL in die Auslieferung, die die MSIX-Zertifizierung stört.
- Wie Duplex- und Einzugsfähigkeit erkannt werden (WIA-Eigenschaften „Document Handling Capabilities/Select“) und wie der Einzug bis „Papierfach leer“ ausgelesen wird.
- Die Scans laufen auf einem eigenen STA-Thread, damit die Oberfläche reagiert. Während eines Scans sind die Buttons gesperrt, und es gibt eine Fortschritts- bzw. „Scannt…“-Anzeige.
- Bildkompression im PDF: Farbe und Graustufen als JPEG (Qualität ca. 80–85), Schwarzweiß verlustfrei. Die Seitengröße im PDF ergibt sich aus Pixeln ÷ DPI, sodass A4 als A4 erscheint. SkiaSharp und PDFsharp (`XImage`) sind vorhanden.
- Fehlerfälle mit verständlicher deutscher Meldung und ohne Absturz: kein Scanner gefunden, Scanner belegt oder ausgeschaltet, Einzug leer, gespeicherter Scanner nicht mehr vorhanden (dann den ersten verfügbaren vorschlagen).
- Wie die Anzahl bereits gescannter Seiten im Dialog sichtbar ist, z. B. „3 Seiten gescannt“, und ob die Vorschaufläche nach einem Scan die zuletzt gescannte Seite zeigt.
- Rückfrage bei „Abbrechen“, wenn schon Seiten gescannt sind („Gescannte Seiten verwerfen?“).

## Entscheidungen nach Prüfung (meine Wahl, vom Nutzer nicht ausdrücklich bestätigt)

- **Vorschau nur bei Quelle Flachbett.** Beim Einzug ist „Vorschau“ deaktiviert, mit Tooltip „Vorschau nur mit Flachbett möglich“. Sonst würde ein Blatt durchgezogen und fehlte danach im Scan. So macht es auch der Windows-Dialog.
- **Fehler mitten im Einzug** (Papierstau, Scanner aus): Die bis dahin gelieferten Seiten werden **behalten**. Die Meldung nennt die Zahl, z. B. „Fehler nach Seite 4. Die 4 gescannten Seiten wurden übernommen. Bitte die restlichen Blätter neu einlegen.“ Danach folgt die normale Frage „Weitere Seiten scannen?“. „Fach leer“ nach mindestens einer Seite gilt als normales Ende, „Fach leer“ ohne eine einzige Seite als Hinweis „Kein Papier im Einzug“.
- **„Geöffnetes Dokument“ = der aktive Tab, sofern er ein Dokument hat.** Ist der aktive Tab leer, entsteht das neue Dokument direkt in diesem leeren Tab statt in einem weiteren. Andere Tabs spielen keine Rolle. Der Scan-Button ist gesperrt, solange der aktive Tab lädt.
- **Schwarzweiß:** Ist 1-Bit verlustfrei mit PDFsharp nicht kompakt einzubetten, ist als Ausweichlösung ein verlustfreies 8-Bit-Graustufenbild (Flate) mit nur zwei Tonwerten zulässig. Das Größenziel von „einigen hundert KB“ gilt verbindlich für Graustufen und Farbe, für Schwarzweiß nur als Richtwert.
- **Speicherort der Einstellungen:** `%AppData%\Roaming\perPDF\scan.json` ist Absicht. Der Ordner `perPDF` wird schon von `FensterEinstellungen` genutzt, `PdfEditor\Signaturen` ist Altbestand.
