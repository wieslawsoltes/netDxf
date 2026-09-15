# Eleventh mixed graph qualification

The integration suite combines stored TABLESTYLE and CELLSTYLEMAP edits with
actual retained legacy 2D POLYLINE children and periodic HATCH conversion. Its
two graph bases are the pinned `acad_table_simple.dxf` (R2013) and
`acad_table_with_blk_ref.dxf` (R2018) producer drawings already used by the tenth
mixed suite. Each graph runs with text/binary input and text/binary output.

The fixtures are deliberately augmented. Native TABLESTYLE rows initially
reference reserved STANDARD, so the setup creates a named clone of that resource
and explicitly binds the loaded row names to the clone. This enables a resource
rename test without changing the reserved-resource contract. The native table
and map packets remain the basis of the test; the full fixture is not presented
as unmodified native producer output.

## Integrated behavior

- Clone a plain pinned producer legacy 2D chain into the matching native TABLE
  document. Bind TABLEGEOMETRY to an actual VERTEX record, then change widths and
  an identifier, transform and reverse the chain without replacing its children
  or its SEQEND identity.
- Rename CELLSTYLEMAP entries with Unicode and literal DXF escape text while
  retaining their identifiers, types, formatting and dependencies. Edit a
  recognized TABLESTYLE row and, when recognized, its classic header. Rename its
  bound STYLE resource while preserving older packet and row snapshots.
- Convert a rational cyclic HATCH packet on an oblique plane after a shear and
  translation. Associate its generated SPLINE and compare sampled world geometry
  before and after serialization. Retain and independently check the complete
  HATCH packet, including its rational flag, supplied knots, weights, populated
  fit points and both tangent vectors.
- Connect retained-child XData to the HATCH, generated SPLINE and SECTION_MANAGER.
  Verify removal protection, release the TABLEGEOMETRY reference, detach the
  actual legacy chain and retire the remaining graph without resurrecting handles.
  Recheck the exact ordered XData packet and blocked manager removal after reload.
- Reject callback attempts to erase the protected manager during map/style
  editing, unsupported periodic overlap during associative conversion and an
  incompatible source-profile save. The affected packets, child identities,
  associations, reactors, registration and handle seed remain unchanged. A valid
  retry then succeeds.

## Validation

Debug and Release each pass all 16 integration cases. Each configuration emits
24 DXFs: eight edited graphs, eight released graphs and eight successful retries.
`tools/verify_eleventh_mixed.py` checks all 24 with ezdxf 1.4.4. It independently
checks the actual on-wire reference graph, transformed original periodic control
points and 256 BSpline evaluation samples, including inherited/explicit legacy
width state and the retained edited identifier. All drawings have zero audit
errors and zero audit repairs. Sixty actual-output corruption controls
are rejected per configuration. The maximum observed world-coordinate error is
`9.607118501968132e-15`.

The expanded gate also compares the complete edited TABLESTYLE header. Independent
review identified earlier gaps covering HATCH knots, weights and the rational flag,
TABLESTYLE description/margins and the retained child's manager reference. Those
actual-output mutations are now explicit rejection controls, along with fit-point
and tangent corruption. No production-code failure was established by that review.

Reproduce from the repository root after building the conformance executable:

```sh
DXF_TEST_FILTER=eleventh-mixed DXF_TEST_ARTIFACTS=artifacts/eleventh-mixed \
  dotnet tests/netDxf.Conformance/bin/Debug/net8.0/netDxf.Conformance.dll
python tools/verify_eleventh_mixed.py artifacts/eleventh-mixed
```

The JSON receipts retain the exact scoped case inventories and independent
results. These checks establish stored-data and geometric integration; they do
not establish native CAD execution, visual table regeneration, formula/layout
evaluation or support for otherwise unqualified private TABLE fields.
