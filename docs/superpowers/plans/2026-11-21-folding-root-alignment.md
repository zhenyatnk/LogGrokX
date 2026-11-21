# Folding Root Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a session-switchable prototype that compares a fixed folding gutter with moving only the Message field's first JSON/XML root expander to the right edge of the column immediately before Message.

**Architecture:** `FoldingLayoutOptions` publishes the session-only layout to text and expander controls. `TextModel` identifies the first outermost structured folding line, while `TextViewSharedFoldingState` owns root toggling and change notifications so every view remains synchronized. The existing `TextView` keeps nested expanders; a new `RootFoldingExpander` hosts only the relocated root button.

**Tech Stack:** C#, WPF, .NET 10, MSTest, existing `GlyphLine`/`TextView` rendering.

## Global Constraints

- Follow `AGENTS.md`: nullable enabled, file-scoped namespaces for new files, no unnecessary comments, MSTest assertions are `(expected, actual)`.
- Do not add dependencies or persist the prototype mode in `appsettings.yaml`.
- Both layouts must use the same coordinates for measurement, rendering, selection, search highlighting, Alt-hover, and hit testing.
- Finish with `dotnet build LogGrokX.sln -t:Rebuild` and `dotnet test LogGrokX.sln`, both with zero warnings and errors.
- Commit each task after its review is clean; the user explicitly authorized per-task local commits. Do not push.

---

### Task 1: Layout mode and horizontal offsets

**Files:**
- Create: `LogGrokX/Controls/TextRender/FoldingLayoutOptions.cs`
- Modify: `LogGrokX/Controls/TextRender/TextControl.cs:54-67,117-140`
- Modify: `LogGrokX/TextModel.cs:33-35,341-368`
- Test: `LogGrokX.Tests/FoldingLayoutTests.cs`
- Modify: `LogGrokX.Tests/TextControlAltHoverTests.cs` and `LogGrokX.Tests/GlyphLineScalingTests.cs` to isolate legacy coordinate/scaling cases from the new default mode.

**Interfaces:**
- Produces: `FoldingLayoutOptions.Instance.Current` of type `FoldingLayoutMode` (`FixedGutter`, `RootInPreviousColumn`) and `FoldingLayoutOptions.Changed`.
- Produces: `TextModel.RootFoldingLine` of type `int?`; it is non-null only for the first outermost JSON/XML folding range.
- Later tasks consume both APIs to hide an externally hosted Message root inline expander and position its replacement in the preceding column.

- [ ] **Step 1: Write failing layout tests**

Create `LogGrokX.Tests/FoldingLayoutTests.cs` with STA tests using the existing `GlyphLine` pattern:

```csharp
[TestMethod]
public void FixedGutterMeasuresPlainAndCollapsibleTextWithSameOffset()
{
    RunOnSta(FoldingLayoutMode.FixedGutter, () =>
    {
        using var plain = CreateLine("same");
        using var collapsible = CreateLine("same");
        var plainControl = CreateControl((plain, false));
        var collapsibleControl = CreateControl((collapsible, true));

        plainControl.Measure(Infinite);
        collapsibleControl.Measure(Infinite);

        Assert.AreEqual(plain.Size.Width + TextView.ExpanderMargin,
            plainControl.DesiredSize.Width);
        Assert.AreEqual(plainControl.DesiredSize.Width,
            collapsibleControl.DesiredSize.Width);
    });
}

[TestMethod]
public void PlainMultilineFoldingHasNoStructuredRootLine()
{
    var model = new TextModel(8, string.Join("\n", Enumerable.Range(0, 22)));

    Assert.IsNotNull(model.CollapsibleRanges);
    Assert.IsNull(model.RootFoldingLine);
}
```

Use a mode-restoring `RunOnSta` helper, `CreateLine` from `TextControlAltHoverTests`, and a `CreateControl` helper that assigns the supplied `(GlyphLine glyphLine, bool isCollapsible)` tuples.

- [ ] **Step 2: Run the tests and confirm the new APIs are missing**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter FullyQualifiedName~FoldingLayoutTests`

Expected: build failure for missing `FoldingLayoutMode` and `RootFoldingLine`.

- [ ] **Step 3: Add the session layout publisher**

Create `FoldingLayoutOptions.cs`:

```csharp
using System;

namespace LogGrokX.Controls.TextRender;

public enum FoldingLayoutMode
{
    FixedGutter,
    RootInPreviousColumn
}

public sealed class FoldingLayoutOptions
{
    private FoldingLayoutMode _current = FoldingLayoutMode.FixedGutter;

    public static FoldingLayoutOptions Instance { get; } = new();

    public FoldingLayoutMode Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
}
```

- [ ] **Step 4: Apply the fixed gutter in `TextControl`**

Replace the conditional offset with:

```csharp
private static double GetHorizontalOffset(bool isCollapsible)
{
    return FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.FixedGutter || isCollapsible
        ? TextView.ExpanderMargin
        : 0.0;
}
```

All existing callers already use this method for measurement, rendering, selection, highlighting, Alt-hover, and mouse position mapping.

- [ ] **Step 5: Identify the first outermost structured root in `TextModel`**

Add `public int? RootFoldingLine { get; }` beside `UniqueId`. After `CollapsibleRanges` is constructed in the constructor, calculate it only when `structuredRanges.Count != 0`:

```csharp
RootFoldingLine = structuredRanges.Count == 0
    ? null
    : CollapsibleRanges?
        .Where(candidate => !CollapsibleRanges.Any(other =>
            other.start < candidate.start &&
            other.start + other.length >= candidate.start + candidate.length))
        .OrderBy(candidate => candidate.start)
        .Cast<(int start, int length)?>()
        .FirstOrDefault()?.start;
```

This chooses the earliest range not contained by another range and excludes ordinary multiline collapsing.

- [ ] **Step 6: Run the focused tests**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter FullyQualifiedName~FoldingLayoutTests`

Expected: fixed-gutter and plain-multiline tests pass.

---

### Task 2: Shared root toggling and the external root expander

**Files:**
- Modify: `LogGrokX/Controls/TextRender/TextViewSharedFoldingState.cs`
- Create: `LogGrokX/Controls/TextRender/RootFoldingExpander.cs`
- Test: `LogGrokX.Tests/FoldingStateSharingTests.cs`
- Test: `LogGrokX.Tests/FoldingLayoutTests.cs`

**Interfaces:**
- Consumes: `TextModel.RootFoldingLine` and `FoldingLayoutOptions` from Task 1.
- Produces: `TextViewSharedFoldingState.ToggleRoot(TextModel)`, `IsRootCollapsed(TextModel)`, and `Changed`.
- Produces: `RootFoldingExpander.TextModel`, `RootFoldingExpander.SharedFoldingState`, and internal `ToggleRoot()` for tests and `OnClick`.

- [ ] **Step 1: Add failing shared-state tests**

Add tests that start with default collapsed JSON, call `state.ToggleRoot(model)`, and assert:

```csharp
var root = model.RootFoldingLine!.Value;
var nested = model.CollapsibleRanges!.Single(range => range.start != root).start;

Assert.IsFalse(state.IsRootCollapsed(model));
Assert.IsTrue(state[model.UniqueId]!.Contains(nested));

state.ToggleRoot(model);
Assert.IsTrue(state.IsRootCollapsed(model));
Assert.IsTrue(state[model.UniqueId]!.Contains(nested));
```

Also create a `RootFoldingExpander`, assign `TextModel`, `SharedFoldingState`, and `FoldingLayoutMode.RootInPreviousColumn`, then assert that `state.ToggleRoot(model)` changes `RootFoldingExpander.State` from `Collapsed` to `ExpandedUpper` and raises its `Changed` notification path.

- [ ] **Step 2: Run the tests and confirm failure**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter "FullyQualifiedName~FoldingStateSharingTests|FullyQualifiedName~FoldingLayoutTests"`

Expected: compilation fails because root toggle APIs and `RootFoldingExpander` do not exist.

- [ ] **Step 3: Implement root operations in `TextViewSharedFoldingState`**

Change `NotifyChanged` to accept `ITextFoldingClient? source`, invoke `Changed?.Invoke(this, EventArgs.Empty)` after notifying clients, and raise `Changed` after global reset operations. Add:

```csharp
public event EventHandler? Changed;

public bool IsRootCollapsed(TextModel textModel)
{
    if (textModel.RootFoldingLine is not int root ||
        textModel.CollapsibleRanges is not { } ranges)
        return false;

    var collapsed = this[textModel.UniqueId] ??
                    GetDefaultSettings(ranges, textModel.Count);
    return collapsed.Contains(root);
}

public void ToggleRoot(TextModel textModel)
{
    if (textModel.RootFoldingLine is not int root ||
        textModel.CollapsibleRanges is not { } ranges)
        return;

    var collapsed = this[textModel.UniqueId] is { } existing
        ? new HashSet<int>(existing)
        : GetDefaultSettings(ranges, textModel.Count);
    if (!collapsed.Add(root))
        collapsed.Remove(root);

    this[textModel.UniqueId] = collapsed;
    NotifyChanged(null);
}
```

- [ ] **Step 4: Implement `RootFoldingExpander`**

Derive from `OutlineExpander`. Add regular dependency properties `TextModel` and `SharedFoldingState`; property callbacks and both weak event subscriptions call `UpdateState`:

```csharp
private void UpdateState()
{
    var model = TextModel;
    var state = SharedFoldingState;
    var visible = FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.RootInPreviousColumn &&
                  model?.RootFoldingLine != null && state != null;
    Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    if (!visible) return;

    State = state!.IsRootCollapsed(model!)
        ? OutlineExpanderState.Collapsed
        : OutlineExpanderState.ExpandedUpper;
}

internal void ToggleRoot()
{
    if (TextModel is { } model && SharedFoldingState is { } state)
        state.ToggleRoot(model);
}

protected override void OnClick()
{
    ToggleRoot();
    base.OnClick();
}
```

Use `WeakEventManager<FoldingLayoutOptions, EventArgs>` for `FoldingLayoutOptions.Instance.Changed` and `WeakEventManager<TextViewSharedFoldingState, EventArgs>` for state changes. Remove the old state handler when `SharedFoldingState` changes. Set `Width`, `Height`, and `Foreground` from the host templates; do not assign `Expandable`.

- [ ] **Step 5: Run focused tests**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter "FullyQualifiedName~FoldingStateSharingTests|FullyQualifiedName~FoldingLayoutTests"`

Expected: all focused shared-state and layout tests pass.

---

### Task 3: Integrate both layouts into all log views and the View menu

**Files:**
- Modify: `LogGrokX/Controls/TextRender/TextView.cs`
- Modify: `LogGrokX/Controls/TextRender/FoldingLayoutOptions.cs`
- Modify: `LogGrokX/Controls/TextRender/RootFoldingExpander.cs`
- Modify: `LogGrokX/Controls/GridView/GridViewFactory.cs`
- Modify: `LogGrokX/Styles/MarkedLinesViewTemplate.xaml`
- Modify: `LogGrokX/MainWindowViewModel.cs`
- Modify: `LogGrokX/MainWindow.xaml`
- Modify: `LogGrokX.Tests/TextControlAltHoverTests.cs` and `LogGrokX.Tests/GlyphLineScalingTests.cs` for the enum rename.
- Test: `LogGrokX.Tests/GridViewFactoryTests.cs`
- Test: `LogGrokX.Tests/FoldingLayoutTests.cs`
- Test: `LogGrokX.Tests/PresentationBoundaryTests.cs`

**Interfaces:**
- Consumes: all Task 1 and Task 2 APIs.
- Produces: inherited attached `TextView.HasExternalRootExpander` of type `bool`, defaulting to false.
- Produces: `MainWindowViewModel.IsRootFoldingInPreviousColumn` for the temporary checkable menu item.
- Uses `FoldingLayoutMode.RootInPreviousColumn`; no row-level `FoldingTextModel` aggregate is introduced.

- [ ] **Step 1: Add failing external-root and host-template tests**

Add folding tests proving that an external Message root loses only its own inline margin/upper expander, while a `TextView` without `HasExternalRootExpander` retains its root margin and all inline expanders in `RootInPreviousColumn` mode. Verify the attached property defaults to false and inherits.

Update `GridViewFactoryTests` to assert that the pin cell has no root control; the Component cell immediately before Message has a star-sized text area and auto-sized horizontal action area with the 8x8 root before BIN; and Index hosts the root when Message is the first parsed field. Assert direct Message-part `TextModel` bindings and the existing normal/merged shared-state bindings. Add marked-line template coverage for the Index action area and whole-text external-root opt-in.

Retain the runtime mode-switch test for measure invalidation, highlight geometry, and expander visibility, setting `HasExternalRootExpander` on its `TextView`.

- [ ] **Step 2: Run integration tests and confirm failure**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter "FullyQualifiedName~GridViewFactoryTests|FullyQualifiedName~FoldingLayoutTests|FullyQualifiedName~PresentationBoundaryTests.MarkedLinesTemplateHostsRootInIndexActionArea"`

Expected: failures show that the root is still in pin cells, the preceding-column and Index hosts are missing, non-Message roots are incorrectly suppressed, and `HasExternalRootExpander` does not exist.

- [ ] **Step 3: Add the external-root contract and rename the mode**

Add inherited attached `TextView.HasExternalRootExpander` with default false. Inline root-margin removal and upper-expander suppression require all of:

```csharp
FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.RootInPreviousColumn &&
TextView.GetHasExternalRootExpander(textView) &&
index == outlineData.RootFoldingLine
```

Leave nested outlines and fixed-gutter behavior unchanged. Rename every `RootInPinColumn` use to `RootInPreviousColumn`, including legacy Alt-hover and scaling helpers. Retain weak layout-change subscriptions and cache invalidation.

- [ ] **Step 4: Generalize the generated right-action cell layout**

Find the case-insensitive `Message` field index. Its own `LogGridViewCell` sets `HasExternalRootExpander=true`; no other generated field sets it. The column at `messageFieldIndex - 1` hosts the root, which naturally selects Index (`fieldIndex == -1`) when Message is first.

For a host cell and/or the existing Component BIN cell, use a Grid with star-sized content and auto-sized actions. The horizontal action area contains the 8x8 root first when hosted, then the existing decode toggles when present. Do not add a GridViewColumn. Bind the root directly to the Message part:

```csharp
root.SetBinding(RootFoldingExpander.TextModelProperty, new Binding
{
    Path = new PropertyPath(".[(0)].TextModel", messageFieldIndex),
    Mode = BindingMode.OneWay
});
```

For merged rows bind `SharedFoldingState` to `sharedFoldingStatePath`; otherwise bind to `DataContext.FoldingState` with `RelativeSource FindAncestor` for `ListView`. Restore pin cells to their original pin/BIN-only content and remove any row-level `FoldingTextModel` aggregate.

- [ ] **Step 5: Place the marked-line root in Index**

Wrap the existing marked-line Index content in a star-sized content area and auto-sized horizontal action area. Bind the 8x8 root to `Text.TextModel` and `Document.FoldingState`. Set `TextView.HasExternalRootExpander="True"` on the whole-line `TextView`; remove the root from the pin cell.

- [ ] **Step 6: Rename the temporary View-menu switch**

Rename the view-model property to `IsRootFoldingInPreviousColumn` using the same session-only setter. Use exactly `MenuItem Header="Root + left of message"` with a two-way `IsChecked` binding. Do not add YAML or localization settings.

- [ ] **Step 7: Preserve fixed-gutter coordinate coverage**

Keep the fixed-gutter Alt-hover test and all legacy coordinate/scaling tests, updating only the enum name. The non-external folding test must prove structured fields other than Message keep their inline root behavior.

- [ ] **Step 8: Run all UI tests**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj`

Expected: all tests pass, including both layouts, root state sharing, preceding-column and Index bindings, marked-line placement, non-Message inline roots, Alt-hover, highlighting, and existing folding measurements.

---

### Task 4: Full verification and live comparison

**Files:**
- Verify only; remove generated logs or test artifacts created by these runs if they are not already ignored.
- Update: `AGENTS.md` only if the prototype changes its documented folding behavior; otherwise leave it unchanged until final cleanup.

- [ ] **Step 1: Run the required clean build**

Run: `dotnet build LogGrokX.sln -t:Rebuild`

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: Run the full solution tests**

Run: `dotnet test LogGrokX.sln`

Expected: all tests pass with zero failed tests.

- [ ] **Step 3: Exercise the real application**

Run `dotnet run --project LogGrokX/LogGrokX.csproj` and open `assets/demo/demo-app.log`. Use the JSON rows around lines 34 and 48, the XML row around line 150, and adjacent plain rows.

In each mode verify:
- plain and structured text alignment;
- only the Message root button moves to the preceding column in `RootInPreviousColumn` mode;
- root collapse/expand and nested collapse/expand;
- live switching without restart or loss of folding state;
- selection, Alt-hover, search highlighting, and horizontal scrolling;
- marked-lines folding synchronization and merged-view folding.

- [ ] **Step 4: Inspect the final working tree**

Run: `git status --short`

Expected: only the intentional prototype source, tests, design, and plan documents are listed. Delete unneeded files created by verification. Report the temporary View-menu switch and leave both modes available for the user's comparison.
