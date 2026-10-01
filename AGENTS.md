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
loading (`LoaderBenchmark`) and the merge core (`MergeBenchmark`:
`MergedLineOrder.Build`, `MergedLineOrder.BuildTimeIndex`,
`TimeIndex.FindLineRange`).

The UI layer uses **WPF-UI 4.3.0** (Fluent controls/theming) and
**AvalonDock 5** for docking. Branding/window title is **LogGrokX** plus the
build version: `BuildInfo.Version` (release `-p:Version`, default `2.1`).
JSON folding lives in `Controls/TextRender` (`TextView`,
`TextViewSharedFoldingState`, `CollapsibleRegionsMachine`, `FoldingManager`).
JSON and XML fragments are detected by `TextOperations.GetStructuredRanges`
(outer range wins on overlap), formatted by `FormatInlineStructured`, and
`TextModel` builds collapsible line ranges from braces (JSON) or
`GetXmlElementRanges` (XML). XML is recognized only when the root element has
child elements, so text like `List<int>` or `<br/>` stays plain (#49).
Thread grouping is driven by `ThreadGroupingService` (toggled from the title bar,
persisted as `ViewSettings.GroupByThread`); group boundaries are attached
properties (`IsGroupFirst` / `IsGroupLast` / `IsGroupContinuation`) on
`Controls/ListControls/BaseLogListViewItem`, computed by
`Controls/ListControls/VirtualizingStackPanel` and rendered by
`Styles/ListViewItemStyle.xaml`. The support window is `SupportWindow.xaml` /
`SupportViewModel` (opened through `OpenSupportCommand`).

The timeline/minimap strip is `Controls/LogMinimapControl.cs` (a
`FrameworkElement` that draws the time-range handles, markers, search matches and
a hover readout). Hovering shows a vertical guide line plus the timestamp under
the cursor (or `Line N` in line-number mode) drawn directly in the canvas via
`DrawHoverTime`/`GetHoverText`, so it does not use a WPF `ToolTip`. Placement is
controlled by `TimelinePlacementService` and persisted as
`ViewSettings.TimelineAtTop`.

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
- **JSON folding performance**: `TextView` keeps one cached `GlyphLine` per
  source line (`_textLines`, plus its collapsed state) and builds glyphs only
  for lines visible under the current folding, so toggling a region re-creates
  just the affected lines. `TextModel.GetCollapsedTextSubstitution` memoizes the
  inlined text of collapsed regions. Avoid reintroducing full re-creation of all
  lines on every folding change — it makes expanding large JSON sluggish (#38).
- **Merged timeline range**: `MergedViewModel` keeps the **full** merged buffer
  (`_mergedBuffer`) and derives the timeline axis (`Minimum`/`Maximum`,
  `TotalLineCount`, `TimelineSegments`, `TimeRangeFilter.Refresh`) from it, while
  the grid/search run on the filtered `_visibleBuffer` mapped back through
  `_visibleToFull` (`VisibleLines`, `GetVisibleFullIndexMap`, `GetVisibleIndex`).
  Deriving the axis from the filtered buffer makes
  `MergedTimeRangeFilterViewModel.Refresh` clamp the handles to the shrunken
  bounds and silently reset the time range.
- **Update check**: on startup `UpdateCheckService` resolves the latest tag from
  the redirect of the `https://github.com/.../releases/latest` web page (not the
  API: unauthenticated API allows 60 requests/hour per IP, shared behind corporate
  NAT, #40). Only if the tag is newer than `BuildInfo.Version` (`UpdateVersion`)
  and not skipped, it calls the `releases/tags/<tag>` API for notes and assets; on
  403/429 it remembers the reset time (`X-RateLimit-Reset` / `Retry-After`) and uses
  `CreateFallbackRelease` (well-known asset URLs, no notes). Then it shows
  `UpdateWindow` / `UpdateViewModel`. "Update"
  downloads `LogGrokX-<ver>-<arch>-setup.exe` to `%TEMP%\LogGrokX\Update`,
  verifies it against `SHA256SUMS.txt`, and `App.OnExit` starts it silently
  (`/VERYSILENT /AUTOUPDATE=1`, `/ALLUSERS` + UAC for Program Files installs)
  after the process exits. Portable builds (no `unins000.exe`) only open the
  release page. The mode is `ViewSettings.UpdateMode` (`disabled`/`check`/`install`,
  Settings -> View); `install` downloads without a dialog. The installer task
  `autoinstallupdates` writes `install`/`check` to `appsettings.yaml` except during
  `/AUTOUPDATE=1` runs. Local builds default to `2.1`, so they never prompt.
  When an installer is downloaded, its release notes are saved to
  `%LOCALAPPDATA%\LogGrokX\Data\pending-update.json`; on the next launch
  `App.OnStartup` calls `UpdateCheckService.TakePendingReleaseForCurrentVersion`,
  which returns the notes only if the stored version matches `BuildInfo.Version`
  (a `v`-prefix is ignored), deletes the file either way, and `WhatsNewWindow` /
  `WhatsNewViewModel` shows them.
- **Search results selection**: the search pane's `VirtualizingStackPanel` sets
  `ReplaceSelectionOnCurrentPosition="True"` so F3/next/previous replace the
  selection. Mouse and Shift+arrow selection must change `CurrentPosition`
  through `SetCurrentPositionKeepingSelection`, otherwise Ctrl/Shift+click
  multi-selection collapses to a single line (#43). The selected search lines
  flow to the timeline as `ISearchDocument.SelectedMatchLines` ->
  `SearchViewModel.CurrentSelectedMatchLines` -> `LogViewModel.SearchSelectedLines`
  -> `LogMinimapControl.MatchLines` (merged view binds
  `Search.CurrentSelectedMatchLines`). They are drawn like `MatchLine` but
  semi-transparent; the current line (`MatchLine`) stays opaque on top.
- **Base64 decoding** (#35): `LogGrokX.Data/Base64Detector` finds Base64 in a
  cell value (whole value, optionally quoted, from 8 chars; fragments from 16
  chars; standard or URL-safe alphabet) and accepts it only if it decodes to
  valid UTF-8 without control characters. Multi-line PEM blocks are matched
  first (`PemRegex`, real or escaped `\n` line breaks, same label in BEGIN/END);
  the markers are kept and the body becomes text, an X.509 summary
  (`X509CertificateLoader`, certificate labels only) or a hex dump capped at
  `MaxHexDumpBytes`. Fragment search runs only outside PEM blocks. A candidate glued to
  surrounding text by `/`, `-`, `_` or `+` (`api/v1/eyJ...`, `X-KSN-eyJ...`,
  `..._v2`) is retried as its suffixes/prefixes at those separators; the split
  with the best `GetTextScore` wins (a misaligned split decodes to junk such as
  `)#~` and loses).
  Binary Base64 without markers is accepted only by `TryDescribeBinaryKey`
  (token from 64 chars starting with `MI`/`Bg`): DER X.509 certificate, DER
  SubjectPublicKeyInfo or CryptoAPI `PUBLICKEYBLOB` (`RSA1`); it is reported as
  `Base64Content.Pem`.
  Inside JSON/XML the caller passes `StructuredSpan`s (from
  `TextOperations.GetStructuredRanges`, mapped in `LinePartViewModel`):
  `CollectJson` decodes only string values (not keys) after
  `JsonSerializer.Deserialize<string>` and writes back nested JSON / an array of
  lines / an escaped string (`EncodeJsonValue`); `CollectXml` handles element
  text, attribute values and CDATA via `WebUtility.HtmlDecode` and re-escapes.
  Text outside the spans goes through the plain path.
  `Base64Detector.Detect` returns `Base64Content` flags (`Pem`, `Base64`), and
  `TryDecode(source, Base64Content, ...)` decodes only the selected kinds.
  `LinePartViewModel.IsPem` / `IsBase64` are computed lazily on first binding;
  `IsPemDecoded` / `IsBase64Decoded` are independent and swap `TextModel` to a
  model cached per flag combination with its own `UniqueId` (so folding state of
  the decoded JSON does not clash with the original). The single `BIN` toggle
  is row-level: `BaseLogLineViewModel.IsDecodable` (= `IsPem || IsBase64`) and
  `IsDecoded` aggregate `GetDecodableParts()` (all fields of
  `LineViewModel`/`MergedLineViewModel`, `Text` of `MarkedLineViewModel`) and
  follow part changes; setting `IsDecoded` decodes every available kind. It is
  rendered by `DecodeTogglesTemplate`
  (`Styles/LogGridViewCellStyle.xaml`) at the right edge of the `Component`
  field column (`GridViewFactory.CreateView`); if the format has no `Component`
  field they fall back under the pin in the pin column
  (`GridViewFactory.CreatePinCellTemplate`, `MarkedLinesViewTemplate.xaml`;
  `PinColumnMinWidth` keeps the column wide enough). The
  "Decode PEM" / "Decode Base64" items in `Styles/LogViewContextMenu.xaml` bind to
  `PlacementTarget.DataContext` (collapsed when it is not a `LinePartViewModel`).
  The index column is created with `detectBase64: false`.
  `LineViewModel.GetDisplayText` uses the decoded model for decoded parts, so
  "Copy" copies what is shown.
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
