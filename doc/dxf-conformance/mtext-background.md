# MTEXT background-fill data and text frames

Baseline: `4b609c93abf21bab7c9d5bfcda9e2a95c0ad3881`, after merged PR #18.

## Delivered scope

`MText.BackgroundFill` exposes an optional `MTextBackgroundFill`: group 90 flags, group 45 fill-box scale, group 63 ACI fallback, group 421 packed RGB, group 431 color-book name, and raw group 441 transparency. Foreground entity color/transparency remain independent. Optional values retain presence in memory; explicit zero RGB, empty names and all transparency bits survive. The raw RGB high byte is also preserved because producers may store color-method bits there. Convenience color construction writes canonical 24-bit RGB without retaining mutable AciColor storage.

Read, edit, clone, remove and write are implemented. INSERT/block cloning and INSERT explosion deep-copy background objects. The reader accepts arbitrary ordering and interleaved comments/unrelated tags, and preserves following drawing direction and XData. Invalid flags, nonpositive/non-finite scales, out-of-range ACI and NUL/line-ending names are rejected instead of discarded. Restricting scale to positive finite values is this library's input policy; it does not assert that every undocumented producer uses it.

The default object authors an enabled color fill with scale 1.5 and index 7. Setting the property to null removes all background data. Flags 0, 1, 2, 3 and combinations with frame bit 16 are represented independently; setting flags to zero retains dormant metadata. Frame-only creation does not invent scale/color fields.

```csharp
var text = new MText("Equipment note", new Vector3(10, 20, 0), 2.5)
{
    BackgroundFill = MTextBackgroundFill.FromColor(new AciColor(240, 244, 248), 1.2)
};
text.BackgroundFill.ColorIndex = 7; // retain an independent ACI fallback
var clone = (MText)text.Clone();
clone.BackgroundFill.TrueColor = 0xFFEECC; // source is unaffected

text.BackgroundFill = MTextBackgroundFill.FromDrawingWindow();
text.BackgroundFill.Flags |= MTextBackgroundFillFlags.TextFrame;

var document = new DxfDocument(DxfVersion.AutoCad2018);
document.Entities.Add(text);
document.Save("background.dxf");
```

## Version/profile matrix

| Capability | 2000 / AC1015 | 2004 / AC1018 | 2007 / AC1021 | 2010 / AC1024 | 2013 / AC1027 | 2018 / AC1032 |
|---|---|---|---|---|---|---|
| Decode encountered background fields | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary |
| Write background fill in this profile | Explicitly rejected | Conservatively rejected; boundary pending | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary |
| Write frame bit in this profile | Rejected | Rejected | Conservatively rejected | Conservatively rejected | Conservatively rejected | Tested text/binary |
| Promote input to 2018 and cross-transport save | Tested | Tested | Tested | Tested | Tested | Tested |

**These are implementation/export-profile boundaries, not claims about first historical availability.** The Autodesk MTEXT DXF page does not specify a minimum release for these fields. ODA's native DWG specification lists background data under R2004+ and frames under R2018+, whereas the independent ezdxf DXF exporter gates background attributes at AC1021. Native DWG support must not be silently treated as proof of an identical historical DXF dialect. This first profile therefore uses the conservative common DXF export boundary. Verifying/expanding AC1018 background export and earlier AC1032-family frame predecessors against authentic AutoCAD-generated historical DXFs remains explicit work.

Read-side retention does not certify that a field is legal in the declared historical version. It avoids throwing that information away and allows an explicit version upgrade before saving.

## Output and downgrade rules

The writer preflights all registered blocks, including model/paper space and nested definitions, before writing any stream bytes or adding default layouts. Unsupported background export raises NotSupportedException internally; public Save retains its existing Debug exception / Release false convention. Removing the property/frame flag or choosing the supported profile is explicit caller policy, never an automatic lossy fallback. This feature preflight is not a general transaction: file-path Save still creates/truncates its file before calling the writer, and other Save failures may occur later.

Active fills receive companion scale/ACI tags at export (1.5 and 7 when absent), as required by independent interoperability observations; this normalization does not mutate the source model. Inactive and frame-only optional-field absence is retained. A background object with no explicit input flag is exported with flag zero. Equal semantics/retained fields, not byte-identical file output, are claimed.

Only the canonical background color slots 421/431 are modeled here; Autodesk's generic ranges 420–429/430–439 are not a claim that every offset is interchangeable. Foreground 420 belongs to AcDbEntity. No mask rendering, bounds calculation, color-book lookup, column layout, transparency application, embedded-MTEXT context, or generic unknown-tag preservation is implemented by this change. Autodesk explicitly marks background transparency as not implemented; here it is opaque data.

## Verification

265 new registered cases. With the new model/tests and the old production document IO: **2,413 passed / 250 failed**. Integrated signed production library: **2,663 passed / 0 failed**, Debug and Release. Cases cover all six admitted formats and both transports, independently authored/reordered records, exact emitted fields, foreground/background separation, optional defaults and presence, all supported flag combinations, invalid records, raw color/transparency bits, Unicode, nested cloning/explosion, removal, preflight with nonempty streams, and explicit promotion to 2018. Normal Linux/Windows SDK CI, netstandard2.0 builds and source audit are additional merge gates. No AutoCAD process has been executed by these tests.

## Primary sources

- Autodesk MTEXT DXF group codes: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm
- Independent ezdxf author documentation: https://ezdxf.readthedocs.io/en/stable/dxfentities/mtext.html
- Independent ezdxf implementation, `src/ezdxf/entities/mtext.py`, blob `5c74e962a16dd261cbbb207d9df30586f092c28e`: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/mtext.py
- ODA Open Design Specification for DWG v5.4.1, printed page 154: https://www.opendesign.com/files/guestdownloads/OpenDesign_Specification_for_.dwg_files.pdf

This entry supersedes only the background subset of the source-pinned 113-row matrix. It does not upgrade the overall MTEXT row to complete.
