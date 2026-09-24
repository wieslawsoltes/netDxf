# WIPEOUT affine clipping geometry

Both transform entry points now use the existing PlanarEntityTransform helper
shared with SOLID, TRACE and planar polylines. The plane derives from its two
transformed axes, not A * Normal. All four corners of a rectangle are mapped.
It stays rectangular only when the image is exactly axis-aligned in the new
OCS; otherwise it becomes a polygon. No approximate rectangle snapping is used.
Nonfinite/projective transforms and numerically collapsed planes reject before
publication. Identity preserves boundary identity and geometry. Normal staging
uses the base accessor; valid Matrix4 input retains virtual Matrix3 dispatch.

Changed geometry, elevation or clipping-boundary reference clears proxy graphics.
Same-value/reference assignments and rejected edits preserve cache state. Clones
own independent boundaries and cache bytes. Deep mutation through a backing-list
cast is not observed, nor are proxy/private graphics regenerated.

## Raster basis on input

The preceding reader averaged the insertion-point elevation with two direction
vectors (dividing canonical elevation by three), then used one projected U
component for both axes. Loading now uses actual WCS U/V vectors and the top-left
pixel origin with image height. Unequal, rotated, reflected and skewed bases
are supported. A repeated polygon endpoint is removed only when present; an
unclosed polygon retains its final corner. Nonpositive/nonfinite image height,
unknown boundary types and rectangular boundaries without exactly two corners
reject. Other declared-count and repeated-field policies remain unchanged.
Output uses the existing canonical encoder, preserving geometry rather than
source raster-basis spelling. No native draw-order or masking behavior is claimed.

## Qualification and limits

656 focused cases include 96 document matrices and 108 independent raster-basis
matrices. All six typed profiles, both source/output transports, three planes,
nine maps and four containers are covered. The complete corpus has 612 drawings
and 8,100 WIPEOUT records. The independent checker derives its expected vertices
from input frames/matrices, validates cyclic edge order through actual packets
and ezdxf boundary_path_wcs, and checks proxy framing, ownership and graph audits.
WCS comparisons use 2e-12 relative / 1e-10 absolute bounds. Corruptions, duplicate
fields, wrong topology and missing/extra inventories must reject. Package-smoke
assertions run in the existing ordinary and eight-runtime paths; no workflow is added.

The transform-only candidate passed 452/548 cases; all 96 wire failures exposed
the reader defect. Production was corrected without removing assertions, then
108 independent-basis cases were added. Final execution details belong to the PR.

The shared helper's near-rank-one threshold (1e-14 after axis normalization) and
finite-intermediate requirements remain: this is not exact affine arithmetic or
universal subnormal/overflow handling. Other no-change comparisons follow that
helper's numeric equality. Arbitrary callbacks, document transactions, default
absent clipping boundaries, exhaustive malformed input and native private raster
metadata are not qualified. Full AutoCAD parity is not established.

Primary references:
- Autodesk WIPEOUT fields: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-2229F9C4-3C80-4C67-9EDA-45ED684808DC.htm
- Independent boundary transform: https://ezdxf.readthedocs.io/en/stable/dxfentities/image.html#ezdxf.entities.Image.boundary_path_wcs
