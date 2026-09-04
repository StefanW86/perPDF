# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Sprache / Language

**All code, identifiers, comments, and UI text are German** — a hard convention (e.g. `Speichern`, `SeitenIndex`, `Höhe`, `Unterschrift`). Mirror the naming in the surrounding file. UI culture is fixed to `de-DE` (`<UICulture>` in the csproj).

## Dateigröße / File size

**Keine Code-Datei darf 500 Zeilen überschreiten. Wenn eine Datei dieses Limit erreicht, muss sie in logische Module aufgeteilt werden.** (No code file may exceed 500 lines; split it into logical modules when it reaches the limit.) Classes are split with C# `partial class` across files named `Typname.Aspekt.cs` (see `MainWindow.*.cs`, `ViewModels/HauptViewModel.*.cs`, `Services/PdfDokumentDienst.*.cs`). XAML view markup (`*.xaml`) is exempt.

## Build & Run

```powershell
dotnet build PdfEditor.sln -c Release
dotnet run --project PdfEditor -c Release
```

Output binary: `PdfEditor\bin\Release\net8.0-windows\win-x64\perPDF.exe`. Product is branded **perPDF**; `AssemblyName`/`Product` are `perPDF`, but project, `RootNamespace`, and namespaces remain `PdfEditor`.

- Windows-only (`net8.0-windows`, `UseWPF`); does not build on Linux/macOS.
- **No test project, linter, or CI.** "Verify" = build + exercise the app manually.

## Architecture

Single WPF project, MVVM with the deliberate exceptions below.

### Byte-array document model (most important concept)

`Services/PdfDokumentDienst.cs` (split into `.Speichern` / `.Formularerstellung` / `.Kommentare` partials) is the only code touching PDFsharp. It holds the document as a `byte[]` (`AktuelleBytes`) — the **single source of truth**. PDFsharp can't edit a `PdfDocument` after `Save`, so every op: load fresh `PdfDocument` from bytes → mutate → serialize back (`Transformieren`). Never cache/pass a live `PdfDocument` across operations.

### Two rendering concerns

1. **Rasterized page image** — `Services/PdfRenderDienst.cs` renders pages via PDFtoImage (PDFium). Full-page render uses `WithFormFill = false` on purpose (form fields are drawn as live WPF controls on top); thumbnails use `WithFormFill = true`.
2. **Editing/saving** — `PdfDokumentDienst` writes form values and bakes annotations via PDFsharp `XGraphics`.

### Annotations are pending overlays, baked only on Save

Signatures (`UnterschriftPlatzierung`), images (`BildEinfügung`), text (`TextNotiz`), and highlights (`Textmarkierung`) live as in-memory collections on `HauptViewModel`, rendered as interactive WPF controls in the overlay canvas, and **drawn into the PDF only on Save** (`PdfDokumentDienst.Speichern` → `MarkierungenZeichnen` / `BilderZeichnen` / `UnterschriftenZeichnen` / `TexteZeichnen`). After saving the collections are cleared (now permanent in the bytes). Z-order (screen and saved PDF): highlights → images → signatures → text → form fields on top.

**Inserted images (`Models/BildEinfügung.cs`, overlay `MainWindow.Bilder.cs`, VM partial `HauptViewModel.Bilder.cs`).** Toolbar "Bild einfügen" (file → click-to-place via `EinfügeArt.Bild`) or Ctrl+V (clipboard → placed centered on the current page; skipped when keyboard focus is in a `TextBoxBase`). The model keeps the original PNG (`PngBytes`; any input format is normalised via `Bildwerkzeuge.PngNormalisieren`) plus `DrehungGrad` (90°-steps CW) and four crop fractions (0..1) — **pipeline is rotate first, then crop in rotated/display space**; rotating remaps the fractions and swaps width/height around the center (`RechtsDrehen`/`LinksDrehen`). Screen preview builds `TransformedBitmap`+`CroppedBitmap` (`MainWindow.BildQuelle`, decoded originals cached in a `ConditionalWeakTable`), saving runs the identical SkiaSharp pipeline `Bildwerkzeuge.TransformiertesPng` → `XImage`. Overlay chrome (focus-gated): full-surface drag, aspect-keeping corner resize (aspect re-read per drag since crop/rotate change it), floating bar with ↺/↻ (updates the container in-place so focus/toolbar survive) and a "Zuschneiden" toggle revealing orange edge thumbs — dragging an edge trims/restores that side while the remaining content stays fixed on the page.

Two annotation editing models:
- **`TextNotiz`** carries formatting (`FontGröße`, `Fett`, `Kursiv`, `Unterstrichen`, `Farbe` as `#RRGGBB`). Overlay = a `TextBox` in a `Border` whose 5px padding is the **grab edge for moving** (drag the rim); container has **no fixed height** so it auto-grows. A floating format bar + width handle + delete chrome shows only while keyboard focus is within (`IsKeyboardFocusWithinChanged`). `Nachführen` syncs the model from the displayed rect, subtracting the grab-ring padding (container seeded `+2*rand` wider) so width doesn't drift. Format-bar buttons mutate both model and live `TextBox` (`TextStilAnwenden`) to avoid a redraw that drops focus.
- **`Textmarkierung`** = freehand stroke: `List<Point>` (PDF points) + `Strichbreite`, drawn as a `Polyline` on screen and via `XPen`/`DrawLines` in the PDF. Created by a drawing mode (`HauptViewModel.MarkerModus`, Esc to exit); `MainWindow`'s `OverlayCanvas` `PreviewMouse*` handlers capture the gesture. A finished stroke (≥2 points) is click-to-select and draggable whole (`Textmarkierung.Verschieben`).

All three implement `Models/IAufSeite` (`int SeitenIndex`), letting `HauptViewModel` shift every annotation's page when pages are inserted/deleted/imported/reordered (`AnnotationenVerschieben`, `AnnotationenBeiLöschen`, `IndexNachVerschieben`).

### Coordinate systems — `Services/SeitenGeometrie.cs`

The central source of bugs. PDF = points, origin bottom-left; WPF = DIP, origin top-left; pages rotate 0/90/180/270°. `SeitenGeometrie` converts both directions (`PunktNachAnzeige` / `AnzeigeNachPunkt` + rect variants) given a `SeitenInfo` and zoom. 1 pt = 96/72 DIP at 100%; `RenderDpi` derived so the bitmap is pixel-sharp at current zoom. Models store geometry in **PDF points**, so values survive zoom and reload.

### Form fields

`PdfDokumentDienst.FormularfelderLesen` walks the AcroForm tree (`DurchlaufeFelder`, recursive, keyed by full dotted name), resolves each widget's rect + owning page (`WidgetGeometrie`); supports Text/CheckBox/Choice only (buttons, radios, signatures skipped). On Save, values match back by full name and `/NeedAppearances` is set. `FormularHolen` swallows PDFsharp's no-form exception. The ViewModel preserves entered values across reloads by re-keying on `FeldName` (`FelderNeuLaden`).

**Creating fields (form designer).** New fields are **pending overlays** (`Models/FormularEntwurf`, mutable, `IAufSeite`) like annotations: held in `HauptViewModel.Entwurfsfelder`, drawn as dashed-border WPF controls (`FügeEntwurfHinzu`), draggable/resizable, **baked into the AcroForm on Save or when leaving `FelderBearbeitenModus`** (`EntwurfsfelderErstellen`; mode exit runs `HauptViewModel.AusstehendeFelderEinbacken` → `PdfDokumentDienst.FeldÄnderungenEinbacken`, which bakes queued deletions + covers + drafts into `AktuelleBytes` without writing the file — fields are immediately fillable), then the collections are cleared and `FelderNeuLaden` re-reads them. After sidebar option edits, `FeldGrößeAnpassen` grows (never shrinks) the field rect — height to fit all options (radio/listbox), width to fit the longest option (all three choice types; measured via `FormattedText`, Arial, pt) — anchored top-left. Toolbar "Formularfeld" dropdown sets `HauptViewModel.NeuesFeldTyp` (mutually-exclusive mode like `MarkerModus`); next page click calls `FeldAnPosition`. Selected field drives a right-hand properties sidebar (bound to `AusgewähltesFeld.*` plus the `FeldOptionenText`/`FeldMaxZeichenText` bridges and `FeldHatOptionen`/`FeldIstText`/… flags). Types: text, multiline, checkbox, **radio group** (one `FormularEntwurf` = one group), dropdown, listbox, date, number, signature. **PdfSharp 6.2.4 has no fluent field-creation API** — fields are assembled as raw `PdfDictionary` objects added to `AcroForm.Fields` + page `/Annots` (see [[pdfsharp-acroform-creation]] and `EntwurfsfelderErstellen`). Rich properties map to `/Ff` flags, `/TU`, `/DV`+`/V`, `/Q`, `/MaxLen`, `/MK /BC /BG`, `/DA`, `/Opt`, `/AA` JS. Created radio/signature fields bake correctly but **don't reappear as live overlays after reload** (`FeldTypBestimmen` maps only Text/CheckBox/Choice) — they stay fillable in any viewer.

**Editing/deleting existing fields (`FelderBearbeitenModus`).** A third mutually-exclusive toolbar mode (like `MarkerModus`). While on, supported existing fields (Text/CheckBox/Choice/**radio group**; signatures excluded — `FeldBearbeitbar`) render as dashed selectable overlays (`FügeBearbeitbaresFeldHinzu`, `MainWindow.Formularbearbeitung.cs`; radios get a static ring preview `ErstelleOptionsfeldVorschau`) with a ✕ delete button instead of fillable controls. **Edit = convert to a `FormularEntwurf` + queue the original for deletion**, reusing the whole designer pipeline: clicking a field calls `HauptViewModel.FeldBearbeiten` → `PdfDokumentDienst.FeldZuEntwurf` reads the field **faithfully** (`/TU`, `/Ff` flags, `/MaxLen`, `/Q`, `/DA` font+colour, `/MK /BC/BG`, `/Opt`, current value→`Standardwert`; date/number detected from `/AA` JS; radio options from the widgets' `/AP /N` export names) into an entwurf, queues the original, removes it from `Felder`, and adds the entwurf (now fully movable/resizable/configurable via the existing sidebar). ✕ alone = `FeldVorhandenLösen` (queue delete, no entwurf). On Save, `PdfDokumentDienst.FelderEntfernen` strips queued fields from `/Fields` + page `/Annots` **before** `EntwurfsfelderErstellen` re-bakes (an edited field re-bakes under the same name unless that name is still taken — see caveat — no `_2` suffix otherwise). `FelderNeuLaden` skips queued fields so they stay hidden across structural changes; the set is cleared on open/load and after Save.

**Field identity — `Models/FeldKennung.cs` (name is NOT a unique key).** Real-world PDFs reuse the same `/T` across different field types (e.g. this repo hit an invoice with both a radio group **and** a text field named `Feld1`). So the edit/delete queue (`_zuLöschendeFelder`) and all field lookups key on a `FeldKennung` = **name + rect** (PDF points, 0.1 pt grid; radio rect = bbox of its kid widgets — `KennungBerechnen` mirrors `FormularfelderLesen`/`OptionsfeldLesen`). The rect is stable across page reorder/insert (unlike page index). Without this, `FeldZuEntwurf` would grab the first same-named field (often the wrong type → "kann nicht bearbeitet werden") and `FelderEntfernen` would delete *all* same-named fields. Caveats: editing **re-creates** the field from modeled properties, so exotic PDF specifics (custom appearance streams, validation JS beyond date/number) are normalised; and if a duplicate same-name field still occupies the name, the re-baked edit gets a `_2` suffix (which de-duplicates the malformed input).

**Radio labels are page content → covered and redrawn by the field.** A radio group's visible labels (e.g. "Ja"/"Nein") live in the page content stream, not the field (the field only has `/AP /N` export codes). Editing a radio (`OptionsfeldBeschriftungenÜbernehmen`) reads each label's text+box from the page via `PdfTextDienst.Beschriftungen` (PdfPig: words right of each widget on the same row, up to a big gap), samples the label's background colour from the rendered page (`PdfRenderDienst.HintergrundFarben` — dominant quantised pixel colour, SkiaSharp), rewrites the entwurf's `Optionen` from export codes to the found label texts (remapping `Standardwert`), and stores one `BeschriftungsAbdeckung` (cover rect + colour, `FormularEntwurf.Abdeckungen`, index-aligned to `Optionen`; `null` where no label was found) per option. **The cover rect is the union of the widget rect and the label box** (`OptionsfeldAbdeckungenFinden`): many PDFs draw the button optics (rings/dots/brackets) as page content, which would remain as residue after the widget is deleted and the field moved. Rows without a detected label (and no-export buttons) get their button-area cover via `Seitenabdeckungen` instead, so the "don't draw an uncovered label twice" rule (`Abdeckungen[i] == null`) still holds. The entwurf rect is widened to the buttons+labels bbox (`HauptViewModel.OptionsfeldGesamtBox`, cached per field object; also used for the dashed frame in `FelderBearbeitenModus`). On Save, `OptionsfeldErstellen` fills the covers first (hiding the baked-in originals), then draws buttons and labels from `Optionen` — so renaming an option in the sidebar is WYSIWYG and labels stay attached to the field (move/resize moves them too). `FormularEntwurf.BeschriftungZeichnen(i)` gates label drawing (save *and* overlay preview): everything draws its label except an edited option whose original wasn't detected (there the uncovered original stays and a new label would double it). `FügeEntwurfHinzu` also paints the covers on screen so the editor matches the saved output. `OptionenUmbenanntNachführen` keeps `Standardwert` pointing at a renamed option. **Deleting** a radio (✕ on the existing field, or ✕ on an edited entwurf) covers the orphaned page-content labels immediately via `Models/SeitenAbdeckung` (an `IAufSeite` pending-overlay collection `HauptViewModel.Seitenabdeckungen`): purely passive rectangles on screen (`ZeichneAbdeckung`), baked first in `NachBytesBacken` (`AbdeckungenZeichnen`), cleared on load/save like the other pending collections (`AusstehendeZurücksetzen`). Caveats: cover assumes a solid background (sampled colour); cover↔option linking is index-based, so inserting/deleting option *lines* (not renaming) desyncs which options count as "covered"; per-label rich formatting is gone — labels use the field's uniform style.

### ViewModel ↔ View interaction

`HauptViewModel` (`ViewModels/`, split into `.Befehle` / `.Formularfelder` / `.Kommentare` / `.Intern` partials) owns all state, toolbar `RelayCommand`s, and the signature gallery. It does **not** build overlays; it raises two events the window subscribes to:
- `AnsichtKomplettNeu` — rebuild thumbnails + current page
- `AktuelleSeiteNeu` — redraw only the current page + overlays

`MainWindow` (split into `MainWindow.*.cs` partials) builds form-field and annotation overlays **imperatively in code-behind** (not XAML), because each control is positioned/sized via `SeitenGeometrie` and wired with drag/resize `Thumb`s + delete button (`StatteAdornerAus`). Page reorder = drag-and-drop via gong-wpf-dragdrop; the window implements `IDropTarget` → `HauptViewModel.SeiteVerschoben`.

MVVM plumbing: `ViewModels/BeobachtbaresObjekt.cs` (INotifyPropertyChanged base, `Melde` / `SetzeWert`) and `ViewModels/RelayCommand.cs` (call `AusführbarkeitAktualisieren` after state changes affecting enablement).

### Interaction modes

`HauptViewModel` exposes mutually-exclusive bool modes — `MarkerModus` (highlighter), `RadiererModus` (eraser: drag over a stroke to delete) and `FelderBearbeitenModus` (edit/delete existing form fields, see Form fields). Setting one clears the others; all gated on `HatDokument`, reset on structural changes and via Esc. The window's `InSpezialModus` (suppresses field-fill clicks + the text-selection layer) covers all three. Toolbar toggles bind `IsChecked` two-way (no commands). `MainWindow` watches via `PropertyChanged` → `ModusAktualisieren` (cursor, redraw). Marker/eraser gestures use `OverlayCanvas` `PreviewMouse*` handlers (win over child overlays); eraser hit-tests strokes geometrically (`PunktSegmentAbstand`). Ctrl+wheel on `SeitenScroll` zooms (`ZoomDurchRad`).

### Selectable PDF text (no mode/button)

When **not** in marker/eraser mode, `ZeichneAktuelleSeite` lays a transparent selection layer at the **bottom** of the overlay z-order via `FügeTextauswahlEbene`. Lines come from `PdfTextDienst` (PdfPig), cached per page in `_textCache` (cleared on `AnsichtKomplettNeu`). The layer is `Controls/TextAuswahlEbene.cs` — **one** `Canvas` over the whole page that manages selection itself (not one `TextBox` per line, which can't select across each other). It converts lines to display coords, sorts into reading order, precomputes per-char X positions (`FormattedText`, Segoe UI, scaled to line width). A drag sets an anchor→cursor `(line, char)` range; lines highlight with teal `Rectangle`s; **Ctrl+C copies line-wise**, Ctrl+A selects all. Bottom z-order keeps annotations/fields above clickable; `CaptureMouse` keeps the drag alive. Only added outside marker/eraser mode.

### Drucken (vector print)

`DruckenBefehl` (toolbar "Drucken", `Ctrl+P`) → `HauptViewModel.Drucken` bakes the **current WYSIWYG state** into temporary bytes via `PdfDokumentDienst.NachBytesBacken` (same pipeline as `Speichern`, but returns a `byte[]` from `d.Save(MemoryStream)` — **no file write, no clearing of overlay collections**). Those bytes go to `Services/DruckDienst.cs`, which shows the native WPF `System.Windows.Controls.PrintDialog` (printer/copies/range/orientation), converts its `PrintTicket` → DEVMODE (`PrintTicketConverter`), creates a GDI printer DC (`CreateDC("WINSPOOL", …)`), and prints **vectorially**: each selected page is drawn straight into the printer HDC via PDFium's GDI export `FPDF_RenderPage` (P/Invoke layer `Services/DruckInterop.cs`; flags `FPDF_ANNOT | FPDF_PRINTING`), fit-to-printable-area + centered. The bundled `pdfium.dll` (from PDFtoImage) **does export the GDI `FPDF_RenderPage`** — see [[pdfium-gdi-vektordruck]]. PDFium is **not** re-initialised (a document has already been opened/rendered, so PDFtoImage loaded it); all print calls run synchronously on the UI thread (PDFium isn't thread-safe). Caveat: form-field values relying only on `/NeedAppearances` (no `/AP`) print like the saved file would in a non-regenerating viewer — fields with appearance streams print fine; complex transparency/shadings are rasterised internally by PDFium. No new NuGet dependency, so no `ÜberFenster` license entry.

### Other services

- `Services/Bildwerkzeuge.cs` — makes near-white signature pixels transparent (threshold 235, SkiaSharp). Cached per file path.
- `Services/UnterschriftSpeicher.cs` — persists imported signatures under `%AppData%\Roaming\PdfEditor\Signaturen`; loaded into the gallery on startup.

### Branding & theme

Product **perPDF**, teal-logo colors: `#0E7C7B`/`#15A39A` (teal), `#F4A93B` (orange), `#1C2B2A` (dark), `#FBFAF7` (off-white). `App.OnStartup` sets the WPF-UI accent via `ApplicationAccentColorManager.Apply(...)`. Overlay chrome uses shared teal `Akzent`/`FeldHintergrund` brushes. Toolbar shows a vector logo (all XAML). App/window icon is `PdfEditor\perpdf.ico` (`<ApplicationIcon>` + `Window.Icon`).

## Dependencies (NuGet)

- **PdfSharp** — PDF structure editing, form fields, drawing annotations.
- **PDFtoImage** (PDFium) — rasterizing pages; pulls in SkiaSharp (also used by `Bildwerkzeuge`).
- **PdfPig** (the `PdfPig` id — *not* `UglyToad.PdfPig`, a stale/foreign id on this feed) — text extraction with line bounding boxes (`PdfTextDienst`); coords in PDF points, bottom-left, compatible with `SeitenGeometrie`.
- **WPF-UI** — Fluent design; main window derives from `Wpf.Ui.Controls.FluentWindow`.
- **gong-wpf-dragdrop** — page reordering.

Note: `XFont("Arial", …)` relies on PdfSharp's WPF font resolver, which only works inside the running WPF app (a console harness throws "No appropriate font found" unless `GlobalFontSettings.FontResolver` is set).

## Gotchas

- "Unterschrift" (signature) means an inserted **image**, not a cryptographic signature.
- After any structural page change, remap annotation `SeitenIndex` via the existing helpers — don't mutate collections ad hoc.
- Don't render the full page with `WithFormFill = true` (would double up with WPF field overlays).
- **OCR is explicitly out of scope** (per user). The selectable-text layer only exposes text the PDF actually contains (PdfPig); scanned/image-only pages have no selectable text **by design**. Do not add OCR.

## Lizenzen

When adding a NuGet dependency, update the library table in `Windows/ÜberFenster.xaml` (name, version, license type).
