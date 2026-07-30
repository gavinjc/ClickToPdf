# Scanstack

A Windows utility that turns a folder of scanned images into a single PDF, in about
three clicks. Select images in File Explorer, right-click, **Combine to PDF**, drag the
pages into order, save.

> Portfolio copy of a private project. See [License](#license) below — this repo is
> shared to demonstrate the work, not for reuse.

**Pre-release software.** Scanstack is still under active development and has not
been publicly released (a free Microsoft Store listing is planned). Expect rough
edges, missing polish, and breaking changes between versions.

## Screenshots

![Explorer context menu](docs/screenshots/explorer-menu.png)
*Select images in File Explorer, right-click, and choose "Combine to PDF" — no need to open the app first.*

![Main window — thumbnail grid](docs/screenshots/main-window.png)
*Every page as a large, readable thumbnail. Drag to reorder, or move pages with Ctrl and the arrow keys.*

![Rotate a page](docs/screenshots/rotate.png)
*Rotate individual pages 90° either way, without ever touching the original file.*

![OCR and deskew options](docs/screenshots/options.png)
*Optional searchable text layer and auto-deskew for scans that came in slightly crooked.*

![Saved PDF](docs/screenshots/save-confirm.png)
*Save confirmation, with quick links to open the file or show it in Explorer.*

![Output opened in a PDF viewer](docs/screenshots/open-pdf.png)
*The result: a clean, properly ordered, searchable PDF.*

## Why

Windows' built-in "Print to PDF" locks page order to filename order, crops full-page
photos, and letterboxes landscape scans. Full PDF editors can merge images, but their
quick-merge dialogs list filenames only — no help when files are named
`IMG_0043.jpg … IMG_0051.jpg`. Scanstack does the one thing that actually matters: show
the pages, drag them into order, done.

## Features

- Right-click images in File Explorer and choose **Combine to PDF** — no need to open the app first
- Large, readable thumbnails in a reorderable grid; drag to reorder, or Ctrl+arrow keys
- Rotate individual pages 90° either way, without touching the original file
- Remove pages before saving; undo at least 20 steps deep
- Double-click a page for a full-size preview; zoom the thumbnail grid
- Pages load in natural filename order (`IMG_2` before `IMG_10`)
- Optional searchable text layer (OCR) and auto-deskew for crooked scans
- A4 or Letter output, automatic per-page orientation, adjustable margins/quality/greyscale
- JPEGs are embedded without re-encoding wherever possible — no quality loss, no bloat
- EXIF orientation honoured; images always scaled to fit, aspect ratio preserved, never cropped
- No account, no telemetry, no network calls — everything happens on-device; source images are opened read-only and never modified

## Tech stack

| Component | Stack |
|---|---|
| Shell extension | Native C++ (WRL), `IExplorerCommand` — adds the Explorer context-menu entry |
| App UI | C# / .NET 8, WinUI 3, MVVM via CommunityToolkit.Mvvm |
| Image decode | `Windows.Graphics.Imaging.BitmapDecoder` (WinRT) |
| PDF writing | [PDFsharp](https://github.com/empira/PDFsharp) 6.x, with JPEG passthrough |
| Packaging | Windows Application Packaging Project → MSIX |
| Tests | xUnit |

Three components ship in one MSIX package: a native shell-extension DLL that Explorer
loads directly (kept deliberately tiny and crash-proof), the WinUI 3 app that does all
the real work, and the packaging project that bundles them. See
[`docs/pagemerge-spec.md`](docs/pagemerge-spec.md) for the full technical spec and the
architecture rationale, and [`docs/ui-refinement-brief.md`](docs/ui-refinement-brief.md)
for the UI design pass.

## Project layout

```
src/
  Img2PDF.Core/            Imaging, layout, PDF composition, OCR — no UI dependencies
  Img2PDF.App/              WinUI 3 app: MainWindow, ViewModels, state, converters
  Img2PDF.Cli/               Console entry point used for engine validation
  Img2PDF.ShellExtension/   Native C++ IExplorerCommand implementation
  Img2PDF.Package/          MSIX packaging project
tests/
  Img2PDF.Core.Tests/       Deskew, page layout, OCR mapping
  Img2PDF.App.Tests/        Settings, natural sort, save-file naming, undo
```

## Download

A prebuilt, unsigned MSIX package is available on the [Releases](../../releases) page —
no build tools required. Since this isn't a Store-signed build, Windows needs to trust
it first:

1. Unzip the release download.
2. Enable **Settings → Privacy & security → For developers → Developer Mode** (or just
   run `Add-AppDevPackage.ps1` from the zip — it will prompt you through enabling
   sideloading if needed).
3. Right-click `Add-AppDevPackage.ps1` → **Run with PowerShell**, and follow the prompts.

## Building from source

Requires Visual Studio 2022 with the "Universal Windows Platform development" and
".NET desktop development" workloads. Open `Img2PDF.sln` and build/run the
`Img2PDF.App` project directly, or build the `Img2PDF.Package` project to produce an
installable (unsigned) MSIX package.

## License

All rights reserved — see [LICENSE](LICENSE). Source is shared here for portfolio
purposes; no reuse, modification, or redistribution rights are granted.
