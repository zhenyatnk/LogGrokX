# LogGrokX — Feature Guide

This document is the deep-dive companion to the [README](./README.md). It covers
**every feature listed in the README**, explains the business value behind it,
and shows the relevant configuration or core API with a ready-to-adapt snippet.

> All screenshots were captured from the running application using **generated
> demo logs** (`demo-app.log`, `demo-worker.log`), not real product traces. Each
> image is 1920×1080 (Full HD).

## Table of contents

- [⚡ Large file support](#-large-file-support)
- [🧩 Configurable log formats](#-configurable-log-formats)
- [📊 Column and field indexing](#-column-and-field-indexing)
- [🔍 Search](#-regex-search)
- [🧾 Filtering](#-filtering)
- [⏳ Time filter and timeline](#-time-filter-and-timeline)
- [📦 JSON folding](#-json-folding)
- [🎨 Color rules](#-color-rules)
- [📌 Marked lines](#-marked-lines)
- [🔠 Text zoom](#-text-zoom)
- [🎯 Centered navigation](#-centered-navigation)
- [🧵 Thread grouping](#-thread-grouping)
- [🔁 Text transformations](#-text-transformations)
- [🔐 XOR-masked logs](#-xor-masked-logs)
- [🌗 Light and dark themes](#-light-and-dark-themes)
- [🩺 Crash dumps](#-crash-dumps)
- [🛟 Support window](#-support-window)
- [📑 Multiple documents](#-multiple-documents)
- [🧬 Merged files view](#-merged-files-view)
- [🔄 Automatic updates](#-automatic-updates)

## ⚡ Large file support

**Business value.** Production logs routinely reach several gigabytes. LogGrokX
never loads a log into memory: it streams the file through a buffered,
line-aware reader and builds indexes in the background, while UI virtualization
keeps scrolling smooth. A file that would not open in a text editor becomes
interactive within seconds.

![Demo](assets/images/feature-1.png)

```csharp
using LogGrokX.Data;

var logFile = new LogFile(@"C:\logs\app.log", xorMask: 0);

using var loader = new Loader(logFile, lineProcessor, logger);
while (loader.IsLoading)
    await Task.Delay(100);

Console.WriteLine($"{logFile.FileSize} bytes indexed");
```

`LogFile` detects the encoding (including BOM-less UTF-16) and `Loader` drives
the background read. The `IItemProvider`/`VirtualList` abstraction in
`LogGrokX.Data.Virtualization` exposes only the visible window of lines to the
grid.

## 🧩 Configurable log formats

**Business value.** Every product logs differently. Instead of hard-coding a
parser, LogGrokX describes each format with a named-capture .NET regex in
`appsettings.yaml`. Several formats can coexist, so a single installation can
open logs from different components without changing code.

![Demo](assets/images/feature-7.png)

```yaml
LogFormats:
  - Regex: ^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)
    IndexedFields:
      - Level
      - Thread
      - Component
```

```csharp
var format = new LogFormat
{
    Regex = @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)",
    IndexedFields = new[] { "Level", "Thread", "Component" },
    TimeField = "Time",
    TimeFormat = "yyyy-MM-dd HH:mm:ss.fff"
};

if (!format.IsCorrect())
    throw new InvalidOperationException("The log format regex is invalid.");
```

Formats also support `Transformations` (ordered rewrites) and `XorMask`
(decoding encrypted files). See the
[Log format options](./README.md#log-format-options) table in the README.

## 📊 Column and field indexing

**Business value.** Once fields are declared under `IndexedFields`, they become
filterable columns and search facets. Instead of scrolling through millions of
lines, an operator can pivot on `Level`, `Thread` or `Component` and narrow the
view instantly.

![Demo](assets/images/feature-1.png)

```csharp
var format = new LogFormat
{
    Regex = @"^(?<Time>\S+)\s+(?<Level>\w+)\s+(?<Thread>\S+)\s+(?<Message>.*)",
    IndexedFields = new[] { "Level", "Thread" }
};

var indexedFieldNumbers = format.IndexedFieldNumbers;
Console.WriteLine($"Indexing {indexedFieldNumbers.Length} fields");
```

The `Indexer`/`SubIndexer` in `LogGrokX.Data.Index` maps each distinct field
combination to a compact key and stores the line numbers per value, which is
what makes filtering and faceting fast.

## 🔍 Regex search

**Business value.** When a customer reports a failure, the fastest path to a
root cause is a precise search across a multi-gigabyte file. LogGrokX runs the
search asynchronously over the parsed buffer, reports live progress, and records
every hit so navigation and the timeline can jump to a result instantly.

![Demo](assets/images/feature-2.png)

```csharp
using System.Text.RegularExpressions;
using LogGrokX.Data.Search;

var pattern = new Regex(@"Payment authorization failed", RegexOptions.IgnoreCase);

var (progress, searchIndexer, lineIndex) = Search.CreateSearchIndex(
    logModelFacade, pattern, cancellationToken);

await progress.Completion;

for (var hit = 0; hit < lineIndex.Count; hit++)
{
    var (sourceLine, offset, length) = lineIndex.GetLine(hit);
    Console.WriteLine($"Hit {hit} maps to source line {sourceLine}");
}
```

`Search.CreateSearchIndex` returns immediately; the pipeline fills
`searchIndexer` and `lineIndex` on a background task. The search pane keeps an
autocomplete cache and lets frequently used patterns be saved and reused.

## 🧾 Filtering

**Business value.** Filtering removes noise without re-reading the file. Filter
by one or more indexed column values directly from the grid header; removable
chips show what is currently excluded, and the same index powers instant
recomputation of the visible set.

![Demo](assets/images/feature-1.png)

```csharp
IReadOnlyDictionary<int, IEnumerable<string>> excludedComponents =
    new Dictionary<int, IEnumerable<string>>
    {
        [2 /* Thread */] = new[] { "Thread-07", "Thread-08" }
    };

var isVisible = indexer.IsLineIncluded(lineNumber, excludedComponents);
```

`Indexer.IsLineIncluded` checks the precomputed key of a line against the
excluded values, so filtering a multi-gigabyte file stays interactive.

## ⏳ Time filter and timeline

**Business value.** Incidents have a "before" and "after". The timeline strip
(top or bottom) is a minimap of the whole file that lets you select a time range
with draggable handles — or line ranges when no timestamps are available — and
jump through marked lines. Narrowing to the interesting window turns a wall of
text into a readable sequence. Hovering the strip draws a vertical guide line
and the timestamp under the cursor (or `Line N` in line-number mode) directly in
the canvas, so the exact moment of a marker or match can be read without leaving
the strip.

![Demo](assets/images/feature-9.png)

```csharp
var timeline = new TimeIndex();
foreach (var ticks in parsedTimestamps)
    timeline.Add(ticks);

if (timeline.FindLineRange(fromTicks, toTicks) is { } window)
{
    Console.WriteLine($"Visible source lines: {window.StartLine}..{window.EndLine}");
}
```

`TimeIndex` normalizes day-less timestamps (rolling them over midnight) and uses
binary search for `FindLineRange`; it is shared by the single-log and merged
timelines. The placement is persisted as `ViewSettings.TimelineAtTop`. The hover
readout is drawn in `Controls/LogMinimapControl.cs` (`DrawHoverTime` /
`GetHoverText`) using `TimeIndex.GetTicksAt` and `TimestampParser.Format`.

## 📦 JSON folding

**Business value.** Many logs embed structured payloads as multi-line JSON or
oversized strings that drown out the surrounding context. LogGrokX formats and
folds those blobs inline so the default view stays compact, while the folded
content can be expanded on demand. The folding state is shared across the log
grid, search results and the marked-lines view of the same document.

![Demo](assets/images/feature-3.png)

```csharp
using LogGrokX.Controls.TextRender;

// One folding state is registered per opened document and bound by the templates.
var foldingState = new TextViewSharedFoldingState();
```

The implementation lives in `Controls/TextRender`
(`TextView`, `TextViewSharedFoldingState`, `CollapsibleRegionsMachine`,
`FoldingManager`). Templates bind it via `textRender:TextView.SharedFoldingState`
using the same `TextModel.UniqueId`, otherwise expansion falls out of sync.

## 🎨 Color rules

**Business value.** The eye finds errors faster than any search. Color rules
highlight matching lines and fragments, and because they are expressed as regex
rules in `appsettings.yaml`, severity conventions can be tuned per team without
a rebuild. Colors adapt to the active theme.

![Demo](assets/images/feature-2.png)

```yaml
ColorSettings:
  Rules:
    - RegexString: \tERR\t
      ForegroundColor: Red
    - RegexString: \tWRN\t
      ForegroundColor: Chocolate
    - RegexString: (?i)fatal
      BackgroundColor: "#FFCD5C5C"
```

Supported values are .NET `System.Drawing.Color` names or `#AARRGGBB`
notation; a rule without colors keeps the default foreground.

## 📌 Marked lines

**Business value.** During an investigation you often collect a handful of
needle lines across a huge haystack. Marking pins them into a dedicated view so
the evidence can be revisited, copied or compared without repeating the search.

![Demo](assets/images/feature-8.png)

Marked lines are browsed from the dedicated "Marked lines" view; jumping to a
marked line centers the target row in the grid (see
[Centered navigation](#-centered-navigation)).

## 🔠 Text zoom

**Business value.** Operators work on different monitors and at different
distances. Log text can be scaled on the fly and the choice is remembered, so
readability preferences survive restarts.

![Demo](assets/images/feature-1.png)

```csharp
var zoom = container.Resolve<TextZoomService>();

zoom.Increase();   // Ctrl + '+'
zoom.Decrease();   // Ctrl + '-'
zoom.Reset();      // Ctrl + '0'
zoom.SetFontSize(14);

Console.WriteLine($"Current log font size: {zoom.FontSize}");
```

`TextZoomService` clamps the size between its min/max, raises `Changed`, and
persists the value as `ViewSettings.LogFontSize`.

## 🎯 Centered navigation

**Business value.** Jumping to a search hit (including `F3`) or a marked line
centers the target row in the grid. That small detail keeps context visible on
both sides of the hit and makes result-by-result review much faster.

![Demo](assets/images/feature-2.png)

Navigation is driven by the document view model (`FindNext`/`FindPrevious`) and
`CurrentDocument`; the grid scrolls the target into the middle of the viewport
rather than to the top edge.

## 🧵 Thread grouping

**Business value.** Interleaved log lines from many worker threads are hard to
follow. Thread grouping collapses consecutive visible lines that share the same
`Thread` value, dims the repeated thread value on continuation rows, and adds
subtle dividers. Toggled from the title bar and persisted.

![Demo](assets/images/feature-5.png)

```csharp
var grouping = container.Resolve<ThreadGroupingService>();
grouping.SetEnabled(true);

if (grouping.IsEnabled)
    Console.WriteLine("Consecutive lines with the same Thread are grouped");
```

The boundaries are attached properties (`IsGroupFirst`, `IsGroupLast`,
`IsGroupContinuation`) on `BaseLogListViewItem`, computed by the custom
`VirtualizingStackPanel` and rendered by `Styles/ListViewItemStyle.xaml`. The
setting is `ViewSettings.GroupByThread`.

## 🔁 Text transformations

**Business value.** Logs frequently carry encoded payloads (Base64, compressed
or JSON-in-JSON). Transformations rewrite matched fragments before display, so
the readable value is shown inline without any external tooling.

![Demo](assets/images/feature-7.png)

```yaml
LogFormats:
  - Regex: ^(?<Time>\S+)\t(?<Thread>0x[0-9a-fA-F]+)\t(?<Severity>\w+)\t(?<Message>.*)
    Transformations:
      - lic\t\[.*ContentImp.*\].*"TicketBody"\s+:\s+\{[^}]+"Data"\s+:\s+(?<Base64Decode'"[^"]+")
```

Each entry under `Transformations` is itself a named-capture regex: the capture
name selects the decoder (for example `Base64Decode` or
`Base64DecodeFormatJson`) and the matched group is replaced with the decoded
text. Rules run in order.

## 🔐 XOR-masked logs

**Business value.** Some vendors obfuscate logs with a single-byte XOR key.
LogGrokX transparently de-obfuscates such files while streaming, so they can be
opened and searched like any other log — no manual decode step.

![Demo](assets/images/feature-7.png)

```csharp
var masked = new LogFile(@"C:\logs\encrypted.log", xorMask: 0xEF);

using var loader = new Loader(masked, lineProcessor, logger);
```

`LogFile` wraps the file stream in `MaskedStream(fileStream, xorMask)`, and the
same key can be declared per format as `XorMask` in `appsettings.yaml`.

## 🌗 Light and dark themes

**Business value.** Log review happens in bright offices and dark NOCs alike.
The theme is applied to the whole application — chrome, log colors and search
highlighting. The window deliberately uses solid theme colors instead of the
Windows 11 Mica material, which is incompatible with AvalonDock's auto-hide
flyout.

![Demo](assets/images/feature-4.png)

```csharp
var themeService = container.Resolve<UiThemeService>();
themeService.Apply(UiThemeService.DarkTheme);

if (themeService.IsDark)
    Console.WriteLine($"Active theme: {themeService.CurrentTheme}");
```

The selected theme is stored in `%LOCALAPPDATA%\LogGrokX\Data\Theme.json` and
re-applied on startup by `UiThemeService.ApplySavedTheme`. Reference
`DynamicResource` brushes in custom styles so both themes keep working.

## 🩺 Crash dumps

**Business value.** A crash in a log viewer is worst exactly when it happens on
a critical incident. Optional Windows Error Reporting local dumps capture the
state for diagnosis, and the feature can be toggled without reinstalling.

![Demo](assets/images/feature-7.png)

```yaml
DebugSettings:
  # Restart required after changing debug settings
  EnableCrashDumps: true
  MaxDumpsCount: 10
```

Dumps are written under `%LOCALAPPDATA%\LogGrokX\Dumps`. `EntryPoint` configures
the WER `LocalDumps` registry key on startup and cleans it up when disabled.

## 🛟 Support window

**Business value.** When users report a problem, diagnostics are the slow part.
The Support window collects version, git commit and branch, .NET runtime, OS and
CPU architecture, lets you check for updates manually, and offers one-click links to the latest release, the issue
tracker and the source, plus "copy diagnostics" and "open logs folder".

![Demo](assets/images/feature-6.png)

Open it with the `?` button in the title bar (`OpenSupportCommand`). Diagnostic
logs live in `%LOCALAPPDATA%\LogGrokX\`; the UI is `SupportWindow.xaml` /
`SupportViewModel`.

## 📑 Multiple documents

**Business value.** Real investigations compare several logs at once. LogGrokX
hosts dockable tabs powered by AvalonDock, so documents can be opened, closed,
rearranged and auto-hidden without losing their state. Command-line paths are
opened as documents on startup.

![Demo](assets/images/feature-8.png)

```powershell
LogGrokX.exe C:\logs\app.log C:\logs\worker.log
```

```csharp
// The application forwards command-line paths to the main view model.
foreach (var path in commandLine)
    mainViewModel.AddDocument(path);
```

## 🧬 Merged files view

**Business value.** A single request usually spans several logs written in
different formats by different components. The merged files view combines any
number of opened documents into one **time-ordered** grid: rows are interleaved
by timestamp, columns are aligned case-insensitively across formats, plain-text
lines fall back into `Message`, and `Level`/`Severity` are unified into a single
`Severity` column. Rows are tinted per source, and search, time filtering,
thread grouping, JSON folding and marking all keep working.

![Demo](assets/images/feature-3.png)

```csharp
using LogGrokX.Data;

var firstTimes = new TimeIndex();
foreach (var ticks in firstLogTimestamps)
    firstTimes.Add(ticks);

var sources = new List<MergeSource>
{
    new MergeSource(firstLogLineCount, firstTimes),
    new MergeSource(secondLogLineCount, secondTimes)
};

var merged = MergedLineOrder.Build(sources);
var timeline = MergedLineOrder.BuildTimeIndex(merged);

if (timeline.FindLineRange(fromTicks, toTicks) is { } window)
{
    for (var i = window.StartLine; i < window.EndLine; i++)
    {
        var line = merged[i];
        Console.WriteLine($"source={line.SourceIndex} line={line.LineNumber}");
    }
}
```

`MergedLineOrder.Build` performs a k-way merge with a priority queue, so the cost
is proportional to the number of lines rather than the number of sources. The
view is toggled by `MergedFilesViewService` and persisted as
`ViewSettings.MergedFilesView`.

## 🔄 Automatic updates

**Business value.** Users stay on the latest fixes without visiting GitHub. LogGrokX
checks for a newer release at most once a day and, with one click, installs it
silently when the application is closed.

- On startup (and hourly while running) the app asks the GitHub
  `releases/latest` API for the newest version, but no more than once every
  24 hours.
- If a newer version exists, the **Update available** dialog shows the release
  notes with **Update**, **Later** and **Skip this version**.
- **Update** downloads the installer for the current architecture, verifies it
  against `SHA256SUMS.txt`, and runs it in silent mode after LogGrokX exits.
  Program Files installs ask for UAC elevation.
- Portable builds cannot self-update: the button opens the release page instead.
- **Check for updates** in the Support window runs a check right away, ignoring
  the daily limit and skipped versions.
- Choose the mode in **Settings → View → Automatic updates**:
  - **Do not check for updates** — no automatic checks (manual check still works);
  - **Check for updates** — show the dialog when a new version is found;
  - **Install updates automatically** — download the new version in the
    background without a dialog and install it silently on exit.
- After the update is applied, the next launch shows a **What's new** window with
  the release notes of the installed version and a link to the release page. The
  notes are captured when the installer is downloaded, stored in
  `%LOCALAPPDATA%\LogGrokX\Data\pending-update.json`, shown once the running
  version matches the release, and then removed.
- The installer has one option, **Install updates automatically**: checked sets
  `install`, unchecked sets `check`. Silent auto-update runs keep the user's choice.

```yaml
  ViewSettings:
    UpdateMode: check   # disabled | check | install
```

## Missing a feature?

If a capability you rely on is not covered here, or you would like to see a
feature demonstrated, please
[open an issue](https://github.com/zhenyatnk/LogGrokX/issues) describing your
use case. Bug reports, format requests and documentation improvements are all
welcome.