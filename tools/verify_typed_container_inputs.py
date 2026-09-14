#!/usr/bin/env python3
"""Independently compare 12 external typed-container roundtrips with ezdxf inputs."""
from pathlib import Path
import argparse
import hashlib
import io
import json
import struct
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

VERSIONS = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024",
            2013: "AC1027", 2018: "AC1032"}


def check(value, message):
    if not value:
        raise ValueError(message)


def encoded(value):
    if isinstance(value, float):
        return struct.pack("<d", value)
    if isinstance(value, (list, tuple)):
        return tuple(encoded(part) for part in value)
    return value


def wire_records(path):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else (
        ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None)))
    records, current = {}, []

    def flush():
        if not current:
            return
        for tag in current:
            if tag.code == 5:
                records[tag.value] = list(current)
                return
            if tag.code == 100:
                return

    for tag in tag_compiler(loader):
        if tag.code == 0:
            flush()
            current = []
        current.append(tag)
    flush()
    return records


def body(record):
    start = next(index for index, tag in enumerate(record) if tag.code == 100)
    return [(tag.code, encoded(tag.value)) for tag in record[start:]]


def compare(source, output, year, binary):
    check(output.read_bytes().startswith(b"AutoCAD Binary DXF") == binary,
          "Transport differs from fixture name")
    original, saved = ezdxf.readfile(source), ezdxf.readfile(output)
    check(saved.dxfversion == VERSIONS[year], "Actual DXF version changed")
    source_wire, saved_wire = wire_records(source), wire_records(output)
    original_app, saved_app = original.rootdict["QA_CONTAINERS"], saved.rootdict["QA_CONTAINERS"]
    check(set(saved_app.keys()) == set(original_app.keys()), "Named container entries changed")
    for name in ("BUFFER", "EMPTY"):
        before, after = original_app[name], saved_app[name]
        check(after.dxftype() == "IDBUFFER", name + " is not an IDBUFFER")
        check(list(after.handles) == list(before.handles), "IDBUFFER null/order/duplicates or targets changed")
        check(after.dxf.handle == before.dxf.handle, "IDBUFFER persistence changed identity")
        check(after.dxf.owner == saved_app.dxf.handle, "IDBUFFER dictionary ownership changed")
        check(after.get_reactors() == before.get_reactors(), "IDBUFFER reactors changed")
        check(body(saved_wire[after.dxf.handle]) == body(source_wire[before.dxf.handle]),
              "IDBUFFER ordered body or XData changed")
    original_order = list(original.modelspace().get_redraw_order())
    saved_order = list(saved.modelspace().get_redraw_order())
    check(saved_order == original_order, "Draw-order entries, pair order or opaque sort keys changed")
    if original_order:
        check(saved.header["$SORTENTS"] == original.header["$SORTENTS"], "SORTENTS flags changed")
        before = original.modelspace().get_sortents_table()
        after = saved.modelspace().get_sortents_table()
        check(after.dxf.handle == before.dxf.handle, "Draw-order persistence changed identity")
        check(after.dxf.block_record_handle == saved.modelspace().block_record_handle,
              "Draw-order block pointer changed")
        check(after.dxf.owner == saved.modelspace().get_extension_dict().dictionary.dxf.handle,
              "Draw-order extension dictionary owner changed")
        check(body(saved_wire[after.dxf.handle]) == body(source_wire[before.dxf.handle]),
              "Draw-order wire body changed")
    source_inserts = {insert.get_xdata("QA_CONTAINERS")[0].value: insert
                      for insert in original.modelspace().query("INSERT")}
    saved_inserts = {insert.get_xdata("QA_CONTAINERS")[0].value: insert
                     for insert in saved.modelspace().query("INSERT")}
    check(len(saved_inserts) == 8 and set(saved_inserts) == set(source_inserts),
          "Clipped INSERT inventory changed")
    check(len(list(saved.objects.query("SPATIAL_FILTER"))) == 8, "Spatial filter count changed")
    for name, before_insert in source_inserts.items():
        after_insert = saved_inserts[name]
        check(after_insert.dxf.handle == before_insert.dxf.handle, "INSERT persistence changed identity")
        before_dict = before_insert.get_extension_dict()["ACAD_FILTER"]
        after_extension = after_insert.get_extension_dict().dictionary
        after_dict = after_extension["ACAD_FILTER"]
        before, after = before_dict["SPATIAL"], after_dict["SPATIAL"]
        check(after.dxftype() == "SPATIAL_FILTER", "SPATIAL dictionary value changed type")
        check(after.dxf.handle == before.dxf.handle, "Spatial filter persistence changed identity")
        check(after_extension.dxf.owner == after_insert.dxf.handle and
              after_dict.dxf.owner == after_extension.dxf.handle and
              after.dxf.owner == after_dict.dxf.handle, "Spatial extension ownership chain changed")
        check(after_dict.dxf.hard_owned == before_dict.dxf.hard_owned, "Filter ownership flag changed")
        check(after.get_reactors() == before.get_reactors(), "Spatial owner reactor changed")
        check(body(saved_wire[after.dxf.handle]) == body(source_wire[before.dxf.handle]),
              name + ": spatial ordered wire fields or exact floating bits changed")
        check(after.boundary_vertices == before.boundary_vertices, name + ": clipping vertices changed")
        check(tuple(after.inverse_insert_matrix) == tuple(before.inverse_insert_matrix),
              name + ": inverse INSERT matrix changed")
        check(tuple(after.transform_matrix) == tuple(before.transform_matrix),
              name + ": clip matrix changed")
        for field in ("extrusion", "origin", "is_clipping_enabled",
                      "has_front_clipping_plane", "has_back_clipping_plane"):
            check(after.dxf.get(field) == before.dxf.get(field), name + ": " + field + " changed")
        # ezdxf 1.4.4 binds front_clipping_plane_distance to the last group40
        # matrix value on load. The complete wire-body comparison above checks
        # the real optional front40 value and its correct position explicitly.
        if before.dxf.has_back_clipping_plane:
            check(after.dxf.back_clipping_plane_distance == before.dxf.back_clipping_plane_distance,
                  name + ": back clipping distance changed")
    for kind in ("LINE", "CIRCLE"):
        before = list(original.modelspace().query(kind))
        after = list(saved.modelspace().query(kind))
        check(len(after) == len(before), kind + " inventory changed")
        for left, right in zip(before, after):
            for field in (("start", "end") if kind == "LINE" else ("center", "radius")):
                check(left.dxf.get(field) == right.dxf.get(field), kind + " geometry changed")
    auditor = saved.audit()
    check(not auditor.errors and not auditor.fixes,
          f"ezdxf audit reported {len(auditor.errors)} errors/{len(auditor.fixes)} repairs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--sources", type=Path)
    args = parser.parse_args()
    sources = args.sources or (Path(__file__).resolve().parents[1] / "tests/fixtures/typed-containers")
    if not sources.is_dir():
        sources = Path(__file__).resolve().parent
    manifest = json.loads((sources / "manifest.json").read_text())
    check(len(manifest["fixtures"]) == 6, "Expected six independent source fixtures")
    for fixture in manifest["fixtures"]:
        source = sources / fixture["file"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == fixture["sha256"],
              "Independent source fixture digest changed: " + source.name)
    expected = {f"independent-typed-containers-R{year}-{kind}.dxf"
                for year in VERSIONS for kind in ("ascii", "binary")}
    paths = list(args.directory.glob("independent-typed-containers-R*.dxf"))
    check({path.name for path in paths} == expected, "Expected all 12 independent container roundtrips")
    for year in VERSIONS:
        for binary in (False, True):
            kind = "binary" if binary else "ascii"
            source = sources / f"independent-typed-containers-R{year}.dxf"
            output = args.directory / f"independent-typed-containers-R{year}-{kind}.dxf"
            compare(source, output, year, binary)
            print("PASS " + output.name)
    print(f"PASS ezdxf {ezdxf.__version__}: 12 external typed-container roundtrips; "
          "96 clipping objects / 192 affine matrices; exact payload bits; zero audit errors/repairs")


if __name__ == "__main__":
    main()
