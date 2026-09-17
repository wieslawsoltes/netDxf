#!/usr/bin/env python3
"""Independently verify authored entity clones, including nested block exports.

Checks 540 source/clone pairs over six profiles and both transports. A complete
ordered ENTITIES/BLOCKS comparison is supplemented by source-derived property
expectations and graph audits. This is not installed-SHX or native visual QA.
"""
import argparse
import io
import math
from pathlib import Path

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from ezdxf.math import OCS, Vec3
from verify_legacy_utilities import PROFILES
from verify_mleader_inputs import check

KINDS = ("solid", "trace", "shape", "polyline", "leader")
NORMALS = (Vec3(0, 0, 1), Vec3(0, 0, -1), Vec3(1, 0, 0))
ELEVATIONS = (13.75, -7.25, 128.5)
CORNERS = ((1, 2), (5, 3), (2, 8), (7, 9))
CONTROL_POINTS = [(1., 2., 3.), (5., 3., 7.), (2., 8., -2.), (7., 9., 11.)]


def inventory(directory):
    expected = {
        f"clone-qualification-{kind}-{variant}-{depth}-{version}-{binary}-{side}.dxf"
        for kind in KINDS for variant in range(3) for depth in range(3)
        for version in PROFILES for binary in (False, True)
        for side in ("source", "clone")
    }
    actual = {path.name for path in directory.glob("clone-qualification-*.dxf")}
    check(actual == expected, "Clone fixture inventory differs: missing or extra file")
    return expected


def packets(path, binary):
    data = path.read_bytes()
    check(data.startswith(b"AutoCAD Binary DXF") == binary, "Clone transport differs")
    tags = list(binary_tags_loader(data) if binary else
                ascii_tags_loader(io.StringIO(data.decode("ascii"))))
    result, section = [], None
    for index, tag in enumerate(tags):
        if tag.code == 0 and tag.value == "SECTION":
            check(tags[index + 1].code == 2, "Missing section name")
            section = tags[index + 1].value
        elif tag.code == 0 and tag.value == "ENDSEC":
            section = None
        elif section in ("ENTITIES", "BLOCKS"):
            # Handles are independently checked by ezdxf's graph audit. Clone
            # documents have distinct identity; only these two slots normalize.
            if tag.code not in (5, 330):
                if 310 <= tag.code <= 319 or tag.code == 1004:
                    value = bytes.fromhex(tag.value) if isinstance(tag.value, str) else tag.value
                else:
                    value = cast_tag_value(tag.code, tag.value)
                result.append((section, tag.code, value))
    check(bool(result), "Empty clone record inventory")
    return result


def verify_proxy(records, variant):
    # Several ezdxf simple entity loaders bypass proxy projection; inspect the
    # actual wire packet rather than mistaking the absent projection for loss.
    packet = [(code, value) for _, code, value in records if code in (92, 160, 310)]
    check(len(packet) == 2 and packet[0][0] in (92, 160) and
          packet[0][1] == 3 and packet[1] == (310, bytes((19, 83, variant))),
          "Clone raw proxy packet differs")


def same_packets(before, after):
    check(before == after, "Ordered source/clone ENTITIES/BLOCKS records differ")


def near(actual, expected, label):
    if isinstance(expected, (tuple, list, Vec3)):
        check(len(actual) == len(expected), label + " cardinality")
        check(all(math.isclose(a, b, rel_tol=1e-13, abs_tol=1e-13)
                  for a, b in zip(actual, expected)), label)
    else:
        check(math.isclose(actual, expected, rel_tol=1e-13, abs_tol=1e-13), label)


def subject(document, depth):
    roots = list(document.modelspace())
    check(len(roots) == 1, "Clone root inventory differs")
    entity = roots[0]
    for level in reversed(range(depth)):
        check(entity.dxftype() == "INSERT", "Missing nested clone INSERT")
        check(entity.dxf.name == "CLONE_AUDIT_BLOCK_" + str(level), "Nested block identity differs")
        children = list(document.blocks[entity.dxf.name])
        check(len(children) == 1, "Nested clone child inventory differs")
        near(entity.dxf.insert, (0, 0, 0), "Nested insertion changed")
        entity = children[0]
    return entity


def verify(entity, kind, variant):
    expected_type = "POLYLINE" if kind == "polyline" else kind.upper()
    check(entity.dxftype() == expected_type, "Clone subject kind differs")
    check(entity.dxf.layer == "CLONE_AUDIT_LAYER", "Clone layer differs")
    check(entity.dxf.color == 3 and entity.dxf.invisible == 1, "Clone appearance differs")
    near(entity.dxf.ltscale, 1.75, "Clone linetype scale differs")
    check([(tag.code, tag.value) for tag in entity.get_xdata("CLONE_AUDIT")] ==
          [(1000, "clone-state-" + str(variant))], "Clone XData differs")
    normal = NORMALS[variant]
    normal_slot = "normal_vector" if kind == "leader" else "extrusion"
    near(entity.dxf.get(normal_slot), normal, "Clone normal differs")
    fields = ["layer", "color", "invisible", "ltscale", normal_slot]
    if kind in ("solid", "trace"):
        for index, (x, y) in enumerate(CORNERS):
            near(entity.dxf.get("vtx" + str(index)), (x, y, ELEVATIONS[variant]), "Clone OCS corner/elevation differs")
        near(entity.dxf.thickness, -2.5, "Clone thickness differs")
        fields += ["vtx" + str(i) for i in range(4)] + ["thickness"]
    elif kind == "shape":
        near(entity.dxf.xscale, (.5, -2.25, 1.375)[variant], "SHAPE clone width factor differs")
        near(entity.dxf.size, 2.25, "SHAPE clone size differs")
        near(entity.dxf.rotation, 37, "SHAPE clone rotation differs")
        near(entity.dxf.oblique, 15, "SHAPE clone oblique angle differs")
        near(entity.dxf.thickness, -2.5, "SHAPE clone thickness differs")
        check(entity.dxf.name == "CLONE_AUDIT_SHAPE", "SHAPE clone name differs")
        fields += ["xscale", "size", "rotation", "oblique", "thickness", "name"]
        # Position is compared verbatim between records, not certified against
        # native SHAPE placement: this review corrects Clone, not the writer.
    elif kind == "polyline":
        smooth = (0, 5, 6)[variant]
        check(entity.dxf.smooth_type == smooth, "POLYLINE clone smoothing differs")
        check(entity.dxf.flags == 128 + 8 + (1 if variant == 1 else 0) + (4 if variant else 0),
              "POLYLINE clone flags differ")
        points = [tuple(vertex.dxf.location) for vertex in entity.vertices
                  if not smooth or vertex.dxf.flags & 16]
        check(points == CONTROL_POINTS, "POLYLINE clone control vertices differ")
        fields += ["smooth_type", "flags"]
    else:
        direction = (Vec3(3/5, 4/5, 0), Vec3(0, 1, 0), Vec3(-5/13, -12/13, 0))[variant]
        ocs = OCS(normal)
        near(entity.dxf.horizontal_direction, ocs.to_wcs(direction), "LEADER clone horizontal direction differs")
        check(entity.dxf.block_color == 120 + variant, "LEADER clone line color differs")
        check(entity.dxf.has_arrowhead == 0 and entity.dxf.path_type == 0 and entity.dxf.has_hookline == 0,
              "LEADER clone flags differ")
        near(entity.dxf.leader_offset_annotation_placement,
             ocs.to_wcs(Vec3(2.25, -1.5, ELEVATIONS[variant])), "LEADER clone offset differs")
        check(len(entity.vertices) == 3, "LEADER clone vertex inventory differs")
        for actual, (x, y) in zip(entity.vertices, ((1, 2), (5, 3), (7, 9))):
            near(actual, ocs.to_wcs(Vec3(x, y, ELEVATIONS[variant])), "LEADER clone vertices differ")
        fields += ["horizontal_direction", "block_color", "has_arrowhead", "path_type", "has_hookline",
                   "leader_offset_annotation_placement"]
    return fields


def rejects(action, label):
    try:
        action()
    except ValueError:
        return 1
    raise AssertionError("Corruption escaped clone verifier: " + label)


def challenge_properties(entity, kind, variant):
    count = 0
    # Bypass ezdxf attribute normalization deliberately, so the actual checker,
    # rather than ezdxf's input validation, must reject each changed output.
    for field in verify(entity, kind, variant):
        value = entity.dxf.get(field)
        corrupted = value + Vec3(.25, .5, .75) if isinstance(value, Vec3) else (
            value + "_corrupt" if isinstance(value, str) else value + 1)
        entity.dxf.__dict__[field] = corrupted
        try:
            count += rejects(lambda: verify(entity, kind, variant), field)
        finally:
            entity.dxf.__dict__[field] = value
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    directory = parser.parse_args().directory
    inventory(directory)
    pair_controls = property_controls = pairs = 0
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for kind in KINDS:
                for variant in range(3):
                    for depth in range(3):
                        stem = f"clone-qualification-{kind}-{variant}-{depth}-{version}-{binary}"
                        source = directory / (stem + "-source.dxf")
                        clone = directory / (stem + "-clone.dxf")
                        before, after = packets(source, binary), packets(clone, binary)
                        same_packets(before, after)
                        verify_proxy(before, variant); verify_proxy(after, variant)
                        # Every retained tag, including unrelated block headers,
                        # is challenged through the positive pair comparator.
                        for index, tag in enumerate(after):
                            changed = list(after)
                            value = tag[2]
                            value = (value + b"\x00" if isinstance(value, bytes) else
                                     value + "_corrupt" if isinstance(value, str) else value + 1)
                            changed[index] = (tag[0], tag[1], value)
                            pair_controls += rejects(lambda: same_packets(before, changed), "record tag")
                        pair_controls += rejects(lambda: same_packets(before, after[:-1]), "missing tag")
                        pair_controls += rejects(lambda: same_packets(before, after + after[-1:]), "extra tag")
                        for path in (source, clone):
                            document = ezdxf.readfile(path)
                            check(document.dxfversion == profile, "Clone version differs")
                            entity = subject(document, depth)
                            verify(entity, kind, variant)
                            property_controls += challenge_properties(entity, kind, variant)
                            audit = document.audit()
                            check(not audit.errors and not audit.fixes, "Clone output requires graph repair")
                        pairs += 1
    print(f"PASS ezdxf {ezdxf.__version__}: {pairs} clone pairs / {2*pairs} drawings; zero audit errors/repairs")
    print(f"Rejected {pair_controls} record corruptions and {property_controls} property corruptions")
    print("SHAPE width is qualified without installed SHX; native glyph rendering/placement is not certified")


if __name__ == "__main__":
    main()
