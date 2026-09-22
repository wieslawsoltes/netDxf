# Dimension label scale, affixes and destination context

Dimension label generation applies DIMLFAC once, before DIMRND rounding.
WindowsDesktop formatting now uses the already scaled/rounded measurement,
not a second multiplication by the stored scale. Its decimal precision and
separator still come from the current thread culture; that culture is not
modified. Other unit conversion and rounding implementations are unchanged.

Negative DIMLFAC applies its absolute magnitude only with an explicit paper
layout owner. A modelspace owner, detached dimension or ordinary block
definition uses scale 1 for a negative setting. A block definition does not
infer paper context from an INSERT instance; one definition may have multiple
instances in different layouts. Angular dimensions remain independent of
DIMLFAC. This implements the existing DimScaleLinear property contract in
selected label paths, not native viewport/instance-dependent regeneration.

DimensionBlock uses DimPrefix for every supported family, while preserving
existing default R/diameter prefixes when no explicit prefix is supplied.
DimSuffix, explicit literal text, suppression and repeated <> replacements
retain their existing behavior. This does not implement complete alternate
unit/tolerance formatting or native fit/font/leader algorithms.

## Adoption context

During initial document adoption, the destination block is known before public
EntityObject.Owner is assigned. Dimension generation now receives that block
through an internal argument. All public Build signatures are unchanged and
use the dimension's existing owner. Internal generic dispatch shares the same
eight typed builder implementations. There is no temporary owner mutation,
thread-static context, active-layout switch or second geometry implementation.
Callbacks therefore retain their previous ownership timing; reentrant builds
of unrelated dimensions cannot inherit another dimension's context.

The existing two-argument internal adoption entry remains for callers such as
SECTION graph import. Its validation/publication behavior is unchanged. This
work does not make all adoption or Update operations transactional, and it does
not prevent arbitrary callbacks from changing application data.

## Qualification

The 304-case focused suite contains 160 scalar API/clone cases, 16 all-family
prefix cases, 32 all-family adoption/callback cases, and 96 wire cases. API
checks cover detached/model/paper/ordinary-block owners, positive/negative
scales, base styles/overrides, decimal/WindowsDesktop formats, two explicit
cultures and rounding on/off. Callback tests use both document entity addition
and direct paper-block addition while modelspace remains active. They check
initial ownership timing, the generated label, final ownership and a nested
detached build.

The 288 wire drawings cover six typed versions, text/binary source and output,
four placements and two explicit cultures. Each has eight aligned dimensions
with known 10.125 measurements. The independent checker verifies geometry,
exact style/override packets, block references, label strings and ownership,
with corruptions and complete-inventory controls. It audits without repairs.
The drawings establish stored-data and generated-string behavior, not native
font, layout, visual or AutoCAD execution equivalence.

An early focused run exposed the paper adoption-order defect after the scalar
fixes; production now passes the explicit destination. During independent
packet review, prefix-only override serialization was found to omit the
inherited suffix from combined DIMPOST, while the typed reader merges missing
parts. That separate pre-existing writer/reader issue is not corrected here.
Wire fixtures explicitly override both affixes so their complete DIMPOST
matches the generated label; direct API tests still exercise individual
prefix/suffix settings. Initial independent checker expectations also assumed
an explicit group67 in ordinary block records; the writer omits it there.
The final checker requires that established physical presence policy. Final
unchanged harness before/after counts and full CI evidence are recorded in the
PR, rather than inferred from test definitions.

Primary references: [Autodesk DIMSTYLE codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm)
(group144 DIMLFAC, group45 DIMRND, group3 DIMPOST, group277 DIMLUNIT) and the
existing `DimensionStyle.DimScaleLinear` API documentation. Historical typed
dialects/pre-R11 support, full FIELD/TABLE/private cache regeneration,
dependency-complete import, general version conversion and native AutoCAD
open/AUDIT/save/reopen remain separate work.
