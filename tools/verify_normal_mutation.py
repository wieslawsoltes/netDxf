#!/usr/bin/env python3
"""Independent normal edits: stored bits, proxy packets, ownership and geometry."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tags import Tags
from ezdxf.lldxf.types import DXFTag
from ezdxf.proxygraphic import load_proxy_graphic
from verify_raw_line_geometry import key, require, reject
from verify_ellipse_axis_proxies import load_visibility_tags, VERSIONS
from verify_dimlfac_fidelity import records

PROXY = bytes((1, 7, 19, 33, 255))
KINDS = ("LINE", "POINT", "CIRCLE", "ARC")


def one(record, code):
    values = [value for actual, value in record if actual == code]
    require(len(values) == 1, f"Missing/duplicate field {code}")
    return values[0]


def normal(mode):
    return (1., 0., 0.) if mode == 1 else (-0., 0., 1.) if mode == 2 else (0., 0., 1.)


def expected_geometry(kind, mode):
    location = (2., 3., 1.) if mode == 1 and kind in ("CIRCLE", "ARC", "ATTDEF", "ATTRIB") else (1., 2., 3.)
    values = dict(zip((10, 20, 30), location))
    if kind == "LINE":
        values.update(zip((11, 21, 31), (4., 5., 6.)))
    if kind in KINDS:
        values.update({39: 2., 62: 3})
    if kind in ("CIRCLE", "ARC"):
        values[40] = 2.
    if kind == "ARC":
        values.update({50: 30., 51: 120.})
    if kind == "POINT":
        values[50] = 330.
    if kind in ("ATTDEF", "ATTRIB"):
        values.update({1: "definition" if kind == "ATTDEF" else "attribute", 2: f"NM{mode}"})
    return values


def packet(record, kind, mode, version, owner):
    require(record[0] == (0, kind), "Wrong entity type")
    values = expected_geometry(kind, mode)
    values.update(zip((210, 220, 230), normal(mode)))
    values[330] = owner
    for code, wanted in values.items():
        require(key((code, one(record, code))) == key((code, wanted)), f"Changed stored field {code}")
    length_code = 160 if version in ("AutoCad2013", "AutoCad2018") else 92
    proxies = [tag for tag in record if tag[0] in (92, 160, 310)]
    require(proxies == ([] if mode in (1, 2) else [(length_code, len(PROXY)), (310, PROXY)]),
            "Proxy field presence/count/bytes")
    return values


def mutate(record, kind, mode, version, owner):
    fields = packet(record, kind, mode, version, owner)
    controls = 0
    for code in fields:
        at = next(i for i, tag in enumerate(record) if tag[0] == code)
        for operation in ("change", "remove", "duplicate", "wrong-code"):
            bad = list(record)
            value = bad[at][1]
            if operation == "change":
                bad[at] = (code, value + "_BAD" if isinstance(value, str) else value + 1)
            elif operation == "remove":
                del bad[at]
            elif operation == "duplicate":
                bad.insert(at, bad[at])
            else:
                bad[at] = (999, value)
            controls += reject(lambda: packet(bad, kind, mode, version, owner))
    for code in (210, 220, 230):
        at = next(i for i, tag in enumerate(record) if tag[0] == code)
        if record[at][1] == 0.:
            bad = list(record); bad[at] = (code, -record[at][1])
            controls += reject(lambda: packet(bad, kind, mode, version, owner))
    bad = [t for t in record if t[0] not in (92, 160, 310)]
    if mode in (1, 2):
        bad += [(92, len(PROXY)), (310, PROXY)]
    controls += reject(lambda: packet(bad, kind, mode, version, owner))
    if mode in (0, 3):
        at = next(i for i, tag in enumerate(record) if tag[0] == 310)
        bad = list(record); bad[at] = (310, b"WRONG")
        controls += reject(lambda: packet(bad, kind, mode, version, owner))
        bad = list(record); bad.insert(at, bad[at])
        controls += reject(lambda: packet(bad, kind, mode, version, owner))
    length_code = 160 if version in ("AutoCad2013", "AutoCad2018") else 92
    extracted = load_proxy_graphic(Tags(DXFTag(c, v) for c, v in record), length_code=length_code)
    require(extracted == (None if mode in (1, 2) else PROXY), "Independent proxy extraction")
    return controls


def same_vector(actual, wanted, message):
    require([key((1040, float(v))) for v in actual] ==
            [key((1040, float(v))) for v in wanted], message)


def inspect(path, version, binary, placement):
    require(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong transport")
    tags = load_visibility_tags(path)
    require(tags[tags.index((9, "$ACADVER")) + 1] == (1, VERSIONS[version]), "Physical version")
    entries = [tags[a:b] for a, b in records(tags)]
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == VERSIONS[version], "Independent version")
    space = doc.modelspace() if placement == 0 else doc.layouts.get("NORMAL_PAPER") if placement == 1 else doc.blocks["NORMAL_HOLDER"]
    hosts = [r for r in entries if any(c == 8 and str(v).startswith("NORMAL_HOST_") for c, v in r)]
    require(len(hosts) == 16 and {one(r, 8) for r in hosts} == {f"NORMAL_HOST_{i:02d}" for i in range(16)},
            "Physical primitive inventory")
    controls = 0
    for record in hosts:
        row = int(one(record, 8).split("_")[-1])
        controls += mutate(record, KINDS[row // 4], row % 4, version, space.block_record_handle)
    loaded = [e for e in space if e.dxf.layer.startswith("NORMAL_HOST_")]
    require(len(loaded) == 16, "Independent primitive count/placement")
    for host in loaded:
        row = int(host.dxf.layer.split("_")[-1]); kind, mode = KINDS[row // 4], row % 4
        require(host.dxftype() == kind and host.dxf.owner == space.block_record_handle, "Independent primitive type/owner")
        same_vector(host.dxf.extrusion, normal(mode), "Independent normal bits")
        if kind == "LINE":
            same_vector(host.dxf.start, (1., 2., 3.), "LINE start")
            same_vector(host.dxf.end, (4., 5., 6.), "LINE end")
        elif kind == "POINT":
            same_vector(host.dxf.location, (1., 2., 3.), "POINT location")
            require(host.dxf.angle == 330., "POINT marker rotation")
        else:
            require(tuple(host.ocs().to_wcs(host.dxf.center)) == (1., 2., 3.) and host.dxf.radius == 2.,
                    "Independent circular WCS center/radius")
            if kind == "ARC":
                require(host.dxf.start_angle == 30. and host.dxf.end_angle == 120., "ARC endpoints")
        require(host.dxf.thickness == 2. and host.dxf.color == 3, "Primitive thickness/appearance")
        require([(t.code, t.value) for t in host.get_xdata("NORMAL_KEEP")] ==
                [(1000, "unmodified"), (1004, bytes((17, 33, 201)))], "Unrelated XData")
    require(sum(r[0] == (0, "ATTDEF") for r in entries) == 4 and
            sum(r[0] == (0, "ATTRIB") for r in entries) == 4, "Physical attribute/definition count")
    for mode in range(4):
        block = doc.blocks[f"NORMAL_ATTRIBUTE_{mode}"]
        definitions = list(block.query("ATTDEF"))
        require(len(definitions) == 1, "Independent attribute definition count")
        insert, = [i for i in space.query("INSERT") if i.dxf.layer == f"NORMAL_INSERT_{mode}"]
        require(insert.dxf.name == f"NORMAL_ATTRIBUTE_{mode}" and insert.dxf.owner == space.block_record_handle,
                "Independent insert reference/owner")
        require(len(insert.attribs) == 1, "Independent attribute count")
        for kind, entity, owner in (("ATTDEF", definitions[0], block.block_record_handle),
                                    ("ATTRIB", insert.attribs[0], insert.dxf.handle)):
            record, = [r for r in entries if r[0] == (0, kind) and (2, f"NM{mode}") in r]
            controls += mutate(record, kind, mode, version, owner)
            require(entity.dxf.owner == owner and entity.dxf.tag == f"NM{mode}", "Attribute owner/tag")
            same_vector(entity.dxf.extrusion, normal(mode), "Independent attribute normal bits")
            require(tuple(entity.ocs().to_wcs(entity.dxf.insert)) == (1., 2., 3.), "Attribute WCS location")
            require(entity.dxf.text == ("definition" if kind == "ATTDEF" else "attribute"), "Attribute content")
    line, = doc.modelspace().query('LINE[layer=="NORMAL_FOLLOWING"]')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), "Following geometry")
    if placement in (2, 3):
        outer = list(doc.modelspace().query("INSERT"))
        require(len(outer) == (1 if placement == 2 else 0), "Referenced/unreferenced holder policy")
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, "Independent graph errors/repairs")
    return controls


def main(directory):
    specs = list(itertools.product(VERSIONS, (False, True), range(4), ("source", "False", "True")))
    def name(spec):
        version, binary, placement, output = spec
        return f"normal-mutation-{version}-{binary}-{placement}-{output}.dxf"
    expected = {name(s) for s in specs}
    def inventory(actual):
        require(actual == expected, f"Normal inventory missing={len(expected-actual)}, extra={len(actual-expected)}")
    inventory({p.name for p in directory.glob("normal-mutation-*.dxf")})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {"normal-mutation-extra.dxf"}))
    controls = 0
    for spec in specs:
        version, binary, placement, output = spec
        controls += inspect(directory / name(spec), version, binary if output == "source" else output == "True", placement)
    print(f"PASS: {len(specs)} drawings / {24*len(specs)} independently read normal-bearing records; "
          f"{controls} packet mutations and two inventory controls rejected; zero graph errors/repairs. "
          "Synthetic proxy bytes test invalidation, not native graphic regeneration.")


if __name__ == "__main__":
    require(len(sys.argv) == 2, "Usage: verify_normal_mutation.py ARTIFACTS")
    main(Path(sys.argv[1]))
