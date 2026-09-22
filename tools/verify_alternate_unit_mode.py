#!/usr/bin/env python3
"""Qualify DIMALTU codes 1..8 independently from primary DIMLUNIT and stacking."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject
from verify_dimlfac_fidelity import PROFILES, records
from verify_dimension_text_blocks import one, corrupt

def base_code(row):
    return 4 if row == 2 else 8

def override_code(row):
    return 0 if row == 0 else 8 if row in (1, 2, 11) else row - 2

def header(tags, variable, wanted):
    indexes = [i for i, t in enumerate(tags) if t == (9, variable)]
    require(len(indexes) == 1, "Header variable absent/duplicate: " + variable)
    at = indexes[0] + 1
    end = next(i for i in range(at, len(tags)) if tags[i][0] in (0, 9))
    require(tags[at:end] == [(70, wanted)], "Incomplete/mistyped header: " + variable)
    return at

def packet(record, row):
    apps = [i for i, t in enumerate(record) if t == (1001, "ACAD")]
    require(len(apps) == 1, "ACAD application count")
    start = apps[0] + 1
    end = next((i for i in range(start, len(record)) if record[i][0] == 1001), len(record))
    expected = [(1000, "DSTYLE"), (1002, "{"), (1070, 140), (1040, .75)]
    if row:
        expected += [(1070, 273), (1070, override_code(row))]
    expected += [(1002, "}")]
    require([key(t) for t in record[start:end]] == [key(t) for t in expected], "Complete sparse/combined DSTYLE packet")
    return range(start, end)

def damage_packets(tags, check, positions):
    controls = 0
    for at in positions:
        for operation in ("change", "remove", "duplicate", "wrong-code"):
            bad = list(tags)
            code, value = bad[at]
            if operation == "change":
                bad[at] = (code, value + "_BAD" if isinstance(value, str) else value + 1)
            elif operation == "remove":
                del bad[at]
            elif operation == "duplicate":
                bad.insert(at, bad[at])
            else:
                bad[at] = (999, value)
            controls += reject(lambda: check(bad))
    return controls

def inspect(path, year, binary, kind=None, placement=None, code=None):
    data = path.read_bytes()
    require(data.startswith(b"AutoCAD Binary DXF") == binary, "Wrong transport")
    tags = load_tags(path)
    av = tags.index((9, "$ACADVER"))
    require(tags[av + 1] == (1, PROFILES[year]), "Wrong version")
    controls = 0
    for name, wanted in (("$DIMALTU", code if code is not None else 8), ("$DIMLUNIT", 6)):
        at = header(tags, name, wanted)
        controls += damage_packets(tags, lambda bad: header(bad, name, wanted), [at])
    entries = [tags[a:b] for a, b in records(tags)]
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], "Independent version")
    require(doc.header["$DIMALTU"] == (code if code is not None else 8), "Independent alternate header")
    require(doc.header["$DIMLUNIT"] == 6, "Independent primary header")
    if code is not None:
        styles = [r for r in entries if r[0] == (0, "DIMSTYLE") and (2, "ALTU_TABLE") in r]
        require(len(styles) == 1, "Table style absent/duplicate")
        controls += corrupt(styles[0], {273: code, 277: 6})
        style = doc.dimstyles.get("ALTU_TABLE")
        require(style.dxf.dimaltu == code and style.dxf.dimlunit == 6, "Independent table mapping")
    else:
        expected_type = ("DIMENSION", "ARC_DIMENSION", "LEADER")[kind]
        hosts = [r for r in entries if r[0][1] in ("DIMENSION", "ARC_DIMENSION", "LEADER")]
        require(len(hosts) == 12, "Physical host count")
        require({one(r, 8)[1] for r in hosts} == {f"ALTU_{i:02d}" for i in range(12)}, "Physical host inventory")
        for host in hosts:
            row = int(one(host, 8)[1].split("_")[1])
            require(host[0] == (0, expected_type), "Physical host type")
            style_name = f"ALTU_STYLE_{row:02d}"
            styles = [r for r in entries if r[0] == (0, "DIMSTYLE") and (2, style_name) in r]
            require(len(styles) == 1, "Named style count")
            controls += corrupt(styles[0], {273: base_code(row), 277: 6})
            controls += corrupt(host, {3: style_name})
            positions = packet(host, row)
            controls += damage_packets(host, lambda bad: packet(bad, row), positions)
        space = doc.modelspace() if placement == 0 else doc.layouts.get("ALTU_PAPER") if placement == 1 else doc.blocks["ALTU_HOLDER"]
        loaded = list(space.query("DIMENSION ARC_DIMENSION LEADER"))
        require(len(loaded) == 12, "Independent host count/placement")
        for host in loaded:
            row = int(host.dxf.layer.split("_")[1])
            require(host.dxftype() == expected_type and host.dxf.owner == space.block_record_handle, "Independent host type/owner")
            style = doc.dimstyles.get(host.dxf.dimstyle)
            require(style.dxf.dimaltu == base_code(row) and style.dxf.dimlunit == 6, "Independent base values")
            wanted = {"dimtxt": .75}
            if row:
                wanted["dimaltu"] = override_code(row)
            require(host.get_acad_dstyle(style) == wanted, "Independent complete override field")
            require([(t.code, t.value) for t in host.get_xdata("ALTU_KEEP")] == [(1000, "unchanged")], "Unrelated XData")
            if kind != 2:
                require(host.dxf.text == "FIXED", "Primary text")
                label, = host.get_geometry_block().query("MTEXT")
                require(label.text == "FIXED", "Fixed primary label")
        line, = doc.modelspace().query("LINE")
        require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), "Following geometry")
        if placement >= 2:
            inserts = list(doc.modelspace().query("INSERT"))
            require(len(inserts) == (1 if placement == 2 else 0), "Referenced/unreferenced block policy")
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, "Independent graph errors or repairs")
    return controls

def main(directory):
    table = list(itertools.product(PROFILES, (False, True), range(1, 9), ("source", "False", "True")))
    wire = [s for s in itertools.product(PROFILES, (False, True), range(3), range(4), (False, True), ("source", "False", "True"))
            if not (s[0] == 2000 and s[2] == 1)]
    def table_name(s):
        year, binary, code, output = s
        return f"alternate-unit-table-AutoCad{year}-{binary}-{code}-{output}.dxf"
    def wire_name(s):
        year, binary, kind, placement, raw, output = s
        return f"alternate-unit-mode-AutoCad{year}-{binary}-{kind}-{placement}-{raw}-{output}.dxf"
    expected = {table_name(s) for s in table} | {wire_name(s) for s in wire}
    def inventory(actual):
        require(actual == expected, f"Mode inventory missing={len(expected-actual)}, extra={len(actual-expected)}")
    inventory({p.name for p in directory.glob("alternate-unit-*.dxf")})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {"alternate-unit-extra.dxf"}))
    controls = 0
    for s in table:
        year, binary, code, output = s
        controls += inspect(directory / table_name(s), year, binary if output == "source" else output == "True", code=code)
    for s in wire:
        year, binary, kind, placement, raw, output = s
        controls += inspect(directory / wire_name(s), year, binary if output == "source" else output == "True", kind, placement)
    print(f"PASS: {len(expected)} DIMALTU drawings / {12*len(wire)} independently loaded host records; "
          f"{controls} actual-packet corruptions and two inventory controls rejected; zero graph errors/repairs. "
          "Mode storage, not native locale or alternate-label rendering, is qualified.")

if __name__ == "__main__":
    require(len(sys.argv) == 2, "Usage: verify_alternate_unit_mode.py ARTIFACTS")
    main(Path(sys.argv[1]))
