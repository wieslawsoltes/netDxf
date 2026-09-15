#!/usr/bin/env python3
"""Pin exact TABLESTYLE / extension dictionary / CELLSTYLEMAP research packets.

The outputs are record fragments, not complete or independently writable drawings.
No DXF value or handle is changed. Use --write to regenerate deliberate evidence.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def packets(data):
    lines = data.splitlines(keepends=True)
    assert len(lines) % 2 == 0
    starts = [i for i in range(0, len(lines), 2) if lines[i].strip() == b"0"]
    result = {}
    for begin, end in zip(starts, starts[1:] + [len(lines)]):
        tags = [(int(lines[i]), lines[i + 1].rstrip(b"\r\n").decode("latin1")) for i in range(begin, end, 2)]
        handle = next((v.strip() for c, v in tags if c == 5), None)
        if handle:
            assert handle not in result
            result[handle] = (tags, b"".join(lines[begin:end]))
    return result


def common_owner(tags):
    depth = 0
    for code, value in tags:
        if code == 100:
            break
        if code == 102:
            if value.startswith("{"): depth += 1
            elif value == "}": depth -= 1
        if code == 330 and depth == 0:
            return value.strip()
    return None


def pin(handle, packet):
    tags, raw = packet
    return {"handle": handle, "kind": tags[0][1], "common_owner": common_owner(tags),
            "tag_count": len(tags), "byte_count": len(raw), "sha256": digest(raw)}


def assess():
    inventory = json.loads((ROOT / "tools/table_oracle/fixtures.json").read_text())
    report = {"qualification": "Exact source record fragments only; no editable style, CELLSTYLEMAP schema, or renderer qualification.",
              "acadsharp_commit": inventory["acadsharp_commit"], "files": []}
    fragments = {}
    for source in inventory["files"]:
        data = gzip.decompress((ROOT / "tests/fixtures/table-oracle" / (source["file"] + ".gz")).read_bytes())
        assert digest(data) == source["sha256"]
        records = packets(data)
        styles = [(h, p) for h, p in records.items() if p[0][0] == (0, "TABLESTYLE")]
        assert len(styles) == 1
        handle, style = styles[0]
        tags = style[0]
        marker = tags.index((102, "{ACAD_XDICTIONARY"))
        assert tags[marker + 1][0] == 360 and tags[marker + 2] == (102, "}")
        extension_handle = tags[marker + 1][1].strip()
        extension = records[extension_handle]
        assert extension[0][0] == (0, "DICTIONARY") and common_owner(extension[0]) == handle
        maps = [(h, p) for h, p in records.items() if p[0][0] == (0, "CELLSTYLEMAP") and common_owner(p[0]) == extension_handle]
        assert len(maps) == 1
        map_handle, cell_map = maps[0]
        map_slot = next(i for i, (c, v) in enumerate(extension[0]) if c in (350, 360) and v.strip() == map_handle)
        assert extension[0][map_slot - 1][0] == 3
        raw = style[1] + extension[1] + cell_map[1]
        fragment = source["file"] + ".records.dxf.gz"
        fragments[fragment] = raw
        subclass = next(i for i, tag in enumerate(tags) if tag == (100, "AcDbTableStyle"))
        first_style = next(i for i, tag in enumerate(tags) if tag[0] == 7)
        row_starts = [i for i, tag in enumerate(tags) if tag[0] == 7]
        rows = []
        for begin, end in zip(row_starts, row_starts[1:] + [len(tags)]):
            row = tags[begin:end]
            name = row[0][1]
            targets = [h for h, p in records.items() if p[0][0] == (0, "STYLE") and (2, name) in p[0]]
            assert len(targets) == 1
            rows.append({"ordinal": len(rows), "text_style_name": name, "text_style_handle": targets[0],
                         "scalar_wire_values": [[c, v.strip()] for c, v in row if c in (140, 170, 62, 63, 283, 90, 91, 1)],
                         "border_visibility_wire_values": [[c, v.strip()] for c, v in row if 284 <= c <= 289]})
        report["files"].append({"source_file": source["file"], "source_url": source["url"],
                                "source_sha256": source["sha256"], "profile": source["profile"],
                                "fragment": fragment, "fragment_sha256": digest(raw), "fragment_byte_count": len(raw),
                                "records": [pin(handle, style), pin(extension_handle, extension), pin(map_handle, cell_map)],
                                "map_dictionary_name": extension[0][map_slot - 1][1], "map_dictionary_code": extension[0][map_slot][0],
                                "style_header_wire_values": [[c, v.strip()] for c, v in tags[subclass + 1:first_style]],
                                "row_packets": rows})
    return report, fragments


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    report, fragments = assess()
    manifest_path = ROOT / "tools/table_oracle/table-style-assessment.json"
    fragment_root = ROOT / "tests/fixtures/table-style-oracle"
    serialized = json.dumps(report, indent=2, ensure_ascii=True) + "\n"
    if args.write:
        fragment_root.mkdir(parents=True, exist_ok=True)
        manifest_path.write_text(serialized)
        for name, raw in fragments.items():
            (fragment_root / name).write_bytes(gzip.compress(raw, mtime=0))
    else:
        assert manifest_path.read_text() == serialized, "TABLESTYLE assessment differs from pinned native source"
        assert {p.name for p in fragment_root.glob("*.gz")} == set(fragments)
        for name, raw in fragments.items():
            assert gzip.decompress((fragment_root / name).read_bytes()) == raw, "Native record bytes changed"
    print(f"PASS {len(fragments)} exact native TABLESTYLE / dictionary / CELLSTYLEMAP record sets; no schema-editing claim")


if __name__ == "__main__":
    main()
