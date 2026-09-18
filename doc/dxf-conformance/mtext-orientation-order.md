# MTEXT orientation declaration order

This C# reader correction follows the text chunking and preflight tasks. It
honors a later primary MTEXT direction declaration over an earlier group 50
rotation, while a later rotation still supersedes an earlier direction. The
previous implementation remembered that a rotation had appeared anywhere and
always used it, even after receiving a later direction vector.

The change is confined to the primary MTEXT orientation-selection state. It
retains the existing degree-based rotation property and WCS-direction-to-OCS
conversion. Each primary 11/21/31 component selects direction interpretation;
group 50 selects angle interpretation. Existing component accumulation and
fallback behavior are unchanged. Column height packets are consumed by the
column parser before this decision, so their group 50 values are not mistaken
for rotation. Embedded column placement/direction fields remain in their own
packet and do not overwrite explicit primary placement.

## Focused qualification

The same **1,088 cases** pass **544 before** and **1,088 after** the correction.
They cover eight declaration sequences, four normals (including an oblique
normal), direct/nested MTEXT, six typed DXF profiles, both transports, direct
manual-height columns from R2007 and embedded manual-height columns in R2018.
Angle-only, vector-only, both orders, repeated declarations, negative angles and
multi-turn angles are included. Each positive case performs two alternating
text/binary save/reload cycles, checking content, placement, dimensions, normal,
spacing, attachment, XData, following entities and column heights/storage.

The fixtures construct WCS vectors from explicit bases without using the
reader's transformation helper. The independent Python checker regenerates
those vectors using ezdxf's OCS implementation and ordinary trigonometry. It
uses the documented last-declaration rule explicitly, not the independent
reader's own conflicting angle/vector preference.

`tools/verify_mtext_orientation_order.py` examines **1,088 input/output pairs**,
including complete ordered selected entity packets apart from entity/owner
handle values. Handle framing remains checked and all **2,176 drawings** receive
independent graph audits with zero errors or repairs. Output has one direction
vector and no competing primary rotation, so the independently loaded model
can also be checked unambiguously. The same positive validator rejects **72,960
actual-packet corruptions** and two missing/extra inventory corruptions.
Floating-point comparisons use relative `1e-13` and absolute `1e-12` bounds;
this is not bit-identical native geometry or font/layout qualification.

The three new text-checker scripts use standard universal-newline handling
when reading ASCII DXF values. This admits ordinary LF and CRLF physical line
endings without treating the CR of a CRLF pair as entity content. Binary text
values are not newline-normalized. The raw fixture writer's CRLF output exposed
this checker issue; it is not a change to DXF field-value comparison tolerances.

```sh
DXF_TEST_FILTER=mtext-orientation/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_mtext_orientation_order.py artifacts/conformance
```

Complete execution receipts are delivered separately from the focused result.

## Reference discrepancies and remaining boundaries

[Autodesk's MTEXT reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm)
explicitly describes the last angle/direction declaration winning. That rule is
the contract implemented here. The same page calls group 50 radians, whereas
this library's long-standing code and
[ezdxf's MTEXT documentation](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mtext.html)
use degrees. This task does not change units. Ezdxf's documentation also states
that the direction has higher priority, unlike Autodesk's order rule. Therefore
passing the independent output check does not establish that different native
producers interpret every conflicting input identically.

The six typed profiles remain R2000/R2004/R2007/R2010/R2013/R2018. Inputs are
synthetic, not retained native producer samples. General malformed orientation
admission, all historical typed dialects, private caches, complete column layout,
font rendering, native AutoCAD open/AUDIT/save/reopen and universal DXF parity
remain outside this correction.
