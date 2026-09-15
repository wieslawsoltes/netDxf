# Independent transparency wire qualification

`tools/verify_transparency.py` checks the actual artifacts emitted by
`TransparencyStoredTests` and `TransparencyBoundaryTests`. It imports no netDxf
assembly or test helper. ezdxf reads each DXF header and ASCII/binary tag stream;
a separate complete-pair parser reads standalone LAS files. The gate requires
the complete file inventory, the requested DXF version, and the stated transport.

Run it after the conformance executable has produced its artifact directory:

```sh
python tools/verify_transparency.py artifacts --output artifacts/transparency-independent.json
```

The required inventory contains 552 files across R2000, R2004, R2007, R2010,
R2013, and R2018, with both ASCII and binary DXF:

| Artifact family | Files | Independently checked behavior |
| --- | ---: | --- |
| `transparency-wire-*` | 108 DXF | Eight distinct packed values in LINE, MTEXT, and LAYER; each original and clone; initial export, reload/export, and explicit opaque edits |
| `transparency-ancillary-*` | 180 DXF | Absent, single, and duplicate 1071 slots; exact text, binary bytes, number, and order; unchanged clone; explicit zero, 25-percent, and whole-value assignment |
| `transparency-state-*` | 156 DXF | Exact FE, FF, unknown-prefix FF, and numeric-zero state fields; physical input; preserved output; normal-layer/state transfer conventions |
| `transparency-authored-*` | 12 DXF | Default layers retain absent transparency XData and authored opaque layer states retain numeric zero |
| `transparency-las-*` | 96 LAS | Exact stored values and field presence in original and resaved standalone layer-state files, including numeric zero |

The checks associate alpha values with the actual layer identity, entity position
and MTEXT text, or per-layer state packet. A matching multiset cannot hide a
value assigned to the wrong entity. Common state owner handles are excluded from
the state property scope, and every state property target must resolve to a
LAYER record. An ancillary packet is compared in full; the last 1071 edit rule
does not permit changing an earlier slot or dropping a private field.

Each accepted output also passes through corruption controls using the same
validator. Controls change actual parsed packed scalars, erase or duplicate their
tags, change one binary byte, alter ancillary text, remove an ancillary field,
or add a forbidden default/missing slot. Every alpha slot is additionally changed
individually, including each clone, each duplicate XData slot, and each nondefault
layer-state/LAS value. Truncating a real LAS output by one value line must fail the
same complete-pair parser. These are in-memory corruptions of parsed output;
the gate does not overwrite the conformance artifacts.

This qualification preserves the existing effective API. Generic packed numeric
zero retains its legacy ByBlock projection; physical layer-state numeric zero
retains its legacy opaque projection. Same-carrier load/clone/export preserves
the exact stored integer. The `LayerStateProperties` constructor, `CopyFrom`, and
`CopyTo` clear only the ambiguous zero cache when copying between those carriers
and encode the existing effective value for the destination.
The transfer artifacts therefore contain a normal layer with packed zero, its
captured state encoded as `0x01000000`, the original physical state zero, and an
opaque destination layer encoded as `0x020000FF`.

R2000 outputs qualify existing library compatibility behavior, not a claim that
the DXF specification introduced entity transparency in R2000. Unknown packed
prefixes are storage controls; no interpretation or rendering of their private
bits is claimed. The 552-file gate covers persisted packets. The shipping C#
tests and separate adversarial API review cover rejected edits, stored-value
presence, clone isolation, whole-value replacement, and transfer behavior before
serialization. The pinned independent SECTION producer provides the native
common group-440 regression; these additional boundary fixtures are deliberately
constructed compatibility controls.

The [qualification receipt](transparency-independent-qualification.json) records
552 accepted files and 4,920 rejected parsed-output corruptions for both final
isolated Debug and combined Release output. It also records all 78 passing
independently authored API counterexamples against the exact final Debug DLL.
