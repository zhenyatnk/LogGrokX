# AGENTS.md

Guidance for AI agents working in this repository.

## Project

**LogGrokX** — a fast WPF log viewer for very large log files. It parses
structured log lines with configurable regex formats, builds in-memory indexes,
and supports search, filtering, colorization and marking while streaming files
that may be many gigabytes.

- Platform: **Windows only** (WPF).
- Target framework: `net10.0-windows` / `net10.0`.
- SDK pinned by `global.json` (10.0.100, `rollForward: latestMajor`).

## Commands

Run from the repository root unless noted. The OS shell is Windows PowerShell.

```powershell
dotnet restore
dotnet build LogGrokX.sln
dotnet test LogGrokX.sln
dotnet run --project LogGrokX\LogGrokX.csproj
dotnet run --project LogGrokX.Benchmarks -c Release
dotnet format LogGrokX.sln
```

- The build treats several warnings as errors (`NU1605` and the nullable
  `CS86xx` family). A clean build must have **0 warnings**.
- Build output is redirected to `bin\Debug\` / `bin\Release\` (not the default
  per-project `bin`).
- After changing code, always verify with `dotnet build` and `dotnet test`.
- Do not commit unless the user explicitly asks.

## Layout

| Project | Purpose |
| --- | --- |
| `LogGrokX` | WPF app: views, view models, controls, theming, DI bootstrap. |
| `LogGrokX.Data` | UI-agnostic core: stream loading, line parsing, indexes, search, virtualization. |
| `LogGrokX.Tests` | Tests for the UI layer. |
| `LogGrokX.Data.Tests` | Tests for the core data layer. |
| `LogGrokX.Benchmarks` | BenchmarkDotNet benchmarks for the core data layer. |

Key areas in `LogGrokX.Data`: `Loader`/`LoaderImpl` (buffered line-aware
reader), `RegexBasedLineParser`, `IndexTree`/`LineIndex`/`SearchLineIndex`,
`Search`/`Pipeline`, `Virtualization`, and `MergedLineOrder`/`MergeSource`/
`MergedLineRef`/`TimeIndex` (k-way, time-ordered merge of several parsed logs).

`LogGrokX.Benchmarks` covers line parsing (`LineParsingBenchmark`), stream
loading (`LoaderBenchmark`), end-to-end loading and indexing
(`IndexingPipelineBenchmark`), the search hot loop (`SearchBenchmark`) and the
merge core (`MergeBenchmark`: `MergedLineOrder.Build`,
`MergedLineOrder.BuildTimeIndex`, `TimeIndex.FindLineRange`).
Hot-path design notes, measurements and the invariants the (opt-in) parallel
loader must preserve are in `docs/performance-notes.md`.

The UI layer uses **WPF-UI 4.3.0** (Fluent controls/theming) and
**AvalonDock 5** for docking. Branding/window title is **LogGrokX** plus the
build version: `BuildInfo.Version` (release `-p:Version`, default `2.1`).
JSON folding lives in `Controls/TextRender` (`TextView`,
`TextViewSharedFoldingState`, `CollapsibleRegionsMachine`, `FoldingManager`).
Thread grouping is driven by `ThreadGroupingService` (toggled from the title bar,
persisted as `ViewSettings.GroupByThread`); group boundaries are attached
properties (`IsGroupFirst` / `IsGroupLast` / `IsGroupContinuation`) on
`Controls/ListControls/BaseLogListViewItem`, computed by
`Controls/ListControls/VirtualizingStackPanel` and rendered by
`Styles/ListViewItemStyle.xaml`. The support window is `SupportWindow.xaml` /
`SupportViewModel` (opened through `OpenSupportCommand`).

The **merged files view** combines several opened documents into one
time-ordered grid and lives in `MergedView/`: `MergedViewModel` owns the merge,
filtering, timeline and navigation; `MergedDocumentItem` holds one document's
parsed lines; `MergedSchema`/`MergedComponentIndexer` align columns across
formats; `MergedSearchDocumentViewModel` and `MergedTimeRangeFilterViewModel`
mirror the single-log search and time-range panes; `MergedViewPalette` supplies
per-source colors; `MergedLineViewModel`/`ListItemProvider` adapt merged rows to
the grid. The view is toggled by `MergedFilesViewService` (title bar / Settings,
persisted as `ViewSettings.MergedFilesView`) and rendered by
`Styles/MergedViewTemplate.xaml`. Cross-format rules: identical column names are
aligned case-insensitively, plain-text/unrecognized lines put the whole line into
`Message`, and `Level`/`Severity` are unified into a single `Severity` column.
Covered by `MergedLineOrderTests`/`TimeIndexTests` (`LogGrokX.Data.Tests`) and
`MergedSchemaTests` (`LogGrokX.Tests`).

## Conventions

- `LangVersion=latest`, `Nullable=enable`. Prefer file-scoped namespaces.
- **Do not add comments** unless they are necessary to explain non-obvious code.
- Tests use **MSTest** (`[TestClass]`, `[TestMethod]`, `[DataRow]`), not xUnit or
  NUnit. Assertion arguments are `(expected, actual)`.
- Keep changes minimal and mirror the style of neighbouring files.

## Gotchas

- **AvalonDock 5**: the XML layout serializer lives in the separate package
  `Dirkster.AvalonDock.Serializer.Xml`; its namespace is
  `AvalonDock.Serializer.Xml` (not `AvalonDock.Layout.Serialization`).
- **NLog 6**: `nlog.config` must stay compatible with NLog 6 — `concurrentWrites`
  was removed, and `${threadid}` accepts no properties.
- **WPF-UI theming**: switch themes through `ApplicationThemeManager` /
  `Theming/UiThemeService.cs`. Reference `DynamicResource` brushes
  (`ApplicationBackgroundBrush`, `TextFillColorPrimaryBrush`,
  `ControlStrokeColorDefaultBrush`, …) instead of hard-coded colors so both
  themes work. WPF-UI ships *keyed* styles (e.g. `UiGridViewColumnHeaderStyle`),
  so implicit styles are not always picked up — define overrides explicitly in
  `Bootstrap/App.xaml`.
- **Window backdrop**: keep `WindowBackdropType="None"` on every `FluentWindow`
  (`MainWindow`, `SupportWindow`, `SettingsWindow`) and `WindowBackdropType.None`
  in `Theming/UiThemeService.cs`. The Mica material breaks composition of
  AvalonDock's auto-hide flyout (`LayoutAutoHideWindowControl` is an `HwndHost`),
  which then renders blank — most visibly in the light theme.
- **AvalonDock themes**: the main `DockingManager` uses `Vs2013LightTheme` /
  `Vs2013DarkTheme`, but the search pane's inner `DockingManager` merges the
  light `AvalonDock.Themes.Metro` theme. Metro keys (e.g.
  `AvalonDock_ThemeMetro_BaseColor5`) therefore sometimes need theme-aware
  overrides in `Styles/DocumentSearchTemplate.xaml`.
- **JSON folding state** is shared per opened document: `DocumentContainer`
  registers one `TextViewSharedFoldingState` (exposed as `FoldingState` on
  `LogViewModel` / `SearchDocumentViewModel` / `DocumentViewModel`) and the
  templates bind it via `textRender:TextView.SharedFoldingState`. The marked-lines
  view must use `Document.FoldingState` and the same `TextModel.UniqueId` as the
  grid's JSON component, otherwise expansion falls out of sync.
- **Merged timeline range**: `MergedViewModel` keeps the **full** merged buffer
  (`_mergedBuffer`) and derives the timeline axis (`Minimum`/`Maximum`,
  `TotalLineCount`, `TimelineSegments`, `TimeRangeFilter.Refresh`) from it, while
  the grid/search run on the filtered `_visibleBuffer` mapped back through
  `_visibleToFull` (`VisibleLines`, `GetVisibleFullIndexMap`, `GetVisibleIndex`).
  Deriving the axis from the filtered buffer makes
  `MergedTimeRangeFilterViewModel.Refresh` clamp the handles to the shrunken
  bounds and silently reset the time range.
- The app writes diagnostic logs to `%LOCALAPPDATA%\LogGrokX\`.
- Runtime configuration is `appsettings.yaml` (watched and hot-reloaded),
  next to the executable.
- **Build version** is injected by the `GenerateBuildInfo` / `GenerateAppManifest`
  targets in `LogGrokX.csproj`: they write `BuildInfo.g.cs`, `VersionInfo.g.cs`
  and a version-stamped `LogGrokX.generated.manifest`. The manifest is fed to
  the compiler via `Win32Manifest` (overriding `ApplicationManifest` alone is not
  enough, the SDK snapshots it at evaluation). `LogGrokX.exe --version` prints
  the version and exits before WPF starts; the release workflow smoke-tests it.
- **App icon**: `LogGrokX/app.ico` is a multi-frame icon (white glyph with a thin
  dark outline on a transparent background) set as `ApplicationIcon` and used for
  `Window.Icon` / the title-bar `ui:ImageIcon`. WPF decodes only the **first**
  `.ico` frame (16×16), so for larger renders use `LogGrokX/app-large.png`
  (256×256). Both files are `<Resource>` items.

## Verification

Minimum bar for any change:

```powershell
dotnet build LogGrokX.sln -t:Rebuild
dotnet test LogGrokX.sln
```

Both must succeed with 0 warnings and 0 errors.
