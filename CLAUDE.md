# PrettyMark

Native Markdown viewer for Windows with live reload and syntax highlighting.
Renders `.md` files in a native WinForms window using WebView2 (EdgeChromium), with GitHub-flavored styling.

**Repository:** https://gitlab.com/eagle1/prettymark

## Features

- GitHub-style Markdown rendering (tables, fenced code, task lists, TOC) via marked.js
- Syntax highlighting via highlight.js (github theme)
- Live reload on file save (FileSystemWatcher with debounce)
- Dark mode with persistence (AppData settings)
- Multi-tab support: open multiple files simultaneously in a single window
- Collapsible sidebar (drawer) with list of open files
- Drag & drop: native Chromium drop intercepted via `NewWindowRequested` (opens as new tab)
- Find bar (`Ctrl+F`): DOM-based text search with highlight, match counter, next/prev navigation
- Print (`Ctrl+P`): native print dialog via `window.print()`, `@media print` CSS hides UI chrome
- Recent Files: File → Recent Files submenu, last 10 files, persisted in AppData
- Session persistence: open tabs and active tab restored on relaunch
- Native menu bar: File → Open / Recent Files / Print / Close Tab, Edit → Find, View → Dark Mode / Sidebar / Zoom / Full Screen, ? → About
- Keyboard shortcuts (JS `keydown` → `postMessage`): `Ctrl+O` open, `Ctrl+W` close tab, `Ctrl+P` print, `Ctrl+F` find, `Ctrl+D` dark mode, `Ctrl+B` sidebar, `Ctrl++`/`Ctrl+-`/`Ctrl+0` zoom, `F11` fullscreen
- Welcome screen when launched without arguments
- Multi-language support (i18n): 12 languages, auto-detect system language, runtime switching via View → Language, preference persisted in AppData

## Project Structure

```
PrettyMark.csproj              # .NET 8 project file
Program.cs                     # Main application (entry point, COM interop, tab management)
nuget.config                   # NuGet feed configuration
installer.nsi                  # NSIS installer script (cross-platform, produces Setup.exe)
build-msix.ps1                 # PowerShell script to build MSIX installer (Windows only, for Store)
assets/index.html              # HTML template with JS rendering logic (tabs, drawer, about)
assets/github-markdown.css     # GitHub-flavored Markdown CSS (light)
assets/github-markdown-dark.css # GitHub-flavored Markdown CSS (dark)
assets/marked.min.js           # Markdown parser (client-side)
assets/highlight.min.js        # Syntax highlighting
assets/highlight-github.min.css # Highlight.js github theme (light)
assets/highlight-github-dark.min.css # Highlight.js github theme (dark)
assets/lang/*.json             # i18n translation files (one per language)
assets/msix/*.png              # MSIX visual assets (StoreLogo, Square44x44, Square150x150)
test.md                        # Test file for verifying rendering
```

## Dependencies

- **.NET 8** — runtime and SDK
- **Microsoft.Web.WebView2** 1.0.2739.15 — WebView2 control for WinForms (EdgeChromium)
- **marked.js** — client-side Markdown → HTML (bundled in assets)
- **highlight.js** — client-side syntax highlighting (bundled in assets)

## Build

### Prerequisites

1. .NET 8 SDK installed: https://dotnet.microsoft.com/download/dotnet/8.0
2. NSIS (for installer): https://nsis.sourceforge.io/ — also available via `apt install nsis` on Linux
3. Windows SDK (for MSIX only): https://developer.microsoft.com/windows/downloads/windows-sdk/

### Run in development

```cmd
cd C:\path\to\prettymark
dotnet run -- test.md
```

### Build portable executable

```cmd
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\PrettyMark.exe`

Cross-compile from Linux: add `-p:EnableWindowsTargeting=true`

### Build NSIS installer

```cmd
makensis installer.nsi
```

Output: `bin\PrettyMark-Setup-1.0.0-win-x64.exe`

Requires `dotnet publish` first (uses exe from `bin\Release\...`). Works on both Windows and Linux.

### Build MSIX installer (Windows only, for Microsoft Store)

```powershell
.\build-msix.ps1 -Version "1.0.0.0"        # unsigned (for Store upload)
.\build-msix.ps1 -Version "1.0.0.0" -Sign   # self-signed (for sideloading)
```

Output: `bin\msix\PrettyMark-1.0.0.0-win-x64.msix`

### Release artifacts

Hosted on GitLab Releases: https://gitlab.com/eagle1/prettymark/-/releases

## Usage

```cmd
# Open a file directly
PrettyMark.exe README.md

# Or launch without arguments → welcome screen → File → Open
PrettyMark.exe
```

## Architecture Notes

- **Rendering pipeline:** Program.cs reads .md file → passes content to WebView2 via `ExecuteScriptAsync()` → `assets/index.html` renders with marked.js + highlight.js
- **Tab system:** C# manages tab state (`TabInfo` list + `activeTabId`), each tab has its own `FileSystemWatcher`. JS manages tab bar + drawer UI, synchronized via `postMessage`.
- **Drag & drop:** `AllowExternalDrop = true` lets Chromium handle the drop natively. `NewWindowRequested` handler intercepts the new-window attempt, extracts the file path, and opens it as a tab.
- **Navigation guard:** `NavigationStarting` allows only `https://app.local/index.html`; all other navigations are blocked. `file:///` URIs with valid extensions are redirected to `OpenTab`. This prevents Chromium from resolving dropped filenames as relative URLs (e.g. `README.md` → `https://app.local/README.md`).
- **Live reload:** `FileSystemWatcher` per tab; debounced handler re-reads and posts updated content (active tab only)
- **Drawer:** Collapsible sidebar (240px) with file list. State persisted in AppData. Toggle via `Ctrl+B` or hamburger button.
- **Dark mode:** CSS class toggle + stylesheet swap (light/dark variants for both markdown and highlight.js). Persisted in AppData.
- **Zoom:** CSS `zoom` property on `#content-area`, controlled via menu/shortcuts/slider in status bar
- **Print:** `window.print()` via JS; `@media print` CSS block hides drawer, tab bar, status bar, find bar and forces white background. Content area overflow set to visible for multi-page printing.
- **Recent Files:** `AppSettings.RecentFiles` (List<string>, max 10). `AddRecentFile()` deduplicates (case-insensitive) and caps at 10. Submenu rebuilt on open/language-switch. "Clear Recent Files" item at bottom.
- **Session persistence:** `AppSettings.SessionFiles` + `SessionActiveFile`. `SaveSession()` called in OpenTab/CloseTab/SwitchTab. `RestoreSession()` runs in `OnNavigationCompleted` before `_initialFilePath` (CLI arg adds to session, doesn't replace). Inline tab creation (no OpenTab call) to avoid recursive saves.
- **Menu:** WinForms `MenuStrip` with File → Open/Recent Files/Print/Close Tab, Edit → Find, View → Dark Mode/Sidebar/Zoom/Full Screen, ? → About. `ShortcutKeys` on items as fallback; primary shortcut handling is JS-side.
- **Keyboard shortcuts:** `AreBrowserAcceleratorKeysEnabled = false` → keys go to web content as JS `keydown`. JS handler uses `postMessage({ type: 'shortcut', action })` for C# actions; zoom/find handled directly in JS.
- **Find bar:** DOM-based search (no `window.find()` — it steals focus in WebView2). Wraps matches in `<mark>` elements, tracks current index, scrolls to match.
- **Menu auto-close:** JS `mousedown` sends `postMessage({ type: 'click' })`, C# calls `HideDropDown()` only on visible dropdowns (calling on hidden ones steals focus from WebView2).
- **Assets:** bundled via `<Content Include="assets\**\*">` in .csproj, copied to output directory
- **i18n:** JSON files in `assets/lang/` (one per language, keyed `snake_case`). C# loads via `LoadTranslationsStatic()`, resolves language via `ResolveLanguageStatic()` (settings → system UI culture → "en" fallback). `T(key)` helper in both C# and JS. C# injects strings into JS via `ExecuteScriptAsync("setStrings({json})")`. `_applyStrings()` updates DOM elements. Adding a language = adding a JSON file (auto-discovered via directory scan).
- **Messages JS→C#:** `open_url`, `switch_tab`, `close_tab`, `shortcut` (incl. `print`), `click`, `drawer_toggled`
