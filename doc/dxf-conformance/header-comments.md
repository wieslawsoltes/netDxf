# Ignore comments while parsing HEADER values

Baseline: `b4ad3ff781f2166c4f25f0b33f820c0407579cd8`, after merged PR #21.

## Defect and correction

HEADER parsing treated group 999 comments as variable names, scalar values, vector components or apparent ENDSEC text. A valid comment between a variable's name and value could prevent loading; a comment containing ENDSEC could terminate HEADER parsing prematurely. Autodesk specifies that group 999 comments are ignored on input.

Use a HEADER-scoped next-tag helper to skip comment records wherever this parser advances: before variable names, between names and values, between vector components, after values, and while skipping existing excluded variables. The raw text tag reader still exposes comments. No global comment filtering, comment-position preservation, general HEADER grammar rewrite, encoding change or dimension-override policy change is introduced.

All admitted formats (AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032) accept these text fixtures. Comment-bearing documents save and reload in both text and binary; binary comments remain unsupported by the existing binary codec. Interior comments are discarded, not written as semantic header data. Low-level EOF/truncation diagnostics and public Debug exception / Release null conventions remain in force.

## Regression evidence

318 new registered cases: insertion at each of 46 HEADER tag gaps for all six versions; dense comments at all gaps; LF/CRLF and comment-free controls; and four truncated-comment/HEADER cases. Comment strings include empty strings, ENDSEC, EOF, $ACADVER, AC1009 and 999. Modeled scalars, UCS/insert-base vectors, custom scalar/Vector2/Vector3 values, ignored dimension variables and the following ENTITIES section are checked. Structural-looking strings remain legal as actual custom values. Round-trip controls retain the existing AC1015 LASTSAVEDBY omission/default behavior rather than incorrectly asserting newer-version output semantics.

After correcting that test's AC1015 expectation, the unchanged production parser reports **2,813 passed / 264 failed**. With the scoped parser correction, the same tests report **3,077 passed / 0 failed**, Debug and Release. The optional independent ezdxf 1.4.4 check reads all six densely commented minimal fixtures, agrees on the scalar/vector values and retains the following LINE. That check is semantic agreement on these minimal fixtures, not a full object-graph audit or an AutoCAD execution.

Final-head Linux/Windows SDK tests, netstandard2.0 compilation and source audit remain required before merge. The pinned 113-row baseline matrix is not reclassified as complete by this parser fix.

## Primary reference

Autodesk group 999 comment semantics: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
