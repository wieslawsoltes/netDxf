# Angular allowance units and semantic tolerance stacks

The tolerance formatter now distinguishes the degree-valued angular measurement
from DIMTP/DIMTM allowances, which are already in the selected DIMAUNIT. For
radian and gradian limits it converts the nominal angle before adding/subtracting
allowances. Deviation and symmetric values are formatted in those selected units
without another degree conversion. Decimal degrees and DMS retain the existing
formatters, precision and symbols. DIMLFAC remains irrelevant to angular values.

For a 90-degree angle, radian limits +0.25/-0.125 at precision three now generate
`1.821r` above `1.446r`, not the degree-offset values converted afterwards. The
stored geometry, native fields, sparse overrides and their inheritance are not
changed. SurveyorUnits still has its existing public rejection.

## MTEXT escaping

Stack rows use MTEXT backslash escapes instead of Unicode commands. A fractional
row must not accidentally become the stack divider, and a semicolon must not
terminate the stack early. Escaping also preserves hash, braces, backslash and
caret decimal separators. Caret decoding precedes stack parsing, so a literal
caret is emitted with its required space terminator in addition to escaping it.
For example, a deviation emits `\S+0 1\/2^ -0 1\/4;`. The outer `^ ` remains the
only stack separator. Scope restores the text height before subsequent content.

The previous Unicode-command form did not survive typed loading unchanged and
was not semantically correct when interpreted by an independent MTEXT parser.
This correction is in the existing shared builder, not an alternative renderer.
None/literal/suppressed labels, nominal fraction formatting, version eligibility
and all public signatures are retained.

## Regression and CI coverage

The 211-case suite adds 72 selected-unit angular cases, 24 six-family fractional
cases, seven custom separator cases and 108 version/transport/placement cases.
Angular tests check both angular families, four supported unit modes, symmetric/
deviation/limits methods, base styles, unrelated overrides and actual angular/
tolerance overrides. Cloning, source state, repeated placeholders, suppression,
regeneration, handles, unrelated XData and following geometry remain checked.

The 324-drawing corpus contains 9,396 dimensions in modelspace, paper layouts
and referenced blocks, using all six existing typed profiles and both source
and output transports. The independent checker derives expected values from
fixed input geometry, parses real MTEXT STACK tokens and their context, and
checks the actual upper/lower rows and relative heights. It also verifies raw
packets and independent DXF loads, ownership, style fields and complete inventory.
Parser-specific negative controls do not rely on exact output-string matching.
The existing all-verifier CI runner automatically discovers the added checker.
The installed-package consumer exercises radian limits and fractional reloads
inside every existing version/transport scenario, without a ProjectReference.

The identical compiled suite was tested against the preceding and changed
production assemblies. Measured results, final source identity and any unexecuted
hosted/native checks are recorded separately in the qualification report. The
baseline tests and checkers are retained unchanged. No duplicate build or release
workflow, publication credential or repository permission is introduced.

## Scope and references

The open PR #190 separately identified the angular/stack concerns; its alternative
implementation and tests are preserved on its branch. This focused correction
was developed on merged PR #192 and then integrated with merged PR #193
(commit f90699e9d8e5f0f1b306d7fb73d48031fd77bda7). All incoming circle/arc
implementation, registrations and installed-package assertions remain intact.
It does not merge or discard the other open drafts. Literal caret handling adds a two-pass grammar check beyond
the simple escaped-slash example. No native font metrics, fitting, printed output,
private-cache regeneration, historical dialects or AutoCAD execution is qualified.

- [ezdxf angular renderer, v1.4.4](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/render/dim_curved.py): selected-unit angular tolerance conversion; an independent implementation, not native producer evidence.
- [ezdxf MTEXT internals](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mtext.html): stack delimiters, backslash escapes, caret decoding and scope.
- [Autodesk DIMSTYLE codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm): DIMAUNIT, DIMTP/DIMTM and tolerance precision fields.

```sh
DXF_TEST_FILTER=tolerance-unit-semantic/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_tolerance_unit_semantics.py artifacts/conformance
```

## Recovery and current-main integration

The previously unpublished six-file checkpoint is preserved exactly on branch
`codex/recover-tolerance-unit-semantics-20260923` (source tree
`41deb7afe1a389c636b2ed96dd1579ec4e82736d`). The integration is based on merged
PR #194 at `3216f397dc0cd780645ced41ce7754bbcefa2746`. That PR independently fixed
stack escaping. Its production escape helper, 107 test identities, independent
checker and installed-package assertions are retained, not replaced by the
recovered equivalent implementation. The 211 recovered semantic cases and the
new radian-limit package checks are added alongside them. Execution counts for
the integrated source are distinct from the earlier standalone checkpoint.
