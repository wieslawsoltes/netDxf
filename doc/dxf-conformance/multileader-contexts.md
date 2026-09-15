# Stored MULTILEADER contexts and MLEADERSTYLE objects

The typed reader previously skipped MULTILEADER entities and retained MLEADERSTYLE only through the generic object boundary. This module adds an editable `MultiLeader` entity, its nested stored context, and a registered `DxfMLeaderStyle` object. It preserves the independently stored entity, context, leader, and style values without evaluating their precedence or synthesizing geometry.

## Qualified profiles and grammar

The qualified typed profiles are R2007, R2010, R2013, and R2018 in text and binary. R2000/R2004 MULTILEADER input and typed output reject; older-profile MLEADERSTYLE objects continue through the existing opaque object path. R2007 is a conservative qualification boundary, not a claim about the first AutoCAD release that implemented the feature. ezdxf's entity-level minimum-export metadata alone is not evidence of historical native support.

| Component | Public model and wire contract |
| --- | --- |
| Entity | `MultiLeader.Properties`; AcDbMLeader effective grammar 2, optional stored version group 270, typed scalar/vector fields and style, linetype, text-style, arrow/block references. |
| Context | `MultiLeader.Context`; exactly one `300 CONTEXT_DATA{` / `301 }` packet, stored scale/base point/text size/arrow size/gap, plane axes/origin, presence flags and attachment values. |
| Text | `MLeaderMTextContent`; stored text, text-style reference, direction/normal/location, dimensions/rotation/color/background/column data and ordered repeated 144 heights. |
| Block | `MLeaderBlockContent`; block-record reference, normal/location/scale/rotation/color, and an independently stored matrix that is absent or contains exactly sixteen group-47 values in wire order. |
| Leader node | `MLeaderNode`; `302 LEADER{` / `303 }`, independent last-point/dogleg flags and values, ordered branch break pairs, node index/length and owned lines. |
| Leader line | `MLeaderLine`; `304 LEADER_LINE{` / `305 }`, ordered vertices, indexed break groups and line-level arrow/linetype/lineweight/color/size/override values. |
| Repeated overrides | `MLeaderArrowHead` retains ordered 94/345 pairs. `MLeaderBlockAttribute` retains ordered 330/177/44/302 associations with exact ATTDEF identities. |
| Style | `DxfMLeaderStyle.Properties`; typed published stored parameter set, text-style and optional arrow/block references. The dictionary key is the name; group 3 is an independent description. Stored envelope group 179 is either absent or explicitly 2. |

`MultiLeader.StoredVersion` and `DxfMLeaderStyle.StoredEnvelopeValue` retain absent envelope markers without changing the effective grammar. `MLeaderLine.StoredColor` retains absent group 92; the existing `Color` getter supplies effective ByBlock when it is absent, and assigning `Color` makes the field explicit.

Entity groups 271/272/273, context groups 272/273, and leader-node group 271 are nullable R2010+ fields. Entity group 295 is nullable R2013+. Style groups 271/272/273 are available in all four qualified profiles, following the pinned producer's separate style schema. Null omits a gated field. Style group 293 (`HasBlockScaling`) is nullable because absence has no known producer default; import does not turn an absent value into `false`. Optional zero handles normalize to absent references. Wholly absent ordinary fields use documented model defaults; supplied independent redundant values remain independent.

Coordinates and directions retain their stored magnitude. Angles in these component properties are radians; colors are signed DXF raw-color integers rather than ACI indices. Strings preserve Unicode, literal backslashes, Unicode-looking escape spellings and MTEXT formatting through the shared DXF string encoding. Setters reject nonfinite values, NUL/CR/LF, and unpaired UTF-16 surrogates. MTEXT paragraphs use formatting sequences rather than literal line breaks.

The parser validates structural order where group numbers acquire their meaning from nesting. It rejects duplicate scalar fields, partial vectors, missing or mismatched delimiters, multiple contexts, incomplete arrow/attribute/break packets, invalid matrix counts, missing or incorrectly typed references, mismatched content flags, unsupported envelopes and unknown packet fields. XData terminates the entity payload; it cannot splice two pieces of a packet together. Scalar vector components may be reordered, but ordered point/break packets must contain complete coordinate triples. Count-free repeated sequences retain every physical item and its order.

## Authoring and references

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
var style = new DxfMLeaderStyle();
style.Properties.TextStyle = document.TextStyles["Standard"];
document.Objects.AddMLeaderStyle("Notes", style);

var leader = new MultiLeader();
leader.Properties.Style = style;
leader.Properties.TextStyle = document.TextStyles["Standard"];
leader.Properties.LeaderLinetype = document.Linetypes["Continuous"];
leader.Properties.ContentType = 2;
leader.Context.MText = new MLeaderMTextContent
{
    Style = document.TextStyles["Standard"],
    Text = "Stored note",
    Position = new Vector3(10, 20, 0)
};
var node = new MLeaderNode();
var line = new MLeaderLine();
line.Vertices.Add(new Vector3(0, 0, 0));
line.Vertices.Add(new Vector3(5, 10, 0));
node.Lines.Add(line);
leader.Context.Leaders.Add(node);
document.Entities.Add(leader);
```

Register referenced styles, linetypes, blocks and ATTDEFs before adopting a MULTILEADER. Registered components require exact target identities in the same document. An attribute association must point into the embedded content block. Adding a detached leader validates its complete schema and references before assigning handles; foreign-owned XData registries reject before adoption. `Validate()` uses the owning document's selected profile. Save preflight checks every block and database style before writing the first output byte.

Value components belong to one parent. Detached drafts may be cloned before their required references are assigned; validation remains mandatory on registration and output. `Clone()` makes independent components, collections and XData while retaining the referenced document objects. Map every external reference explicitly before adding a clone to another document. MLEADERSTYLE participates in the existing two-pass database graph clone: value data are independent, internal graph references are remapped, and external cross-document references require explicit replacements. `AddMLeaderStyle` creates or uses `ACAD_MLEADERSTYLE` and rejects an occupied name. The typed style itself can participate in other valid dictionary graphs.

Reference queries derive MULTILEADER uses from the current nested data, so changes to mutable lists and optional content release their references immediately. Multiple slots referencing one resource contribute to its use count. An entity's ordinary graphics reference and nested MULTILEADER reference merge into one count entry. Removal protects referenced STYLE/LTYPE/BLOCK records and referenced ATTDEFs, including ATTDEF indexer replacement. Foreign same-name table instances cannot bypass that protection. STYLE/LTYPE/BLOCK rename indexes commit after observers accept the name and recheck the current owner; throwing observers and callback-driven attachment, detachment, moves, or collisions preserve coherent indexes. Failed anonymous-block renames preserve their flags. Removing an unreferenced block unregisters its block, block-record and child handles while retaining the block-record identity and child membership, allowing the same populated block to be added again. Existing entity-specific removal behavior, such as dimension/leader/hatch association teardown, still applies; this change does not claim a general graph-preserving erase operation.

For native packet extraction, optional envelope/color presence tests and opaque-style qualification, see [Native MLEADER input envelopes and private styles](mleader-native-inputs.md).

## Boundaries and evidence

This is a stored-schema implementation. It does not evaluate style override bits, format or render leaders, rebuild an anonymous block, resolve annotation-scale contexts, or provide tolerance content. One context and content types 0 (none), 1 (block), and 2 (MTEXT) are qualified. Private/unknown MLEADERSTYLE group 298 and private style subclasses retain the entire object opaquely, with pending typed fixups rolled back. Typed MULTILEADER references still require a compatible typed style. Other explicit envelope versions, malformed known style fields, extra context grammars and unknown entity packet fields reject.

`TransformBy` accepts only the exact identity operation. Every component of Matrix3 plus translation and all sixteen Matrix4 entries are checked, including calls through `EntityObject`. Tiny nonzero changes, NaN/infinity, and nonidentity fourth rows reject before mutation. Applications can edit stored coordinates intentionally; the library does not partially transform redundant geometry.

`tests/fixtures/mleader` contains four independently authored ezdxf 1.4.4 drawings, the native-model generator, raw source hashes and compiled record/context snapshots. Each has text and block content, multiple nodes and lines, branch and indexed line breaks, repeated arrows, an ATTDEF association, deliberately different redundant values, XData and a following LINE. The original producer omits the MULTILEADER CLASS and writes an incorrect MLEADERSTYLE instance count; its audit does not report those omissions. The manifest records this limitation. netDxf writes checked class declarations and correct counts, and the verifier requires them.

The scoped executable suite exercises all six profile choices and both transports: independent inputs, repeated alternating persistence, API-authored content alternatives, component and database-graph cloning, ownership/reference/rename callbacks, exact transform rejection, before-output version validation and malformed packet cases. `tools/verify_mleader_inputs.py` independently checks eight persisted source fixtures: sixteen contexts, thirty-two nodes, forty lines, exact nested wire packets, redundant fields, style presence, resource graphs, declarations and zero ezdxf audit errors or repairs. `tools/verify_mleader_authored.py` checks eight authored drawings containing no-content, repeated-column-height MTEXT, and block content with absent matrix. Independent scratch review also challenged twelve parser cases: two valid controls and ten malformed boundaries, without relying on netDxf's reader as the oracle.

Sources: Autodesk's [MULTILEADER overview](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-72D20B8C-0F5E-4993-BEB7-0FCF94F32BE0.htm), [common fields](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-69B9139A-48B4-48A5-B3CF-A3233ABFBE49.htm), [context fields](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-EC56D0DE-026D-46AB-87B1-9692393B0C22.htm), [leader nodes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-8648B8F7-5BD3-445B-A1B2-6F65EC4ECB3E.htm), [leader lines](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-B2E6436A-F17D-4F59-9DE8-DBDB61AD36C6.htm), and [MLEADERSTYLE](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-0E489B69-17A4-4439-8505-9DCE032100B4.htm), together with the pinned [ezdxf 1.4.4 schema/parser](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/mleader.py). Autodesk's tables and the producer's executable grammar are both used because repeated group numbers and nesting require context beyond a flat code list.
