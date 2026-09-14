# Named page setups and wipeout variables

`DxfDocument.Objects` supports editable standalone `PLOTSETTINGS` and
`WIPEOUTVARIABLES` objects. Named page setups and the `AcDbPlotSettings` payload
embedded in `LAYOUT` share the same reader, writer, and `PlotSettings` model.
This is storage and reference support; netDxf does not evaluate a printer driver,
render a shaded plot, or resolve PC3/CTB/STB files.

```csharp
var settings = new PlotSettings
{
    PlotterName = "Office.pc3",
    PaperSizeName = "Custom A3",
    PlotType = PlotType.Window,
    WindowBottomLeft = new Vector2(-10, -20),
    WindowUpRight = new Vector2(200, 300),
    StandardScaleType = 25, // the stored group-75 code for 1:50
    StandardScaleFactor = 0.02
};
DxfPlotSettingsObject page = doc.Objects.AddPlotSettings("Review", settings);
page.Settings.Origin = new Vector2(5, 7);
doc.Objects.SetWipeoutVariables(displayFrame: true);
```

The helper copies the supplied payload, writes its page-setup name from the
dictionary entry name, and inserts it under `ACAD_PLOTSETTINGS` in the named
object dictionary. Existing names and incompatible root entries are rejected.
`DxfPlotSettingsObject.Settings` exposes that object's independent mutable copy.
The lower-level object constructor and dictionary API also support storage under
application dictionaries. A dictionary owner is required.

`SetWipeoutVariables` creates or edits the canonical `ACAD_WIPEOUT_VARS` entry;
repeated edits retain its handle and identity. `GetWipeoutVariables` returns the
typed object or null when the entry is absent or opaque. `DisplayFrame` preserves
the object schema's group-70 values 0/1, also defined by
[ezdxf's wipeout schema](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/image.py).
This is separate from newer header frame
controls that have additional values. Both helpers establish owner reactors.

## Field fidelity and compatibility

The codec retains strings, margins, paper dimensions, plot/image origins,
window coordinates, flags, paper units and rotation, plot type, custom scale,
standard scale, and shade settings. New properties preserve three fields that
the previous embedded implementation collapsed or omitted:

| Property | DXF field | Behavior |
| --- | --- | --- |
| `StandardScaleType` | 75 | Retains all 33 published values, 0 through 32. |
| `StandardScaleFactor` | 147 | Retains an explicit finite real independently of the custom scale ratio. |
| `ShadePlotObject` | 333 | Optional reference to a registered nongraphical object. |

The existing `ScaleToFit` property remains available. It reads true for code 0;
setting true selects 0, while setting false retains an existing nonzero code or
selects code 16 when changing from fit. Existing `PrintScale` still represents
the custom numerator divided by the denominator. A null `StandardScaleFactor`
preserves the old authoring behavior of writing `PrintScale`; an imported
explicit field is stored independently. Zero and negative finite explicit
values are retained as data without asserting a useful plotting interpretation.
Custom numerator and denominator retain their existing positive-value contract.

The [current Autodesk PLOTSETTINGS reference](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-DXF/files/GUID-1113675E-AB07-4567-801A-310CDE0D56E9.htm)
contains duplicated corner descriptions for groups 48/140 and 49/141. The
implemented mapping follows the explicit independently implemented coordinate
fields in [ezdxf 1.4.4's layout schema](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/layout.py)
and asymmetric producer fixtures:

| Coordinate | Group |
| --- | --- |
| Lower-left X | 48 |
| Lower-left Y | 49 |
| Upper-right X | 140 |
| Upper-right Y | 141 |

This is an interoperability inference, not resolution of the contradictory
primary wording through native CAD testing. Older netDxf versions wrote lower
Y to 140 and upper X to 49. Such previously generated files now follow the
coordinate mapping above when loaded; their old logical corners can change.
There is no reliable file provenance marker on which to base an automatic
legacy interpretation. Both standalone and embedded settings use this mapping.

The meaning of 147 also differs between the current Autodesk description and
ezdxf's `unit_factor` interpretation. The API therefore stores the field without
deriving a new value from 75. The independent zero-audit corpus uses positive
147 values: ezdxf normalizes nonpositive `unit_factor` values, so it cannot
qualify their native meaning. Separate netDxf tests cover their wire retention.

## Profiles and references

Core page settings and wipeout variables are tested in AutoCAD 2000, 2004,
2007, 2010, 2013, and 2018 text and binary transport profiles. This establishes
roundtrip interoperability with the producer, not historical native legality
of every modern field. The current Autodesk reference does not date shade
groups 76–78, and ezdxf's default field-version metadata does not establish their
introduction. The shared writer retains the existing embedded writer's emission
of 76–78 for all six profiles. Historical legality of these shade fields in the
oldest profiles remains partially qualified.

A nonnull 333 reference requires the conservatively qualified **2007 or later
export profile**. This is a tested export boundary, not a claim that 2007 was the
first release supporting this field. The reader can retain an older-profile
reference for inspection; promote the document before saving. Downgrade is
rejected before stream output or database allocation. The independent 333
fixtures cover 2007 onward and point to an actual retained `VISUALSTYLE` object.
Its private rendering payload remains opaque; typed settings do not interpret
or execute it.

References are resolved after all objects are registered. A missing reference,
a graphical target, or a target from another document is rejected. Save
preflight covers both standalone objects and embedded layouts before writing
bytes. Correcting a rejected reference permits retry without losing caller
state. Literal backslashes and Unicode-escape-looking strings survive both
transports; invalid Unicode, CR/LF/NUL framing, nonfinite numbers, duplicate
fields, invalid enumerations, and invalid shade DPI are rejected.
Omitted scalar fields use the existing `PlotSettings` defaults and are
materialized on output; an absent or zero 333 field maps to a null reference.

`Objects.CloneObject(source, destinationDictionary, name, externalReferences)`
copies a registered object's ownership subtree through the existing two-pass
database clone path. It maps the source owner to the destination dictionary,
remaps internal references/reactors/XData 1005 handles, and requires explicit
cross-document mappings for external targets such as shade objects. Payloads
are independent copies. Mapping keys use exact object identity; an equal name
on a table record from another document does not identify the source record.
The operation snapshots caller mappings and rechecks
destination availability after caller enumeration before registering clones.
It never replaces an occupied name or transfers source ownership. Existing
dictionary `Clone` and extension-dictionary clone APIs retain their behavior.
The ordinary `PlotSettings.Clone()` remains a payload copy; its shade reference
continues to refer to the same target until explicitly remapped.

Private or additional standalone subclasses fall back to `DxfOpaqueObject`
preservation. Malformed fields in a recognized public schema are rejected.
Embedded layout data cannot use that standalone fallback; unexpected plot
payload fields are rejected instead of silently discarded. This is not a claim
of support for arbitrary private plot settings.

## Reproducible checks

`OutputSettingsTests.cs` registers the focused cases in the normal conformance
runner. Tests cover all standard scale codes in all profiles/transports,
literal strings, exact stored 147 independence, wipeout edits, external fixture
loading, 333 resolution, clone mappings and callback failure, validation and
retry, profile rejection/promotion, and malformed standalone/embedded packets.

`tests/fixtures/output-settings/generate_fixtures.py` uses ezdxf 1.4.4 to produce
six source drawings, each containing 33 page setups, an embedded paper layout,
wipeout variables, XData, owner reactors, and sentinel geometry. SHA-256 hashes
and actual profile metadata are committed in its manifest. The producer's
`PlotSettings.export_entity` omits its declared optional 333 field; the generator
explicitly inserts that field after serialization for 2007+ and links it to a
native ezdxf-created `VISUALSTYLE`. It does not load and re-save that augmentation.
All six producer files pass independent audit without repairs.

```sh
dotnet run --project tests/netDxf.Conformance
python tools/verify_output_settings.py artifacts/conformance
```

The independent verifier requires exactly 24 output drawings and exactly six
source profiles with the pinned producer identity and hashes. It checks actual
transport signatures and version headers, 792 named page setups, 24 embedded
layouts, ordered plot payloads including floating-point bits, coordinate
mapping, owner and handle persistence, XData/reactors, 333 targets, wipeout
state, class declarations/counts, sentinel geometry, and zero audit changes.
These fixtures do not establish native AutoCAD plotting or printer output.
