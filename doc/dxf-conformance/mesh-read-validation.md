# Counted MESH input validation

Baseline: `41f2cccdc11b2fc53198f303fb4327979849321c`, after merged PR #37. Audit date: 13 September 2026.

## Corrected behavior

MESH input previously reserved vertex/edge arrays from untrusted counts, allocated face arrays before consuming their contents, and accepted same-typed values under incorrect group codes. Face sizes could overrun group 93; negative or out-of-range topology indices survived; invalid subdivision levels silently became zero. Valid group 999 comments within counted lists broke parsing.

Validate expected vertex coordinates (10/20/30), face and edge items (90), and crease values (140). Track face-list capacity with a subtraction-based remaining-item counter. Grow collections only when real tags arrive; do not preallocate from declared counts. Reject negative counts, polygons shorter than three indices, out-of-range indices, unmatched crease counts and subdivision levels outside the model's 0–255 range with MESH/group/position diagnostics. Skip comments only in semantic MESH parsing; raw tags remain available unchanged.

An empty list is represented by a zero count, not a Debug.Assert failure. All-zero geometry lists can round-trip through the model; this is a data-model contract, not a claim that every CAD consumer renders or accepts empty meshes. The minimum polygon length and subdivision limit are explicit typed-model policies. Geometric degeneracy, duplicate vertices, manifoldness, orientation and watertightness are not evaluated.

## Versions and boundaries

| Behavior | 2000 / AC1015 | 2004 / AC1018 | 2007 / AC1021 | 2010 / AC1024 | 2013 / AC1027 | 2018 / AC1032 |
|---|---|---|---|---|---|---|
| Typed MESH export | Rejected by #36 | Rejected by #36 | Rejected by #36 | Existing | Existing | Existing |
| Counted input, comments, topology indices | Shared reader; no new legality claim | Same | Same | Tested text/binary | Tested text/binary | Tested text/binary |
| Independent nonempty output audit | Not emitted | Not emitted | Not emitted | Both transports | Both transports | Both transports |

The typed reader still tolerates encountered MESH records without proving historical legality. This change does not add pre-2010 MESH export. Subentity overrides, repeated core-count records, arbitrary unsupported fields, complete schema validation, global resource budgets and subdivision evaluation remain separate work. Actual long input still uses memory proportional to its consumed data; preventing forged-count preallocation is not a universal memory quota.

Public Load retains its current Debug exception / Release null convention; caller-owned streams remain open. Physical EOF remains an EndOfStreamException from the underlying codec. No writer topology validation or file-save transaction is introduced here.

## Executed evidence

The initial 87 added cases against unchanged production gave **5,809 passed / 63 failed in Release**, and **5,791 passed / 81 failed in Debug**. Debug additionally requires contextual InvalidDataException rather than incidental argument exceptions. The corrected parser passes that same set.

Twelve further cases exercise empty lists and forged `int.MaxValue` counts, including a nearly maximum face length; each short malformed fixture must allocate less than 4 MiB on the test thread. These allocation probes were run only after fixing the preallocation path, not claimed as safe old-code executions.

Final 99 added cases: **5,884 passed / 0 failed**, local signed production assembly, .NET 8, Debug and Release. Six saved nonempty fixtures pass the included ezdxf 1.4.4 topology/value/XData checks and audit with zero errors and zero repairs. Final-head Linux/Windows Debug/Release, netstandard2.0 compilation and source inventory remain merge gates. No AutoCAD process or full-standard certification is claimed.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_mesh_read_validation.py artifacts/conformance
```

## Primary reference

Autodesk defines the counted MESH vertex, face, edge and crease fields here:
https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm
