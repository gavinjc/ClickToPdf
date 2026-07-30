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

## 4. Theming and colour

**The app is effectively dark-only.** Every colour decision in the file is hardcoded:
`#AA000000` on the icon buttons (line 145) and the filename bar (line 237),
`Foreground="White"` on the icons, badge text and filename, and `Foreground="Red"` on the
error text (line 53). WinUI follows the system theme by default, so a user — or a Store
certification tester, who is likely on stock light Windows — gets black chips and
white-on-white icons.

**Do:**

- Replace every hardcoded colour with a theme resource. `Foreground="Red"` →
  `SystemFillColorCriticalBrush`. Icon and filename scrims → a theme-aware smoke or acrylic
  brush. Icon foregrounds → `TextOnAccentFillColorPrimaryBrush` or
  `TextFillColorPrimaryBrush` depending on the final scrim.
- The page badge uses raw `SystemAccentColor` with `Foreground="White"` (line 213). Raw
  accent is oversaturated on dark backgrounds, and white text fails outright if the user's
  accent is pale — a yellow accent gives white-on-yellow. Use
  `AccentFillColorDefaultBrush` with `TextOnAccentFillColorPrimaryBrush`.
- `RootGrid` (line 8) sets no background, so the window renders near-black. White scanned
  pages against near-black is maximum contrast and the pages glare. Set
  `ApplicationPageBackgroundThemeBrush`, or enable Mica via `SystemBackdrop` on the Window
  for a properly native surface. This single change does most of the visual work.

**Test both themes before calling this done.** Switch Windows to light mode and re-check
every tile state: loading, loaded, error, selected, hovered.

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

## 8. Also outstanding, out of scope for this brief

Both are spec §4.2 gaps, tracked in `docs/store/submission-checklist.md` §5:

- **No-argument launch has no empty state.** Spec requires a "Choose images…" button.
  `AllowDrop` is set but there is no empty-state UI. This is a Store submission risk — a
  certification tester's first action is launching from the Start menu, as is WACK's launch
  test. Worth fixing before submission, but it is a separate piece of work.
- **No Sort split-button**, and no settings persistence anywhere in `Img2PDF.App`.

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
