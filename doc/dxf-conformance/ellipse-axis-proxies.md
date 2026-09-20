# Ellipse axis changes invalidate stale proxy graphics

`Ellipse.SetAxis(axis1, axis2)` retains its existing positive-finite input
validation and sorted major/minor diameter semantics. Both values are validated
before any field changes. After successful assignment, a change in either
stored axis clears common proxy graphics. Bit-identical sorted axes preserve
existing proxies even when the two arguments are swapped. Invalid inputs
preserve previous geometry, metadata and proxy bytes. A one-ULP change counts
as a resize independently of MathHelper.Epsilon.

The former method stored new axes while retaining proxy graphics describing
the old geometry. This task fixes that cache-invalidation gap; it does not
regenerate proxies or rewrite private associations. Other ellipse properties
and their validation are unchanged. The affine transform code is untouched.
Clone initialization still retains valid source proxies; later clone resizing
does not affect its source.

An existing underflow-transform rejection test intentionally resizes its
fixture before checking transform rollback. It now asserts that resize cleared
its stale proxy and reattaches a synthetic proxy before the original rollback
assertion. The case and original rejection/assertions remain enabled.

Tests exercise sorted inputs, all affected fields, one-ULP changes, owned and
detached identity, invalid arguments in both positions, finite extremes,
no-ops, epsilon independence, clones and six modern typed DXF profiles in both
transports. The independent checker validates axes, ratio and proxy presence
in 72 drawings, including swapped no-op arguments, then confirms the loaded
entity geometry. Its ELLIPSE loader deliberately skips proxy graphics, so the
checker validates exact physical bytes and additionally uses ezdxf's general
proxy-packet extractor. It does not claim the loaded ELLIPSE object retains
those bytes. The checker preserves binary chunks before generic scalar casting
and decodes text chunks as strict hexadecimal, rather than stringifying bytes.
Corrupted axes, ratio and cache state must fail the positive validator.
These are synthetic checks, not native renderer qualification.

```sh
DXF_TEST_FILTER=ellipse-axis-proxy/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_axis_proxies.py artifacts/conformance
```

Final executed platform results are recorded in the task PR. No measured
old-versus-new .NET run is claimed when a local SDK is unavailable. Historical
typed loading, pre-R11 dialects, native AutoCAD open/AUDIT/save/reopen, private
FIELD/TABLE/cache regeneration, dependency-complete imports, general version
conversion and native font/visual equivalence remain separate work.
