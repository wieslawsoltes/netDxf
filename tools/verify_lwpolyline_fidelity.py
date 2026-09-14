#!/usr/bin/env python3
"""Verify all 120 LWPOLYLINE fidelity outputs and independent source provenance."""
from pathlib import Path
import argparse
import hashlib
import io
import json
import math
import struct
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from ezdxf.render import TraceBuilder
from ezdxf.math import Vec2, bulge_to_arc

VERSIONS = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024",
            2013: "AC1027", 2018: "AC1032"}


def check(value, message):
    if not value:
        raise ValueError(message)


def exact(value):
    if isinstance(value, float):
        # Numeric width fidelity permits normalization of IEEE signed zero.
        return struct.pack("<d", 0.0 if value == 0.0 else value)
    if isinstance(value, (tuple, list)):
        return tuple(exact(item) for item in value)
    if isinstance(value, dict):
        return {key: exact(item) for key, item in value.items()}
    return value


def polyline_packets(path):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else (
        ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None)))
    packets = []
    entity = None
    active = False
    for tag in tag_compiler(loader):
        code, value = tag
        if code == 0:
            if value == "LWPOLYLINE":
                entity = {"constant_width": None, "vertices": [], "flags": 0,
                          "elevation": 0.0, "thickness": 0.0, "declared_count": None}
                packets.append(entity)
            else:
                entity = None
            active = False
        if entity is None:
            continue
        if code == 100:
            active = value == "AcDbPolyline"
        elif code >= 1000:
            active = False
        elif active:
            if code == 10:
                entity["vertices"].append({"x": value[0], "y": value[1], "start": None,
                                           "end": None, "bulge": 0.0, "id": None})
            elif code in (40, 41, 42, 91):
                check(entity["vertices"], "Vertex field outside a vertex packet")
                field = {40: "start", 41: "end", 42: "bulge", 91: "id"}[code]
                entity["vertices"][-1][field] = value
            elif code in (43, 70, 38, 39, 90):
                field = {43: "constant_width", 70: "flags", 38: "elevation",
                         39: "thickness", 90: "declared_count"}[code]
                entity[field] = value
    return packets


def trace_signature(polyline):
    # This exercises ezdxf's documented AutoCAD width policy independently of
    # the tag reader: nonzero43 governs both taper endpoints even with40/41.
    return [tuple(tuple(entity.dxf.get("vtx" + str(index))) for index in range(4))
            for entity in TraceBuilder.from_polyline(polyline, segments=128).virtual_entities()]


def compare(source, target, fixture, binary):
    check(target.read_bytes().startswith(b"AutoCAD Binary DXF") == binary,
          "Actual transport differs from fixture name")
    packets = polyline_packets(target)
    check(len(packets) == 1, "Expected one LWPOLYLINE")
    packet = packets[0]
    check(packet["declared_count"] == len(packet["vertices"]) == 4,
          "Vertex count changed")
    check(exact(packet["constant_width"]) == exact(fixture["constant_width"]),
          "Absent/zero/positive constant43 or exact value changed")
    check(exact(packet["vertices"]) == exact(fixture["vertices"]),
          "Coordinates, width40/41 presence, bulges or identifiers changed")
    check(packet["flags"] & 1 and packet["elevation"] == fixture["elevation"]
          and packet["thickness"] == fixture["thickness"], "Polyline common geometry changed")
    before, after = ezdxf.readfile(source), ezdxf.readfile(target)
    check(after.dxfversion == fixture["version"], "Actual DXF version changed")
    old = before.modelspace().query("LWPOLYLINE")[0]
    new = after.modelspace().query("LWPOLYLINE")[0]
    check(old.get_xdata("QA_LW_FIDELITY") == new.get_xdata("QA_LW_FIDELITY"), "XData changed")
    old_trace, new_trace = trace_signature(old), trace_signature(new)
    check(len(old_trace) == len(new_trace) > 0, "Trace facet count changed")
    for original_face, output_face in zip(old_trace, new_trace):
        for original_point, output_point in zip(original_face, output_face):
            check(math.dist(original_point, output_point) <= 1e-10, "Independent width trace changed")
    lines = list(after.modelspace().query("LINE"))
    check(len(lines) == 1 and tuple(lines[0].dxf.start) == (20, 30, 40)
          and tuple(lines[0].dxf.end) == (50, 60, 70), "Following LINE changed")
    audit = after.audit()
    check(not audit.errors and not audit.fixes,
          f"ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")


def authored_vertices(year):
    rows = [(-3.0, 2.0, 0.25, None, 1.25), (1.0, -1.0, 0.0, 0.0, None),
            (6.0, 4.0, -0.5, 2.5, 3.75), (9.0, -2.0, 0.75, None, None)]
    ids = [0, -17, 2147483647, 42] if year >= 2013 else [None] * 4
    return [{"x": x, "y": y, "bulge": bulge, "start": start, "end": end, "id": ident}
            for (x, y, bulge, start, end), ident in zip(rows, ids)]


def reversed_vertices(vertices):
    # Derive outgoing attributes by matching geometric edges; point IDs travel
    # with the point while widths/bulges belong to the edge being reversed.
    result = []
    for point in reversed(vertices):
        source_index = vertices.index(point)
        edge = vertices[(source_index - 1) % len(vertices)]
        result.append(dict(point, start=edge["end"], end=edge["start"], bulge=-edge["bulge"]))
    return result


def cross_section(packet, index, fraction):
    vertex = packet["vertices"][index]
    next_vertex = packet["vertices"][(index + 1) % len(packet["vertices"])]
    start, end = Vec2(vertex["x"], vertex["y"]), Vec2(next_vertex["x"], next_vertex["y"])
    bulge = vertex["bulge"]
    if bulge:
        center, _, _, radius = bulge_to_arc(start, end, bulge)
        sweep = 4 * math.atan(bulge)
        angle = (start - center).angle + sweep * fraction
        position = center + Vec2.from_angle(angle, radius)
        tangent = Vec2(-math.sin(angle), math.cos(angle)) * (1 if sweep > 0 else -1)
    else:
        position = start.lerp(end, fraction)
        tangent = (end - start).normalize()
    constant = packet["constant_width"]
    start_width = constant if constant else (vertex["start"] or 0.0)
    end_width = constant if constant else (vertex["end"] or 0.0)
    width = start_width * (1 - fraction) + end_width * fraction
    offset = Vec2(-tangent.y, tangent.x) * (width / 2)
    return position, position + offset, position - offset, width


def verify_authored(directory):
    expected = {f"lw-fidelity-AutoCad{year}-{binary}-{mode}-{stage}.dxf"
                for year in VERSIONS for binary in ("False", "True")
                for mode in ("absent", "zero", "positive")
                for stage in ("original", "reversed", "restored")}
    check({path.name for path in directory.glob("lw-fidelity-AutoCad*.dxf")} == expected,
          "Expected all 108 authored LWPOLYLINE fidelity outputs")
    samples = 0
    for year, version in VERSIONS.items():
        for binary in (False, True):
            for mode, constant in (("absent", None), ("zero", 0.0), ("positive", 1.75)):
                packets = {}
                for stage in ("original", "reversed", "restored"):
                    path = directory / f"lw-fidelity-AutoCad{year}-{binary}-{mode}-{stage}.dxf"
                    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary,
                          path.name + ": actual transport differs")
                    doc = ezdxf.readfile(path)
                    check(doc.dxfversion == version, path.name + ": actual DXF version differs")
                    polylines = list(doc.modelspace().query("LWPOLYLINE"))
                    raw = polyline_packets(path)
                    check(len(polylines) == len(raw) == 1, path.name + ": expected one polyline")
                    packet = raw[0]
                    packets[stage] = packet
                    vertices = authored_vertices(year)
                    if stage == "reversed":
                        vertices = reversed_vertices(vertices)
                    check(packet["declared_count"] == len(packet["vertices"]) == 4,
                          path.name + ": vertex count changed")
                    check(exact(packet["vertices"]) == exact(vertices),
                          path.name + ": width presence, raw taper, bulge, point or ID changed")
                    check(exact(packet["constant_width"]) == exact(constant),
                          path.name + ": constant width value/presence changed")
                    check(packet["flags"] & 1 and packet["elevation"] == 2.5 and packet["thickness"] == 0,
                          path.name + ": closed/elevation/thickness changed")
                    check(tuple(polylines[0].dxf.extrusion) == (0, 0, 1), "Extrusion changed")
                    check(trace_signature(polylines[0]), "Independent width trace is empty")
                    audit = doc.audit()
                    check(not audit.errors and not audit.fixes,
                          f"{path.name}: {len(audit.errors)} audit errors/{len(audit.fixes)} repairs")
                    print("PASS " + path.name)
                original, reversed_packet = packets["original"], packets["reversed"]
                check(exact(original) == exact(packets["restored"]), "Double reversal changed raw packet")
                for index, vertex in enumerate(reversed_packet["vertices"]):
                    next_vertex = reversed_packet["vertices"][(index + 1) % 4]
                    match = [i for i, old in enumerate(original["vertices"])
                             if (old["x"], old["y"]) == (next_vertex["x"], next_vertex["y"])]
                    check(len(match) == 1, "Reversed outgoing edge not found")
                    for step in range(17):
                        before = cross_section(original, match[0], 1 - step / 16)
                        after = cross_section(reversed_packet, index, step / 16)
                        check(math.dist(before[0], after[0]) < 1e-10, "Reversal changed arc locus")
                        check(math.dist(before[1], after[2]) < 1e-10 and math.dist(before[2], after[1]) < 1e-10,
                              "Reversal changed effective taper cross-section")
                        check(abs(before[3] - after[3]) < 1e-12, "Reversal changed effective width")
                        samples += 1
    return samples


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--sources", type=Path)
    args = parser.parse_args()
    sources = args.sources or (Path(__file__).resolve().parents[1] / "tests/fixtures/lwpolyline-fidelity")
    if not sources.is_dir():
        sources = Path(__file__).resolve().parent
    manifest = json.loads((sources / "manifest.json").read_text())
    check(len(manifest["fixtures"]) == 6, "Expected six independent source fixtures")
    expected = {f"independent-lw-fidelity-R{year}-{mode}-{kind}.dxf"
                for year in (2000, 2018) for mode in ("absent", "zero", "positive")
                for kind in ("ascii", "binary")}
    check({path.name for path in args.directory.glob("independent-lw-fidelity-R*.dxf")} == expected,
          "Expected all 12 external LWPOLYLINE roundtrip fixtures")
    for fixture in manifest["fixtures"]:
        source = sources / fixture["file"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == fixture["sha256"],
              "Independent source digest changed: " + source.name)
        for binary in (False, True):
            kind = "binary" if binary else "ascii"
            output = args.directory / (source.stem + "-" + kind + ".dxf")
            compare(source, output, fixture, binary)
            print("PASS " + output.name)
    samples = verify_authored(args.directory)
    print(f"PASS ezdxf {ezdxf.__version__}: all 120 LWPOLYLINE fidelity outputs; "
          f"12 external round trips, 108 authored outputs, {samples} reversed cross-sections; "
          "exact width presence/IDs and independent width traces; zero audit errors/repairs")


if __name__ == "__main__":
    main()
