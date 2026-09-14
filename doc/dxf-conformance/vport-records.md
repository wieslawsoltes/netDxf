# VPORT configuration records and current-view persistence

The typed reader previously discarded every `*Active` record at the end of `ReadVPort`, while its caller accepted only a non-null `*Active` record. Consequently, even a single current viewport silently reverted to defaults on load. Other configuration records were discarded, and the writer generated a full-window rectangle for every viewport. Active-record cloning also rejected the reserved name and omitted the snap-mode flag.

This module restores the first active record, preserves every physical record in file order, and supports multiple tiles sharing a configuration name. Every record retains its own handle, table owner, binary/string XData and independent clone state. Missing or empty VPORT tables still acquire a default active record; a named-only table keeps its authored records and receives an additional default active record.

## Public collection contract

VPORT is the exception to the usual DXF table rule: several entries can share one name. Its public collection preserves existing `TableObjects<VPort>` operations while exposing the complete record sequence explicitly.

| API | Meaning |
|---|---|
| `VPorts[name]`, `TryGetValue`, `Count`, `Names`, `Items`, enumeration | Named configurations; lookup and enumeration expose each configuration's first physical record |
| `VPorts.Records` | Read-only live list of every physical tile in file order |
| `GetConfiguration(name)` | Read-only snapshot of all tiles with that name, compared without case sensitivity |
| `Add(record)` | Add a new named configuration, or return its existing first record |
| `AddRecord(record)` | Append a detached physical tile, including an additional tile in an existing configuration |
| `Remove(name)` | Remove the complete named configuration; the active configuration is protected |
| `Remove(record)` | Remove one tile by identity; the final active tile is protected |
| `DxfDocument.Viewport` | First record in the `*Active` configuration |

Adding the same owned record is idempotent. Adding a record owned by another document rejects before changing either document; clone it first. Detached records whose XData registry is owned by another document also reject before assigning a handle or mutating either document; cloning the record provides independent XData. Deleting a representative promotes the next tile of that configuration. Renaming a named tile changes that tile's configuration, allows it to join an existing configuration, and retains physical ordering. Active names remain reserved, and blank renames reject. Configuration indexes update only after all name-change observers return successfully, so a rejected rename leaves them unchanged. `Clear()` removes all unreferenced named configurations and preserves the active configuration.

Physical VPORT records now compare by object identity. Their hash codes remain stable through renaming. A protected `UsesReferenceIdentity` policy lets VPORT select identity semantics without changing the existing public equality signature. When either comparison operand requires record identity, equality uses object identity; comparisons between other table objects retain their existing behavior. This distinction is required for repeated-name entries in the reader's deferred-XData map and the application registry's reference map. A clone never equals its source by physical identity, even when the names match.

```csharp
DxfDocument drawing = new DxfDocument();
VPort first = drawing.Viewport;
first.LowerLeftCorner = new Vector2(0, 0);
first.UpperRightCorner = new Vector2(0.5, 1);
first.ViewCenter = new Vector2(15, 20);

VPort second = (VPort)first.Clone();
second.LowerLeftCorner = new Vector2(0.5, 0);
second.UpperRightCorner = new Vector2(1, 1);
second.ViewDirection = new Vector3(1, 1, 1);
drawing.VPorts.AddRecord(second);

VPort saved = (VPort)first.Clone("Review");
drawing.VPorts.Add(saved);
```

## Field and version scope

The following editable fields are retained across all six admitted typed versions (2000, 2004, 2007, 2010, 2013 and 2018), in both text and binary. They are tested field subsets, not a claim that every VPORT schema field is implemented.

| Group codes | Stored state |
|---|---|
| 2, 70 | Configuration name and symbol-table flags |
| 10/20, 11/21 | Display rectangle corners |
| 12/22, 13/23, 14/24, 15/25 | View center, snap origin, snap spacing, grid spacing |
| 16/26/36, 17/27/37 | World-coordinate view direction and target |
| 40, 41, 42 | Height, aspect ratio and lens length |
| 43, 44, 50, 51 | Front/back clipping offsets and snap/view rotation angles |
| 71–78 | View mode, circle resolution, fast zoom, UCS icon, snap/grid state, snap style and isometric plane |
| 281, 65 | Render mode and whether activation restores the stored UCS |
| 110/120/130, 111/121/131, 112/122/132, 79, 146 | Unnamed UCS origin/axes, orthographic type and elevation |

Direction and UCS axes retain their authored magnitudes; the setter no longer normalizes the view direction. Scalars and coordinate components must be finite. Heights, aspect ratios and lens lengths must be positive, and selected Boolean/enum settings are range-checked. Invalid assignments do not mutate the previous value. Stored rectangle corners are retained without tiling, intersection, aspect-ratio or normalized-domain repair. UCS axes are nonzero but no orthogonality or UCS evaluation is inferred.

The reader dispatches unique scalar groups independently of printed order, accepts text comments, validates partial coordinate groups, rejects duplicate known fields and rejects duplicate record handles. Unfamiliar fields still follow the existing typed fallback policy; exact unknown-tag preservation remains the separate raw API. The writer counts physical records in the VPORT table header and caps its obsolete 16-bit count field rather than overflowing it.

Autodesk's VPORT table lists view height as group 45 and omits aspect ratio. Existing netDxf behavior, ezdxf's implementation and its published VPORT fixtures use groups 40 and 41 respectively. This module retains that established packet convention. The legacy snap/grid fields remain stored and emitted across admitted versions for round-trip compatibility; no historical-first-introduction claim is made for each optional field.

## Verification

`VPortWireTests.cs` supplies independently assembled four-record input, all six typed versions, both transports, reversed scalar ordering with comments, exact binary64 values, repeated format changes, active cloning, empty tables, malformed fields and incomplete vectors. Every record is checked by its original handle and independent XData. Exported tags are compared with the authored field values and a following LINE remains intact.

`VPortApiTests.cs` checks configuration versus physical-record counts, case-insensitive grouping, snapshots, addition/removal/promotion, merging through renaming, foreign-document isolation, record identity through `TableObject` and equality interfaces, stable hashes, application-registry references after rename/removal, all stored clone properties, named-only recovery, duplicate handles and rejected-assignment nonmutation.

`tools/verify_vport_records.py` reads the 12 exported drawings with ezdxf and enumerates every physical table record, checking four distinct handles and their exact values despite the repeated names. The unchanged PR #82 production (commit `44c9f70`) fails 36 positive wire/clone cases in an isolated Release run: 24 canonical/reordered persistence cases and 12 active-clone cases. The same run passes 325 cases (348 VPORT wire cases plus 13 existing transport smoke cases in total). The old Debug production aborts at its pre-existing invalid-name assertion, so a complete old-Debug red result is not claimed. The final complete signed-library suite reports **19,143 passed / zero failed** in both Debug and Release, including 404 new VPORT cases. The netstandard2.0 target builds successfully; 15 documentation-integrity tests and generated-comparison freshness checks also pass. These build/test counts are separate from native interoperability qualification. ezdxf 1.4.4 independently passed all 12 files with zero audit errors or repairs. Native AutoCAD open/AUDIT/save/reopen has not been executed.

## Remaining boundaries

Named/base UCS reference resolution, frozen-layer references, plot-style sheets, background/visual-style/sun/shade-plot graphs, modern lighting/ambient fields and unknown private metadata remain incomplete. The stored unnamed UCS and render-mode values are persisted; this library does not activate application viewports or render their appearance. Typed R12/R13/R14 admission remains unchanged and separate raw support is unaffected.

## References

- [Autodesk VPORT reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-8CE7CC87-27BD-4490-89DA-C21F516415A9.htm) describes duplicate-name configurations, the first active entry and published group fields.
- [ezdxf VPORT internals and independently published examples](https://ezdxf.readthedocs.io/en/stable/dxfinternals/tables/vport_table.html) provide the packet evidence for display rectangles and the established height/aspect-ratio fields.
- [ezdxf VPort source](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/vport.py) records its version metadata and the documented height-code discrepancy.
