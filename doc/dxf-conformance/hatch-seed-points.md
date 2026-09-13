# HATCH seed-point preservation and editing

Baseline: `5ea2a7fcbc75ffcae6821dea96cd401a625d4954`, after merged PR #40. Audit date: 13 September 2026.

## Corrected data loss and new API

The reader skipped group 98 and its repeated 10/20 coordinates, and the writer always substituted one seed at (0,0). Explicit empty seed lists, nonzero points, repeated points and their order were lost. Negative or incomplete counts were accepted without inspecting the declared list.

`Hatch.SeedPoints` now exposes an independently owned `IList<Vector2>`. It stores group-98 count and repeated group-10/20 coordinates in the hatch's object coordinate system (OCS), not pattern origin, world coordinates, or boundary vertices. The writer emits the actual count and coordinates. Read and untransformed round trips retain double bits, ordering and duplicates. Solid and gradient hatches use the same seed-point storage; their existing target-version export policy is unchanged.

A newly constructed hatch keeps the previous writer's one-zero-seed default. `Clear()` authors an explicit zero count. Loading an absent seed list yields an empty collection, serialized as a zero count: absence versus explicit zero is not retained in the typed API. The collection validates finite points on insertion and replacement, including non-generic IList access. It is mutable and does not promise concurrent mutation safety.

Cloning copies seed storage; block/INSERT cloning and INSERT explosion no longer lose it. TransformBy converts the old OCS point at the old elevation to world coordinates, applies the transform, and converts into the resulting hatch OCS. This follows the existing Hatch plane/normal transformation convention. Tests cover translations, rotations, uniform scaling, reflection and an in-plane nonuniform INSERT scale. This change does not repair every pre-existing arbitrary affine/singular transformation or pattern-origin issue.

```csharp
static Hatch CloneWithSeedPoints(Hatch hatch)
{
    var copy = (Hatch)hatch.Clone();
    copy.SeedPoints.Clear();
    copy.SeedPoints.Add(new Vector2(2.5, 3.75));
    copy.SeedPoints.Add(new Vector2(8.0, 4.0));
    copy.TransformBy(Matrix3.RotationZ(Math.PI / 2), new Vector3(10, 20, 0));
    return copy; // source collection and coordinates are unchanged
}
```

## Counted parser contract

Both outer HATCH parsing and the pattern-data parser recognize a seed list. Thus seeds can precede pattern style, follow pattern data, or follow XData. Comments between count/components are skipped. A second group-98 list, negative counts, missing components and wrong X/Y group codes are rejected with entity/field/group-position diagnostics. Physical truncation remains EndOfStreamException internally. Public Load keeps its existing Debug exception / Release null conventions.

The parser allocates incrementally from consumed data rather than using the declared count as an allocation size. An int.MaxValue count with a short body terminates at the first missing component. This does not impose a universal semantic-document memory budget or validate every unrelated HATCH group. Boundary extraction, flood/ray-casting evaluation, containment tests, automatic seed generation and seed membership in an island are not implemented. Pixel-size persistence and unrelated ACAD XData retention remain separate gaps.

## Per-version coverage

| Capability | 2000 AC1015 | 2004 AC1018 | 2007 AC1021 | 2010 AC1024 | 2013 AC1027 | 2018 AC1032 |
|---|---|---|---|---|---|---|
| Read/write seed count and OCS coordinates | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Empty/nonzero/repeated seeds and scoped ordering | Tested | Tested | Tested | Tested | Tested | Tested |
| Clone, edits, pattern/solid/gradient storage | Tested | Tested | Tested | Tested | Tested | Tested |
| Malformed counted fields and physical EOF | Tested | Tested | Tested | Tested | Tested | Tested |

This does not change typed admission for R12/R13/R14 or claim a first historical release. The separate raw pipeline already retains these ordinary ordered tags within its admitted profiles.

## Executed evidence

204 independent-byte cases against unchanged PR #40 production: **6,101 passed / 204 failed**, Debug and Release. The identical cases pass after the correction. Fifty additional API, validation, finite-value, absent-list, EOF, OCS and INSERT tests give **6,355 passed / 0 failed** in local signed-library Debug and Release runs. Fifteen documentation-integrity tests remain separate from this C# count.

The independent ezdxf 1.4.4 verifier loads twelve exported drawings, compares all three seed points' exact binary64 bits and order, checks boundaries/elevation and following XData, and reports zero audit errors and zero repairs. It does not execute AutoCAD or evaluate flood fill. The C# tests independently inspect emitted group 98 and the actual following 10/20 tags; the optional verifier checks the other library's stored seed list rather than its non-authoritative cached count accessor.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_seed_points.py artifacts/conformance
```

Final-head Linux/Windows Debug/Release, netstandard2.0 builds, documentation integrity and source audit are required before merge. No production dependency, strong-name identity or target-framework change is introduced.

## Primary reference

Autodesk HATCH group 98 and seed-point group 10/20 coordinates in OCS:
https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm
