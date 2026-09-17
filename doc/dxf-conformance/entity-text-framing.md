# Preflight text-entity string framing

This C# task follows the separate MTEXT Unicode-chunk correction. It validates
TEXT and MTEXT content, ATTRIB values, ATTDEF values/prompts, and DIMENSION user
text before writer preparation or output begins. The registered block table
covers modelspace, paperspace and nested blocks without recursive INSERT walks.
Legacy linked MTEXT columns are ordinary registered MTEXTs and are also checked.

## Contract

Unpaired UTF-16 surrogates and actual NUL characters reject in both transports.
Literal CR or LF rejects in text DXF, whose group code and value occupy separate
physical lines. Binary CR/LF remains accepted: it does not terminate a binary
string. No native interpretation of those binary control characters is claimed.
Valid surrogate pairs, Unicode combining sequences and literal DXF escape or
formatting sequences retain the existing encoding behavior. The guard does not
interpret a literal `\U+hhhh` sequence as a character or normalize text.

The new check runs before any existing writer validation/preparation that could
add application registries, layouts or generated records. With malformed text,
Debug `Save` throws `InvalidDataException`; Release keeps the established API
behavior of returning false. Existing destination bytes and position, source
strings, object identities, block/layout identities and registry inventories
remain unchanged. Repairing the source value permits a subsequent save.

This is a save-time admission rule, not a change to in-memory setters. It is a
behavioral correction for callers that previously relied on silent replacement
characters or malformed physical records. For a paragraph use the supported
MTEXT formatting sequence `\P`, rather than a literal newline in text DXF.
No silent character substitution or broad escaping policy is introduced.

## Executed focused evidence

The same **1,730** regression cases pass **360 before** and **1,730 after**
enabling the preflight check: **1,370 existing failures** are corrected. They
exercise six text fields in direct and nested blocks, six typed DXF profiles,
both transports, seven malformed Unicode strings, NUL and text-only physical
line breaks. Positive cases retain valid Unicode and the prior binary CR/LF
admission. Rejection assertions include exact destination preservation and
unchanged source/document inventories; the two linked-column cases reject
before any prefix is written.

The positive DIMENSION fixtures explicitly enable `BuildDimensionBlocks`, the
existing opt-in geometry-block construction setting. The independent reader
then receives actual valid dimension geometry, rather than an intentionally
blockless dimension that its audit would remove.

`tools/verify_entity_text_framing.py` checks **360 emitted drawings** using an
independent physical-tag reader. It compares the selected text field exactly,
with explicit decoding of generated legacy non-ASCII Unicode escapes; the
input's literal `\U+0041` remains a literal sequence in this comparison. It
rejects **1,800 actual-packet mutations** and two inventory corruptions, and
all 360 drawings pass graph auditing with zero errors or repairs. This verifier
checks the valid output side, not the rejection/rollback paths, which are
exercised by the C# tests. It is not a whole-document byte comparator.

Run from the repository root:

```sh
DXF_TEST_FILTER=entity-text-framing/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_entity_text_framing.py artifacts/conformance
```

The final complete-suite receipts accompany the delivery and distinguish local
net8.0 execution from remote Linux/Windows and netstandard2.0 CI. The checker's
normal discovery includes the new script without changing the CI workflow.

## References and boundaries

The two-line ASCII group/value framing and group value types are described in
[ezdxf's DXF tags documentation](https://ezdxf.readthedocs.io/en/stable/dxfinternals/dxftags.html).
The actual binary writer uses NUL-terminated string values. The rejection policy
above is an explicit library safety contract, not an assertion that AutoCAD
uses these exception types or accepts/rejects identical malformed inputs.

Coverage is R2000/R2004/R2007/R2010/R2013/R2018, text and binary. This task does
not add historical typed dialects, all entity/private string fields, embedded
attribute MTEXT authoring, complete FIELD/TABLE cache regeneration, arbitrary
text layout or font engines. Existing raw-record preservation, STYLE and other
specialized validators are unchanged. No AutoCAD open/AUDIT/save/reopen, font,
visual equivalence or complete DXF parity is asserted.
