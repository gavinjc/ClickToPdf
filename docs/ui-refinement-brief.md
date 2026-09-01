# UI refinement brief

Implementation brief for a round of UI work on `Img2PDF.App`, derived from a design review
of the running app on 28 July 2026. Everything here is confined to
`src/Img2PDF.App/MainWindow.xaml` and its code-behind unless stated otherwise.

Follow the C# and XAML conventions in `CLAUDE.md` (Allman braces, 4-space indent,
120-column lines). `docs/pagemerge-spec.md` remains authoritative on behaviour — where this
brief and the spec disagree, the spec wins and this document is wrong.

**Build and run after each section.** This codebase has been bitten three times by WinUI
failures that are invisible when reading the file — see *Known traps* at the end. Do not
batch all five sections and build once.

---

## Status as of 2026-08-20

Most of this brief has been implemented. Verified against `MainWindow.xaml` (now 520 lines)
by inspection, not by running the app:

| Section | State |
|---|---|
| 1 — invisible selection | Done, `IsSelected` present |
| 2 — filename escapes tile | Done |
| 3 — tile chrome, tooltips, undo | Done; buttons in the card footer, 12 tooltips, 10 automation names, Undo in the toolbar |
| 4 — theming | **Superseded — rewritten below.** The hardcoded-colour cleanup is done; the surface palette is the remaining work |
| 5 — preview overlay | Close, rotate and page indicator present, with `StopTappedPropagation` on each. **Could not verify `←`/`→` stepping** — it would live in code-behind; confirm it exists |
| 6 — toolbar polish | Done; `AccentButtonStyle`, page count, `HorizontalAlignment="Stretch"` on the `Expander` |
| 8 — empty state, sort | Done; `EmptyStatePanel` and the sort split-button both landed in 0.5.6 |

Only four hardcoded colours remain, all inside `PreviewOverlay` — that is deliberate and
correct, see §4.

---

## 1. Selection is invisible — fix first

`TileContainerStyle` (line 126) replaces `GridViewItem`'s control template with a bare
`ContentPresenter`, which removes every built-in selection, focus and hover visual. The
comment on line 120 states that selection is "drawn manually in the DataTemplate below via
the Border bound to IsSelected" — that Border does not exist, and `PageItem` has no
`IsSelected` property.

Consequence: `SelectionMode="Extended"` is active and these all operate on a selection the
user cannot see — `Ctrl`+`A`, `Ctrl`+`←`/`→` page moves, `R` / `Shift`+`R` rotate, `Delete`,
and multi-select drag. That is most of the keyboard surface in spec §4.2.

**Do:**

- Add an observable `IsSelected` bool to `PageItem`.
- Keep `SelectedItems` on the `GridView` as the source of truth; sync `IsSelected` from
  `SelectionChanged` rather than making the collection authoritative in two places.
- Bind the card `Border` (line 178) so selection is unmistakable at a glance across a grid
  of 40 near-identical white pages. A 2px `AccentFillColorDefaultBrush` border plus a subtle
  accent tint on the card background reads better than border alone at 120px zoom.
- Restore a keyboard focus visual too. Stripping the container template also removed the
  focus rect, so keyboard-only navigation is currently unnavigable.

Verify by selecting three non-adjacent tiles with `Ctrl`+click and confirming all three are
obvious at both 120px and 400px zoom.

---

## 2. Filename bar escapes the tile

The filename `Border` (line 237) sits directly in the tile `Grid`, which has an explicit
`Width` but does not clip its children — a WinUI `Grid` never does. The `Border` has no
width constraint, so the `TextBlock` is measured against infinite width, `TextTrimming`
never engages, and the bar grows to the full text width and spills below and to the right of
the card. In screenshots this reads as unstyled white text floating outside the tile, and
the `#AA000000` backing is invisible against the dark app background.

**Do:** move the filename `Border` inside the card `Border`'s inner `Grid` (line 183), which
is width-constrained by the card. `TextTrimming="CharacterEllipsis"` will then work as
intended. Confirm with a deliberately long filename at 120px zoom.

---

## 3. Tile chrome — consolidate, reposition, recede

The three icon buttons (line 223) are **deliberately always visible**, and that decision
stands: double-click is not a discoverable gesture, so the magnifier is the discoverable
path to preview, not a redundant one. Do not remove any of the three buttons, and do not
replace always-visible with hover-reveal.

The problems are weight and placement, not count.

**3a. Move the button group to the bottom of the card**, on the same row as the filename.
They currently sit top-right, directly over the page header — on a scan that is the
letterhead, title and first column, which is the most useful region for telling one page
from another. Identifying pages at a glance is the entire purpose of the grid (spec §2), so
chrome must not cover it. The page footer is almost always whitespace or a page number.

Keep the page-number badge (line 212) where it is, top-left. It is small, it is information
rather than a control, and top-left is the natural reading position for an ordinal.

**3b. Consolidate the three chips into one pill.** Three separate opaque `#AA000000`
rectangles per tile means 45 competing rectangles on a 15-page grid. A single rounded
container holding all three glyphs reads as one piece of chrome and is substantially quieter
at the same opacity. Use a theme-aware scrim rather than hardcoded `#AA000000` — see §4.

**3c. Use opacity, not visibility, for hover emphasis.** Rest the pill around 55% opacity and
take it to 100% on pointer-over, via a `VisualStateManager` `PointerOver` state on the tile's
root `Grid`.

This specifically avoids the bug documented at line 219: toggling `Visibility` from
`PointerEntered`/`PointerExited` flickers when the pointer crosses onto the buttons
themselves. Opacity does not affect hit-testing, so the buttons stay clickable and the
pointer never leaves the tile's hit region. A `VisualStateManager` state on the root also
covers the whole subtree, which per-element pointer events do not.

**3d. Add tooltips and automation names to all three buttons.** They are currently three
unlabelled glyphs — visible, but not self-explanatory; a user still has to guess what the
circular arrow does, and a screen reader announces nothing at all. Given that the rationale
for always-visible buttons is discoverability, this is the larger half of that goal.

```
E71E  Preview      "Preview page"
E7AD  Rotate       "Rotate right"
E711  Remove       "Remove from document"
```

Set both `ToolTipService.ToolTip` and `AutomationProperties.Name` on each.

**3e. Make undo reachable by mouse.** Remove is one click away on every tile, but `Ctrl`+`Z`
is keyboard-only with no visible affordance anywhere in the window. By the same standard that
makes double-click too hidden to rely on, `Ctrl`+`Z` is too hidden as well — so the app
currently makes destruction one click away and recovery invisible.

`UndoStack` already exists with a 20-entry capacity and passing tests; it just has no mouse
path. Either add an Undo button to the top toolbar, or surface a transient "Removed — Undo"
bar on removal. The toolbar button is simpler and also covers undoing a mis-drag, which is
the more likely mistake.

---

## 4. Surface palette and the unified options panel

Rewritten 2026-08-20. The original §4 — replacing hardcoded colours with theme resources —
is done. What follows is a separate problem, agreed after reviewing the running app in both
themes.

### The problem

Two distinct faults, both dark-theme only. Light theme is close to right and must not become
collateral damage.

**The surface ladder is compressed.** `RootGrid` uses
`ApplicationPageBackgroundThemeBrush` and the tile card uses
`CardBackgroundFillColorDefaultBrush` (line 247), which in dark theme is roughly 5% white
over the base. The window background, the Options panel and the tile card therefore land
within a few percent of each other, and the whole screen reads as flat and murky.

**The pages glare.** A white scan on a near-black background is the maximum contrast the
display can produce, so the thumbnails burn and the surrounding UI disappears. In light
theme the same page sits on a near-white card and blends in, which is why light theme looks
fine and dark theme does not.

### Decision

Explicit named brushes, per theme, with the ladder deliberately widened at *both* ends — the
background goes darker as the cards go lighter. That matters: dropping the background from
`#1C1C1C` to `#141414` buys as much apparent separation as lifting the card does, and costs
nothing in glare because the background carries no content.

| Role | Dark | Light |
|---|---|---|
| Window / canvas background | `#141414` | `#F3F3F3` |
| Options panel, header **and** body | `#2F2F2F` | `#FFFFFF` |
| Options panel border | `#474747` | `#E0E0E0` |
| Controls inside the panel | `#3A3A3A`, border `#565656` | `#F6F6F6`, border `#DDDDDD` |
| Toolbar buttons | `#2E2E2E`, border `#4A4A4A` | framework default |
| Tile card | `#3B3B3B`, border `#545454` | `#FFFFFF`, border `#E0E0E0` |
| Tile card, selected | 2px accent border | 2px accent border |
| Page image hairline | `#606060` | `#DCDCE0` |
| Text primary / secondary | `#F0F0F0` / `#D6D6D6` | framework default |

The borders are load-bearing, not decorative. `#2F` panel against `#3B` card is a 12-point
gap, comfortable on a good monitor but liable to converge on a dim or low-contrast display;
the 1px borders are what hold the separation when the fills stop doing it. Do not drop them
in favour of fill contrast alone.

The page hairline is the other quiet essential — it stops the white image butting directly
against the card and is most of why the glare subsides.

### Mechanism

`App.xaml` currently has only `MergedDictionaries` and no `ThemeDictionaries`. Add them, and
define the palette as named brushes there rather than inline in `MainWindow.xaml`. That keeps
light and dark independent, and gives one place to tune values.

```xml
<ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
        <!-- existing XamlControlsResources -->
    </ResourceDictionary.MergedDictionaries>

    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="AppCanvasBackgroundBrush" Color="#141414"/>
            <SolidColorBrush x:Key="AppPanelBackgroundBrush"  Color="#2F2F2F"/>
            <SolidColorBrush x:Key="AppPanelBorderBrush"      Color="#474747"/>
            <SolidColorBrush x:Key="AppTileBackgroundBrush"   Color="#3B3B3B"/>
            <SolidColorBrush x:Key="AppTileBorderBrush"       Color="#545454"/>
            <SolidColorBrush x:Key="AppPageEdgeBrush"         Color="#606060"/>
        </ResourceDictionary>
        <ResourceDictionary x:Key="Light">
            <!-- light column from the table above -->
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

Then consume with `{ThemeResource AppTileBackgroundBrush}` and friends. `RootGrid` moves from
`ApplicationPageBackgroundThemeBrush` to `AppCanvasBackgroundBrush`; the tile card at line 247
moves from `CardBackgroundFillColorDefaultBrush` to `AppTileBackgroundBrush`.

**Gotcha:** overrides of *framework* theme resources (the `Expander*` keys below) must also
live inside these `ThemeDictionaries`. Defining them as plain App-level resources applies one
value to both themes, which is exactly the bug this section exists to avoid.

### Unified options panel

`Expander` paints its header and its content with different brushes and puts a divider
between them, which is why the Options section currently reads as a lid on a box rather than
one panel. Override so header and body are a single continuous surface, in both themes:

- `ExpanderHeaderBackground` and `ExpanderContentBackground` → `AppPanelBackgroundBrush`
- `ExpanderHeaderBorderThickness` → `0`, dropping the internal divider
- `ExpanderHeaderBorderBrush` and `ExpanderContentBorderBrush` → `AppPanelBorderBrush`

Verify those key names against `generic.xaml` for the installed Windows App SDK before
relying on them — `Expander`'s resource keys have shifted between releases, and a misspelled
key fails silently rather than erroring, which is a nasty way to lose an hour.

### Deliberate exception

`PreviewOverlay` keeps its hardcoded `#CC000000` scrim and `Foreground="White"` glyphs. A dim
scrim with white controls over a full-bleed photograph is correct in both themes — the
overlay is not a themed surface, it is a darkroom. Do not "fix" these four values.

### Re-check afterwards

- The floating add button and the info glyph both sit on the canvas; confirm their contrast
  against `#141414` rather than the old background.
- The selected-tile accent border at 120px zoom, where the card is mostly border.
- Secondary text on the lighter `#3B3B3B` card — `#D6D6D6` is the intended value; anything
  around `#A8A8A8` will be too dim once the card lifts.
- Light theme, every tile state, before calling this done.

---

## 5. Preview overlay

`PreviewOverlay` (line 248) is a dimmed `Grid` with a single `Image` and a `Tapped` handler.
It has no close affordance, no page indicator, and no way to move between pages.

The workflow it needs to serve is checking page order, which means comparing consecutive
pages. Right now that costs a close, a hunt for the next tile, and a double-click per page.

**Do, in priority order:**

1. `←` / `→` to step to the previous/next page without leaving the overlay. This is the
   highest-value item in this brief after §1.
2. A page indicator — "3 of 5" — so position is not lost while stepping.
3. A close `X`, top-right. `Esc` and click-outside both work already but are invisible;
   keep both and add the button.
4. A rotate button. Sideways pages are noticed here, and currently require closing the
   overlay to act on.

Note that the existing `Tapped="PreviewOverlay_Tapped"` fires for taps on the image too,
since the event bubbles. That is acceptable now, but adding controls inside the overlay
means their taps must not close it — mark those handled.

---

## 6. Toolbar and layout polish

**Save does not look primary.** It is a default-styled `Button` (line 50) wedged between the
zoom slider and the error text. It is the app's terminal action. Give it
`AccentButtonStyle` and right-align it, leaving the zoom group left. Add a page count
("5 pages") near it — useful confirmation before committing to a save.

**The Options expander changes width when it opens.** Line 58 sets
`HorizontalContentAlignment="Stretch"`, which controls the content inside the expander, not
the expander itself — so it sizes to content in both states and visibly jumps. Set
`HorizontalAlignment="Stretch"` on the `Expander`.

**Both InfoBars push the layout down.** Rows 0 and 1 are `Auto` (lines 15–16), so opening
either shifts the entire grid — around 85px for the save confirmation, more for the skipped-
files warning. The user is looking at the grid when the save bar appears, and it moves under
them. Float the bars over the content, or reserve the row height.

---

## 7. Explicitly not doing

Recorded so these are not helpfully reintroduced:

- **No "Done" or "Close" button in the main toolbar.** It duplicates the titlebar close
  button, and next to `Save` it raises a question users should not have to answer — does
  Done save? Spec §4.2 also deliberately keeps the window open after saving because users
  often save a second variant. If a completion affordance is wanted later, add `Close` as a
  third button inside the Saved InfoBar, where it is contextual and unambiguous.
- **Do not remove the magnifier button** or any of the three tile buttons. See §3.
- **Do not convert the tile buttons to hover-reveal.** See §3c.

---

## 8. Spec §4.2 gaps — both closed

Kept for history; nothing to do. Both landed in 0.5.6 and are recorded in
`docs/store/submission-checklist.md` §5.

- **No-argument launch empty state** — `EmptyStatePanel` with a "Choose images…" button, plus
  drag-and-drop onto `RootGrid`. This had been a Store submission risk, since a certification
  tester's first action is launching from the Start menu, as is WACK's launch test.
- **Sort split-button and settings persistence** — both implemented, backed by
  `State/AppSettings`.

---

## Known traps in this codebase

Read the inline comments before changing the areas they guard — each records a real failure:

- **Line 193:** `ProgressRing.IsActive` crashed natively (`Microsoft.UI.Xaml.dll`,
  `0xc000027b`) inside this virtualized `GridView` item template. Confirmed by bisection.
  The plain "Loading…" `TextBlock` is the workaround. Do not reintroduce `ProgressRing` here.
- **Line 51:** `x:Bind` with a `Converter` fails to compile when the code-behind root is a
  `Window` rather than a `FrameworkElement`. Classic `{Binding}` is used deliberately in
  those places.
- **Line 120:** `GridViewItem`'s default template paints hover/pressed/selected chrome above
  the item's own content. Overriding individual brush resources did not fix it, which is why
  the container template is replaced wholesale. Restoring selection visuals (§1) must work
  within that constraint — draw them in the `DataTemplate`, do not revert the container style.
- **Line 140:** `Button`'s default template overrides `Background` on hover, which read as
  the icon buttons disappearing against the thumbnail. `IconButtonStyle` is intentionally
  static. If §3c adds hover opacity, apply it to the pill container, not by reintroducing
  `Button` visual states.

## Verification

- `dotnet test` — `Img2PDF.Core.Tests` and `Img2PDF.App.Tests` should stay green. Adding
  `IsSelected` to `PageItem` touches a type used by `UndoStackTests`.
- Run the app from Explorer with a real multi-page scan, at both 120px and 400px zoom.
- Run it in light theme and dark theme.
- Check tile states: loading, loaded, decode error, selected, hovered, mid-drag.
- Confirm a long filename ellipsises rather than overflowing.
