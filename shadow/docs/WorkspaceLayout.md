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
AutoCAD CUI compatibility, tear-off ribbon panels, palette transparency or
multi-monitor native windows.

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
