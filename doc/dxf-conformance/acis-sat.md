# Inert ACIS SAT entities

`Body`, `Region`, and `Solid3D` expose the SAT envelope used by BODY, REGION, and 3DSOLID in the AutoCAD 2000, 2004, 2007, and 2010 DXF profiles. `DrawingEntities.Bodies`, `.Regions`, and `.Solids3D` enumerate these entities in the active layout. The existing `Solid`/`.Solids` API remains the separate four-corner 2D SOLID entity.

This module stores modeler data. It does not parse SAT geometry, evaluate topology, repair a model, compute a bounding box, tessellate arbitrary ACIS, or provide a solid-modeling kernel. A syntactically valid DXF envelope does not establish that its opaque SAT content is accepted by any particular ACIS or CAD version.

## Payload API and fidelity

```csharp
var body = new netDxf.Entities.Body();
body.SetSatLines(existingSatLines); // decoded logical lines, without newline delimiters
var document = new DxfDocument(DxfVersion.AutoCad2010);
document.Entities.Add(body);
document.SaveAtomic("body.dxf");
```

`ModelerFormatVersion` is the DXF group 70 envelope version, fixed at the supported value 1. It is independent of the SAT content version, such as 700, in the first opaque SAT line. No content-version conversion occurs.

`EncodedSatChunks` is an immutable snapshot of immutable `AcisSatChunk` objects. Each has a `GroupCode` of 1 (new logical line) or 3 (continuation) and an encoded `Text` value. `SatLines` provides a corresponding immutable snapshot of decoded logical lines. `SetEncodedSatChunks` retains exact chunk boundaries, empty chunks, and whitespace. `SetSatLines` encodes printable ASCII and produces canonical chunks of up to 255 characters; it retains whitespace and empty lines. Both setters validate a complete caller enumeration before replacing the current payload. Existing snapshots remain unchanged after later edits.

SAT text uses its own substitution codec. Encoded chunks bypass generic DXF Unicode decoding and escaping. The producer corpus includes an actual encoded `\U+0041` sequence and verifies that it survives as literal encoded text in both transports. The codec also retains an encoded A escape split across chunk boundaries.

The envelope limits are 65,536 chunks, 16 MiB of total encoded characters, and 1 MiB of encoded characters per logical line. Strings contain printable ASCII only, without CR, LF, NUL, or other control characters. Group 3 requires an earlier group 1. An encoded A marker must be followed by its codec space, including across chunks. The parser rejects missing/duplicate format versions, unsupported format values, unknown private tags/subclasses, malformed continuations, and misplaced data after XData or solid history. No SAT record-count or proprietary model grammar is inferred from the content.

An empty input sequence clears the payload. Saving an entity with no chunks fails during writer preflight before bytes are written to the supplied stream. `SaveAtomic` also preserves an existing destination file, document name, and working folder on failure. The legacy `Save(string)` API retains its existing truncate-on-open behavior and changes name/folder before writer preflight; use `SaveAtomic` when destination preservation is needed. A stored empty logical line remains a line; the library does not infer whether it constitutes useful modeler content.

## History, cloning, transforms, and versions

`Solid3D.HistoryHandle` preserves absent (`null`) versus explicitly null (`"0"`) group 350 metadata in the 2007 and 2010 profiles. Nonzero/live history pointers are rejected because preserving their object graph requires a separate modeler-history implementation. Explicit history metadata in 2000/2004 is rejected conservatively; the independent producer emits the `AcDb3dSolid` subclass only after its 2004 profile. For 2007/2010, the writer emits that subclass even when group 350 is absent. Exact subclass-marker omission is not part of the SAT chunk-fidelity promise.

Clones retain independent payload snapshots, common entity metadata and proxy bytes, and cloned XData. They are detached from handles, owners, and reactors, following the normal entity clone contract. Generic extension dictionary cloning remains an explicit database-graph operation. Block/INSERT clones preserve the payload; an identity INSERT can be exploded. Nonidentity `TransformBy` and an INSERT explosion requiring such a transform throw before modifying payload data. This includes tiny translations and shears and invalid homogeneous bottom rows in a full `Matrix4`, also when the caller holds an `EntityObject` reference. Stored INSERT placement can still position a block without rewriting its contained SAT data.

The typed reader and writer reject these entities in 2013 and 2018 profiles. Those files use SAB with an ACDSDATA section, entity UID/flags, and identity-linked ACDS records. Supporting that representation requires its own schema and lifecycle work, including the section's definitions and its handle references. This implementation does not emit an empty replacement, discard an opposite-format payload, or convert SAT to SAB. `DxfRawDocument` is the available whole-file preservation path for unsupported ACIS/history graphs.

## Qualification

The 242 registered scenarios in `AcisSatTests.cs` cover exact text/binary preservation and repeated transport flips across the four positive profiles, all six supported profiles' placement/preflight behavior, malformed packets and limits, immutable snapshots, transactional failures, clone/ownership/common metadata, transform guards, and history absence/zero.

The committed producer corpus under `tests/fixtures/acis-sat` consists of 24 files independently authored with ezdxf 1.4.4: BODY and 3DSOLID cube shells and planar REGION bodies across four profiles and both transports. Its generator uses ezdxf's own length-prefixed SAT header writer for long product text and pins metadata timestamps. The manifest records source hashes, exact encoded chunks, decoded lines, body/vertex/face counts, and versions. Each source produces two outputs, for 48 independently checked exports.

Run the ordinary conformance executable from the repository root, then:

```sh
python tools/verify_acis_sat.py artifacts/conformance
```

The verifier checks all 48 outputs, both actual transports, exact chunks and history presence, source hashes, filename-bound versions, decoded lines, common metadata, entity/LINE handle and owner identity, following XData/LINE, and zero ezdxf audit errors or repairs. For these known polyhedral producer fixtures only, it also compares the independent ACIS subset's extracted vertices/faces. That check does not qualify arbitrary SAT model evaluation or AutoCAD rendering.

## Primary references

- Autodesk [BODY group codes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-7FB91514-56FF-4487-850E-CF1047999E77.htm), [REGION group codes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-644BF0F0-FD79-4C5E-AD5A-0053FCC5A5A4.htm), and [3DSOLID group codes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-19AB1C40-0BE0-4F32-BCAB-04B37044A0D3.htm): modeler subclass, envelope version, proprietary text groups, and solid history pointer.
- ezdxf [ACIS documentation](https://ezdxf.readthedocs.io/en/stable/acis.html) and [3DSOLID documentation](https://ezdxf.readthedocs.io/en/stable/dxfentities/3dsolid.html): SAT/SAB profile split and limitations of its ACIS subset.
- ezdxf 1.4.4 primary source: [`entities/acis.py`](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/acis.py), [`tools/crypt.py`](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/tools/crypt.py), [`acis/hdr.py`](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/acis/hdr.py), and [`sections/acdsdata.py`](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/sections/acdsdata.py): codec, chunking, profile gates, valid producer header encoding, and deferred external binary data lifecycle.

The codec implementation follows ezdxf's MIT-licensed substitution algorithm, copyright (c) 2014–2018 Manfred Moitzi. Its permission notice is reproduced in the source alongside the adapted codec.
