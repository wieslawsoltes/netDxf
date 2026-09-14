#!/usr/bin/env python3
"""Independently verify twelve API-authored pointer/draw-order/clipping drawings."""
from pathlib import Path
import argparse
import ezdxf
from verify_typed_container_inputs import wire_records

VERSIONS = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024",
            2013: "AC1027", 2018: "AC1032"}


def check(value, message):
    if not value:
        raise ValueError(message)


def object_body(record):
    start = next(index for index, tag in enumerate(record) if tag.code == 100)
    return record[start:]


def inspect(path, year, binary):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Actual transport differs")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == VERSIONS[year], "Actual DXF version differs")
    records = wire_records(path)
    model = doc.modelspace()
    lines, circles, inserts = list(model.query("LINE")), list(model.query("CIRCLE")), list(model.query("INSERT"))
    check(len(lines) == 2 and len(circles) == 1 and len(inserts) == 2, "Drawing entity inventory changed")
    first, second = lines
    circle = circles[0]
    check(tuple(first.dxf.start) == (1.25, -2.5, 3.75) and tuple(first.dxf.end) == (8.5, 9.25, -4.125),
          "First LINE geometry changed")
    check(tuple(second.dxf.start) == (-10, -20, -30) and tuple(second.dxf.end) == (-40, -50, -60),
          "Second LINE geometry changed")
    check(tuple(circle.dxf.center) == (4.5, -5.25, 6.125) and circle.dxf.radius == 2.75,
          "CIRCLE geometry changed")
    graph = doc.rootdict["TYPED_CONTAINERS"]
    check(set(graph.keys()) == {"MODE", "BUFFER", "EMPTY"}, "Named graph entries changed")
    check(graph.dxf.owner == doc.rootdict.dxf.handle and graph["MODE"].dxf.value == "typed mode",
          "Graph owner or variable changed")
    buffer, empty = graph["BUFFER"], graph["EMPTY"]
    expected_refs = [circle.dxf.handle, "0", first.dxf.handle, circle.dxf.handle,
                     graph["MODE"].dxf.handle, inserts[0].dxf.handle, "0"]
    check(list(buffer.handles) == expected_refs and list(empty.handles) == [],
          "IDBUFFER duplicates/nulls/order/targets changed")
    check(buffer.dxf.owner == graph.dxf.handle and empty.dxf.owner == graph.dxf.handle,
          "IDBUFFER ownership changed")
    check(buffer.get_reactors() == [graph.dxf.handle], "IDBUFFER reactor changed")
    check(list(buffer.get_xdata("TYPED_CONTAINERS")) == [(1005, first.dxf.handle)], "IDBUFFER XData changed")
    check([(t.code, t.value) for t in object_body(records[buffer.dxf.handle])] ==
          [(100, "AcDbIdBuffer")] + [(330, handle) for handle in expected_refs] +
          [(1001, "TYPED_CONTAINERS"), (1005, first.dxf.handle)], "IDBUFFER wire body changed")
    check([(t.code, t.value) for t in object_body(records[empty.dxf.handle])] == [(100, "AcDbIdBuffer")],
          "Empty IDBUFFER invented payload")
    check(len(list(doc.objects.query("SPATIAL_FILTER"))) == 2, "Spatial filter count changed")
    for index, insert in enumerate(inserts):
        check(insert.dxf.name == "TYPED_CONTENT" and tuple(insert.dxf.insert) == (10 + index, 20, 3),
              "Clipped INSERT identity/placement changed")
        extension = insert.get_extension_dict().dictionary
        filter_dict = extension["ACAD_FILTER"]
        spatial = filter_dict["SPATIAL"]
        check(extension.dxf.owner == insert.dxf.handle and filter_dict.dxf.owner == extension.dxf.handle
              and spatial.dxf.owner == filter_dict.dxf.handle, "Spatial ownership chain changed")
        check(spatial.get_reactors() == [filter_dict.dxf.handle], "Spatial owner reactor changed")
        check(list(spatial.get_xdata("TYPED_CONTAINERS")) ==
              [(1000, "rectangle" if index == 0 else "polygon"), (1005, insert.dxf.handle)],
              "Spatial XData target changed")
        boundary = [(-2, -1), (7, 5)] if index == 0 else [(-3, -1), (5, -2), (8, 3), (3, 7), (-4, 4)]
        check([tuple(v) for v in spatial.boundary_vertices] == boundary, "Clip boundary coordinates changed")
        check(tuple(spatial.dxf.extrusion) == (0, 0.6, 0.8) and tuple(spatial.dxf.origin) == (1, -2, 3),
              "Clip coordinate frame changed")
        body = object_body(records[spatial.dxf.handle])
        codes = [100, 100, 70] + [10] * len(boundary) + [210, 11, 71, 72]
        codes += ([40] if index == 0 else []) + [73] + ([41] if index == 1 else [])
        codes += [40] * 24 + [1001, 1000, 1005]
        check([tag.code for tag in body] == codes, "Optional clipping plane/matrix field order changed")
        values = lambda code: [tag.value for tag in body if tag.code == code]
        check(values(100) == ["AcDbFilter", "AcDbSpatialFilter"] and values(70) == [len(boundary)],
              "Spatial subclasses/boundary count changed")
        check(values(71) == [index] and values(72) == [1 - index] and values(73) == [index],
              "Clip enabled/front/back flags changed")
        inverse = [1, 0, 0, -10 - index, 0, 1, 0, -20, 0, 0, 1, -3]
        transform = [1.25, 0.375, -0.25, 7.5 + index,
                     0.125, 1.5, 0.625, -11.25, 0.25, -0.125, 2.25, 3.125]
        # High-level ezdxf1.4.4 misreads the optional front40 as the final matrix
        # value. Verify the full ordered40 sequence directly, including2.5.
        check(values(40) == ([2.5] if index == 0 else []) + inverse + transform,
              "Front distance or complete affine matrix values changed")
        check(values(41) == ([] if index == 0 else [-7.25]), "Optional back distance changed")
        check(tuple(spatial.inverse_insert_matrix.transform((0, 0, 0))) == (-10 - index, -20, -3),
              "Independent inverse-matrix interpretation changed")
        check(tuple(spatial.transform_matrix.transform((1, 2, 3))) == (8.75 + index, -6.25, 9.875),
              "Independent nonsymmetric matrix interpretation changed")
    tables = list(doc.objects.query("SORTENTSTABLE"))
    if year < 2004:
        check(not tables, "SORTENTSTABLE was authored below its qualified profile")
    else:
        check(len(tables) == 1, "Draw-order table count changed")
        table = tables[0]
        expected = [(circle.dxf.handle, "FFFFFFFFFFFFFFFE"), (second.dxf.handle, "0"),
                    (inserts[0].dxf.handle, second.dxf.handle), (first.dxf.handle, "FFFFFFFFFFFFFFFE")]
        check(list(table) == expected, "Ordered draw associations or opaque sort keys changed")
        extension = model.get_extension_dict().dictionary
        check(extension.dxf.owner == model.block_record_handle and table.dxf.owner == extension.dxf.handle
              and extension["ACAD_SORTENTS"] is table and table.dxf.block_record_handle == model.block_record_handle,
              "Draw-order block/extension owner graph changed")
        check(doc.header["$SORTENTS"] == 17, "Draw-order regeneration flag overwrote existing settings")
        check([(t.code, t.value) for t in object_body(records[table.dxf.handle])] ==
              [(100, "AcDbSortentsTable"), (330, model.block_record_handle)] +
              [tag for handle, key in expected for tag in ((331, handle), (5, key))],
              "SORTENTSTABLE ordered wire pairs changed")
    for name, cpp, count in (("IDBUFFER", "AcDbIdBuffer", 2), ("SPATIAL_FILTER", "AcDbSpatialFilter", 2),
                             ("SORTENTSTABLE", "AcDbSortentsTable", int(year >= 2004))):
        if not count:
            continue
        definition = doc.classes.get(name)
        check(definition.dxf.cpp_class_name == cpp and definition.dxf.app_name == "ObjectDBX Classes"
              and definition.dxf.flags == 0 and definition.dxf.is_an_entity == 0
              and definition.dxf.was_a_proxy == 0 and
              (definition.dxf.instance_count == count if year >= 2004 else not definition.dxf.hasattr("instance_count")),
              name + " class metadata/count changed")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes,
          f"ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    names = {f"typed-containers-AutoCad{year}-{binary}.dxf"
             for year in VERSIONS for binary in ("False", "True")}
    check({path.name for path in args.directory.glob("typed-containers-AutoCad*.dxf")} == names,
          "Expected all twelve API-authored typed-container fixtures")
    for year in VERSIONS:
        for binary in (False, True):
            path = args.directory / f"typed-containers-AutoCad{year}-{binary}.dxf"
            inspect(path, year, binary)
            print("PASS " + path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: twelve authored typed-container fixtures; "
          "ordered pointers/opaque sort keys, 24 filters/48 affine matrices, class counts; zero audit errors/repairs")


if __name__ == "__main__":
    main()
