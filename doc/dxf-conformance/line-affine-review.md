# LINE affine geometry review

This C# task corrects existing LINE transform and reverse operations. Its base
is merged PR #106, `cd2b4b2d0949eaa85ff517b438a1838f8fb25ffb`. It does not
replace the separate clone or SOLID/TRACE planar-transform tasks, and does not
change the JavaScript port or its pinned C# reference.

## Geometry and public contracts

Autodesk's [LINE DXF reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-FCEF5726-53AE-4C43-B4EA-C84EB8686A66.htm)
defines WCS start/end coordinates (10/20/30 and 11/21/31), optional thickness
(39), and extrusion direction (210/220/230). The independently maintained
[ezdxf LINE documentation](https://ezdxf.readthedocs.io/en/stable/dxfentities/line.html)
also distinguishes extrusion thickness from graphical lineweight.

For an affine map `p' = A p + t`, this implementation maps both WCS endpoints
and the physical signed extrusion vector `normal * thickness`. When `A normal`
is nonzero, the new normal points along that image and the new thickness is
`thickness * length(A normal)`. Reflection therefore changes the direction of
the normal while retaining the authored sign of thickness. A shear of the
extrusion is representable: unlike SOLID/TRACE, LINE does not require a shared
filled-plane normal perpendicular to its endpoint direction.

A singular map may collapse the extrusion to zero without invalidating a
representable WCS segment. In that case thickness becomes zero and the old
normal remains as unused metadata. Collapsed/coincident endpoints are allowed;
this is not a nondegeneracy validator for application-specific geometry.

Both existing `TransformBy(Matrix3, Vector3)` and the new LINE-specific
`TransformBy(Matrix4)` override validate every matrix component, translation
component and source coordinate/normal/thickness before publication. Matrix4
requires the exact bottom row `(0, 0, 0, 1)`; projective matrices are rejected
rather than silently truncated. Nonfinite inputs, an invalid zero stored
normal, overflow, nonzero coordinate/thickness underflow to zero and a nonzero
normal component that cannot survive normalization all reject without changing
LINE geometry or proxy bytes. These are deliberate conservative admission
contracts, not inferred native AutoCAD rounding or projective semantics.

All candidate geometry is computed before any state is published. Publication
uses the actual base-class stored normal, bypassing user-overridden virtual
Normal accessors, and does not invoke user callbacks. Subclasses overriding
TransformBy itself remain responsible for their own behavior. Thread-concurrent
mutation is not supported by this transaction.

Successful changed geometry clears the common proxy-graphics packet; exact
identity and unchanged geometry preserve it. `Reverse()` also clears the proxy
when it changes endpoint order, but a coincident-endpoint reversal retains the
packet. Neither operation regenerates a native graphics cache. Other drawing
objects that reference the LINE are retained, not automatically reevaluated.

```csharp
var line = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6))
{
    Normal = Vector3.UnitZ,
    Thickness = -2,
    ProxyGraphics = new byte[] { 1, 2, 3 }
};
line.TransformBy(Matrix3.Scale(2, 3, 4), Vector3.Zero);
// Thickness == -8; WCS endpoints are scaled; stale proxy is cleared.
```

## Numerical policy

Affine dot products are evaluated as exact dyadic sums of the supplied finite
binary64 inputs and rounded once, nearest with midpoint ties to even. This
avoids overflow in individually large products that cancel to a finite result.
The fixed three-product-plus-translation operation has bounded integer size
(about 4,200 bits), no input-dependent recursion or enumeration, and no general
rational GCD computation. It can cost more than ordinary multiply/add arithmetic;
no performance claim or native bit-for-bit equivalence is made.

Transformed normal components are scaled by a common binary exponent before
length/normalization, avoiding overflow/underflow of squared magnitudes. Signed
thickness is rescaled without an overflowing intermediate length. The square
root and normalization use ordinary binary64 operations; only the affine dot
products have an exact once-rounded contract. Signed zero is preserved by the
exact identity fast path; nonidentity arithmetic does not promise its sign.

## Qualification

The unchanged 1,653 added harness cases passed **130/1,653** against the old
production methods and **1,653/1,653** after correction. They cover 216 model
transforms, 1,296 wire scenarios, every Matrix3/Matrix4 element with NaN and both
infinities, invalid source values, projective bottom rows, bitwise rejection
rollback, reverse/identity behavior, singular maps, 1e-200/1e200 scales,
minimum subnormals, exact large-product cancellation, and a 512-scenario exact
arithmetic corpus within one harness case.

The independent checker audits **1,296 drawings / 2,592 LINE entities** across
R2000/R2004/R2007/R2010/R2013/R2018 in text and binary. It derives original and
transformed geometry independently, compares exact once-rounded endpoints,
checks physical signed extrusion, and compares the remaining ordered LINE
fields apart from handles/owners and the intentionally invalidated proxy.
Geometric tolerance is relative 4e-13, with only eight minimum subnormal units
allowed for an expected zero; no unit-sized absolute floor hides small-scale
errors. All drawings have zero independent audit errors and zero repairs.

The gate rejects **62,316 actual-record corruptions**, including missing LINEs
and reintroduced stale proxies. Python Fraction arithmetic additionally checks
**1,536 exact binary64 results in 512 scenarios** and rejects a one-ULP mutation
of each result. The manifest contains actual input matrices/points and actual
results, not production-derived expected coordinates. Missing and extra file
inventories were separately challenged and rejected.

The existing `common-data/api/isolation-geometry` case now explicitly checks
identity-cache preservation and changed-LINE invalidation, rather than expecting
stale proxy retention. Its setter/getter/clone isolation, unrelated metadata,
polyline behavior and oversized-buffer rejection checks remain enabled. A new
explicit replacement cache is supplied before testing failed setter rollback.

Complete-suite and remote qualification receipts are recorded on the associated
PR. Local .NET SDK 8.0.425 / runtime 8.0.31 execution targets net8.0; local Linux
results are not substitutes for Windows or netstandard2.0 CI execution.

```sh
DXF_TEST_FILTER=line-affine dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Debug
python tools/verify_line_affine_review.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

## Remaining boundaries

These mathematical fixtures and independent graph audits are not native
AutoCAD open/AUDIT/save/reopen or visual qualification. Historical typed
DXF dialects, private schemas, dependency-complete import, native FIELD/TABLE
regeneration and general document-version conversion remain separate work.
This task does not claim to audit or repair every other entity transform,
shared vector API, arbitrary subclass, or dependency cache.
