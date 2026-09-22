# DIMALTU operating-system format (code 8)

The existing public `LinearUnitType.WindowsDesktop` value is now encoded as
DIMALTU **8** in the active-style HEADER, DIMSTYLE group 273, and the shared
DIMENSION/ARC_DIMENSION/LEADER DSTYLE override packet. Typed table and override
loading decode 8 to that existing enum value. Primary DIMLUNIT remains **6**:
the two variables have different integer encodings.

Previously table code 8 became Scientific, override code 8 was ignored, and an
authored WindowsDesktop alternate format omitted a table value or left an
incomplete header/override field. The change adds the missing branch to each
existing codec; formats 1 through 7 and invalid-value fallback are unchanged.
No new public enum, signature, global state or alternate-label renderer is added.

Stacking is represented independently only for Architectural/Fractional modes.
WindowsDesktop with an inactive true StackUnits flag writes 8 and loads with
StackUnits false. Saving does not change that authored flag, the sparse override
dictionary or the base style. A present complete override materializes the
existing two typed components; an absent override continues to inherit.
This uses the grouped-setting preservation corrected in PR #177.

## Qualification

The 386-case focused harness contains 18 all-family cases, 96 table/header
mapping cases for every code 1 through 8, and 272 mixed-host round-trip cases.
The matrix uses all six typed versions, text/binary sources and outputs,
modelspace, paper space, referenced blocks and unreferenced blocks. ARC_DIMENSION
retains its R2004+ gate. Each mixed-host case contains 12 rows: absence,
stacking-only and units-only overrides, all eight format encodings, and inactive
stacking. Checks retain the distinct primary code, sparse source presence,
loaded complete presence, handles, cloning isolation, unrelated scalar and
other-application XData, repeated Update, primary literal labels and following
geometry. Alternate numeric label rendering is not inferred from fixed labels.

Half of the mixed-host inputs are independently patched code-1 seeds: they do
not depend on the newly added code-8 encoder to test the new reader. All inputs
and outputs form a required 1,104-drawing corpus. The independent ezdxf checker
validates physical headers, style fields, exact DSTYLE packets, loaded integer
values, ownership and graph integrity. Actual-packet mutations and missing/extra
inventory controls must reject. These synthetic fixtures are not AutoCAD output.

The final identical harness is measured against preceding and changed production
sources. Initial local after-change results were 384/386: two new LEADER fixtures
used the default Standard style, which document adoption correctly replaces
with the canonical existing Standard style. Giving those fixtures an explicit
named style fixes setup; every assertion and the production patch remain.
Executed final/full/hosted results are recorded in the PR and are not implied
merely by the test definitions.

## Boundaries and primary references

This qualifies stored mode support, not native operating-system locale/grouping
behavior, alternate/tolerance text formatting, fit/leader/font rendering, arbitrary
same-ACAD-application XData, whole-operation rollback or private-cache regeneration.
Undefined enum admission and general malformed-input handling are unchanged.
Historical typed dialects, pre-R11, dependency-complete imports, general version
conversion and native AutoCAD open/AUDIT/save/reopen remain separate work.

- [Autodesk HEADER DXF schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm) lists all eight DIMALTU values and the distinct DIMLUNIT code 6.
- [Autodesk DIMSTYLE DXF schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm) identifies group 273.
