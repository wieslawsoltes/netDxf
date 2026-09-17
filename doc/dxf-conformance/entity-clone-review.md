# Authored entity clone fidelity review

## Scope and corrected behavior

This C# review starts at merged PR #106, commit
`cd2b4b2d0949eaa85ff517b438a1838f8fb25ffb`, tree
`68dd970d70c3d79c8759aef02e4c9f492f2a8f06`.
The separate JavaScript port and its pinned C# reference are unchanged.

| Entity | Existing defect | Correction |
| --- | --- | --- |
| SOLID | `Clone()` reset OCS elevation to zero | Copy the stored elevation with the four original corners and thickness |
| TRACE | `Clone()` reset OCS elevation to zero | Copy the stored elevation with the four original corners and thickness |
| SHAPE | `Clone()` reset relative X scale to one | Copy `WidthFactor`, including authored negative values |
| Authored 3D POLYLINE | Flags survived but the smoothing discriminator reset | Copy the private smoothing value without replaying its flag-changing setter |
| LEADER | Horizontal direction reset; mutable line color was shared | Copy exact direction components and deep-clone the line color |

These are omissions in existing public `Clone()` methods, not new entity models.
The fixes apply to direct copies and to the existing recursive BLOCK/INSERT
clone path. Public signatures, writer schemas and source-bound clone admission
rules do not change. The existing LEADER annotation and style-override clones
remain independently owned, with annotation reactors pointing to the new leader.
A copied normalized direction is not normalized again: doing so can change its
binary64 components even when its geometric direction is unchanged.

```csharp
var original = new Solid(new Vector2(1, 2), new Vector2(5, 3),
                         new Vector2(2, 8), new Vector2(7, 9))
{
    Elevation = 13.75,
    Thickness = -2.5
};
var copy = (Solid)original.Clone(); // Elevation remains 13.75.

var leader = new Leader(new[] { Vector2.Zero, Vector2.UnitX })
{
    Direction = new Vector2(3, 4),
    LineColor = new AciColor(17, 83, 149)
};
var leaderCopy = (Leader)leader.Clone();
leaderCopy.LineColor.Index = 2; // Does not recolor the original leader.
```

## Wire evidence and independent expectations

Autodesk's [SOLID reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E0C5F04E-D0C5-48F5-AC09-32733E8848F2.htm)
defines its four OCS vertices. [SHAPE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-0988D755-9AAB-4D6C-8E26-EC636F507F2C.htm)
defines group 41 as the relative X scale. [POLYLINE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm)
distinguishes its bit-coded flags from group 75's smoothing discriminator.
[LEADER](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-396B2369-F89F-47D7-8223-8B7FB794F9F3.htm)
defines the horizontal direction vector and BYBLOCK fallback color.
These references describe storage; they are not evidence of native execution.

The development-only checker uses ezdxf 1.4.4, not netDxf's parser. It requires
exactly **540 source/clone pairs (1,080 files)**: five entity kinds, three
nondefault variants, three nesting depths, six modern writer profiles and two
transports. Source and copy are saved in separate documents so registration of
a same-named block cannot accidentally replace the copy with the original.

It independently checks the expected OCS corner elevations, shape width,
polyline flags/smoothing/control vertices, and leader WCS direction/vertices
using ezdxf's OCS implementation. Both source and clone must satisfy those
expectations: identical corruption in both cannot pass merely by comparison.
All ordered physical ENTITIES/BLOCKS tags are also compared, excluding only
identity groups 5 and 330. Every other retained tag is challenged through the
same comparator. Common appearance, XData, and the actual length/data proxy
packet are checked; simple ezdxf entity loaders can omit proxy projection, so
its absence in their typed model is not mistaken for loss on the wire.

The field checks use relative and absolute tolerances of `1e-13` for independent
floating-point calculations. The complete decoded packet comparison is exact.
All output drawings undergo ezdxf graph audit with zero errors and zero repairs.
HEADER timestamps/seed, TABLES, OBJECTS and CLASSES are not included in the
ordered ENTITIES/BLOCKS comparator; the graph audit does not certify their
complete native semantics. General spline-fit algorithm correctness is not
claimed by preserving the existing control/fit packets.

## Reproduction and qualification

```sh
dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Debug
dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Release
python tools/verify_entity_clone_review.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

The new module registers **646 conformance cases**. Running those unchanged cases
against the merged baseline produced **54 passes and 592 failures**. After the
five production corrections, all **646 pass**. This includes 540 wire cases,
90 detached/registered model cases, three annotated leaders, one 512-direction
exact-bit case, and twelve retained-source guard cases. The 512 operations are
not misreported as 512 harness cases.

The independently executed new gate checks all 1,080 files and rejects
**109,944 ordered-record corruptions plus 10,584 property corruptions**. Exact
full-suite and remote CI receipts belong to the associated PR; a previous
baseline run is not substituted for execution of the changed source.

## Interrupted-work recovery

The source archive from PR #106 was SHA-256 verified and reconstructed all 3,135
tracked files with the exact base Git tree, including ignored DXF fixtures.
A separately uploaded gzip Git blob,
`f8ad428238fd059a4cee453883621543256a6f47`, was corrupt. Its SHA-256 is
`742176a37dfc61f87e1f9209c270731a7f63ccca71dd1780eb13d6e7c95fd0d1`.
Decompression failed with an invalid back-reference; only 2,655 unverified
prefix bytes could be read. That prefix identified the clone review, but was
not applied as a valid patch. Each correction was re-established from the
verified baseline and tested. No claim is made to have recovered the complete
interrupted implementation. Recovery/transport workflows remain separate from
the implementation branch and normal conformance permissions remain read-only.

## Remaining boundaries

The typed writer tests cover R2000, R2004, R2007, R2010, R2013 and R2018. They do
not add historical R11/R12/R13/R14 typed authoring or universal version
conversion. The existing source-bound POLYLINE and block clone rejections for
retained external/private dependencies remain tested and unchanged.

SHAPE's authored model and emitted width are tested without distributing or
inventing an installed SHX font. Typed SHAPE reload needs that external shape
definition, and native glyph rendering/placement is not qualified here. Native
AutoCAD open/AUDIT/save/reopen, all-entity clone equivalence, recursive
resource-complete import, private TABLE/cache regeneration and universal DXF
parity remain separate work. The historical aggregate conformance matrix is
not relabelled as a current all-feature certificate.
