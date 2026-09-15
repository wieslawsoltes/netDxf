# Stored SPATIAL_INDEX and VBA_PROJECT envelopes

This module exposes two public OBJECTS envelopes and their existing database ownership, reactor, extension-dictionary and XData relationships. It does not build spatial indexes, interpret VBA project structure, execute macros, or establish native CAD interoperability.

## Public API and qualified profiles

| Object | Public model | Stored fields | Typed export qualification |
| --- | --- | --- | --- |
| SPATIAL_INDEX | `DxfSpatialIndex` | `Timestamp`: finite `double`, group 40, between `AcDbIndex` and empty `AcDbSpatialIndex` subclasses | 2000, 2004, 2007, 2010, 2013, 2018 |
| VBA_PROJECT | `DxfVbaProject` | `DataLength`: group 90; `Data`, `Chunks`, `SetChunks`: group 310 byte sequence | 2000, 2004, 2007, 2010, 2013, 2018 |

These dates identify tested database-format families, not first product releases. Both envelopes are qualified in all six profiles. The independent VBA exporter permits 2000 and the published byte grammar shows no profile-specific difference; historical data-model tables do not justify an additional cutoff. No native VBA corpus was found, so this qualification establishes inert public-field storage, without claiming native project validity or an introduction date.

`Timestamp` defaults to zero and accepts every finite double, including negative values, negative zero, subnormal values and fractions. There is no calendar conversion, range inference or automatic update. Recognized input requires one timestamp; a missing value is rejected rather than synthesized. Text output normalizes lexical formatting and retains the double value; the tested runtime preserves negative-zero bits in both transports.

`Data` returns a defensive byte snapshot. Assigning it validates the size, copies the bytes, and creates canonical physical chunks of up to 127 bytes. Assigning an empty array creates no chunks. `Chunks` returns an independently copied read-only sequence of independently copied arrays. `SetChunks` validates and snapshots the complete enumerable before replacing the payload. Zero-length physical chunks are retained: no chunks and two empty chunks both encode count zero, but remain distinguishable. Null arrays, null chunks and invalid limits are rejected without changing prior data. Exceptions during enumeration or disposal also leave the old payload intact.

| Payload admission limit | Value |
| --- | --- |
| `MaximumChunkLength` | 127 bytes per group 310 |
| `MaximumDataLength` | 16 MiB of concatenated data |
| `MaximumChunkCount` | 262,144 physical chunks, including empty chunks |

These are model and payload admission limits. The typed OBJECTS reader collects the record's tags before validation; the limits do not establish a whole-file or streaming allocation bound. `DxfRawDocument` has separate input budgets and remains available for preservation outside this typed qualification.

## Parsing and extension boundaries

The reader requires unique public subclass markers and required scalar fields in the documented order. For VBA, count 90 must be nonnegative, within the admitted range, precede group 310, and equal the exact sum of public chunk lengths. Duplicate counts, missing or reordered required fields, overlong chunks, excessive chunk counts and truncated payloads are rejected. XData starts at group 1001 and uses the existing common XData reader after a recognized payload.

A genuinely unknown field or private subclass retains the entire subclass payload as a `DxfOpaqueObject`, including original tag order, bytes and XData tags. Nothing from that record is partially exposed as a typed object. Known fields remain validated in their public subclass: an unknown field does not conceal a duplicate required count or marker. Fields inside a private subclass are not interpreted as public fields. An unknown first subclass selects the opaque path; the known `AcDbSpatialIndex` subclass without its required `AcDbIndex` base is malformed.

The models use caller-selected dictionary attachment. There is no inferred `ACAD_INDEX` placement helper. Existing `CloneObject`, dictionary `Clone`, and `CloneExtensionDictionary` operations copy the owned graph, preserve payload/chunk boundaries, and remap owners, persistent reactors, extension dictionaries and XData 1005 handles. External references require explicit mappings across documents. Opaque graphs retain the existing refusal to guess private references during cloning.

SPATIAL_INDEX CLASS metadata uses `AcDbSpatialIndex`, `ObjectDBX Classes`, nongraphical classification and the physical instance count. Enforcement occurs only when a typed spatial index is present; unused private classes and opaque-only instances preserve their declarations. Mixed typed/opaque populations count all physical SPATIAL_INDEX records. The original declaration is not mutated during writing. The 2000 writer omits the derived CLASS count under its existing profile rule. No VBA_PROJECT CLASS metadata is invented.

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
var group = new DxfDictionary();
document.NamedObjects.Add("APPLICATION_ENVELOPES", group);
group.Add("INDEX", new DxfSpatialIndex { Timestamp = 2451544.5 });

var project = new DxfVbaProject();
project.SetChunks(new[] { new byte[] { 0, 255, 17 }, Array.Empty<byte>() });
group.Add("PROJECT", project);

// The bytes are inert; this does not validate them as an executable project.
document.SaveAtomic("envelopes.dxf", isBinary: true);
```

Both stored objects participate in the existing supported-profile writer rules. The earliest supported profile, 2000, is explicitly tested for cross-document graph cloning, stream output and replacement through `SaveAtomic`. Failed reference remapping still rejects before attachment or handle allocation. `SaveAtomic` provides the existing file and document-state preservation contract; ordinary filename-based `Save` retains its separate legacy behavior.

## Evidence and qualification limits

- Autodesk's [SPATIAL_INDEX schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-CD1E44DA-CDBA-4AA7-B08E-C53F71648984.htm) documents the Julian-date timestamp and empty spatial subclass. Its [VBA_PROJECT schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-F247DB75-5C4D-4944-8C20-1567480221F4.htm) documents the byte count followed by binary chunks. The [official 2009 DXF reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf), printed pages 226 and 236, corroborates both envelopes.
- ezdxf 1.4.4's [`VBAProject` implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dxfobj.py) writes count 90 and 127-byte chunks. Its loader ignores the declared count, and its `copy_data` implementation does not copy from the source. Neither behavior is used as a validation or clone oracle.
- Upstream [synthetic VBA unit tests](https://github.com/mozman/ezdxf/blob/d6f2ac10caeddc712ed1824aaeb3b9c050de4a04/tests/test_01_dxf_entities/test_136_vba_project.py) use small byte arrays. The [upstream changelog](https://github.com/mozman/ezdxf/blob/d6f2ac10caeddc712ed1824aaeb3b9c050de4a04/notes/pages/CHANGELOG.md) records the absence of real-world embedded VBA DXF files. The [data-model overview](https://ezdxf.readthedocs.io/en/stable/dxfinternals/datamodel.html) supplies historical context only.
- An independent scan of 14 pinned upstream DXF files found no actual SPATIAL_INDEX or VBA_PROJECT OBJECTS records. SPATIAL_INDEX occurrences were CLASS declarations. The [corpus assessment](../../tests/fixtures/stored-envelopes/corpus-assessment.json) records file paths and Git blob hashes. This bounded negative finding does not assert that native files do not exist elsewhere.

The [fixture corpus](../../tests/fixtures/stored-envelopes/README.md) has 24 inputs: 12 spatial envelopes and 12 VBA envelopes, each across six profiles and two transports. Spatial payloads are explicitly authored from the public schema. VBA payloads start with the independent exporter, followed by a documented raw chunk-boundary patch, and contain synthetic bytes. The normal harness loads every input and emits 48 roundtrips, plus 12 authored exports. The independent [60-output verifier](../../tools/verify_stored_envelopes.py) requires the exact source/output inventory and checks source hashes, actual profiles/transports, timestamp bits, count/chunk framing, empty chunks, ownership/reactor/extension links, XData, following records and zero ezdxf audit errors or repairs.

Focused C# scenarios also exercise exact/over model and input limits, setter atomicity, malformed/private distinctions, explicit cross-document mappings, clone isolation, stream and atomic-save behavior, and private/unused CLASS preservation. This qualification covers public tags and database graphs; it makes no claim about native VBA validity, index usefulness, macro behavior or CAD evaluation.

## Verification record

The focused module has 140 scenario registrations and passes in Debug and Release on .NET 8. The independent gate passes all 60 exports in each configuration against all 24 committed inputs, with no ezdxf audit errors or repairs. Two separate generator invocations reproduce the same source hashes; automatic CLASS record order is explicitly sorted to remove Python hash-order variation.

The library also compiles in Release for netstandard2.0, net471, net48, net6.0 and net8.0, and in Debug for netstandard2.0. Execution qualification is on .NET 8; compilation of the other targets is not a claim of native CAD evaluation or platform-specific runtime testing. Full combined-suite validation is recorded by the repository's integration qualification.
