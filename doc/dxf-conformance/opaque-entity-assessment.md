# Proposed standalone opaque entity storage

Status: historical scope assessment. The implemented contract and qualification limits are documented in [opaque-entities.md](opaque-entities.md). The source baseline is local commit `30acdc023e678b8426ba386a4041d5fc581d0560`, published with the same tree at `c30657b949220537e5cc05eba46ba4cfee8c7787`. This work is separate from the completed target-version report increment.

The proposed feature retains an eligible unknown entity as a real member of its source block with its original physical identity. Its complete source tag sequence is immutable. Documented common appearance fields remain editable through `EntityObject`. No geometric interpretation, hidden application dependency resolution, authoring, or proxy conversion is implied.

## Current loss path and evidence

`DxfReader.ReadEntity` obtains `CurrentSourceRecord`, parses and normalizes common entity data, then its default dispatch calls `ReadUnknowData` and returns null. The fallback therefore has to branch before common parsing. Falling back after a recognized entity parser fails would conceal invalid supported data and is excluded.

The checked-in inventory scans 386 fixture DXFs plus the two historical `TestDxfDocument` samples. Of 388 files, 384 parse with the independent ezdxf 1.4.4 tag readers; four are the already documented truncated SUNSTUDY binary failed saves. The only names outside ordinary entity dispatch are child records: 1,915 VERTEX, 148 SEQEND, and 40 ATTRIB occurrences. There are no supplied standalone unknown or proxy records in this corpus. These counts include related carrier copies and are inventory observations, not independent native drawings or qualification cases. Each file's stored and inflated SHA-256 appears in `opaque-entity-source-inventory.json`.

Reproduce from the repository root:

```sh
python tools/inventory_opaque_entity_sources.py --output /tmp/opaque-entity-source-inventory.json
```

Declared schema fixtures are consequently required for the first implementation. Existing `UNSUPPORTED_CURVE` identity tests are synthetic and must remain labeled as such. No native AutoCAD round-trip claim is available.

## Proposed public surface

| Surface | Contract |
| --- | --- |
| `DxfOpaqueEntity : EntityObject` | Sealed, load-only construction; original entity type remains `CodeName`. |
| `EntityType.OpaqueEntity` | Append a distinct enum value without renumbering existing values. |
| `DrawingEntities.OpaqueEntities` | Enumerate the loaded records in the active layout using existing collection conventions. |
| `SourceVersion` | Immutable DXF profile captured during loading. |
| `SourceHandle` | Immutable original nonzero physical identity, retained even after retirement. |
| `SourceTags` | Read-only ordered `DxfTag` snapshot including group 0, common header, subclasses, private groups, and trailing XData; binary values use defensive copies. This is typed tag equality, not original text whitespace or floating-point spelling. |
| `References` | Actual retained objects bound through context-qualified standard references; object references remain live, and private unclassified dependencies are not represented. |

Inherited layer, linetype, color, lineweight, visibility, scale, transparency, color name, shadow mode, common proxy-cache bytes, and XData remain editable. The source snapshot remains fixed after an edit. A dedicated writer must patch only changed common fields within their recognized scope and retain untouched tag order, duplicate private codes, strings, binary chunks, and unknown common tags. Known extension-dictionary/reactor metadata must either use the existing validated metadata editing path or explicitly reject unsupported changes before writing. No additional metadata edit is promised merely because a mutable base property exists.

`Normal` has no generic geometric meaning for an unknown record. The implemented getter keeps the existing `EntityObject` UnitZ placeholder because shared entity visitors read it; this is explicitly not a geometric projection. Assignment of any other value rejects. Exact identity transforms are no-ops; every other Matrix3 or Matrix4 transform rejects. Clone, containing-block clone, foreign adoption, owner changes, and profile changes reject before changing collections, identities, callback-visible state, or output.

## First accepted envelope

The envelope has exactly one nonzero common handle, one exact physical BLOCK_RECORD owner, one `AcDbEntity` frame, and at least one further subclass. Declared layer/linetype identities must resolve to accepted source records; a generated fallback cannot satisfy an explicit source name. Application groups must be balanced, and standard XData must occupy the tail. Repeated or invalid projected common singleton fields reject instead of being normalized silently. Unknown common fields remain stored.

Common entity codes distinguish the block owner and known metadata groups from application-defined group contents. Material and plot-style pointers are also common entity data. The implementation must respect these scopes while preserving their original tags. [Autodesk common entity codes](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm)

The first increment excludes:

- Existing recognized entity schemas, including malformed instances; their current readers remain authoritative.
- Structural names and orphan VERTEX, ATTRIB, and SEQEND records; child and block boundary subclass markers remain excluded even under an unfamiliar group-0 name.
- Child-follow group 66 and embedded-object group 101 outside application-private groups.
- Block-reference, dimension, modeler, and surface subclass bases whose additional graph or external storage requirements are not part of this envelope.
- `ACAD_PROXY_ENTITY` or an `AcDbProxyEntity` subclass, including a disguised proxy under another name.
- Drawings that combine a candidate unknown entity with nonempty ACDSDATA: the current typed reader discards that section and cannot establish whether it backs the entity.

Matching CLASS declarations require the existing qualified scalar schema, without discarded unknown or duplicate source fields. Their actual declaration identity and captured values must survive to preflight. This constraint requires recording class-source qualification during reading; it cannot be inferred solely from the already projected `DxfClass` object.

Proxy support is explicitly deferred. Proxy group 91 indexes source CLASSES order beginning at 500, and the proxy body contains independently framed graphics, entity data, and object IDs. Keeping the body tags alone cannot prove that the original application class remains selected. [Autodesk ACAD_PROXY_ENTITY schema](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-89A690F9-E859-4D57-89EA-750F3FB76C6B.htm) Current class writing preserves collection order but updates generated instance counts; current class reading also discards unknown fields. A later proxy increment needs explicit class framing and regeneration qualification.

## Identity and graph behavior

Bind explicit nonzero standard handles only through `GetObjectBySourceHandle`, including retained metadata identities. Missing, duplicated, ambiguous, or generated-only targets reject. Zero reference values remain zero. Arbitrary handles in groups 320–329 and values inside unknown application groups are never promoted to dependencies. Do not inspect binary data or infer named resources from private strings.

Owner edges need actual target ownership and cycle validation; ordinary pointer cycles are not owner cycles. Known incoming references guard entity removal, referenced-object removal, and ancestor removal. Explicitly removing an otherwise unreferenced opaque entity retires it and prevents later attachment. Same-document registration and source owner identity are checked again before Save. These guarantees cover the recognized graph; private application semantics remain unknown.

All save checks precede handle allocation, default layout/NOD creation, application-registry insertion, header mutation, and opening or writing output. The target-version report should emit `STORED_SOURCE_PROFILE` for a different target and omit retired records.

## Qualification matrix before shipping

| Area | Required evidence |
| --- | --- |
| Transport/profile | Six supported source profiles, ASCII and binary input, each valid output transport, complete typed source-tag equality on unchanged records. |
| Common edits | Each documented appearance field and XData edit produces only the expected scoped difference; identical private codes and unknown common tags survive; source snapshot stays fixed. |
| Source identity | Actual accepted owners, layers, linetypes, metadata, and standard handle targets; missing, generated, duplicate, null, and case-variant controls. |
| Graph lifecycle | Standard pointer versus owner cycles, incoming references, dependencies and ancestors, explicit retirement, same/foreign document and owner adoption. |
| Framing | Misleading type/subclass names, nested private groups, private handle-like fields, private 66/101 controls, unsupported aggregate and proxy shapes, malformed XData and duplicate common fields. |
| CLASS | Valid qualified declaration, replacement/removal/value mutation, unknown/duplicate source fields, and output declaration comparison. No proxy claim. |
| Atomicity | Failed clone, transform, adoption, and Save preserve streams/files, handle seeds, collections, header, NOD laziness, and callbacks; repair only the rejected state and verify retry. |
| Integration | Unknown entities before/after supported neighbors, block and model/paper ownership, metadata links, and diagnostics; malformed supported entities retain their existing rejection behavior. |

Use independent raw packet inspection for both transports and audit produced drawings with ezdxf where it accepts the declared records. Record any auditor's unknown-type omission explicitly. Native application rendering, regeneration, private geometry correctness, and exhaustive DXF legality remain outside the evidence.
