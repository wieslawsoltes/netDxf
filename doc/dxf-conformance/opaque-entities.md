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

`ACAD_PROXY_ENTITY` remains unsupported. Its class ordinal, generated CLASS behavior and independently framed embedded references need separate qualification. No supplied native standalone unknown or proxy fixture was found. The [historical assessment](opaque-entity-assessment.md) records the initial evidence and primary schema references. The [final source inventory](opaque-entity-source-inventory-final.json), repeated after rebasing onto final PR93, scans 412 files (408 parse; four previously documented truncated SUNSTUDY failed saves do not). Its only unsupported dispatch names are child records: 2,131 VERTEX, 172 SEQEND and 40 ATTRIB occurrences. Counts include related carrier copies and are not independent native drawings.

## Common edits and lifecycle

Layer, linetype, ACI/true color, lineweight, linetype scale, visibility, transparency, color name, shadow mode, common proxy-cache bytes and XData use their existing public entity APIs. Edits overlay only qualified common scopes or the XData tail. Untouched fields retain their original typed values and ordering, including duplicate private codes, unknown common tags, private strings and binary data. The original `SourceTags` snapshot never changes. Color name requires R2004+ and shadow mode R2007+, consistent with the existing writer. Proxy-cache bytes are storage only and do not establish rendered geometry.

Known extension-dictionary and reactor metadata can be loaded and referenced. Direct changes are refused during preflight. The qualified HATCH source-backlink release described below is the supported lifecycle mapping. Layout reassignment or changing a stored layout-name binding is also unsupported. Current common resources and known links must remain registered.

Every nonidentity transform, entity clone, containing-block clone, block extraction/export and insert explosion containing an opaque entity rejects. Exact identity transforms are no-ops. Source-document, source-owner and source-profile bindings prevent foreign adoption or owner changes. Known incoming links guard entity, dependency and ancestor removal. Explicitly removing an otherwise unreferenced opaque entity retires it permanently; its source snapshot remains readable, and reattachment rejects. None of these constraints resolves hidden application-private dependencies.

## Loaded HATCH source associations

A HATCH source list may bind to an actual eligible `DxfOpaqueEntity` in the same block. The HATCH retains its own stored boundary edges; this association does not project or interpret the unknown geometry. Initial binding preserves duplicate source occurrences and the exact source identity.

`UnLinkBoundary`, source-path removal, HATCH removal, boundary recreation and supported HATCH transforms release the affected managed source occurrences. When the final occurrence for a particular HATCH leaves, output omits only that actual HATCH target from the qualified common `{ACAD_REACTORS}` group. A balanced empty group remains valid. The original `SourceTags` stays unchanged, and `References` stops reporting the released backlink. Standard pointers elsewhere in the packet (including a flat private-subclass group 340), XData references and unknown application groups remain unchanged. A private standard pointer to that same HATCH still prevents its removal.

Each release validates the current source state before HATCH or collection mutation; directly changing persistent reactors or extension metadata cannot be authorized by a later unlink. Partial path removal updates duplicate occurrence counts while preserving any remaining HATCH backlink. Explicit unlink retains source entities; the existing last-path removal behavior may retire an otherwise unreferenced source. A removed path cannot be reattached with its resolved unknown source, and `HatchBoundaryPath.Update` rejects unknown geometry before clearing stored edges. HATCH cloning copies its stored edges and detaches association without cloning or changing the opaque source. Containing-block removal remains subject to existing source-owner and dependency guards and can refuse without mutation.

Qualification uses all six profiles and both transports of the pinned native and ezdxf HATCH relation carriers, with an explicitly declared change to one source entity name plus private-group/pointer controls. These are adapted schema cases, not native unknown-entity evidence. The independent operation harness compares the same assertions against frozen before/after libraries, including duplicate sources, collection insertion/replacement/clearing and direct dirty metadata. See the [HATCH interop receipt](opaque-hatch-release-qualification.json).

## Admission limits and save preflight

The reader and writer allow at most 65,536 tags per retained entity, including group 0, and 1,048,576 body tags across retained entities, excluding their group-0 markers. Balanced private application groups may nest at most 32 levels. Common proxy-cache data is limited to 16 MiB. Writer limits apply to the actual resulting packet after mutable common and XData edits.

Opaque-specific validation runs before writer initialization and output. It also runs before the file overload opens its destination or changes the document name and working folder. Failed opaque preflight preserves existing stream bytes/position and existing destination files; this does not promise transactional behavior for unrelated legacy writer failures. ASCII comments (999) are preserved for ASCII output and explicitly reject binary output. Embedded NUL characters reject both transports; retained CR/LF characters reject ASCII output. CLASS text bound to an opaque entity uses literal-backslash-safe encoding and the same text preflight. A binary chunk too large for binary framing rejects before output.

The read-only target-version report emits `STORED_SOURCE_PROFILE` for this type when the requested profile differs. Retired entities are omitted from fresh analysis. The report does not analyse uninterpreted subclass contents or certify that a save is otherwise valid.

## Qualification scope

The declared fixture is a netDxf-authored scaffold with an explicitly specified `QUALIFIED_FUTURE_CURVE` packet. It is not a native CAD or private application sample. It tests all six supported profiles and both transports, complete unchanged packet equality, scoped common edits, exact source references, CLASS pinning, framing controls, retirement and pre-output refusal. The mandatory `tools/verify_opaque_entities.py` gate independently decodes complete source/output packets and rejects deliberately corrupted controls.

The [qualification receipt](opaque-entity-qualification.json) records 528 passing focused cases in each of Debug and Release, with identical case-result records. The physical gate checks 24 whole source/output packet pairs and 24 literal CLASS outputs per configuration, then rejects 16 corrupted controls. It includes model, paper and unused-block placement; exact source dependencies; source and mutable XData layer bindings; per-packet and aggregate tag boundaries; transport restrictions; and stream/file refusal followed by repair and retry. A separate ten-case existing source-identity regression also passes.

The [independent review](receipts/opaque-entity-independent/README.md) passes 91 cases in each configuration and preserves before/after proofs of the two defects found during review. Its 114 main output audits report zero errors or repairs in ezdxf 1.4.4, which retains this unknown type as `DXFTagStorage`. The audits check surrounding DXF structure and do not interpret the entity. Shared declared-fixture factory provenance is explicit in that review.

All five supported library target frameworks build in Release with the existing 561 CS1591 warnings per framework and no new warning codes or errors. The receipt distinguishes the exact runtime-qualified library hashes from a later build's regenerated source-revision metadata; the production source subtree is the same. The final packet archives include original input/output bytes and per-file hashes.

Unknown entity rendering, regeneration, private geometry semantics, proxy conversion, native CAD execution and exhaustive DXF legality remain outside the evidence.
