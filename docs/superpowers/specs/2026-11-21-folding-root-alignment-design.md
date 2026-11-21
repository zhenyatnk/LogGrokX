# Folding root alignment prototype

## Goal

Compare two live layouts that prevent the first JSON/XML folding expander from shifting text relative to ordinary log text. The prototype must keep nested folding, selection, search highlighting, copying, and shared folding state working.

## Prototype switch

Add a temporary checkable View menu item that switches between the two layouts immediately in all open views. The setting is session-only and is not written to `appsettings.yaml`. After a layout is chosen, remove the switch and the rejected implementation.

## Variant 1: fixed folding gutter

Every `TextView` reserves the existing 12 DIP folding margin on the left, including plain text and lines without an expander. Root and nested expanders keep their current positions. Text in structured and ordinary rows therefore starts at the same horizontal coordinate within the corresponding cell.

Measurement, rendering, selection, search highlights, Alt-hover, and hit testing must all use the same offset. No folding-state changes are required for this variant.

## Variant 2: root expander in the pin column

Move only the first outermost JSON/XML expander to the existing left pin/BIN column. Its button is shown beside ordinary row controls and uses the same collapsed/expanded visuals as an inline expander. The root text line no longer receives the folding margin; nested expanders and their existing margins remain inline.

The root button targets the first field with structured folding. It operates through `TextViewSharedFoldingState`, using the `TextModel.UniqueId`, so the main grid, search results, marked lines, and merged view stay synchronized. Folding notifications update the root button when folding changes elsewhere. Plain text and ordinary multiline collapsing do not get this new root button.

Switching layouts invalidates visible text measurement and highlighting geometry. Newly virtualized rows must derive their layout from the current mode without additional synchronization.

## Verification

Add or update MSTest coverage for both modes, including equal offsets for plain and structured text in fixed-gutter mode, removal of only the root offset in pin-column mode, root toggling through shared state, and nested folding preservation. Keep existing Alt-hover and folding measurement tests aligned with the selected layout semantics.

Run `dotnet build LogGrokX.sln -t:Rebuild` and `dotnet test LogGrokX.sln` with zero warnings and errors. Exercise the real application with adjacent plain, JSON, and XML rows in both modes; collapse and expand root and nested regions, switch modes without restarting, and check selection, search highlighting, horizontal scrolling, marked lines, and merged view.

## Cleanup

After the user chooses a layout, remove the temporary View menu item, session switch, and rejected code. Retain only the tests relevant to the chosen behavior and update `AGENTS.md` if the folding layout documentation changes.
