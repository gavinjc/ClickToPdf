# ClickTo: PDF

A Windows utility that turns a folder of scanned images into a single PDF, in about
three clicks. Select images in File Explorer, right-click, **Combine to PDF**, drag the
pages into order, save.

> Portfolio snapshot of a shipped project — see [License](#license). The maintained source is
> private; this copy is a point-in-time mirror and won't track every future change.

**Published on the Microsoft Store**, v1.0.3.

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
`IMG_0043.jpg … IMG_0051.jpg`. ClickTo: PDF does the one thing that actually matters: show
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
- Built-in offline Help (F1), local-only diagnostic logging and an About-dialog "Report a problem" flow — nothing is ever transmitted
- No account, no telemetry, no network calls — everything happens on-device; source images are opened read-only and never modified

## Tech stack

| Component | Stack |
|---|---|
| Shell extension | Native C++ (WRL), `IExplorerCommand` — adds the Explorer context-menu entry |
| App UI | C# / .NET 8, WinUI 3, MVVM via CommunityToolkit.Mvvm |
| Image decode | `Windows.Graphics.Imaging.BitmapDecoder` (WinRT) |
| PDF writing | [PDFsharp](https://github.com/empira/PDFsharp) 6.x, with JPEG passthrough |
| Shared UI | [Bureau.Common](https://github.com/gavinjc/Bureau.Common) (git submodule): About, Help and message-bar chrome shared with the other ClickTo apps |
| Packaging | Windows Application Packaging Project → MSIX |
| Tests | xUnit |

Three components ship in one MSIX package: a native shell-extension DLL that Explorer
loads directly (kept deliberately tiny and crash-proof), the WinUI 3 app that does all
the real work, and the packaging project that bundles them. The design docs and spec stay
in the private repo; the [Bureau.Common](https://github.com/gavinjc/Bureau.Common) repo
includes the conventions the four ClickTo apps share.

ClickTo: PDF was the first of four ClickTo apps. The others —
[ClickTo: Images](https://github.com/gavinjc/ClickToImages),
[ClickTo: Slideshow](https://github.com/gavinjc/ClickToSlideshow) and
[ClickTo: Speech](https://github.com/gavinjc/ClickToSpeech) — reuse its shell-extension and
app structure, and all four share [Bureau.Common](https://github.com/gavinjc/Bureau.Common).

## Project layout

```
src/
  ClickToPDF.Core/            Imaging, layout, PDF composition, OCR — no UI dependencies
  ClickToPDF.App/             WinUI 3 app: MainWindow, ViewModels, state, converters
  ClickToPDF.Cli/             Console entry point used for engine validation
  ClickToPDF.ShellExtension/  Native C++ IExplorerCommand implementation
  ClickToPDF.Package/         MSIX packaging project
tests/
  ClickToPDF.Core.Tests/      Deskew, page layout, OCR mapping
  ClickToPDF.App.Tests/       Settings, natural sort, save-file naming, undo
external/
  Bureau.Common/              Shared About/Help/message-bar chrome (submodule)
```

## Download

**ClickTo: PDF** is free on the Microsoft Store:

**[Get it from the Microsoft Store](https://apps.microsoft.com/detail/9NF364X93V6L)**

It installs and updates through the Store like any other app — no separate download,
no certificate to trust, signed with a proper Store certificate.

## Building from source

Clone with submodules: `git clone --recursive https://github.com/gavinjc/ClickToPdf`.
GitHub's "Download ZIP" leaves the submodule out, so the solution won't build from a ZIP.

Requires Visual Studio 2022 with the ".NET desktop development", "Desktop development with C++"
and "Windows application development" workloads. Open `ClickToPDF.sln` and run the
`ClickToPDF.App` project directly, or build the `ClickToPDF.Package` project to produce an
MSIX package.

## License

All rights reserved — see [LICENSE](LICENSE). The source is shared here for portfolio purposes;
no rights to reuse, modify or redistribute it are granted.
