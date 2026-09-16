#!/usr/bin/env python3
"""Verify authored/transferred/restructured CELLSTYLEMAP packets with ezdxf.

Checks exact field values, structural inventories, resource remapping and native
before/after physical records. Does not assert TABLE synchronization or rendering.
"""
import argparse
import copy
import gzip
import hashlib
import json
from pathlib import Path
import tempfile
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES
from verify_table_style_borders import PROFILES
from verify_table_row_settings import wire_text
from verify_table_styles import common, entries as dictionary_entries
from verify_mleader_inputs import check, decode_once

SHAPES = ("empty", "empty-format", "no-margins", "zero-grids", "full", "mixed")
MODES = ("reorder", "expand", "frames", "clear")
NAME = "Authored Ω \\U+0041 😀"


def payload(tags):
    return tags[next(i for i, tag in enumerate(tags) if tag == [100, "AcDbCellStyleMap"]):]


def split_entries(tags):
    check(tags[:1] == [[100, "AcDbCellStyleMap"]] and tags[1][0] == 90, "Wrong map header")
    result, index = [], 2
    for _ in range(tags[1][1]):
        start = index
        check(tags[index:index + 2] == [[300, "CELLSTYLE"], [1, "TABLEFORMAT_BEGIN"]], "Wrong entry prefix")
        index += 1
        stack = []
        while index < len(tags):
            code, value = tags[index]
            index += 1
            if code == 1:
                check(value.endswith("_BEGIN"), "Wrong frame opening")
                stack.append(value[:-6])
            elif code == 309:
                check(stack and value == stack.pop() + "_END", "Wrong frame ending")
                if not stack:
                    break
        check(not stack, "Unterminated format")
        check(tags[index] == [1, "CELLSTYLE_BEGIN"] and [t[0] for t in tags[index + 1:index + 4]] == [90, 91, 300]
              and tags[index + 4] == [309, "CELLSTYLE_END"], "Wrong entry metadata")
        index += 5
        result.append(copy.deepcopy(tags[start:index]))
    check(index == len(tags), "Surplus or miscounted map entries")
    return result


def one_map(items):
    found = [(key, tags) for key, tags in items.items() if tags[0] == [0, "CELLSTYLEMAP"]]
    check(len(found) == 1, "Expected exactly one map")
    return found[0]


def resource(items, kind, name):
    found = [key for key, tags in items.items() if tags[0] == [0, kind] and [2, name] in tags]
    check(len(found) == 1, "Expected one destination resource")
    return found[0]


def authored_packet(shape, profile, style, line):
    def entry(identifier, kind, name, empty=False, type_code=5):
        packet = [[300, "CELLSTYLE"], [1, "TABLEFORMAT_BEGIN"], [90, type_code], [170, 0 if empty else 1]]
        if not empty:
            packet += [[91, 17], [92, 42], [62, 10], [93, 3], [300, "CONTENTFORMAT"], [1, "CONTENTFORMAT_BEGIN"],
                       [90, 19], [91, 2], [92, 4], [93, 2], [300, wire_text("%lu2%pr3 Literal \\U+0041 — Ω 😀 0", profile)],
                       [40, .25], [140, 2.25], [94, 6], [62, 11], [340, style], [144, 3.5], [309, "CONTENTFORMAT_END"],
                       [171, 0 if shape == "no-margins" else 1]]
            if shape != "no-margins":
                packet += [[301, "MARGIN"], [1, "CELLMARGIN_BEGIN"]] + [[40, v] for v in (.25, .5, .75, 1, 1.25, 1.5)] + [[309, "CELLMARGIN_END"]]
            n = 0 if shape == "zero-grids" else 6
            packet.append([94, n])
            for slot in range(n):
                packet += [[95, 1 << slot], [302, "GRIDFORMAT"], [1, "GRIDFORMAT_BEGIN"], [90, 7], [91, 1],
                           [62, 20 + slot], [92, 25], [340, line], [93, slot % 2], [40, .125 * (slot + 1)], [309, "GRIDFORMAT_END"]]
        packet += [[309, "TABLEFORMAT_END"], [1, "CELLSTYLE_BEGIN"], [90, identifier], [91, kind],
                   [300, wire_text(name, profile)], [309, "CELLSTYLE_END"]]
        return packet
    blocks = [] if shape == "empty" else [entry(7, 3, NAME, shape == "empty-format")]
    if shape == "mixed":
        blocks += [entry(-1, -2, "Empty", True, 9), copy.deepcopy(blocks[0])]
    return [[100, "AcDbCellStyleMap"], [90, len(blocks)]] + sum(blocks, [])


def validate_created(items, wanted):
    key, tags = one_map(items)
    check(payload(tags) == wanted, "Authored or transferred fields changed")
    split_entries(payload(tags))
    owner, extension, reactors = common(tags)
    check(extension is None and reactors == [owner], "Authored common metadata changed")
    check(tags == [[0, "CELLSTYLEMAP"], [5, key], [102, "{ACAD_REACTORS"], [330, owner],
                   [102, "}"], [330, owner]] + wanted, "Authored physical identity or common framing changed")
    check(owner in items and ["AUTHORED_MAP", 360, key] in dictionary_entries(items[owner]), "Authored owner slot changed")
    for code, value in wanted:
        if code == 340 and int(value, 16):
            check(value in items and items[value][0][1] in ("STYLE", "LTYPE"), "Unresolved transferred resource")


def challenge_created(items, wanted):
    validate_created(items, wanted)
    key, tags = one_map(items)
    count = 0
    # Include common identity/ownership/reactor fields, not only the authored body.
    for index in range(len(tags)):
        changed = dict(items); changed[key] = copy.deepcopy(tags)
        value = changed[key][index][1]
        changed[key][index][1] = value + "_CORRUPT" if isinstance(value, str) else value + 1
        try:
            validate_created(changed, wanted)
        except (ValueError, KeyError, IndexError, StopIteration):
            count += 1
        else:
            raise AssertionError("Authored field corruption escaped verifier")
    # The same positive validator must reject a missing dictionary owner link and
    # every absent bound resource. These changes are made to actual output data.
    owner = common(tags)[0]
    changed = dict(items)
    changed[owner] = [item for item in items[owner] if item != [360, key]]
    candidates = [changed]
    for target in {value for code, value in wanted if code == 340 and int(value, 16)}:
        changed = dict(items)
        del changed[target]
        candidates.append(changed)
    for changed in candidates:
        try:
            validate_created(changed, wanted)
        except (ValueError, KeyError, IndexError, StopIteration):
            count += 1
        else:
            raise AssertionError("Authored graph corruption escaped verifier")
    return count


def restructured_packet(before, mode, profile):
    blocks = split_entries(before)
    check(len(blocks) == 3, "Native source inventory changed")
    if mode == "clear": blocks = []
    elif mode == "reorder": blocks.reverse()
    elif mode == "frames": blocks[0] = [[300, "CELLSTYLE"], [1, "TABLEFORMAT_BEGIN"], [90, 5], [170, 0], [309, "TABLEFORMAT_END"]] + blocks[0][-5:]
    else:
        extra = copy.deepcopy(blocks[0]); extra[-4:] = [[90, 42], [91, 9], [300, wire_text(NAME, profile)], [309, "CELLSTYLE_END"]]
        blocks.append(extra)
    return [[100, "AcDbCellStyleMap"], [90, len(blocks)]] + sum(blocks, [])


def check_native(before, after, mode, profile):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    key, tags = one_map(before)
    wanted = copy.deepcopy(before)
    wanted[key] = tags[:len(tags) - len(payload(tags))] + restructured_packet(payload(tags), mode, profile)
    check(list(wanted) == list(after), "Native physical record inventory changed")
    check(wanted == after, "Native change escaped explicit map structure scope")
    split_entries(payload(after[key]))


def challenge_native(before, after, mode, profile):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    key, original = one_map(before)
    wanted = dict(before)
    wanted[key] = original[:len(original) - len(payload(original))] + restructured_packet(payload(original), mode, profile)
    def validate(candidate):
        check(list(wanted) == list(candidate), "Native physical record inventory changed")
        check(wanted == candidate, "Native change escaped explicit map structure scope")
    validate(after)
    tags = after[key]; count = 0
    # Only actual parsed values are corrupted, after the separately checked metadata normalization.
    for index in range(len(tags)):
        changed = dict(after); changed[key] = copy.deepcopy(tags)
        value = changed[key][index][1]
        changed[key][index][1] = value + "_CORRUPT" if isinstance(value, str) else value + 1
        try: validate(changed)
        except (ValueError, KeyError, IndexError, StopIteration): count += 1
        else: raise AssertionError("Native field corruption escaped verifier")
    for other in after:
        if other == key: continue
        changed = dict(after); changed[other] = after[other] + [[999, "CORRUPT"]]
        try: validate(changed)
        except (ValueError, KeyError, IndexError, StopIteration): count += 1
        else: raise AssertionError("Unrelated record corruption escaped verifier")
    return count


def inventory():
    expected = set()
    for version in PROFILES:
        for binary in (False, True):
            expected.update(f"map-structure-create-{version}-{binary}-{shape}.dxf" for shape in SHAPES)
            expected.update(f"map-structure-transfer-{file}-{version}-{binary}.dxf" for file in FILES)
    for file in FILES:
        for binary in (False, True):
            expected.update(f"map-structure-native-{phase}-{file}-{binary}-{mode}.dxf" for phase in ("before", "after") for mode in MODES)
    return expected


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("directory", type=Path); args = parser.parse_args()
    check({p.name for p in args.directory.glob("map-structure-*.dxf")} == inventory(), "Exact structural fixture inventory required")
    root = Path(__file__).resolve().parents[1]; sources = {}
    manifest = json.loads((root / "tools/table_oracle/fixtures.json").read_text(encoding="utf-8"))["files"]
    pins = {item["file"]: item for item in manifest}
    check(set(pins) == set(FILES), "Native source manifest inventory changed")
    with tempfile.TemporaryDirectory() as temporary:
        for file in FILES:
            original = gzip.decompress((root / "tests/fixtures/table-oracle" / (file + ".gz")).read_bytes())
            check(hashlib.sha256(original).hexdigest() == pins[file]["sha256"], "Pinned native source digest changed")
            path = Path(temporary) / file; path.write_bytes(original)
            sources[file] = records(path)
    checked = controls = 0
    for version, profile in PROFILES.items():
        for binary in (False, True):
            for label in SHAPES + FILES:
                transfer = label in FILES
                name = f"map-structure-transfer-{label}-{version}-{binary}.dxf" if transfer else f"map-structure-create-{version}-{binary}-{label}.dxf"
                path = args.directory / name; data = records(path); style, line = resource(data, "STYLE", "DEST_STYLE"), resource(data, "LTYPE", "DEST_LINE")
                if transfer:
                    source = sources[label]; wanted = copy.deepcopy(payload(one_map(source)[1]))
                    for tag in wanted:
                        if tag[0] == 340 and int(tag[1], 16):
                            check(tag[1] in source and source[tag[1]][0][1] in ("STYLE", "LTYPE"), "Source resource kind is not qualified for transfer")
                            tag[1] = style if source[tag[1]][0] == [0, "STYLE"] else line
                        elif tag[0] == 300: tag[1] = wire_text(decode_once(tag[1]), profile)
                else: wanted = authored_packet(label, profile, style, line)
                doc = ezdxf.readfile(path); check(doc.dxfversion == profile, "Destination profile changed")
                check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Destination transport changed")
                audit = doc.audit(); check(not audit.errors and not audit.fixes, "Authored graph needs independent repairs")
                controls += challenge_created(data, wanted); checked += 1
    for file in FILES:
        for binary in (False, True):
            for mode in MODES:
                paths = [args.directory / f"map-structure-native-{phase}-{file}-{binary}-{mode}.dxf" for phase in ("before", "after")]
                docs = [ezdxf.readfile(path) for path in paths]
                check(docs[0].dxfversion == docs[1].dxfversion == pins[file]["profile"], "Native profile changed")
                for path in paths: check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Native transport changed")
                controls += challenge_native(records(paths[0]), records(paths[1]), mode, docs[0].dxfversion); checked += 1
    check(checked == 150, "Expected 110 authored/transferred maps and 40 native structure pairs")
    print(f"PASS ezdxf {ezdxf.__version__}: {checked} structure checks, {controls} actual-output corruptions; no TABLE synchronization claim")


if __name__ == "__main__":
    main()
