# SUNSTUDY producer attempts and raw preservation evidence

The recovered producer probe uses the exact NuGet package IxMilia.Dxf 0.8.4.
`producer/packages.lock.json` pins the dependency content; `source-manifest.json`
also pins the package SHA-256, every original/partial output, and compressed
bytes. `producer-results.json` identifies the executed assembly SHA-256 and
preserves all 16 outcomes. The separately pinned generator-source revision is
not claimed to be the package build commit. No producer patch was applied.

| Attempt | R2013 ASCII | R2013 binary | R2018 ASCII | R2018 binary |
|---|---|---|---|---|
| No dates, no hour entries | Self-roundtrip | Self-roundtrip | Self-roundtrip | Self-roundtrip |
| No dates, four hour entries | Self-roundtrip | Malformed binary / reload fails | Self-roundtrip | Malformed binary / reload fails |
| Three dates, no hour entries | Save fails | Save fails | Save fails | Save fails |
| Three dates, four hour entries | Save fails | Save fails | Save fails | Save fails |

Six complete producer originals with usable SUNSTUDY packets are in
`originals-gzip/`; their six
producer resaves are in `resaved-gzip/`. Each retains the full drawing, including
the owner dictionary and the exact page setup, view, visual style and text style
records referenced by groups 340–343. The manifest records the complete ordered
SUNSTUDY body and dependency graph. These source files are retained byte-for-byte, without extraction, handle
reassignment, or metadata normalization.

The two files in `failed-reloads-gzip/` successfully finish the producer's save
operation but are malformed. The writer emits a two-byte short for repeated
group 290, whose binary representation requires a one-byte boolean. Independent
parsing exposes two invalid group-8704 records inside SUNSTUDY. The producer
reload raises `InvalidCastException`. These are negative inputs and are
excluded from the usable producer packet count.

All eight attempted nonempty date-list saves fail with `InvalidCastException`
because a `Double` is supplied to the group-90 `Int32` writer. Their partial bytes
are retained under `failed-saves-gzip/` solely as failed-attempt evidence. They
must not be loaded as valid drawings or used to infer a date-pair grammar.

The original whole files also contain an unrelated producer defect: two default
DIMSTYLE records each write empty handles for groups 340–344. netDxf's strict raw
codec rejects these at the first empty group 340, before reaching SUNSTUDY. This
whole-file import failure is retained explicitly.

The six `carriers/` files replace only those ten empty DIMSTYLE pointers with the
null handle `0`. They normalize transport formatting through ezdxf's low-level
writer. Every other ordered record and decoded value is retained, including the
entire SUNSTUDY body, owner dictionary and all four exact resource records. Each
carrier hash and each changed tag are recorded in `source-manifest.json`; no
original bytes are changed. This is a disclosed codec carrier, not native CAD
validation or a successful unchanged whole-file import.

`SunStudyProducerRawTests.cs` adds 26 cases: six carriers each saved byte-exactly,
normalized to text, and normalized to binary, plus eight unchanged-source
rejection cases. The 18 successful outputs retain every carrier tag, value type,
numeric bit pattern, owner and reference. `tools/verify_sunstudy_producer.py`
independently checks that each carrier differs from its original only in the ten
declared DIMSTYLE values, compares all output records with the carriers, checks
the complete ordered SUNSTUDY field sequence and four resource targets, and runs
six corruption controls. Its low-level parser does not perform a high-level CAD
audit or claim a schema implementation for SUNSTUDY.

These results qualify raw storage preservation of the six stated SUNSTUDY
packets within the disclosed carriers. They do not implement a typed SUNSTUDY authoring API, establish native
AutoCAD acceptance, prove a minimum native DXF version, qualify nonempty date
lists, or evaluate a solar study.

To reproduce the producer behavior and create a new pinned evidence snapshot:

```sh
dotnet restore tests/fixtures/sunstudy-producer/producer/producer.csproj --locked-mode
dotnet run --project tests/fixtures/sunstudy-producer/producer/producer.csproj -c Release -- artifacts/sunstudy-attempts
python tests/fixtures/sunstudy-producer/pin_sources.py artifacts/sunstudy-attempts /path/to/ixmilia.dxf.0.8.4.nupkg
python tools/verify_sunstudy_producer.py
```

The checked-in snapshot preserves the exact executed attempt bytes. Regeneration
can change producer-generated whole-file metadata and therefore hashes; inspect
such changes instead of assuming byte-identical regeneration. Pass the netDxf
conformance artifact directory to the final gate to verify its 18 output files.
