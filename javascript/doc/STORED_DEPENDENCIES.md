# Retained FIELD, DIMASSOC, SUNSTUDY and binary diagnostics

Runtime checkpoint: `a7f4559822ad6e63cfb10ea01ad814fd25929f7c`.
Executable verification checkpoint: `887557749595490b0e75977686eb3f40c1b4620d`.
Executable tree: `36b8b13f725d85d67a55c93a0409b4129f60aa1e`.
Unchanged C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This checkpoint adds three original-path source mirrors and fixes an existing
binary-reader diagnostic defect. It does **not** implement a FIELD evaluator,
dimension regeneration, SUNSTUDY execution or complete typed DXF transport.

## FIELD source and ownership semantics

`netDxf/Objects/DxfStoredField.js` retains the full payload, evaluator identifier
and field-code string without executing those strings. The internal constructor
and Resolve method are explicit retained-loader adapters, not substitutes for
the missing typed reader's admission checks.

Semantic handle tags bind to exact source objects. Identity and arbitrary-handle
fields remain stored but do not become fabricated dependencies. Numeric handle
canonicalization preserves UInt64 parsing, including leading zeroes, overflow and
format errors. The source's leading object slots retain their separate null and
lookup-spelling behavior. Repeated references remain repeated; dependency use
counts therefore do not collapse to a set.

Leading FIELD children must have unique FIELD identities, reciprocal ownership
and no ancestor cycle. Validation rejects undeclared owned children while
exempting the actual extension dictionary. Payload/evaluator data is immutable;
Children, ReferencedObjects and References are live, read-only list views with
source-compatible enumerator invalidation.

Resolution is deliberately not an invented transaction: a failing callback can
leave already observed dependencies or children in the backing lists. Repeat
resolution and callback mutation follow the unchanged C# behavior. Source/profile,
registration and identity checks continue to reject stale or moved graphs.

## DIMASSOC identities and reactor backlinks

`netDxf/Objects/DxfStoredDimAssoc.js` includes the immutable
DxfStoredDimAssocPoint value model. Points copy Vector3 values and retain exact
binary64 parameters, including negative zero. Geometry binding is an explicit
internal-loader step rather than a mutable public point property.

DIMASSOC resolves an actual registered Dimension and graphical geometry objects,
retaining ordered/repeated references and partial state on failure. Its source
owner must be the reciprocal dimension extension dictionary with the exact
case-sensitive `ACAD_DIMASSOC` hard-owner entry.

The model separately snapshots the persistent-reactor sequence and whether each
source graphical entity did or did not contain the association backlink.
Validation detects changed ownership, source profile, target registration,
reactor ordering and backlink presence. This does not compute osnap positions,
re-evaluate associations or regenerate dimensions.

## SUNSTUDY retained projections

`netDxf/Objects/DxfStoredSunStudy.js` preserves its retained packet and exposes
qualified scalar values, decoded text, raw hour flags and role references.
Signed output/date/time numbers remain source values, not inferred application
enumerations or evaluated date ranges. Ordered and repeated references, null
slots and lexical handle spelling are preserved.

Resolution requires an actual source dictionary owner and records the registered
ancestry through the source document. Later ownership or reference-identity
changes are reported independently. Resolver failure and repeated resolution
retain the native partial-mutation behavior. Generic cloning and erasure of all
three source-bound models remain explicitly rejected by their existing schema
boundaries instead of inventing a private application lifecycle.

## Binary-reader diagnostic correction

Porting all original SunStudyProducerRawTests exposed a real existing defect:
the JavaScript binary reader rejected invalid data but omitted the native group
code and value byte address from the error. Four binary producer-rejection cases
initially failed while the other 22 cases passed.

BinaryCodeValueReader now captures its actual absolute stream position after
reading the group code and reports it for invalid hexadecimal handles, booleans
and nonfinite doubles. Modern and legacy binary group encodings and nonzero
stream origins are tested. The precise native diagnostic is retained, for example:

```text
Invalid hexadecimal handle (1 to 16 digits) value for group code 340 at byte address 24.
```

The fix neither accepts malformed input nor closes the caller's stream. It does
not claim all BinaryReader/System.IO behavior or nonseekable-stream parity.

## Independent tests and required integration

The new input-only corpus contains **738 scenarios / 5,566 operations**:
**218 retained-model scenarios / 5,046 operations** and **520 binary diagnostic
inputs / operations**. Separate C# and JavaScript adapters invoke the unchanged
native models and actual production port. Observations include exact scalar
bits, UTF-16 text, identities, ordered views, source errors, callback traces and
partial state. Native diagnostic messages are compared directly, not normalized.

The binary inputs span handle, double and Boolean group codes, invalid values,
legacy/modern group encoding, stream origins and a preceding valid record.
Retained-model inputs cover source profiles, leading FIELD children, DIMASSOC
masks/geometry/backlinks, SUNSTUDY ancestry, failures, repeated resolution,
metadata/removal guards and 32 deterministic randomized sequences.

The **26 complete original SunStudyProducerRawTests cases** retain every source
identity and assertion: producer manifest classifications, compressed and
uncompressed hashes, exact source bytes, disclosed carrier transformations,
raw transport normalization, error detail and caller stream ownership. These
are raw producer-fixture tests, **not typed SUNSTUDY Load/Save qualification**.
No original typed-reader/evaluator test was shortened to increase coverage.

The **32 added supplemental tests** are counted separately. The new category is
mandatory in run-qualification.mjs and verify.mjs, appended after all earlier
browser inputs, and exercised through the installed package. The hosted ownership
workflow retains its Windows replacement-host prerequisite and every previous
comparison and original test.

## Remaining parity work

Current ledger: **388/510 library mirrors (122 missing)**,
**60/193 original conformance-file mirrors (133 missing)** and
**2,946/35,309 original cases (32,363 missing)**. File presence is not exhaustive
member/signature or behavioral qualification.

Release retains 26 concrete-dimension, six LEADER, eight block, three coordinate
and 128 entity operation differences. Debug retains one cubic Bezier tangent
NaN-sign comparison and 22 unavailable native scenarios from original assertions:
six dimensions, twelve tolerances and four layout/viewports. Both casing stages
reject the local Debian globalization profile before pairwise comparison; their
precomputed pair counts are not executed-comparison counts. No casing table,
expectation, tolerance or failed-result allowlist was changed.

Typed DXF reading/writing and Load/Save/SaveAtomic integration, full version
conversion, remaining TABLE and private-schema APIs, field/association evaluation,
original tests/examples and broad platform/filesystem/performance acceptance are
still incomplete. Retained constructor/Resolve adapters do not qualify the typed
loader's schema admission. The raw transport API is not a typed-IO substitute.
No native AutoCAD open/AUDIT/save/reopen fidelity is claimed.

PR #98 remains draft. No original C#/fixture changes, merge, force push,
comparison removal, failure waiver or npm publication occurred.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/STORED_DEPENDENCIES.md). Current published scope is maintained in the [README](../README.md).
