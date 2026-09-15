#!/usr/bin/env python3
"""Qualify twelve typed erasure exports against independent ezdxf ownership fixtures."""
import argparse
import hashlib
import io
import json
import struct
from pathlib import Path

import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler

ROOT = Path(__file__).resolve().parents[1]
PROFILES = {2000: "AC1015", 2004: "AC1018", 2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}


def check(condition, message):
    if not condition:
        raise ValueError(message)


def wire_records(path):
    data = path.read_bytes()
    if data.startswith(b"AutoCAD Binary DXF"):
        loader = binary_tags_loader(data)
    else:
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError:
            text = data.decode("cp1252")
        loader = ascii_tags_loader(io.StringIO(text, newline=None))
    records, current = {}, []

    def flush():
        if not current or current[0].value in ("SECTION", "ENDSEC", "ENDTAB", "EOF"):
            return
        for tag in current:
            if tag.code == 100:
                return
            if tag.code in (5, 105):
                key = int(tag.value, 16)
                check(key not in records, "Duplicate numeric object identity")
                records[key] = list(current)
                return

    for tag in tag_compiler(loader):
        if tag.code == 0:
            flush()
            current = []
        current.append(tag)
    flush()
    return records


def json_value(value):
    if isinstance(value, bytes):
        return {"hex": value.hex()}
    if hasattr(value, "xyz") or isinstance(value, (tuple, list)):
        return list(value)
    return value


def exact(value):
    if isinstance(value, str):
        return decode_dxf_unicode(value)
    if isinstance(value, float):
        return struct.pack("<d", value)
    if hasattr(value, "xyz") or isinstance(value, (tuple, list)):
        return tuple(exact(part) for part in value)
    return value


def payload(record):
    start = next(i for i, tag in enumerate(record) if tag.code == 100 and tag.value == "AcDbXrecord") + 1
    return [(tag.code, exact(tag.value)) for tag in record[start:]]


def entries(record):
    found, name = {}, None
    for tag in record:
        if tag.code == 3:
            name = decode_dxf_unicode(tag.value)
        elif name is not None and tag.code in (350, 360):
            check(name not in found, "Duplicate dictionary name")
            found[name] = (tag.code, int(tag.value, 16))
            name = None
    return found


def compare(path, source, fixture, binary):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Transport differs from filename")
    original, saved = ezdxf.readfile(source), ezdxf.readfile(path)
    check(saved.dxfversion == fixture["version"], "Actual profile differs from fixture")
    source_wire, saved_wire = wire_records(source), wire_records(path)
    erased = {int(value, 16) for value in fixture["erased_handles"].values()}
    check(len(erased) == 5 and not erased.intersection(saved_wire), "Erased ownership subtree remains in output")
    for entity in saved.entitydb.values():
        check(int(entity.dxf.handle, 16) not in erased, "Erased handle remains in independent object database")
    root = saved.rootdict
    root_handle = int(fixture["kept_handles"]["root"], 16)
    check(int(root.dxf.handle, 16) == root_handle and "QA_ERASE" not in root, "Root identity or erased entry differs")
    before_entries, after_entries = entries(source_wire[root_handle]), entries(saved_wire[root_handle])
    for name in ("QA_KEEP", "QA_KEEP_ALIAS"):
        check(after_entries[name] == before_entries[name], "Surviving alias code or target changed: " + name)
    keep = root["QA_KEEP"]
    check(root["QA_KEEP_ALIAS"] is keep, "Survivor aliases no longer share identity")
    keep_handle = int(fixture["kept_handles"]["record"], 16)
    check(int(keep.dxf.handle, 16) == keep_handle and keep.dxf.owner == root.dxf.handle, "Survivor identity or owner changed")
    check(payload(source_wire[keep_handle]) == payload(saved_wire[keep_handle]), "Ordered survivor payload or XData changed")
    old_keep = original.rootdict["QA_KEEP"]
    check(keep.get_reactors() == old_keep.get_reactors(), "Survivor reactors changed")
    check(list(keep.get_xdata("QA_ERASURE")) == list(old_keep.get_xdata("QA_ERASURE")), "Survivor XData changed")
    check(not keep.has_extension_dict, "Survivor acquired erased extension metadata")
    line_handle = fixture["kept_handles"]["line"]
    lines = list(saved.modelspace().query("LINE"))
    check(len(saved.modelspace()) == 1 and len(lines) == 1 and lines[0].dxf.handle == line_handle, "Following geometry inventory or identity changed")
    line, old_line = lines[0], original.entitydb[line_handle]
    for field in ("start", "end"):
        check(exact(line.dxf.get(field)) == exact(old_line.dxf.get(field)), "Following LINE exact vector changed")
    check(line.dxf.layer == old_line.dxf.layer and line.dxf.owner == old_line.dxf.owner, "Following LINE layer or owner changed")
    check(list(line.get_xdata("QA_ERASURE")) == list(old_line.get_xdata("QA_ERASURE")), "Following LINE XData changed")
    check(saved.appids.get("QA_ERASURE").dxf.handle == fixture["kept_handles"]["appid"], "Live shared APPID identity changed")
    fresh = root["QA_FRESH"]
    check(fresh.dxftype() == "XRECORD" and fresh.dxf.owner == root.dxf.handle, "Fresh object attachment is invalid")
    check(list(fresh.tags) == [(1, "fresh after erase")], "Fresh object payload differs")
    check(int(fresh.dxf.handle, 16) >= int(fixture["handle_seed"], 16) and int(fresh.dxf.handle, 16) > fixture["deleted_max"], "Fresh identity reused old allocation space")
    check(int(saved.header["$HANDSEED"], 16) > int(fresh.dxf.handle, 16), "Saved allocation seed is not above fresh identity")
    # No retained exposed pointer, common owner/reactor, or XData handle may target a deleted record.
    # Group5 identities, arbitrary320..329 and private non-handle strings/binary are not pointers.
    for record in saved_wire.values():
        for tag in record:
            if 330 <= tag.code <= 369 or 390 <= tag.code <= 399 or tag.code in (480, 481, 1005):
                check(int(tag.value, 16) not in erased, "Output retained an exposed pointer into erased subtree")
    audit = saved.audit()
    check(not audit.errors and not audit.fixes, "Independent audit changed exported graph: " + str(audit.errors + audit.fixes))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    manifest = json.loads((ROOT / "tests/fixtures/typed-erasure/manifest.json").read_text())
    check(manifest["producer"] == "ezdxf 1.4.4" and ezdxf.__version__ == "1.4.4", "Qualification requires pinned ezdxf1.4.4")
    fixtures = manifest["fixtures"]
    check(len(fixtures) == 6 and {(f["year"], f["version"], f["file"]) for f in fixtures} == {(year, version, f"independent-typed-erasure-R{year}.dxf") for year, version in PROFILES.items()}, "Fixture inventory must contain each exact profile once")
    expected = {f"typed-erasure-R{year}-{transport}.dxf" for year in PROFILES for transport in ("ascii", "binary")}
    actual = {path.name for path in args.artifacts.glob("typed-erasure-*.dxf")}
    check(actual == expected, "Missing/extra erasure artifacts: " + str(expected ^ actual))
    for fixture in fixtures:
        source = ROOT / "tests/fixtures/typed-erasure" / fixture["file"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == fixture["sha256"], "Producer source hash changed")
        original = ezdxf.readfile(source)
        check(original.dxfversion == fixture["version"], "Source profile disagrees with manifest")
        source_wire = wire_records(source)
        for handle, tags in fixture["wire_records"].items():
            check([[t.code, json_value(t.value)] for t in source_wire[int(handle, 16)]] == tags, "Independent raw manifest snapshot changed")
        erased = fixture["erased_handles"]
        dictionary = original.entitydb[erased["dictionary"]]
        check(dictionary.dxf.hard_owned == 0, "Mixed-strength source flag differs")
        check(entries(source_wire[int(dictionary.dxf.handle, 16)]) == {"PRIMARY": (360, int(erased["child"], 16)), "ALIAS": (350, int(erased["child"], 16)), "EMPTY": (360, int(erased["empty_child"], 16))}, "Producer alias patch does not match qualification")
        source_audit = original.audit()
        check(not source_audit.errors and not source_audit.fixes, "Independent source audit is not clean")
        for binary in (False, True):
            output = args.artifacts / f"typed-erasure-R{fixture['year']}-{'binary' if binary else 'ascii'}.dxf"
            compare(output, source, fixture, binary)
            print("PASS", output.name)
    print("PASS ezdxf1.4.4: 12 typed erasure outputs; five-object owned subtrees removed; retained aliases/payload/references; monotonic handles; zero audit changes")


if __name__ == "__main__":
    main()
