# Independent GEODATA inputs

These three DXF files were authored independently with ezdxf 1.4.4 using
`generate_fixtures.py`. They were not exported from netDxf. The producer's complete
source bytes are retained, with SHA-256 hashes and expected metadata in
`manifest.json`.

Each R2010/R2013/R2018 drawing contains version-2 GEODATA under the modelspace
BLOCK_RECORD extension dictionary, a long coordinate-system XML definition,
nonunit directions, five paired mesh vertices, four faces, common reactors and
XData, an incoming named XRECORD reference, and a LINE. All producer files passed
ezdxf audit with no errors or repairs. The generator carries the primary source
URL. Regenerating changes source IDs/timestamps and therefore requires a reviewed
manifest update.

Definition chunks can end with significant spaces. Preserve those DXF line
endings and trailing spaces: trimming a chunk boundary changes the joined XML.

Run `python tools/verify_geodata.py ARTIFACT_DIRECTORY` after the C# conformance
runner. All six text/binary independent round trips and all six authored outputs
are mandatory. The verifier
checks actual outgoing GEODATA CLASS instance counts; the unmodified ezdxf
producer uses its known default instance count of zero in the source files.
