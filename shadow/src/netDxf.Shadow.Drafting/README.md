# Reusable CAD drafting

`netDxf.Shadow.Drafting` depends on the immutable shadow/editor/rendering contracts,
not netDxf, ASP.NET, SkiaSharp, or a particular application. A `DraftingPlanner`
validates detached command input and returns a `DraftingPlan`. Planning has no side
effects. Execute the plan through an `EditorSession` for one canonical undo entry,
or apply its patch to a `ShadowMemoryStore` for an isolated preview.

```csharp
var planner = new DraftingPlanner();
var plan = planner.Plan(editor.Snapshot,
    new DraftingCommand("RECTANG", [new(0, 0), new(120, 80)], Layer: "0"));
plan.Execute(editor);
editor.Undo();
editor.Redo();
```

Every plan carries document identity and expected revision. Stale plans fail before
source adoption. Entity edits reject locked layers; creation rejects locked/frozen
layers. The host must additionally guard document/workspace identity and serialize
its own scene/selection context. Immutable plans do not grant authorization.

## Construction and modification

| Command | Contract |
| --- | --- |
| LINE / PLINE | Point chain; LINE is one transaction even when it creates many entities |
| RECTANG / POLYGON | Closed XY polyline; polygon uses center/radius and 3–1024 sides |
| CIRCLE / ARC / ELLIPSE | Analytic geometry; ARC is three-point, ELLIPSE is center/major endpoint/minor point |
| POINT / TEXT / MTEXT | Explicit location, text height, text content and optional rotation/MTEXT width |
| SPLINE | Clamped cubic control-point NURBS, not a sampled polyline or fit-point spline |
| DIMALIGNED / DIMLINEAR | Native dimension definitions; two references plus dimension-line point |
| INSERT | Existing named block, position, signed XYZ scale, rotation and optional alternate definition base point |
| MOVE / ROTATE / SCALE / MIRROR | Explicit base point/axis; representation-preserving existing affine planners |
| STRETCH | Crossing-window endpoints/vertices, insertion/center anchors and fully enclosed affine-representable geometry |
| BREAK / BREAKATPOINT | Projected cuts on XY LINE, ARC, CIRCLE or Polyline2D; preserve analytic bulges and interpolate widths |
| COPY / ARRAY | Deep entity-owned data copies, shared table/block definitions; rectangular array |
| ERASE | Structural removal with canonical undo/redo |
| OFFSET | Lines, circles/arcs, and simple straight polylines; side point plus positive distance |
| TRIM / EXTEND | LINE endpoint editing against an intersecting finite LINE or CIRCLE boundary |
| FILLET / CHAMFER | Two intersecting XY lines; trim to a tangent circular arc or equal-distance bevel |
| JOIN | Unbranched connected LINE chain; endpoint adjacency rather than repeated edge scans |
| EXPLODE | XY Polyline2D to analytic lines/arcs, or one level of an ordinary block reference with exact supported affine geometry |

Coordinates use drawing units; angles use degrees. ARRAY uses `Count` columns,
`Value` integral rows, and the delta of its two points as X/Y cell spacing. The
original occupies the first cell. COPY uses its two points as displacement.

`STRETCH` takes four points: two crossing-window corners, a base point and a
destination. Its explicit selected entities scope the edit. LINE endpoints and
Polyline2D vertices inside the window move by the displacement; exterior
vertices remain fixed. Widths and bulges are retained. Circles/ellipses use their
center, while text, points and INSERTs use their insertion point; INSERT
attributes move together and shared definitions remain unchanged. Other supported
geometry moves when wholly enclosed, using the existing affine planner. Conic
containment uses analytic extrema and NURBS use their positive-weight control
hull. Partially enclosed arcs/splines and constrained geometry still require
additional planners and are rejected atomically. The UI offers a crossing
rectangle by drag or two points, selection removal, base/second point and
Displacement prompts. Crossing polygons and multiple windows remain outstanding.
These behaviors follow the documented
[STRETCH convention](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Core/files/GUID-F000A502-D39E-4D31-A8E2-4A626473FB72.htm).

`BREAK` takes two projected points and one selected entity. Open curves retain
the portions outside the gap; a whole-curve gap deletes the source. A circle
becomes an arc after removing the counterclockwise interval. Closed polylines
retain one open path across the authored seam. `BREAKATPOINT` takes one interior
point on an open supported curve and splits it without a gap. Polyline cuts
retain exact circular bulges and interpolate tapered widths at each cut. The
browser supports a picked first point, the `First` option and `@` for a no-gap
break. Source identities, appearance, undo/redo and DXF output are verified.
Persistent attached data that cannot be redistributed safely is rejected before
mutation. Ellipse/spline splitting remains outstanding. See Autodesk's
[BREAK](https://help.autodesk.com/cloudhelp/2025/ENG/AutoCAD-Core/files/GUID-36A1CDE0-3871-4B25-AC98-93235FA83863.htm)
and [BREAKATPOINT](https://help.autodesk.com/cloudhelp/2025/ENG/AutoCAD-Core/files/GUID-E0439DE0-B2C3-4233-BB4D-5A574A00694B.htm)
command conventions.

`INSERT` retains shared block definitions. `Value` continues to supply a uniform
scale; optional `Scale` supplies separate X/Y/Z factors and allows negative axes
for mirroring. Optional `BasePoint` is expressed in the definition's coordinates:
that point lands on `Points[0]`, accounting for block origin, rotation, scale and
native block/drawing unit conversion. The definition's origin remains unchanged.
Scale components must be finite and have absolute values within the configured
drafting length/coordinate limits. Native construction places default attributes
using the complete insertion transform and owner units.

```csharp
planner.Plan(editor.Snapshot, new DraftingCommand("INSERT", [new(100, 50)],
    Reference: definitionId, Scale: new(2, -2, 1), Rotation: 30,
    BasePoint: new(10, 5))).Execute(editor);
```

The Studio block picker searches named definitions and shows their descriptions,
origin, units and attribute counts. It offers uniform or separate XYZ scale,
rotation, an optional alternate base point and repeated placement. Click a point
for each reference; Escape ends repetition. Each placement is independently
undoable. Attribute defaults are retained and can be edited through source
properties. Existing command-line `INSERT` name/point/uniform-scale/rotation input
remains supported.

Block `EXPLODE` clones one level of definition entities, applies the exact native
insertion coordinate transform, resolves layer 0/ByBlock inheritance and removes
only the selected reference. Nested INSERTs remain references to shared inner
definitions. Original definitions and other instances remain intact. The whole
selection is one transaction and is rejected if it requires an unsupported affine
representation (for example, nonuniformly scaled circles), attribute-to-text
conversion, MINSERT expansion or INSERT XDATA redistribution.

## Explicit boundaries

This is not an AutoCAD compatibility claim or a solid-modeling kernel. Unsupported
combinations fail explicitly without changing the source: general curve trimming,
self-intersecting offsets, tapered-polyline explosion, unrestricted block explosion,
non-XY planar commands, and automatic metadata redistribution for JOIN/EXPLODE.
COPY preserves captured owned values rather than copying tessellated render output.
The source adapter can reject a data-model shape for which no public reconstruction
contract exists; the transaction is not partially accepted.

Drafting budgets are configurable. The default bounds point/entity/clone counts and
coordinates; OFFSET self-intersection verification has an additional 1024-edge cap.
Source-normalization, arbitrary file/font resolution, render fidelity and numerical
conditioning retain their separate contracts in the respective infrastructure.

`DraftingVerification` runs in the existing cross-platform advanced verification
suite and exercises source/memory stores, DXF round trips, undo, copies, construction,
modification, stale input, locked layers and negative input controls.

### Named resource managers

`DraftingLayers.Plan` and `DraftingResources.Plan` return ordinary immutable plans, executable through `EditorSession` as one undo step. `DraftingResources.Linetypes` and `TextStyles` expose detached manager records, including current/reserved status and source reference counts. Studio exposes these through `GET resources` and `POST resource`; layer saves retain `POST layer`. All writes use the usual source revision and document/workspace/view checks.

The shared Studio ribbon opens the Layer manager, Linetype manager, and Text style manager. Search, new, copy, rename, current, and delete operate on the document's actual tables. Deletion refuses current, reserved, or referenced resources; references from block definitions count as use. Renaming the current resource updates `CLayer`, `CeLtype`, or `TextStyle`. New drawing commands use the current linetype and text style; new TEXT takes the style's width, oblique, generation flags, and nonzero fixed height.

Layer fields include on/off, freeze, lock, plot, RGB or indexed color, existing linetype, native lineweight, transparency (0–90%), description, and freeze in new viewports. Current layers cannot be frozen, and a layer must be on and thawed before this manager makes it current. New-viewport freeze is stored metadata; individual viewport overrides and CTB/STB plotting are not implemented.

`DraftingLayerStates.Read` and `Plan` expose native saved layer states. Studio's
Home/Manage ribbon and `LAYERSTATE`/`LAS` commands open the shared manager. Save,
update, rename, delete and restore each create one undo operation. Restoration
selects global on/off, frozen, locked, plot, new-viewport freeze, color, linetype,
lineweight and transparency fields, optionally restoring the current layer and
turning off unlisted layers. Colors and transparency are independent snapshots;
capturing global fields preserves stored viewport-only flag bits. State names,
descriptions and saved properties survive text/binary DXF and workflow replay.
Saved name references count as resource use and follow layer/linetype renames,
including undo and redo. The host endpoint is `POST layer-state` with a
`CadLayerStateRequest`; resource discovery includes `layerStates`.

The editor bounds states to 1,000 and layers per state to 10,000. Restoration
rejects missing named resources and frozen current-layer targets before mutation.
Recreating missing layers would prevent native removal during undo, so unresolved
states require resource repair first. Individual saved-layer editing, persisted
restore masks, viewport overrides, and LAS import/export remain outside this
manager. Behavior reference: Autodesk's
[Layer States Manager](https://help.autodesk.com/cloudhelp/2025/ENG/AutoCAD-Core/files/GUID-3E0F662B-226F-4A27-B5BB-D15252CC994C.htm).

Linetypes support up to 256 ordered simple, text, or shape segments, with dash/gap/dot length, text or shape name, an existing document style, XY offset, positive scale, rotation, and Relative/Absolute/Upright mode. Shape segments require a numeric DXF shape identifier so saving never needs an implicit local SHX path lookup. Reserved ByLayer, ByBlock, and Continuous definitions are preserved; create a named copy to author a different pattern. The LIN picker accepts files up to 512 KiB and 256 definitions, previews one selected definition before applying it, preserves complex text options, and resolves shape names only against explicit document resources. A shape with no known numeric identifier remains editable and cannot be saved until its identifier is supplied. Existing SHX/SHP upload and presentation alignment commands remain available.

Text styles expose stored TTF/SHX font filename, optional SHX Big Font, independent TrueType family and bold/italic face metadata, fixed height, width factor (0.01–100), oblique angle (−85°–85°), vertical/backward/upside-down flags. Unedited extended metadata and unknown flag bits are preserved. Font references are metadata: the manager does not browse server files or promise that a missing font renders. Vertical behavior depends on the font. The native fidelity operation `TextStyle.SetStoredFontFiles` updates filenames without replacing extended font XData, allowing source history to restore exact font-data identities.

Behavior references: Autodesk [Layer Properties Manager](https://help.autodesk.com/cloudhelp/2025/ENG/AutoCAD-Core/files/GUID-B297EBD9-D68C-47E1-87CE-1B3798496599.htm), [Custom Linetype Definitions](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-LT-Customization/files/GUID-5A6E6759-8A9A-4A8A-9AEE-EE9DB72F792D.htm), and [complex linetype syntax](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-create-Complex-linetypes.html). These managers cover the supported netDxf fields; they do not claim complete AutoCAD feature parity.
