# DXF field evidence generator

```sh
dotnet run --project tools/netDxf.FieldAudit -- --self-test
dotnet run --project tools/netDxf.FieldAudit -- . artifacts/field-audit
```

Produces `field-inventory.json` and `field-inventory.md` from the actual C# syntax trees. The JSON includes direct group-code switch cases and case bodies, literal/dynamic write expressions, version conditions, record dispatch, helper calls, declared public properties, source locations and file hashes. The Markdown links methods to their source revision.

This is **syntax evidence, not conformance measurement**. A code can be consumed but ignored, constant-written, handled transitively, or illegal in a target release. The tool does not infer those semantics. It analyzes the DEBUG/TRACE/NET8_0/NETCOREAPP preprocessor configuration; other configurations require separate review. Generated source can add a record name without implementing its behavior. A supplied revision label does not prove a clean worktree; the file hashes identify the actual input.

The built-in self-test covers unrelated nested switches, comments and strings containing code-like text, dynamic code expressions, version gates, dispatch, helper extraction, property visibility and hashes. The pinned Roslyn 4.11 dependency is confined to this development tool; no production library dependency or target framework changes.

The Linux Debug conformance job regenerates and retains the inventory with its exact source archive. The published human-reviewed version matrix is in `doc/dxf-conformance/version-feature-matrix.md`.
