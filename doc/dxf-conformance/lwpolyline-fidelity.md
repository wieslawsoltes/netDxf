# LWPOLYLINE width and vertex identifier fidelity

`Polyline2D` retains the optional LWPOLYLINE constant width (group 43), each optional start/end width (40/41), and each optional vertex identifier (91). The reader no longer expands group 43 into per-vertex widths and loses the original representation. The writer emits each of these fields only when present, including explicit zero. This preserves conflicting constant and variable widths for downstream consumers instead of silently choosing one representation while saving.

| Public property | DXF field | Absent value | Qualified output versions |
| --- | --- | --- | --- |
| `Polyline2D.ConstantWidth` | 43 | `null` | 2000, 2004, 2007, 2010, 2013, 2018 |
| `Polyline2DVertex.StartWidthOverride` | 40 | `null` | All six versions |
| `Polyline2DVertex.EndWidthOverride` | 41 | `null` | All six versions |
| `Polyline2DVertex.VertexIdentifier` | 91 | `null` | 2013, 2018 |

Group 91 is an opaque signed 32-bit stored value. Zero, negative values, and integer limits are preserved; this module does not allocate identifiers or interpret application identity rules. The 2013/2018 output restriction is a conservative database-family qualification boundary, **not a claim about the first product release that supported group 91**. Autodesk's 2015 documentation explicitly includes this field; that release uses the AC1027/2013 file family. Earlier file families were not established by the primary sources used for this module. Reading retains identifiers even with older headers. Saving those identifiers in an earlier profile fails before bytes are written, until callers explicitly remove them.

## Width interpretation and compatibility

Autodesk's current LWPOLYLINE table gives contradictory precedence statements: group 43 is described as unused with variable widths, while groups 40/41 are described as unused with constant width. The ezdxf 1.4.4 entity source calls out this discrepancy, and its `TraceBuilder.from_polyline` uses a nonzero constant width for both ends of every segment. Its source also notes a different BricsCAD interpretation. Consequently, this implementation separates exact storage from the qualified effective-width policy.

`GetEffectiveStartWidth(index)` and `GetEffectiveEndWidth(index)` follow the ezdxf trace policy: a positive group 43 takes precedence; absent or zero group 43 uses that vertex's raw width, with absent raw widths defaulting to zero. Setting `ConstantWidth` never changes the stored vertex widths. Widths must be finite and nonnegative. Signed floating-point zero has the same numeric meaning as zero; its text spelling and IEEE sign bit are not part of the fidelity guarantee.

The existing `StartWidth` and `EndWidth` properties remain raw numeric vertex values, defaulting to zero when the corresponding field is absent. Assigning either property, even zero, marks that field present. The nullable override properties add explicit presence/removal controls. Code that used vertex widths after loading a constant-width source should use the new effective-width getters: the old reader had destructively flattened group 43 into the vertex values. Preserving both representations requires exposing them separately.

The existing `SetConstantWidth(width)` method retains its established editing behavior: set every raw start/end width to the supplied value. It now also clears group 43 so an earlier constant width cannot mask that edit. To preserve the vertex fields while setting the DXF constant-width field, use the `ConstantWidth` property.

```csharp
var polyline = new Polyline2D(new[]
{
    new Polyline2DVertex(0, 0) { StartWidthOverride = 0 },
    new Polyline2DVertex(5, 0) { EndWidthOverride = 2 }
});
polyline.ConstantWidth = 1.5;          // group 43; leaves groups 40/41 intact
polyline.Vertexes[0].VertexIdentifier = 17; // save with DXF 2013 or later
polyline.Vertexes[0].StartWidthOverride = null; // remove just group 40
// Effective segment width remains 1.5 while ConstantWidth is positive.
```

`Explode()` and `PolygonalVertexes()` retain their existing centerline behavior. A line/arc decomposition or polygonal centerline does not represent either constant or tapered stroke width. No `GetConstantWidth()` or `ToPolyline()` method exists on this repository's `Polyline2D`. Smoothing writes the older POLYLINE representation, so saving a smoothed entity with an explicit group 43 or vertex identifiers fails before output rather than dropping those LWPOLYLINE fields. Callers can explicitly bake a uniform width with `SetConstantWidth` and clear identifiers before selecting that representation.

## Editing and parsing guarantees

Reversal keeps identifiers with their point positions. Bulges and optional widths follow outgoing segments; reversed widths swap start and end, preserving absent versus explicitly present zero independently. Double reversal restores all stored values and presence. Vertex/entity/block cloning creates independent copies. `Insert.Explode()` clones and transforms a contained polyline, retaining these fields.

Uniform scaling in the polyline plane scales group 43 and all present raw widths, including widths currently masked by group 43. Missing values remain absent. The guard rejects nonuniform plane scaling, shear, singular transforms, a transformed normal no longer perpendicular to the plane, and reflections of wide arc segments before changing the entity. Normal-axis scaling may differ from plane scaling. These guards do not broaden the existing support for transforming zero-width bulged geometry.

The packet reader allows 40, 41, 42, and 91 before or after a vertex's group 20, once its group 10 has begun. It preserves the existing strict group 90 count and complete XY packet validation. Orphan or duplicate 91, duplicate 43, duplicate per-vertex 40/41, and negative/nonfinite widths are rejected. XData and the following entity remain separate. The same reader/writer logic handles text and binary transports.

## Verification

The normal conformance harness registers 328 focused cases in addition to the existing 312 LWPOLYLINE integrity/reversal cases. The module covers all six output profiles and both transports, absent/zero/positive constant widths, conflicting variable widths, optional field ordering, malformed packets, down-save rejection before output, smoothing rejection, cloning, reversal, centerline compatibility, and transactional transform guards.

Six committed input files in `tests/fixtures/lwpolyline-fidelity` were independently authored with ezdxf 1.4.4. Their producer patches only the LWPOLYLINE subclass after ezdxf document creation because ezdxf does not preserve individual width-field absence or group 91 in its entity model. The manifest records SHA-256 and expected semantics. These files run in the normal suite without an opt-in environment variable.

Run the independent gate after the conformance executable:

```sh
python tools/verify_lwpolyline_fidelity.py artifacts/conformance
```

The gate requires exactly 108 authored original/reversed/restored exports and 12 external-input exports. It reads raw tags independently with ezdxf, checks versions/transports and exact optional presence, audits the documents, compares external trace geometry, and samples 2,448 reversed arc/taper cross-sections and offset boundaries. This establishes serialization, numeric geometry, and ezdxf interoperability within the stated profiles. It does not claim licensed AutoCAD or BricsCAD rendering/evaluation.

## Primary references

- [Autodesk 2015 LWPOLYLINE DXF group codes](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
- [Autodesk LWPOLYLINE DXF group codes](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
- [ezdxf LWPOLYLINE documentation](https://ezdxf.readthedocs.io/en/stable/dxfentities/lwpolyline.html)
- [ezdxf 1.4.4 LWPOLYLINE source](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/lwpolyline.py)
- [ezdxf 1.4.4 trace geometry source](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/render/trace.py)
