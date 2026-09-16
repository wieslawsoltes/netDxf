# Legacy feature review: units, dates, colors and conic geometry

This review deliberately broadens the work beyond the preceding TABLE modules.
It examines existing numerical utilities, common serialization and geometry
operations, preserves their public entry points, and adds regression and
independent output checks. It does not claim that all older APIs have now been
reviewed or that every DXF family is complete.

## Findings and implemented changes

| Reviewed area | Defect or unsupported boundary found | Implemented contract |
|---|---|---|
| Linear unit text | Int-sized whole parts, inconsistent negative fractions, rounding into 12 inches, culture-dependent MTEXT scale strings | Bounded precision, exact binary64 rounding, normalized carries, one sign, invariant fraction controls |
| Angular unit text | DMS components could round to 60 without carrying; no general angular dispatcher or surveyor bearing helper | Round before decomposition, explicit angle-unit dispatch, normalized quadrant bearings |
| DXF calendar/elapsed values | Noon-style calendar arithmetic, early/final-year bounds and subsecond truncation | Midnight-based DXF day serials, Gregorian range checks and tick-rounded elapsed values |
| Public ACI palette and RGB/HSL | Writable nested palette arrays could change shared color lookup; nonfinite values were admitted; packing relied on byte order | Deep palette snapshots, finite admission, explicit endian-independent packing |
| Ordinary RGB serialization | Generated group 420 contained the AcCmColor high-byte marker and appeared in R2000 | Untagged 24-bit RGB in R2004+, existing ACI fallback in R2000 |
| ARC/CIRCLE transforms | Diagonal matrix checks missed actual in-plane distortion; reflected/sheared normals, endpoints and thickness could disagree | Actual plane-basis qualification, oriented normal, signed extrusion and atomic rejection |
| ELLIPSE transforms | Five-point reconstruction depended on center magnitude; normal and arc endpoint calculation used incorrect state | Center-independent scaled axis factorization, transformed-plane normal and preserved directed arc image |
| ELLIPSE sampling | Multiplication/squaring of large or small axes overflowed or underflowed | Scaled polar-point evaluation without multiplying the semi-axes |
| Independent date verification | High-level ezdxf loading rewrites TDCREATE metadata | Inspect the original physical HEADER tags, including one-ULP corruption controls |

The last finding is a verifier defect, not evidence that the original drawing
contains today's creation time. Keeping exact date assertions against the
physical file exposed the separate real R2000/RGB serialization defect. Neither
check was disabled to obtain a passing workflow.

## Legacy linear and angular formatting

`LinearUnitFormat` and `AngleUnitFormat` remain separate from the newer
`DxfValueFormat` compiler. Their fixed/fractional utility contract rounds the
exact finite binary64 input once, with midpoint ties to even. `DxfValueFormat`
retains its separately documented ties-away-from-zero contract. Scientific
legacy output still uses the .NET numeric formatter. No universal equivalence
between all native formatter options is inferred from these choices.

Precision is explicitly limited to 0–8. Engineering and architectural lengths
are interpreted as inches. Whole values use BigInteger during decomposition,
so large representable inputs are not truncated to Int32. Rounded inches carry
into feet; DMS seconds and minutes carry before output. Negative results have
one leading sign, and a rounded zero does not retain a minus sign. Fractions
are reduced; MTEXT fraction height-scale syntax uses the invariant decimal
separator even when the host culture uses commas.

```csharp
var format = new UnitStyleFormat { AngularDecimalPlaces = 4 };
string dms = AngleUnitFormat.ToDegreesMinutesSeconds(12.9999999, format);
string bearing = AngleUnitFormat.ToSurveyor(135, format);
string radians = AngleUnitFormat.Format(90, AngleUnitType.Radians, format);
```

Angles supplied to these helpers are in degrees. `Format(..., normalize: true)`
reduces the input before rounding; ordinary signed formatting does not otherwise
normalize. Surveyor output always normalizes, selects N/S and E/W explicitly,
and measures from north or south. Exact-axis spellings, spacing and suppression
are documented utility policies, not automatic inference of a document's
ANGBASE, ANGDIR, UNITMODE, DIMSTYLE or FIELD settings. Finite input/range/format
validation occurs before output. Unsupported enum values and nonfinite
conversion results reject.

## DXF clock and elapsed time

The [Autodesk date/time DXF reference](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-6942BAF3-095F-4217-9F61-6931975D3A64.htm)
specifies a day number plus a fraction measured from midnight, with elapsed
variables expressed as days plus a fraction. Its 1999-12-31 21:58:35 example and
the [DATE examples](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Core/files/GUID-CBB24068-1654-4753-BE2E-1D0CE9700411.htm)
anchor the conversion. This API is a DXF civil-clock serial conversion, not a
general astronomical Julian-date or timezone converter.

The declared utility range is the proleptic Gregorian DateTime range, years
1–9999. Dates near the final day are admitted by the reader instead of rejected
by an inclusive integer-day upper bound. Binary64 serials cannot retain every
100-nanosecond tick over this range: conversion rounds the fractional day, and
DateTime.MaxValue output is bounded to the last representable serial below the
following midnight. Tests account for that actual day-serial resolution rather
than promising lossless DateTime ticks. Elapsed values support signed durations
and nearest-even tick rounding, with explicit overflow/nonfinite rejection.
Local/UTC labels are not silently converted by arithmetic; source clock semantics
remain the caller's responsibility. Tests of early Gregorian years are a public
utility contract, not native AutoCAD qualification of ancient drawing dates.

## Color isolation and wire representations

`AciColor.IndexRgb` returns a read-only dictionary containing independent RGB
arrays. Mutating an array obtained from that property no longer changes global
palette lookup or another color's result. Code that intentionally relied on
modifying the process-global palette through this property must stop doing so.
Palette values and indices themselves are not changed. RGB/HSL floating-point
factories reject NaN and infinities; byte packing is explicit and independent
of machine endianness.

There are two intentionally different color representations:

* The existing public `AciColor.ToTrueColor` packed value retains its
  `0xC2RRGGBB` AcCmColor marker for existing consumers and schemas.
* Generated ordinary DXF group **420** contains **0x00RRGGBB**, as required by
  [Autodesk's common entity codes](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm).

Ordinary true color is a [R2004+ DXF feature](https://ezdxf.readthedocs.io/en/stable/concepts/true_color.html).
R2000 generated output therefore keeps the existing ACI approximation and omits
420 without mutating the source color object. The shared writer helper applies
to layer/entity colors, legacy POLYLINE and vertex colors, ATTDEF/ATTRIB, and
MLINESTYLE fill/element colors. Explicit recoloring of a retained polyface face
uses the same rule. Unedited retained packets still keep their storage contract.
Private/opaque replay, tagged values in other group codes, native color-book
names and other object-specific color schemas are not normalized by this fix.
This is not a general color-management, ICC or arbitrary private-color decoder.

## ARC and CIRCLE affine behavior

A transformed circle is still representable as ARC/CIRCLE only when its actual
two plane axes remain orthogonal and equally scaled. The new preparation helper
checks those vectors with a relative 1e-12 qualification tolerance rather than
comparing matrix diagonal entries. Uniform plane scaling, axial scaling,
rotations, reflections and an unaffected circular plane under some singular
3D matrices can be represented. Actual in-plane nonuniform scaling or shear
rejects; use an ellipse representation for that geometry.

The oriented normal is computed from the transformed plane axes. Arc start/end
angles preserve the directed image under reflection. A nonzero signed thickness
must still be parallel to the resulting normal; an oblique extrusion cannot be
represented by that field and rejects. Finite coordinates, positive radius and
representable output are checked before any state changes. Four-by-four input
must be affine, not projective. Successful changed geometry invalidates stale
proxy bytes; identity operations retain them. Hidden-state conversion behavior
is preserved when generating polylines or related geometry.

## ELLIPSE affine behavior

An ellipse uses a WCS center and major-axis vector, a plane normal, axis ratio
and start/end parameters in the [published DXF representation](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm).
The public netDxf API continues to expose full axis lengths and polar endpoint
angles; the existing writer translates them into the DXF parameters.

The revised transform takes the images of the two original parametric semi-axis
vectors. A scaled two-by-two Gram eigensystem determines a principal direction;
cross-product area divided by the major length determines the minor length
without cancellation from subtracting nearly equal eigenvalues. Translation
is handled separately, so a very large center does not corrupt modest axis
lengths. The cross product supplies the oriented image-plane normal rather
than incorrectly treating a normal as a general position vector.

```csharp
var ellipse = new Ellipse(new Vector3(3, -4, 5), 8, 3)
{
    Rotation = 37,
    StartAngle = 25,
    EndAngle = 285
};
ellipse.TransformBy(
    new Matrix3(1, .75, 0, .2, 1, 0, 0, 0, 1),
    new Vector3(11, -7, 13));
```

Full ellipses and partial directed arcs retain their affine image under
nonuniform scaling, shear and reflection when the result is representable.
Original endpoints are evaluated before new axes are published. Translation-
only operations preserve axis/angle values; exact identities retain proxy data.
Zero/rank-one images, nonfinite data, projective matrices, output overflow or
underflow and indistinguishable arc endpoints reject atomically rather than
silently leaving a partially transformed object or clamping it to an epsilon
ellipse. The finite-precision admission boundary is explicit, not an arbitrary
precision guarantee for every ill-conditioned matrix.

`PolarCoordinateRelativeToCenter` avoids multiplying/squaring unscaled axes and
supports the tested very small/large semi-axes. Constructor/SetAxis inputs must
be finite and positive. `ToPolyline2D` now carries the source visibility. The
existing `Ellipse.Thickness` property remains an in-memory/polyline convenience;
this work does not invent a standard ELLIPSE thickness field in DXF. Its affine
vector is transformed only when representable by normal plus signed thickness.

## Verification and compatibility

The full conformance harness continues to execute every prior test. New tests
exercise all six typed source profiles through text and binary saves/reloads,
culture variants, immutable palette behavior, malformed values, atomic transform
rejection, retained proxy semantics, and preservation of world-space geometry.
The reviewed implementations change formerly incorrect output deliberately;
callers depending on overflowing unit text, writable global palette arrays or
silently accepted unrepresentable transforms may need to adapt.

Independent checks are not fed an emitted expected-coordinate manifest:

| Gate | Independently derived expectations |
|---|---|
| `verify_legacy_utilities.py` | Python Fraction nearest-even arithmetic for 4,320 unit strings; Gregorian arithmetic for 75 clock serials; 14 elapsed values; original HEADER tags and 12 drawings |
| `verify_circular_geometry_review.py` | Original parameterized ARC/CIRCLE world-space images in 96 drawings, including extrusion vectors |
| `verify_ellipse_affine_review.py` | Original full ellipse loci and directed arc samples transformed in world coordinates across 120 drawings |
| `verify_true_color_review.py` | Exact generated 420/ACI field and record inventories across nine record kinds in 12 drawings |

Each gate challenges actual parsed output fields through the same positive
validator. The date gate perturbs stored doubles by one ULP, rather than merely
changing already normalized high-level reader metadata. Missing and extra
fixture inventories reject. Every new drawing is independently audited without
repairs. Geometry tolerances are explicit in each script; this is numerical
and file-structure qualification, not pixel/font or native AutoCAD certification.

```sh
DXF_TEST_FILTER=ellipse-review/ dotnet run --project tests/netDxf.Conformance -c Debug
DXF_TEST_FILTER=true-color-review/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_legacy_utilities.py artifacts/conformance
python tools/verify_circular_geometry_review.py artifacts/conformance
python tools/verify_ellipse_affine_review.py artifacts/conformance
python tools/verify_true_color_review.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

Measured final counts, exact source/commit/tree and downloaded artifact digests
are recorded in PR #104. The recovered draft's green C# jobs did not substitute
for a passing independent verifier. A checksum-locked isolated source-transfer
job applies changes to large legacy files; its temporary workflow and transport
files are not part of the reviewed source ancestry. Normal conformance CI keeps
its original read-only permissions.

## Audit boundary and remaining work

This review covers the named utility, color and conic families, not every
remaining entity or object method. It does not assert that mesh/solid/text/
dimension transforms, all collection semantics or all private schemas have been
newly exhaustively reviewed. Their existing regression gates remain active.
The historical [297-row comparison](version-feature-matrix.md) stays a pinned
prior checkpoint; this note supplies current scoped improvements without
promoting broad families to complete.

The TABLE/FIELD work from previous increments remains guarded: persistent native
FIELD graphs, date/angle FIELD-expression decoding, automatic native style
precedence, duplicated-format synchronization, full coordinated cache/layout
regeneration, complete modern/private TABLE interpretation, recursive dependency
import and general document-version conversion are not completed by these
utility and geometry fixes. Native AutoCAD open/AUDIT/save/reopen, installed-font
metrics and visual qualification remain unexecuted. Native interoperability is
not inferred solely from passing library tests and independent DXF audits.
