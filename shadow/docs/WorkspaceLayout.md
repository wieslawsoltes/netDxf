# Studio palette and ribbon workspace

## Desktop CAD shell

The shared native and WebAssembly frontend uses a compact application/Quick Access
strip, workspace switcher, tabbed ribbon and drawing-file tabs above the drawing.
The drawing column owns its viewport, resizable command window and Model/paper
layout tabs. Docked palettes extend beside that complete column to the application
status bar. The command window no longer reduces the height of Properties or an
opposite dock. Agent chat remains an independent adjacent workspace.

The Home ribbon uses three-row command grids, large primary drawing/annotation
tools, a full-width current-layer selector, semantic SVG icon colors and functional
panel launchers. All seven Home panels fit at 1280 CSS pixels. Curves and Shapes
split buttons retain their real command controls in keyboard-accessible menus;
collapsed ribbon panels expose the alternatives directly. All existing command
identities, contextual tabs, customization, floating panels and display modes are
shared with the same command dispatcher.

The application menu provides registered file commands, command search and open
drawings. The drawing-list button reaches tabs that overflow the file strip;
right-click a drawing tab for Activate, Save, Save All, Close, Close Others and Close All. Drawing and layout tabs
support Left/Right and Home/End. Asynchronous activation and background document
refresh preserve the focused tab or Close control. Model/paper layouts have a
separate list beside their tabs, and the status bar identifies the current space.

**Workspace switching** is available beside Quick Access and on the status bar.
Choose a preset or saved layout, manage/save layouts, or reset the current layout.
Preset and saved identities are distinct, even when names match. Renaming a current
saved layout updates the switcher; deleting it leaves the current arrangement
detached until another workspace is applied or saved. UI profiles now include
status visibility/text labels and command-window position in addition to the
existing ribbon and palette state. Existing profiles receive safe defaults.

## Command window and status bar

Drag the command-row grip to float the existing command editor. Double-click the
grip or use its dock/float button to change mode. A floating window can be moved
with the grip, resized from any edge or corner, or adjusted with arrow keys
(Shift uses one-pixel steps). Drop near the drawing column's top or bottom edge to dock;
hold Ctrl to keep floating. The preview indicates the destination, and Escape
cancels movement or resizing. The input, selection, history and handlers retain
their identities while moving between hosts. Restored geometry is fitted to the
available workspace. Ctrl+9 and F2 retain their command-window/history functions. Ctrl+F2 opens the full-size, selectable command-history window. Its Focus command line action restores a hidden editor before focusing it.

The status strip uses compact SVG indicators for the existing drafting aids.
**Customize status bar** shows/hides individual indicators and coordinates, enables
text labels, or restores defaults. Hidden indicators retain their keyboard
shortcuts and do not disable the corresponding drafting aid. Right-click a drafting
indicator (or press Down while focused) for its settings; Object Snap exposes the
individual running snap modes. Menus choose space above or below the trigger and
remain inside the viewport, including compact screens.

`workspace-shell-model.mjs` normalizes only bounded presentation preferences.
`StudioShell` binds the shell to the existing command catalog and profile manager;
`CommandLineDock` moves the original form without cloning command state. The shared
`desktop_shell_browser_checks.py` fixture runs in the native and static-only WASM
suites. It covers drawing-column geometry, command focus/buffer retention, pointer
and keyboard docking, profile identity, status controls, asynchronous tabs and
320–1600-pixel responsive layouts while checking drawing/history preservation.

## Palette placement

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

Drag over another docked group's center or tab strip to join it as a tab; drag
over its upper/lower region to insert a stack above/below it. Live drop previews
identify the destination. Tabs can be dragged out to float or into another group.
Ctrl suppresses drop targets, and Escape restores the pre-drag arrangement.
The title grip also accepts double-click to dock/float. Right-click a palette title
or tab for the same layout options, with keyboard menu navigation. Palette chrome
uses SVG controls rather than font-dependent docking glyphs.

The Palettes launcher exposes **Lock palette layout** and **Reset palette
layout**. Locking prevents movement, resizing and docking while permitting
close/reopen. Ctrl+1 toggles Properties only; Ctrl+Shift+H hides/restores palettes, the command window, ribbon and drawing tabs. Ctrl+0 hides drawing chrome temporarily. Ctrl temporarily overrides position locks.
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
floating and disables edge docking and anchoring until re-enabled. Floating auto-hide remains independently available.
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
Discipline analysis groups Modules, Graph and References beside Properties,
leaving discipline enablement under the Modules controls.
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

**Transfer UI workspace** exports the selected layout as versioned Studio JSON.
Import validates the file, shows its palette/ribbon summary, and saves it under
a chosen name. Replacing an existing name requires the explicit checkbox.
Saving an import does not activate it; use **Apply layout** to switch.
Files are limited to 500 KiB of UTF-8 data. Only supported UI fields are copied,
unknown palettes are omitted, and the destination host fits window positions to
its screen. Imported custom commands still resolve against the destination's
registered command catalog. This format does not provide CUIx interoperability.

**Search commands** in Quick Access, **Manage → Find a command**, or Ctrl/Cmd+K
opens command search. Search labels, aliases and categories for drawing commands,
resource managers, graph analyses and discipline tools. Results explain their
availability; execution rechecks the current drawing and module state.
Enter executes the selected result and Escape returns focus. Opening and
filtering search does not cancel the drawing command or change selection.

**Customize Quick Access** pins, removes and reorders up to 16 command IDs.
The arrow menu toggles common commands; More commands opens the full editor. Right-click a ribbon command to pin/unpin it directly, or a Quick Access command to remove it. Overflow executes the actual hidden commands, and the menu moves the retained toolbar above or below the ribbon. Choices persist locally and
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


## Keyboard ribbon and menu navigation

Ribbon tabs use Left/Right, Home/End and Down to enter the selected ribbon.
Command panels and Quick Access use one roving button Tab stop, spatial arrows
and Home/End. Native text fields and selectors retain their ordinary Tab stops
and editing keys. Escape returns from flyouts to their launcher, from an expanded
ribbon to its tab, or from floating content to its move grip. Tab leaves the
control group without a focus trap. Disabled and hidden commands are skipped.

Alt+F on a focused panel launcher floats that panel. Full panels expose a
keyboard-reachable Float button. The floating move grip supports
arrow positioning; Escape returns to the selected tab. These shortcuts are scoped
to ribbon controls and do not intercept the browser's global Alt navigation.
File and workspace menus use Up/Down and Home/End, close on Tab or Escape and
restore their trigger focus. Shift+F10 in the drawing viewport opens command
actions at its center; Escape restores viewport focus without canceling a drawing
command. Ribbon customization keeps the newly rendered tab/panel selector focused
after a selection change.

`ribbon-keyboard.mjs` provides reusable visibility filtering, spatial navigation
and binding lifecycle. Quick Access, full/collapsed/floating ribbon panels and
workspace menus share it across native and browser hosts.


## Whole-ribbon docking and expanded panels

The ribbon move grip supports top docking, left/right vertical docking and a
floating host with horizontal/vertical orientation. Drag shows a destination
preview; Ctrl suppresses docking and Escape, pointer cancellation or window blur
restores the pre-drag state. Double-click toggles floating and the previous dock.
The side dock width and both floating orientation sizes persist. On narrow screens,
a stored side dock becomes a bounded floating host, retaining its desktop preference.

Right-click a tab, panel title, command or ribbon background for context-specific
Show Tabs/Show Panels, move, hide, float, return, docking and customization actions.
Tabs and panels can also be reordered by dragging within their ribbon. Floating
panels retain the original controls across tab switches, support orientation changes
and can be returned by dropping over the ribbon. Home Modify and Block expose
secondary commands through slide-outs. A pushpin keeps an expanded panel open;
workspace save/transfer retains its pin. Responsive collapsed panels expose the
complete command set directly.

Bare Alt opens scoped key tips for the application menu, Quick Access and ribbon
tabs. Selecting a tab displays command key tips, including native inputs, split
menus, collapsed panels and slide-outs. Escape steps back and restores focus;
mouse input dismisses the overlay. IME composition, text editing, Ctrl/Meta/AltGr
combinations and drafting function keys keep their existing handlers. F10 remains
Polar Tracking. The optional menu bar supports Left/Right navigation, Down to open,
Left/Right to switch open menus and Escape to return to its menu heading.

Behavioral reference: Autodesk's [ribbon tabs, floating panels and pinned slide-outs](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-D20EF1D7-4135-48A7-B68E-65BF3BFF3D70.htm).

## Window behavior and interface locks

Floating palette **Auto-hide** rolls its content into the title bar. Pointer hover,
keyboard focus or the expand control reveals the same live content. Escape rolls it
up and returns focus to the title control. This differs from left/right anchoring,
which attaches a palette to an edge and overlays the drawing when opened. All eight
floating resize edges preserve the opposing edge, enforce readable minima, and
restore exact geometry on cancellation. No palette renderer is recreated merely
because its floating content rolls up.

The command-window context menu provides Dock top/bottom, Anchor top/bottom,
Float, Allow Docking and independent inactive/active opacity. Docking reserves space;
anchoring follows the drawing edge without reserving space. The top dock places the
splitter below the window and reverses its growth direction. All gestures preserve
the original input, buffer, selection and focus. Losing pointer capture or window
focus cancels resizing. Opacity applies only when floating or anchored.

The status-bar lock controls `LOCKUI`. Right-click it to choose categories. Values
combine as a bit mask; `15` locks all categories and `0` unlocks them.

| Bit | Category | Owners |
| --- | --- | --- |
| 1 | Docked toolbars and panels | Ribbon host and docked ribbon panels |
| 2 | Docked or anchored windows | Palettes and command window |
| 4 | Floating toolbars and panels | Floating ribbon host and ribbon panels |
| 8 | Floating windows | Floating palettes and command window |

Holding Ctrl temporarily allows movement or resizing. The persisted lock mask stays
unchanged; releasing Ctrl ends the override. Opening, closing and using controls
remain possible. The pre-existing palette-only layout lock is an independent,
additional preference. Position changes do not enter the drawing undo stack.

Hide/Show Palettes records visibility instead of closing every palette. Reopening
one palette reveals only that palette while the remaining set stays hidden. A
subsequent manual close remains closed after Show Palettes. The command window,
ribbon and drawing-tab strip similarly restore their prior states unless explicitly
changed during the hidden period. The bounded palette exception list survives
workspace normalization and reload.

References: [dockable window behavior](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-6EF21814-B9FB-4F2A-897E-852B25679D17.htm),
[LOCKUI](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-19C5EEB0-D2DD-4726-89A7-3D346B513715.htm),
[HIDEPALETTES](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-0C234107-7EBC-44EE-876A-6A4C0CC17DFE.htm), and
[SHOWPALETTES](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-EEE9EDD3-3A4D-494B-8CA2-F2A86F36D541.htm).

## Workspace policy and recovery commands

Workspace saving is manual by default. `WSAUTOSAVE 1`, or Automatically save
workspace changes in the switcher, saves an existing outgoing named workspace
before switching. Built-in presets, detached layouts and deleted profiles are never
overwritten. Names are matched case-insensitively while retaining stored casing;
preset identity remains distinct from an identically named saved profile. Storage
failure aborts the switch and retains the active layout. Explicit Reset bypasses
autosave. The global autosave policy survives applying presets or saved workspaces.
This follows the current [WSAUTOSAVE definition](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-8E8D0109-CA57-4E24-A9E2-B588B0BC19CB.htm).

The live ribbon tab persists independently of named workspace saves. Startup waits
for discipline entries before resolving a saved discipline tab. Imported UI state
contains bounded declarative presentation data and registered command IDs only.

| Command or variable | Effect |
| --- | --- |
| `RIBBON`, `RIBBONCLOSE` | Show/hide the ribbon, retaining its docking and display mode |
| `COMMANDLINE`, `COMMANDLINEHIDE` | Show/hide the existing command editor |
| `TEXTSCR` or Ctrl+F2 | Open the command-history text window |
| `FILETAB`, `FILETABCLOSE` | Show/hide the drawing-tab strip |
| `FILETABSTATE 0/1` | Set drawing-tab visibility |
| `LAYOUTTAB 0/1` | Set Model/paper tab visibility |
| `MENUBAR 0/1` | Set optional menu-bar visibility |
| `QATLOCATION 0/1` | Move Quick Access above/below the ribbon |
| `LOCKUI 0..15` | Set position/size lock categories |
| `WSAUTOSAVE 0/1` | Disable/enable outgoing named-workspace autosave |
| `HIDEPALETTES`, `SHOWPALETTES` | Suppress/restore palettes, command window, ribbon and drawing tabs |
| `CLEANSCREENON`, `CLEANSCREENOFF` | Enter/exit temporary clean-screen presentation |
| `SAVEALL` | Download each open drawing through its existing DXF save operation |
| `CLOSEALL`, `CLOSEALLOTHER` | Close drawings sequentially with existing unsaved-change confirmation |

Save/Close All holds a document-ownership token for the entire sequence. Public
activation, creation and edits cannot redirect it between asynchronous requests;
document/layout controls and background polling are suspended. Cancel stops the
sequence. Save All and Close Others restore the originating drawing when it remains
open, and Close All leaves an editable blank drawing. Browser download permissions
still govern saving multiple files.

## Implementation and qualification map

| Component | Responsibility | Regression coverage |
| --- | --- | --- |
| `ribbon-host-model.mjs`, `ribbon-host.mjs` | Bounded placement, orientation, docking, focus-preserving host moves | `ribbon-host.test.mjs`, `ribbon_workspace_browser_checks.py` |
| `ribbon-context.mjs`, `ribbon-keytips.mjs`, `ribbon-slideout.mjs` | Context customization, scoped key tips, expanded/pinned secondary tools | `ribbon-keytips.test.mjs`, ribbon keyboard/workspace browser checks |
| `palette-layout.mjs`, `palette-docking.mjs` | Groups, all-edge resize, roll-up, anchoring, visibility exceptions, locks | Model/controller tests and shared palette browser checks |
| `desktop-chrome.mjs`, `workspace-shell.mjs` | QAT placement/menus, optional menu bar, lock/status controls, history window | `desktop_parity_browser_checks.py` |
| `workspace-shell-model.mjs`, `command-line-docking.mjs` | Validated shell state, retained command editor, dock/anchor geometry and opacity | `workspace-shell.test.mjs`, desktop shell/parity browser checks |
| `workspace-profiles.mjs`, `workspace-profile-ui.mjs` | Typed workspace identity, explicit saving, autosave-before-switch | Model tests and `workspace_autosave_browser_checks.py` |
| `app.mjs` document batch owner | Serial Save/Close All and protected active document | `document_batch_browser_checks.py` at the end of disposable native/WASM sessions |

All shared browser fixtures run against the native host and the static-only
WebAssembly host. The same catalog, immutable drawing engine and frontend assets
serve both. Regression checks assert drawing identity, revision, selection and undo
history preservation for presentation-only actions, as well as CSP-safe operation.

This is browser-local ribbon and docking behavior for Studio's supported commands.
Operating-system windows, second-monitor native docking, CUIx/DIESEL/AutoLISP
customization, and AutoCAD-only command contexts are not implemented by these
modules. Browser-reserved shortcuts have visible menu or command alternatives.
