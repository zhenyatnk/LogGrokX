# Folding root alignment prototype

## Goal

Compare two live layouts that prevent the first JSON/XML folding expander from shifting text relative to ordinary log text. The prototype must keep nested folding, selection, search highlighting, copying, and shared folding state working.

## Prototype switch

Add a temporary checkable View menu item named `Root + left of message` that switches between the two layouts immediately in all open views. It binds to `MainWindowViewModel.IsRootFoldingInPreviousColumn` and selects `FoldingLayoutMode.RootInPreviousColumn` when checked. The setting is session-only and is not written to `appsettings.yaml`. After a layout is chosen, remove the switch and the rejected implementation.

## Variant 1: fixed folding gutter

Every `TextView` reserves the existing 12 DIP folding margin on the left, including plain text and lines without an expander. Root and nested expanders keep their current positions. Text in structured and ordinary rows therefore starts at the same horizontal coordinate within the corresponding cell.

Measurement, rendering, selection, search highlights, Alt-hover, and hit testing must all use the same offset. No folding-state changes are required for this variant.

## Variant 2: root expander at the right of the column before Message

Move only the first outermost JSON/XML expander of the `Message` field (or the whole `Text` field in marked lines) to the right edge of the column immediately to its left. If the preceding field is `Component`, the root control shares its right-side action area with `BIN`. If `Message` is the first field, the host is the Index column. Nested expanders and their existing margins remain inline in `Message`.

Only a `TextView` whose root is hosted externally suppresses its inline root expander and root margin. The inherited attached `TextView.HasExternalRootExpander` Boolean property defaults to false and is set only on the generated Message cell and the marked-line whole-text `TextView`. Suppression requires both this property and `RootInPreviousColumn` mode. Structured fields other than `Message` retain their inline root and current behavior. Plain text and ordinary multiline collapsing do not get the new root button.

The root button operates through `TextViewSharedFoldingState`, using the `TextModel.UniqueId`, so the main grid, search results, marked lines, and merged view stay synchronized. Folding notifications update the root button when folding changes elsewhere. Switching layouts invalidates visible text measurement and highlighting geometry, and newly virtualized rows derive their layout from the current mode.

## Verification

Add or update MSTest coverage for both modes, including equal offsets for plain and structured text in fixed-gutter mode, removal of only the externally hosted root offset, root toggling through shared state, nested folding preservation, and preservation of inline roots in non-Message fields. Verify the root control's binding and right-side placement in the column immediately before Message, including the Index and marked-lines cases.

Run `dotnet build LogGrokX.sln -t:Rebuild` and `dotnet test LogGrokX.sln` with zero warnings and errors. Exercise the real application with adjacent plain, JSON, and XML rows in both modes; collapse and expand root and nested regions, switch modes without restarting, and check selection, search highlighting, horizontal scrolling, marked lines, and merged view.

## Cleanup

After the user chooses a layout, remove the temporary View menu item, session switch, and rejected code. Retain only the tests relevant to the chosen behavior and update `AGENTS.md` if the folding layout documentation changes.
