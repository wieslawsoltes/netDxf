# Standalone unknown entity packet preservation

`DxfOpaqueEntity` retains an eligible unknown entity as an actual member of its original block. It preserves the entire original typed tag packet and exposes qualified common appearance edits through `EntityObject`. It does not interpret geometry or application-private dependencies. Construction is load-only; no opaque entity authoring API is supplied.

```csharp
foreach (DxfOpaqueEntity entity in document.Entities.OpaqueEntities)
{
    Console.WriteLine($"{entity.CodeName} {entity.SourceHandle} {entity.SourceVersion}");
    entity.Color = AciColor.Red;
    // SourceTags remains the original immutable packet after this appearance edit.
}
```

`DrawingEntities.OpaqueEntities` follows the active-layout convention. An entity in an unused block is available through that block's `Entities` collection. `EntityType.OpaqueEntity` is appended without changing existing enum values.

| Public member | Contract |
| --- | --- |
| `SourceVersion` | Captured original DXF profile. Saving under any other profile is unsupported. |
| `SourceHandle` | Captured nonzero physical identity, retained after explicit removal. |
| `SourceTags` | Immutable ordered `DxfTag` snapshot including group 0, common header, all subclasses and private groups, and trailing XData. Binary values use defensive copies. Preservation means typed tag values, not original whitespace, numeric spelling or file bytes. |
| `References` | Live actual objects reached through qualified standard links and current common resources or XData. Unknown private groups, arbitrary handles and binary contents are not interpreted. This is not a complete application dependency graph. |
| `Normal` | The existing base entity UnitZ placeholder, with no claimed relationship to stored geometry. Assigning UnitZ is harmless; any other assignment rejects. |

## Accepted records and source identity

The early reader branch captures the full record before ordinary common-field normalization. Existing recognized entity names retain their existing readers; a failure in a recognized reader never falls through to opaque storage. An eligible packet has one unique nonzero common handle, an exact accepted source BLOCK_RECORD owner, one `AcDbEntity` frame, an explicit actual source layer, and at least one private subclass. Explicit source linetype and APPID names must also resolve to accepted physical records. Common singleton values and known metadata frames are validated without interpreting private application groups.

Nonzero standard handle links bind to exact source objects. Missing, duplicate, ambiguous or generated-only identities reject. Null standard pointers remain null. Standard owner links require reciprocal actual ownership; ordinary self-pointers are allowed. Groups 320–329 and handle-like values inside unknown balanced group-102 application groups remain uninterpreted storage. Standard XData 1005 and 1003 retain their actual handle and layer dependencies.

The first scope excludes orphan VERTEX, ATTRIB and SEQEND records; structural names; child/block-boundary, block-reference, dimension, modeler, surface and proxy subclass bases; and group 66 child-follow or group 101 embedded-object framing outside private application groups. It also rejects an eligible unknown entity in a drawing with nonempty discarded ACDSDATA. These forms cannot safely be reduced to a standalone head record. Use the existing raw-document path when their untyped packet storage is required.

A matching CLASS declaration is optional. When present it must have the qualified scalar schema and identify an entity class. Its actual object identity and captured scalar values are pinned; removal, replacement or mutation rejects. Preflight also compares the prepared output declaration. A source R2000 CLASS carrying an explicit instance count cannot be saved by this path because the existing class writer would omit that field.

`ACAD_PROXY_ENTITY` remains unsupported. Its class ordinal, generated CLASS behavior and independently framed embedded references need separate qualification. No supplied native standalone unknown or proxy fixture was found in the checked-in corpus; the [source inventory](opaque-entity-assessment.md) records that evidence and the primary schema references.

## Common edits and lifecycle

Layer, linetype, ACI/true color, lineweight, linetype scale, visibility, transparency, color name, shadow mode, common proxy-cache bytes and XData use their existing public entity APIs. Edits overlay only qualified common scopes or the XData tail. Untouched fields retain their original typed values and ordering, including duplicate private codes, unknown common tags, private strings and binary data. The original `SourceTags` snapshot never changes. Color name requires R2004+ and shadow mode R2007+, consistent with the existing writer. Proxy-cache bytes are storage only and do not establish rendered geometry.

Known extension-dictionary and reactor metadata can be loaded and referenced, but changing it requires a separate complete mapping and is refused during preflight. Layout reassignment or changing a stored layout-name binding is also unsupported. Current common resources and known links must remain registered.

Every nonidentity transform, entity clone, containing-block clone, block extraction/export and insert explosion containing an opaque entity rejects. Exact identity transforms are no-ops. Source-document, source-owner and source-profile bindings prevent foreign adoption or owner changes. Known incoming links guard entity, dependency and ancestor removal. Explicitly removing an otherwise unreferenced opaque entity retires it permanently; its source snapshot remains readable, and reattachment rejects. None of these constraints resolves hidden application-private dependencies.

## Admission limits and save preflight

The reader and writer allow at most 65,536 tags per retained entity, including group 0, and 1,048,576 body tags across retained entities, excluding their group-0 markers. Balanced private application groups may nest at most 32 levels. Common proxy-cache data is limited to 16 MiB. Writer limits apply to the actual resulting packet after mutable common and XData edits.

Opaque-specific validation runs before writer initialization and output. It also runs before the file overload opens its destination or changes the document name and working folder. Failed opaque preflight preserves existing stream bytes/position and existing destination files; this does not promise transactional behavior for unrelated legacy writer failures. ASCII comments (999) are preserved for ASCII output and explicitly reject binary output. Retained strings containing NUL/CR/LF reject ASCII output. A binary chunk too large for binary framing rejects before output.

The read-only target-version report emits `STORED_SOURCE_PROFILE` for this type when the requested profile differs. Retired entities are omitted from fresh analysis. The report does not analyse uninterpreted subclass contents or certify that a save is otherwise valid.

## Qualification scope

The declared fixture is a netDxf-authored scaffold with an explicitly specified `QUALIFIED_FUTURE_CURVE` packet. It is not a native CAD or private application sample. It tests all six supported profiles and both transports, complete unchanged packet equality, scoped common edits, exact source references, CLASS pinning, framing controls, retirement and pre-output refusal. The mandatory `tools/verify_opaque_entities.py` gate independently decodes complete source/output packets and rejects deliberately corrupted controls.

The implementation checkpoint passes 434 focused Debug conformance cases. Release and independent qualification are recorded separately when complete; this checkpoint count is not a native acceptance claim. Unknown entity rendering, regeneration, private geometry semantics, proxy conversion and exhaustive DXF legality remain outside the evidence.
