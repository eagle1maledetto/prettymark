# PrettyMark

A lightweight, native Markdown viewer for Windows with live reload and syntax highlighting.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **GitHub-style rendering** — tables, fenced code blocks, task lists, TOC via [marked.js](https://marked.js.org/)
- **Syntax highlighting** — automatic language detection via [highlight.js](https://highlightjs.org/) (GitHub theme)
- **Live reload** — preview updates instantly when the file is saved
- **Multi-tab** — open multiple files in a single window
- **Sidebar** — collapsible drawer with list of open files and paths
- **Drag & drop** — drop `.md` files directly into the window
- **Find in page** — `Ctrl+F` with match highlighting and navigation
- **Dark mode** — toggle with `Ctrl+D`, preference is remembered
- **Multi-language** — 12 languages (EN, IT, ES, PT, FR, DE, ZH, JA, KO, RU, TR, UK), auto-detects system language, switchable at runtime via View → Language
- **Full screen** — `F11` toggle
- **Zoom** — `Ctrl++` / `Ctrl+-` / `Ctrl+0`
- **Native** — single `.exe`, no Electron, no browser required (uses WebView2/EdgeChromium)

## Screenshot

<!-- TODO: add screenshot -->

## Quick Start

### Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run from source

```cmd
git clone https://gitlab.com/eagle1/prettymark.git
cd prettymark
dotnet run -- README.md
```

### Build a standalone executable

```cmd
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

The output is a single `PrettyMark.exe` in `bin\Release\net8.0-windows\win-x64\publish\`.

## Usage

```cmd
# Open a file directly
PrettyMark.exe README.md

# Launch without arguments → welcome screen → File → Open or drag & drop
PrettyMark.exe
```

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open file |
| `Ctrl+W` | Close current tab |
| `Ctrl+F` | Find in page |
| `Ctrl+D` | Toggle dark mode |
| `Ctrl+B` | Toggle sidebar |
| `F11` | Toggle full screen |
| `Ctrl++` | Zoom in |
| `Ctrl+-` | Zoom out |
| `Ctrl+0` | Reset zoom |

## How It Works

PrettyMark is a WinForms application that hosts a [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) control (EdgeChromium). Markdown files are read by C# and passed to the embedded HTML page, which renders them client-side using marked.js and highlight.js.

- **Tab management** is handled in C# — each open file gets its own `FileSystemWatcher` for live reload
- **Drag & drop** uses native Chromium drop handling, intercepted via `NewWindowRequested` to open files as tabs
- **Settings** (dark mode, sidebar state, language) are persisted in `%AppData%\PrettyMark\settings.json`
- **i18n** uses JSON files in `assets/lang/` — C# loads the JSON and injects strings into JS via `setStrings()`. Adding a language is just adding a new JSON file

## License

MIT
