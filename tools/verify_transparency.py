#!/usr/bin/env python3
"""Independently qualify shipped transparency DXF and LAS output packets.

No netDxf code is imported. DXF tags/header are read by ezdxf; LAS uses a
separate complete-pair parser. Corruption controls mutate actual parsed output
and must fail the same validator that accepted that output.
"""
import argparse
from copy import deepcopy
import json
from pathlib import Path

import ezdxf
from verify_mleader_inputs import check, records

VERSIONS = {"AutoCad2000": "AC1015", "AutoCad2004": "AC1018",
            "AutoCad2007": "AC1021", "AutoCad2010": "AC1024",
            "AutoCad2013": "AC1027", "AutoCad2018": "AC1032"}
WIRE = [0x02000000, 0x02000001, 0x02000018, 0x02000080,
        0x020000FE, 0x020000FF, 0x01000000, -0x7EFFED81]
STATE = [0x020000FE, 0x020000FF, -0x7EFFED01, 0]
OPAQUE, QUARTER, BYBLOCK = 0x020000FF, 0x020000BF, 0x01000000


def values(tags, code):
    return [value for current, value in tags if current == code]


def one(tags, code):
    result = values(tags, code)
    check(len(result) == 1, f"Expected one group {code}, got {len(result)}")
    return result[0]


def application(tags):
    starts = [i for i, tag in enumerate(tags) if tag == [1001, "AcCmTransparency"]]
    check(len(starts) <= 1, "Repeated AcCmTransparency application")
    if not starts:
        return None
    start = starts[0] + 1
    end = next((i for i in range(start, len(tags)) if tags[i][0] == 1001), len(tags))
    return tags[start:end]


def layers(data):
    result = {}
    for handle, tags in data.items():
        if tags[0] == [0, "LAYER"]:
            name = one(tags, 2)
            check(name not in result, "Duplicate layer name")
            result[name] = (handle, tags)
    return result


def state_record(data):
    matches = [tags for tags in data.values() if tags[0] == [0, "XRECORD"]
               and [100, "AcDbXrecord"] in tags and [91, 2047] in tags and values(tags, 302)]
    check(len(matches) == 1, "Expected one layer-state XRECORD")
    return matches[0]


def state_values(data):
    layer_handles = {handle: name for name, (handle, _) in layers(data).items()}
    tags = state_record(data)
    start = tags.index([100, "AcDbXrecord"]) + 1
    result, current, packet = {}, None, []
    for code, value in tags[start:] + [[330, None]]:
        if code == 330:
            if current is not None:
                check(current not in result, "Repeated state layer")
                result[current] = one(packet, 440)
            if value is None:
                current = None
            else:
                check(value in layer_handles, "State target is not a layer")
                current = layer_handles[value]
            packet = []
        elif current is not None:
            packet.append([code, value])
    check(result, "Layer state lost its layer packets")
    return result


def alpha_layer(data, name, expected):
    found = layers(data)
    check(name in found, "Missing layer " + name)
    check(application(found[name][1]) == [[1071, expected]], name + ": packed alpha/presence changed")


def validate_wire(data, kind, phase):
    if kind == "LAYER":
        found = layers(data)
        names = {"PACKED_" + str(i) for i in range(8)} | {"PACKED_COPY_" + str(i) for i in range(8)}
        check({name for name in found if name.startswith("PACKED_")} == names, "Packed layer inventory changed")
        for i, packed in enumerate(WIRE):
            for name in ("PACKED_" + str(i), "PACKED_COPY_" + str(i)):
                alpha_layer(data, name, OPAQUE if phase == "edited" else packed)
    else:
        found = [tags for tags in data.values() if tags[0] == [0, kind]]
        check(len(found) == 16, "Packed entity/clone inventory changed")
        groups = {i: [] for i in range(8)}
        for tags in found:
            point = one(tags, 10)
            check(len(point) == 3 and point[1:] == [0, 0] and point[0] in groups,
                  "Packed entity position changed")
            index = int(point[0])
            if kind == "MTEXT":
                check(one(tags, 1) == "packed " + str(index), "MTEXT identity/text changed")
            else:
                check(one(tags, 11) == [index, 1, 0], "LINE endpoint changed")
            groups[index].append(one(tags, 440))
        for i, group in groups.items():
            check(group == [OPAQUE if phase == "edited" else WIRE[i]] * 2,
                  f"Entity {i}/clone packed alpha or presence changed")


def ancillary_packet(slots, phase):
    packet = [[1000, "private-before"], [1004, {"hex": "0300ff"}]]
    if slots == 2:
        packet.append([1071, 0x02000080])
    if slots:
        packet.append([1071, OPAQUE])
    packet += [[1040, 1.25], [1000, "private-after"]]
    if phase in ("zero", "25", "assigned"):
        indices = [i for i, tag in enumerate(packet) if tag[0] == 1071]
        replacement = [1071, QUARTER if phase == "25" else OPAQUE]
        if indices:
            packet[indices[-1]] = replacement
        else:
            packet.append(replacement)
    return packet


def validate_ancillary(data, slots, phase):
    found = layers(data)
    expected_names = {"PRIVATE_ALPHA"} | ({"PRIVATE_ALPHA_COPY"} if phase != "untouched" else set())
    check({name for name in found if name.startswith("PRIVATE_ALPHA")} == expected_names,
          "Ancillary layer/clone inventory changed")
    check(application(found["PRIVATE_ALPHA"][1]) == ancillary_packet(slots, phase),
          "Ancillary packet order, values, last-slot edit, or absent slot changed")
    if phase != "untouched":
        check(application(found["PRIVATE_ALPHA_COPY"][1]) == ancillary_packet(slots, "untouched"),
              "Source edit changed clone ancillary packet")


def validate_state(data, packed, phase):
    expected = {"0": 0, "STATE_ALPHA_LAYER": BYBLOCK if phase == "captured" and packed == 0 else packed}
    if phase == "transfer":
        check(packed == 0, "Transfer fixture requires stored numeric zero")
        expected["CAPTURE_ALPHA"] = BYBLOCK
        alpha_layer(data, "CAPTURE_ALPHA", 0)
        alpha_layer(data, "TRANSFER_ALPHA", OPAQUE)
    check(state_values(data) == expected, "State exact alpha/presence or carrier-transfer meaning changed")
    alpha_layer(data, "STATE_ALPHA_LAYER", packed)


def validate_authored(data):
    found = layers(data)
    check("DEFAULT_ALPHA" in found, "Missing default authored layer")
    check(application(found["DEFAULT_ALPHA"][1]) is None, "Default layer acquired alpha XData")
    check(state_values(data) == {"0": 0, "DEFAULT_ALPHA": 0}, "Authored opaque state encoding changed")


def parse_las(text):
    lines = text.splitlines()
    check(len(lines) % 2 == 0 and len(lines) >= 4, "LAS ends with an incomplete code/value pair")
    tags = []
    for i in range(0, len(lines), 2):
        code = int(lines[i].strip())
        value = lines[i + 1]
        if code in (62, 90, 91, 92, 290, 370, 440):
            value = int(value.strip())
        tags.append([code, value])
    return tags


def validate_las(tags, packed):
    check(tags[:2] == [[0, "LAYERSTATEDICTIONARY"], [0, "LAYERSTATE"]], "LAS record envelope changed")
    check(one(tags, 1) == "STATE_ALPHA", "LAS state identity changed")
    result, current, packet = {}, None, []
    for code, value in tags[2:] + [[8, None]]:
        if code == 8:
            if current is not None:
                check(current not in result, "Repeated LAS layer")
                result[current] = one(packet, 440)
            current, packet = value, []
        elif current is not None:
            packet.append([code, value])
    check(result == {"0": 0, "STATE_ALPHA_LAYER": packed}, "LAS packed alpha/presence changed")


def rejected(validator, mutated, label):
    try:
        validator(mutated)
    except (ValueError, KeyError, IndexError, TypeError):
        return
    raise ValueError("Corruption control was accepted: " + label)


def mutate_code(data, code, operation, predicate=None):
    changed = deepcopy(data)
    candidates = changed.values() if isinstance(changed, dict) else [changed]
    for tags in candidates:
        if predicate is not None and not predicate(tags):
            continue
        for index, tag in enumerate(tags):
            if tag[0] == code:
                operation(tags, index)
                return changed
    raise ValueError("Corruption control could not locate group " + str(code))


def alter(tags, index):
    value = tags[index][1]
    if isinstance(value, dict) and "hex" in value:
        binary = bytearray.fromhex(value["hex"])
        binary[0] ^= 1
        tags[index][1] = {"hex": binary.hex()}
    else:
        tags[index][1] = (value ^ 1) if isinstance(value, int) else "CORRUPTED"


def erase(tags, index):
    del tags[index]


def duplicate(tags, index):
    tags.insert(index, deepcopy(tags[index]))


def qualify_dxf(path, version, binary, validator, controls):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "DXF transport differs from artifact name")
    check(ezdxf.readfile(path).dxfversion == VERSIONS[version], "DXF source profile changed")
    data = records(path)
    validator(data)
    for code, operation, predicate, label in controls:
        rejected(validator, mutate_code(data, code, operation, predicate), label)
    count = len(controls)
    # Exercise every stored scalar independently: changing only the first state
    # layer or original entity would not prove that the nondefault/clone slots
    # participate in the comparison. This also covers both duplicate XData slots.
    for handle, tags in data.items():
        for index, (code, _) in enumerate(tags):
            if code not in (440, 1071):
                continue
            mutated = deepcopy(data)
            alter(mutated[handle], index)
            rejected(validator, mutated, f"individual alpha slot {handle}:{index}")
            count += 1
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    expected, files, controls, failures = set(), [], 0, []

    def dxf(name, version, binary, validator, mutations):
        nonlocal controls
        expected.add(name)
        try:
            controls += qualify_dxf(args.artifacts / name, version, binary, validator, mutations)
            files.append(name)
        except Exception as error:
            failures.append({"file": name, "error": str(error)})

    layer = lambda tags: tags[0] == [0, "LAYER"] and any(code == 2 and str(value).startswith("PACKED_") for code, value in tags)
    private = lambda tags: tags[0] == [0, "LAYER"] and [2, "PRIVATE_ALPHA"] in tags
    state = lambda tags: tags[0] == [0, "XRECORD"] and [100, "AcDbXrecord"] in tags
    for version in VERSIONS:
        for binary in (False, True):
            for kind in ("LINE", "MTEXT", "LAYER"):
                for phase in ("authored", "reloaded", "edited"):
                    code = 1071 if kind == "LAYER" else 440
                    pred = layer if kind == "LAYER" else lambda tags, k=kind: tags[0] == [0, k]
                    dxf(f"transparency-wire-{kind}-{version}-{binary}-{phase}.dxf", version, binary,
                        lambda data, k=kind, p=phase: validate_wire(data, k, p),
                        [(code, operation, pred, "wire " + label) for operation, label in ((alter, "value"), (erase, "presence"), (duplicate, "duplicate"))])
            for slots in (0, 1, 2):
                for phase in ("untouched", "cloned", "zero", "25", "assigned"):
                    mutations = [(1000, alter, private, "private text"), (1004, alter, private, "private binary"),
                                 (1040, erase, private, "ancillary field presence")]
                    if slots or phase in ("zero", "25", "assigned"):
                        mutations += [(1071, alter, private, "packed/earlier slot"), (1071, erase, private, "slot presence")]
                    else:
                        mutations.append((1000, lambda tags, i: tags.append([1071, OPAQUE]), private, "invented missing slot"))
                    dxf(f"transparency-ancillary-{slots}-{version}-{binary}-{phase}.dxf", version, binary,
                        lambda data, s=slots, p=phase: validate_ancillary(data, s, p), mutations)
            for packed in STATE:
                phases = ("captured", "input", "preserved", "transfer") if packed == 0 else ("captured", "input", "preserved")
                for phase in phases:
                    mutations = [(440, operation, state, "state " + label) for operation, label in ((alter, "value"), (erase, "presence"), (duplicate, "duplicate"))]
                    if phase == "transfer":
                        for name in ("TRANSFER_ALPHA", "CAPTURE_ALPHA"):
                            mutations.append((1071, alter, lambda tags, n=name: [2, n] in tags, "transfer layer " + name))
                    dxf(f"transparency-state-{packed & 0xffffffff:08X}-{version}-{binary}-{phase}.dxf", version, binary,
                        lambda data, p=packed, stage=phase: validate_state(data, p, stage), mutations)
                for phase in ("source", "resaved"):
                    name = f"transparency-las-{packed & 0xffffffff:08X}-{version}-{binary}-{phase}.las"
                    expected.add(name)
                    try:
                        content = (args.artifacts / name).read_text(encoding="utf-8-sig")
                        tags = parse_las(content)
                        validator = lambda data, p=packed: validate_las(data, p)
                        validator(tags)
                        for operation in (alter, erase, duplicate):
                            rejected(validator, mutate_code(tags, 440, operation), "LAS scalar/presence")
                            controls += 1
                        for index, (code, _) in enumerate(tags):
                            if code == 440:
                                mutated = deepcopy(tags)
                                alter(mutated, index)
                                rejected(validator, mutated, "individual LAS layer alpha")
                                controls += 1
                        rejected(parse_las, "\n".join(content.splitlines()[:-1]), "LAS dangling value")
                        controls += 1
                        files.append(name)
                    except Exception as error:
                        failures.append({"file": name, "error": str(error)})
            dxf(f"transparency-authored-{version}-{binary}.dxf", version, binary, validate_authored,
                [(440, alter, state, "authored state value"), (440, erase, state, "authored state presence"),
                 (2, lambda tags, i: tags.extend([[1001, "AcCmTransparency"], [1071, OPAQUE]]),
                  lambda tags: tags[0] == [0, "LAYER"] and [2, "DEFAULT_ALPHA"] in tags, "invented default layer packet")])

    actual = {path.name for pattern in ("transparency-*.dxf", "transparency-*.las") for path in args.artifacts.glob(pattern)}
    if actual != expected:
        failures.append({"inventory": {"missing": sorted(expected - actual), "unexpected": sorted(actual - expected)}})
    result = {"schema": 1, "parser": "ezdxf " + ezdxf.__version__ + " raw DXF tags; independent complete-pair LAS parser",
              "expected_files": len(expected), "checked_files": len(files),
              "checked_dxf": sum(name.endswith(".dxf") for name in files),
              "checked_las": sum(name.endswith(".las") for name in files),
              "corruption_controls": controls, "failures": failures}
    if args.output:
        args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
