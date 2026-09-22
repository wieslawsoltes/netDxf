# Literal dimension text and suppression

The typed DIMENSION/ARC_DIMENSION reader preserves the decoded group 1 string
without trimming spaces or tabs. Exactly one ASCII space is the DXF text
suppression marker; empty/absent text requests the measurement. Other strings,
including multiple spaces, tabs, surrounding whitespace and nonbreaking spaces,
remain literal user content. Public UserText assignment, Unicode decoding,
writers, version gates and dimension geometry algorithms are unchanged.

Previously a whitespace-only group 1 was changed to an empty string. In
particular, loading and then updating a suppressed dimension could regenerate a
numeric label that the source had explicitly suppressed. The correction is in
the shared typed reader, not in the raw document parser or a second serializer.
Regeneration continues through the existing block builders and text finishing
path from PR #173. No new font, fitting or DIMTMOVE semantics are implemented.

## Verification

The focused harness contains 64 direct API/clone/repeated-build cases and 282
wire cases. Each wire case contains eight text variants across one of the eight
concrete dimension families, one of three placements (modelspace, paper layout,
referenced block), a supported typed version and an input transport. ARC_DIMENSION
retains its R2004+ gate; other dimensions cover the six R2000-R2018 typed profiles.
Both output transports are reloaded. Repeated Update, detached clone builds,
manual-anchor and handle preservation, label contents, source-stream ownership,
following LINE geometry and object validation are checked.

The complete independent corpus is 846 drawings / 6,768 dimension records.
Physical group 1 and MTEXT strings are checked separately from independent ezdxf
reads, including historical Unicode escapes and UTF-8. Expected numeric labels
for empty text and <> follow the fixed source geometry, not netDxf output.
The checker rejects corrupt text/anchor/type/owner packets, added suppressed
labels, absent/duplicate labels and incomplete/extra inventories. It audits
without repairs. The source corpus is synthetic, not native AutoCAD evidence.

The fixture uses cardinal manual anchors to isolate text IO from the existing
radial/diametric SetDimensionLinePosition trigonometric reprojection. An initial
noncardinal fixture revealed a one-ULP measurement change on Update; that
numerical behavior is not fixed or qualified by this reader change. The exact
measurement assertion is retained with the cardinal fixture. Executed baseline,
final and hosted results are recorded in the PR, not inferred from definitions.

```sh
DXF_TEST_FILTER=dimension-text-literal/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_dimension_text_literals.py artifacts/conformance
```

Primary reference: [Autodesk common dimension group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-EDD54EAC-A339-4EBA-AEA6-EC8066505E2B.htm),
group 1. The implementation preserves all decoded content; it does not infer
that whitespace-only text has identical glyph/layout behavior in all renderers.
Historical typed dialects, pre-R11 support, native fonts/visual equivalence,
private FIELD/TABLE/cache regeneration, dependency-complete imports, general
version conversion and AutoCAD open/AUDIT/save/reopen remain separate work.
