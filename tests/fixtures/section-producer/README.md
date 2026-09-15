# Independent SECTION entity producer packets

`producer/Program.cs` generates eight original files using the pinned NuGet package
IxMilia.Dxf 0.8.4: R2007, R2010, R2013 and R2018 in ASCII and binary transports.
The exact original bytes are retained as deterministic gzip files in
`originals-gzip/`. `source-manifest.json` records each original hash, byte count and
complete compiled entity packet. The package hash and separately pinned schema
source revision are recorded as distinct evidence; the source revision is not
claimed to be the package's build commit.

The producer writes state 7, flags 17, name `Producer section`, vertical vector
(0.25, -0.5, 2), top height 17.125, bottom height -2.75, transparency 37, indexed
color group 63 value 5 and color name `ProducerColor`. It writes three front
vertices and two back-line vertices. The geometry-settings pointer is null and
therefore omitted by this producer. This is positive stored-packet evidence;
IxMilia's permissive parser is not used as a malformed-input oracle.

`extract_fixtures.py` builds independent ezdxf 1.4.4 carriers without loading the
original files through ezdxf's high-level reader. The reader confuses the
documented SECTION entity name with a file-section delimiter. This extraction
also avoids unrelated producer scaffolding differences. Only two changes are
made to the original entity packet: handle 1D becomes F1000, and the originally
absent common owner group 330 is inserted with the generated model-space
BLOCK_RECORD identity. All remaining common fields, proxy/XData if present, and
the exact ordered AcDbSection body are retained. The generated carrier's HANDSEED
is F1001; generated CLASS packets are sorted by name and metadata is fixed.

`manifest.json` pins the original, gzip and extracted hashes, explicit
handle map, inserted owner, and equality results. For structural audit only, an
in-memory adapter changes the entity type SECTION and its CLASS name to the
observed native name SECTIONOBJECT. No scalar, geometry, count, owner or settings
field is repaired for that audit. Every extracted file on disk retains SECTION.
All eight adapted audits have zero errors and zero repairs. This does not claim
that the original complete files import unchanged, nor that a CAD application
evaluates these stored settings.

Regenerate with `python extract_fixtures.py`. Independent regeneration reproduced all original-gzip, extracted-file and
manifest hashes under PYTHONHASHSEED 0, 1, 2, 17 and 37.
