#!/usr/bin/env python3
"""Verify twelve GEODATA v2 outputs with source hashes and independent parsing."""
from pathlib import Path
import argparse
import hashlib
import io
import json
import struct
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler


def check(value, message):
    if not value:
        raise ValueError(message)


def normalized(value):
    if isinstance(value, str):
        return decode_dxf_unicode(value)
    if isinstance(value, float):
        return struct.pack("<d", value)
    if isinstance(value, (tuple, list)):
        return tuple(normalized(item) for item in value)
    return value


def decode_once(value):
    return decode_dxf_unicode(value).encode("utf-16", "surrogatepass").decode("utf-16")


def geodata_body(path):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else (
        ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None)))
    records = []
    record = None
    active = False
    for tag in tag_compiler(loader):
        if tag.code == 0:
            if tag.value == "GEODATA":
                record = []
                records.append(record)
            else:
                record = None
            active = False
        if record is None:
            continue
        if tag.code == 100 and tag.value == "AcDbGeoData":
            active = True
        elif active:
            record.append(tag)
    check(len(records) == 1, "Expected exactly one GEODATA wire record")
    return records[0]


def compare(source, output, fixture, binary, require_instance_count=True):
    check(output.read_bytes().startswith(b"AutoCAD Binary DXF") == binary,
          "Actual transport differs from fixture name")
    before, after = ezdxf.readfile(source), ezdxf.readfile(output)
    check(after.dxfversion == fixture["version"], "Actual DXF version changed")
    left = before.modelspace().get_geodata()
    right = after.modelspace().get_geodata()
    check(left is not None and right is not None, "Modelspace GEODATA missing")
    check(right.dxf.version == 2, "GEODATA object version changed")
    declaration = after.classes.get("GEODATA")
    check(declaration.dxf.cpp_class_name == "AcDbGeoData" and
          declaration.dxf.app_name == "ObjectDBX Classes" and
          declaration.dxf.flags == 4095 and declaration.dxf.is_an_entity == 0 and
          declaration.dxf.was_a_proxy == 0, "GEODATA class declaration changed")
    if require_instance_count:
        check(declaration.dxf.instance_count == 1, "GEODATA class instance count is stale")
    check(right.dxf.handle == fixture["geodata"], "GEODATA persistence changed identity")
    check(right.dxf.block_record_handle == after.modelspace().block_record_handle,
          "GEODATA host block pointer changed")
    extension = after.modelspace().get_extension_dict().dictionary
    check(extension.dxf.owner == after.modelspace().block_record_handle and
          extension["ACAD_GEOGRAPHICDATA"] is right and right.dxf.owner == extension.dxf.handle,
          "GEODATA extension/owner chain changed")
    check(right.get_reactors() == left.get_reactors(), "Owner reactor changed")
    for field in fixture["attributes"]:
        old, new = left.dxf.get(field), right.dxf.get(field)
        if hasattr(old, "__iter__") and not isinstance(old, (str, bytes)):
            old, new = tuple(old), tuple(new)
        check(normalized(new) == normalized(old), "GEODATA field changed: " + field)
    check(normalized(right.coordinate_system_definition) == normalized(fixture["coordinate_system_definition"]),
          "Chunked XML, newlines or Unicode changed")
    check(right.get_crs() == (3395, True), "Stored XML CRS identification changed")
    check([tuple(v) for v in right.source_vertices] == [tuple(v) for v in fixture["source_vertices"]],
          "Source mesh points changed")
    check([tuple(v) for v in right.target_vertices] == [tuple(v) for v in fixture["target_vertices"]],
          "Destination mesh points changed")
    check([tuple(face) for face in right.faces] == [tuple(face) for face in fixture["faces"]],
          "Triangle order or indices changed")
    source_body, output_body = geodata_body(source), geodata_body(output)
    source_values = [(tag.code, normalized(tag.value)) for tag in source_body if tag.code not in (301, 303)]
    output_values = [(tag.code, normalized(tag.value)) for tag in output_body if tag.code not in (301, 303)]
    check(output_values == source_values, "Ordered GEODATA payload fields or XData changed")
    chunks = [tag for tag in output_body if tag.code in (301, 303)]
    check(len(chunks) > 1 and [tag.code for tag in chunks] == [303] * (len(chunks) - 1) + [301],
          "Coordinate definition continuation/final tag grammar changed")
    check(all(len(tag.value) <= 255 for tag in chunks), "Coordinate definition chunk exceeds255 characters")
    check(decode_once("".join(tag.value for tag in chunks)).replace("^J", "\n") ==
          fixture["coordinate_system_definition"], "Low-level coordinate definition text changed")
    mesh_count = [tag.value for tag in output_body if tag.code == 93]
    face_count = [tag.value for tag in output_body if tag.code == 96]
    check(mesh_count == [5] and face_count == [4], "Declared GEODATA mesh counts changed")
    check([tag.code for tag in output_body if tag.code in (13, 14)] == [13, 14] * 5,
          "Paired source/destination mesh grammar changed")
    check([tag.code for tag in output_body if tag.code in (97, 98, 99)] == [97, 98, 99] * 4,
          "Face index triple grammar changed")
    reference = after.rootdict["QA_GEODATA"]["REFERENCE"]
    check(list(reference.tags) == [(1, "geodata reference"), (330, right.dxf.handle),
                                   (340, after.modelspace().block_record_handle)],
          "Named XRECORD GEODATA/block reference changed")
    check(right.get_xdata("QA_GEODATA") == left.get_xdata("QA_GEODATA"), "GEODATA XData changed")
    lines = list(after.modelspace().query("LINE"))
    check(len(lines) == 1 and lines[0].dxf.handle == fixture["line"] and
          tuple(lines[0].dxf.start) == (20, 30, 40) and tuple(lines[0].dxf.end) == (50, 60, 70),
          "Referenced LINE identity or geometry changed")
    audit = after.audit()
    check(not audit.errors and not audit.fixes,
          f"ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")


def verify_authored(directory):
    profiles = {2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}
    names = {f"geodata-AutoCad{year}-{kind}.dxf"
             for year in profiles for kind in ("ascii", "binary")}
    check({path.name for path in directory.glob("geodata-AutoCad*.dxf")} == names,
          "Expected all six API-authored GEODATA fixtures")
    expected_definition = "x" * 252 + "🧪 Żółć\\U+0041\n" + "z" * 600
    scalar = {"version": 2, "coordinate_type": 1, "horizontal_unit_scale": 0.3048,
              "vertical_unit_scale": 0.0254, "horizontal_units": 2, "vertical_units": 1,
              "scale_estimation_method": 2, "user_scale_factor": 0.9996,
              "sea_level_correction": 1, "sea_level_elevation": -17.25,
              "coordinate_projection_radius": 6378137.0}
    vectors = {"design_point": (1.25, -2.5, 7.75), "reference_point": (11.5, 48.25, 100.125),
               "north_direction": (3, 4, 0), "up_direction": (2, 3, 4)}
    metadata = {"geo_rss_tag": "metadata", "observation_from_tag": "origin Żółć",
                "observation_to_tag": "target 東京", "observation_coverage_tag": "survey bounds"}
    for year, version in profiles.items():
        for binary in (False, True):
            kind = "binary" if binary else "ascii"
            path = directory / f"geodata-AutoCad{year}-{kind}.dxf"
            check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary,
                  "Authored actual transport differs")
            doc = ezdxf.readfile(path)
            check(doc.dxfversion == version, "Authored actual DXF version differs")
            geo = doc.modelspace().get_geodata()
            check(geo is not None and len(list(doc.objects.query("GEODATA"))) == 1,
                  "Authored GEODATA missing or duplicated")
            for field, value in scalar.items():
                check(geo.dxf.get(field) == value, "Authored GEODATA scalar changed: " + field)
            for field, value in vectors.items():
                check(tuple(geo.dxf.get(field)) == value, "Authored GEODATA vector changed: " + field)
            for field, value in metadata.items():
                check(decode_once(geo.dxf.get(field)) == value, "Authored GEODATA text changed: " + field)
            extension = doc.modelspace().get_extension_dict().dictionary
            check(geo.dxf.block_record_handle == doc.modelspace().block_record_handle and
                  extension.dxf.owner == doc.modelspace().block_record_handle and
                  extension["ACAD_GEOGRAPHICDATA"] is geo and geo.dxf.owner == extension.dxf.handle,
                  "Authored GEODATA host/owner/extension graph changed")
            check([tuple(v) for v in geo.source_vertices] == [(0, 0), (1, 0), (0, 1)] and
                  [tuple(v) for v in geo.target_vertices] == [(11, 48), (12, 48), (11, 49)] and
                  [tuple(v) for v in geo.faces] == [(0, 1, 2)], "Authored GEODATA mesh changed")
            body = geodata_body(path)
            chunks = [tag for tag in body if tag.code in (301, 303)]
            check(len(chunks) > 2 and [tag.code for tag in chunks] == [303] * (len(chunks) - 1) + [301],
                  "Authored continuation/final chunk grammar changed")
            check(all(len(tag.value.encode("utf-16-le", "surrogatepass")) // 2 <= 255 for tag in chunks),
                  "Authored UTF16 chunk limit exceeded")
            # Decode only the joined wire value, exactly once: applying the
            # decoder to expected literal\\U+0041 would incorrectly turn it intoA.
            definition = decode_once("".join(tag.value for tag in chunks)).replace("^J", "\n")
            check(definition == expected_definition, "Surrogate/literal escape/newline/chunk boundary changed")
            check(decode_once(geo.coordinate_system_definition) == expected_definition,
                  "Independent GEODATA definition interpretation changed")
            values = lambda code: [tag.value for tag in body if tag.code == code]
            check(values(93) == [3] and values(96) == [1], "Authored mesh counts changed")
            check([tag.code for tag in body if tag.code in (13, 14)] == [13, 14] * 3 and
                  [tag.code for tag in body if tag.code in (97, 98, 99)] == [97, 98, 99],
                  "Authored paired mesh/triple index grammar changed")
            declaration = doc.classes.get("GEODATA")
            check(declaration.dxf.cpp_class_name == "AcDbGeoData" and
                  declaration.dxf.app_name == "ObjectDBX Classes" and declaration.dxf.flags == 4095
                  and declaration.dxf.is_an_entity == 0 and declaration.dxf.was_a_proxy == 0
                  and declaration.dxf.instance_count == 1, "Authored GEODATA class metadata/count changed")
            audit = doc.audit()
            check(not audit.errors and not audit.fixes,
                  f"Authored ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")
            print("PASS " + path.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--sources", type=Path)
    args = parser.parse_args()
    sources = args.sources or (Path(__file__).resolve().parents[1] / "tests/fixtures/geodata")
    if not sources.is_dir():
        sources = Path(__file__).resolve().parent
    manifest = json.loads((sources / "manifest.json").read_text())
    check(len(manifest["fixtures"]) == 3, "Expected three independent GEODATA inputs")
    names = {f"independent-geodata-R{year}-{kind}.dxf"
             for year in (2010, 2013, 2018) for kind in ("ascii", "binary")}
    check({path.name for path in args.directory.glob("independent-geodata-R*.dxf")} == names,
          "Expected all six GEODATA external roundtrips")
    for fixture in manifest["fixtures"]:
        source = sources / fixture["file"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == fixture["sha256"],
              "Independent source digest changed: " + source.name)
        for binary in (False, True):
            output = args.directory / (source.stem + ("-binary" if binary else "-ascii") + ".dxf")
            compare(source, output, fixture, binary)
            print("PASS " + output.name)
    verify_authored(args.directory)
    print(f"PASS ezdxf {ezdxf.__version__}: all12 GEODATA v2 outputs (six external/six authored); "
          "complete fields/meshes/chunked definition text and ownership graph; zero audit errors/repairs")


if __name__ == "__main__":
    main()
