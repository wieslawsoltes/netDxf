#!/usr/bin/env python3
"""Independently verify native and public-schema stored section-manager packets.

The native evidence is one unchanged R2018 drawing. Other versions, empty lists,
duplicate references and documented spelling are explicit synthetic schema cases.
"""
import argparse
import copy
import gzip
import hashlib
import json
from pathlib import Path
import tempfile

from verify_fourth_mixed_modules import load, metadata, dictionary_edges

YEARS = {"AutoCad2007": 2007, "AutoCad2010": 2010, "AutoCad2013": 2013, "AutoCad2018": 2018}
SPELLINGS = ("SECTION_MANAGER", "SECTIONMANAGER")


def check(condition, message):
    if not condition:
        raise ValueError(message)


def manager(records):
    values = [(handle, row) for handle, row in records.items() if row[0][1] in SPELLINGS]
    check(len(values) == 1, "Exactly one stored manager is required")
    return values[0]


def packet(row):
    start = next(i for i, tag in enumerate(row) if tag[0] == 100)
    return row[start:]


def validate(records, classes, spelling, count, native=None):
    handle, row = manager(records)
    check(row[0] == (0, spelling), "Manager source spelling differs")
    body = packet(row)
    check(len(body) == 3 + count, "Manager packet length differs")
    flag = 0 if native is not None or count == 0 else 1
    check(body[:3] == [(100, "AcDbSectionManager"), (70, flag), (90, count)], "Stored update flag or section count differs")
    check(all(code == 330 for code, _ in body[3:]), "Manager section-pointer framing differs")
    owner, reactors, _ = metadata(row)
    check(owner in records and records[owner][0] == (0, "DICTIONARY"), "Manager dictionary owner is absent")
    check(("ACAD_SECTION_MANAGER", 350, handle) in dictionary_edges(records[owner]), "Manager original dictionary entry differs")
    check(metadata(records[owner])[0] in (None, "0"), "Manager owner is not the root dictionary")
    targets = [target for _, target in body[3:]]
    for target in targets:
        check(target in records and records[target][0] == (0, "SECTIONOBJECT"), "Manager target is not its physical section entity")
    if native is not None:
        check(handle == "229" and row == native[handle], "Complete native manager packet differs")
        check(targets == ["228"] and reactors == ["C"], "Native section reference or reactor differs")
        check(next(value for code, value in packet(records["228"]) if code == 1) == "Section Plane (1)", "Native section identity/name differs")
        check(metadata(records["22A"])[0] == "228", "Native section settings owner differs")
    elif count:
        check(targets[0] == targets[2] and targets[0] != targets[1], "Repeated section identities were changed")
        names = [next(value for code, value in packet(records[target]) if code == 1) for target in targets]
        check(names == ["Manager first", "Manager second", "Manager first"], "Source section names differ")
    definitions = [row for row in classes if (1, spelling) in row]
    check(len(definitions) == 1, "Manager CLASS inventory differs")
    for tag in [(2, "AcDbSectionManager"), (3, "ObjectDBX Classes"), (90, 1024), (91, 1), (280, 0), (281, 0)]:
        check(tag in definitions[0], "Manager CLASS metadata differs: " + repr(tag))


def validate_opaque(records, variant):
    handle, row = manager(records)
    start = next(i for i, tag in enumerate(row) if tag[0] == 100)
    body = packet(row)
    prefix = [tag for tag in row[:start] if tag[0] not in (0, 5, 330)]
    check(prefix == ([(300, "private common value")] if variant == 2 else []), "Opaque manager common packet differs")
    count = 0 if variant == 5 else 3
    flag = 2 if variant == 4 else 0 if count == 0 else 1
    check(body[:3] == [(100, "PrivateSectionManager" if variant == 0 else "AcDbSectionManager"), (70, flag), (90, count)], "Opaque public-looking prefix differs")
    refs = body[3:3 + count]
    check(all(code == 330 and target in records and records[target][0] == (0, "SECTIONOBJECT") for code, target in refs), "Opaque exposed manager references differ")
    if refs:
        check(refs[0] == refs[2] and refs[0] != refs[1], "Opaque repeated reference order differs")
    suffixes = {
        1: [(100, "PrivateManagerTail")],
        3: [(300, "private payload")],
        6: [(102, "{PRIVATE"), (1000, "private data"), (102, "}")],
        7: [(100, "PrivateManagerTail"), (340, "EEEEEEEE")],
    }
    check(body[3 + count:] == suffixes.get(variant, []), "Whole opaque manager suffix differs")
    owner = next(value for code, value in row[:start] if code == 330)
    check(("ACAD_SECTION_MANAGER", 350, handle) in dictionary_edges(records[owner]), "Opaque manager dictionary entry differs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    fixture = root / "tests/fixtures/section"
    manifest = json.loads((fixture / "manifest.json").read_text())
    packed = (fixture / "LiveSection1.dxf.gz").read_bytes()
    check(hashlib.sha256(packed).hexdigest() == manifest["gzip_sha256"], "Native compressed source hash differs")
    original = gzip.decompress(packed)
    check(hashlib.sha256(original).hexdigest() == manifest["source_sha256"], "Native source hash differs")
    check(manifest["transformations"] == [], "Native source transformations are not allowed")
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary) / "native.dxf"
        path.write_bytes(original)
        _, native, _ = load(path, 2018, False)
    native_cases = [(f"section-manager-native-{input_binary}-{binary}.dxf", 2018, binary, "SECTION_MANAGER", 1, native)
                    for input_binary in (False, True) for binary in (False, True)]
    schema_cases = [(f"section-manager-schema-{version}-{spelling}-{count}-{binary}.dxf", year, binary, spelling, count, None)
                    for version, year in YEARS.items() for spelling in SPELLINGS for count in (0, 3) for binary in (False, True)]
    opaque_cases = [(f"section-manager-opaque-{variant}-{binary}.dxf", variant, binary)
                    for variant in range(8) for binary in (False, True)]
    expected = {row[0] for row in native_cases + schema_cases + opaque_cases}
    check({path.name for path in args.directory.glob("section-manager-*.dxf")} == expected, "All 52 section-manager outputs are mandatory")
    controls = 0
    for name, year, binary, spelling, count, source in native_cases + schema_cases:
        doc, records, classes = load(args.directory / name, year, binary)
        audit = doc.audit()
        check(not audit.errors and not audit.fixes, "Manager output required audit errors or repairs: " + name)
        validate(records, classes, spelling, count, source)
        for fault in range(4):
            bad = copy.deepcopy(records)
            bad_classes = copy.deepcopy(classes)
            handle, row = manager(bad)
            start = row.index((100, "AcDbSectionManager"))
            if fault == 0:
                row[start + 2] = (90, count + 1)
            elif fault == 1:
                owner = metadata(row)[0]
                bad[owner] = [(code, "MissingManager" if code == 3 and value == "ACAD_SECTION_MANAGER" else value) for code, value in bad[owner]]
            elif fault == 2:
                definition = next(row for row in bad_classes if (1, spelling) in row)
                definition[definition.index((90, 1024))] = (90, 0)
            elif count:
                bad[row[start + 3][1]][0] = (0, "LINE")
            else:
                row[start + 1] = (70, 1)
            try:
                validate(bad, bad_classes, spelling, count, source)
            except (ValueError, KeyError, StopIteration):
                controls += 1
            else:
                raise ValueError("Manager corruption was accepted: " + name + "/" + str(fault))
        print("PASS " + name)
    for name, variant, binary in opaque_cases:
        doc, records, _ = load(args.directory / name, 2004 if variant == 5 else 2018, binary)
        audit = doc.audit()
        check(not audit.errors and not audit.fixes, "Opaque manager output required audit repair: " + name)
        validate_opaque(records, variant)
        bad = copy.deepcopy(records)
        _, row = manager(bad)
        row.append((300, "changed private content"))
        try:
            validate_opaque(bad, variant)
        except (ValueError, KeyError, StopIteration):
            controls += 1
        else:
            raise ValueError("Opaque manager corruption was accepted")
        print("PASS " + name)
    check(controls == 160, "Section-manager corruption-control inventory differs")
    print("PASS 52 outputs, 4 exact native packets, 32 explicit schema cases, 16 opaque cases, and 160 corruption controls")


if __name__ == "__main__":
    main()
