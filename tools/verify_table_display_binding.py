#!/usr/bin/env python3
"""Check explicit TABLE display rebinding independently with complete DXF records.

Both resource selectors must change together. Only the exact common proxy packet
may be removed; inline cells, backing content, geometry and every block remain
unchanged. This does not qualify native TABLE regeneration or text metrics.
"""
import argparse
import copy
import json
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata, FILES
from verify_table_row_settings import wire_text
from verify_mleader_inputs import check

NAME = "BOUND_TABLE_Ω"


def value(tags, code):
    found = [v for c, v in tags if c == code]
    check(len(found) == 1, f"Expected unique group {code}")
    return found[0]


def proxy_indices(tags):
    start = tags.index([100, "AcDbEntity"]) + 1
    end = tags.index([100, "AcDbBlockReference"])
    counts = [i for i in range(start, end) if tags[i][0] in (92, 160)]
    chunks = [i for i in range(start, end) if tags[i][0] == 310]
    check(len(counts) <= 1 and (bool(counts) or not chunks), "Ambiguous common proxy framing")
    if counts:
        declared = tags[counts[0]][1]
        check(isinstance(declared, int) and 0 <= declared <= 16 * 1024 * 1024, "Invalid proxy length")
        sizes = []
        for index in chunks:
            check(isinstance(tags[index][1], dict) and set(tags[index][1]) == {"hex"}, "Invalid proxy chunk")
            sizes.append(len(bytes.fromhex(tags[index][1]["hex"])))
        check(all(size <= 127 for size in sizes) and sum(sizes) == declared, "Proxy byte count mismatch")
    return set(counts + chunks)


def selectors(tags):
    check([v for c, v in tags if c == 100] == ["AcDbEntity", "AcDbBlockReference", "AcDbTable"], "Unqualified TABLE subclasses")
    block = tags.index([100, "AcDbBlockReference"])
    table = tags.index([100, "AcDbTable"])
    first_cell = next(i for i in range(table + 1, len(tags)) if tags[i][0] == 171)
    names = [i for i in range(block + 1, table) if tags[i][0] == 2]
    pointers = [i for i in range(table + 1, first_cell) if tags[i][0] == 343]
    check(len(names) == len(pointers) == 1, "Ambiguous display selectors")
    return names[0], pointers[0]


def compare(wanted, actual):
    check(list(wanted) == list(actual), "Ordered physical record inventory changed")
    for handle in wanted:
        check(wanted[handle] == actual[handle], "Unexpected physical record change: " + handle)


def expected(before, profile):
    spelling = wire_text(NAME, profile)
    targets = [h for h, t in before.items() if t[0] == [0, "BLOCK_RECORD"] and [2, spelling] in t]
    check(len(targets) == 1, "Expected one pre-registered display BLOCK_RECORD")
    target = targets[0]
    blocks = [t for t in before.values() if t[0] == [0, "BLOCK"] and [2, spelling] in t]
    check(len(blocks) == 1 and value(blocks[0], 330) == target, "Generated BLOCK owner disagrees with BLOCK_RECORD")
    members = [t for t in before.values() if t[0][1] not in ("BLOCK", "ENDBLK") and [330, target] in t]
    check(members and all(t[0][1] in ("LINE", "SOLID", "MTEXT") for t in members), "Generated display must contain flat primitives")
    check(any(t[0] == [0, "MTEXT"] for t in members), "Generated display has no text")
    # The harness selects the first source TABLE with its known backing content.
    # Every pinned carrier has its native selected TABLE first in record order.
    tables = [(h, t) for h, t in before.items() if t[0][1] in ("ACAD_TABLE", "TABLE") and [100, "AcDbTable"] in t]
    check(tables, "No source TABLE")
    handle, original = tables[0]
    name, pointer = selectors(original)
    prior = original[pointer][1]
    check(prior != target and prior in before and before[prior][0] == [0, "BLOCK_RECORD"], "Source display pointer unresolved")
    check(value(before[prior], 2) == original[name][1], "Source display selectors disagree")
    wanted = dict(before)
    record = copy.deepcopy(original)
    record[name] = [2, spelling]
    record[pointer] = [343, target]
    removed = proxy_indices(original)
    wanted[handle] = [tag for i, tag in enumerate(record) if i not in removed]
    check(not proxy_indices(wanted[handle]), "Expected stale proxy removal")
    return wanted, handle, len(removed)


def challenge(before, after, profile):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    wanted, handle, removed = expected(before, profile)
    compare(wanted, after)
    count = 0

    def reject(candidate):
        nonlocal count
        try:
            compare(wanted, candidate)
        except (ValueError, KeyError):
            count += 1
        else:
            raise AssertionError("Corrupt display or unrelated record escaped the comparator")

    for index, (code, val) in enumerate(after[handle]):
        candidate = dict(after)
        candidate[handle] = list(after[handle])
        changed = val + "_CORRUPT" if isinstance(val, str) else val + 1 if isinstance(val, (int, float)) else "CORRUPT"
        candidate[handle][index] = [code, changed]
        reject(candidate)
    # Reintroduce a stale proxy, including when the original had no proxy bytes.
    candidate = dict(after)
    candidate[handle] = list(after[handle])
    at = candidate[handle].index([100, "AcDbBlockReference"])
    candidate[handle][at:at] = [[92, 1], [310, {"hex": "00"}]]
    reject(candidate)
    for other in after:
        candidate = dict(after)
        if other == handle:
            del candidate[other]
        else:
            candidate[other] = after[other] + [[999, "CORRUPT"]]
        reject(candidate)
    return count, removed


def inventory():
    return {f"table-display-{phase}-{file}-{binary}.dxf"
            for file in FILES for binary in (False, True) for phase in ("before", "after")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    check({p.name for p in args.directory.glob("table-display-*.dxf")} == inventory(), "Exact display fixture inventory required")
    root = Path(__file__).resolve().parents[1]
    pins = json.loads((root / "tools/table_oracle/fixtures.json").read_text(encoding="utf-8"))["files"]
    profiles = {p["file"]: p["profile"] for p in pins}
    count = removed = pairs = 0
    for file in FILES:
        profile = profiles[file]
        for binary in (False, True):
            paths = [args.directory / f"table-display-{phase}-{file}-{binary}.dxf" for phase in ("before", "after")]
            for path in paths:
                check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Display transport changed")
                doc = ezdxf.readfile(path)
                check(doc.dxfversion == profile, "Display source profile changed")
                audit = doc.audit()
                check(not audit.errors and not audit.fixes, "Display graph needs independent repairs")
            controls, proxy_tags = challenge(records(paths[0]), records(paths[1]), profile)
            count += controls
            removed += proxy_tags
            pairs += 1
            print("PASS " + paths[1].name)
    check(pairs == 10, "Expected ten native-family display pairs")
    print(f"PASS ezdxf {ezdxf.__version__}: {pairs} display pairs, {count} corruption controls, {removed} stale proxy tags removed; no native regeneration claim")


if __name__ == "__main__":
    main()
