# Typed CLASSES definition retention

Baseline: `5ce3d631f7b64f5af9e20b61b9aedd357fb830d7`, after merged PR #23.

## API and fields

`DxfDocument.Classes` is an ordered `DxfClassCollection` of `DxfClass` definitions. This implements the documented CLASS fields: DXF name (1), C++ name (2), application name (3), raw proxy-capability flags (90), optional declared instance count (91), was-proxy flag (280), and entity/object classification (281). Definitions have no handle, owner, executable plugin loading, or connection to the DxfObject handle allocator.

DXF and C++ identities are immutable and independently unique under ordinal comparison. Collection insertion/replacement validates both identities before changing collection state. Other metadata is editable, and cloning is independent. Unknown proxy-mask bits are retained. Empty application names and absent instance counts are distinct from invented values. Invalid names/control characters, negative counts, non-boolean 280/281 values, duplicate identities, and malformed section boundaries are rejected. Unspecified flags default to zero; an unspecified application name defaults to empty.

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
var definition = new DxfClass("VENDOR_ENTITY", "AcDbVendorEntity", "Vendor plugin")
{
    ProxyFlags = 1023,
    WasProxy = true,
    IsEntity = true,
    InstanceCount = 0
};
document.Classes.Add(definition);
var copy = (DxfClass)definition.Clone();
copy.ApplicationName = "Independent copy";
document.Save("class-definitions.dxf");
```

## Version and output matrix

| Capability | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Read encountered documented CLASS fields | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Write names and flags | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Write supplied group 91 | Omitted by 2000 profile | Retained | Retained | Retained | Retained | Retained |
| Non-ASCII string output | Unicode escapes | Unicode escapes | UTF-8 | UTF-8 | UTF-8 | UTF-8 |
| Ordered custom/generated reconciliation | Tested | Tested | Tested | Tested | Tested | Tested |

The group-91 export boundary retains the existing raster-class writer profile and agrees with the independent ezdxf CLASS codec's DXF2004 gate. A count encountered in older input is retained in memory, but its derived metadata tag is omitted on a 2000 save. Null count remains absent in custom definitions. This is a documented normalization, not byte-identical cross-version preservation.

The writer works on cloned definitions. It retains custom ordering, adds missing raster declarations without duplicates, and rejects contradictory raster DXF/C++ identities or entity classification before writing stream bytes or adding layouts. Compatible input declarations retain their application/proxy metadata. Generated RASTERVARIABLES, IMAGEDEF, IMAGEDEF_REACTOR and IMAGE counts reflect current database records, including IMAGE entities in registered blocks rather than only the active layout. Multiple INSERTs of one block do not multiply the count of its stored IMAGE record. Unused input raster declarations remain present with zero instance counts. The old four hardcoded class writers are replaced by one typed writer.

## Evidence

206 new registered cases. New model/tests with the old document IO: **3,247 passed / 144 failed**. Integrated signed library: **3,391 passed / 0 failed**, Debug and Release, using local Roslyn/.NET 8 reference compilation. Tests cover independently assembled/reordered records, adversarial structural-looking strings, comments, optional defaults, all admitted versions/transports, exact field types/values, Unicode, clone/edit/removal, collection failure atomicity, generated raster reconciliation, block-instance counts, malformed records and nonempty-stream preflight. The entire final source tree is compared against the published branch before merge, in addition to Linux/Windows SDK, netstandard2.0 and source-audit CI.

Independent ezdxf 1.4.4 read/audit of all 12 retained `classes-*.dxf` fixtures agrees on names, flags, counts and explicitly decoded application strings, with **zero errors and zero repairs**. That audit does not validate whether declared custom counts match unsupported custom instances.

## Remaining scope

Retaining CLASS declarations does **not** retain, instantiate or evaluate the unknown custom ENTITY/OBJECT payloads they describe; generic unknown-record/dependency preservation remains separate work. Supplied counts for non-raster custom classes are metadata and are not recomputed by this feature; callers must not treat them as proof that those instances survived loading. Extra undocumented CLASS tags, original tag/comment positions, field-presence distinctions other than group 91, pre-2000 dialects, general resource quotas and AutoCAD-process validation are not implemented here. This supersedes the baseline matrix's discarded CLASSES entry for the documented fields only, not the whole preservation pipeline.

## Primary references

- Autodesk CLASS group-code definitions and unique names: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DBD5351C-E408-4CED-9336-3BD489179EF5.htm
- Independent ezdxf CLASS codec (`src/ezdxf/entities/dxfclass.py`, observed blob `89249a368f16689255ef4421168fa8fe40eaf783`): https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/dxfclass.py
