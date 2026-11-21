# ClickTo: PDF — Release notes

## 1.0.3 — 21 November 2026

### Fixes

- **Uninstalling now removes everything.** Settings, logs and the app's temporary files used to
  be kept in folders that stayed behind after the app was uninstalled. They now live in the
  app's own storage, which Windows removes with it. Your zoom and sort settings come across on
  the first launch, and the old folders are deleted.
- **Missing heading in the About box.** The heading above the diagnostics buttons showed as an
  empty line. That section is now headed "Having trouble?".
- **The same image added twice.** Dragging in or picking an image that was already in the set
  added it as a second page. It is now skipped, and the message says so: "Skipped photo.jpg:
  already added".
- **Clicking Sort did nothing.** Only the arrow beside it worked. Clicking Sort itself now sorts
  again by the last order you chose, which is useful after adding more images.
- **Ctrl+Z only worked with a page selected.** After clicking a button or an option, Ctrl+Z did
  nothing. It now undoes wherever you are in the window.
- **Dragging a page couldn't be undone.** Undo skipped over a drag and took back the change
  before it. Each drag is now one undo step.
- **Changes during a save.** Pages and options could still be changed while a PDF was being
  saved, so the window could end up showing something other than what was saved. Save, Undo,
  Sort, adding, rotating, removing, dragging and the options are now unavailable until the save
  finishes or you cancel it.

### Improvements

- **Help.** A new **?** button beside About, or the F1 key, opens Help in its own window: how
  to get started, arrange and save pages, every save option, searchable text, the keyboard
  shortcuts, privacy, and what to do when something goes wrong. It is built into the app and
  works offline. F1 works even while a page preview is open.
- **Drop a folder to add its images.** Dragging a folder onto the window adds the images
  directly inside it, the same way right-clicking a folder does. Its subfolders are left out.
- **Clearer messages.** "Saved", skipped files and errors now all appear in one bar at the
  bottom of the window, one line each, and each can be closed. Errors used to be plain red
  text. Every skipped item is named, so nothing is left out silently. Adding images or saving successfully
  clears an earlier error.
- **More from The Bureau of Stuff.** The About box now lists our other apps, each linking to
  its Microsoft Store page. Nothing is fetched online: the list is built into the app.
- **Tidier About box.** The version sits next to the app name, the help buttons are grouped
  under "Having trouble?", and the third-party licences fold away until you want them, so the
  box no longer needs a scrollbar.
- **Report a problem** now fills in your app version, Windows version and runtime version in
  the email for you, and explains how to attach the log file.
- **Rate the app.** After a save, the app may ask once (and only once) whether you would like to
  rate it in the Microsoft Store. It never asks if anything has gone wrong since you opened the
  window, such as an image that couldn't be opened or a save that failed.

## 1.0.1 — 1 October 2026

### Fixes

- **HEIC and WEBP guidance.** When an HEIC or WEBP image can't be opened, the page now says
  exactly what is missing and links straight to it in the Microsoft Store. HEIC photos need two
  free Microsoft components — HEIF Image Extensions and an HEVC decoder — and the message used to
  name only one. `.heif` files are now accepted too.
- **Add-images button no longer covers notifications.** The round **+** button used to sit over
  the close button of the "Saved" and "Items skipped" bars. It now moves up above them while
  they are showing.
- **Phone photos saved sideways.** Saving before every page had finished loading could put
  photos into the PDF without their camera rotation. Save is now unavailable until loading
  finishes.
- **Rotations lost while loading.** Rotating a page before it had finished loading could be
  silently undone when it did.
- **"Date taken" sort.** With "Date taken" as your remembered sort, pages could come out partly
  in date-modified order, and images added to an existing set always did.
- **Replacing an existing PDF.** Cancelling a save over an existing PDF deleted that PDF. The
  new file is now written separately and only replaces the old one once it is complete, so
  cancelling or a failed save leaves the original untouched.
- **Unreadable images no longer break saving.** A page that couldn't be opened (for example,
  HEIC without the codec) made every save fail with an unhelpful error and left an empty PDF
  behind. Those pages are now left out, and the app tells you which ones.

### Improvements

- **Folder name in the window title**, so separate ClickTo: PDF windows are easy to tell apart
  in the taskbar and Alt+Tab.
- **Save before closing?** Closing a window with pages you haven't saved as a PDF now asks
  whether to save, discard, or cancel.

## 1.0.0 — 1 September 2026

First release on the Microsoft Store.

- **Right-click to combine.** Select images in File Explorer, right-click, and choose
  **Combine to PDF**.
- **See every page.** Pages appear as large thumbnails in a grid. Drag them into order or move
  them with Ctrl and the arrow keys, rotate pages 90° either way, remove pages, and undo up to
  twenty steps. Double-click a page for a full-size preview, and zoom the thumbnails between
  small and large.
- **Sensible starting order.** Pages arrive in natural filename order (IMG_2 before IMG_10).
  You can instead sort by date taken or date modified, or reverse the order. Your choice is
  remembered.
- **Add more images** to an open set by dragging them in or using the **+** button.
- **Output that looks right without fiddling.** A4, no margins, per-page orientation, and
  original image quality by default. Options cover Letter or match-the-image page size, narrow
  or standard margins, forced portrait, reduced quality (300, 200 or 150 DPI), and greyscale.
  Images are always scaled to fit and never cropped.
- **No quality loss.** JPEGs go into the PDF without being re-compressed, and other formats are
  stored losslessly. Camera (EXIF) orientation is honoured.
- **Searchable PDFs (optional).** An invisible text layer makes printed text selectable and
  searchable. Handwriting isn't reliably recognised.
- **Auto-deskew (optional)** straightens slightly crooked flatbed scans.
- **Formats:** JPG, JPEG, PNG, HEIC, TIF, TIFF, BMP, WEBP and GIF.
- **Private by design.** No account, no telemetry, no network connections of any kind. Your
  source images are opened read-only and never changed, and the PDF contains no username or
  folder paths.
- **Diagnostics.** Errors are recorded in a log that stays on your PC and is never sent
  anywhere. About › Diagnostics can copy diagnostic info, open the log folder, or start a
  problem report by email.
