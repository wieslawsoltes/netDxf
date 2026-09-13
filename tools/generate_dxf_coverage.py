#!/usr/bin/env python3
"""Validate the checked-in coverage ledger and generate its version/feature comparison.

Run from any directory. --check is read-only and fails when the Markdown is stale.
Only Python's standard library is used; no source, fixtures or external URLs are executed.
The validator checks documentation integrity, not whether a DXF implementation is correct.
"""
from __future__ import annotations

import argparse
from collections import OrderedDict
from datetime import date
import json
import os
from pathlib import Path
import re
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "doc/dxf-conformance/coverage.json"
OUTPUT = ROOT / "doc/dxf-conformance/version-feature-matrix.md"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def validate(data: dict[str, Any], root: Path) -> None:
    require(isinstance(data, dict), "Coverage must be a JSON object")
    require(data.get("schema_version") == 1, "Unknown coverage schema version")
    date.fromisoformat(data["audit_date"])
    source = data["source"]
    for field in ("commit", "tree"):
        require(re.fullmatch(r"[0-9a-f]{40}", source[field]) is not None, f"Invalid source {field}")
    require(source["repository"] == "wieslawsoltes/netDxf", "Unexpected coverage repository")
    profiles = data["profiles"]
    keys = [p["id"] for p in profiles]
    require(len(keys) == len(set(keys)) and len(keys) > 0, "Duplicate/empty profile catalog")
    for profile in profiles:
        require(re.fullmatch(r"AC\d{4}", profile["id"]) is not None, "Invalid admitted version identifier")
        require(isinstance(profile["raw"], bool) and isinstance(profile["typed"], bool), "Invalid admission flag")
    early = data["unadmitted_profiles"]
    early_keys = [p["id"] for p in early]
    require(len(early_keys) == len(set(early_keys)), "Duplicate unadmitted version")
    require(not (set(early_keys) & set(keys)), "Version is both admitted and unadmitted")
    definitions = data["status_definitions"]
    require(all(isinstance(v, str) and v for v in definitions.values()), "Empty status definition")
    evidence = data["evidence"]
    for key, item in evidence.items():
        path = Path(item["path"])
        require(not path.is_absolute() and ".." not in path.parts, f"Non-repository evidence path {key}")
        resolved = (root / path).resolve()
        require(root.resolve() in resolved.parents, f"Evidence escapes repository: {key}")
        require(resolved.is_file(), f"Missing evidence {key}: {path}")
        require(bool(item["label"]), f"Missing evidence label {key}")
    seen = set()
    require(bool(data["features"]), "Empty feature catalog")
    for row in data["features"]:
        id = row["id"]
        require(re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", id) is not None, f"Invalid feature id {id}")
        require(id not in seen, f"Duplicate feature id {id}")
        seen.add(id)
        require(row["pipeline"] in ("typed", "raw"), f"Unknown pipeline {id}")
        require(all(isinstance(row[field], str) and row[field] for field in ("feature", "scope", "area")), f"Missing feature text {id}")
        require(set(row["status"]) == set(keys), f"Incomplete/extra version columns {id}")
        require(all(v in definitions for v in row["status"].values()), f"Unknown status {id}")
        require(row["evidence"] and all(key in evidence for key in row["evidence"]), f"Missing/unknown evidence {id}")
        for profile in profiles:
            if row["pipeline"] == "typed" and not profile["typed"]:
                require(row["status"][profile["id"]] == "X", f"Typed claim for unadmitted version {id}/{profile['id']}")
    pending_ids = [row["id"] for row in data["next_work"]]
    require(len(pending_ids) == len(set(pending_ids)), "Duplicate next-work id")
    qualification = data["qualification"]
    require(type(qualification["test_cases"]) is int and type(qualification["test_failures"]) is int, "Test counts must be integers")
    require(qualification["test_cases"] >= qualification["test_failures"] >= 0, "Invalid test counts")
    # A generator can never turn field counts into a certification.
    require(qualification["full_standard_complete"] is False, "Full-standard claim needs a separate reviewed acceptance contract")
    require(isinstance(qualification["autocad_executed"], bool), "Invalid AutoCAD execution flag")


def cell(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def render(data: dict[str, Any]) -> str:
    source = data["source"]
    profiles = data["profiles"]
    keys = [p["id"] for p in profiles]
    qualification = data["qualification"]
    typed = sum(p["typed"] for p in profiles)
    raw = sum(p["raw"] for p in profiles)
    rows = data["features"]
    lines = [
        "# netDxf DXF version and feature comparison",
        "",
        "> Generated from `coverage.json` by `tools/generate_dxf_coverage.py`; edit the ledger, not this file.",
        "",
        f"Audit date: **{data['audit_date']}**. Production baseline after PR **#{qualification['last_implementation_pr']}**: "
        f"[`{source['commit']}`](https://github.com/{source['repository']}/tree/{source['commit']}).",
        f"Source tree: `{source['tree']}`. Branch: `{source['branch']}`.",
        "",
        "## 1. Current result and scope",
        "",
        f"**{len(rows)} scoped feature rows; {typed} typed format families; {raw} raw-preservation format families. Full AutoCAD DXF capability is not yet achieved.**",
        "",
        "`DxfDocument` is the existing typed geometry/database API. `DxfRawDocument` is a separate immutable ordered-tag/record API. "
        "Raw preservation is now implemented; it is not an automatic preservation fallback inside typed `DxfDocument.Load/Save`. "
        "A raw file containing an unfamiliar entity can survive while that entity is still missing from the typed API.",
        "",
        "Each cell describes the exact operation and pipeline named by its row. It does not declare historical legality of every field in that record, "
        "nor that every producer uses the same defaults. In particular, raw retention of a modern record inside an older declared profile does not make that record legal in the older format. "
        "Generic opaque support is not a per-entity semantic fixture claim.",
        "",
        "The original 113-row source audit is retained unchanged as [the 12 September historical snapshot](version-feature-matrix-2026-09-12.md). "
        "Its then-missing VIEW/raw/CLASSES/UCS features must not be read as current status. "
        "This ledger keeps broad partial-family rows and adds narrow tested increments rather than promoting an entire family after fixing one field.",
        "",
        "| Mark | Meaning |", "|---|---|",
    ]
    for mark, meaning in data["status_definitions"].items():
        lines.append(f"| {cell(mark)} | {cell(meaning)} |")
    lines += ["", "## 2. Admitted version profiles", "",
              "| Format | ACADVER | Typed DxfDocument | Raw DxfRawDocument | Binary group codes | Character strategy |",
              "|---|---|---|---|---|---|"]
    for p in profiles:
        lines.append("| " + " | ".join(map(cell, [p["label"], p["id"], "Text/binary; partial typed schema" if p["typed"] else "Rejected",
            "Text/binary; preservation profile" if p["raw"] else "Rejected", p["binary_group_code"], p["encoding"]])) + " |")
    lines += ["", "Product release years and database-format families are separate: an AC1032 file can contain later product extensions. "
              "No new 2024/2026 database format is invented here. Historical code-page support and binary framing are explained in "
              "[R12](raw-r12.md), [R13/R14](raw-r13-r14.md), and [the raw API](raw-document.md). "
              "Autodesk's [HEADER reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm) supplies the format identifiers.",
              "", "### Declared enum families not admitted by either document API", "",
              "| Enum family | Declared identifier | Status |", "|---|---|---|"]
    for p in data["unadmitted_profiles"]:
        lines.append(f"| {cell(p['label'])} | {cell(p['id'])} | {cell(p['scope'])} |")
    lines += ["", "`Unknown` is an error/recognition sentinel, not another supported file version. "
              "Headerless inference remains open; enum presence alone does not implement a dialect.", ""]
    grouped: dict[str, list[dict[str, Any]]] = OrderedDict()
    for row in rows:
        grouped.setdefault(row["area"], []).append(row)
    for index, (area, items) in enumerate(grouped.items(), 3):
        lines += [f"## {index}. {area}", "",
                  "| Feature / pipeline | " + " | ".join(cell(p["label"]) for p in profiles) + " | Scope and evidence |",
                  "|---|" + "---|" * len(profiles) + "---|"]
        for row in items:
            references = []
            for key in row["evidence"]:
                item = data["evidence"][key]
                relative = os.path.relpath(item["path"], "doc/dxf-conformance").replace("\\", "/")
                references.append(f"[{key}]({relative})")
            lines.append("| " + cell(row["feature"]) + " · " + row["pipeline"] + " | " +
                " | ".join(row["status"][key] for key in keys) + " | " + cell(row["scope"]) + " " + ", ".join(references) + " |")
        lines.append("")
    number = len(grouped) + 3
    lines += [f"## {number}. Remaining implementation sequence", "",
              "| Workstream | Required scope |", "|---|---|"]
    for row in data["next_work"]:
        lines.append(f"| {cell(row['title'])} | {cell(row['scope'])} |")
    lines += ["", "Every feature/fix requires an isolated PR, independently authored positive and malformed fixtures, applicable version/transport tests, "
              "explicit normalization/downgrade semantics, and green final-head CI before merge. Shared reader/writer/graph edits are sequenced; "
              "nonconflicting work can proceed independently. Do not substitute raw preservation, static appearance or synthesized values for tested semantic support.",
              "", f"## {number+1}. Evidence and qualification", "",
              f"At the pinned production baseline, the .NET {qualification['dotnet_runtime']} conformance harness reports "
              f"**{qualification['test_cases']-qualification['test_failures']:,} passed / {qualification['test_failures']:,} failed** in Debug and Release. "
              "Linux/Windows GitHub Actions execute the SDK harness and compile netstandard2.0. "
              "These counts are regression evidence, not a percentage of DXF completeness.",
              "",
              f"Selected retained fixtures are also checked with **{qualification['independent_reader']}** using the development-only `tools/verify_*.py` scripts. "
              "Some scripts compare ordered tags; others invoke that implementation's audit. Their individual notes specify which claim was actually tested. "
              + ("AutoCAD execution evidence is recorded separately." if qualification["autocad_executed"] else "**No AutoCAD process was executed for this qualification.**"),
              "",
              "The Roslyn `tools/netDxf.FieldAudit` inventory records group-code branches, output expressions, declared properties, source locations and hashes. "
              "Literal code coverage is not semantic conformance: a consumed code may be ignored, derived or context-dependent. "
              "The detailed feature notes link the applicable Autodesk definitions and independent evidence.",
              "",
              "Production target frameworks and strong naming are retained. net471/net48/net6.0 compilation or runtime behavior is not inferred from .NET 8 test success. "
              "Full qualification still requires native/versioned CAD fixtures, dependency-closure checks, malformed/resource-limited corpora, "
              "actual legacy runtime execution and explicit per-loss down-save reporting.",
              "", "```sh", "python tools/generate_dxf_coverage.py --check",
              "python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'",
              "dotnet run --project tests/netDxf.Conformance -c Debug",
              "dotnet run --project tests/netDxf.Conformance -c Release",
              "dotnet build netDxf/netDxf.csproj -f netstandard2.0 -c Release", "```", ""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify without modifying files")
    args = parser.parse_args()
    try:
        data = json.loads(MANIFEST.read_text(encoding="utf-8"))
        validate(data, ROOT)
        text = render(data)
        if args.check:
            if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != text:
                print("Coverage Markdown is stale; run python tools/generate_dxf_coverage.py", file=sys.stderr)
                return 1
        else:
            OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"Coverage validation failed: {exc}", file=sys.stderr)
        return 2
    print(f"PASS coverage ledger: {len(data['features'])} scoped rows, {len(data['profiles'])} version columns")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
