# Stored LAYER_INDEX graphs

The bounded public model stores the finite group-40 Julian-date timestamp and
ordered layer-name entries, each owning one distinct IDBUFFER through group 360.
Layer names are strings independent of the document's layer table. Duplicate,
case-distinct and unresolved names remain distinct entries.

Group 90 is checked against the complete group-330 sequence in its resolved
IDBUFFER, including duplicate and null references. A mismatched input count is
rejected. After explicit edits to the buffer's reference list, output derives the
count from that list. No stale stored count is silently repaired during import.

`SetEntries` snapshots and validates entries before changing ownership. Detached
indexes can adopt unowned detached buffers; registered indexes can rename and
reorder their existing entries while preserving the entire owned buffer set.
The ordinary IDBUFFER reference API remains editable. Generic object-graph
cloning remaps the owned buffers and requires explicit mappings for cross-document
external references. Terminal erasure follows reciprocal ownership and rejects
incoming references from outside the erased graph.

The reader accepts the public fields as ordered parallel sequences, allowing
both grouped arrays and interleaved name/handle/count tuples. Unknown private
fields or subclass extensions preserve the entire object as opaque. LibreDWG's
undocumented extra leading group 90 is also opaque. Opaque graphs cannot be
cloned without a schema. The writer emits interleaved public tuples.

The model stores an index graph; it does not calculate index contents, evaluate
layer membership, update timestamps, or track later layer renames. Native index
execution and historical native interoperability are not qualified.

## Schema evidence

- [Autodesk LAYER_INDEX schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-17560B05-31B9-44A5-BA92-E92C799398C0.htm) describes timestamp 40, repeated layer names 8, hard-owned IDBUFFER handles 360, and IDBUFFER entry counts 90.
- The [Autodesk 2009 DXF reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf), printed pages 194–195, agrees. Its class table gives AcDbLayerIndex with nongraphical flags zero.
- [IxMilia source at 3ab0f9d6](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf.Generator/Specs/ObjectsSpec.xml) declares LAYER_INDEX from R14 and writes three grouped arrays in ordinal correspondence. This supports admitting the six current export profiles without an invented later-version restriction; it is not native-version qualification.
- [LibreDWG source at 34f02f54](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg.spec) writes name/handle/count tuples plus an undocumented leading 90=0. That extra field is not assigned invented semantics.

## Qualification

The focused Debug and Release suites each pass 140 LAYER_INDEX cases and 289
related regression cases (429 total per configuration). An independently written adversarial probe passes 182 cases, including
all six versions and both transports. These checks cover grouped and interleaved
fields, complete ownership and count validation, private fallback, entry changes,
explicit clone mappings, binary/XData independence, and erasure preflight.

The [fixture manifest](../../tests/fixtures/layer-index/manifest.json) pins 12
unchanged IxMilia.Dxf 0.8.4 producer files by SHA-256, together with the NuGet
package hash and separately identified schema-source commit. Whole producer-file
import is not qualified: their unrelated DIMSTYLE or STYLE scaffolding fails
before the LAYER_INDEX parser. The originals also have one known root dictionary
owner repair in ezdxf's audit.

The extraction script builds independent ezdxf 1.4.4 scaffolding and copies all
six original application packets per input (two indexes and four buffers),
changing only declared identity/reference handles through an explicit manifest
map. All 72 source application packets match exactly after that map; extracted
fixtures require zero audit errors or repairs. Only generated CLASS packets are
sorted; source packet order remains unchanged. All extracted file hashes match
under PYTHONHASHSEED 0, 1, and 17.

The [corpus assessment](../../tests/fixtures/layer-index/corpus-assessment.json)
found no actual LAYER_INDEX objects in six pinned LibreDWG Leader drawings and
five shared TABLE corpus drawings. Some contain CLASS declarations; those are
not native application-object qualification.

The conformance suite emits 72 drawings per configuration: 12 authored, 24
extracted-producer roundtrips, 12 graph clones, 12 erased graphs and 12 opaque
variants. The mandatory [independent verifier](../../tools/verify_layer_index.py)
passes those outputs in both configurations. It verifies pinned original and
extracted input hashes, all 72 exactly mapped source packets, output counts and
reciprocal ownership, internal references, XData, persistent reactors, extension
dictionaries, aliases, fresh clone identities, terminal erasure, and exact private
variants. Every output has zero independent audit errors or repairs. Three
corrupted-output controls exercise count, ownership, and literal-escape failures.

All five library targets build in Release: netstandard2.0, net471, net48, net6.0
and net8.0. The netstandard2.0 Debug build also passes. The builds retain the
existing 561 CS1591 XML-documentation warnings per target and introduce no other
warning categories.

To verify an emitted artifact directory independently, run:

```sh
python tools/verify_layer_index.py /path/to/artifacts
```

The full mandatory-verifier runner discovers this script automatically.
