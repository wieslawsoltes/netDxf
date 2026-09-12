#!/usr/bin/env python3
"""Emit auditable source evidence; method presence is NOT conformance proof."""
import json
import pathlib
import re
import subprocess

root = pathlib.Path(__file__).resolve().parents[1]
result = {
    "schemaVersion": 1,
    "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
    "warning": "Static inventory only. A dispatch or model does not establish full field support or round-trip fidelity.",
    "files": {},
}
for relative in ["netDxf/IO/DxfReader.cs", "netDxf/IO/DxfWriter.cs", "netDxf/Header/HeaderVariables.cs", "netDxf/DxfDocument.cs"]:
    path = root / relative
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    methods = []
    gates = []
    dispatch = []
    for index, line in enumerate(lines):
        if re.search(r"\b(?:Read|Write|Check)[A-Za-z0-9_]*\s*\(", line) and re.search(r"\b(?:public|private|internal)\b", line):
            methods.append({"line": index + 1, "text": line.strip()})
        if "DxfVersion." in line or "not supported" in line.lower() or "not implemented" in line.lower():
            gates.append({"line": index + 1, "context": "\n".join(lines[max(0, index-2):index+5])})
        if re.search(r"case (?:DxfObjectCode|DxfSectionCode|HeaderVariableCode|EntityType)\.", line):
            dispatch.append({"line": index + 1, "context": "\n".join(lines[index:index+6])})
    result["files"][relative] = {"lines": len(lines), "methods": methods, "versionGates": gates, "dispatch": dispatch}
for directory in ["Entities", "Objects", "Tables", "Header"]:
    result[directory] = sorted(p.name for p in (root / "netDxf" / directory).glob("*.cs"))
print(json.dumps(result, indent=2))
