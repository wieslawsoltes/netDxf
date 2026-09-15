#!/usr/bin/env python3
"""Check opaque HATCH backlink release without interpreting unknown geometry.

The source names and private controls are declared adaptations of pinned HATCH
carriers. They are not native unknown-entity evidence or application execution.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import ezdxf
from verify_hatch_source_relations import BINARY_SIGNATURE, FIXTURES, VERSIONS, check, owner, paths, wire_records

SCENARIOS = ("retained", "unlink", "paths", "create-unlinked", "create-linked", "transform", "clone", "remove-hatch",
             "dirty-unlink", "dirty-clear", "dirty-remove", "update", "readd", "private-pointer")


def qualified_reactors(packet):
    result = set()
    inside = False
    for index, (code, value) in enumerate(packet):
        if code == 100:
            break
        if code == 102:
            inside = value == "{ACAD_REACTORS"
        elif inside and code == 330:
            result.add(index)
    return result


def validate(wire, original, target, first_hatch, scenario):
    if scenario in ("paths", "readd"):
        check(target not in wire, "Final path release must retire the unreferenced source")
        return
    check(target in wire, "Surviving source was discarded")
    packet = wire[target]
    live_hatches = set()
    for handle, tags in wire.items():
        if tags[0] != (0, "HATCH"):
            continue
        for boundary in paths(tags):
            if target in boundary["sources"]:
                check(owner(tags) == owner(packet), "Source/HATCH owner mismatch")
                live_hatches.add(handle)
    expected = list(original)
    expected[0] = (0, "FUTURE_BOUNDARY_CURVE")
    at = next((i for i, tag in enumerate(expected) if tag[0] == 1001), len(expected))
    expected[at:at] = [(102, "{PRIVATE_RELEASE"), (330, first_hatch), (102, "}")]
    if scenario == "private-pointer":
        expected.insert(at, (340, first_hatch))
    qualified = qualified_reactors(expected)
    expected = [tag for i, tag in enumerate(expected) if i not in qualified or tag[1] in live_hatches]
    check(packet == expected, "Packet changed beyond the exact released common HATCH backlinks")
    actual = {packet[i][1] for i in qualified_reactors(packet)}
    check(actual == live_hatches, "Physical HATCH backlinks disagree with surviving source associations")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    check(ezdxf.__version__ == "1.4.4", "The independent reader must be pinned to ezdxf1.4.4")
    manifest = json.loads((FIXTURES / "manifest.json").read_text())
    pinned = {item["file"]: item["sha256"] for item in manifest["fixtures"]}
    outputs = []
    controls = None
    for year in VERSIONS:
        for binary in (False, True):
            for kind in ("native", "producer"):
                filename = f'{kind}-R{year}-{"binary" if binary else "ascii"}.dxf'
                data = (FIXTURES / filename).read_bytes()
                check(hashlib.sha256(data).hexdigest() == pinned[filename], "Pinned carrier changed")
                source = wire_records(data)
                target = "8E" if kind == "native" else "3A0"
                first_hatch = next(handle for handle, packet in source.items() if packet[0] == (0, "HATCH"))
                for scenario in SCENARIOS:
                    for written in (False, True):
                        name = f"opaque-hatch-{kind}-AutoCad{year}-{binary}-{scenario}-final-{written}.dxf"
                        file = args.artifacts / name
                        check(file.is_file(), "Missing mandatory output: " + name)
                        payload = file.read_bytes()
                        check(payload.startswith(BINARY_SIGNATURE) == written, "Wrong physical transport")
                        wire = wire_records(payload)
                        validate(wire, source[target], target, first_hatch, scenario)
                        doc = ezdxf.readfile(file)
                        check(doc.dxfversion == VERSIONS[year], "Source profile changed")
                        audit = doc.audit()
                        check(not audit.errors and not audit.fixes, "Independent audit reported errors or repairs")
                        if target in wire:
                            check(type(doc.entitydb[target]).__name__ == "DXFTagStorage", "Unknown geometry unexpectedly interpreted")
                        outputs.append({"file": name, "sha256": hashlib.sha256(payload).hexdigest()})
                        if kind == "producer" and year == 2018 and not binary and not written and scenario == "private-pointer":
                            controls = wire, source[target], target, first_hatch, scenario
    wire, original, target, first_hatch, scenario = controls
    rejected = []
    for defect in ("private-pointer", "private-group", "owner", "released-backlink"):
        damaged = copy.deepcopy(wire)
        packet = damaged[target]
        if defect == "private-pointer":
            packet.remove((340, first_hatch))
        elif defect == "private-group":
            packet[packet.index((102, "{PRIVATE_RELEASE")) + 1] = (330, "FFFF")
        elif defect == "owner":
            packet[packet.index((330, owner(packet)))] = (330, "FFFF")
        else:
            packet.insert(packet.index((102, "{ACAD_REACTORS")) + 1, (330, first_hatch))
        try:
            validate(damaged, original, target, first_hatch, scenario)
        except ValueError:
            rejected.append(defect)
    check(len(outputs) == 672 and len(rejected) == 4, "Mandatory output or corruption-control inventory differs")
    print(json.dumps({"reader": "ezdxf1.4.4", "outputs": len(outputs), "negative_controls": rejected, "audit_errors": 0,
                      "audit_repairs": 0, "unknown_geometry_interpreted": False, "native_unknown_evidence": False,
                      "output_sha256": outputs}, sort_keys=True))


if __name__ == "__main__":
    main()
