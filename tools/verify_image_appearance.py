#!/usr/bin/env python3
"""Verify IMAGE appearance edits, pixel clipping and common proxy preservation."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import require, reject
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_dimlfac_fidelity import PROFILES, records
from verify_image_affine import common, one

PAYLOAD = bytes((i * 29) & 255 for i in range(300))
LAYERS = {f"IMAGE_AP_{e:02d}_{s}" for e, s in itertools.product(range(8), range(3))}

def row(record):
    layer = one(record, 8)
    require(layer in LAYERS, "Unknown appearance row")
    parts = layer.split("_")
    return int(parts[2]), int(parts[3])

def expected_clip(edit):
    # Public pixel coordinates are written at the existing half-pixel offset.
    return ((0., 0.), (5.5, .5), (1.5, 4.5), (0., 0.)) if edit == 6 else (
        ((-.5, -.5), (7.5, 5.5)) if edit == 7 else ((.5, .5), (6.5, 4.5)))

def expected_fields(edit):
    return {10: 3., 20: -5., 30: 7., 11: .5, 21: 0., 31: 0., 12: 0., 22: .5, 32: 0.,
            13: 8., 23: 6., 70: 0 if edit == 4 else 7, 280: 0 if edit == 5 else 1,
            281: 0 if edit == 1 else 61, 282: 100 if edit == 2 else 37,
            283: 90 if edit == 3 else 9, 71: 2 if edit == 6 else 1,
            91: 4 if edit == 6 else 2}

def check_record(record, year):
    require(record[0] == (0, "IMAGE"), "Wrong entity")
    edit, state = row(record)
    for code, value in expected_fields(edit).items():
        require(one(record, code) == value, f"Appearance or geometry field {code}")
    wanted_clip = [(code, v) for point in expected_clip(edit) for code, v in zip((14, 24), point)]
    require([t for t in record if t[0] in (14, 24)] == wanted_clip, "Ordered pixel clipping packet")
    length_code = 160 if year >= 2013 else 92
    wanted = [] if edit != 0 or state == 0 else [(length_code, 0 if state == 1 else len(PAYLOAD))]
    if edit == 0 and state == 2:
        wanted += [(310, PAYLOAD[i:i+127]) for i in range(0, len(PAYLOAD), 127)]
    require([t for t in common(record) if t[0] in (92, 160, 310)] == wanted,
            "Stale, absent, empty or changed common proxy packet")

def corrupt(record, year):
    edit, _ = row(record)
    controls = 0
    positions = [i for i, (code, _) in enumerate(record)
                 if code in set(expected_fields(edit)) | {14, 24, 92, 160, 310}]
    for at in positions:
        for op in ("change", "remove", "duplicate", "wrong-code"):
            bad = list(record); code, value = bad[at]
            if op == "change": bad[at] = (code, value+b"!" if isinstance(value, bytes) else value+17)
            elif op == "remove": del bad[at]
            elif op == "duplicate": bad.insert(at, bad[at])
            else: bad[at] = (999, value)
            controls += reject(lambda: check_record(bad, year))
    at = record.index((100, "AcDbEntity"))+1
    for code in (92, 160):
        bad = list(record); bad[at:at] = [(code, 1), (310, b"X")]
        controls += reject(lambda: check_record(bad, year))
    return controls

def inspect(path, year, binary, placement):
    require(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong transport")
    tags = load_visibility_tags(path)
    at = tags.index((9, "$ACADVER"))
    require(tags[at+1] == (1, PROFILES[year]), "Wrong profile")
    entries = [tags[a:b] for a, b in records(tags)]
    images = [r for r in entries if r[0] == (0, "IMAGE")]
    require(len(images) == 24 and {one(r, 8) for r in images} == LAYERS, "Physical IMAGE inventory")
    controls = 0
    for record in images:
        check_record(record, year)
        controls += corrupt(record, year)
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], "Independent version")
    space = doc.modelspace() if placement == 0 else doc.layouts.get("AP_PAPER") if placement == 1 else doc.blocks["AP_HOLDER"]
    images = list(space.query("IMAGE"))
    require(len(images) == 24 and {i.dxf.layer for i in images} == LAYERS, "Independent IMAGE inventory/placement")
    for item in images:
        edit, state = map(int, item.dxf.layer.split("_")[2:])
        require(tuple(item.dxf.insert) == (3., -5., 7.) and tuple(item.dxf.u_pixel) == (.5, 0., 0.)
                and tuple(item.dxf.v_pixel) == (0., .5, 0.), "Independent unchanged geometry")
        require(tuple(item.dxf.image_size) == (8., 6., 0.) and item.dxf.owner == space.block_record_handle, "Independent size/owner")
        for name, code in (("flags", 70), ("clipping", 280), ("brightness", 281), ("contrast", 282),
                           ("fade", 283), ("clipping_boundary_type", 71)):
            require(item.dxf.get(name) == expected_fields(edit)[code], "Independent appearance "+name)
        require(tuple(tuple(v) for v in item.boundary_path) == expected_clip(edit), "Independent ordered clip")
        # ezdxf maps a declared empty common cache to None; the physical packet check distinguishes it.
        require(item.proxy_graphic == (PAYLOAD if edit == 0 and state == 2 else None), "Independent proxy")
        definition = doc.entitydb[item.dxf.image_def_handle]
        require(definition.dxftype() == "IMAGEDEF" and definition.dxf.filename == "appearance.png"
                and tuple(definition.dxf.image_size) == (8., 6., 0.), "Independent IMAGEDEF")
        require([(t.code, t.value) for t in item.get_xdata("IMAGE_AP_KEEP")] == [(1000, "unchanged")], "Independent XData")
    line, = doc.modelspace().query("LINE")
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), "Following entity")
    if placement >= 2:
        require(len(list(doc.modelspace().query("INSERT"))) == (1 if placement == 2 else 0), "Block instance policy")
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, "Independent database errors/repairs")
    return controls

def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(4), ("source", "False", "True")))
    def name(s):
        year, binary, placement, output = s
        return f"image-appearance-AutoCad{year}-{binary}-{placement}-{output}.dxf"
    wanted = {name(s) for s in specs}
    def inventory(actual): require(actual == wanted, "Missing/extra appearance fixtures")
    inventory({p.name for p in directory.glob("image-appearance-*.dxf")})
    reject(lambda: inventory(wanted-{min(wanted)}))
    reject(lambda: inventory(wanted|{"image-appearance-extra.dxf"}))
    controls = 0
    for s in specs:
        year, binary, placement, output = s
        controls += inspect(directory/name(s), year, binary if output == "source" else output == "True", placement)
    print(f"PASS: {len(specs)} IMAGE appearance drawings / {24*len(specs)} independent records; "
          f"{controls} actual-packet corruptions and two inventory controls rejected; zero database errors/repairs. "
          "Stored appearance/cache coherence, not native raster rendering, is qualified.")

if __name__ == "__main__":
    require(len(sys.argv) == 2, "Usage: verify_image_appearance.py ARTIFACTS")
    main(Path(sys.argv[1]))
