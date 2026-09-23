# Reconcile tolerance drafts without reverting qualified work

This merge integrates the two older C# tolerance drafts into the post-#198
source. It keeps the current formatter, typed reader, effective-style resolver,
MTEXT escaping, angular-unit semantics, primitive geometry and release pipelines.
It does not restore either draft's obsolete copies of those files.

## Preserved work and conflict resolutions

PR #188 at `cef4ee53421a48eb839acabb9c02a0078653a394` and PR #190 at
`7bfda2d663be9baace56e3378935a9dacc706157` remain parents in Git history. Their
complete original source remains available at those commits. Shared tolerance
fidelity work is already integrated through #185; later #187/#194/#195 fixes take
precedence where the earlier implementations conflict.

Every incoming draft test is retained under a distinct `draft188-` or `draft190-`
case-name prefix. Private helpers, output file stems and checker names are also
namespaced so the drafts neither collide with each other nor overwrite current
baseline fixtures. The two suites contain 464 cases each. Both independent
checkers are registered by the existing all-verifier discovery, without skipping
or replacing a previous checker. Their corpora contain 1,128 and 1,062 drawings.

Some incoming string expectations intentionally change to the established current
contract: radius/diameter prefixes belong inside the nominal alignment scope;
alternate affixes surround the nominal alternate value; exact zero deviations
retain the existing blank sign slot. The independent parser still requires the
precise upper/lower rows, decoded numeric content, alignment and height scope.
PR #188's SurveyorUnits case now asserts the existing public rejection and verifies
that the rejected assignment does not mutate the style, rather than expecting an
unsupported angular assignment to succeed. Its case identity remains present.
These are documented conflict resolutions, not a claim that every incoming test
line is unchanged. No existing post-#198 baseline assertion is removed or relaxed.

## Newly exposed lower-bound data loss

The restored #190 tests exposed a remaining serialization defect. A symmetric
base with upper 0.25 and inactive lower 0.125 writes native lower 0.25. A method-only
override switching to Deviation reactivates lower 0.125 in the typed model. The
preceding writer omitted that lower override because it only handled transitions
*to* symmetry. Reload then inherited 0.25 and changed the selected tolerance.

The writer now compares the effective lower allowance to the lower allowance
actually serialized by the base style, for any explicitly selected tolerance
component. It writes group 48 only when the inherited native value would differ
(or when the lower component was explicitly selected). This covers switching from
Symmetrical to Deviation, Limits or None as well as the existing transitions to
symmetry. Disabled tolerance settings keep their reactivated lower value for a
later edit. Unselected bounds remain inherited when representable; source styles
and sparse override dictionaries are never expanded or changed. Numeric equality
retains the established signed-zero policy; an explicitly selected lower field
still carries its exact stored bits.

No reader policy, public signature, version eligibility or formatter implementation
changes. Native equal-bound deviations still decode as Symmetrical because their
physical fields are identical. Same-application XData preservation from #179 stays
in force.

## Added transition qualification and pipelines

The new 96-case matrix spans all six typed versions, both transports, DIMENSION
and LEADER, and modelspace, paper layouts, referenced and unreferenced blocks.
Each document contains all four target modes and six signed/boundary-value pairs,
including subnormal and signed-zero inputs. Three save/load passes check the exact
minimal DSTYLE field set, effective lower bits, source non-mutation, handles,
clone isolation, unrelated application data and following LINE geometry.

The independent checker requires all 288 transition drawings / 6,912 hosts,
checks physical table/override values separately from independent typed reads,
and rejects corrupt packets and missing/extra inventories. The two restored
corpora retain their numeric/MTEXT/parser/graph checks. These synthetic drawings
are not native AutoCAD output.

The existing installed-package assertions now also round-trip a method-only
transition and require both the reactivated lower value and regenerated label.
This shared body is used by the ordinary consumer and all eight exact-assembly
runtime profiles. Build, conformance, runtime-evidence sealing and release
rehearsal remain required; no publication credential or permission is changed.
Actual before/after, full-suite and hosted results are recorded on the PR.

Initial unchanged-draft runs against the current library exposed both the
representational conflicts and the genuine missing-lower field. An initial new
packet-check helper incorrectly treated group-1070 values as identifiers; it was
corrected to consume each pair without removing any assertion. The same corrected
compiled transition harness is executed against old and new production libraries.
No incomplete run or corruption-control pass is counted as a positive fixture.

## Boundaries

This is stored-data and generated-text fidelity, not full AutoCAD parity.
Native open/AUDIT/save/reopen, font metrics/fit/leader/viewport behavior, historical
typed dialects/pre-R11, complete private FIELD/TABLE/cache regeneration,
transaction-wide rollback, dependency-complete import and general version
conversion remain separate work. JavaScript PR #98 is not incorporated while
its independent port/parity gates fail.

Primary schema: [Autodesk DIMSTYLE group codes](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm)
identifies DIMTP 47, DIMTM 48 and the DIMTOL/DIMLIM 71/72 flag pair. The enum
projection and sparse-presence behavior above are explicit library contracts,
not claims of native API equivalence.
