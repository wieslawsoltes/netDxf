# Preview, metadata and mesh transport helpers

Runtime checkpoint: `38e48f152c0ebf33221f456de2c74066db0b8e24`.
Executable verification checkpoint: `688b6d0e50d9c71e82ee209ac8042746302408bd`.
Executable tree: `53ce231b513b9367dae34dcc22d221ed26f09227`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This adds five original-path IO mirrors and an explicit browser-safe
`DxfTransport` namespace. They are working section codecs and output validators,
**not a complete typed DxfDocument.Load/Save/SaveAtomic pipeline**. They do not
render previews, interpret image formats, or evaluate private CAD schemas.

## Implemented contracts

**DxfThumbnailImage.** Read starts at the section name, checks the single
nonnegative group-90 count against the bytes actually received, tolerates the
source's comment/empty/128-byte input cases, and leaves ENDSEC current. Missing,
duplicate, excessive and truncated counts retain the original failure order and
positions. Buffer growth follows received data, never an untrusted declared
allocation. Write emits nothing for an empty image; otherwise it emits the full
section, byte count and independent packets of at most 127 bytes.

**DxfEntityCommonData.** The explicit reader state retains color name, shadow mode,
proxy byte count, accumulated bytes and completion state. Null proxy data differs
from a declared empty proxy. Count-after-data, duplicate fields, 128-byte input
packets, the 16 MiB bound, long-count range checks and version restrictions match
the source. Rejected values preserve prior state and callback timing; completion
preserves native disposal/repeat behavior. Output uses group 92 before the 2013
profile and group 160 thereafter, with copied 127-byte packets. Color-name output
preserves literal backslashes and escapes line controls. Whole-document version
validation visits every block, attribute definition and INSERT attribute.

**DxfMTextBackground.** Only background-specific group codes allocate the ref-cell
model. The initialized object remains visible after an invalid first value;
ArgumentException is wrapped with the source position and inner error. Repeated
recognized fields update the same object. Background output requires the 2007
profile, and frame output requires 2018. Required active-background defaults are
written without mutating absent scale or color-index properties on the model.

**DxfMeshWriteValidation / DxfMeshVersion.** Preflight counts the serialized face
list before walking vertex indices, including repeated references to one large
face array. It checks null/short faces, finite coordinates and creases, valid
vertex indices and edges. Document traversal also validates POLYLINE polygon
meshes. MESH output is rejected before the 2010 profile, including entities in
unused definitions and inactive layouts; no automatic lossy conversion is used.

Private C# partial methods are adapted as explicit chunk/version/document
functions rather than represented by a second incomplete DxfReader class.
The string adapter preserves single-pass DXF Unicode decoding, pre-2007 UTF-16
escapes, and literal database-string escaping. Arbitrary encodings and full CLR
stream/localized-exception behavior are outside this helper checkpoint.

## Buffer ownership correction

A regression test caught Node Buffer.slice returning shared storage. Preview and
proxy writers now allocate a fresh Uint8Array for every packet and copy the
relevant subarray into it. Retaining or mutating a supplied Buffer cannot rewrite
an already emitted packet. Tests also preserve the completed prefix when a
synchronous caller write throws. This correction is included in `688b6d0`.

## Original and independent tests

The input-only corpus has **1,165 scenarios / 3,473 operations**, using text,
modern binary and legacy binary codecs, six writer profiles, all relevant block
placements and malformed/count boundary cases. Independent controllers invoke
the unchanged C# private methods and the actual production JavaScript helpers.
They observe exact emitted bytes, reader state and position, metadata state,
inner errors and nullable background fields. Strict envelope/count checks reject
missing or malformed observations. No expected output tables or normalization
are used in production code.

**32 complete original ThumbnailImageTests cases** are now mirrored. The twelve
original document-roundtrip cases still requiring typed Load/Save are absent,
not shortened, skipped or counted as passing. **29 supplemental tests** are
counted separately. The category is mandatory in aggregate verification, appended
after all existing browser inputs, and exercised from the offline-installed
package. Existing reader/writer corpora and the Windows replacement-host build
remain enabled in the hosted workflow.

## Remaining work

**416/510 library mirrors (94 missing), 66/193 original conformance-file mirrors
(127 missing), and 3,619/35,309 original cases (31,690 missing).** Presence is not
exhaustive public member/signature or behavioral qualification.

Release retains the existing 26 concrete-dimension, six LEADER, eight block,
three coordinate and 128 entity operation differences. GTE retains twelve
polynomial operation differences and 32 unavailable native scenarios in Release,
or 38 unavailable scenarios in Debug. Debug also retains the original six
dimension, twelve tolerance and four layout assertions and one cubic Bezier
NaN-sign difference. Both casing stages reject the local globalization profile
before pairwise comparisons. None of those failures is waived.

The Release browser's 119 failures are 87 prior-model digest differences and 32
unavailable GTE native observations. All 1,165 new section scenarios match. The
browser count is not a claim that Node's polynomial differences were fixed.

Complete typed reader/writer dispatch, DxfDocument Load/Save/SaveAtomic,
resource/reference reconstruction, all private-schema IO, general version
conversion, remaining original tests/examples and broad host/numerical/performance
acceptance remain unfinished. No AutoCAD open/AUDIT/save/reopen fidelity is claimed.
No original C# or fixture change, merge, force push, expected-failure waiver or
npm publication occurred. PR #98 remains a draft.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/TRANSPORT_SECTIONS.md). Current published scope is maintained in the [README](../README.md).
