# Minimal modern DXF document initialization

Initialize omitted symbol-table collections before BLOCKS, ENTITIES, or OBJECTS needs them. Initialize omitted object collections and raster settings, then reconstruct the model/paper-space layout graph before deferred entities are attached. Already parsed collections retain their handles, contents and metadata; a real named-object dictionary takes precedence over fallback collections.

A missing layer-state-manager dictionary is now checked by `TryGetValue` rather than indexing and then checking for null. This matters when OBJECTS is empty but a LAYER table extension reference remains. The existing missing-BLOCK_RECORD fallback is no longer a fatal Debug assertion when TABLES is omitted.

## Tests and evidence

84 new registered cases cover all six admitted formats and both transports: header-only empty drawings, empty sections, implicit layer/application tables, empty OBJECTS, entirely omitted OBJECTS, and block definitions/inserts without TABLES. Fixtures include hand-authored semantic records as well as generated drawings with OBJECTS removed.

The old implementation produced **527 passed / 84 failed**. The corrected signed library produces **611 passed / 0 failed**, both Debug and Release. Tests verify initialization, ownership, model/paper-space geometry, nested block geometry, original entity and VIEW handles, UCS elevation, implicit layer defaults, XData, caller stream ownership, and save/reload of the recovered document. Final-head cross-platform CI and netstandard2.0 compilation are required before merge.

## Scope and recovery limits

The fixtures retain a supported `$ACADVER` and a valid `$HANDSEED`. Headerless/legacy-version inference and repair of invalid handle seeds or arbitrary dangling object references are not implemented here. Section order remains the existing supported order; this is not a general out-of-order-section parser.

When OBJECTS was not supplied, omitted layout names, plot settings and other absent object data cannot be recovered. The reader reconstructs layouts from surviving model/paper-space blocks using its existing fallback naming rules; it does not invent the original layout metadata. Unresolved IMAGE/UNDERLAY or other required semantic dependencies remain unsupported rather than being claimed recovered by collection initialization.

## Primary references

Autodesk permits omission of TABLES/BLOCKS when not required and implicit creation of referenced layers; its minimal example omits nongraphical object records:
https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-5D1DFE5C-94FC-43B7-B535-43001D1662C1.htm

Section and object roles:
https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-D939EA11-0CEC-4636-91A8-756640A031D3.htm
