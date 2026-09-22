# Complete DIMPOST and DIMAPOST pairs

## Corrected IO behavior

DIMPOST and DIMAPOST each store one combined prefix/measurement/suffix string.
The public API exposes the two components as independent overrides. When either
component is overridden, the writer now combines it with the effective other
component from the entity's current base style. An explicitly empty string clears
that component; it is not treated as an absent override. DIMENSION,
ARC_DIMENSION and LEADER use the same corrected collector.

Reading either stored field creates **both** component overrides, including empty
ones. Previously, the reader discarded empty components, allowing base-style
affixes to reappear after regeneration. The writer also omitted inherited
components when serializing a one-sided override. DIMAPOST is resolved from its
own alternate-unit components, independently of the primary-unit components.

The DIMSTYLE table and active-style header writers now choose the alternate
`[]` placeholder from `AlternateUnits.Prefix`, not `DimPrefix`. This fixes an
alternate prefix silently becoming a suffix when the primary prefix is empty.
The existing no-prefix/suffix-only spelling is otherwise unchanged.

## Sparse API versus complete stored fields

Saving does not add dictionary entries or change the base style. A sparse
in-memory prefix-only override remains prefix-only on that source entity.
However, its serialized DIMPOST contains the effective prefix **and** suffix.
Loading therefore materializes both overrides. Subsequent changes to the base
style no longer change either stored component unless the caller removes its
override. DXF cannot express an independently inherited half inside one complete
DIMPOST/DIMAPOST field. Code that counts individual affix overrides must account
for this intentional reader behavior.

Example: a base prefix `S:` and suffix `:END`, with an override prefix `O:`,
produce `O:<>:END`. Clearing both components produces `<>`; loading it retains two
explicit empty overrides instead of restoring `S:` and `:END`. Alternate units
follow the same rule with `[]`. An absent field still creates neither override.
The existing first-placeholder split and suffix-only parsing convention remain.
No new support for reserved placeholder sequences inside prefixes is implied.

## Regression and independent checks

The focused harness contains 1,001 cases: 424 multi-host round-trip cases,
192 table/header combinations, 384 explicitly authored raw-pair input cases,
and one live-inheritance/materialization case. Fifteen variants cover absent,
one-sided, clear-one, clear-both, combined, whitespace, Unicode, and additional
suffix-placeholder content. The matrix includes every implemented dimension
family plus LEADER, modelspace, a paper layout, a referenced block and an
unreferenced block. ARC_DIMENSION retains its R2004+ gate; other types cover all
six R2000-R2018 typed profiles and text/binary input/output.

Checks distinguish the authored sparse dictionary from the loaded complete
pair, preserve handles and base styles, exercise clone isolation and repeated
Update, and retain an unrelated scalar override and another application's
XData. Primary generated labels are checked against fixed geometric values.
Raw-pair tests independently replace actual stored payloads, including empty,
placeholder-only and suffix-only strings, then test both loaded components.

The independent checker requires 1,464 drawings: 1,272 source/output drawings
containing 19,080 dimension/leader records, plus 192 table/header drawings. It
inspects exact physical DSTYLE packets, table/header strings, owners, primary
labels and manual anchors, then loads them with ezdxf and audits without repairs.
It rejects modified/missing/duplicate/mistyped packets and incomplete/extra file
inventories. Unicode wire spelling and decoded typed values are checked separately.
Executed results and source/artifact hashes are recorded in the PR, not inferred
from these test definitions.

## Boundaries

No public API, version eligibility, raw parser, delimiter grammar or geometry
algorithm is changed. This is selected affix storage and primary-label fidelity,
not full alternate-unit/tolerance rendering, DIMTMOVE/fit/leader/font equivalence,
transactional saves/adoption/Update, preservation of arbitrary unrelated records
inside the same ACAD XData application, or private-cache regeneration. The tests
are synthetic; they do not establish native AutoCAD open/AUDIT/save/reopen,
historical typed dialects/pre-R11, dependency-complete imports, general document
version conversion or full all-version AutoCAD parity.

## Primary references

- [Autodesk DIMSTYLE DXF group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm): group 3 DIMPOST and group 4 DIMAPOST.
- [Independent ezdxf DIMSTYLE schema](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dimstyle.py).
- [Previously reproduced defect on PR #175](https://github.com/wieslawsoltes/netDxf/pull/175#issuecomment-5771878481).
