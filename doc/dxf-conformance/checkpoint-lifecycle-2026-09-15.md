# Stored objects and lifecycle checkpoint

The implementation snapshot is [0f924c892b94840cf85519d57837c3839c582a2e](https://github.com/wieslawsoltes/netDxf/commit/0f924c892b94840cf85519d57837c3839c582a2e), tree `55d3de29002c9f40d68499280ec737129731d082`, published in [PR #86](https://github.com/wieslawsoltes/netDxf/pull/86) against `netstandard`. Documentation follows that implementation commit. The [PR checks](https://github.com/wieslawsoltes/netDxf/pull/86/checks) qualify the final published head separately.

The previous increment, [PR #85](https://github.com/wieslawsoltes/netDxf/pull/85), merged as `7d1e92b9b8c7e84231000bb1ad4bdb47915250b6`. Its final Linux/Windows Debug/Release jobs passed 22,047 cases each, and its post-merge checks passed. All 829 files in its retained source archive matched the published tree. Its detailed scope remains in the [parallel module checkpoint](checkpoint-modules-2026-09-14.md).

## Delivered behavior

This increment adds 947 cases to that baseline:

| Area | Qualified behavior | Added cases |
| --- | --- | ---: |
| [LAYER_FILTER / OBJECT_PTR](layer-filter-pointer.md) | Ordered unresolved names and the published common-only pointer envelope | 62 |
| [SPATIAL_INDEX / VBA_PROJECT](stored-envelopes.md) | Exact finite timestamp and inert bytes with ordered chunks and admission limits | 140 |
| [LIGHTLIST](lightlist.md) | Explicit stored version, ordered LIGHT identities, duplicate references and independent names | 189 |
| [Terminal erasure](typed-object-erasure.md) | Owned closure, incoming-reference preflight, alias/extension teardown and terminal identity | 87 |
| [Declared TABLE ownership](table-ownership-prerequisites.md) | Qualified XRECORD-owned TABLECONTENT/TABLEGEOMETRY relationships | 35 |
| [Erasure / declared ownership](fourth-mixed-modules.md) | Terminal adoption and declared-descendant consistency | 7 |
| [Mixed lifecycle](fourth-mixed-modules.md) | Mapped copies, APPID rename, rejected erasure and surviving external resources | 12 |
| [APPID / retained XData](appid-xdata-lifecycle.md) | Callback-safe names, canonical identities, isolated bytes, carrier accounting and BLOCK/ENDBLK clones | 70 |
| [Native R2010 proxy input](common-entity-data.md) | Accept group 160 with strict length/allocation validation; retain canonical output codes | 146 |
| [Native MLEADER compatibility](mleader-native-inputs.md) | Optional entity/style markers, stored line-color presence, opaque private styles and exact native packets | 199 |

The stored-object modules participate in common metadata, CLASS validation, explicit graph-clone mappings and document lifecycle. LAYER_FILTER names remain strings rather than resolved layer references. OBJECT_PTR implements only its published common envelope. VBA_PROJECT bytes are inert and are never executed. LIGHTLIST requires R2007 or later for typed output; older records remain opaque.

`EraseOwnedTree` validates the complete qualified ownership closure and every exposed incoming reference before mutation. It leaves original handles on terminal objects and rejects reattachment. Dictionary name removal remains a separate unlink operation. The mixed corpus checks exact clone maps, semantic pointers, arbitrary group 320 values, repeated/null references, rejected and successful erasure, and exact external-resource identity.

The APPID work covers retained carriers outside the primary object registry, including attributes, layout viewport metadata and block end records. Block clones now copy ENDBLK XData independently, including binary data and cyclic registry relationships. Native R2010 group 160 proxy input preserves the complete byte cache; output continues to use group 92 through R2010 and group 160 from R2013.

Native MLEADER input can omit entity group 270, style group 179 and leader-line group 92. Their public effective defaults remain available while nullable stored properties preserve physical absence. Assigning a line's `Color` records an explicit group 92; assigning `StoredColor = null` clears it. Private MLEADERSTYLE payloads remain complete opaque records and cannot provide typed resources to a MULTILEADER.

## Executed evidence

| Check | Result |
| --- | --- |
| Full .NET 8 Debug conformance | 22,994 passed, zero failed; all names unique |
| Full .NET 8 Release conformance | 22,994 passed, zero failed; all names unique |
| Retained DXF artifacts, each configuration | 2,179 |
| Independent verifier scripts, each configuration | 73 passed, zero failed |
| Distinct DXFs opened by independent verifiers, each configuration | 2,078 |
| Release library targets | netstandard2.0, net471, net48, net6.0 and net8.0 compiled successfully |
| Additional API build | netstandard2.0 Debug compiled successfully |
| Python ledger/runner tests | 18 passed |
| Roslyn field audit | Self-test passed; 70 IO files, 616 IO methods and 226 model/header files |
| Coverage comparison | 244 scoped rows across nine profile columns |

All library targets report zero errors and retain 561 existing XML-documentation warnings per target. The runtime harness uses .NET 8. Compiling the other target frameworks does not establish execution on those frameworks. The production library has no new third-party dependency. ezdxf 1.4.4 and the syntax-audit tooling remain development dependencies.

Every checked-in independent verifier is mandatory. Missing files, failed semantic or tag checks, corruption-control failures and timeouts fail the runner. A separate read-only accounting hook records which DXF files each verifier opens. It does not modify the fixtures or gate results.

The expanded APPID gate compares 132 exact XData packets in 40 drawings. Proxy checks compare 44 copies of 22 pinned native caches, totaling 90,832 bytes, in addition to the existing decoded-command corpus. The new MLEADER gate requires 102 drawings and verifies the original 23 extracted records, 15 exact entity bodies in both transports, eight resource identities and the native extension closure. Fixture generation is reproducible across independent Python processes with different hash seeds. Full-source and extracted-packet checks have distinct documented scopes.

Independent adversarial reviews additionally reproduced 24 BLOCK/ENDBLK clone failures before the fix and passed all 24 afterward. The proxy review passed 156 probes against the fixed reader, including six native R2010 cases that failed before the fix. Forty MLEADER line-color probes verify stored absence, explicit defaults, nondefault values, clearing, cloning, wire output and duplicate rejection. These supplement the checked-in tests and final-head CI.

## Remaining boundaries

The [coverage ledger](coverage.json) and [generated comparison](version-feature-matrix.md) retain `full_standard_complete: false` and `autocad_executed: false`. Native AutoCAD open/AUDIT/save/reopen, rendering and regeneration, historical typed dialects and actual older-framework runtime execution remain unqualified.

TABLE entity preservation and literal grids, editable DATATABLE storage and LAYER_INDEX are being implemented in separate branches. This checkpoint contains TABLE ownership prerequisites; it does not claim complete TABLE/TABLESTYLE/TABLECONTENT/TABLEGEOMETRY schemas or cell evaluation. Further public work includes SECTION, FIELD/DIMASSOC, material/rendering and sun families, modern SAB/ACDSDATA and surfaces.

Other open areas include typed unknown entities, universal dependency import, hidden private references, full HATCH geometry/associativity, MESH overrides, internal VERTEX metadata, UCS base relationships and VPORT frozen-layer behavior. MULTILEADER annotation contexts, tolerance content, transformations and style evaluation remain outside the stored subset. Whole original ACadSharp drawings containing independent live-ACIS or other unsupported features are not certified by extracting and qualifying their MLEADER packets.
