#!/usr/bin/env python3
"""Reproduce independent mixed-module gate review using real DXF byte mutations.

The additional controls rewrite the ASCII output bytes before parsing. The prior
gate argument is optional and records the demonstrated coverage gap, not a
requirement for qualifying the corrected gate. This is stored-packet evidence;
it does not claim native AutoCAD execution or evaluation of the mixed drawing.
"""
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


def module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo", type=Path)
    parser.add_argument("artifacts", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--prior-gate", type=Path)
    parser.add_argument("--runtime-proof", type=Path)
    args = parser.parse_args()
    args.repo = args.repo.resolve()
    args.artifacts = args.artifacts.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    tools = args.repo / "tools"
    sys.path.insert(0, str(tools))
    gate_path = tools / "verify_eighth_mixed.py"
    gate = module(gate_path, "current_mixed_gate")
    prior = module(args.prior_gate, "prior_mixed_gate") if args.prior_gate else None
    gate_run = subprocess.run([sys.executable, str(gate_path), str(args.artifacts)],
                              check=True, text=True, capture_output=True)
    (args.output / "corrected-gate.log").write_text(gate_run.stdout)
    source_path = args.repo / "tests/fixtures/sunstudy-producer/typed-carriers/ixmilia-sunstudy-R2018-ascii-no-dates-hours.dxf"
    source = gate.one(gate.records(source_path.read_bytes()), "SUNSTUDY")
    base_path = args.artifacts / "eighth-mixed-AutoCad2018-False.dxf"
    before_path = args.artifacts / "eighth-mixed-AutoCad2018-False-input.dxf"
    before = gate.records(before_path.read_bytes())
    original = base_path.read_bytes()
    assert not original.startswith(b"AutoCAD Binary DXF")
    lines = original.decode("utf-8-sig").splitlines(keepends=True)
    assert len(lines) % 2 == 0
    pairs = [(int(lines[i].strip()), lines[i + 1].strip()) for i in range(0, len(lines), 2)]
    start = next(i for i, pair in enumerate(pairs) if pair == (0, "HATCH"))
    end = next(i for i in range(start + 1, len(pairs)) if pairs[i][0] == 0)

    def at(code, last=False):
        positions = [i for i in range(start, end) if pairs[i][0] == code]
        assert positions, code
        return positions[-1] if last else positions[0]

    faults = {
        "plane-normal": {at(210): "0", at(220): "1", at(230): "0"},
        "elevation": {at(30): "10"},
        "seed-point": {at(10, last=True): "999"},
        "knot-count": {at(95): "999"},
        "control-count": {at(96): "999"},
        "seed-count": {at(98): "999"},
    }
    controls = []
    for name, edits in faults.items():
        changed = list(lines)
        for position, value in edits.items():
            old = changed[position * 2 + 1]
            suffix = "\r\n" if old.endswith("\r\n") else "\n"
            changed[position * 2 + 1] = value + suffix
        path = args.output / ("mixed-extra-" + name + ".dxf")
        data = "".join(changed).encode("utf-8")
        assert data != original
        path.write_bytes(data)
        wire = gate.records(data)
        try:
            gate.validate(wire, source, before)
        except ValueError as error:
            current_result = str(error)
        else:
            raise AssertionError("Actual byte mutation escaped corrected gate: " + name)
        prior_result = None
        if prior:
            try:
                prior.validate(wire, source, before)
            except ValueError as error:
                prior_result = "rejected: " + str(error)
            else:
                prior_result = "accepted"
        try:
            audit = ezdxf.readfile(path).audit()
            audit_result = {"errors": len(audit.errors), "repairs": len(audit.fixes)}
        except Exception as error:
            audit_result = {"rejected": type(error).__name__ + ": " + str(error)}
        controls.append({"fault": name, "file": path.name, "sha256": sha(path),
                         "corrected_gate_rejection": current_result,
                         "prior_gate": prior_result, "ezdxf_audit": audit_result})
    result = {
        "review": "Independent PR92 eighth mixed-module gate qualification",
        "runtime_source_checkpoint": "5190451",
        "runtime": json.loads(args.runtime_proof.read_text()) if args.runtime_proof else None,
        "focused_conformance": json.loads((args.artifacts / "results.json").read_text()),
        "corrected_module_gate": json.loads(gate_run.stdout),
        "additional_actual_wire_controls_rejected": len(controls),
        "controls": controls,
        "harness_sha256": sha(Path(__file__)),
        "corrected_gate_sha256": sha(gate_path),
        "prior_gate_sha256": sha(args.prior_gate) if args.prior_gate else None,
        "fixture_sha256": sha(source_path),
        "artifacts_sha256": {p.name: sha(p) for p in sorted(args.artifacts.glob("eighth-mixed-*.dxf"))},
        "scope": "Four stored-packet outputs and four pre-edit snapshots; authored mixed links; exact retained identity and payload assertions. Native CAD executables were unavailable and no native runtime qualification is claimed.",
    }
    (args.output / "mixed-review-summary.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({"module_gate": result["corrected_module_gate"], "actual_wire_controls_rejected": len(controls),
                      "prior_gate_acceptances": sum(c["prior_gate"] == "accepted" for c in controls)}))


if __name__ == "__main__":
    main()
