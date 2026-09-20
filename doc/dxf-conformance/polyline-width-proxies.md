# Typed polyline width changes and cached graphics

`Polyline2D.ConstantWidth` now invalidates parent proxy graphics when the stored
value or presence changes. `SetConstantWidth` also invalidates after per-vertex
start/end widths or their presence changes, including when group 43 was already
absent. Both paths retain proxies for bit-identical definitions. Null and explicit
zero remain distinct; signed-zero and one-ULP changes are not hidden by epsilon.

The bulk method retains its previous policy: clear group 43, make both widths
explicit on every vertex, preserve vertex identities and keep legacy validation.
Width/source validation completes before any mutation; malformed late vertices
cannot cause partial publication. The implementation is factored into the existing
fidelity partial class. Clone initialization still copies valid source proxies
last; subsequent clone edits do not affect the source.

This is parent-cache invalidation, not proxy regeneration or automatic ownership
notification for arbitrary direct edits through Vertexes. Legacy child proxy
caches, private associations and constraints are not regenerated. No serializer,
version conversion, geometry transform or width-precedence rule is changed.

Tests exercise all nullable width transitions, signed zero, very small/large
finite values, no-ops, invalid inputs, a late null source vertex, clone isolation
and seven wire scenarios across six modern typed profiles and both transports.
The independent checker compares physical widths, coordinates and proxy bytes,
and checks them with an independent entity reader and proxy packet decoder.
Corrupted field/cache controls and inventory controls must reject. Existing tests
remain enabled. Executed counts are reported in the PR; no local C# run is claimed
without an available SDK. The fixtures are synthetic, not native producer evidence.

```sh
DXF_TEST_FILTER=polyline-width-proxy/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_polyline_width_proxies.py artifacts/conformance
```

Full historical typed support, private FIELD/TABLE regeneration, dependency-complete
import, general version conversion and native AutoCAD open/AUDIT/save/reopen remain
separate qualification work.
