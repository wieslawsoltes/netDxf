# Studio palette and ribbon workspace

Properties is an independent palette. Use **Palettes** in the document bar, the
palette selector, View ribbon or a CAD command to open a tool. Multiple palettes
can remain open: inspect a graph beside object properties, or keep Layers on the
opposite side. A selector replaces one palette with another; opening from the
Palettes menu adds a palette without closing others.

Each title has a move grip, selector, auto-hide pin, layout menu and close button.
The layout menu supports **Dock left**, **Dock right**, **Float palette**,
**Auto-hide left** and **Auto-hide right**. Dock columns reserve drawing space;
anchored palettes open over the drawing from an edge strip. Move the pointer
away and leave keyboard focus to retract an anchored palette. Escape retracts
it immediately. Floating windows support drag/resize, while dock columns have
keyboard and pointer splitters. Drag near an edge to dock. Move/resize grips
also accept arrow keys, with Shift for one-pixel steps.

The Palettes launcher exposes **Lock palette layout** and **Reset palette
layout**. Locking prevents movement, resizing and docking while permitting
close/reopen. Ctrl+1 toggles palettes; Ctrl+0 hides drawing chrome temporarily.
Small workspaces automatically present docked palettes as anchored overlays.
Window rectangles are clamped to the available workspace. Local preferences
restore each palette's visibility, docking side, mode and size; reset returns to
one Properties palette on the right. Old single-palette preferences migrate.
Drawing files and undo history are unaffected by layout changes.

Docked palettes can share a tab group. Use a palette's layout menu to group it
with another docked palette, move it to the target's side, detach it, or change the order of tabs
and groups. The active tab owns the content renderer; inactive tabs release
asynchronous work. Arrow keys navigate tabs. Horizontal separators resize stacked
groups independently of the column width. Splitter moves can be cancelled and
the layout lock prevents regrouping and resizing.

**Allow Docking** is a per-palette preference. Turning it off keeps that palette
floating and disables edge docking and auto-hide anchoring until re-enabled.
Existing layouts and saved workspaces migrate to the group model; narrow screens
retain edge overlays while preserving the desktop arrangement. The interaction
follows Autodesk's [docking permissions and pane resizing](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-Core/files/GUID-6EF21814-B9FB-4F2A-897E-852B25679D17.htm).

Dragging a palette now shows left/right docking targets and a shaded preview.
Release over a target to dock; hold Ctrl to keep the palette floating. Escape or
pointer cancellation restores the previous layout. Menu docking remains available
to keyboard and touch users, and the layout lock also prevents drag docking.

**UI workspaces** in the document bar, View or Manage ribbon opens saved layouts.
The Drafting and Graph analysis presets provide starting arrangements.
Dependency analysis opens a wider References/Graph tab group beside Properties.
Save the
current palette groups/sizes/transparency, ribbon display/tab/customization, Quick Access commands and command-line visibility under
a name, then apply, update, rename or delete it later. Up to 24 layouts are stored
locally in the browser, within a shared 512,000-character storage budget. Oversized
saves or updates preserve all existing profiles and report the limit. `WORKSPACE` and `WSSAVE` open the same manager. This follows
the task-oriented display arrangement described in Autodesk's
[workspace documentation](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-Core/files/GUID-52902E88-41B8-4890-A2FF-E47332CBD0D8.htm).
Drawing branches remain under **Immutable workspaces**. UI layouts exclude
drawings, drafting aids, discipline enablement and agent credentials/settings.
Storage failures are reported instead of claiming a layout was saved.

**Search commands** in Quick Access, **Manage → Find a command**, or Ctrl/Cmd+K
opens command search. Search labels, aliases and categories for drawing commands,
resource managers, graph analyses and discipline tools. Results explain their
availability; execution rechecks the current drawing and module state.
Enter executes the selected result and Escape returns focus. Opening and
filtering search does not cancel the drawing command or change selection.

**Customize Quick Access** pins, removes and reorders up to 16 command IDs.
Overflow keeps controls reachable on narrow screens. Choices persist locally and
are included in saved UI workspaces; reset restores New, Open, Save, Undo and Redo.
This follows Autodesk's [Quick Access customization](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-5F9B5220-1D63-4791-96C6-97E50621320C.htm).
The reusable `command-catalog.mjs` owns discovery, availability and bounded
preferences; `command-access.mjs` supplies UI using host execution callbacks.
Saved preferences contain command identities, never drawing payloads or credentials.

The ribbon display menu offers **Full ribbon**, **Panel buttons**, **Panel
titles** and **Tabs only**. The adjacent arrow cycles these modes; double-click
a tab toggles the full/tabs modes. Tabs-only mode opens a temporary ribbon when
a tab is chosen, then retracts after choosing a command, clicking outside or
pressing Escape. Panel titles/buttons expose their complete tool sets in bounded
flyouts; full mode collapses trailing panels when space is limited. Keyboard
Left/Right/Home/End changes tabs, Down enters the selected ribbon, and Escape
closes a flyout and returns focus. Native controls retain normal Tab navigation.
The **Graph** tab groups topology, routes, structural analysis, features and
source inspection. Enabled discipline tabs and Block Editor controls continue
to share the same responsive ribbon.

The interaction conventions follow Autodesk's documentation for
[tool palette docking, anchoring and floating](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-Core/files/GUID-6EF21814-B9FB-4F2A-897E-852B25679D17.htm)
and [ribbon display modes](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-627FCA86-38B1-404B-BB09-9FC3348B54F2.htm).
Studio implements these workspace patterns in a browser; it does not claim
AutoCAD CUIx compatibility or multi-monitor native windows.

`palette-layout.mjs` is a pure, bounded preference and layout model.
`PaletteDock` in `palette-docking.mjs` accepts palette descriptors, render and
lifecycle callbacks, storage and a host workspace. Content renderers stay
independent of docking. Hidden panes detach and invoke cleanup callbacks so old
asynchronous UI responses cannot update an inactive palette. Each analysis
palette has its own request state. `RibbonLayout` similarly reuses controls and
their handlers when moving a panel into a flyout; it does not duplicate commands.
These shared web assets ship in `netDxf.Shadow.Studio.Web` for native and WASM.

Pure-model tests cover malformed preferences, migration, viewport clamping,
space allocation, independent palettes, locking and ribbon display restoration.
The shared native/WASM browser fixture exercises simultaneous Graph/Properties,
opposite docks, floating movement/resizing, auto-hide, reopening, reset and all
four ribbon display modes.

Block editing opens a contextual **Block Editor** ribbon tab. Definition save and
discard controls remain available when using other ordinary tabs. Discipline tabs
are disabled during block editing, and leaving the editor restores the previously
active enabled tab. In Panel titles mode, clicking a hovered panel enters its tools;
Escape returns focus to its launcher.

`workspace-profiles.mjs` supplies bounded preference normalization, presets,
explicit updates and injected storage. `workspace-profile-ui.mjs` uses host
capture/restore callbacks; `PaletteDock.restore()` cancels gestures and old content
lifetimes before restoring a sanitized, viewport-clamped layout. These components
are shared by native and standalone WASM Studio.

## Ribbon customization and floating panels

`CUI` or **Manage → Customize ribbon** opens a draft editor. Show/hide and order
built-in tabs and their panels, create up to eight custom tabs and 24 custom
panels, and add up to 32 registered commands per panel. Availability is checked
again at execution, so a saved button cannot bypass drawing, selection, block
editor or discipline restrictions. Home and block save/discard controls stay
recoverable. Apply saves; Cancel leaves the active ribbon unchanged.

Import/export uses bounded Studio JSON (128 KiB), containing command identities
and UI preferences. Unknown command identities stay disabled. Executable macros,
CUIx files and arbitrary drawing payloads are not accepted. Named UI workspaces
capture the same configuration. Explicit save errors are reported; restoring a
workspace can still apply in memory if optional browser storage is unavailable.

Drag a panel title out of the ribbon or use its **Float** button. A floating panel
keeps its original controls across tab changes. Move it by pointer or arrow keys
(Shift for one-pixel steps), cancel a drag with Escape, or use **Return**. Positions
are clamped to the viewport. Hiding its tab/panel or disabling its discipline
suspends the floating controls while retaining the preference. Block definition
save/discard controls remain on the ribbon. Up to 16 panels can float.

`ribbon-customization.mjs` supplies a pure preference model and injected storage;
`ribbon-customization-ui.mjs` binds the editor to the command catalog.
`ribbon-floating.mjs` handles pointer/keyboard movement and lifecycle within the
browser workspace. These are shared web-package assets, not operating-system
windows.

## Palette transparency

The palette layout menu's **Transparency…** dialog sets normal and rollover
opacity (0–100%). Rollover opacity cannot be lower than normal opacity. A preview
shows both states before Apply; Cancel and Escape discard the draft. Apply to all
palettes is explicit. **Disable all palette transparency** is also available in
the Palettes launcher for recovery. Docked palettes and palettes containing
keyboard focus or an open layout menu remain opaque. Floating and anchored
palettes use the saved values. Appearance remains editable with positions locked.

This follows the controls described in Autodesk's
[Transparency dialog](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Core/files/GUID-B04E43D3-2BA0-4230-82C1-8B7140EA4559.htm)
and [PALETTEOPAQUE behavior](https://help.autodesk.com/cloudhelp/2025/ENG/AutoCAD-Core/files/GUID-32C9D125-6D80-4C1B-A51D-3F48854BD1E4.htm).
Keeping keyboard-focused controls opaque is Studio's accessibility behavior.
`palette-appearance.mjs` supplies reusable normalization, effective opacity and
an isolated draft dialog. Preferences belong to the workspace, never to drawing
entity transparency or undo history.
