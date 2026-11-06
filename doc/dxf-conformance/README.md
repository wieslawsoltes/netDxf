# DXF guide

Start here when choosing a netDxf API or checking its limits. Detailed signatures
and operation-specific constraints live in the library's XML API documentation;
the guides below cover the decisions that span several APIs.

| Task | Guide |
|---|---|
| Choose a format version, typed model or raw preservation | [Compatibility](compatibility.md) |
| Load and save files, preserve bytes, handle encodings and failures | [IO](io.md) |
| Edit objects, handles, ownership and retained records | [Editing](editing.md) |
| Transform geometry, edit curves, work with meshes and hatches | [Geometry](geometry.md) |
| Evaluate fields, calculate tables and generate table display geometry | [FIELD and TABLE](tables-and-fields.md) |
| Run tests and interpret CI evidence | [Verification](verification.md) |
| Find historical feature contracts, audits and review receipts | [History](history.md) |

**Full AutoCAD parity is not established.** Preserving a record, editing it,
evaluating its meaning and regenerating its display are separate capabilities.
An admitted DXF version or a passing test count does not imply complete support
for every entity, private schema or application behavior.

The [repository README](../../README.md) contains installation and a minimal
drawing example. The [Shadow guide](../../shadow/README.md) covers rendering,
editing services and Studio applications. The [CI/release guide](../CI-RELEASE.md)
describes package qualification and publication.

When behavior changes, update the relevant topic above and its regression tests.
Keep generated output in `artifacts/` and CI artifacts. Per-PR narratives,
execution logs and copies of old test results belong in PR history, rather than
new pages in this directory.
