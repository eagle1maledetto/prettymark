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
- Drag & drop via COM IDropTarget (full path, live reload works)
- Native menu bar: File → Open / Close Tab, View → Dark Mode / Sidebar / Zoom, ? → About
- Keyboard shortcuts: `Ctrl+O` open, `Ctrl+W` close tab, `Ctrl+D` dark mode, `Ctrl+B` sidebar, `Ctrl++`/`Ctrl+-`/`Ctrl+0` zoom
- Welcome screen when launched without arguments

## Project Structure

```
PrettyMark.csproj              # .NET 8 project file
Program.cs                     # Main application (entry point, COM interop, tab management)
nuget.config                   # NuGet feed configuration
assets/index.html              # HTML template with JS rendering logic (tabs, drawer, about)
assets/github-markdown.css     # GitHub-flavored Markdown CSS (light)
assets/github-markdown-dark.css # GitHub-flavored Markdown CSS (dark)
assets/marked.min.js           # Markdown parser (client-side)
assets/highlight.min.js        # Syntax highlighting
assets/highlight-github.min.css # Highlight.js github theme (light)
assets/highlight-github-dark.min.css # Highlight.js github theme (dark)
test.md                        # Test file for verifying rendering
```

## Dependencies

- **.NET 8** — runtime and SDK
- **Microsoft.Web.WebView2** 1.0.2739.15 — WebView2 control for WinForms (EdgeChromium)
- **marked.js** — client-side Markdown → HTML (bundled in assets)
- **highlight.js** — client-side syntax highlighting (bundled in assets)

## Build (Windows)

### Prerequisites

1. .NET 8 SDK installed: https://dotnet.microsoft.com/download/dotnet/8.0

### Run in development

```cmd
cd C:\path\to\prettymark
dotnet run -- test.md
```

### Build the executable

```cmd
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\PrettyMark.exe`

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
- **Drag & drop:** COM `IDropTarget` registered on WebView2's internal Chrome widget HWND via `RegisterDragDrop`. Extracts file paths from CF_HDROP. WebView2's own drop is disabled (`AllowExternalDrop = false`).
- **Live reload:** `FileSystemWatcher` per tab; debounced handler re-reads and posts updated content (active tab only)
- **Drawer:** Collapsible sidebar (240px) with file list. State persisted in AppData. Toggle via `Ctrl+B` or hamburger button.
- **Dark mode:** CSS class toggle + stylesheet swap (light/dark variants for both markdown and highlight.js). Persisted in AppData.
- **Zoom:** CSS `zoom` property on `document.body`, controlled via menu or WebView2 JS interop
- **Menu:** WinForms `MenuStrip` with File → Open/Close Tab, View → Dark Mode/Sidebar/Zoom, ? → About
- **Assets:** bundled via `<Content Include="assets\**\*">` in .csproj, copied to output directory
- **Messages JS→C#:** `open_url`, `switch_tab`, `close_tab`, `drawer_toggled`
