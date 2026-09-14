# Raw embedded-object handle context

## Reproduced defect

The raw handle index previously ignored `(101, "Embedded Object")`. It interpreted ordinary handle-looking fields in the embedded object's private payload as pointers belonging to the enclosing object. It could also mistake private 102 strings for reactor/control data and 1001/1005 pairs for enclosing XData. Consequently a generic remap could rewrite those exposed fields without knowing their application schema.

Recognize that separator outside application control groups, before any existing XRECORD payload, in database sections. All handle-valued tags from the first recognized separator through the end of that lexical record now have `DxfRawHandleRole.Opaque` and context `Embedded Object`. They remain observable in source order but are not identities, owners, pointers, reactors, or XData of the enclosing record. Numeric spellings, values and ordered tags are unchanged.

This is deliberately conservative: a private embedded grammar can reuse 100, 101, 102 and 1001. The generic index does not infer an exit from the embedded tail at one of those values. Identifying enclosing-object XData after an embedded object requires a class-specific boundary contract; it is not guessed here. The next group-0 record resets context normally. Marker-looking strings nested in an application group, nonmatching 101 values and XRECORD's already-opaque ordinary-code data retain their preceding contracts. Malformed enclosing controls remain diagnosed.

## API consequences

No public API changes. `GetDependencyClosure` exposes the affected slots in `UninterpretedHandles` rather than following them. `AreSelectedReferencesResolved` describes only interpreted selected links, not private dependency completeness. `RemapHandles` uses its existing affected-opaque-slot guard to reject changes touching either a source or target numeric handle in this tail. Unrelated remapping remains possible, retaining untouched tag-object identity. Numeric no-ops still return the original raw snapshot.

This closes an unsafe reinterpretation path; it does not add typed MTEXT columns, embedded ATTRIB/ATTDEF editing, private handle discovery, aggregate extraction or schema-complete remapping.

## Profile contract

| Format family | Raw indexing, traversal and remapping | Typed admission |
|---|---|---|
| AC1009 / R11-R12 | Conservative marker recognition, text and legacy binary framing | Unchanged: not admitted |
| AC1012 / R13 | Same, modern binary framing | Unchanged: not admitted |
| AC1014 / R14 | Same | Unchanged: not admitted |
| AC1015 / 2000 | Same | Existing partial typed model unchanged |
| AC1018 / 2004 | Same | Same |
| AC1021 / 2007 | Same | Same |
| AC1024 / 2010 | Same | Same |
| AC1027 / 2013 | Same | Same |
| AC1032 / 2018 | Same | Same |

The historical rows test the raw safety policy for an exposed marker, not historical schema legality or a claim that embedded objects existed in those releases. Synthetic names and private fields test lexical isolation, not AutoCAD-valid entity construction.

## Executed evidence

234 new registered cases: nine raw profiles, both independent fixture encoders, MTEXT/ATTRIB/ATTDEF/unknown entity names, control-looking embedded values, direct unsafe-remap probes and four context-boundary controls. They cover ordered slot classification, absence of false diagnostics, unchanged enclosing references, closure evidence, affected-slot rejection, unrelated edits, exact original bytes and cross-transport output.

The same final tests against unchanged pre-fix production report **16,603 passed / 162 failed** in both local Debug and Release. Corrected separately compiled signed production reports **16,765 passed / zero failed**, both configurations, using .NET 8.0.31. The 72 boundary-control cases already passed on the baseline; no assertions were weakened. This continuation's red and green source/test sets differ only by the production context fix.

`verify_raw_embedded_handles.py` checks four retained 2018 transport/control fixtures with ezdxf 1.4.4's independent ASCII/binary tag decoders. All 48 embedded tag/value pairs, neighboring enclosing references and the isolated following-POINT rename match exactly in both configurations. The verifier initially used a non-universal-newline StringIO for CRLF text; selecting normal universal-newline input corrected that harness error without stripping values or changing assertions. This is tag-level verification, not ezdxf semantic AUDIT or native AutoCAD execution. Final-head Linux/Windows SDK, netstandard2.0, documentation integrity and source audit remain required before merge.

## Primary references

- [Autodesk common entity codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm): context-qualified owner/reactor/application data.
- [ezdxf DXF tag internals, Embedded Objects](https://ezdxf.readthedocs.io/en/stable/dxfinternals/dxftags.html#embedded-objects): observed separator, appended object data and reuse of nonzero group codes; it explicitly distinguishes observations from assumptions about XData placement.
- The Autodesk ObjectARX developer-guide page linked by that primary implementation documentation could not be fetched during this audit. The change does not depend on treating undocumented embedding grammar or that page's quoted text as a complete standard schema.
