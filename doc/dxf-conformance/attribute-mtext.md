# DXF 2018 multiline attributes

`Attribute` and `AttributeDefinition` expose `HasMText`, `GetMText()` and
`SetMText(AttributeMText)`. The embedded content is not a database entity.
`GetMText()` returns an independent content/style snapshot; edit that snapshot
and call `SetMText` to publish. The setter copies rather than adopts its argument.
Passing null explicitly removes the embedded content. Neither operation guesses
or rewrites the host's single-line Value, Style, height, alignment or position.

```csharp
var definition = new AttributeDefinition("NOTES") { Value = "Single-line fallback" };
definition.SetMText(new AttributeMText
{
    Value = "First paragraph\\PSecond paragraph",
    Position = new Vector3(10, 20, 0),
    Height = 2.5,
    RectangleWidth = 30,
    Style = new TextStyle("NOTES_BODY", "romans.shx")
});
```

## Wire and ownership contract

The reader distinguishes AcDbText, AcDbAttribute/AcDbAttributeDefinition,
AcDbXrecord and group 101 Embedded Object. Repeated code numbers in these scopes
are independent. Group 280 version/position-lock values are read and written as
Int16, in both transports. Optional `IsPositionLocked` distinguishes absent,
false and true. Version 0, unused field length, attribute type, secondary flag
and secondary alignment are retained separately from the fallback fields.

Embedded fields cover logical content and chunking, WCS insertion, height,
reference/defined/actual extents, style, extrusion, direction, optional rotation,
attachment, flow, line spacing and background data. Optional scalar presence and
ordered direction/rotation fields survive unchanged resaves. Rotation follows
netDxf's existing degree-valued DXF MTEXT contract. Content chunks are concatenated
before Unicode escape decoding. No font is opened by this feature.

Embedded style resources register independently of fallback styles. Existing
same-named destination styles are canonical, matching ordinary netDxf resource
adoption. Reference counts cover both uses, replacement, removal, attribute Sync
and block lifetimes. A retrieved snapshot has an independent style. Shared style
renames are reflected by subsequent snapshots and serialized names.

Copies of definitions and attributes have independent content. INSERT.Clone
binds copied attributes to the copied canonical block definition when the source
was canonically bound; orphan/noncanonical source definitions retain their
existing clone behavior. INSERT.Explode emits MTEXT rather than discarding the
multiline content in favor of its fallback. Parent graphical properties and array
cell offsets apply to that emitted MTEXT.

## Editing and failure policy

Translation, proper rotation and positive uniform scale are supported. Embedded
geometry and stored extents transform together; inline text formatting is not
reflowed. Shear, reflection, nonuniform/collapsed transforms and auxiliary records
without a transform schema reject. Embedded transform candidates are validated
before host publication. The existing two-phase INSERT transform preserves live
attribute identity and style uses. TransformAttributes resets definition geometry
while retaining instance content/style/background, so repeated Sync does not
accumulate placement transforms. This is not a transactional rewrite of all Sync
collection callbacks or arbitrary dependencies.

The writer rejects embedded/auxiliary state below R2018 before preprocessing or
output writes. Stored version/lock data uses a conservative R2010+ output profile.
R12 selection explicitly rejects multiline, locked and nonzero-length attribute
semantics rather than dropping them. Clear unsupported state explicitly before
attempting a reduced-format projection; no automatic flattening is implied.

Reader limits are 16,777,216 aggregate string code units and 131,072 embedded tags,
plus 4,096 auxiliary tags per attribute. Duplicate embedded scalars, malformed or
repeated markers, partial/zero directions, invalid numeric/layout values and
missing/extra terminal content reject. Ordinary raw framing/surrogate rules still
apply to embedded strings. Unknown embedded field schemas (including columns)
are not guessed. Pointer-free AcDbXrecord payloads are retained as auxiliary data;
pointer-bearing auxiliary schemas require explicit mapping and currently reject.

This is stored/editable multiline attribute support, not full AutoCAD parity,
font shaping, line wrapping, layout regeneration, arbitrary column semantics,
annotation contexts or automatic fallback text synchronization.

## Verification

`AttributeMTextTests.cs` exercises actual C# read/write, snapshot isolation,
resource lifecycle, cloned definition binding, Sync, transformations, malformed
input and down-save refusal. Twelve byte-pinned ezdxf 1.4.4 inputs deliberately
separate fallback and embedded styles, positions, heights and content. Their
provenance/hashes are in `tests/fixtures/attribute-mtext/manifest.json`.
`verify_attribute_mtext.py` requires 90 generated text/binary drawings, compares
embedded payloads to those independent inputs, checks typed geometry/resources,
rejects payload corruptions and requires no independent audit errors or repairs.
Checker unit tests are separate from actual library execution. Final execution
and package qualifications belong to the PR's source-bound receipts.

References: Autodesk ATTRIB/ATTDEF DXF group-code documentation; ezdxf 1.4.4
`src/ezdxf/entities/attrib.py` (EmbeddedMText and attribute export scopes).
