# netDxf immutable shadow DOM

This additive solution separates a mutable DXF database from persistent, detached snapshots and downstream consumers. The existing netDxf library and its target frameworks are unchanged.

Projects are being implemented on `feature/immutable-shadow-dom`: core values/snapshots, a netDxf adapter, backend-neutral rendering, a SkiaSharp backend, graph analysis, an editor, and an integrated sample. No renderer-specific objects are stored in snapshots.

## Contracts

* A snapshot belongs to one document identity and monotonically increasing revision. Readers require no source lock. Every collection is immutable and every value is detached.
* Changes carry expected values and an expected revision. Stale edits fail, rather than silently overwriting another writer.
* The public object graph retains source types, references, scalar types, collection order, and unknown entity data. Semantic consumers may report unsupported features without deleting their source data.
* Block definitions are shared graph nodes. Instances and their inherited style/coordinate context are consumer projections, not copied source entities.
* Arbitrary direct mutations of netDxf require an explicit refresh: netDxf does not supply a complete property-change stream. The adapter is the single-writer boundary; callers must coordinate external source access.
* This is a public-object-model projection, not an independent DXF parser or a promise to recover information already discarded by netDxf.
