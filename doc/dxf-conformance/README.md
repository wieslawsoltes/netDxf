# DXF conformance work

Start with [the version and feature matrix](version-feature-matrix.md). It compares 113 feature rows across the six admitted DXF formats, distinguishes tested behavior from partial/lossy/missing paths, documents concrete group-code gaps and sets the remaining per-version implementation order.

The matrix is source-pinned and is not a full-standard certificate. Current implementation PR notes include [UCS table metadata](ucs-table-xdata.md) and [thumbnail preservation](thumbnail-image.md). The original baseline is [DXF-CONFORMANCE.md](../DXF-CONFORMANCE.md).

The [Roslyn field-audit tool](../../tools/netDxf.FieldAudit/README.md) regenerates exact syntax evidence in CI. Linux Debug artifacts contain `field-audit/field-inventory.json`, `field-audit/field-inventory.md`, regression reports, generated DXF fixtures and an exact source archive.

Every feature or defect requires its own scoped PR with regression proof, applicable version/transport tests, explicit downgrade/preservation semantics, and green CI before merge. Opaque preservation, semantic editing and independent AutoCAD validation remain separate claims.
