# Folding Root Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a session-switchable prototype that compares a fixed folding gutter with moving only the first JSON/XML root expander into the existing pin column.

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
- Produces: `FoldingLayoutOptions.Instance.Current` of type `FoldingLayoutMode` (`FixedGutter`, `RootInPinColumn`) and `FoldingLayoutOptions.Changed`.
- Produces: `TextModel.RootFoldingLine` of type `int?`; it is non-null only for the first outermost JSON/XML folding range.
- Later tasks consume both APIs to hide the root inline expander and position its pin-column replacement.

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
    RootInPinColumn
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

### Task 2: Shared root toggling and the pin-column expander

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

Also create a `RootFoldingExpander`, assign `TextModel`, `SharedFoldingState`, and `FoldingLayoutMode.RootInPinColumn`, then assert that `state.ToggleRoot(model)` changes `RootFoldingExpander.State` from `Collapsed` to `ExpandedUpper` and raises its `Changed` notification path.

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
    var visible = FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.RootInPinColumn &&
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

Use `WeakEventManager<FoldingLayoutOptions, EventArgs>` for `FoldingLayoutOptions.Instance.Changed` and `WeakEventManager<TextViewSharedFoldingState, EventArgs>` for state changes. Remove the old state handler when `SharedFoldingState` changes. Set `Width`, `Height`, and `Foreground` from the pin templates; do not assign `Expandable`.

- [ ] **Step 5: Run focused tests**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter "FullyQualifiedName~FoldingStateSharingTests|FullyQualifiedName~FoldingLayoutTests"`

Expected: all focused shared-state and layout tests pass.

---

### Task 3: Integrate both layouts into all log views and the View menu

**Files:**
- Modify: `LogGrokX/Controls/TextRender/TextView.cs:19-51,147-199,332-336,566-600,620-642`
- Modify: `LogGrokX/BaseLogLineViewModel.cs:36-55,122-140`
- Modify: `LogGrokX/Controls/GridView/GridViewFactory.cs:22-44,164-195`
- Modify: `LogGrokX/Styles/MarkedLinesViewTemplate.xaml:73-83`
- Modify: `LogGrokX/MainWindowViewModel.cs:423-438`
- Modify: `LogGrokX/MainWindow.xaml:220-226`
- Modify: `LogGrokX.Tests/TextControlAltHoverTests.cs`
- Test: `LogGrokX.Tests/GridViewFactoryTests.cs`
- Test: `LogGrokX.Tests/FoldingLayoutTests.cs`

**Interfaces:**
- Consumes: all Task 1 and Task 2 APIs.
- Produces: `BaseLogLineViewModel.FoldingTextModel` of type `TextModel?` for pin-template binding.
- Produces: `MainWindowViewModel.IsRootFoldingInPinColumn` for the temporary checkable menu item.

- [ ] **Step 1: Add failing inline-root and pin-template tests**

Add `PinColumnModeMarksOnlyRootLineAsNotRequiringInlineMargin` to `FoldingLayoutTests`:

```csharp
RunOnSta(FoldingLayoutMode.RootInPinColumn, () =>
{
    var state = new TextViewSharedFoldingState();
    state.ExpandAll();
    var view = new TextView();
    TextView.SetSharedFoldingState(view, state);
    view.TextModel = new TextModel(7, "{\"a\":{\"b\":1}}");

    view.Measure(Infinite);
    var control = (TextControl)VisualTreeHelper.GetChild(view, 0);

    Assert.IsFalse(control.TextLines![0].isCollapsible);
    Assert.IsTrue(control.TextLines.Skip(1).Any(line => line.isCollapsible));
});
```

In `GridViewFactoryTests`, create a normal view, load `view.Columns[0].CellTemplate`, find the single `RootFoldingExpander` in the resulting `StackPanel`, and assert:

```csharp
var binding = expander.GetBindingExpression(RootFoldingExpander.TextModelProperty)!.ParentBinding;
Assert.AreEqual(nameof(BaseLogLineViewModel.FoldingTextModel), binding.Path.Path);
Assert.AreEqual(8d, expander.Width);
Assert.AreEqual(8d, expander.Height);
```

Add an STA test that switches `FoldingLayoutOptions.Instance.Current` at runtime and asserts an existing `TextView` is invalidated and a visible `RootFoldingExpander` gets `Visibility.Collapsed` in `FixedGutter` mode. Restore the previous mode in `finally`.

- [ ] **Step 2: Run integration tests and confirm failure**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj --filter "FullyQualifiedName~GridViewFactoryTests|FullyQualifiedName~FoldingLayoutTests"`

Expected: the inline-root test fails because the root still reserves an inline margin, and the pin-template test fails because no root expander or `FoldingTextModel` binding exists.

- [ ] **Step 3: Hide only the root inline expander in pin-column mode**

Extend `TextView.OutlineData` with `int? RootFoldingLine`, supplied from `TextModel` when collapsible ranges are set. In `CreateVisibleTextLines`, use:

```csharp
var needsInlineMargin = outlineData.IsCollapsible(index) &&
    !(FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.RootInPinColumn &&
      index == outlineData.RootFoldingLine);
visibleLines.Add((GetOrCreateTextLine(text, index, pixelsPerDip), needsInlineMargin));
```

In `UpdateChildren`, treat the matching root line as `Outline.None` only in `RootInPinColumn`; leave every nested outline unchanged. Add `FrameworkPropertyMetadataOptions.Inherits` to `TextView.SharedFoldingStateProperty` so the state assigned on the main/search `ListView` reaches its text cells.

In the `TextView` constructor, weak-subscribe to `FoldingLayoutOptions.Instance.Changed`; the handler sets `_isCollapsibleStateDirty = true`, calls `InvalidateMeasure()`, `InvalidateVisual()`, and `_textControl.OnTextLinesChanged()` through an internal cache-reset method so stale highlight geometry is not reused.

- [ ] **Step 4: Expose the first structured field model**

Add to `BaseLogLineViewModel`:

```csharp
public TextModel? FoldingTextModel => DecodableParts
    .Select(part => part.TextModel)
    .FirstOrDefault(model => model.RootFoldingLine != null);
```

When a part raises `PropertyChanged` for `TextModel`, call `InvokePropertyChanged(nameof(FoldingTextModel))`. Existing lazy `DecodableParts` caching is reused; no second parts collection is introduced.

- [ ] **Step 5: Add the root button to generated and marked-line pin cells**

Pass `sharedFoldingStatePath` into `CreatePinCellTemplate`. Insert an 8x8 `RootFoldingExpander` below `PinControl` and before decode toggles. Bind:

```csharp
root.SetBinding(RootFoldingExpander.TextModelProperty,
    new Binding(nameof(BaseLogLineViewModel.FoldingTextModel)) { Mode = BindingMode.OneWay });
```

For merged rows bind `SharedFoldingState` to `sharedFoldingStatePath`; otherwise bind to `DataContext.FoldingState` with `RelativeSource FindAncestor` for `ListView`. In `MarkedLinesViewTemplate.xaml`, add the same 8x8 control below `PinControl`, binding `TextModel="{Binding FoldingTextModel}"` and `SharedFoldingState="{Binding Document.FoldingState}"`.

- [ ] **Step 6: Add the temporary View-menu switch**

Add to `MainWindowViewModel`:

```csharp
public bool IsRootFoldingInPinColumn
{
    get => FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.RootInPinColumn;
    set
    {
        FoldingLayoutOptions.Instance.Current = value
            ? FoldingLayoutMode.RootInPinColumn
            : FoldingLayoutMode.FixedGutter;
        InvokePropertyChanged();
    }
}
```

Add a checkable `MenuItem Header="Root + in pin column"` beside thread grouping, with `IsChecked="{Binding IsRootFoldingInPinColumn, Mode=TwoWay}"`. Do not add a YAML setting or localization resource for this temporary prototype item.

- [ ] **Step 7: Add fixed-gutter Alt-hover coverage**

The legacy Alt-hover and scaling STA helpers were isolated under `RootInPinColumn` in Task 1. Add one new fixed-gutter Alt-hover test that points at a non-collapsible glyph using `TextView.ExpanderMargin` and verifies the returned rectangle uses the same margin.

- [ ] **Step 8: Run all UI tests**

Run: `dotnet test LogGrokX.Tests/LogGrokX.Tests.csproj`

Expected: all tests pass, including both new layouts, root state sharing, pin-template bindings, Alt-hover, highlighting, and existing folding measurements.

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
- only the root button moves in pin-column mode;
- root collapse/expand and nested collapse/expand;
- live switching without restart or loss of folding state;
- selection, Alt-hover, search highlighting, and horizontal scrolling;
- marked-lines folding synchronization and merged-view folding.

- [ ] **Step 4: Inspect the final working tree**

Run: `git status --short`

Expected: only the intentional prototype source, tests, design, and plan documents are listed. Delete unneeded files created by verification. Report the temporary View-menu switch and leave both modes available for the user's comparison.
