# Native MLEADER input envelopes and private styles

`MultiLeader.StoredVersion` retains whether entity group 270 was physically
present. `DxfMLeaderStyle.StoredEnvelopeValue` does the same for style group 179.
Both properties default to 2 for newly authored objects and accept either 2 or
null. A null value survives cloning, typed loading and writing. The effective
qualified entity grammar remains version 2; absence does not select another
parser. Explicit unsupported values, repeated markers, misplaced markers and
malformed known packets are rejected.

An MLEADERSTYLE record with an unfamiliar field or private subclass is retained
as a complete `DxfOpaqueObject`. Its scalar order, values, common metadata and
XData are preserved. References tentatively collected for a discarded typed
style are removed from the typed resolution queue. This matters when a private
style uses a reference with semantics that the public schema cannot establish.
A MULTILEADER still requires a typed compatible style; opaque retention does
not make a private style usable by the typed entity API. Malformed recognized
fields remain errors even when an additional private field is present.

The writer validates public CLASS identities when typed objects require them.
A private CLASS declaration is preserved when all corresponding styles remain
opaque. The tests retain a deliberately private C++ class name, application name
and nondefault instance count rather than replacing that declaration with the
public schema.

`MLeaderLine.StoredColor` retains optional leader-line group 92 as a nullable
raw-color integer. The existing `Color` property still returns the effective
ByBlock raw color 0xC1000000 when the stored value is absent. Assigning `Color`
makes its value explicit; assigning `StoredColor = null` restores absence.
Newly authored lines default to absent stored color with the same effective
ByBlock value. Both component and entity clones retain this distinction.

## Corpus and transformations

The exact original source files, source URLs and decoded SHA-256 digests are
pinned in `tools/table_oracle/fixtures.json`. They are shared with the TABLE
qualification work and are stored once in `tests/fixtures/table-oracle`.

The R2013 `acad_table_simple.dxf` and R2018 `acad_table_with_blk_ref.dxf` source
files contain private style group 298. Both original files load and save in
both transports, preserving every MLEADERSTYLE record exactly. These checks
qualify the private style preservation path. They do not establish fidelity of
TABLE entities or unrelated families in those original documents.

The ACadSharp R2007 source `sample_AC1021_ascii.dxf` contains 15 MULTILEADER
entities that omit group 270, and two MLEADERSTYLE objects that omit group 179.
The full source also contains independent feature boundaries, including live
ACIS history. To isolate the native MLEADER packet grammar without rewriting
the source's private graphs, `tests/fixtures/mleader-native/generate_scaffold.py`
places the following exact original records in a minimal ezdxf 1.4.4 carrier:

- All 15 MULTILEADER packets, with their original model-space owner 1F,
  contexts, geometry, references, handles and XData.
- STYLE 11, LTYPE 14, APPID 107 (`ACAD_MLEADERVER`), style dictionary D7,
  and MLEADERSTYLE D8 and E5.
- The last entity B27's complete extension closure: dictionary 13CD and
  XRECORD 13CE, including its stored roundtrip payloads.
- The original MULTILEADER and MLEADERSTYLE CLASS records.

The carrier's otherwise independent handles are allocated in the F0000 range.
Its model-space block record, root dictionary and STYLE/LTYPE/APPID table
handles are assigned the original identities needed by the extracted records.
The carrier's generated MLEADERSTYLE dictionary and metadata are removed; the
original D7 dictionary is attached to its root. Dates and GUIDs are fixed, and generated carrier CLASS records are sorted by
name so Python hash randomization cannot reorder them.
No tag within any extracted original record is changed. The manifest lists all
23 exact original record identities and every carrier transformation. Repeated
fixture generation across Python hash seeds 1, 2, 3 and 100 produces identical
decoded and gzip bytes.

## Verification

`RegisterMLeaderNativeTests` contributes 199 cases: one API contract case,
32 profile/transport/presence combinations, 32 line-color presence/value cases,
32 private-style combinations,
96 malformed packet controls, two original R2007 scaffold transport cases and
four original complete-file private-style cases. The existing 269 MLEADER
cases run alongside these tests.

The mandatory `tools/verify_mleader_native.py` gate requires exactly 102 output
DXFs. It checks pinned source hashes, physical field presence, exact ordered
entity and private-style payloads, independent ezdxf context semantics,
resource identities, original extension closure, and CLASS counts. It compares
all 23 extracted records to the original compressed corpus before comparing
outputs. All ordered native entity-body values, including physical envelope
and line-color absence, must match. Style scalar order is immaterial, and only
independently documented default style values may be added. These checks
establish decoded payload fidelity within that scope, not whole-file byte
identity. The R2007 scaffold and presence cases require zero ezdxf audit errors
and zero repairs. Three deliberate corruptions of actual output packet copies must be
detected: inserting an absent envelope marker, altering private group 298 and losing a
protected literal Unicode escape.

The unresolved handles deliberately placed in synthetic opaque-style controls
are preserved raw. They are not qualified references, and a semantic audit of
those private values is outside this gate. Native AutoCAD rendering,
regeneration, annotation-scale behavior, private style evaluation and complete
original-file interoperability are not claimed.
