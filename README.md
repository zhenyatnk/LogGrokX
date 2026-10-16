# LogGrokX

[![Latest release](https://img.shields.io/github/v/release/zhenyatnk/LogGrokX)](https://github.com/zhenyatnk/LogGrokX/releases/latest)
[![Run Unit tests](https://github.com/zhenyatnk/LogGrokX/actions/workflows/run-tests.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/run-tests.yml)
[![Upload Binaries](https://github.com/zhenyatnk/LogGrokX/actions/workflows/build_upload.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/build_upload.yml)
[![Benchmarks](https://github.com/zhenyatnk/LogGrokX/actions/workflows/benchmarks.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/benchmarks.yml)

A fast WPF log viewer for very large log files. LogGrokX parses structured
log lines with configurable regular expressions, builds in-memory indexes for
columns and fields, and lets you search, filter, colorize and mark lines while
remaining responsive even on multi-gigabyte files.

## Installation

LogGrokX is **Windows-only** (Windows 10 or later) and is published for **x64**
and **x86**. Download the latest build from the
[Releases page](https://github.com/zhenyatnk/LogGrokX/releases/latest).

### Installer (recommended)

| Windows   | File                                |
| --------- | ----------------------------------- |
| 64-bit    | `LogGrokX-<version>-x64-setup.exe`  |
| 32-bit    | `LogGrokX-<version>-x86-setup.exe`  |

The installer is **self-contained** — no .NET runtime needs to be installed — and
offers:

- a Start Menu shortcut and an optional desktop shortcut;
- an optional **.log** file association;
- **Install updates automatically** (checked downloads and installs new versions
  silently on exit; unchecked only notifies — see
  [Automatic updates](./FEATURES.md#-automatic-updates));
- a standard uninstaller in **Add/Remove Programs**.

Setup installs **per-user** by default; choose to install for all users to place
LogGrokX under `Program Files` (this asks for elevation). Upgrading keeps your
existing `appsettings.yaml`.

The interface, settings and installer support English, Russian, German, French,
Spanish, Japanese, Polish and Brazilian Portuguese. A fresh installation uses
the language selected in Setup; portable builds use the system language, falling
back to English. Change it under **Settings -> View -> Language**, then restart.
The choice is saved as `Settings.ViewSettings.Language` (`system`, `en`, `ru`,
`de`, `fr`, `es`, `ja`, `pl`, `pt-BR`) and is preserved on upgrade. Log contents,
field names (including column headers) and configuration keys retain their
original text.

### Portable

No installation needed — extract the archive and run `LogGrokX.exe`.

| Variant                              | 64-bit                                        | 32-bit                                        | Needs .NET runtime |
| ------------------------------------ | --------------------------------------------- | --------------------------------------------- | ------------------ |
| Self-contained (recommended)         | `LogGrokX-<version>-win-x64-selfcontained.zip` | `LogGrokX-<version>-win-x86-selfcontained.zip` | no                 |
| Framework-dependent                  | `LogGrokX-<version>-win-x64-portable.zip`      | `LogGrokX-<version>-win-x86-portable.zip`      | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Portable builds cannot update themselves: the **Update** button opens the release
page instead of installing silently.

### Verify the download

Every release ships `SHA256SUMS.txt`. Compare the hash of the file you downloaded:

```powershell
Get-FileHash .\LogGrokX-<version>-x64-setup.exe -Algorithm SHA256
```

### Open a log from the command line

```powershell
LogGrokX.exe C:\logs\app.log C:\logs\worker.log
```

## Features

- **Large file support** — files are streamed and indexed in the background
  instead of being loaded into memory; UI virtualization keeps scrolling fast.
- **Configurable log formats** — describe your log layout with named-capture
  .NET regular expressions. Multiple formats can be configured, each with its
  own indexed fields.
- **Big lines** — oversized lines are pruned (truncated with an ellipsis) or
  wrapped into fixed-size chunks (`BigLine` / `BigLineSize`), so a single
  malformed record cannot stall the grid or bury everything around it.
- **Column / field indexing** — fields declared under `IndexedFields` are
  available as filterable columns and as search facets.
- **Search** — regex search with match-case and regex toggles, a progress
  indicator, result navigation and an
  autocomplete cache. Search patterns can be saved and reused.
- **Filtering** — filter by indexed column values directly from the grid, with
  removable filter chips.
- **Time filter & timeline** — a minimap/timeline strip (top or bottom) to
  filter by time range (or line numbers when no timestamps are available) and to
  navigate through marked lines. Hovering the strip shows the timestamp under the
  cursor (or the line number) with a vertical guide line.
- **JSON/XML folding** — inline JSON and XML blobs and oversized strings are
  formatted and can be expanded/collapsed inline. The folding state is shared across the
  log grid, search results and the marked-lines view of the same document.
- **HEX to text** — cell values that contain HEX-encoded text (UTF-8 or
  UTF-16LE, blocks longer than 64 bits, e.g. `48656C6C6F20776F726C64`, `48 65 6C 6C 6F 20 77 …`,
  `0x48, 0x65, …`; shorter values such as `0x80070005` are treated as numbers) are decoded
  by the same row `BIN` toggle as Base64 (or per cell with "Decode HEX" in the
  context menu), including HEX values inside JSON strings and XML, which keep
  their folding.
- **Color rules** — highlight matching lines and text with rules in
  `appsettings.yaml`; colors adapt to the active theme.
- **Marked lines** — mark interesting lines; they are highlighted across the full
  row in the grid (and merged view) and can be browsed in a dedicated view.
- **Text zoom** — scale log text with `Ctrl` + mouse wheel or `Ctrl` + `+`/`-`,
  and reset with `Ctrl` + `0`; the size is persisted in `appsettings.yaml`.
- **Centered navigation** — jumping to a search hit (including `F3`) or a marked
  line centers the target row in the grid.
- **Thread grouping** — group consecutive visible lines that share the same
  `Thread` field, separated by subtle dividers and with the thread value dimmed
  on continuation rows. Rows of threads other than the current line are dimmed
  and search results are limited to the current thread. Toggled from the title
  bar and persisted in `appsettings.yaml`.
- **Text transformations** — rewrite matched fragments of a line before display
  (for example Base64/JSON decoding) via `Transformations`.
- **Base64 decoding** — rows with Base64 that decodes to readable UTF-8 text
  (a whole value or a fragment such as a JWT or `payload=...`), multi-line PEM
  blocks, or plain Base64 DER certificates, public keys and CryptoAPI RSA key
  blobs get a single `BIN` toggle at the right edge of the `Component` column
  (under the pin if there is no `Component` field), so the text does not shift
  and the row does not grow. It shows the decoded content in place: text,
  formatted and foldable JSON, a certificate summary or a hex dump. Values
  inside JSON and XML are unescaped before detection and re-escaped after
  decoding, so the document stays valid and foldable. Per cell, Base64 and PEM
  can also be toggled separately with "Decode Base64" / "Decode PEM" in the
  context menu. Detection is on by default and can be turned off completely or
  per kind (PEM, Base64, HEX) in Settings → View → Binary data detection.
- **Value mappings** — show a readable meaning next to codes and identifiers:
  a value from a mapping list is displayed as `value(mapping)`, for example
  `0x00001(type 1)` or `0x80070005(E_ACCESSDENIED)`. Lists can be limited to
  specific fields, are case-sensitive or not, are edited in Settings → Value
  mappings and are imported/exported as JSON. Search, filters and indexes still
  work on the original values.
- **XOR-masked logs** — transparently de-obfuscate XOR-encoded log files.
- **Light & dark themes** — switch theme from the title bar; chrome, log colors
  and search highlighting follow the active theme. The window uses solid theme
  colors rather than the Windows 11 Mica material, which is incompatible with
  AvalonDock's auto-hide flyout.
- **Crash dumps** — optional Windows Error Reporting local dumps for diagnostics.
- **Support window** — version, commit, runtime/OS info and links to releases,
  issues and source, a manual update check, plus "copy diagnostics" and "open logs folder"
  (`?` button in the title bar).
- **Automatic updates** — checks GitHub for a new release at most once a day,
  shows a dialog with release notes and installs the update silently when the
  app is closed. Modes: do not check, check, or install automatically (Settings, installer); a manual check
  is available in the Support window. After an update is applied, a **What's new**
  window shows the release notes on the next launch.
- **Multiple documents** — dockable tabs powered by AvalonDock. Open files from
  the command line or by drag & drop, and use the tab context menu to copy a
  path or file name or open the containing folder.
- **Grid conveniences** — horizontal scrolling, auto-width columns, and
  copy-as-displayed / copy-as-native for whole rows or single cells.
- **Settings** — a tabbed settings window (View, Colors, Log formats, Value
  mappings, Debug) that applies changes without a restart and mirrors
  `appsettings.yaml`.
- **Merged files view** — combine several opened logs into one time-ordered grid.
  Columns are aligned across different log formats, rows are tinted per source,
  and search, time-range filtering, thread grouping, JSON folding and marking
  all work as in a single document. Toggled from the title bar or Settings and
  persisted in `appsettings.yaml`.

### Feature guides

In-depth walkthroughs with Full HD screenshots, business context and code
snippets for every feature live in [FEATURES.md](./FEATURES.md):

- [⚡ Large file support](./FEATURES.md#-large-file-support)
- [🧩 Configurable log formats](./FEATURES.md#-configurable-log-formats)
- [📊 Column and field indexing](./FEATURES.md#-column-and-field-indexing)
- [🔍 Search](./FEATURES.md#-regex-search)
- [🧾 Filtering](./FEATURES.md#-filtering)
- [⏳ Time filter and timeline](./FEATURES.md#-time-filter-and-timeline)
- [📦 JSON/XML folding](./FEATURES.md#-jsonxml-folding)
- [🔣 HEX to text](./FEATURES.md#-hex-to-text)
- [🎨 Color rules](./FEATURES.md#-color-rules)
- [📌 Marked lines](./FEATURES.md#-marked-lines)
- [🔠 Text zoom](./FEATURES.md#-text-zoom)
- [🎯 Centered navigation](./FEATURES.md#-centered-navigation)
- [🧵 Thread grouping](./FEATURES.md#-thread-grouping)
- [🔁 Text transformations](./FEATURES.md#-text-transformations)
- [🔓 Base64 and PEM decoding](./FEATURES.md#-base64-and-pem-decoding)
- [🏷️ Value mappings](./FEATURES.md#️-value-mappings)
- [🔐 XOR-masked logs](./FEATURES.md#-xor-masked-logs)
- [🧱 Big lines](./FEATURES.md#-big-lines)
- [📑 Files and documents](./FEATURES.md#-files-and-documents)
- [🧭 Grid behaviour](./FEATURES.md#-grid-behaviour)
- [⚙️ Settings](./FEATURES.md#️-settings)
- [🌗 Light and dark themes](./FEATURES.md#-light-and-dark-themes)
- [🩺 Crash dumps](./FEATURES.md#-crash-dumps)
- [🛟 Support window](./FEATURES.md#-support-window)
- [🧬 Merged files view](./FEATURES.md#-merged-files-view)
- [🔄 Automatic updates](./FEATURES.md#-automatic-updates)

## Requirements

### To run

- Windows 10 or later (x64 or x86).
- The **installer** and the **self-contained** portable ZIPs bundle the .NET
  runtime, so nothing else is required.
- The **framework-dependent** portable ZIPs need the
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### To build from source

- .NET SDK 10.0.100 or later (pinned by `global.json`)
- Visual Studio 2022+ or the .NET CLI

## Building

```powershell
dotnet restore
dotnet build --configuration Release
```

The build output is written to `bin\Release\`. Run the application with:

```powershell
dotnet run --project LogGrokX\LogGrokX.csproj
```

## Testing

The solution contains two test projects: `LogGrokX.Tests` and
`LogGrokX.Data.Tests`.

```powershell
dotnet test
```

## Benchmarks

Micro-benchmarks for the core data layer live in `LogGrokX.Benchmarks` and are
powered by [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet). The
suite covers line parsing (`LineParsingBenchmark`), stream loading
(`LoaderBenchmark`) and the merge core (`MergeBenchmark`:
`MergedLineOrder.Build`, `MergedLineOrder.BuildTimeIndex`,
`TimeIndex.FindLineRange`).

```powershell
dotnet run --project LogGrokX.Benchmarks -c Release
```

Pass `--list flat` to list the benchmarks, `--filter` to select a subset, or
`--job Dry` for a quick smoke run. Reports are written to
`BenchmarkDotNet.Artifacts/`.

A smoke run executes on every push and pull request. The nightly **Benchmarks**
workflow runs the full suite only when the branch has changed since the last
successful run, and uploads the reports as a build artifact.

## Support

The **Support** window (the `?` button in the title bar) shows the application
version, git commit and branch, .NET runtime, OS and CPU architecture. From
there you can check for updates, open the latest release, report an issue, view the source, copy
the diagnostics block to the clipboard, or open the log folder at
`%LOCALAPPDATA%\LogGrokX\`.

## Project layout

| Project              | Description                                                       |
| -------------------- | ----------------------------------------------------------------- |
| `LogGrokX`        | WPF application: views, view models, controls, theming.           |
| `LogGrokX.Data`   | Platform-agnostic core: stream loading, line parsing, indexes, search, virtualization. |
| `LogGrokX.Tests`  | Tests for the UI layer.                                           |
| `LogGrokX.Data.Tests` | Tests for the core data layer.                                |
| `LogGrokX.Benchmarks` | BenchmarkDotNet benchmarks for the core data layer.          |

The UI is built on [WPF-UI](https://github.com/lepoco/wpfui) (Fluent controls
and theming) with [AvalonDock](https://github.com/Dirkster99/AvalonDock) for
docking.

### `LogGrokX.Data`

- `Loader` / `LoaderImpl` — buffered, line-aware stream reader.
- `RegexBasedLineParser` — parses lines using the configured log formats.
- `IndexTree`, `LineIndex`, `SearchLineIndex` — in-memory indexes and
  search-result to source-line mapping.
- `Search` / `Pipeline` — asynchronous regex search pipeline.
- `Virtualization` — `IItemProvider`/`VirtualList` abstractions consumed by the UI.
- `MergedLineOrder`, `MergeSource`, `MergedLineRef` — k-way, time-ordered merge
  of several parsed logs.
- `TimeIndex` — normalized, day-aware timestamps with monotonic bounds and range
  lookup, shared by the single-log and merged timelines.

## Configuration

Settings live in `appsettings.yaml`, next to the executable. The file is
watched and reloaded at runtime. Open it from the app with the
**settings** button in the title bar.

```yaml
Settings:
  DebugSettings:
    EnableCrashDumps: false
    MaxDumpsCount: 10

  ColorSettings:
    Rules:
      - RegexString: \tERR\t
        ForegroundColor: Red
      - RegexString: (?i)fatal
        BackgroundColor: "#FFCD5C5C"

  ViewSettings:
    # BigLine: prune|break
    BigLine: prune
    BigLineSize: 4096
    # Log text size in points; adjustable from the UI (Ctrl+wheel / Ctrl+0)
    LogFontSize: 12
    # Group consecutive lines that share the same Thread field
    GroupByThread: false
    # Draw horizontal separators between thread groups
    ShowThreadGroupSeparators: true
    # Combine all open documents into one time-ordered grid
    MergedFilesView: false
    # Binary data detection (BIN): DetectBinary turns it off completely,
    # the other keys select the kinds
    DetectBinary: true
    DetectPem: true
    DetectBase64: true
    DetectHex: true
    # Updates: disabled | check (notify about a new version) | install (install silently on exit)
    UpdateMode: check

  # Shown as value(mapping); Fields empty = every field
  ValueMappings:
    - Name: Types
      Enabled: true
      IgnoreCase: true
      Fields:
        - Message
      Items:
        - Value: 0x00001
          Mapping: type 1

  LogFormats:
    - Regex: ^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)
      IndexedFields:
        - Level
        - Thread
        - Component
```

### Log format options

| Option            | Description                                                        |
| ----------------- | ------------------------------------------------------------------ |
| `Regex`           | Named-capture regex describing one logical log line.               |
| `IndexedFields`   | Capture group names exposed as indexed/filterable columns.         |
| `Transformations` | Ordered rewrite rules applied to matched line fragments.           |
| `XorMask`         | XOR mask used to decode encrypted log files.                       |

## Downloads

Build artifacts are produced by the **Upload Binaries** workflow and attached to
the workflow run under `LogGrokX-build-<run_number>`.

Tagged releases are published by the **Release** workflow, which runs on `v*`
tags (or manually via `workflow_dispatch` with a version input). It runs the
tests, publishes self-contained and framework-dependent builds for x64 and x86,
builds Inno Setup installers, optionally signs them, and attaches the installers,
portable ZIPs and `SHA256SUMS.txt` to a GitHub Release. Installed copies use
these assets for automatic updates; the installer's **Install updates
automatically** option sets `UpdateMode: install` (unchecked — `UpdateMode: check`).
