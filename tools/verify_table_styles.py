#!/usr/bin/env python3
"""Independently verify immutable TABLESTYLE packets and owned opaque map graphs.

Native source/output subclass packets must match exactly. Common ownership,
extension dictionary links and STYLE resource identities are checked separately.
This does not qualify CELLSTYLEMAP semantics, editing or rendering.
"""
import argparse
import copy
import gzip
import hashlib
import json
import tempfile
from pathlib import Path
import ezdxf
from verify_mleader_inputs import records, check, decode_once


def verify_file(path, binary, year):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "TABLESTYLE transport changed")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}[year], "TABLESTYLE source profile changed")
    return doc


def payload(record):
    start = next(i for i, t in enumerate(record) if t[0] == 100)
    return record[start:]


def common(record):
    owner = extension = None
    reactors = []
    group = None
    for code, value in record:
        if code == 100: break
        if code == 102:
            group = None if value == "}" else value
        elif code == 330:
            if group == "{ACAD_REACTORS": reactors.append(value)
            elif group is None: owner = value
        elif code == 360 and group == "{ACAD_XDICTIONARY": extension = value
    return owner, extension, sorted(reactors)


def entries(record):
    name = None
    result = []
    for code, value in payload(record):
        if code == 3: name = value
        elif code in (350, 360):
            check(name is not None, "Dictionary entry has no name")
            result.append([name, code, value]); name = None
    return result


def verify_native(before, after):
    styles = [h for h, r in before.items() if r[0] == [0, "TABLESTYLE"]]
    check(styles == [h for h, r in after.items() if r[0] == [0, "TABLESTYLE"]], "Native TABLESTYLE identity/count changed")
    for handle in styles:
        source, saved = before[handle], after[handle]
        check(payload(source) == payload(saved), "Native TABLESTYLE subclass changed")
        check(common(source) == common(saved), "Native TABLESTYLE common graph changed")
        extension = common(source)[1]
        check(extension is not None and extension in after, "Native style lost its extension dictionary")
        check(common(before[extension]) == common(after[extension]), "Native extension owner changed")
        check(entries(before[extension]) == entries(after[extension]), "Native map ownership entry changed")
        for _, _, child in entries(before[extension]):
            check(after[child][0] == before[child][0], "Native extension child type changed")
            check(payload(after[child]) == payload(before[child]), "Native extension child packet changed")
            check(common(after[child]) == common(before[child]), "Native extension child common graph changed")
        map_handle = next(value for name, code, value in entries(before[extension]) if name == "ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP" and code == 360)
        check(after[map_handle][0] == [0, "CELLSTYLEMAP"], "Native map type changed")
        check(payload(before[map_handle]) == payload(after[map_handle]), "Native opaque map packet changed")
        check(common(before[map_handle]) == common(after[map_handle]), "Native map owner/reactors changed")
        for code, name in source:
            if code != 7: continue
            source_style = [h for h, r in before.items() if r[0] == [0, "STYLE"] and [2, name] in r]
            saved_style = [h for h, r in after.items() if r[0] == [0, "STYLE"] and [2, name] in r]
            check(source_style == saved_style and len(source_style) == 1, "Native named STYLE identity changed")


def one_style(found):
    matches = [r for r in found.values() if r[0] == [0, "TABLESTYLE"]]
    check(len(matches) == 1, "Synthetic TABLESTYLE count changed")
    return matches[0]


def synthetic_packet():
    tags = [[100, "AcDbTableStyle"], [3, "Independent description"], [70, 0], [71, 0], [40, 0.06], [41, 0.06], [280, 0], [281, 0]]
    for i in range(3):
        tags.extend([[7, r"STYLE_\U+0052EF"], [140, 1.0 + i], [170, 5], [62, 0], [63, 257], [283, 0], [90, 512], [91, 0], [1, "private raw format"], [284, 1]])
    return tags


def projection_packet(variant):
    tags = synthetic_packet(); first = 8
    if variant == "leading280": tags.insert(1, [280, 0])
    if variant == "duplicate-margin": tags.insert(first, [40, 3.0])
    if variant == "missing-height": tags.pop(first + 1)
    if variant == "duplicate-height": tags.insert(first + 1, [140, 3.0])
    if variant == "private-application": tags[first + 1:first + 1] = [[102, "{PRIVATE"], [7, "PRIVATE_STYLE"], [140, 99.0], [102, "}"]]
    if variant == "private-subclass": tags.extend([[100, "PrivateStyle"], [7, "PRIVATE_STYLE"], [310, {"hex": "00ff"}]])
    if variant == "unknown-first": tags[0] = [100, "PrivateStyle"]
    if variant == "four-rows": tags.extend(copy.deepcopy(tags[first:first + 10]))
    if variant == "case-spelling": tags = [[c, r"style_\U+0072ef" if c == 7 else v] for c, v in tags]
    return tags


def verify_exact(expected, actual):
    check(expected == payload(actual), "Synthetic retained TABLESTYLE packet changed")


def reject_change(actual, code, validate):
    changed = copy.deepcopy(actual)
    index = next(i for i, t in enumerate(changed) if t[0] == code)
    value = changed[index][1]
    changed[index][1] = value + 1 if isinstance(value, (int, float)) else "CORRUPTED"
    try: validate(changed)
    except (ValueError, KeyError, StopIteration): return 1
    raise AssertionError(f"Changed TABLESTYLE group {code} escaped the identical validator")


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("directory", type=Path); args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    inventory = json.loads((root / "tools/table_oracle/fixtures.json").read_text())["files"]
    expected = set(); checked = negative = 0
    with tempfile.TemporaryDirectory() as temporary:
        for item in inventory:
            original = gzip.decompress((root / "tests/fixtures/table-oracle" / (item["file"] + ".gz")).read_bytes())
            check(hashlib.sha256(original).hexdigest() == item["sha256"], "Native source digest changed")
            path = Path(temporary) / item["file"]; path.write_bytes(original); before = records(path)
            for binary, full in ([(False, False), (True, False)] + ([(False, True), (True, True)] if item["file"].startswith("acad_table_") else [])):
                kind = "full-native" if full else "native"
                name = f"table-style-{kind}-{item['file']}-{binary}.dxf"; expected.add(name); path = args.directory / name
                check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Native transport changed")
                check(ezdxf.readfile(path).dxfversion == item["profile"], "Native profile changed")
                after = records(path); verify_native(before, after); checked += 1
                if not full:
                    # Only the core style graph is native; external reactor carriers
                    # are explicit placeholders, never represented as native entities.
                    core_style = next(r for r in before.values() if r[0] == [0, "TABLESTYLE"])
                    owner_handle, extension_handle, _ = common(core_style)
                    check(common(before[owner_handle]) == common(after[owner_handle]), "Native style dictionary common metadata changed")
                    check(entries(before[owner_handle]) == entries(after[owner_handle]), "Native style dictionary entries changed")
                    core = {next(h for h, r in before.items() if r is core_style), owner_handle, extension_handle,
                            next(h for h, r in before.items() if r[0] == [0, "CELLSTYLEMAP"]), "C"}
                    for handle in common(core_style)[2]:
                        if handle not in core:
                            check(after[handle][0] == [0, "ACDBPLACEHOLDER"], "Scoped external reactor placeholder changed")

                style = next(h for h, r in after.items() if r[0] == [0, "TABLESTYLE"])
                map_handle = next(h for h, r in after.items() if r[0] == [0, "CELLSTYLEMAP"])
                controls = [(style, 40), (style, 7), (map_handle, 90)]
                if item["file"] == "sample_AC1018_ascii.dxf": controls.append(("1401", 90))
                for handle, code in controls:
                    def validate(record, key=handle):
                        changed = dict(after); changed[key] = record; verify_native(before, changed)
                    negative += reject_change(after[handle], code, validate)
    for binary in (False, True):
        for year in (2004, 2007, 2010, 2013, 2018):
            name = f"table-style-lifecycle-AutoCad{year}-{binary}.dxf"; expected.add(name); path = args.directory / name
            doc = verify_file(path, binary, year)
            found = records(path); style = one_style(found)
            expected_payload = synthetic_packet()
            for tag in expected_payload:
                if tag[0] == 7: tag[1] = "RENAMED_Ω_STYLE"
            def validate(record):
                decoded = [[c, decode_once(v) if c == 7 else v] for c, v in payload(record)]
                check(decoded == expected_payload, "Renamed STYLE fields or retained payload changed")
            validate(style); negative += reject_change(style, 7, validate); checked += 1
            check(sum(decode_once(style.dxf.name) == "RENAMED_Ω_STYLE" for style in doc.styles) == 1, "Renamed STYLE resource absent")
        for variant in ("leading280", "duplicate-margin", "missing-height", "duplicate-height", "private-application", "private-subclass", "unknown-first", "four-rows", "case-spelling"):
            name = f"table-style-projection-{variant}-{binary}.dxf"; expected.add(name)
            verify_file(args.directory / name, binary, 2018)
            style = one_style(records(args.directory / name)); wanted = projection_packet(variant)
            verify_exact(wanted, style); negative += reject_change(style, 140, lambda record: verify_exact(wanted, record)); checked += 1
        for kind, target_type in (("appid", "APPID"), ("entity", "LINE"), ("block-member", "LINE"), ("attribute", "ATTRIB"), ("endblock", "ENDBLK")):
            name = f"table-style-removal-{kind}-{binary}.dxf"; expected.add(name)
            verify_file(args.directory / name, binary, 2018)
            found = records(args.directory / name); style = one_style(found)
            def validate(record):
                pointers = [v for c, v in record if c == 340]
                check(len(pointers) == 1 and pointers[0] in found and found[pointers[0]][0] == [0, target_type], "Protected semantic dependency changed")
                check(payload(record)[:-1] == synthetic_packet(), "Dependency packet changed")
            validate(style); negative += reject_change(style, 340, validate); checked += 1
        for variant in ("older-profile", "private-class", "private-class-empty"):
            name = f"table-style-boundary-{variant}-{binary}.dxf"; expected.add(name); path = args.directory / name
            verify_file(path, binary, 2000 if variant == "older-profile" else 2018)
            if variant == "older-profile":
                style = one_style(records(path)); wanted = synthetic_packet()
                verify_exact(wanted, style); negative += reject_change(style, 140, lambda record: verify_exact(wanted, record))
            else:
                doc = ezdxf.readfile(path); definition = doc.classes.get("TABLESTYLE")
                check(definition.dxf.cpp_class_name == "PrivateCpp" and definition.dxf.instance_count == 73, "Private CLASS rewritten")
                if variant == "private-class":
                    style = one_style(records(path)); wanted = [[100, "PrivateTableStyle"], [1, "private payload"]]
                    verify_exact(wanted, style); negative += reject_change(style, 1, lambda record: verify_exact(wanted, record))
            checked += 1
        for decoy in ("absent", "unknown-entity", "discarded-underlay", "dictionary-entity", "ignored-section", "private-identity"):
            name = f"table-style-identity-{decoy}-{binary}.dxf"; expected.add(name); path = args.directory / name
            verify_file(path, not binary, 2018); style = one_style(records(path)); wanted = synthetic_packet() + [[340, "C0FFEE01"]]
            verify_exact(wanted, style); negative += reject_change(style, 340, lambda record: verify_exact(wanted, record)); checked += 1
    check({p.name for p in args.directory.glob("table-style-*.dxf")} == expected, "Expected all TABLESTYLE output carriers")
    check(checked == 70 and negative == 98, "TABLESTYLE qualification count changed")
    print(f"PASS {checked} TABLESTYLE outputs (10 scoped native graphs, 4 full native drawings) and {negative} corruption controls; no map semantics or regeneration claim")


if __name__ == "__main__":
    main()
