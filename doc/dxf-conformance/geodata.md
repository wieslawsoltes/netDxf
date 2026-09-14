# GEODATA coordinate metadata

`DxfGeoData` reads, edits, writes and clones the published version-2 `AcDbGeoData`
schema in DXF 2010, 2013 and 2018, in text and binary transport. It stores
coordinate-system metadata and the geographic mesh. It does not interpret XML,
resolve EPSG identifiers, calculate reprojections, evaluate scale policies, or
render a mesh.

```csharp
using netDxf;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Units;

var drawing = new DxfDocument(DxfVersion.AutoCad2018);
var host = drawing.Blocks[Block.DefaultModelSpaceName].Record;
var geo = new DxfGeoData(host)
{
    CoordinateType = DxfGeoCoordinateType.LocalGrid,
    DesignPoint = new Vector3(100, 200, 0),
    ReferencePoint = new Vector3(500000, 4500000, 0),
    HorizontalUnits = DrawingUnits.Meters,
    VerticalUnits = DrawingUnits.Meters,
    HorizontalUnitScale = 1,
    VerticalUnitScale = 1,
    NorthDirection = Vector2.UnitY,
    UpDirection = Vector3.UnitZ,
    CoordinateSystemDefinition = "<Dictionary>\n<!-- supplied CRS metadata -->\n</Dictionary>"
};
geo.SetMesh(
    new[] {
        new DxfGeoMeshPoint(new Vector2(0, 0), new Vector2(11, 48)),
        new DxfGeoMeshPoint(new Vector2(1, 0), new Vector2(12, 48)),
        new DxfGeoMeshPoint(new Vector2(0, 1), new Vector2(11, 49))
    },
    new[] { new DxfGeoMeshFace(0, 1, 2) });
drawing.Objects.SetGeoData(geo);
drawing.Save("geodata.dxf");

// Edit existing metadata without replacing its database identity:
drawing.Objects.GetGeoData(host).SeaLevelElevation = 42.5;
```

`SetGeoData` adds the `ACAD_GEOGRAPHICDATA` entry to the host block record's
extension dictionary, retaining existing entries. Duplicate attachment and
cross-document host references fail before adoption. `GetGeoData` returns null
when the entry is absent or preserved as an opaque object. The attachment API
accepts registered block records; modelspace is the independently qualified
consumer path. Storing the metadata on another block is not a claim that every
CAD application will use it there.

The public properties retain design/reference points, horizontal/vertical units
and scales, unnormalized north/up directions, scale estimation policy, user scale,
sea-level correction/elevation, projection radius, coordinate definition, GeoRSS
and observation strings. Paired mesh members prevent unequal source/target
counts. Faces have three zero-based indices. Values must be finite, scales must
be positive, directions must be nonzero, and the projection radius cannot be
negative. `SetMesh` validates complete input snapshots before replacing either
collection. Direct collection edits may temporarily leave an out-of-range face;
`Objects.Validate()`, saving and cloning reject that state until it is repaired.

Coordinate definitions use repeated group 303 chunks followed by one final 301,
with at most 255 UTF-16 code units per emitted chunk. Chunk boundaries preserve
Unicode escape tokens and surrogate pairs; unpaired surrogates are rejected. LF line breaks use `^J`; CR and a
literal `^J` in an authored definition are rejected because the public wire form
cannot distinguish them reliably. Other metadata strings are single-line.
Backslashes and non-ASCII text use the database string encoder, so literal DXF
Unicode-looking text survives netDxf round trips.

`CloneExtensionDictionary(sourceHost, destinationHost, mappings)` copies the
complete extension graph and automatically remaps the GEODATA host reference.
The destination must have an empty extension slot. References to other objects
outside a cross-document graph need explicit mappings. Mesh collections are
independent after cloning. Copying the GEODATA graph into a named dictionary
instead of a host extension, copying to an incompatible host type, or downgrading
to pre-2010 DXF fails before registration. Common XData, reactors and other
extension entries follow the existing object-database clone rules. Erasing a
GEODATA graph is subject to the database's existing removal limitations; removing
a dictionary name alone does not erase the object or repair its references.

Implementation version 1, unknown versions, private extra payload groups, and
pre-2010 records remain `DxfOpaqueObject` payloads. They can be saved without
pretending to support their schema; opaque graph cloning explicitly fails. A
recognized version-2 public record with invalid counts, malformed triangle groups,
duplicate/missing required fields, or unresolved/wrong-type host fails loading
(an exception in Debug; the existing `DxfDocument.Load` failure result in Release).
Opaque preservation retains typed tags rather than original lexical spelling or
comments; use `DxfRawDocument` when that distinction matters.

The reader and writer use the [Autodesk GEODATA group-code reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-104FE0E2-4801-4AC8-B92C-1DDF5AC7AB64.htm),
the [ezdxf GEODATA reference](https://ezdxf.readthedocs.io/en/stable/dxfobjects/geodata.html),
and ezdxf 1.4.4's `entities/geodata.py` implementation for chunking, version gates,
and the mesh sequence. The conservative version-2 gate avoids the incompatible
version-1 mesh/north codes documented in that implementation.

Independent inputs live in `tests/fixtures/geodata`, with a reproducible ezdxf
generator and a manifest recording original SHA-256 hashes and semantic values.
The conformance runner must emit six independent-input round trips, plus six
authored version/transport drawings. `tools/verify_geodata.py ARTIFACT_DIRECTORY`
requires all twelve outputs and checks every manifest field, exact
double values, paired vertices, ordered faces, complete chunked XML, source
identity, owner/host chain, reactors, XData, incoming references, class metadata
and an independent ezdxf audit with zero repairs. These checks qualify storage
and interoperability, not coordinate accuracy or CAD rendering.
