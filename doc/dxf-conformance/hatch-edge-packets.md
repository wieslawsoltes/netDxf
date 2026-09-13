# HATCH edge packets: progress, counted data and component validation

Baseline: `ad2ad829b804644223cd7bd8902d7d837be119c7`, after merged PR #48; source tree `8ef3dabb5001d31c9dc430d30ed669eb7a715bff`.

## Corrected failure modes

An unsupported edge-kind value entered a switch with no default while the outer loop neither advanced the input nor incremented its edge index. The reader could spin indefinitely on a tiny malformed HATCH. Other branches used positional reads without validating scalar group codes, narrowed an Int32 spline degree into Int16 without checking, and used untrusted knot/control/reference counts as allocation sizes.

Use a counted dispatch loop that requires group 72 and rejects unsupported kinds before advancing. Read line, arc and ellipse component packets with explicit group-code checks. Spline input validates the existing degree storage range, rational/periodic flags, knot/control counts and component tags. Source-reference counts and group-330 handles are checked after the edges. Unknown types, negative counts, missing/wrong components, noncanonical flags and surplus edge/reference data produce HATCH-edge/group-code/position diagnostics. Comments are skipped on each local advance, without changing raw comment access elsewhere.

Collections grow from consumed input, not declared counts. Spline weights remain optional with default one. The existing 2010+ fit/tangent packet is validated and consumed, but **fit points and tangents are still discarded by the typed model**. This change does not add their storage or change the earlier-profile policy. It does not validate NURBS knot monotonicity, knot/control/degree relations, manifoldness, radius/ratio domains, or closed-area topology. The positive Int16 degree range is the current model's storage policy, not a claim about all AutoCAD-supported degrees.

| Contract | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Line/arc/ellipse/spline primitive packets | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Unsupported-kind rejection and progress | Tested | Tested | Tested | Tested | Tested | Tested |
| Incremental knot/control/reference reads | Tested | Tested | Tested | Tested | Tested | Tested |
| Existing fit/tangent packet validation | Not consumed by existing profile | Not consumed by existing profile | Not consumed by existing profile | Tested, not stored | Tested, not stored | Tested, not stored |

Physical EOF retains the strict codec's EndOfStreamException. Public Load keeps Debug exceptions / Release null and leaves caller streams open. Normal writer output, target frameworks and signing are unchanged. This is one packet-grammar correction, not a complete HATCH schema or historical typed admission.

## Executed evidence

The stalled old reader was tested in **separate child processes with a two-second parent timeout** on the same tiny AC1032 fixture, edge kind 5. Both Debug and Release printed the entry marker then exceeded the deadline and were terminated by the parent. With the corrected reader, both returned within that bound: Release returned null; Debug raised contextual InvalidDataException. The relevant input tail is:

```text
 91
1
 92
0
 93
1
 72
5
 97
0
```

The full new regression suite was deliberately **not** run against the old parser: its unknown-kind cases would stall and its huge-count cases could allocate from attacker-controlled lengths. No full old-suite failing-case count is asserted. The bounded probe, source control flow and positive/negative packet tests are distinct evidence.

618 new registered C# cases yield **8,588 passed / 0 failed** with the corrected signed production assembly in local Debug and Release on .NET 8. Coverage includes all four supported primitive kinds, interleaved comments, 12 dispatch/reference failures, 24 component/flag/degree/list failures, sparse rational weights, existing modern fit/tangent grammar, physical truncation and forged Int32-max edge/knot/control/reference counts. Allocation probes require less than 4 MiB for their tiny inputs; this is not a universal memory quota for valid drawings.

The independent ezdxf 1.4.4 verifier checks all 48 exported fixtures (four edge kinds, six versions, two transports) for primitive values, seeds, elevation and following XData. These include open line/spline paths and are **primitive-decoding controls**, not an assertion of valid enclosed fill areas or a full drawing AUDIT. No native AutoCAD process was executed.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_edge_packets.py artifacts/conformance
```

Final-head Linux/Windows Debug/Release, netstandard2.0, generated-ledger integrity and source audit are required before merge. Remaining scopes include fit/tangent retention, outer path-list grammar, keyed scalar reordering, geometric validity, arbitrary-affine transforms, reference closure and native CAD qualification.

Primary source: Autodesk Boundary Path Data, including edge kinds, component codes, counts, optional weights and source references:
https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm
