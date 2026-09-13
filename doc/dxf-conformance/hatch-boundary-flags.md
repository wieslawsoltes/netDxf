# HATCH boundary classification fidelity (group 92)

Baseline: `c9f886c553c2f8469c526ce7e9cdeaf4292c3fa7`, after merged PR #46.

## Corrected losses

The reader unconditionally added External and Derived bits to every group-92 value. Path cloning rebuilt those constructor defaults, losing Textbox/Outermost and other supplied classification bits. TransformBy likewise recreated default-classified paths. Separately, an associative contour update could keep a stale Polyline bit after replacing a closed polyline with open line/arc edges, making the flag inconsistent with the serialized payload.

Read group 92 without adding flags. Clone the stored classification. TransformBy preserves all nonstructural bits and uses the Polyline bit of the resulting edge representation. Update clears that structural bit when rebuilding edges, then sets it only if the new representation contains a closed polyline. Open-polyline transformation retains the existing conversion to separate edges; it does not add a closing segment.

Newly constructed paths retain their existing defaults (External/Derived, with Polyline inferred). No new public PathType setter or contour-copy policy is introduced. Unknown nonstructural bits are retained, not interpreted or certified as historically legal. No containment calculation, island detection, boundary repair or hatch renderer is added.

| Operation | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Read/write exact group-92 bit field | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Clone and nested INSERT preservation | Tested | Tested | Tested | Tested | Tested | Tested |
| Transform classification with representation-aware Polyline bit | Tested | Tested | Tested | Tested | Tested | Tested |

The removed reader override was a historical workaround mentioning groups 47/98; preserving field values now takes priority over silently rewriting classification. The recent pixel/seed/parser fixes have their own tests. Independent CAD fixture agreement below is not evidence of every native AutoCAD release's behavior.

## Executed evidence

424 new registered cases. Final tests against unchanged PR #46 production: **7,133 passed / 387 failed**, Debug and Release. Corrected signed-library tests: **7,520 passed / 0 failed**, both local .NET 8 configurations. Read/clone/transform/wire tests cover all 32 combinations of the five defined bits across all six versions and both transports, with additional open-path, unknown-bit, independent clone/transform and associative-update controls.

The independent ezdxf 1.4.4 verifier reads twelve output drawings containing 384 classified paths, checks exact flags and polyline/edge representation before audit, and reports zero errors or repairs. Those combinations test bit preservation; they are not a claim that every classification represents correct geometric nesting. No native AutoCAD process was executed. Final-head Linux/Windows Debug/Release, netstandard2.0 and documentation/source CI are merge gates.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_boundary_flags.py artifacts/conformance
```

## Primary reference and remaining scope

Autodesk defines group 92 as the bit-coded Default/External/Polyline/Derived/Textbox/Outermost field:
https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm

Counted non-polyline edge grammar, spline fit/tangent data, fractional gradient shift, complete affine geometry handling and dependency-safe associations remain separate work. Path updates retain existing nontransactional failure behavior and applicability to associated contours.

## Integration with concurrent pattern-list work

PR #47 was developed independently against the same merged PR #46 base and has now merged as `a54698d1a2e033cab0d4f0a0e6e2e51b5ed91858` (tree `0e7308a2ac536e25a4c37060bdab07112b89d77a`). Its pattern parser, 450 tests and verifier are retained alongside this classification correction; both test registrations remain active. The combined signed-library suite passes **7,970 cases / 0 failures** in local Debug and Release. The initial red/green counts above describe the original PR #46-based comparison, not a fabricated test run on the integrated base. Final-head CI must use this integrated tree.
