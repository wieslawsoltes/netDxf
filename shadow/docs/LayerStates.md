# Saved layer states and LAS transfer

Open **Layer states** from Home or Manage, or enter `LAYERSTATE` / `LAS`.
Save the current layer properties, rename/delete an existing state, restore
selected properties, or use **Edit saved properties** to change individual
saved entries without changing the drawing's live layers.

The saved-property editor supports on/off, freeze, lock, plotting, freeze in
new viewports, color, linetype, lineweight and transparency. Add an existing
drawing layer, remove a saved entry, or change the saved current layer.
Each accepted edit is one undoable transaction. Resource and layer identities
remain attached to the drawing's native layer-state manager.
New entries capture live drawing properties; save and reopen to edit them.
Pending additions can be removed before saving.

**Save restore mask** persists the selected property fields in DXF and LAS
group 91. This mask has different numeric values from the existing
`LayerStateFields`/`LayerPropertiesRestoreFlags` API flags; callers should use
`DraftingLayerStates.FieldsFromMask` and `MaskFromFields` for conversion.
Unsupported viewport/plot-style and unknown status bits are retained when
editing supported mask fields. “Restore current layer” and “Turn off unlisted
layers” remain explicit options for that restore operation.

## Transfer workflow

**Export LAS** downloads the selected saved state. **Import LAS** reads one
bounded UTF-8 file and prepares a preview. Choose a destination name and
explicitly enable overwrite when replacing an existing state.

Missing layers or linetypes are conflicts. Map them to existing resources,
or cancel, create/load the missing resources and preview again. Conflicting
many-to-one layer mappings and invalid current-layer assignments are refused.
The importer does not silently replace missing linetypes with Continuous.
Import saves properties; use Restore separately to change drawing layer flags.

Preview tokens bind the exact immutable drawing snapshot, workspace, view and
layout. They expire after five minutes and are consumed after a successful
import. Changes to the drawing or resources require a fresh preview. Import
executes as one undo entry and can be recorded/replayed in CAD workflows.
The host retains at most four pending imports.

## Reusable APIs

- Native `LayerState.ReadLas`, `FromLasText`, `WriteLas` and `ToLasText` provide
  a strict in-memory codec, limited to 2 MiB and 10,000 saved entries. Unknown
  or unsupported packet fields are rejected rather than silently dropped.
  Export rejects ByLayer transparency and inconsistent packed/effective
  transparency values that LAS cannot preserve.
  Stream APIs leave caller streams open. `SaveAtomic` remains the filesystem
  API for atomic replacement.
- `LayerState.RestoreMask` preserves native group 91 bits through cloning,
  LAS and text/binary DXF. Existing legacy load/import APIs remain available.
- `DraftingLayerStates.Details`, `Export`, `AnalyzeImport`, `PlanImport` and
  entry edit plans live in the reusable Drafting project.
- Studio Core supplies `CadLayerStatePreviewRequest` and
  `CadLayerStateImportRequest`; native HTTP and standalone WASM share
  `layer-state/details`, `layer-state/export`, `layer-state/preview` and
  `layer-state/import`. Semantic agent tools expose the same guarded routes.
- The shared web package contains the manager, saved-entry editor and transfer
  dialog. Hosts provide the drawing client, dialog and download callbacks.

Individual viewport overrides and plot-style payloads are outside this
increment. Native layer-filter dictionaries remain separate from the existing
browser-local filter/group UI. LAS import does not imply support for these
features.
