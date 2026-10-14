# Immutable viewport layer overrides

`RenderCompileOptions.ViewportOverrides` accepts a document-bound `ViewportOverrideCatalog`. Each viewport maps to an immutable `ViewportLayerProfile`, whose keys are source layer identities. Supported values are 24-bit RGB, lineweight in millimeters, transparency in percent, an existing explicit linetype identity, viewport freeze/thaw, and plot visibility. Null fields inherit drawing values.

```csharp
var profile = new ViewportLayerProfile(new[]
{
    new KeyValuePair<ObjectId, ViewportLayerOverride>(pipeLayerId,
        new(Rgb: 0xff8800, LineweightMillimeters: 0.5, TransparencyPercent: 20,
            Linetype: dashedLinetypeId))
});
var catalog = new ViewportOverrideCatalog(snapshot.DocumentId,
    new[] { new KeyValuePair<ObjectId, ViewportLayerProfile>(viewportId, profile) });
var paper = compiler.Compile(snapshot, new("Sheet") { ViewportOverrides = catalog });
```

## Inheritance and compilation order

A private immutable rendering view replaces only selected layer properties, before normal scene expansion. The original source snapshot, document revision, editor and history are never mutated or returned as an edited state. All unchanged nodes remain shared. Existing ByLayer, ByBlock, layer-zero insert inheritance and dimension component resolution therefore run normally. Explicit entity properties are not recolored or replaced after the fact.

Linetype replacement happens before glyph generation, fitting, viewport compensation and clipping. Both native and SHX decorations use the selected pattern; explicit entity linetypes remain authoritative. Model-tab compilation ignores this paper-only context. Paper model compilation still disables model annotation scaling to avoid multiplying it into PSLTSCALE.

Global invisibility and freezing remain authoritative. `Frozen=false` removes a viewport-local frozen-layer entry, but cannot thaw a globally frozen/off layer. Freezing an insertion layer suppresses its descendants through existing scene visibility inheritance.

`ForPlot=true` applies source/overridden layer Plot flags to projected model content. It is false by default because a nonplotting layer remains visible in ordinary screen display. Paper annotations are not filtered by this model-layer context. CTB/STB plot-style evaluation, visual-style shading and producer-specific extension-dictionary decoding are not provided by this API; hosts can construct profiles from an authoritative decoder. Profiles are view configuration, not a source DXF persistence format.

## Immutable identity and limits

Catalogs reject foreign documents and unknown viewport/layer/linetype identities. Inherited ByLayer/ByBlock linetypes cannot be assigned to layer profiles. Input collections are copied; profiles compare their ordered-independent contents structurally. Equivalent scale/profile pairs share model compilation. `PaperSpaceOptions.MaximumPatternScales` now bounds distinct combinations of pattern scale and override profile, preventing each new appearance from bypassing the existing multiplicative-work quota. The complete set is preflighted before model resource providers run.

Profile construction defaults to 4,096 layers. Catalog construction defaults to 256 profiles and 65,536 total layer entries; callers can provide explicit finite limits. Plotting layer scans use the catalog's entry quota. Weak snapshot-keyed view caches retain at most 32 profile views per source snapshot. Existing render, source, geometry and resource budgets still apply. These are bounded operation counts, not native-byte or wall-clock guarantees.

Generated items keep the original entity, insert path, source-to-world and viewport editing transforms. Picking, translation and undo operate on the original source. Graph analysis uses the untouched source snapshot. Retained render frames preserve old source/resource/configuration identities; a new source snapshot or catalog invalidates the relevant render result.

## Verification

The advanced suite includes explicit-property precedence, ByBlock/layer-zero inheritance, independent/equivalent viewport profiles, complex-symbol regeneration, native freeze/thaw interaction, plot visibility, source and history isolation, retained-frame invalidation, source-identified editing, malformed inputs and budgets, cancellation, and real Skia/SVG output. No existing gate or workflow is changed.

The inheritance rules follow Autodesk's [viewport layer override documentation](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-LT/files/GUID-C96B67FA-B992-4C49-BB72-FB3E44FFB6BC.htm). The host-profile API deliberately does not pretend to decode arbitrary private layer records or evaluate external plot-style files.
