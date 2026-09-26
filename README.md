# LogGrokX

[![Latest release](https://img.shields.io/github/v/release/zhenyatnk/LogGrokX)](https://github.com/zhenyatnk/LogGrokX/releases/latest)
[![Run Unit tests](https://github.com/zhenyatnk/LogGrokX/actions/workflows/run-tests.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/run-tests.yml)
[![Upload Binaries](https://github.com/zhenyatnk/LogGrokX/actions/workflows/build_upload.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/build_upload.yml)
[![Benchmarks](https://github.com/zhenyatnk/LogGrokX/actions/workflows/benchmarks.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/benchmarks.yml)
[![Release](https://github.com/zhenyatnk/LogGrokX/actions/workflows/release.yml/badge.svg)](https://github.com/zhenyatnk/LogGrokX/actions/workflows/release.yml)

A fast WPF log viewer for very large log files. LogGrokX parses structured
log lines with configurable regular expressions, builds in-memory indexes for
columns and fields, and lets you search, filter, colorize and mark lines while
remaining responsive even on multi-gigabyte files.

## Features

- **Large file support** — files are streamed and indexed in the background
  instead of being loaded into memory; UI virtualization keeps scrolling fast.
- **Configurable log formats** — describe your log layout with named-capture
  .NET regular expressions. Multiple formats can be configured, each with its
  own indexed fields.
- **Column / field indexing** — fields declared under `IndexedFields` are
  available as filterable columns and as search facets.
- **Search** — regex search with a progress indicator, result navigation and an
  autocomplete cache. Search patterns can be saved and reused.
- **Filtering** — filter by indexed column values directly from the grid, with
  removable filter chips.
- **Time filter & timeline** — a minimap/timeline strip (top or bottom) to
  filter by time range (or line numbers when no timestamps are available) and to
  navigate through marked lines. Hovering the strip shows the timestamp under the
  cursor (or the line number) with a vertical guide line.
- **JSON folding** — multi-line JSON blobs and oversized strings are formatted
  and can be expanded/collapsed inline. The folding state is shared across the
  log grid, search results and the marked-lines view of the same document.
- **Color rules** — highlight matching lines and text with rules in
  `appsettings.yaml`; colors adapt to the active theme.
- **Marked lines** — mark interesting lines and browse them in a dedicated view.
- **Text zoom** — scale log text with `Ctrl` + mouse wheel or `Ctrl` + `+`/`-`,
  and reset with `Ctrl` + `0`; the size is persisted in `appsettings.yaml`.
- **Centered navigation** — jumping to a search hit (including `F3`) or a marked
  line centers the target row in the grid.
- **Thread grouping** — group consecutive visible lines that share the same
  `Thread` field, separated by subtle dividers and with the thread value dimmed
  on continuation rows. Toggled from the title bar and persisted in
  `appsettings.yaml`.
- **Text transformations** — rewrite matched fragments of a line before display
  (for example Base64/JSON decoding) via `Transformations`.
- **XOR-masked logs** — transparently de-obfuscate XOR-encoded log files.
- **Light & dark themes** — switch theme from the title bar; chrome, log colors
  and search highlighting follow the active theme. The window uses solid theme
  colors rather than the Windows 11 Mica material, which is incompatible with
  AvalonDock's auto-hide flyout.
- **Crash dumps** — optional Windows Error Reporting local dumps for diagnostics.
- **Support window** — version, commit, runtime/OS info and links to releases,
  issues and source, plus "copy diagnostics" and "open logs folder"
  (`?` button in the title bar).
- **Multiple documents** — dockable tabs powered by AvalonDock.
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
- [📦 JSON folding](./FEATURES.md#-json-folding)
- [🎨 Color rules](./FEATURES.md#-color-rules)
- [📌 Marked lines](./FEATURES.md#-marked-lines)
- [🔠 Text zoom](./FEATURES.md#-text-zoom)
- [🎯 Centered navigation](./FEATURES.md#-centered-navigation)
- [🧵 Thread grouping](./FEATURES.md#-thread-grouping)
- [🔁 Text transformations](./FEATURES.md#-text-transformations)
- [🔐 XOR-masked logs](./FEATURES.md#-xor-masked-logs)
- [🌗 Light and dark themes](./FEATURES.md#-light-and-dark-themes)
- [🩺 Crash dumps](./FEATURES.md#-crash-dumps)
- [🛟 Support window](./FEATURES.md#-support-window)
- [📑 Multiple documents](./FEATURES.md#-multiple-documents)
- [🧬 Merged files view](./FEATURES.md#-merged-files-view)

## Requirements

- Windows
- .NET SDK 10.0.100 or later (see `global.json`)
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
there you can open the latest release, report an issue, view the source, copy
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
    # Combine all open documents into one time-ordered grid
    MergedFilesView: false

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
portable ZIPs and `SHA256SUMS.txt` to a GitHub Release.
