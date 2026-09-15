"""Independently mutate real mixed10 ASCII output and require precise wire rejection."""
import argparse
import hashlib
import importlib
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("repository", type=Path)
parser.add_argument("artifacts", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
sys.path.insert(0, str(args.repository.resolve() / "tools"))
gate = importlib.import_module("verify_tenth_mixed")
args.output.mkdir(parents=True, exist_ok=True)
name = "tenth-mixed-acad_table_with_blk_ref.dxf-False-False-"
initial = args.artifacts / (name + "input.dxf")
linked = args.artifacts / (name + "linked.dxf")
original, _, year = gate.source(args.repository, "acad_table_with_blk_ref.dxf")
_, before, _ = gate.load(initial, year, False)
doc, records, classes = gate.load(linked, year, False)
gate.validate(records, classes, original, before, "linked", year)
audit = doc.audit()
assert not audit.errors and not audit.fixes
mesh, _, children, face = gate.mesh_packet(records)
opaque, _ = gate.unique(records, gate.OPAQUE)
hatch, _ = gate.unique(records, "HATCH", lambda row: (8, "TENTH_PATTERN") in row)
section = gate.section_inventory(records)["Tenth second"][0]
geometry, _ = gate.unique(records, "TABLEGEOMETRY")
content, _ = gate.unique(records, "TABLECONTENT")
pairs = gate.ascii_pairs(linked.read_bytes())
results = []
identities = {"hatch-identity": hatch, "mesh-identity": mesh,
              "face-identity": face[0], "coordinate-identity": children[1][0],
              "opaque-identity": opaque}
for ordinal, kind in enumerate([*identities, "hatch-elevation-x", "geometry-target", "content-display"]):
    changed = list(pairs)
    if kind in identities:
        previous = identities[kind]
        replacement = f"3FFF1{ordinal:03X}"
        changed = [(code, replacement if value == previous and gate.qualified_handle_code(code) else value)
                   for code, value in changed]
        for index, tag in enumerate(changed):
            if tag == (9, "$HANDSEED"):
                assert changed[index + 1][0] == 5
                changed[index + 1] = (5, "3FFF2000")
    else:
        target, code = ((hatch, 10) if kind == "hatch-elevation-x" else
                        (geometry, 330) if kind == "geometry-target" else (content, 302))
        dxftype = "HATCH" if kind == "hatch-elevation-x" else "TABLEGEOMETRY" if kind == "geometry-target" else "TABLECONTENT"
        start, end, row = gate.ascii_row(changed, dxftype, target)
        if kind == "geometry-target":
            body = row.index((100, "AcDbTableGeometry"))
            index = next(i for i in range(body + 1, len(row)) if row[i][0] == 330)
            row[index] = (330, section)
        elif kind == "content-display":
            index = next(i for i in range(len(row) - 1) if row[i][0] == 302 and row[i + 1] == (304, "ACVALUE_END"))
            row[index] = (302, "UNREQUESTED DISPLAY")
        else:
            index = next(i for i, tag in enumerate(row) if tag[0] == code)
            row[index] = (10, "0.125")
        changed[start:end] = row
    path = args.output / (kind + ".dxf")
    path.write_bytes("\n".join(f"{code}\n{value}" for code, value in changed).encode("utf-8") + b"\n")
    changed_doc, changed_records, changed_classes = gate.load(path, year, False)
    changed_audit = changed_doc.audit()
    assert not changed_audit.errors and not changed_audit.fixes, f"Control invalidated ordinary DXF structure: {kind}"
    try:
        gate.validate(changed_records, changed_classes, original, before, "linked", year)
    except (ValueError, KeyError, StopIteration) as error:
        reason = str(error)
    else:
        raise AssertionError(f"Actual ASCII mutation escaped the gate: {kind}")
    results.append({"control": kind, "file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "audit_errors": len(changed_audit.errors), "audit_repairs": len(changed_audit.fixes),
                    "rejection": reason})
    print("PASS", kind, reason)
summary = {"controls": len(results), "all_rejected": True,
           "gate_sha256": hashlib.sha256((args.repository / "tools/verify_tenth_mixed.py").read_bytes()).hexdigest(),
           "input_sha256": hashlib.sha256(initial.read_bytes()).hexdigest(),
           "linked_sha256": hashlib.sha256(linked.read_bytes()).hexdigest(), "results": results}
(args.output / "actual-wire-controls.json").write_text(json.dumps(summary, indent=2) + "\n")
