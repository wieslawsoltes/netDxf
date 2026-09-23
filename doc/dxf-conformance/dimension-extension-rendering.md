# Fixed-length dimension extension geometry

The existing ExtLineFixed/DIMFXLON and ExtLineFixedLength/DIMFXL settings now
participate in generated dimension blocks, not only stored style records.
Aligned, rotated/linear, two-line angular, three-point angular and arc-length
builders share a fixed-extension helper. Ordinary variable-length geometry
keeps its original path, except for the side-specific corrections below.

The fixed distance is measured from the dimension line toward the reference
origin. The overshoot (DIMEXE) is independent. The origin offset (DIMEXO) remains
a minimum gap, so a nearby origin can shorten the fixed portion. If that gap
consumes the whole segment, no inverted or zero-length extension is emitted.
Overall dimension scale applies once to all three distances; measurement scale
DIMLFAC does not affect geometry. A zero fixed length leaves only the overshoot
when there is room. Each side retains its suppression, linetype, color and
lineweight. Internal direction normalization uses exact zero rather than the
global geometric epsilon, and rejects nonfinite or overflowing active geometry.
This is not a promise that every upstream dimension calculation is independent
of MathHelper.Epsilon.

The effective-style copy now retains both fixed-length settings and applies
their overrides. An unrelated override no longer loses them. Explicit disabling
still returns to ordinary extension lengths without changing the source style.
No public API, serialized field, version eligibility or DXF parsing path changes.

The three angular/arc builders previously used ExtLine1Linetype on both sides.
They now use ExtLine2Linetype for the second extension. Three-point angular
geometry also determines inward/outward direction per reference point: two
references can have different radii, with the dimension arc between them.
Arc-length uses the same per-side decision without changing its radius model.

## Verification

The 517-case harness contains 240 direct/generic/clone cases, 232 whole-document
round trips and 45 invalid-active-length cases. Fixed/variable/disabled settings,
unrelated overrides, length and side-linetype overrides, overall scale,
minimum-gap clipping, zero fixed length, one/both-side suppression, inward,
outward, negative-offset and 37-degree rotated configurations are covered.
The 696 drawings contain 8,352 independently read dimensions. Six existing typed
profiles, text/binary input and output, modelspace, paper space, referenced and
unreferenced blocks are exercised. ARC_DIMENSION retains its R2004+ gate.

The independent checker inspects actual LINE endpoints, per-side attributes,
complete line inventories, base settings, sparse overrides, owners and following
entities, and requires graph audits without repairs. Endpoint comparisons use
an explicit 1e-9 absolute drawing-unit tolerance on these bounded fixtures;
stored style values and field presence are checked separately. Expected geometry
is derived from declared fixture origins and dimensions, not captured output.
Corrupted coordinates, attributes, omitted/extra segments and incomplete fixture
inventories must reject. The installed-package consumer also exercises fixed
extension regeneration with unrelated overrides.

An initial checker used 8 rather than the existing ARC_DIMENSION type value 7;
its family expectation was corrected and a physical type check was added.
The source, production type encoder and C# family tests were not changed to
accommodate that checker mistake. A separate discovered second-ray direction
error was corrected in production; the final tests require the geometric
per-ray direction rather than reproducing that old output.

Actual final local/hosted counts and before/after evidence are recorded in the
PR. All preceding test identities and independent verifiers remain registered.
These fixtures do not establish native AutoCAD activation of newer style
settings in old dialects, font/fit/collision behavior, or native open/AUDIT/save
acceptance. Existing typed IO fields in old profiles are not new version support.
Automatic oblique extension layout, general dimension-line suppression/fit,
viewport-dependent regeneration and private cache rebuilding remain separate.

Primary references: [Autodesk DIMFXL](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-LT/files/GUID-CC0F54D4-688B-4A14-9926-9840BCB30FCA.htm),
[DIMFXLON](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-LT/files/GUID-D0E2EA4A-4CA2-4286-9439-55A19726C166.htm), and
[extension-line control](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-LT/files/GUID-695722CD-A131-48DB-9AB8-162F0832FE04.htm).
