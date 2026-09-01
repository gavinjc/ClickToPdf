# PageMerge — Technical Specification

**Working title:** PageMerge *(name not yet reserved — see §9)*
**Target:** Windows 11 (22H2+), Windows 10 20H1+ as secondary
**Distribution:** Microsoft Store, free
**Status:** greenfield, no existing code

---

## 1. Problem statement

Scanning a multi-page document on a flatbed or phone produces a folder of JPGs. Turning
those into a single PDF to email someone is currently either:

- **Windows built-in** (select files → right-click → Print → *Microsoft Print to PDF*) —
  works, but ordering is locked to filename order, "Full page photo" crops the page edges,
  landscape scans get letterboxed, and the output is a re-rasterised photo print rather than
  a document.
- **PDF24 / Acrobat** — capable, but heavyweight installs. PDF24's quick merge dialog lists
  **filenames only**, which is useless for `IMG_0043.jpg … IMG_0051.jpg`. Getting a visual
  reorder means merging first, then reopening the result in a full PDF editor.

Neither gives the one thing that matters: **see the pages, drag them into order, done.**

## 2. Product definition

A single-purpose utility. Select image files in File Explorer → right-click → **Combine to
PDF** → a window opens showing readable thumbnails in a reorderable grid → adjust order and
rotation → Save.

**The thumbnail reorder grid is the product.** Every other feature is optional polish and
must not complicate that flow.

### Explicit non-goals for v1

- No PDF editor, viewer, splitter, or page extractor
- No OCR (see §8 — deliberately deferred)
- No auto-crop / border removal (deskew shipped — see §8 item 2)
- No conversion of Office documents, HTML, or other non-image formats
- No cloud, no accounts, no telemetry, no auto-updater (Store handles updates)

If a feature request would add a second window, it is out of scope.

---

## 3. Architecture

Three components in one MSIX package.

```
PageMerge.sln
├── PageMerge.ShellExtension/    C++/WinRT DLL — IExplorerCommand, launches the app
├── PageMerge.App/               C# / .NET 8 / WinUI 3 — the actual UI and PDF writing
└── PageMerge.Package/           Windows Application Packaging Project → MSIX
```

### 3.1 Why this split

The Windows 11 *main* context menu (not the "Show more options" legacy menu) requires an
`IExplorerCommand` implementation registered through an MSIX manifest. The extension DLL is
loaded by Explorer itself, so it must be small, native, fast, and must never block. It
therefore does **nothing** except collect the selected paths and start the main app.

All real work — imaging, UI, PDF generation — lives in the C# app, where it is easy to write
and cannot destabilise Explorer.

Because we ship a **full MSIX** (not a sparse package), the app has package identity
natively. No separate sparse-package dance, and no purchased code-signing certificate:
the Store re-signs MSIX packages during certification.

### 3.2 Technology choices

| Concern | Choice | Rationale |
|---|---|---|
| Shell extension | Native C++ (WRL), `IExplorerCommand` | Required for Win11 main menu; must be native in-proc. The reference implementation below uses `Microsoft::WRL::RuntimeClass`, not literal C++/WinRT (`winrt::implements`) projections — WRL needs no NuGet header-generation step and gives one-line `DllGetClassObject`/`DllCanUnloadNow` via `CoCreatableClass` |
| App UI | WinUI 3 (Windows App SDK 1.6+), .NET 8 | `GridView` gives drag-reorder essentially free; matches Win11 visuals; Store-friendly |
| Image decode | `Windows.Graphics.Imaging.BitmapDecoder` (WinRT) | Native, no licensing questions, handles EXIF orientation, fast embedded-thumbnail path |
| PDF writing | PDFsharp 6.x (MIT) | Simple image→page API, and critically supports **JPEG passthrough** |
| Packaging | Windows Application Packaging Project | Standard MSIX output for Partner Center |

**Do not** use System.Drawing.Common (legacy, Windows-only warnings) or ImageSharp
(licence complications for a Store app). Reference implementation for the shell extension:
[`microsoft/vscode-explorer-command`](https://github.com/microsoft/vscode-explorer-command).

---

## 4. Component specs

### 4.1 Shell extension (`PageMerge.ShellExtension`)

Implements `IExplorerCommand`. (`IObjectWithSelection` is not needed — `GetState` and
`Invoke` both receive the current selection directly as an `IShellItemArray*` parameter,
per the reference implementation below.)

**Registration** — declared in the package manifest under
`desktop4:FileExplorerContextMenus`, associated with these file types:
`.jpg .jpeg .png .heic .tif .tiff .bmp .webp .gif`

GIF is included alongside the others because it decodes through the same WIC
`BitmapDecoder` path at no extra cost — no new decode logic, just an extension-list entry.
This is the ceiling for "free" formats: everything else WIC decodes natively for still
images is already on the list.

**Visibility logic** (`GetState`):

- Return `ECS_ENABLED` when **at least one** selected item is usable — a supported image
  type, or a folder that directly (non-recursively) contains at least one. The rest of the
  selection is not required to match; unusable items are silently excluded and reported to
  the user by the app on launch (see Invoke below) rather than hiding the whole command for
  one bad file. Registered under `AllFilesystemObjects` (not per-extension), since a verb
  registered only under specific extensions' `SystemFileAssociations` is pruned by
  Explorer's multi-selection merge logic unless *every* selected item's type also has it
  registered — which would make a mixed selection never even reach `GetState`.
- Return `ECS_HIDDEN` when nothing in the selection qualifies — e.g. selection is empty, or
  every item is either an unsupported file or a folder with no supported files inside it.
- Single-file selection is valid (single-page PDF is a legitimate use).

**Title** (`GetTitle`): `Combine to PDF` for 2+ selected items, `Convert to PDF` for exactly 1
(counts the raw selection, not the expanded page count from an included folder).

**Icon** (`GetIcon`): package-relative path to the app icon.

**Invoke** — must return in **well under 100 ms**. It:

1. Enumerates the `IShellItemArray` into a list of full paths, classifying each: a supported
   file is used as-is; a supported folder (per the `GetState` definition above) is expanded
   into its directly-contained supported files; anything else is recorded by filename as
   skipped and excluded.
2. Writes the resulting supported-file paths, UTF-8, one per line, to a temp file:
   `%LOCALAPPDATA%\Temp\PageMerge\{guid}.list`
3. Launches the app: `PageMerge.exe --list "<path-to-list-file>"`
4. Returns `S_OK`.

> **Do not pass file paths directly on the command line.** A 60-page scan folder will blow
> past the ~32 000 character command-line limit. The temp-file handshake is not optional.
> The app deletes the list file after reading it.

### 4.2 Main app (`PageMerge.App`)

Single window. No menu bar, no ribbon, no settings dialog beyond an inline expander.

#### Startup

- Parse `--list <path>`; read and delete the list file.
- If launched with no arguments (e.g. from Start menu), show an empty state with a
  "Choose images…" button and accept drag-and-drop of files onto the window.
- Begin async thumbnail loading immediately; the window must appear within ~300 ms with
  placeholders, not wait for decoding.

#### Initial ordering

Explorer's selection order is **not** reliable. Default to **natural sort** on filename —
i.e. `IMG_2.jpg` before `IMG_10.jpg` (a plain string sort gets this wrong, and scanner
output hits this constantly).

Provide a `Sort` split-button offering: *Name (natural)*, *Date taken*, *Date modified*,
*Reverse current*. Remember the last-used choice.

#### Main view

A virtualised `GridView`:

```xml
<GridView CanReorderItems="True" AllowDrop="True" CanDragItems="True"
          SelectionMode="Extended" />
```

Each tile shows:

- The page thumbnail, **at least 180 px on the long edge** — large enough to identify a page
  at a glance. This is the whole point of the app; do not shrink it to fit more tiles.
- A page number badge (position in output, updates live on reorder).
- The source filename, single line, ellipsised, secondary text style.
- On hover: a rotate-right button and a remove (×) button.

A zoom slider adjusts tile size between roughly 120 px and 400 px. Persist the setting.

#### Interactions

| Action | Behaviour |
|---|---|
| Drag tile | Reorder; multi-select drag moves the whole group |
| `Ctrl` + `←`/`→` | Move selected tile(s) one position |
| `R` / `Shift`+`R` | Rotate selected 90° CW / CCW |
| `Delete` | Remove selected from the set (does **not** touch the file on disk) |
| `Ctrl`+`Z` | Undo last reorder / rotate / remove — at least 20 levels |
| Double-click | Full-size preview overlay; `Esc` closes |
| `Ctrl`+`A` | Select all |
| `Enter` | Save |

**Rotation is per-page and non-destructive.** It affects PDF output only; source JPGs are
never modified. The app must never write to, move, or delete the user's source files.

#### Output options (inline expander, collapsed by default)

- **Page size:** A4 *(default)*, Letter, or *Match image* (page = image aspect, no margins)
- **Margins:** None *(default)*, Narrow (5 mm), Standard (10 mm)
- **Orientation:** Auto per page *(default — landscape images get landscape pages)*, Force portrait
- **Quality:** Original *(default, see §5)*, High (300 DPI), Medium (200 DPI), Small (150 DPI)
- **Greyscale:** off by default

Defaults must produce a good result with zero interaction. A user who never opens the
expander should still get a clean, correctly-oriented, sensibly-sized A4 document.

#### Save

- `FileSavePicker` defaulting to the source folder.
- Default filename: always the generic `Scan_yyyy-MM-dd.pdf`, appending ` (2)` etc. on
  collision — deliberately *not* derived from the source folder name or an image filename.
  A name that matches a folder visible wherever the user saves (e.g. saving into the source
  folder's parent, where a same-named folder sits right there) triggers a real Windows
  Save-dialog behavior: the dialog auto-selects the matching folder, and Enter/Save then
  opens it instead of committing the filename. Confirmed as a general Windows common-dialog
  behavior (not specific to any one folder name), not something fixable via the public
  `FileSavePicker` API — a generic date-stamped name sidesteps it entirely rather than just
  reducing the odds.
- Progress bar for sets over 10 pages, cancellable.
- On success: a toast/InfoBar with **Open** and **Show in folder** actions. Do not close the
  window automatically — users often save a second variant.

### 4.3 PDF generation

For each page, in display order:

1. Decode the source (or reuse the already-decoded stream).
2. Apply EXIF orientation, then the user's rotation delta.
3. Choose page dimensions per the size/orientation settings.
4. Scale the image to **fit** inside the page minus margins, **preserving aspect ratio**.
   Centre it. **Never crop.** (This is the specific failure of Windows' "Full page photo".)
5. Draw and move to the next page.

**JPEG passthrough is a hard requirement.** When the source is a JPEG, quality is set to
*Original*, and no rotation-by-non-multiple-of-90 or greyscale conversion is needed, embed
the original JPEG bytes into the PDF *without re-encoding*. Re-encoding scans visibly
degrades text and inflates file size. PDFsharp supports this via `XImage.FromStream` on JPEG
data — verify it is actually taking that path and not silently re-compressing.

Set PDF metadata: `Title` = output filename, `Producer` = `PageMerge`, `CreationDate` = now.
Do not embed the user's name or source paths.

---

## 5. Performance requirements

Test set: **40 JPGs, ~3 MB each, 3500 × 2500 px** (typical 300 DPI A4 flatbed output).

| Metric | Target |
|---|---|
| Window visible after invoke | < 500 ms |
| All 40 thumbnails rendered | < 3 s |
| Reorder drag | 60 fps, no stutter |
| PDF write (Original quality) | < 5 s |
| Peak memory | < 500 MB |

Implementation notes:

- Load thumbnails via `BitmapDecoder.GetThumbnailAsync` / `GetPreviewAsync` where the JPEG
  has an embedded thumbnail — orders of magnitude faster than full decode.
- Decode thumbnails on a bounded background queue (≈4 concurrent), prioritising visible tiles.
- Never hold full-resolution bitmaps for all pages simultaneously; stream during PDF write.
- The UI thread must never block on file I/O.

---

## 6. Build milestones

Build and verify in this order. Each milestone should be independently runnable.

**M1 — Core PDF engine (console).**
A console app taking a list of image paths and emitting a PDF. Correct fitting, auto-rotate,
EXIF handling, JPEG passthrough. *This is the milestone most likely to disappoint — validate
output quality against real scans before building any UI.*

**M2 — WinUI 3 shell.**
Window, virtualised GridView, async thumbnails, drag-reorder, rotate, remove, undo.
Launched manually with a folder path. Meets the §5 performance targets.

**M3 — Save flow.**
Options expander, save picker, naming rules, progress, cancellation, success InfoBar.

**M4 — Shell extension.**
Native `IExplorerCommand` COM DLL (WRL, not literal C++/WinRT projections — see the
reference implementation in §3.2), temp-file handshake, correct show/hide logic. Locally
registered via per-user (HKCU) registry keys for dev testing — no admin, fully reversible.
*Per §3.1, main-menu placement itself requires MSIX manifest registration, which doesn't
exist until M5. Expect this local registration to surface under "Show more options";
verify it appears in the **main** Win11 context menu only after M5 packages the manifest
extension.*

**M5 — MSIX packaging.**
Packaging project, manifest with the context-menu extension, assets at all required scales,
self-signed cert for local sideload testing.

**M6 — Store readiness.**
Store-compliant listing assets, privacy policy page, age-rating answers, final validation
via the Windows App Certification Kit.

---

## 7. Acceptance criteria

- [x] Selecting 1–100 JPGs in Explorer shows **Combine to PDF** in the Win11 main context
      menu (verified after M5's MSIX manifest registration — see M4 note in §6)
- [x] Menu entry is hidden when the selection contains a non-image file
- [x] 60 selected files launch correctly (command-line length regression guard)
- [x] Thumbnails are legible enough to distinguish two similar scanned pages
- [x] Drag-reorder updates page numbers live (badges update on drop, not a continuous
      mid-drag renumber — confirmed as the intended reading of "live" here)
- [x] Rotation persists into output and never modifies the source file
- [x] A4 output: full page visible, aspect preserved, nothing cropped
- [x] Landscape source produces a landscape page under Auto orientation
- [x] JPEG passthrough — output size is within ~10% of the sum of source file sizes at Original quality
- [x] EXIF-rotated phone photos appear upright
- [x] Mixed portrait/landscape sets produce correct per-page orientation
- [x] Single-file selection produces a valid single-page PDF
- [x] Source files are byte-identical after every operation
- [x] Meets all §5 performance targets
- [x] App handles a corrupt/unreadable image with a per-tile error state, not a crash
- [ ] Passes Windows App Certification Kit

---

## 8. Deliberately deferred

Candidates for v1.1+, in rough priority order. **None of these belong in v1** — they exist
here so the architecture doesn't preclude them.

1. **OCR text layer ("searchable PDF")** — an invisible text layer drawn behind the existing
   page image, positioned from `Windows.Media.Ocr` word bounding boxes, so the page looks
   unchanged but becomes selectable/searchable. Has a genuine MVP/hard-tail split, since the
   invisible text only needs to be roughly positioned, not pixel-perfect:
   - **MVP (bounded) — *(implemented)*** — clean single-column printed text in the user's
     Windows display language, normal scan quality: call the engine per page, draw the
     returned words as invisible text, done. No preprocessing, no reading-order logic, no
     per-document config. `Img2PDF.Core.Ocr.PageOcr` rotates the source pixels to the page's
     final display rotation before recognizing (a plain 90°-multiple correction, not deskew —
     skipping it measurably hurt real-world accuracy in testing), and each word is drawn via
     an alpha-0 `XFont`/`DrawString` in `PdfComposer` — invisible but still text-extractable in
     any reader, confirmed against real output with `pdftotext`. Opt-in via the "Make
     searchable (OCR)" checkbox (`PdfOptions.Searchable`, off by default) and Cli's
     `--searchable` flag.
   - **Hard tail (open-ended)** — handwriting (the engine isn't built for it), heavy
     skew/low-contrast scans (needs real preprocessing, overlaps item 2), non-primary-language
     or mixed-language pages (needs the matching OCR language pack installed, silently, like
     the HEIF codec gap), and multi-column/dense layouts (words still recognize, but reading
     order can scramble).
   - Failure mode either way is soft: a hard-tail page just ends up with no/partial searchable
     text, never a crash or blocked output — so MVP-first, harden-later is a legitimate way to
     ship this rather than an all-or-nothing gate.
   - **In-app disclaimer shipped 2026-08-24:** the "Make searchable (OCR)" checkbox carries an
     asterisk (`SearchableCheckBox.Content`, `Strings/en-us/Resources.resw`) pointing at a fixed
     disclaimer line under the Options panel (`OcrHandwritingDisclaimer`): "OCR recognizes
     printed text only — handwriting isn't supported and may produce garbled or no searchable
     text." **TODO before store submission:** the same disclaimer still needs to reach the Store
     listing copy (`docs/store/listing.md`, near the "Make the PDF searchable" bullet).
   - **Real finding, worth keeping:** a *post-save* "some pages weren't recognized" warning was
     attempted and reverted the same day. `Windows.Media.Ocr` does not fail cleanly on
     handwriting — confirmed by running it against a real 10-page handwritten scan and
     inspecting the embedded text via `pdftotext`: it returned confident, plausible-looking
     garbage (`cyo rccecLwc/`, `CJN`, `SbUS(z`, ...) rather than an empty result, so a
     "zero words recognized" per-page heuristic never fires for the actual common case. The
     WinRT OCR API also exposes no per-word/line confidence score to build a better heuristic
     from. A static, always-shown disclaimer (above) is deliberately preferred over detection
     that cannot reliably detect the failure it's meant to catch — don't re-attempt a
     post-save "OCR outcome" heuristic without a genuinely new signal to build it from.
2. **Deskew** *(implemented)* **/ auto-crop** *(still deferred)* — high value for flatbed
   scans. Deskew reuses the `Searchable` feature's `Windows.Media.Ocr` call: `OcrResult.TextAngle`
   (clockwise degrees, verified against Microsoft's docs) is folded straight into the existing
   90°-multiple rotation as one extra graphics-transform angle, plus a compensating uniform
   scale-down (`PdfComposer.ComputeDeskewCompensatingScale`) so the larger rotated bounding box
   never overhangs the page's already-fitted image rect. Because it's a pure content-stream
   rotation, not a pixel resample, it does **not** force JPEG passthrough off — `/DCTDecode`
   survives deskew just like it survives the coarse rotation. Same soft-fail contract as
   `Searchable`: no detectable text (or no OCR language pack) just means no correction, never a
   crash or blocked save. Only corrects a single dominant rotation angle — no perspective/shear
   correction, so camera-photo keystoning isn't addressed, only flatbed-style pure skew. Opt-in
   via the "Auto-deskew" checkbox (`PdfOptions.Deskew`, off by default) and Cli's `--deskew` flag;
   combines with `Searchable` (both share one OCR call per page, and the text layer gets the same
   extra rotation/scale so it stays aligned with the now-upright image). Auto-crop is unrelated
   work (trimming empty page margins) and remains deferred — needs real-world test images.
3. **Blank page detection** — flag likely-blank reverse sides for one-click removal.
4. **Duplex interleave** — merge an odds pass and a reverse-order evens pass. Niche but the
   people who need it *really* need it.
5. **Save profile presets** — e.g. "Email (small)" vs "Archive (original)".
6. **Lossless passthrough for non-JPEG formats** — *(implemented)* at Original quality, a
   JPEG source embeds untouched (true byte-identical passthrough); every other source
   (PNG, GIF, BMP, TIFF, WEBP, HEIC, or a JPEG needing greyscale) now re-encodes losslessly
   as PNG at native resolution instead of the old lossy-JPEG-at-300dpi behavior. PDF has no
   filter that maps to non-JPEG source formats directly (unlike JPEG → `/DCTDecode`), so
   "passthrough" here means no quality loss, not literally the original bytes — verified via
   the embedded image XObject's actual `/Filter` entry (`/FlateDecode`, with a real alpha
   `/SMask` for transparent PNGs), not just file size. The High/Medium/Small quality tiers
   are unaffected — those still resample and recompress as lossy JPEG by design.
7. **HEIC decode fallback messaging** — *(implemented)* if WIC can't find a HEIF-capable
   codec (extension not installed), the tile shows a friendly tooltip pointing at the
   Store's free "HEIF Image Extensions" rather than a bare "Can't load". Deliberately does
   not bundle an HEIC/HEVC decoder (e.g. libheif/libde265) — HEVC decoding is
   patent-encumbered even where the library license itself (LGPL) permits redistribution;
   relies on the user's installed system codec instead.
8. **PDF-page import** — combine existing PDF files alongside images, not just images.
   Technically cheap: `PdfSharp` (already a dependency) supports importing pages from an
   existing document via `PdfDocument.ImportPage` with no rasterization/quality loss, and
   `Windows.Data.Pdf.PdfDocument` (built into Windows, no new dependency) can render a page
   to a bitmap for the grid thumbnail. Deferred anyway, because it brushes directly against
   the §2 non-goal "no PDF editor, viewer, splitter, or page extractor" — at that point the
   app is merging PDFs, not just combining images, which is real scope creep regardless of
   implementation cost. Only worth doing if this becomes a genuinely recurring ask post-v1.

---

## 9. Store submission notes

Registration is **free** for individuals as of late 2025 (company accounts free since May
2026); identity is verified with a government ID and a selfie rather than a card.
Microsoft re-signs MSIX packages during certification, so **no purchased certificate is
required**. A self-signed cert is still needed for local sideload testing during M5.

Reserve the app name in Partner Center **early** — names are first-come and reserving costs
nothing. "PageMerge" is a working title and is likely taken; have alternatives ready.

Required for submission: app icon at all scales, at least one 1366×768 screenshot, a short
and long description, a category (*Productivity*), age-rating questionnaire answers, and a
**publicly hosted privacy policy URL** — required even though the app collects nothing. A
short static page stating that the app processes files locally and transmits nothing is
sufficient.

Expect the first submission to bounce on something cosmetic. That is normal.

---

## 10. Constraints and cautions

- **Never modify source files.** Read-only access, always. A scan-merging tool that damages
  originals is worse than useless.
- **MSIX containerisation** means file system access follows the package model. Files arriving
  via the shell selection carry implicit permission, so the primary flow is fine. Do not add
  unprompted folder scanning without revisiting this.
- **Explorer stability.** Any unhandled exception in the shell extension degrades the user's
  Explorer. Wrap everything; fail silently to no-menu-entry rather than throwing.
- **No telemetry, no network calls, no auto-update.** The Store handles distribution. The app
  should work identically offline, forever.
- **HEIC** decode depends on the user having the HEIF Image Extensions installed. Detect the
  failure and show a clear per-tile message pointing at the Store extension, rather than a
  generic decode error.
