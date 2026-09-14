# Stored DIMSTYLE settings and override parity

`DimensionStyle` and `DimensionStyleOverrideType` expose `TickSize`,
`TextVerticalPosition`, and `UserPositionedText`. They retain the published
DIMTSZ, DIMTVP, and DIMUPT values in table records and ACAD/DSTYLE entity XData.
All six supported profiles (R2000, R2004, R2007, R2010, R2013, and R2018) support
these fields in text and binary transport. The defaults are zero, zero, and
false. New override enum members are appended to preserve existing member values.

| Setting | Property | DIMSTYLE / DSTYLE identifier | Header group | Override value group |
| --- | --- | --- | --- | --- |
| DIMTSZ | `TickSize` | 142 | 40 | 1040 (double) |
| DIMTVP | `TextVerticalPosition` | 145 | 40 | 1040 (double) |
| DIMUPT | `UserPositionedText` | 288 | 70 | 1070 (short 0/1) |

`TickSize` requires a finite nonnegative double; zero selects arrowheads and
positive values specify the oblique stroke size. `TextVerticalPosition` is a
finite signed multiple of text height, applicable when DIMTAD is zero. The
`UserPositionedText` flag stores CAD interactive placement behavior, independently
of a dimension entity's manually positioned text flag. Invalid field values fail
explicitly; flags accept only zero or one on input.

```csharp
var style = new DimensionStyle("Survey")
{
    TickSize = 1.5,
    TextVerticalPosition = -0.625,
    UserPositionedText = true
};
drawing.DimensionStyles.Add(style);
dimension.Style = style;
dimension.StyleOverrides.Add(new DimensionStyleOverride(
    DimensionStyleOverrideType.TickSize, 2.75));
```

The three header variables are retained through the existing custom variable
API. An explicit header value is independent of the active table style, including
zero or false. Without an explicit value, saving derives that variable from the
active dimension style. Removing a custom variable restores this fallback.
Saving emits each variable once. These rules are limited to these three names;
other active-style header derivation remains unchanged. Invalid custom types,
codes, nonfinite values, or flags fail before writer output and document setup.

```csharp
// Keep an explicit header override, distinct from the DIMSTYLE table value.
drawing.DrawingVariables.AddCustomVariable(
    new HeaderVariable("$DIMTSZ", 40, 0.0));
drawing.DrawingVariables.TryGetCustomVariable("$DIMTSZ", out var value);
drawing.DrawingVariables.RemoveCustomVariable("$DIMTSZ");
```

The parity audit also corrects DIMRND override decoding to use group 1040/double
and DIMALT override writing to use 1 for true and 0 for false. Style cloning now
retains the three new settings plus the previously omitted fixed extension-line
flag/length, text inside/outside alignment, and text direction.

This module qualifies stored data and cloning. It does not expand the dimension
block renderer or promise that the renderer reflects every setting. Existing
imported dimension geometry remains available; applications that regenerate
geometry apply their own dimension rendering behavior.

## Qualification

`tests/fixtures/dimstyle-stored-settings/generate.py` uses ezdxf 1.4.4 to produce
six independent inputs with different table, header, and entity override values,
including explicit zero, negative DIMTVP, both flag states, real DIMRND overrides,
and a following LINE. The checked-in manifest records each source SHA256.
The conformance tests load these inputs, save and reload three cycles in both
transports, and test authored values, cloning, header presence/fallback,
validation, malformed scalar input, and rejected-save retry.

`python tools/verify_dimstyle_stored_settings.py <artifact-directory>` requires
all **24** outputs (six profiles × two transports × authored/independent). It
checks source hashes, actual profile/transport, direct table/header/DSTYLE tags,
unique emission, exact real/short types and values, entity identity and following
LINE, and zero ezdxf audit errors or repairs. No native CAD rendering result is
claimed.

## Primary references

- [Autodesk DIMSTYLE group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm).
- [Autodesk DIMTSZ](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-Core/files/GUID-D225D167-A66B-4617-BC59-C1385069D152.htm), [DIMTVP](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-Core/files/GUID-248E5BFA-F355-4369-A889-291D77070B26.htm), and [DIMUPT](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-Core/files/GUID-4B271261-0596-48DE-BA45-0AB0C7379FA4.htm).
- [ezdxf 1.4.4 DIMSTYLE schema](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dimstyle.py) and [header schema](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/sections/headervars.py): DIMTSZ/DIMTVP date to R12 and DIMUPT to R2000, so no additional gate is needed inside the supported R2000–R2018 range.
