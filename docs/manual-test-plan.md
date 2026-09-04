# diaphane — manual test plan

The unit tests (`dotnet test tests/Diaphane.Shell.Tests`) and the one live
engine test (`tests/Diaphane.Core.Tests`) cover the shell logic and the native
input pipeline. The **WinUI event → CEF** layer (focus routing, real mouse and
keyboard) can only be exercised against a running window.

## Automated UI smoke

```
powershell scripts/ui-smoke.ps1
```

Launches the debug build, drives real OS mouse + keyboard at a fixed form page,
and asserts: a click focuses a text field, the field **keeps** focus, typed text
lands, and a checkbox toggles. Needs an interactive desktop (it moves the real
cursor). Exit code 0 = pass.

## Manual checks

Run the debug build (`src/Diaphane.App/bin/x64/Debug/.../Diaphane.App.exe`) or a
packaged zip. Work through the list; each line is one check.

### Tabs & navigation
- [ ] Ctrl+T opens a new blank tab; the tab strip shows it and switches to it.
- [ ] Type `example.com`, Enter → the page loads; the tab title updates.
- [ ] Type a phrase with spaces, Enter → a search runs on the configured engine.
- [ ] Back / Forward buttons and Alt+Left / Alt+Right move through history.
- [ ] F5 reloads. Ctrl+W closes the tab; closing the last tab opens a fresh one.
- [ ] Ctrl+Tab / Ctrl+Shift+Tab cycle tabs. Ctrl+L focuses the address bar.
- [ ] The Home button navigates to the homepage set in Settings.

### Form input (the regression area)
Open a page with a real form (a login page, `https://httpbin.org/forms/post`, or
any site).
- [ ] Click a text field → the caret appears **and stays**; it does not flash and
      lose focus.
- [ ] Type — characters appear in the field. Backspace, arrow keys, Home/End work.
- [ ] Tab moves between fields within the page.
- [ ] Click a checkbox / radio button → it toggles.
- [ ] A `<select>` dropdown: click it, use Up/Down + Enter to pick an option — the
      value changes. (The dropdown list is **not drawn yet** — known gap.)
- [ ] A range slider: click it, ArrowLeft/Right move the thumb.
- [ ] A date field: click it and type digits — the segments fill.
- [ ] `<textarea>`: multi-line typing and Enter for newlines work.
- [ ] Click the address bar, then click back on the page — focus returns to the
      page and typing works again.
- [ ] Submit the form (click the button or press Enter in a field) → it posts.

### Bookmarks / history / omnibox
- [ ] Ctrl+D bookmarks the page; it appears on the bookmarks bar. Ctrl+D again
      removes it. Right-click a bookmark → Remove.
- [ ] Ctrl+Shift+B toggles the bookmarks bar.
- [ ] The history button lists visited pages; clicking one navigates; "Clear all"
      empties it.
- [ ] Start typing a URL you've visited → it appears as a suggestion.

### Sandbox tabs
- [ ] Ctrl+Shift+N opens a Sandbox tab (violet shield icon; toolbar tint).
- [ ] Log into a site in a normal tab; a Sandbox tab does **not** see that login.
- [ ] Sandbox browsing does not appear in history after the tab closes.

### diaphane:// panels
- [ ] `diaphane://privacy` (or the toolbar shield / Ctrl+Shift+Del): pick scopes +
      a time range, "Clear now" → cookies/cache/history are cleared.
- [ ] Tick "clear … every time diaphane closes", restart → the data is gone.
- [ ] `diaphane://settings` (gear): change search engine, homepage, theme — theme
      applies immediately; homepage/startup apply on the next launch.
- [ ] `diaphane://extensions` (Ctrl+Shift+E): "Load unpacked…" a folder with a
      `manifest.json` → it lists; toggle / Remove work; "Check for updates" only
      re-reads the folder (never network).
- [ ] `diaphane://media` (Ctrl+Shift+M): "Run probe" → H.264 / AAC / MP3 / VP9 /
      AV1 show as playable; Widevine shows "Not available" by default.

### DevTools
- [ ] F12 (or Ctrl+Shift+I) opens the DevTools pane on the right.
- [ ] Elements shows the live DOM; selecting a node highlights it on the page.
- [ ] Console evaluates expressions. The Network tab records requests on reload.
- [ ] Drag the splitter to resize the pane. F12 again closes it.

### Media playback (codecs)
- [ ] An MP4 (H.264/AAC) video plays with sound — e.g. a news site or
      `https://test-videos.co.uk/bigbuckbunny/mp4-h264`.
- [ ] An MP3 audio file plays.
- [ ] A YouTube video plays (VP9/AV1 + Opus). DRM/Netflix will **not** play
      unless Widevine is enabled in Settings *and* the CDM is present.

### Privacy / no-phone-home
- [ ] With a network monitor (Wireshark / Fiddler), start diaphane and sit idle →
      no outbound connections except to pages you opened.
- [ ] The omnibox makes **no** request while you type (only on Enter).
- [ ] No update check happens unless you set a manifest URL and click the button.

### Packaging
- [ ] `powershell scripts/package.ps1` produces `dist/diaphane-<ver>-win-x64.zip`.
- [ ] Unzip on a machine **without** .NET installed → `Diaphane.App.exe` runs.
