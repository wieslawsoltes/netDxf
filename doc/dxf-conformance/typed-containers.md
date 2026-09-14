# Typed IDBUFFER, SORTENTSTABLE and SPATIAL_FILTER

`DxfDocument.Objects` exposes these three public OBJECTS schemas as registered
objects with the same ownership, handle lookup, XData, persistent-reactor and
extension-dictionary behavior as the named-object database. This is read, edit,
write and explicit graph-copy support. It does not perform native CAD drawing
regeneration or spatial clipping.

| Object | Public model | Qualified typed profiles |
| --- | --- | --- |
| IDBUFFER | `DxfIdBuffer.References` | 2000, 2004, 2007, 2010, 2013, 2018; text and binary |
| SORTENTSTABLE | `DxfSortentsTable.BlockRecord`, `Entries`, `DxfSortOrderEntry` | 2004, 2007, 2010, 2013, 2018; text and binary |
| SPATIAL_FILTER | `DxfSpatialFilter` | 2000, 2004, 2007, 2010, 2013, 2018; text and binary |

IDBUFFER references are ordered soft pointers. Duplicate entries retain their
identity; `null` represents handle `0`. References may identify graphical or
nongraphical objects. Adoption and editing reject objects from another document
or detached targets outside the adopted ownership graph. Removing a referenced
object later makes database validation fail; it does not silently alter the
sequence. Empty buffers are supported.

```csharp
var buffer = new DxfIdBuffer();
buffer.References.Add(line); // line is already in this document
buffer.References.Add(null);
buffer.References.Add(line);
document.NamedObjects.Add("APP_BUFFER", buffer);
```

SORTENTSTABLE is attached to the owning `BlockRecord` extension dictionary under
`ACAD_SORTENTS`. The helper creates that structure and, by default, sets the
regeneration bit (16) of the `$SORTENTS` header while retaining existing bits.
Passing `enableRegeneration: false` leaves header flags as supplied. The stored
associations preserve their exact order. Each entity occurs at most once and
must belong to that block. The hexadecimal sort key is an opaque ordering value,
not an object reference: zero is valid, keys can repeat, and a key can equal an
existing object's handle. Keys are neither remapped during cloning nor reserved
in the document handle allocator. The public value retains its supplied spelling;
normal DXF handle parsing canonicalizes spelling when loading a file.

```csharp
var table = document.Objects.CreateSortentsTable(line.Owner.Record, new[]
{
    new DxfSortOrderEntry(circle, "FFFFFFFFFFFFFFFF"),
    new DxfSortOrderEntry(line, "0")
});
// Edit this sequence to change the serialized associations.
table.Entries[0] = new DxfSortOrderEntry(circle, "A0");
```

The 2000 SORTENTSTABLE profile can be read for inspection, but typed export
requires promotion to 2004 or later. This conservative gate follows the native
reader problem explicitly recorded in ezdxf's implementation. A separate R2000
producer probe is retained for the inspection and rejection test; its existence
is not a native compatibility claim.

SPATIAL_FILTER exposes the ordered 2D OCS boundary, nonzero normal, origin,
clipping-enabled flag, optional front and back clipping distances, and both
stored affine transforms. Two boundary vertices specify opposite rectangle
corners; a longer sequence is the polygon boundary. `SetBoundary` copies and
validates the entire enumeration before changing state, and `Boundary` returns
an immutable snapshot. Numeric fields must be finite. Matrices use netDxf's
column-vector convention, with translation in `M14`, `M24`, `M34` and last row
`0,0,0,1`. Serialization converts that convention to the documented two 12-value
DXF matrices. The optional front distance is distinguished from the 24 matrix
values even though all use group 40.

```csharp
var filter = new DxfSpatialFilter
{
    Normal = Vector3.UnitZ,
    Origin = Vector3.Zero,
    FrontClippingDistance = 2.5,
    BackClippingDistance = null,
    InverseInsertTransform = Matrix4.Identity,
    ClipBoundaryTransform = Matrix4.Identity
};
filter.SetBoundary(new[] { new Vector2(-2, -1), new Vector2(7, 5) });
document.Objects.SetSpatialFilter(insert, filter);
```

`SetSpatialFilter` creates the required INSERT extension dictionary,
`ACAD_FILTER` dictionary and `SPATIAL` entry, and adds the filter's owner reactor.
An existing filter is edited in place rather than replaced by this helper.
The caller supplies the stored transforms; changing an INSERT does not recompute
its filter matrices. Polygon simplicity, winding and actual clipping results
are not evaluated. Private inverted-XCLIP records, roundtrip dictionaries,
SPATIAL_INDEX and other private filter schemas are outside this typed module.

`Objects.Clone` remaps IDBUFFER references with the same two-pass object graph
policy as XRECORD and XData references. References outside the copied ownership
subtree require explicit destination mappings across documents. Use
`Objects.CloneExtensionDictionary(sourceOwner, destinationOwner, mappings)` to
copy a complete extension dictionary. The destination owner must be registered
and have no extension dictionary. The owner mapping is automatic; other external
references still require mappings. A SORTENTSTABLE clone needs mappings for its
entities into the destination block. Copying an extension dictionary does not
change the destination document's `$SORTENTS` header flags. A SPATIAL_FILTER clone must end up attached
to an INSERT. Copies validate these placement constraints before registration,
preserve opaque sort keys, and remap reactors and XData group 1005. Generic
`EntityObject.Clone()` continues to have the separate scope documented in the
named-object database guide.

Save preflight validates references, placement, version and generated CLASS
identities before writing destination bytes. CLASS definitions use
`AcDbIdBuffer`, `AcDbSortentsTable` and `AcDbSpatialFilter`; their instance counts
are recomputed for 2004 and later, while the 2000 profile omits group 91. Loaded
CLASS metadata other than the recomputed count is preserved. Unsupported or
malformed fields inside these recognized public schemas are rejected instead of
silently discarded. The separate raw OBJECTS API remains the preservation path
for arbitrary private or malformed records.

## Qualification

`TypedContainerTests.cs` covers authoring and editing in both transports for all
six profiles, graph copies, external mappings, failed-copy atomicity, invalid
references and ownership, defensive boundaries, malformed counts/flags/matrices,
opaque sort keys, class preflight and the 2000 SORTENTSTABLE gate.

`tests/fixtures/typed-containers` contains six original ezdxf 1.4.4 producer
fixtures, their generator and a SHA-256 manifest. Each contains empty and ordered
IDBUFFERs, eight rectangle/polygon filter combinations with all front/back flags,
nonidentity inverse matrices, nonsymmetric boundary matrices, reactors and
XData. The five eligible profiles also contain draw-order associations with
repeated, zero and entity-equal opaque keys. They are original producer outputs;
they must not be normalized by loading and re-saving in ezdxf 1.4.4 because its
high-level SPATIAL_FILTER loader confuses the optional front distance with the
last repeated group-40 matrix value.

The conformance executable emits both transports for those external inputs.
`tools/verify_typed_container_inputs.py` independently compares ordered low-level
wire data, graph references, matrices, geometry, actual file versions, transport
signatures and zero-audit results. `tools/verify_typed_containers.py` checks the
separate files authored through the new public API. These are interoperability
and storage checks; no native AutoCAD rendering result is claimed.

## Primary references

- [Autodesk SPATIAL_FILTER DXF schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-34F179D8-2030-47E4-8D49-F87B6538A05A.htm)
- [ezdxf 1.4.4 IDBUFFER implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/idbuffer.py)
- [ezdxf 1.4.4 SORTENTSTABLE implementation and version note](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dxfobj.py)
- [ezdxf 1.4.4 SPATIAL_FILTER implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/spatial_filter.py)
- [ezdxf 1.4.4 filter ownership creation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/xclip.py)
