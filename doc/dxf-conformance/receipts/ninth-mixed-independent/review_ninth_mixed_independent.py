#!/usr/bin/env python3
"""Exercise mixed9 identity continuity with coherent mutations of actual DXF bytes."""
import argparse
import hashlib
import importlib.util
import json
import subprocess
import sys
from pathlib import Path

import ezdxf


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pairs(path):
    rows = path.read_text(encoding="utf-8-sig").splitlines()
    assert len(rows) % 2 == 0
    return [(int(rows[i]), rows[i + 1]) for i in range(0, len(rows), 2)]


def chunks(tags):
    starts = [i for i, (code, _) in enumerate(tags) if code == 0]
    return [(start, starts[i + 1] if i + 1 < len(starts) else len(tags)) for i, start in enumerate(starts)]


def rewrite(tags, mapping):
    def is_handle(code):
        return code in (5, 105, 480, 481, 1005) or 320 <= code <= 369 or 390 <= code <= 399
    return [(code, mapping.get(value.strip().upper(), value) if is_handle(code) else value) for code, value in tags]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo", type=Path)
    parser.add_argument("artifacts", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.repo = args.repo.resolve(); args.artifacts = args.artifacts.resolve(); args.output.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(args.repo / "tools"))
    gate_path = args.repo / "tools/verify_ninth_mixed.py"
    spec = importlib.util.spec_from_file_location("mixed9_gate", gate_path); gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
    run = subprocess.run([sys.executable, str(gate_path), str(args.artifacts)], capture_output=True, text=True, check=True)
    (args.output / "module-gate.log").write_text(run.stdout)
    filename = "acad_table_simple.dxf"; prefix = "ninth-mixed-" + filename + "-False-"
    input_path = args.artifacts / (prefix + "input.dxf"); edited_path = args.artifacts / (prefix + "edited.dxf"); released_path = args.artifacts / (prefix + "released.dxf")
    original, _, year = gate.source(args.repo, filename)
    _, before, _ = gate.load(input_path, year, False)
    _, edited, _ = gate.load(edited_path, year, False)
    manager = gate.unique(edited, "SECTION_MANAGER")[0]
    second, section = gate.unique(edited, "SECTIONOBJECT", lambda row: (1, "Ninth second") in row)
    settings = gate.field(gate.public(section, "AcDbSection"), 360)
    source_tags = pairs(edited_path)
    variants = [("manager-identity", "edited", rewrite(source_tags, {manager.upper(): "FFF00001"})),
                ("section-identity", "edited", rewrite(source_tags, {second.upper(): "FFF00002"})),
                ("settings-identity", "edited", rewrite(source_tags, {settings.upper(): "FFF00003"}))]
    input_tags = pairs(input_path); spans = chunks(input_tags)
    start = next(start for start, end in spans if input_tags[start] == (0, "POLYLINE") and (8, "NINTH_GRAPH") in input_tags[start:end])
    terminal_start, terminal_end = next((a, b) for a, b in spans if a > start and input_tags[a] == (0, "SEQEND"))
    mesh_tags = input_tags[start:terminal_end]
    handles = [value.strip().upper() for code, value in mesh_tags if code == 5]
    renamed = rewrite(mesh_tags, {value: format(0xFFF01000 + i, "X") for i, value in enumerate(handles)})
    released_tags = pairs(released_path)
    entities_start = next(i for i, pair in enumerate(released_tags) if pair == (2, "ENTITIES"))
    entities_end = next(i for i in range(entities_start + 1, len(released_tags)) if released_tags[i] == (0, "ENDSEC"))
    variants.append(("replacement-mesh", "released", released_tags[:entities_end] + renamed + released_tags[entities_end:]))
    controls = []
    for name, phase, tags in variants:
        seed = next(i for i, pair in enumerate(tags) if pair == (9, "$HANDSEED")) + 1
        assert tags[seed][0] == 5
        tags[seed] = (5, "FFF02000")
        path = args.output / (name + ".dxf")
        path.write_bytes("".join(str(code) + "\n" + str(value) + "\n" for code, value in tags).encode("utf-8"))
        drawing, records, classes = gate.load(path, year, False)
        try:
            gate.validate(records, classes, original, before, phase)
        except (ValueError, KeyError, StopIteration) as error:
            rejection = type(error).__name__ + ": " + str(error)
        else:
            raise AssertionError("Coherent actual identity mutation escaped: " + name)
        audit = drawing.audit()
        controls.append({"mutation": name, "file": path.name, "sha256": sha(path), "rejected": rejection, "audit_errors": len(audit.errors), "audit_repairs": len(audit.fixes)})
    result = {"module_gate": json.loads(run.stdout), "additional_actual_wire_identity_controls": len(controls), "controls": controls,
              "harness_sha256": sha(Path(__file__)), "gate_sha256": sha(gate_path),
              "outputs_sha256": {p.name: sha(p) for p in sorted(args.artifacts.glob("ninth-mixed-*.dxf"))},
              "scope": "Coherent handle remapping and extra replacement mesh, applied to actual ASCII output bytes. Native CAD application execution is not claimed."}
    (args.output / "ninth-review-summary.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({"module_gate": result["module_gate"], "additional_actual_wire_identity_controls": len(controls)}))


if __name__ == "__main__":
    main()
