# Detached complex linetypes

Select **Complex linetypes** in Studio's layout selector for native text, upright reversed lines, explicit SHX stroke text, numeric SHX shapes, a closed polyline and an arc. The existing inspector, selection, movement, history, retained views, branch workspaces, Skia and SVG backends remain the same. The synthetic SHX instructions are generated in source; no font files are bundled or implicitly loaded.

## Source preservation is independent of resource availability

`LinetypeShapeSegment.ShapeNumber` is a nullable signed 16-bit authored DXF group-75 word. A non-null value is authoritative, including zero and high-bit values represented as negative shorts. The DXF reader preserves it even when the local shape resource cannot provide a name. The writer uses it without requiring a name lookup. Clone preserves the word and clones the shape style. Unresolved complex segments are no longer deleted on load, so missing resources cannot shorten a linetype's period.

Changing `Name` or `Style` does not silently clear the authored identifier. Explicitly set `ShapeNumber = null` to resume the existing name-to-number lookup behavior on save. Source loading retains the reader's pre-existing optional name lookup; the renderer itself never opens a path from the drawing.

```csharp
var symbol = new LinetypeShapeSegment(
    "display-name", new ShapeStyle("PIPE_SYMBOLS", "pipe-symbols.shx"), -1)
{
    ShapeNumber = 17,
    Scale = 0.5,
    Offset = new Vector2(-0.25, -0.25)
};
```

## Neutral metadata and dependency tracking

`CadStyle.Linetype` optionally contains a `CadLinetype`: immutable elements, authored text/shape metadata, effective entity/global scale and the entity-local plane normal. Simple patterns keep the existing signed `Dashes` representation and avoid per-element complex objects. `CadLinetype.Read` reads through `ShadowReader`, including referenced STYLE records, so style/font/label edits invalidate dependent roots. ByLayer and ByBlock resolution, component-style inheritance and child entity scaling preserve complex metadata.

Occurrence equivalence includes the complete complex metadata, not only dash lengths. Changing a label from `GAS` to `WATER` therefore cannot reuse stale decoration output merely because all segment lengths remain equal. Unchanged source occurrences remain reusable. Retained snapshots keep detached strings, identifiers and style values.

## Placement contract

`LinetypePlanner.Plan` consumes sampled `RenderContour` paths and a detached pattern. It returns immutable placements, each with a contour index, element index, stable ordinal, path distance, decoration and affine frame. It does not need netDxf, Skia or a font service.

The distance metric is sampled world-space arc length, matching the existing signed stroke engine. Each decoration is anchored at the **end of its preceding dash/gap**. Phase continues through sampled edges and polyline corners. Independent contours restart the pattern. Duplicate points do not invent tangents. Closed contours include their implicit closing edge and emit their seam only once. At an exact internal vertex, the incoming nonzero edge defines the frame.

Segment `Scale` controls glyph size; offsets use the effective linetype scale independently of glyph scale. A positive authored style height multiplies segment scale; zero height uses unit height. Relative rotation follows the tangent, absolute rotation uses the world entity-plane basis, and upright rotation flips the glyph axes when necessary for that basis. Offsets remain tangent-relative when glyphs are flipped. Text width, obliquing, backward and upside-down metadata are carried independently.

Non-finite values, zero-only periods, singular planes, invalid dimensions and unrepresentable distance increments fail explicitly. Nonplanar paths require a specialized transport policy rather than a guessed frame. Curves are sampled at the existing render tolerance; their placements are not certified analytic arc-length solutions.

## Rendering and resource APIs

The render compiler expands decorations **after semantic projection**, into separate `RenderItem` instances with unique presentation keys and the original entity, insert path and source-to-world transform. Their bounds participate in the persistent spatial index, their geometry participates in picking, and existing editing maps their selection back to the source. Selecting multiple symbols from one entity does not multiply a translation. Decorations do not enter the semantic `CadScene` consumed by topology and graph analysis.

Generated symbols carry `IsPresentationOnly` on their consumer occurrences and render items. `Snapping.Find` reads the render scene but excludes these items by default. The overload with `includePresentation: true` explicitly enables glyph snapping. Paper-space copies preserve the flag. Picking and source editing remain available in either mode. The original compiler constructor, static compilation and snapping binary signatures are retained alongside the extended overloads.

Ordinary text uses the existing text layout service. `ILinetypeDecorationProvider` allows a host to supply unit-size local geometry and a revision. A provider may handle additional typography; component trees are not accepted as decoration prototypes. Providers must bound their own parsing, glyph compilation and custom curve complexity.

`ShxEntityProjector` implements the provider using the same explicit catalog or pinned `ShxFontSet` as entity projection. The render compiler discovers the first such capability in its builder's ordered projector registry; an explicit constructor argument overrides that choice. Numeric shape words are interpreted as unsigned SHX indices, with names used only when no number was authored. Horizontal Unicode/legacy SHX text becomes stroke geometry. Missing resources produce one diagnostic per affected pattern element and occurrence without changing dash spacing. No system-font substitution is silently made for missing SHX linetypes.

```csharp
var fonts = new ShxFontCatalog();
// Registration is an explicit host action; load only a resource the host has authorized.
fonts.Register("pipe-symbols.shx", authorizedResource);
var shx = new ShxEntityProjector(fonts.Current); // Pinned resource snapshot.
var builder = new CadSceneBuilder([shx]);
var compiler = new RenderCompiler(builder, textLayout: hostTextLayout,
    linetypeOptions: new LinetypePlanOptions(MaximumPlacements: 10_000));
var render = compiler.Compile(snapshot);
```

Prototype resolution is reused per pattern element; compiled expansions are weakly keyed by source occurrence, sampling options and provider revision. Snapshot render keys also include the provider revision. A revision change during compilation rejects publication. Studio live, retained and branch compilers reuse their existing live/pinned resource ownership rules. Arbitrary system-font installation changes are not pinned by an SHX snapshot.

## Work limits and atomic publication

The defaults are 4,096 pattern elements, 20,000 placements per occurrence, 2,000,000 sampled points, 100,000 UTF-16 characters per decoration and 200,000 placements per compiled scene. Repetition counts are checked before a microscopic period can cause billions of iterations.

Expansion additionally defaults to 500,000 work units per occurrence and 2,000,000 per compiled scene. Units count attempted render items, contours, analytic curves and UTF-16 character copies, plus image corner/clip points. Missing-resource placements still consume attempt work. A shared ledger is charged on cache hits and rejected groups, so cached output cannot bypass the scene quota. Complete groups are preflighted before repeated geometry copies or repeated text layout are created.

These are deterministic complexity quotas, not byte limits, native allocation limits, wall-clock guarantees or limits on arbitrary trusted provider code. SHX decoding and glyph compilation retain their own limits. Paper viewport composition retains its separate limits and reuses model-scene compilation.

Failure to expand a decoration group leaves its base stroke and an explicit `render.linetype` diagnostic; no partial group is published. Successfully resolved groups are built completely before being added to the render scene. Cancellation is checked in path traversal, planning and expansion and is never converted into a successful partial frame.

## Verification

The existing advanced executable suite contains source round trips across four DXF versions in both ASCII and binary, signed shape-word preservation, immutable capture, planner edge cases, cache/style/resource invalidation, graph isolation, both backends and source-identified picking. Studio, editor and snapping checks exercise the gallery, save/reload, duplicate-source selection, insert/definition transforms, undo/redo and explicit presentation snapping. Expansion-quota tests use counting curves to verify rejection before geometry copies. All run in the existing cross-platform Shadow jobs; no workflow or existing gate is weakened.

```sh
dotnet run --project shadow/tests/netDxf.Shadow.AdvancedVerification -c Release -p:TargetFrameworks=net8.0
python shadow/tests/http_contract.py
node --test shadow/tests/browser/state.test.mjs
```

## Fidelity boundaries

This increment is not a claim of complete proprietary CAD appearance. The periodic engine does not implement AutoCAD's A-alignment end fitting, its complete character rotation pivot rules, all PLINEGEN/MSLTSCALE/PSLTSCALE and viewport overrides, or full nonuniform-insert glyph fitting. Upright orientation is supported by detached planning and authored in-memory metadata; this change does not add a distinct upright encoding to the existing DXF writer. Complete BIGFONT/vertical layout and arbitrary nonplanar transport remain specialized provider work. The raw source snapshot retains data that a default consumer cannot interpret.

Reference specifications: Autodesk [LTYPE DXF group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F57A316C-94A2-416C-8280-191E34B182AC.htm), [text in custom linetypes](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-FEDCE7EB-4919-43AE-A54E-F3A293DD60CA.htm), and [shapes in custom linetypes](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-AF0613E6-5C8B-47F0-800C-8B2524BF2015.htm). The existing netDxf reader/writer's degree-valued rotation representation is preserved; this increment does not change the source angle codec.
