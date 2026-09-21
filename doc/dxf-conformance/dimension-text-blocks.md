# Dimension text placement during block regeneration

All eight concrete `DimensionBlock.Build` overloads now share a text finishing
path, including calls through `DimensionBlock.Build(Dimension)`. A manual
`TextReferencePoint` and its flag survive direct builds, initial document/block
adoption with `BuildDimensionBlocks = true`, and repeated `Dimension.Update`.
The newly generated top-level MTEXT label uses that OCS anchor instead of the
automatic label location. The dimension's elevation and extrusion continue to
locate the local planar drawing block; no new coordinate convention is added.

Manual labels use `AttachmentPoint`. A split `\\X` label is joined as two
paragraphs in one MTEXT anchored there, rather than retaining two independently
positioned automatic labels. `TextRotation` is applied as an offset from the
builder's default label direction. `LineSpacingStyle` and `LineSpacingFactor`
are applied for automatic and manual labels. Zero rotation preserves the
existing default direction without reassignment. Automatic layout retains its
existing attachment and split-label decisions. Text inside custom arrow block
definitions is neither traversed nor edited.

Only newly generated entities are changed. Automatic reference-point publication
occurs after successful block construction; manual reference points are not
reassigned, retaining their bits (including signed zero). Null/empty block names,
nonfinite chosen anchors/rotation and nonfinite or non-DXF spacing reject.
Invalid manual attachments reject. This tightens previously permissive Build
behavior without changing public property setters or input-version gates.

```csharp
var dimension = new LinearDimension(Vector2.Zero, new Vector2(10, 0), 3, 30);
dimension.UserText = "UPPER\\XLOWER";
dimension.TextReferencePoint = new Vector2(17.25, -8.5);
dimension.AttachmentPoint = MTextAttachmentPoint.MiddleCenter;
dimension.TextRotation = 25;
dimension.LineSpacingStyle = MTextLineSpacingStyle.Exact;
dimension.LineSpacingFactor = 1.5;
var document = new DxfDocument { BuildDimensionBlocks = true };
document.Entities.Add(dimension);
dimension.Update(); // Manual anchor and generated label agree after regeneration.
```

## Verification

The focused harness contains 562 cases. It exercises all eight concrete and
shared dispatches, nine attachments, automatic orientation/spacing, custom-arrow
text isolation, split/suppressed text, signed-zero manual anchors, clone and
repeat-build isolation, invalid settings, and safe name refusal. The wire matrix
covers six typed profiles, text and binary input/output, modelspace, paper space
and referenced blocks, tilted extrusion and nonzero elevation. ARC_DIMENSION
retains its existing R2004+ eligibility. The complete new inventory is 846
source/output drawings, including 564 tilted-plane WCS anchors/directions.

The independent checker reads physical packets and reloads every drawing with
ezdxf. It checks dimension flags/settings, geometry-block references, MTEXT
content, attachment, spacing, local anchor, analytically derived directions,
tilted WCS anchors/directions and graph audits. It rejects actual-tag mutations
and incomplete/extra inventories. Execute with:

```sh
DXF_TEST_FILTER=dimension-text-block/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_dimension_text_blocks.py artifacts/conformance
```

Actual source-bound results and earlier failures are recorded in the PR; test
definitions alone are not execution evidence. The unchanged focused harness was
also run against the preceding production implementation, not a duplicate model.

## Boundaries

This repairs the manual-anchor loss documented in PR #172; it is not a rewrite
of dimension fitting. Existing `CalculateReferencePoints` and DIMTMOVE policies
remain: moving text can move dimension geometry under applicable fit settings.
Automatic collision detection, font/MTEXT extents, native leader routing,
alternate/tolerance formatting and every dimension-style display option are
not newly qualified. Failed entire Update/adoption operations are not guaranteed
transactional. Shared style mutation, concurrent mutation and private association
caches are outside this change. The independent reader is not AutoCAD and the
synthetic corpus is not native producer/rendering acceptance evidence.

The task adds no historical typed dialect, pre-R11 support, dependency-complete
imports or general document-version conversion. Private FIELD/TABLE/cache
regeneration and native AutoCAD open/AUDIT/save/reopen/font qualification remain
separate requirements.

## Primary references

- [Autodesk common DIMENSION group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-EDD54EAC-A339-4EBA-AEA6-EC8066505E2B.htm):
  OCS text point, user-positioned flag, attachment, rotation offset and spacing.
- [ezdxf dimension block expansion](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dimension.py):
  independently transforms local block labels using the dimension OCS/elevation.
