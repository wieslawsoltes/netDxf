#!/usr/bin/env python3
"""Audit 20 mixed-module exports with independent tag, graph and geometry checks."""
from pathlib import Path
import argparse
import math
import ezdxf
from verify_typed_container_inputs import VERSIONS, wire_records
from verify_lwpolyline_fidelity import (
    polyline_packets, reversed_vertices, cross_section, trace_signature,
)


def check(condition, message):
    if not condition:
        raise ValueError(message)


def values(tags, code):
    return [tag.value for tag in tags if tag.code == code]


def subclass(tags, name):
    start = next(i for i, tag in enumerate(tags) if tag.code == 100 and tag.value == name)
    end = next((i for i in range(start + 1, len(tags))
                if tags[i].code == 100 or tags[i].code >= 1000), len(tags))
    return tags[start + 1:end]


def field(tags, code, expected):
    check(values(tags, code) == [expected], f"Group {code}: expected {expected!r}, got {values(tags, code)!r}")


def xdata(entity, label, original):
    check([(tag.code, tag.value) for tag in entity.get_xdata("MIXED_MODULES")]
          == [(1000, label), (1005, original.dxf.handle)], label + ": XData remapping changed")


def extension(owner):
    result = owner.get_extension_dict().dictionary
    check(result.dxf.owner == owner.dxf.handle, "Extension dictionary ownership changed")
    return result


def child(dictionary, name, kind):
    result = dictionary[name]
    check(result.dxftype() == kind and result.dxf.owner == dictionary.dxf.handle,
          name + ": child type/ownership changed")
    return result


def expected_vertices(year):
    rows = [(0, 0, .5, None, 1), (5, 0, 0, 0, None),
            (5, 4, -.25, 2, 3), (0, 4, 0, None, None)]
    ids = [0, -17, 2147483647, 42] if year >= 2013 else [None] * 4
    return [dict(x=float(x), y=float(y), bulge=float(b), start=s, end=e, id=ident)
            for (x, y, b, s, e), ident in zip(rows, ids)]


def verify_geometry(path, host, year, records):
    entities = list(host)
    check([entity.dxftype() for entity in entities] == ["LWPOLYLINE", "LWPOLYLINE", "LINE"],
          "Mixed block entity inventory/order changed")
    original, reverse, line = entities
    packets = polyline_packets(path)
    check(len(packets) == 2, "Expected two LWPOLYLINE packets")
    vertices = expected_vertices(year)
    for entity, packet, expected, layer in zip(
            (original, reverse), packets, (vertices, reversed_vertices(vertices)),
            ("MIX_ORIGINAL", "MIX_REVERSED")):
        check(entity.dxf.layer == layer and entity.dxf.owner == host.block_record_handle,
              "Polyline layer/owner changed")
        check(packet["declared_count"] == len(packet["vertices"]) == 4, "Vertex count changed")
        # Compare numbers numerically so integer expectations and signed zero
        # normalization do not masquerade as geometry or presence changes.
        check(packet["vertices"] == expected, "Point, bulge, nullable width or identifier association changed")
        check(packet["constant_width"] == 1.5 and packet["flags"] == 1
              and packet["elevation"] == 2 and packet["thickness"] == 0,
              "Polyline width/closure/elevation/thickness changed")
        check(tuple(entity.dxf.extrusion) == (0, 0, 1), "Polyline normal changed")
        common = subclass(records[entity.dxf.handle], "AcDbEntity")
        count_code = 160 if year >= 2013 else 92
        field(common, count_code, 7)
        check(not values(common, 92 if count_code == 160 else 160), "Wrong proxy count profile")
        check(b"".join(values(common, 310)) == bytes([0, 1, 2, 255, 10, 13, 0]),
              "Opaque proxy bytes changed")
        check(values(common, 430) == (["BOOK$Mixed"] if year >= 2004 else []), "Color name presence changed")
        check(values(common, 284) == ([0] if year >= 2007 else []), "Shadow mode presence changed")
        check(trace_signature(entity), "Independent width trace is empty")
    samples = 0
    for index in range(4):
        reversed_index = (2 - index) % 4
        for step in range(17):
            fraction = step / 16
            center, left, right, width = cross_section(packets[0], index, fraction)
            back_center, back_left, back_right, back_width = cross_section(packets[1], reversed_index, 1 - fraction)
            check(width == back_width == 1.5, "Constant width no longer overrides per-edge width")
            check(max(math.dist(center, back_center), math.dist(left, back_right),
                      math.dist(right, back_left)) < 1e-10,
                  "Reversal changed an independently evaluated arc/width cross-section")
            samples += 1
    check(tuple(line.dxf.start) == (20, 30, 40) and tuple(line.dxf.end) == (50, 60, 70)
          and line.dxf.owner == host.block_record_handle, "Following line geometry/owner changed")
    return original, reverse, line, samples


def verify_tables(doc, records, present):
    named = ("MIX_FRAME" in doc.ucs, "MIX_VIEW" in doc.views, "MIX_PORT" in doc.viewports)
    check(named == (present,) * 3, "Named UCS/VIEW/VPORT lifecycle changed")
    if not present:
        return []
    frame = doc.ucs.get("MIX_FRAME")
    view = doc.views.get("MIX_VIEW")
    ports = doc.viewports.get("MIX_PORT")
    check(len(ports) == 1, "Named VPORT multiplicity changed")
    port = ports[0]
    ucs = subclass(records[frame.dxf.handle], "AcDbUCSTableRecord")
    for code, expected in {70: 64, 10: (3, 4, 5), 11: (1, 0, 0), 12: (0, 1, 0),
                           79: 0, 146: 4.5, 71: 1, 13: (0, 0, 1)}.items():
        field(ucs, code, expected)
    for entity, name, origin, xaxis, yaxis, elevation in (
            (view, "AcDbViewTableRecord", (6, 7, 8), (0, 2, 0), (-3, 0, 0), 1.25),
            (port, "AcDbViewportTableRecord", (-2, -3, -4), (1, 0, 0), (0, 1, 0), -2.5)):
        packet = subclass(records[entity.dxf.handle], name)
        for code, expected in {110: origin, 111: xaxis, 112: yaxis, 146: elevation,
                               79: 1, 345: frame.dxf.handle, 346: frame.dxf.handle}.items():
            field(packet, code, expected)
        check(entity.dxf.ucs_handle == entity.dxf.base_ucs_handle == frame.dxf.handle,
              "Named/base UCS pointer identity changed")
    field(subclass(records[view.dxf.handle], "AcDbViewTableRecord"), 72, 1)
    # Per-viewport UCS activation is deliberately false: stored pointers and
    # frame metadata are still preserved. DXF uses65 here;71 is view mode.
    field(subclass(records[port.dxf.handle], "AcDbViewportTableRecord"), 65, 0)
    return [frame, view, port]


def verify_filter(insert, original, records):
    outer = extension(insert)
    check(set(outer.keys()) == {"ACAD_FILTER"}, "INSERT extension inventory changed")
    dictionary = child(outer, "ACAD_FILTER", "DICTIONARY")
    check(set(dictionary.keys()) == {"SPATIAL"}, "Filter dictionary inventory changed")
    spatial = child(dictionary, "SPATIAL", "SPATIAL_FILTER")
    check(spatial.get_reactors() == [dictionary.dxf.handle], "Filter owner reactor changed")
    packet = subclass(records[spatial.dxf.handle], "AcDbSpatialFilter")
    prefix = [(70, 2), (10, (-1, -2)), (10, (3, 4)), (210, (0, 0, 2)),
              (11, (1, 2, 3)), (71, 1), (72, 1), (40, 2.5), (73, 0)]
    inverse = [1, 0, 0, -10, 0, 1, 0, 0, 0, 0, 1, 0]
    boundary = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]
    check([(tag.code, tag.value) for tag in packet] == prefix + [(40, n) for n in inverse + boundary],
          "Spatial boundary, optional clipping plane or ordered affine matrices changed")
    # ezdxf1.4.4's high-level front distance binds a later group40 matrix value;
    # the ordered wire comparison above checks the actual optional distance.
    check(tuple(spatial.inverse_insert_matrix.transform((10, 2, 3))) == (0, 2, 3),
          "Inverse INSERT matrix convention changed")
    check(tuple(spatial.transform_matrix.transform((4, 5, 6))) == (4, 5, 6),
          "Boundary matrix convention changed")
    xdata(spatial, "filter", original)
    return outer, dictionary, spatial


def verify_geo(geo, host, original, records):
    packet = subclass(records[geo.dxf.handle], "AcDbGeoData")
    expected = {90: 2, 330: host.block_record_handle, 70: 1, 10: (1, 2, 3),
                11: (100, 200, 300), 40: 1, 91: 6, 41: 1, 92: 6, 210: (0, 0, 2),
                12: (0, 3), 95: 2, 141: 1.00025, 294: 1, 142: 12.5, 143: 6378137,
                301: "LOCAL_MIXED^Juninterpreted", 302: "", 305: "", 306: "", 307: "",
                93: 3, 96: 1, 97: 0, 98: 1, 99: 2}
    for code, value in expected.items():
        field(packet, code, value)
    check(not values(packet, 303), "Unexpected coordinate definition continuation")
    check(values(packet, 13) == [(0, 0), (5, 0), (5, 4)]
          and values(packet, 14) == [(100, 200), (110, 200), (110, 212)], "Geographic mesh pairs changed")
    check([(tag.code, tag.value) for tag in packet if tag.code in (13, 14, 97, 98, 99)] == [
        (13, (0, 0)), (14, (100, 200)), (13, (5, 0)), (14, (110, 200)),
        (13, (5, 4)), (14, (110, 212)), (97, 0), (98, 1), (99, 2)],
        "Mesh pairing/face ordering changed")
    check(geo.coordinate_system_definition == "LOCAL_MIXED\nuninterpreted", "Coordinate text decoding changed")
    check(list(geo.faces) == [(0, 1, 2)], "Mesh face interpretation changed")
    xdata(geo, "geo", original)


def verify(path, year, binary, scenario, source=None):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Transport differs from filename")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == VERSIONS[year], "Actual DXF version differs from filename")
    records = wire_records(path)
    host_name = "MIX_HOST_COPY" if scenario == "clone" else "MIX_HOST"
    host = doc.blocks[host_name]
    check(set(block.name for block in doc.blocks if not block.name.startswith("*")) == {host_name},
          "Unexpected user block inventory")
    original, reverse, line, samples = verify_geometry(path, host, year, records)
    tables = verify_tables(doc, records, scenario != "lifecycle")
    inserts = list(doc.modelspace())
    check(len(inserts) == 1 and inserts[0].dxftype() == "INSERT", "Modelspace inventory changed")
    insert = inserts[0]
    check(insert.dxf.name == host_name and tuple(insert.dxf.insert) == (10, 0, 0), "INSERT geometry/host changed")
    outer, filters, spatial = verify_filter(insert, original, records)
    host_extension = extension(host.block_record)
    expected_names = {"MIX_REFERENCES"}
    if year >= 2004:
        expected_names.add("ACAD_SORTENTS")
    if year >= 2010:
        expected_names.add("ACAD_GEOGRAPHICDATA")
    check(set(host_extension.keys()) == expected_names, "Shared block extension inventory changed")
    links = child(host_extension, "MIX_REFERENCES", "IDBUFFER")
    geo = child(host_extension, "ACAD_GEOGRAPHICDATA", "GEODATA") if year >= 2010 else None
    order = child(host_extension, "ACAD_SORTENTS", "SORTENTSTABLE") if year >= 2004 else None
    expected_refs = [original, reverse, line] + tables + [insert, None, original] + ([geo] if geo else [])
    expected_handles = [entity.dxf.handle if entity else "0" for entity in expected_refs]
    check(list(links.handles) == expected_handles, "IDBUFFER ordered duplicates/null/internal/external remapping changed")
    field_codes = subclass(records[links.dxf.handle], "AcDbIdBuffer")
    check([(tag.code, tag.value) for tag in field_codes] == [(330, h) for h in expected_handles],
          "IDBUFFER wire soft-reference grammar changed")
    for entity in expected_refs:
        if entity is not None:
            check(doc.entitydb[entity.dxf.handle] is entity, "Reference does not resolve to the destination object")
    xdata(links, "links", original)
    if geo:
        verify_geo(geo, host, original, records)
    if order:
        key = source["original"] if scenario == "clone" else original.dxf.handle
        expected_order = [(reverse.dxf.handle, "0"), (line.dxf.handle, key),
                          (original.dxf.handle, "FFFFFFFFFFFFFFFF")]
        check(list(order) == expected_order and order.dxf.block_record_handle == host.block_record_handle,
              "SORTENTSTABLE references/order/opaque keys changed")
        packet = subclass(records[order.dxf.handle], "AcDbSortentsTable")
        check([(tag.code, tag.value) for tag in packet] == [(330, host.block_record_handle)] + [
            tag for handle, sort_key in expected_order for tag in ((331, handle), (5, sort_key))],
            "Sort pair grammar changed")
        # Cloning an extension graph does not copy document HEADER settings.
        if scenario != "clone":
            check(doc.header["$SORTENTS"] & 16, "Full source omitted explicit draw-order header bit")
    owned = {"host": host.block_record, "original": original, "reverse": reverse, "line": line,
             "insert": insert, "extension": host_extension, "links": links, "insert_extension": outer,
             "filters": filters, "spatial": spatial}
    if tables:
        owned.update(zip(("frame", "view", "port"), tables))
    if geo:
        owned["geo"] = geo
    if order:
        owned["order"] = order
    identities = {name: entity.dxf.handle for name, entity in owned.items()}
    check(len(set(identities.values())) == len(identities), "Distinct graph members share a handle")
    if scenario == "clone":
        check(all(source[name] != handle for name, handle in identities.items()),
              "Clone output did not allocate distinct source/destination identities")
        check(source["original"] != identities["original"], "Opaque sort-key remapping fixture is ineffective")
        check(sum(layer.dxf.name.startswith("MIX_PAD_") for layer in doc.layers) == 128,
              "Clone handle-distinguishing padding layer inventory changed")
    if scenario == "lifecycle":
        check(all(source[name] == handle for name, handle in identities.items()),
              "Deleting table references changed surviving graph identities")
        check(all(source[name] not in doc.entitydb for name in ("frame", "view", "port")),
              "Removed table objects remain in the destination database")
    for kind, expected_count in (("IDBUFFER", 1), ("SPATIAL_FILTER", 1),
                                  ("SORTENTSTABLE", int(year >= 2004)), ("GEODATA", int(year >= 2010))):
        check(len(list(doc.objects.query(kind))) == expected_count, kind + " object inventory changed")
        if expected_count:
            cls = next(item for item in doc.classes if item.dxf.name == kind)
            check(cls.dxf.is_an_entity == 0, kind + " incorrectly declared an entity")
            if year >= 2004:
                check(cls.dxf.instance_count == expected_count, kind + " CLASS instance count changed")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes,
          f"ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")
    return identities, samples


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    years = {"roundtrip": list(VERSIONS), "clone": [2013, 2018], "lifecycle": [2000, 2018]}
    expected = {f"mixed-{scenario}-AutoCad{year}-{binary}.dxf"
                for scenario, profiles in years.items() for year in profiles for binary in (False, True)}
    check({path.name for path in args.directory.glob("mixed-*.dxf")} == expected,
          "Expected exactly 20 mixed-module outputs across all profiles/scenarios/transports")
    sources, samples = {}, 0
    for scenario, profiles in years.items():
        for year in profiles:
            for binary in (False, True):
                path = args.directory / f"mixed-{scenario}-AutoCad{year}-{binary}.dxf"
                try:
                    identities, count = verify(path, year, binary, scenario, sources.get((year, binary)))
                except Exception as error:
                    raise ValueError(path.name + ": " + str(error)) from error
                if scenario == "roundtrip":
                    sources[year, binary] = identities
                samples += count
                print("PASS " + path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: 20 mixed exports; {samples} independent reversed arc/width "
          "cross-sections; scoped common bytes, extension ownership, table lifecycle, clone references, "
          "literal sort keys, geographic meshes and zero audit errors/repairs")


if __name__ == "__main__":
    main()
