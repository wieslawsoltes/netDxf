# HATCH authored RGB stops and dialog state

## Defects and scoped correction

The former gradient reader regenerated the second RGB stop for a single-color gradient, discarding the second stop actually present in the file. For a two-color gradient it discarded the authored tint. The public Tint setter passed through Color2, whose setter selects two-color mode, so a tint edit could clear SingleColor. Clone invoked those editing setters again, changing mode and colors rather than copying state. A rejected null Color2 assignment also cleared mode before throwing. Gradient clones omitted their description and dormant line definitions.

An internal restoration constructor now retains both authored RGB stops, the mode and the tint without recomputing a stop. The reader uses this constructor, and clone deep-copies the same state. Base pattern description and line definitions are copied as well. This is deliberately different from an explicit editing operation: setting Tint in single-color mode still derives Color2 using the existing HSL algorithm; setting SingleColor to true still requests that derivation. Those operations no longer clear the mode inadvertently. Assigning a non-null Color2 continues to select two-color mode.

Tint values must be finite and in [0, 1], including dormant tint metadata in two-color mode. Invalid assignments leave tint, mode and color references unchanged. Constructor and reader validation obey the same domain. The reader gives a contextual group-462 diagnostic; the existing Debug exception / Release null Load convention is preserved.

Color1 replacement and mutation keep their existing behavior: they do not automatically recompute Color2. No new mutable-color observer is installed. Cloning and saving are not interpreted as editing commands. Description and dormant line definitions remain API metadata, not newly supported gradient wire fields.

## Version contract

| Family | Typed input | Typed output / clone |
|---|---|---|
| R11/R12 / AC1009 | Not admitted | Raw preservation stays separate |
| R13 / AC1012 | Not admitted | Same |
| R14 / AC1014 | Not admitted | Same |
| 2000 / AC1015 | Existing permissive gradient import retains RGB pair, tint and mode | Existing solid-fill downgrade still drops gradient metadata |
| 2004 / AC1018 | Authored RGB pair and dialog metadata retained | Text/binary, direct/nested clone and INSERT explosion |
| 2007 / AC1021 | Same | Same |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

## Regression evidence

Baseline is merged PR #53, `c98c5681da08ffeb32784f586d0ef700e70e72ec`, tree `d84d85946d8876b36b78319f8c79fe9b15145249`. The 602 added registered cases cover all nine gradient names, five gradient-capable profiles, text/binary, both modes, endpoint/intermediate tints, independently encoded input, three alternating output cycles, nested clone/explode, invalid wire/API values, atomic null rejection, and base clone metadata. Two AC1015 controls retain its existing import/export boundary.

With the final corrected fixtures, unchanged production reports **10,127 passed / 504 failed in Debug** and **10,147 passed / 484 failed in Release**. The difference is the existing public exception convention on invalid single-color input. Corrected signed production reports **10,631 passed / zero failed in both configurations**. The 15 Python ledger-integrity tests pass separately.

The first development test draft incorrectly treated AciColor.ToTrueColor as a plain 24-bit integer. That API includes an existing high-byte marker. Final tests explicitly compare RGB channels and group-421 RGB24 portions; the full corrected suite was rerun against unchanged and corrected production. No production fix is inferred from the discarded fixture failure. Exact binary64 tint, mode, ordering and both RGB values remain asserted.

The independent ezdxf 1.4.4 verifier checks 20 exported files / 40 authored gradient color states, including both modes, exact tint, RGB channels, shift, rotation, boundaries, seeds, elevation, XData and the following LINE. It requires zero audit errors and repairs. Native AutoCAD execution and rendered appearance equivalence are not claimed.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_gradient_state.py artifacts/conformance
```

## Boundaries

This does not repair the positional gradient grammar, optional ACI values, ACI identity retention, color-stop control values, unknown gradient names, reserved fields, general affine gradient evaluation or centralized downgrade reporting. Existing packed-color high-byte output policy is unchanged; retaining RGB24 is not byte-identical preservation of the original group-421 integer. Public HSL editing remains the existing algorithm, not an independently certified reproduction of AutoCAD's gradient dialog.

Primary definition: [Autodesk HATCH DXF reference](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm), groups 452 and 462: color-definition mode and tint are dialog metadata, distinct from the two stored colors. The specified tint range is [0, 1].
