# Single-traversal SPLINE fit-point construction

`Spline(IEnumerable<Vector3> fitPoints)` now snapshots the caller's sequence
once, before fitting, and retains that same owned snapshot as `FitPoints`.
The previous constructor enumerated once inside the Bézier fitter and again to
store fit points. Single-use input threw on the second pass; changing producers
could produce controls and fit points from different traversals.

The fitting algorithm, degree, knots, weights and creation method are unchanged.
Input arrays are copied, and null/short-input validation retains `fitPoints` as
the parameter name. Producer exceptions propagate and enumerators are disposed.
The fitter still works on a copy of the snapshot: no caller-owned array is
adopted. This does not add new fitting, tolerance, finite-input or resource-limit
semantics to the existing fitter.

The 31 focused regressions pass 7 before the correction and 31 afterward. They
cover one-use/changing/counting sequences, exact traversal/disposal counts,
producer failures, array isolation, stored/control agreement, clone, existing
sample equality and text/binary output in six typed profiles. The independent
checker validates 12 physical SPLINE packets: all five fit points and each
cubic span's endpoints. It rejects 540 fit-coordinate changes/omissions/
duplications and audits all files with ezdxf. This selected-field checker is not
a complete private-packet comparison or native AutoCAD fitting qualification.

Reproduce with `DXF_TEST_FILTER=spline-fit-input/ dotnet run --project
tests/netDxf.Conformance -c Debug`, then
`python tools/verify_spline_fit_input.py artifacts/conformance`.
Final complete-suite and hosted results are recorded against exact commits in
the PR. Existing historical-dialect, private graph, font, native AutoCAD and
version-conversion boundaries remain unchanged.

Autodesk identifies separate control (10/20/30) and fit-point (11/21/31) records:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm
