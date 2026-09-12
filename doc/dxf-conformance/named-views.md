# Core named VIEW table support

Named views were previously consumed by a placeholder reader and never serialized. `DxfDocument.Views` is now public and its entries are read, written, cloned, indexed by handle, and managed through the existing named-table collection.

## Implemented record fields

| Group codes | Model |
|---|---|
| 2, 5, 330, 100 | Name, handle, table owner and subclass markers |
| 70 | `ViewFlags`, including paper-space and xref metadata flags |
| 10/20 | `ViewCenter` in display coordinates |
| 11/21/31 | Nonzero `ViewDirection` from the target in world coordinates |
| 12/22/32 | `Target` in world coordinates |
| 40, 41 | Positive `Height` and `Width` |
| 42 | Positive `LensLength`, not an angular field of view |
| 43, 44 | Front/back clipping-plane offsets |
| 50 | Twist `Rotation` in degrees |
| 71 | `ViewMode` flags |
| 281 | All seven `ViewRenderMode` values |
| 73 | `IsCameraPlottable`, emitted only for 2007 and newer |
| 1001+ | Per-view and VIEW-table XData, including binary payloads |

The existing `Camera`, `Fov` and `Viewmode` members remain as aliases for `ViewDirection`, `LensLength` and `ViewMode`. The existing lens-length default of 40 is retained. Geometry must be finite; direction cannot be zero; size and lens length must be positive. Invalid API assignments now fail rather than creating an invalid view. Directions are preserved without normalization. Clipping offsets may be negative, and changing the paper-space flag does not clear unrelated flags.

## Version matrix

| Behavior | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 |
|---|---|---|---|---|---|---|
| Core geometry, flags, render modes and XData | Tested | Tested | Tested | Tested | Tested | Tested |
| Plottable-camera group 73 | Not emitted | Not emitted | Tested | Tested | Tested | Tested |
| Save a true plottable-camera value | Reject | Reject | Preserve | Preserve | Preserve | Preserve |

Each tested column includes both text and binary transport. The existing public `Save` conventions remain: unsupported down-saves throw in Debug and return false in Release. A failed save is not an atomic operation and may already have written part of its destination. This PR does not change the document-wide save contract.

The group-73 version boundary is corroborated by ezdxf's explicit DXF2007 attribute declaration, not by independently running each historical AutoCAD release. The Autodesk reference defines the field semantics. Self-round-trip and hand-authored fixtures are not AutoCAD certification.

## Usage

```csharp
using netDxf;
using netDxf.Header;
using netDxf.Tables;

var document = new DxfDocument(DxfVersion.AutoCad2018);
var view = new View("Inspection")
{
    ViewCenter = new Vector2(10, 20),
    ViewDirection = new Vector3(1, 2, 3),
    Target = new Vector3(100, 200, 0),
    Height = 80,
    Width = 120,
    LensLength = 85,
    Rotation = 15,
    RenderMode = ViewRenderMode.HiddenLine
};
document.Views.Add(view);
if (!document.Save("inspection.dxf"))
    throw new System.IO.IOException("DXF save failed.");
```

## Regression proof

66 new registered tests cover API invariants and aliases, non-unit directions, flags, clone isolation, case-insensitive collection membership and renaming, handle registration/removal, every render mode, Unicode names, table/entry metadata, exact output tags, independently authored reordered VIEW records, omitted defaults, malformed records, and version-specific camera behavior.

With the new model/public collection but old document IO: **380 passed / 60 failed**. Integrated signed-library execution: **440 passed / 0 failed**, both Debug and Release. Final-head Linux/Windows CI, netstandard2.0 compilation and source-audit checks are required before merge.

## Remaining VIEW scope

This is the core named-view feature, not complete VIEW preservation. Saved UCS state (72/110–132/79/146/345/346), background/live-section/visual-style/Sun references, and generic unknown-tag/dependency preservation remain separate work. The writer currently emits 72=0. External-reference flags are metadata; this feature does not load external drawings or restore a view into a rendering engine.

## Sources

- Autodesk VIEW record definition: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-CF3094AB-ECA9-43C1-8075-7791AC84F97C.htm
- Autodesk binary DXF numeric representation: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- Independent implementation/version-boundary evidence: `mozman/ezdxf`, `src/ezdxf/entities/view.py`, audited blob `e834f2a0377e61fc40639e6d8e0479d367409145`; https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/view.py
