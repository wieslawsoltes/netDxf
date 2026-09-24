# Entity body codecs: local parity continuation

Executable local commit: `53138d7a13b4c3def4a0d49fe482ab8dbf12be10`.
Earlier tested local commit: `30dfe8ef0739736615313a2f90c4322c139635da`.
Executable tree: `5da4c38072e1519e110094f2d27af1c050b8e13d`.
Latest preserved remote checkpoint: `b7a572eea896c84fe477cf13b8903fb7b2f3e0e4`.
Published starting checkpoint: `8c6ff73fa6b5ca126b566d626a9d1bc4ac92cb09`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This continuation implements five original-path entity-body codec files and a
shared XData helper. It does **not** provide complete typed document loading,
saving, entity dispatch, resource reconstruction or private CAD evaluation.

## Publication and recovery

The available files contained recovery archives, not an unpublished checkout.
Restoring the published checkpoint reproduced its complete tree
`a31afdf1e6b3aa09f7b470c24d163c37bd1b52fb` exactly. Existing source, tests,
fixtures and earlier port work were preserved.

A final remote read discovered `b7a572e`, which removed eighteen branch-specific
JavaScript workflows while preserving every source and test file. Its complete
tree `495d3f12d59e24e2983c3a45fd71abb63f930d5a` was reproduced exactly. The
local commits were reapplied on that tree without restoring a removed workflow;
the earlier local commits remain on a recovery branch. Runtime and verification
fingerprints remain identical to the tested `30dfe8e` checkpoint.

The implementation and verification integration are committed locally. GitHub
accepted an unreferenced partial tree, but blocked the next tree write with an
indeterminate safety-status result. No branch update or remote commit was made
in this continuation. The incomplete tree was not published. The accompanying
format-patch files preserve the complete local changes for review and recovery;
local reconstruction commit identities differ from the published history.

## Exported APIs

The existing browser-safe `DxfTransport` namespace and original relative modules
now expose:

| Original-path module | Functions |
| --- | --- |
| `netDxf/IO/DxfOleFrame.js` | `ReadOleFrame`, `WriteOleFrame` |
| `netDxf/IO/DxfOle2Frame.js` | `ReadOle2Frame`, `WriteOle2Frame` |
| `netDxf/IO/DxfLight.js` | `ReadLight`, `WriteLight`, `ValidateLightVersions` |
| `netDxf/IO/DxfAcisSat.js` | `ReadAcisEntity`, `WriteAcisEntity`, `ValidateAcisEntities` |
| `netDxf/IO/DxfLwPolyline.js` | `ReadLwPolyline`, `WriteLwPolyline`, `ValidateLwPolylineFidelity` |

Readers accept a code/value reader already positioned at the appropriate
subclass marker and the destination document. `ReadAcisEntity` additionally takes
the entity code. Writers accept the code/value writer, DXF version and entity.
The adapters expose source private methods explicitly; they are not CLR reflection
or access-control emulation. `WriteLwPolyline` is sourced from the original main
writer, alongside the related original partial-file reader and validation code.

The shared `runtime/DxfXDataIO.js` implements the source XData read/write path.
It is a reusable helper, not an additional original source mirror.

## OLEFRAME and OLE2FRAME

The body readers preserve byte-count and chunk limits, required terminators,
duplicate metadata checks and optional metadata presence. Omitted fields remain
distinct from explicitly supplied default values. OLE2 corner coordinates require
complete triples. XData is admitted at the source-defined point in the packet.

A declared count does not cause an allocation of that size; storage grows from
received chunks. Readers retain the native failure position and leave the next
group-zero record current after success. Nested metadata exceptions retain their
original error type and parameter inside the InvalidDataException wrapper.

Writers emit independently allocated binary packets of at most 127 bytes. A
caller mutation can affect data not yet copied, as in the original synchronous
writer, but cannot rewrite a packet already delivered to the caller. Tests cover
Node Buffer storage, re-entrant writes and the completed output prefix when a
callback throws. OLE payloads remain opaque bytes: nothing activates, interprets
or executes their contents.

## LIGHT

The reader handles the source-defined scalar properties and position/target
triples. Duplicate known fields are rejected before applying the second value.
Property validation retains its failure timing, group code, reader position and
inner exception. Unknown scalar groups retain the original advancement behavior;
unsupported subclass markers and unprefixed XData are rejected.

The writer retains source field order and database-string escaping. The profile
validator visits all registered blocks, including unused definitions and inactive
layouts, rather than checking only the active entity collection. It does not
silently downgrade a LIGHT to another entity type.

## SAT-based ACIS

BODY, REGION and 3DSOLID body codecs retain encoded SAT chunks without applying
generic DXF Unicode decoding to the modeler payload. Format-version placement,
continuation ordering, chunk/line/total size bounds, private-history restrictions
and XData termination follow the pinned implementation. Output preserves the
stored encoded strings and profile-dependent 3DSOLID history subclass.

As in the source, the qualified SAT path rejects DXF 2013+ profiles that require
SAB/ACDSDATA support. It does not execute a solid modeler, regenerate topology,
convert SAT to SAB or claim support for a live ACIS history graph.

## Lightweight polylines

LWPOLYLINE parsing checks the declared vertex count against actual vertices,
requires X/Y pairing, preserves nullable constant and per-vertex width presence,
and retains identifiers, bulges, flags, elevation, thickness and normal behavior.
Zero widths remain distinct from absent widths; binary64 negative zero is retained.
Duplicate counts, widths and identifiers preserve native failure ordering.

The version/fidelity validator rejects unsupported identifier down-saving and
smoothed output that would discard lightweight-polyline-only metadata. The writer
emits the source body order without inventing an entity/common-data envelope.

The native Debug build has an assertion for an unprefixed-XData branch in this
reader. The portable implementation retains Release advancement rather than
terminating the JavaScript process. That assertion path is not in the new finite
comparison corpus and is not represented as verified Debug process equivalence.

## XData and observations

XData reading uses actual document application registries and retains the source
canonicalization side effects, including registration that precedes a later
entity failure. Decoding is one-pass. Repeated application groups preserve record
order, typed scalar reads and source limits. Binary output packets are copied,
including final and zero-length packets.

The input-only corpus has **1,107 unique scenarios / 3,714 operations** across
text, modern binary and legacy binary, six DXF profiles, malformed and valid
packets, scalar/length boundaries, metadata presence, XData and randomized
polyline inputs. Negative-zero input is transported by its explicit binary64 bits;
observed results are not normalized.

The C# harness invokes unchanged native private methods. The JavaScript harness
invokes the actual production helpers. Comparisons cover exact emitted bytes,
model values, errors, inner errors, reader state and positions, and registry
side effects. Incomplete observation envelopes are failures.

**31 supplemental tests** were added. No original test identity was added or
shortened: cases that still require complete typed Load/Save remain missing.

## Completed local verification

SDK 8.0.425 / .NET 8.0.31, Node 22.16.0, Debian 13 x64. All entries below belong
to the executable local commit above, not to a new hosted workflow run.

| Check | Actual result |
| --- | --- |
| New native/JS comparison, Debug | 1,107 scenarios / 3,714 operations; zero mismatches or unavailable observations |
| New native/JS comparison, Release | 1,107 scenarios / 3,714 operations; zero mismatches or unavailable observations |
| Full unchanged C# suite | 35,309 passed in each configuration |
| Complete mirrored original JS subset | 3,619 passed; no unexpected or duplicate identities |
| Full supplemental suite | 1,184 passed; zero failures, skips or TODOs |
| Offline-installed package | Passed; 570 files |
| Foundation, dimension and GTE generation | All three exact regeneration checks passed |
| Focused Chromium, Debug and Release | Each: 1,107 scenarios / 3,714 operations; zero differences or page errors |
| Full-parity verification, both configurations | Failed; missing work and unrun evidence remain blocking |

The focused browser run uses real Chromium 144.0.7559.96 and 539 native ESM
modules, compared with independently recorded C# observations. Its scripts and
full native observations are included in the validation archive. It is **not**
the complete 146,991-check browser qualification and does not satisfy that gate.

The new category is wired into the existing browser runner, aggregate verifier
and installed-package smoke test. The initial codec-workflow edit was discarded
when preserving the newer upstream workflow deletion; no removed workflow is
reintroduced. The complete 52-stage aggregate, full browser suites, MPFR/performance
checks and hosted CI were not rerun for this local checkpoint. Historical results remain tied to their earlier commits.
An interrupted initial original-test attempt was followed by a complete passing
rerun; only the completed run supplies the result above.

Runtime fingerprint:
`1dbbd215e548792f30659c0a4503122233ea65a87084127a96a621a4b8db2994`.
Verification fingerprint:
`aa8b544ec031ffd41af69688ccbdd09a7668e0528e04d7c3cdd3032f3ea7b511`.
Unchanged source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

With the pinned source and toolchain configured, run from `javascript/`:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:entity-body-io
npm test
npm run test:unit
npm run test:package
npm run verify:complete
```

## Remaining work

The current local ledger is **421/510 library mirrors**, **66/193 original
conformance-file mirrors**, and **3,619/35,309 original cases**: **89 library
paths, 127 test-file paths and 31,690 original cases** remain missing. File presence
is not exhaustive member/signature or behavioral qualification.

Complete typed document dispatch, entity envelopes, resource/reference binding,
Load/Save/SaveAtomic, full private-schema and TABLE/evaluator IO, general version
conversion and broad numerical/host/performance acceptance remain incomplete.
Existing numerical, native-assertion/recursion and globalization-profile failures
are not waived by the focused results here. No original C# source, original test
or shared fixture changed; there was no merge, force push or npm publication.
