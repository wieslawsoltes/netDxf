# Native dimension picture export

Studio saves through the reusable `DxfDocument.SaveWithDimensionPictures` API.
The ordinary `DxfDocument.Save` methods keep their existing behavior. This
opt-in path closes an interoperability gap found by independently auditing the
CAD browser test's real downloaded DXF: its new DIMENSION entities had no
picture blocks, and the independent reader's audit removed them.

```csharp
using var output = new MemoryStream();
document.SaveWithDimensionPictures(output, isBinary: false,
    options: new DxfDimensionPictureOptions(maximumPictures: 10_000));
```

The exporter prepares detached copies of all eligible native dimension
families in their original block/layout owner context. It derives reference
points from current semantic properties, then uses `DimensionBlock` for picture
entities and the existing `DxfWriter.WriteDimension` schema for dimension tags.
It does not maintain an alternative dimension serializer or round-trip the
whole drawing through the typed reader.

Only the prepared dimension subclass ranges are replaced in the bounded raw
DXF tag snapshot. Other entity/common tags, ownership, resources and native
writer output remain in place. Both text and binary serialization use the
existing raw codec. This is not a byte-preserving edit of an original imported
file: the initial output is still produced by the normal typed netDxf writer.

Missing pictures receive generated anonymous blocks. Blocks created by this
API carry `NETDXF_DIMENSION_PICTURE` XData schema `1`; subsequent exports
regenerate those caches without accumulating another block per save. An
existing generated picture with other owners is rejected instead of modifying
another entity's cache. Unmarked imported/custom pictures are preserved, not
claimed to have been regenerated or checked for visual fidelity.

Temporary blocks or temporary generated contents of an existing owned cache
are restored in reverse order, including on a writer failure. Live dimension
geometry, manual text state, source block identity and original cache entity
instances are not normalized by the export. As with ordinary netDxf writing,
handle/resource-table housekeeping can change the live document. Callers must
serialize access and reconcile that housekeeping; this API is not a general
source transaction or an atomic file save. Studio does so through the existing
`EditorSession.SynchronizeSource` / `ShadowSession.MutateSource` save boundary.

The API throws consistently in Debug and Release, leaves the destination open,
and accepts cancellation and picture/entity/raw serialization quotas. Native
preparation/serialization is staged before final output. A final stream I/O
failure can leave a partial destination, so file applications should stage and
atomically replace the destination using their own storage policy.

Regression coverage includes all eight dimension families in text and binary,
source header derivation after edits, generated-cache refresh, repeated export,
imported-picture/XData preservation, block/paper context, quotas, cancellation,
I/O failure cleanup and Studio undo/redo across saves. The real-origin browser
CI test additionally audits the actual downloaded DXF with pinned ezdxf 1.4.4
and requires both dimensions to survive with **no errors and no repairs**.
