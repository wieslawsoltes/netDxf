# Independent standalone opaque entity review

The unchanged [review receipt](recovery-audit/opaque-review-summary.json) records 91 passing cases in each of Debug and Release against production commit `15b228174f3fc4c123caef8b04f1c791efb1001f`. The independent probe validates packet equality, common edits, CLASS identity and text, exact standard references, table resource removal, source-owned metadata, non-model placement and aggregate admission limits. Its assertions and mutations are separate from the conformance implementation.

The review found two defects in the initial candidate: literal CLASS backslashes could be decoded again after output, and the shared table-resource collector omitted opaque entity references. The preserved four-case correction probe reproduces both on the original DLL and confirms both fixes on the final Debug and Release DLLs. Initial fixture setup failures were excluded until their valid baselines loaded; the receipt identifies those corrections.

The main probe reuses only the declared `OpaqueFixture(DxfVersion, string)` factory from the pinned conformance assembly identified in the receipt. This is explicitly shared fixture provenance, not an independent producer or native CAD sample. The factory source is in `tests/netDxf.Conformance/OpaqueEntityTests.cs`; the pinned initial source checkpoint is `36658f6`. Replaying with a rebuilt factory should be recorded as a new run with its actual assembly hash.

## Preserved evidence

`recovery-audit/opaque-probe/` contains all three unchanged C# sources and its project. `recovery-audit/opaque-corrections-probe/` contains the unchanged before/after proof source and project. The review receipt, result records, audit records, scope statement and Python audit source retain their original bytes and hashes.

The [output packet archive](opaque-independent-output-packets.tar.gz) preserves all 120 original DXFs: 57 main Debug outputs, 57 main Release outputs, and two CLASS proof outputs for each of the original, corrected Debug and corrected Release configurations. The [manifest](output-packet-manifest.json) gives each archive member's original byte length and SHA-256. Extraction restores the original `recovery-audit/` relative paths. All archive members were read back and checked against their hashes after packaging.

The 114 main outputs had zero ezdxf errors or repairs. ezdxf 1.4.4 retains the declared unknown type as `DXFTagStorage`; those audits check surrounding structure and do not interpret application geometry or private semantics. Complete raw packet checks are separate. No native CAD application was run.

## Replay

From this directory, restore the preserved packets with:

```sh
tar -xzf opaque-independent-output-packets.tar.gz
```

Build the independent main probe against a qualified library, then supply the conformance fixture-factory assembly and a new output directory:

```sh
dotnet build recovery-audit/opaque-probe/Probe.csproj -p:DxfReviewLibrary=/absolute/path/netDxf.netstandard.dll
dotnet recovery-audit/opaque-probe/bin/Debug/net8.0/Probe.dll /absolute/path/netDxf.Conformance.dll /absolute/path/new-review-output
python recovery-audit/audit_probe_outputs.py /absolute/path/new-review-output
```

The production and fixture-factory assembly hashes must be recorded for any new replay. The original qualification hashes, complete case names and exact observed output hashes remain in the preserved records.
