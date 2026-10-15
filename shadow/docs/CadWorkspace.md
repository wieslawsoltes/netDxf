# CAD workspace

`netDxf.Shadow.Studio` now opens a local, source-backed 2D CAD workspace. Run
from the repository root with the .NET 8 SDK:

```sh
dotnet run --project shadow/samples/netDxf.Shadow.Studio -c Release -p:TargetFrameworks=net8.0
```

Open `http://127.0.0.1:5080`. Linux needs fontconfig and usable system fonts.
The previous diagnostics interface remains at `/diagnostics.html`. GitHub
Pages remains an explicitly read-only rendering gallery: it does not run this
ASP.NET application. No host/origin/CSRF/receipt protection has been relaxed.

## Working with drawings

The quick-access bar creates blank drawings, opens a DXF into a separate tab,
downloads an edited DXF, and controls undo/redo. The hub retains at most eight
native documents with independent selection, history, saved state and branches.
Closing a dirty drawing requires confirmation. Browser unload warns when the
current document list contains unsaved work. This is not durable autosave:
closing the local host loses unsaved in-memory drawings.

The dark ribbon exposes connected Draw, Modify, Annotation, Layers, Blocks,
View, Inspect and Output tools. Layout tabs switch model/paper views. The right
palette has Properties, Layers, Blocks, Drawing explorer, Workspaces and
Diagnostics pages. Palette and command-line splitters support dragging and
arrow keys; the palette can be docked left/right or hidden. Display preferences
are local to the browser, not written into the DXF.

Middle-drag or Space-drag pans; the wheel zooms about the pointer. Two-finger
touch pans/zooms. `Z E` or Zoom extents fits the native scene. A cached rendered
frame is reprojected during camera movement while the next native frame loads.
Skia PNG and SVG share the existing shadow compiler, text/resource providers,
source appearances and retained snapshots; the browser is not a second DXF
renderer. Grid/crosshair/grips and transient construction overlays are drawn
in a device-pixel-ratio-aware canvas. Top/WCS navigation is a 2D orientation
indicator, not a 3D orbit control.

## Precision and selection

Start commands from the ribbon or type a command/alias. Enter finishes a point
chain or accepts a default; Escape cancels without committing partial geometry.
Up/down recalls command history. Input supports absolute `X,Y[,Z]`, explicit
absolute `#X,Y`, relative `@dX,dY`, polar `@distance<degrees`, and a distance in
the current cursor direction. Numeric prompts accept bounded arithmetic with
`+ - * / ( ) pi` and scientific notation; no JavaScript is evaluated.

For example, `L 0,0 100,0 100,80 C` makes a closed three-segment native LINE
chain in **one undo transaction**. `C 140,40 25` makes a native CIRCLE. A chain
accepts `U` to remove its most recent uncommitted point. TEXT and MTEXT consume
the remaining input as text; `\P` creates an MTEXT paragraph.

Click to select; drag left-to-right for window containment and right-to-left
for crossing selection. Ctrl adds, Shift removes. Selection expands to the
root INSERT instead of modifying a shared block definition accidentally.
Modification commands use the ordered source selection. TRIM and EXTEND require
exactly two selections: target LINE first, then boundary LINE/CIRCLE; the pick
point chooses the target endpoint. Select with Ctrl in this order before
starting, or press Enter after the command's selection prompt.

Blue grips edit validated source properties. The property palette commits
supported scalar/coordinate values on Enter. Layer creation, flags, color,
linetype, lineweight and assignments use the same native transaction/history
infrastructure. Locked-layer edits are refused, not silently discarded.

Running snaps can include endpoint, midpoint, center, quadrant, intersection,
perpendicular, tangent, insertion and nearest. Analytic snaps come from
`DraftingQueries`; numerical nearest-curve results are marked approximate.
Status toggles: F3 object snaps, F7 grid, F8 ortho, F9 grid snap, F10 polar
tracking, F12 dynamic coordinate display. F2 expands command history; Ctrl+1
shows/hides the palette, Ctrl+0 toggles the reduced-chrome view. Dynamic display
is a prompt/coordinate readout; keyboard precision input stays in the command
line rather than claiming every AutoCAD dynamic-input mode.

### Input transaction fences

A pointer gesture is bound to the drawing, workspace, view, revision, active
command prompt and retained-snapshot state at pointer-down. Final object-snap
acquisition is independent of hover sampling, so a later mouse move cannot
abort an exact click into an unsnapped edit. Escape, a new command, a typed
prompt advance or a document/view transition invalidates outstanding picks and
grips. Stale or aborted final acquisition is refused explicitly; geometry is
never committed to a different prompt or drawing as a fallback. Reads are also
fenced by the client generation, including same-document reactivation and
queued writes before the next native revision has been published.

## Connected command scope

Construction: LINE, PLINE, RECTANG, POLYGON, CIRCLE, three-point ARC, ELLIPSE,
POINT, open clamped control-point SPLINE, TEXT, MTEXT, DIMALIGNED, DIMLINEAR and
INSERT of an existing named block. The spline transient shows its control
polygon; the committed curve uses native spline rendering.

Modification: MOVE, COPY, ROTATE, uniform SCALE, MIRROR, rectangular ARRAY,
ERASE, OFFSET, endpoint TRIM/EXTEND, FILLET/CHAMFER, JOIN and EXPLODE. Geometry
admission is defined by `DraftingPlanner`, not by the buttons: offset supports
its admitted XY primitives/straight polylines, trim/extend is LINE against
LINE/CIRCLE, fillet/chamfer admits intersecting XY lines, join admits compatible
LINE chains, and explode admits supported Polyline2D vertices/bulges. Unsupported
geometry, nonplanar cases and lossy conversions produce explicit errors. ARRAY
includes the original cell; zero/invalid spacing and quotas are native errors.
DIST and polygon AREA are non-mutating measurements in drawing units.

Views can retain read-only snapshots, show graph overlays and expose render
and grip diagnostics. Branches support fork/switch and explicit conflict-policy
review before applying one canonical merge transaction to the live source.
SHX/SHP loading and annotation/linetype view context remain available. PNG/SVG
exports use native rendering; DXF save invokes the actual netDxf writer.
Missing/generated dimension pictures are exported through the reusable bounded
`SaveWithDimensionPictures` API without normalizing live source geometry outside
editor history. Imported custom pictures are preserved. Reimport adds the
picture's block-definition entities to the detached graph; these are not extra
editable model-space roots. ASCII/binary output, repeated save, edited dimension
headers, owner context, cleanup on failures and undo across save are covered by
native regressions.

This is an AutoCAD-inspired **2D** workspace, not certification of pixel-perfect
AutoCAD parity. DWG, 3D solids/orbit, parametric constraints, block-definition
authoring, hatch authoring, all grip/modify variants and full plot/page-setup
workflows are not implemented by this UI. Existing imported content retains
the shadow renderer's documented capability/diagnostic boundaries.

## Reusable boundaries

- `netDxf.Shadow.Drafting`: immutable command/query/grip/layer contracts and
  source-aware geometry plans. No HTML, HTTP, Skia canvas or application UI.
- `EditorSession` and the netDxf adapter: validate and apply complete plans with
  source identity, structural history and optimistic document/revision checks.
- `StudioDocumentHub` and `StudioCadHttp`: bounded host lifetimes and receipt-
  guarded per-document services. This is still a local single-user host.
- `wwwroot/cad/core.mjs`: DOM/network-free precision parser, camera math,
  constraints and command state machine; importable independently by other UIs.
- `client.mjs`: serialized mutations, generation-fenced reads and explicit
  conflicts. Failed writes are never implicitly retried. Queued operations
  cannot migrate silently into another drawing/workspace/view.
- `viewport.mjs`, `ui.mjs`, `app.mjs`: native-frame composition, transient input
  and DOM adapters, separated from source geometry and command planning.

## Verification

```sh
node --test shadow/tests/browser/*.test.mjs
python -m unittest discover -s tests/ci_pipeline -p 'test_*.py'
dotnet publish shadow/samples/netDxf.Shadow.Studio -c Release \
  -p:TargetFrameworks=net8.0 -r linux-x64 --self-contained true \
  -o artifacts/studio-linux-x64
python -m pip install playwright==1.57.0
python -m playwright install --with-deps chromium
python shadow/tests/cad_workspace_browser.py --published artifacts/studio-linux-x64
```

The Linux Shadow CI job runs the real-origin Chromium test against the actual
published app without mocked routes. It checks native creation/history,
modifiers, selection, properties, layers/refusals, snapshot restrictions,
branch merging, real DXF download/reopen, native SVG export, compact layout and
uncaught JavaScript/CSP errors. Screenshots, results and host diagnostics go to
`artifacts/studio-browser/` and the existing retained verification artifact.
The existing native verification, conformance, package/runtime and qualification
gates remain active.
