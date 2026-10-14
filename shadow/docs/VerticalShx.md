# Vertical SHX typography

Styles declaring `IsVertical` now select the font's authored vertical programs. Opcode 14 is executed by the existing bounded glyph VM with `vertical: true`; horizontal and vertical glyphs have independent cache keys. This is not a rotation of a horizontal text string. The glyph starts at its authored top-center origin and returns to the column baseline before advancing downwards.

The shared paragraph compositor accepts vertical-flow metrics through an additive overload. Width and obliquing are applied to glyph cross-flow geometry rather than multiplying vertical advance a second time. Inline styles, colors, tracking, stacks, rules, paragraph breaks, indents, spacing and tabs retain the shared bounded composition pipeline. Paragraph flow runs downwards and columns progress leftwards; the supplied rectangle-width limit is interpreted as maximum advance along a vertical column. Attachment names are mapped to flow/cross-flow coordinates. This explicit consumer convention is not certification of every proprietary vertical-MTEXT layout rule.

The provider emits neutral presentation-only stroke groups and regions; both Skia and SVG consume identical geometry, and topology remains unaffected. Entity OCS, rotation, width and oblique axes are applied after local layout. Vertical linetype text uses the same resource provider. Live catalog replacement invalidates resource-dependent projections; pinned font sets preserve prior output.

A selected font must declare vertical support and have supported Unicode/legacy metrics. Missing glyphs produce explicit boxes and diagnostics. Upward or sideways final glyph advances and unbalanced cumulative vector scaling are refused instead of restarting state incorrectly at formatting boundaries. Such unconventional stateful fonts require a specialized provider. BIGFONT mapping is developed separately; this feature does not guess code pages or silently substitute a horizontal font.

Character, measurement-work, glyph-instruction and curve quotas remain enforced. A failed projection does not publish partial geometry. No source filename grants filesystem access and no font files are bundled. The test definitions are original in-memory instruction examples.

Reference: Autodesk Special Codes Reference, https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Customization/files/GUID-06832147-16BE-4A66-A6D0-3ADF98DC8228.htm .
