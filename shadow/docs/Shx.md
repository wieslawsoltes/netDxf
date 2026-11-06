# Explicit SHX resources and detached stroke typography

`netDxf.Shadow.Rendering.Shx` is an optional .NET 8 library with no native, Skia,
Python or netDxf dependency. It references the neutral rendering/geometry layer.
It does not read filenames, contact a network, install fonts or embed font bytes.
Hosts explicitly supply bytes/text and control aliases, trust and lifetime.

## Processing stages

1. `ShxFontReader.ReadBinary` or `ReadAscii` detaches input into immutable font
   metadata, symbol maps and typed instructions. Caller-owned buffers are not kept.
2. `ShxGlyphCompiler.Compile` interprets a symbol under work/recursion/stack budgets,
   emitting analytic `LineCurve`/`EllipseCurve` contours, advance and ink bounds.
3. `ShxEntityProjector` projects registered SHAPE entities or rich TEXT/MTEXT and
   attributes into ordinary neutral geometry. Consumer tessellation and native
   resources remain downstream of the immutable source model.

Text strokes use `CadStrokeGroup` / `RenderStrokeGroup`, which are presentation
geometry and do not introduce glyph edges into semantic topology. A SHAPE entity,
in contrast, has semantic curves and can participate in graph analysis. Colors,
decoration regions, occurrence transforms, paper-space depth/XY clipping and
stroke-aware picking are shared by SkiaSharp and SVG.

## Supported format and execution contracts

The binary reader supports SHAPES 1.0/1.1 and UNIFONT 1.0;
[BIGFONT](BigFont.md) has a separate format and mapping contract. The textual reader accepts
Autodesk SHP (not GIS shapefiles), wrapped comma/parenthesis lists, comments, decimal
and leading-zero hexadecimal numbers, signed octant operands and font metadata.
Symbol names are case-sensitive. Resource aliases are case-insensitive. Duplicate
IDs/names, truncation, bad directory ranges, unexpected terminators/padding and
input/instruction budgets are explicit failures. SHP declared byte counts are
retained as authoring metadata; this reader is not an SHP-to-SHX binary compiler.

The interpreter supports packed vectors, pen state, scaling, position push/pop,
subshape calls, individual/repeated displacements, octant/fractional arcs,
individual/repeated bulges and vertical-conditional instructions. Subshapes inherit
pen and scale state. Position restore does not draw a stroke. The default saved
position stack is four entries; returned glyphs must leave it balanced. Reserved
opcodes, illegal arc counts and forbidden bulge operands are rejected. Analytic
arc bounds include cardinal extrema in the directed sweep.

`ShxExecutionOptions` defaults to one million instructions, 200,000 curves,
64 nested calls and four saved positions. Cancellation is checked during execution.
The entity projector additionally bounds character count, generated curves,
uncached glyph work and per-font cache size. Source font objects are cache keys
through a weak-key table; glyph results contain no source/native references.

Horizontal text uses Unicode scalar values for Unicode fonts. Legacy degree,
plus/minus and diameter map to reserved symbols 256, 257 and 258. The shared text
parser maps `%%c` to U+2205; literal U+00D8 is unchanged. A nonbreaking space can use
the ordinary space glyph. Missing symbols produce a visible box and diagnostic,
not invisible content. Text glyphs must restore their scale to 1; arbitrary
cumulative cross-glyph scale requires a separate stateful layout provider.

Rich text preserves wrapping, scoped width/height/tracking/oblique, inline colors,
decorations and stacked fractions. Requested inline resource aliases are resolved
only from the host catalog. Optional `|bold`, `|italic`, or `|bold|italic` aliases
select authored variants; missing variants are diagnosed instead of fabricated.
Unicode embedding flags, including bit combinations, remain metadata. Hosts remain
responsible for the supplied resource's license; this library never exports it.

## Live versus frozen resource snapshots

A source snapshot intentionally does not own external fonts. `ShxFontCatalog`
publishes immutable `ShxFontSet` values, each with a monotonically increasing
revision. Replacing an alias updates live projection caches without changing DXF
source revision or history. Registering the same object under the same alias is a
no-op. Replacements never mutate older sets.

Pass a catalog to a projector for live resources, or a `catalog.Current` value for
pinned resources. Hosts should serialize changes with compilation, or use pinned
sets for deterministic exports. Scene construction rejects a resource revision
change observed during the build instead of publishing a mixed-resource scene.
The Studio retains a pinned SHX compiler with each retained source snapshot.
Native/system fonts and raster resolvers have their own host lifetime contracts.

```csharp
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;

// approvedBytes comes from an explicit host-owned upload/asset policy.
var font = ShxFontReader.ReadBinary(approvedBytes, new ShxReadOptions(
    MaximumBytes: 4 * 1024 * 1024,
    MaximumInstructions: 250_000));
var catalog = new ShxFontCatalog(maximumFonts: 32);
catalog.Register("drawing-font.shx", font);

var liveBuilder = new CadSceneBuilder([new ShxEntityProjector(catalog)]);
var liveCompiler = new RenderCompiler(liveBuilder);
var liveScene = liveCompiler.Compile(snapshot);

// Pin both source and external resources for reproducible retained rendering.
var retainedSource = snapshot;
var retainedFonts = catalog.Current;
var frozenCompiler = new RenderCompiler(
    new CadSceneBuilder([new ShxEntityProjector(retainedFonts)]));
var retainedScene = frozenCompiler.Compile(retainedSource);
```

Both scenes work with the existing SkiaSharp, SVG and custom rendering backends.
SVG output contains paths for SHX text, not browser font references or font bytes.
Text source values and source identity remain in the shadow snapshot for editing.

## Studio and verification

**Load SHX/SHP** sends the selected resource to a bounded local endpoint. The sample
accepts 4 MiB, stores up to 32 aliases and normalizes imported `.shp` names to `.shx`
references. Imported resources do not authorize any further I/O. They are in-memory
and are not embedded in DXF save output. The sample's **SHX strokes** page uses
original synthetic glyph instructions defined in C#, not bundled third-party fonts.

`ShxVerification` and `ResourceRevisionVerification` exercise synthetic ASCII/binary
records, sixteen-bit Unicode subshape calls, every truncated prefix of binary test
records, metadata, all implemented VM instructions, budgets, cycles, stack errors,
coordinate bounds, named shapes, typography, paints, graph separation, resource
replacement/pinning, paper clipping, save/reload and frontend-facing font import.
Rendered artifacts are `shx-gallery.png` and `shx-gallery.svg` (never font files).

## Remaining specialized work

[BIGFONT decoding and explicit character maps](BigFont.md) and
[vertical paragraph flow](#vertical-typography) are supported under their separate
resource contracts. Complete paragraph bidi/shaping and fields, arbitrary
cumulative glyph state, exact TEXT fit/aligned semantics, mixed native/SHX runs,
exact miter behavior and proprietary-renderer certification remain specialized
work. [Complex linetypes](Linetypes.md) document their own placement limits.
Unregistered SHX text falls back through
the standard projector with a substitution diagnostic; an unresolved named SHAPE
reports an error rather than drawing a fabricated shape.

## Primary format references

Autodesk AutoCAD: Special Codes Reference:
https://help.autodesk.com/view/ACD/2026/ENU/?caas=caas/documentation/CIV3D/2014/ENU/filesACD/GUID-06832147-16BE-4A66-A6D0-3ADF98DC8228-htm.html

Autodesk AutoCAD: Unicode Font Descriptions:
https://help.autodesk.com/view/ACD/2026/ENU/?caas=caas/documentation/CIV3D/2014/ENU/filesACD/GUID-D38A5A7B-1877-46B3-8120-32DA5F7430D1-htm.html

Autodesk AutoCAD: Shape Descriptions:
https://help.autodesk.com/view/ACD/2026/ENU/?caas=caas/documentation/CIV3D/2014/ENU/filesACD/GUID-DE941DB5-7044-433C-AA68-2A9AE98A5713-htm.html

Binary directory layouts were cross-checked against ezdxf's primary implementation
under MIT terms. See `src/netDxf.Shadow.Rendering.Shx/ThirdPartyNotices.md`.

## Vertical typography

Styles declaring `IsVertical` now select the font's authored vertical programs. Opcode 14 is executed by the existing bounded glyph VM with `vertical: true`; horizontal and vertical glyphs have independent cache keys. This is not a rotation of a horizontal text string. The glyph starts at its authored top-center origin and returns to the column baseline before advancing downwards.

The shared paragraph compositor accepts vertical-flow metrics through an additive overload. Width and obliquing are applied to glyph cross-flow geometry rather than multiplying vertical advance a second time. Inline styles, colors, tracking, stacks, rules, paragraph breaks, indents, spacing and tabs retain the shared bounded composition pipeline. Paragraph flow runs downwards and columns progress leftwards; the supplied rectangle-width limit is interpreted as maximum advance along a vertical column. Attachment names are mapped to flow/cross-flow coordinates. This explicit consumer convention is not certification of every proprietary vertical-MTEXT layout rule.

The provider emits neutral presentation-only stroke groups and regions; both Skia and SVG consume identical geometry, and topology remains unaffected. Entity OCS, rotation, width and oblique axes are applied after local layout. Vertical linetype text uses the same resource provider. Live catalog replacement invalidates resource-dependent projections; pinned font sets preserve prior output.

A selected font must declare vertical support and have supported Unicode/legacy metrics. Missing glyphs produce explicit boxes and diagnostics. Upward or sideways final glyph advances and unbalanced cumulative vector scaling are refused instead of restarting state incorrectly at formatting boundaries. Such unconventional stateful fonts require a specialized provider. BIGFONT mapping is documented in [the companion guide](BigFont.md); this feature does not guess code pages or silently substitute a horizontal font.

Character, measurement-work, glyph-instruction and curve quotas remain enforced. A failed projection does not publish partial geometry. No source filename grants filesystem access and no font files are bundled. The test definitions are original in-memory instruction examples.

Reference: Autodesk Special Codes Reference, https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Customization/files/GUID-06832147-16BE-4A66-A6D0-3ADF98DC8228.htm .
