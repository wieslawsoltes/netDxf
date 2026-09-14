# Typed HELIX: spline payload and parameter metadata

`Helix : Spline` adds a typed entity family previously discarded by `DxfDocument`. The two documented subclasses are separate: `AcDbSpline` stores the authored curve and `AcDbHelix` stores its parameter definition. Reused codes 10/11/12 and 40/41/42 cannot be interpreted without the subclass boundary. The reader stops spline decoding at that boundary instead of consuming helix coordinates as extra spline controls or tolerances.

## API and operations

`new Helix(spline)` deep-copies the supplied spline; it does not reverse-engineer helix parameters. Set `AxisBasePoint`, `StartPoint`, `AxisVector`, `Radius`, `Turns`, `TurnHeight`, `IsRightHanded`, `Constraint`, `MajorReleaseNumber` and `MaintenanceReleaseNumber` explicitly. Radius is the terminal radius; start radius is represented separately by the start point. Parameter edits deliberately do **not** refit stored control points, weights or knots. This preserves externally authored files whose two representations differ.

Positions and vectors are finite WCS values. Axis vectors must be nonzero, but are not normalized in storage. Radius may be zero; turn count must be positive; signed and zero turn height are retained. Constraint values are turn-height/turn-count/total-height selectors, not an automatic constraint solver. The class-version defaults 29/63 are interoperability conventions, not DXF format-version numbers. Missing complete parameter fields use the API defaults; partially present vectors and duplicate known fields are rejected. Optional scalar presence is normalized on output.

Deep clones retain both representations, common entity properties and independent XData while discarding database identities. `ToSpline()` returns an ordinary, independent SPLINE with exactly the authored payload. `DrawingEntities.Helices` selects helices; `DrawingEntities.Splines` also includes them because they inherit SPLINE. The new `EntityType.Helix` is appended, preserving all earlier enum values.

```csharp
var spline = new Spline(controlPoints, weights, knots, degree, false);
var helix = new Helix(spline)
{
    AxisBasePoint = new Vector3(0, 0, 0),
    StartPoint = new Vector3(5, 0, 0),
    AxisVector = Vector3.UnitZ,
    Radius = 3,
    Turns = 4.5,
    TurnHeight = 2,
    IsRightHanded = true,
    Constraint = HelixConstraint.Turns
};
var document = new DxfDocument(DxfVersion.AutoCad2018);
document.Entities.Add(helix);
// The supplied spline is retained, not regenerated from these parameters.
```

Transforms support nonsingular similarities: translation, rotation, uniform scale and reflection. Positions receive translation, axes and tangents do not; radius and signed pitch scale, and reflections flip handedness. Nonfinite results, shear and nonuniform scales are refused before mutation. `Insert.Explode()` degrades a nonuniformly transformed helix explicitly to its stored SPLINE, analogous to converting a transformed circle to an ellipse; it does not retain false helix parameters. Other inherited spline edits modify only the stored representation.

Generated HELIX CLASS records use AcDbHelix / ObjectDBX Classes and actual exported instance counts across all registered blocks. Compatible authored class metadata is retained, output counts are recomputed without mutating `document.Classes`, and conflicting live HELIX class definitions fail before output. No proxy code is executed.

## Version comparison

| Profile | Typed input | Typed output |
|---|---|---|
| AC1009/R11-R12, AC1012/R13, AC1014/R14 | Typed admission unchanged (rejected) | Separate raw preservation only |
| AC1015/2000, AC1018/2004 | Permissive subclass decoding tested | HELIX rejected before supplied-stream writes; explicit `ToSpline()` is available |
| AC1021/2007 | Both subclasses retained | Text/binary, clones, model/paper/nested/unreferenced blocks |
| AC1024/2010 | Same | Same |
| AC1027/2013 | Same | Same |
| AC1032/2018 | Same | Same |

The 2007+ exporter is an explicit conservative writer profile, **not** a historical-introduction claim derived from the current group-code table. No implicit upgrade, deletion or conversion occurs during Save. The filename Save wrapper remains nontransactional; its file may be truncated before the stream-level guard. Debug exceptions and Release null/false conventions remain unchanged.

## Executed validation

Base: merged PR66, `ce2298476feba25dc6b030295633950bc03336c9`, tree `852eaa288ab068ce8b7760c75d395349a8f14eb9`.

144 independent-byte cases on unchanged production: **15,271 passed / 144 failed**, both Debug and Release (HELIX entities were discarded). Those identical cases pass after implementation. 82 further API, clone, class, malformed-packet, transform and export-profile cases bring the complete signed-library local suite to **15,497 passed / zero failed**, both configurations on .NET 8. Early API test fixtures were corrected to clone owned entities and select a supported output profile before final execution; ownership and export guards were not relaxed. Fifteen Python ledger tests pass separately.

The independent ezdxf 1.4.4 verifier checks **48 files / 96 helices**, all four output profiles, text/binary, all three constraint values and both handedness values. It verifies exact spline controls/weights/knots, each helix parameter, axis magnitude, tangents, class counts, XData and the following LINE. All outputs have **zero audit errors and zero repairs**. The two authored representations are deliberately distinct in these fixtures; an independent AUDIT result does not prove mathematical agreement or rendered appearance.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_helix.py artifacts/conformance
```

Before merge, execute final-head Linux/Windows Debug/Release, netstandard2.0, ledger and source-audit CI. The primary reference is [Autodesk HELIX group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-76DB3ABF-3C8C-47D1-8AFB-72942D9AE1FF.htm). The [ezdxf HELIX implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/helix.py) and class registry supply independent-reader conventions, not proprietary AutoCAD execution.

## Remaining boundaries

This increment is typed transport, metadata editing and selected geometry operations, not analytic helix construction, automatic parameter solving, spline fitting, per-field unknown-tag retention, all inherited SPLINE grammar/flags, arbitrary-affine HELIX semantics or native AutoCAD qualification. Broader database references and transactional file saves remain separate. The main source-pinned ledger is refreshed after production merges.
