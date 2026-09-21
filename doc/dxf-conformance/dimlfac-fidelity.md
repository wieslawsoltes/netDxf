# DIMLFAC scalar fidelity

`DimensionStyle.DimScaleLinear`, its corresponding `DimensionStyleOverride`,
and typed DIMSTYLE loading now use exact scalar admission rather than
`MathHelper.Epsilon`. A finite nonzero value is retained, including negative
values and finite subnormals. Previously the public setters rejected values
inside the geometric zero tolerance, and the reader silently replaced them
with 1. Raising the global tolerance to 100 could even reject a default style's
ordinary scale 1 during unrelated ELLIPSE document loading.

Both signs of zero still reject in public setters and DSTYLE overrides. An
explicit zero in a DIMSTYLE table record retains the existing compatibility
fallback to 1; an absent table field retains its default. NaN and infinities now
reject in the property, override and reader before storage. Rejected property
assignments leave the old value intact. This intentionally tightens the old
nonfinite admission; it does not change other dimension-style scalar policies.

The writer is unchanged: table group 144, the header derived from the active
style, and ACAD/DSTYLE identifier 144 with real group 1040 retain their ordinary
output paths. Public names, version gates, signed scale meaning, ownership,
style cloning and Debug/Release Load failure conventions remain unchanged.
Reading does not apply the scale to geometry and does not regenerate dimension
blocks or private caches. The API does not change MathHelper.Epsilon.

```csharp
var style = new DimensionStyle("MICRO") { DimScaleLinear = 1e-13 };
var paperSpaceOverride = new DimensionStyleOverride(
    DimensionStyleOverrideType.DimScaleLinear, -1e-13);
```

## Qualification

The added harness covers finite scales from the smallest subnormal through
Double.MaxValue, both signs, four tolerance settings, style/dimension clones,
invalid assignments, wrong override types, explicit/absent table defaults,
nonfinite physical input, and preserved zero-override refusal. The wire matrix
uses all six existing typed profiles, both input/output transports, modelspace
and referenced blocks. It requires 1,008 source/output drawings and checks
exact binary64 values, identities, following entities and database validation.
Dimension blocks are seeded with an explicit fixed user label at ordinary scale;
these fixtures verify stored settings, not automatic label/cache regeneration.

The independent checker requires that complete inventory, inspects actual
physical header/table/DSTYLE packets, loads them using ezdxf, audits graph
integrity and rejects missing/extra inventories and corrupted scalar packets.
Extreme-scale checks are not native AutoCAD or dimensional rendering evidence.
A separate 60-case matrix repeats the previously blocked whole-document
ELLIPSE cases at tolerance 100 across all six typed profiles and both
transports. The previous ELLIPSE codec tests remain registered and unchanged.
This selected matrix is not universal global-tolerance independence.

```sh
DXF_TEST_FILTER=dimlfac-fidelity/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_dimlfac_fidelity.py artifacts/conformance
```

Executed counts, failures and source-bound qualification are recorded in the
PR rather than inferred from these test definitions. The task does not add
historical typed dialects, pre-R11 support, full private FIELD/TABLE/cache
regeneration, dependency-complete imports, general document-version conversion,
or native AutoCAD open/AUDIT/save/reopen and font/visual equivalence.

## Primary references

- [Autodesk DIMSTYLE group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm)
  identify group 144 as DIMLFAC.
- [ezdxf DIMSTYLE schema](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dimstyle.py)
  provides an independent stored real-value reader. It is not a native renderer.
