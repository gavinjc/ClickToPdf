# ClickToPDF

## Project Overview
A single-purpose Windows utility: select image files in File Explorer, right-click
**Combine to PDF**, reorder pages as thumbnails in a grid, save as PDF. No PDF editor,
no OCR, no cloud — see `docs/pagemerge-spec.md` for the full spec (working title
"PageMerge" in that document).

**Naming:** the codebase — repo (`The-Bureau-of-Stuff/ClickToPDF`, local
`ClickToPDF`), solution, all five projects
(`ClickToPDF.App`/`.Core`/`.Cli`/`.ShellExtension`/`.Package`), namespaces, folder names — is
`ClickToPDF.*`; the exe is `ClickToPDF.App.exe`. The product/store-facing name is
**ClickTo: PDF** (with the colon and space): window title, PDF `/Producer` metadata, the MSIX
manifest's `DisplayName`/`PublisherDisplayName`/`SurrogateServer` name, and all `docs/store/*`
submission copy. The `Identity Name`/`Publisher` in `Package.appxmanifest` are the real Partner
Center values (`TheBureauofStuff.ClickToPDF`; see `docs/store/submission-checklist.md` §1.1).

The code name was `Img2PDF` until 2026-11-21. A few `Img2PDF` strings stay on purpose because
installed copies depend on them: the manifest's verb `Id="Img2PDF"` (and the dev-loop registry
key in `Registration.cpp` that mirrors it), and the legacy `%LOCALAPPDATA%\Temp\Img2PDF` folder
that `AppDataFolders` deletes and the regsvr32 dev loop still writes. Don't rename them. Dated
history (release notes, Store submission records, old package file names) also keeps the old
name.

## Shared code: Bureau.Common (git submodule)
The About dialog, cross-promo panel, rating prompt, title-bar theming, surface palette and
`IconButtonStyle` come from the shared
[Bureau.Common](https://github.com/gavinjc/Bureau.Common) repo, mounted
at `external/Bureau.Common` and pinned to one commit.
- Clone with `git clone --recurse-submodules`. After a pull that moves the pin, run
  `git submodule update --init`. Without it, `external/Bureau.Common` is empty and the build
  fails on the missing project.
- Change shared chrome **in Bureau.Common**: commit and push there first, then bump the pin
  here (`git -C external/Bureau.Common pull`, then commit the submodule change). Don't edit
  the files under `external/` as if they were this repo's.
- Read `external/Bureau.Common/CLAUDE.md` before touching it. Its rules include: no network
  calls, its own resource map for strings, and the WindowsAppSDK version kept in step with
  this app's.

## The ClickTo family
This app is one of a family. The family context below is loaded from the submodule, as of its
pinned commit. It says where the shared conventions and lessons live; family-wide lessons go
there, not into this repo's notes.

(The shared family-context file is internal and not part of this snapshot.)

## Build
```
ClickToPDF.sln has four project types:
- ClickToPDF.Core / ClickToPDF.Cli / ClickToPDF.App (csproj) — build via `dotnet build`/`dotnet run`.
- ClickToPDF.ShellExtension (vcxproj, native WRL COM DLL) — needs VS2022's C++ desktop
  workload; build via VS2022's own MSBuild.exe, not `dotnet build`.
- ClickToPDF.Package (wapproj, MSIX packaging) — needs the "Universal Windows Platform
  development" VS2022 workload; also built via VS2022's MSBuild.exe. Signing needs a
  local self-signed dev cert (ClickToPDF.Package_TemporaryKey.pfx, gitignored, per-machine;
  a machine set up before the 2026-11-21 rename has it as Img2PDF.Package_TemporaryKey.pfx
  and must rename it —
  regenerate via New-SelfSignedCertificate, Subject must match the manifest's Publisher
  exactly, and the cert must be imported into LocalMachine\TrustedPeople for
  Add-AppxPackage to accept it).

Local dev/test loop for the shell extension itself (fast, no MSIX rebuild needed):
regsvr32-register ClickToPDF.ShellExtension.dll directly (HKCU, no admin) with
ClickToPDF.App's build output copied alongside it — see build_environment memory for the
exact commands. This surfaces the command under "Show more options", not the main
context menu; main-menu placement only comes from the MSIX manifest (ClickToPDF.Package).
```

## Tests
```
dotnet test — xunit projects under tests/ (ClickToPDF.Core.Tests, ClickToPDF.App.Tests).
```

## Architecture
See `docs/CURRENT_APPLICATION.md` for a living summary and `docs/pagemerge-spec.md`
for full detail (this is the authoritative spec — read it before starting any
implementation work).

Three components, one MSIX package:
- `ClickToPDF.ShellExtension/` — native C++ (WRL), `IExplorerCommand`, launches the app. Must
  return in well under 100ms and never throw (an unhandled exception here degrades
  Explorer itself).
- `ClickToPDF.App/` — C# / .NET 8 / WinUI 3. All real work: UI, image decode, PDF
  generation. Framework-dependent WindowsAppSDK deployment (not self-contained) — required
  for MSIX packaging to work; self-contained conflicts with packaged WinRT activation.
- `ClickToPDF.Package/` — Windows Application Packaging Project → MSIX.

Build order follows spec §6 milestones (M1 console PDF engine → M2 WinUI shell → M3
save flow → M4 shell extension → M5 MSIX packaging → M6 Store readiness). Each
milestone should be independently runnable — validate PDF output quality (M1) against
real scans before building any UI.

## Progress

Milestones and tasks are tracked in `docs/PROGRESS.md` (format:
`~/.claude/init-shared/progress-format.md`). Keep it current as you work: mark the task you
start `status: active`, tick finished tasks and add `done:` with today's date, and add tasks
you discover.

## Code standards

General code standards load from user scope (`~/.claude/init-shared/code-standards.md`).
For copy-paste duplication specifically, `npx jscpd .` uses this repo's `.jscpd.json`.

## Notes
- Never modify, move, or delete the user's source image files — read-only access only.
- No telemetry, no network calls, no auto-updater. The app must work identically offline.
- JPEG passthrough is a hard requirement for PDF output quality/size — verify PDFsharp
  is not silently re-encoding (spec §4.3).
- Full acceptance criteria: `docs/pagemerge-spec.md` §7.
- The privacy policy lives in the public `The-Bureau-of-Stuff/privacy-policies` repo (served at
  `https://the-bureau-of-stuff.github.io/privacy-policies/ClickToPDF/privacy-policy/`), because
  this repo is private. Edit it there, not here.

## GUI tests

Anything that shows a window on the desktop (the app under test, a GUI test run) must be launched through
`powershell -NoProfile -File "run-gui-test.ps1" -Project ClickToPDF -Command "<command>"`.
It holds the desktop lock while the command runs, and waits if a hub worker is using the desktop.
