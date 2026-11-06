# Compatibility and support boundaries

## Choose the document model

| API | Admitted DXF families | Use it for |
|---|---|---|
| `DxfDocument` | AutoCAD 2000, 2004, 2007, 2010, 2013, 2018 | Typed entities, resources and supported object semantics |
| `DxfRawDocument` | R11/R12, R13, R14 and the six modern families above | Ordered records, unfamiliar data and explicitly supported raw edits |
| `DxfR12Codec` / `DxfR12SelectionPlan` | R11/R12 selection interchange; selection plans can emit R12 or the six modern families | A bounded selection of supported entities and reachable blocks |

Text and binary transports have separate framing and encoding rules. Raw
preservation does not provide a typed R13/R14 model. Changing a version string
does not convert the schemas of retained records. See [IO](io.md) for byte
preservation and transport conversion.

The core NuGet package targets `netstandard2.0`, `net471`, `net48`, `net6.0` and
`net8.0`. Package target frameworks describe runtime compatibility; they are
independent of the DXF format families in the table.

## Check a target before saving

`document.AnalyzeVersionCompatibility(targetVersion)` provides a read-only
snapshot of known typed-writer restrictions and omissions. It does not change
the document version, allocate handles or perform a conversion.

```csharp
var report = document.AnalyzeVersionCompatibility(DxfVersion.AutoCad2007);
foreach (var diagnostic in report.Diagnostics)
    Console.WriteLine($"{diagnostic.Kind}: {diagnostic.PropertyPath}: {diagnostic.Message}");
```

`HasKnownRejections` and `HasKnownLosses` describe the rules covered by the
report. There is no `CanSave` guarantee: invalid geometry, resource graphs,
encoding or unsupported packets can still reject a save. Captured diagnostic
fields do not update when their live `SourceObject` changes; analyze again after
editing. Source-bound stored objects can require their original format even
when the requested change looks like an upgrade.

The current rules are in
[version analysis](../../netDxf/DxfDocument.VersionCompatibility.cs) and exercised
by [version regression tests](../../tests/netDxf.Conformance/VersionCompatibilityTests.cs)
and the [independent output check](../../tools/verify_version_compatibility.py).

## Selection interchange and limits

`DxfR12SelectionPlan.Prepare(entities)` captures an immutable normalized selection.
`CreateDocument(version)` creates an independent editable graph; `Save` stages
bounded output before touching the destination stream. This is a selection API:
unrelated document headers, objects, layouts, original handles and lexical
formatting are not preserved.

The supported profile requires unitless, acyclic blocks. Xrefs, unsupported
entities or metadata, distinct same-named source blocks and modern-only attribute
fields are rejected. Only the explicitly supported neutral DesignCenter unit
packet is projected; arbitrary application data is not silently discarded.
Consult the [selection-plan API](../../netDxf/IO/DxfR12SelectionPlan.cs) for the
exact admission, cancellation and resource-limit contract.

Opaque records, proxy graphics and ACIS payloads may be retained without being
decoded or evaluated. Stored FIELD/TABLE data is not a universal native formula
or regeneration engine. Native appearance, private application semantics and
external-resource behavior need their own qualified providers. See
[editing](editing.md), [geometry](geometry.md) and [FIELD/TABLE](tables-and-fields.md)
for those boundaries; old per-feature assessments are available in [history](history.md).
