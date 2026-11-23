#!/usr/bin/env python3
"""Automated public API parity comparator between C# netDxf and Rust rust/netdxf.

Usage:
    python3 tools/rust_port/compare_api.py
    python3 tools/rust_port/compare_api.py --report doc/rust-port/API-PARITY-REPORT.md
    python3 tools/rust_port/compare_api.py --fail-on-missing
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Set, Tuple

# Intentional idiomatic Rust mappings where C# uses .NET-specific patterns
# (events, typed ObservableCollection subclasses, exceptions, reflection helpers).
IDIOMATIC_MAPPINGS: Dict[str, str] = {
    # Exceptions -> DxfError variants
    "DxfVersionNotSupportedException": "DxfError::VersionNotSupported",
    # Generic / .NET collection wrappers -> TableObjects<T> / IndexMap / Vec
    "ApplicationRegistries": "TableObjects<ApplicationRegistry>",
    "BlockRecords": "TableObjects<Block>",
    "DimensionStyles": "TableObjects<DimensionStyle>",
    "Groups": "TableObjects<Group>",
    "ImageDefinitions": "TableObjects<ImageDefinition>",
    "Layers": "TableObjects<Layer>",
    "Layouts": "TableObjects<Layout>",
    "Linetypes": "TableObjects<Linetype>",
    "MLineStyles": "TableObjects<MLineStyle>",
    "ShapeStyles": "TableObjects<ShapeStyle>",
    "TextStyles": "TableObjects<TextStyle>",
    "UCSs": "TableObjects<UCS>",
    "UnderlayDgnDefinitions": "TableObjects<UnderlayDefinition>",
    "UnderlayDwfDefinitions": "TableObjects<UnderlayDefinition>",
    "UnderlayPdfDefinitions": "TableObjects<UnderlayDefinition>",
    "VPorts": "TableObjects<VPort>",
    "Views": "TableObjects<View>",
    "AttributeCollection": "Vec<Attribute>",
    "AttributeDefinitionDictionary": "IndexMap<String, AttributeDefinition>",
    "DimensionStyleOverrideDictionary": "IndexMap<DimensionStyleOverrideType, DimensionStyleOverride>",
    "DxfClassCollection": "Vec<DxfClass>",
    "EntityCollection": "Vec<EntityObject>",
    "ObservableCollection": "Vec<T>",
    "ObservableDictionary": "IndexMap<K, V>",
    "XDataDictionary": "IndexMap<String, XData>",
    "DxfObjectReferences": "Handle / Vec<String> references",
    "DxfObjectReference": "Handle / String reference",
    # C# abstract bases modeled as Rust enums/shared structs
    "BezierCurve": "BezierCurveCubic / BezierCurveQuadratic",
    "EntityObject": "EntityObject (enum) + EntityCommon",
    "AcisEntity": "AcisEntityData + Solid3D/Body/Region",
    "LinetypeSegment": "LinetypeSegment (enum)",
    "LinetypeSimpleSegment": "LinetypeSegment::Simple",
    "LinetypeTextSegment": "LinetypeSegment::Text",
    "LinetypeShapeSegment": "LinetypeSegment::Shape",
    "UnderlayDefinition": "UnderlayDefinition",
    "UnderlayDgnDefinition": "UnderlayDefinition (UnderlayType::Dgn)",
    "UnderlayDwfDefinition": "UnderlayDefinition (UnderlayType::Dwf)",
    "UnderlayPdfDefinition": "UnderlayDefinition (UnderlayType::Pdf)",
    "AlignedDimension": "Dimension (DimensionType::Aligned)",
    "Angular2LineDimension": "Dimension (DimensionType::Angular)",
    "Angular3PointDimension": "Dimension (DimensionType::Angular3Point)",
    "ArcLengthDimension": "Dimension (DimensionType::ArcLength)",
    "DiametricDimension": "Dimension (DimensionType::Diameter)",
    "LinearDimension": "Dimension (DimensionType::Linear)",
    "OrdinateDimension": "Dimension (DimensionType::Ordinate)",
    "RadialDimension": "Dimension (DimensionType::Radius)",
    "MLineStyleElement": "MLineStyleElement",
    "XRecordEntry": "XRecordEntry / DxfTag",
    "StringEnum": "Rust enum with as_code()/from_code()",
    "StringValueAttribute": "Rust enum as_str() / Display methods",
    "UnitFormatMath": "Internal unit formatting math",
    "ConicSplineExtensions": "Spline / Circle / Arc / Ellipse methods",
    "CurvePolylineConversion": "Polyline2D / Spline / Arc conversion methods",
    "DxfTableAverageAccumulator": "Neumaier compensated mean in DxfTableFormula",
    # Internal/advanced C# database plumbing folded into DxfDocument/DxfRawDocument
    "DxfDatabaseObject": "DxfObject",
    "DxfObjectDatabase": "DxfDocument",
    "DxfObjectPointer": "Option<String> handle",
    "DxfRawHandleIndex": "DxfRawDocument handle index",
    "DxfRawOptions": "DxfRawDocument options",
    "DxfTagSlice": "&[DxfTag]",
    "DxfFieldBinaryValue": "XDataValue::Binary / Vec<u8>",
    "DxfFieldEvaluationSnapshot": "DxfFieldResult",
    "DxfFieldFailure": "DxfError",
    "DxfFieldVariable": "DxfStandardFieldEvaluator variables",
    "DxfTableContentGrid": "StoredTable grid",
    "DxfTableLayout": "StoredTable layout",
    "DxfStoredTableContent": "StoredTable",
    "DxfStoredTableGeometry": "StoredTable",
    "DxfStoredCellStyleMap": "DxfCellStyleFormat",
    "DxfCellStyleMapDefinition": "DxfCellStyleFormat",
    "DxfR12SelectionPlan": "DxfR12Codec",
    "DxfSplineCountPolicy": "Spline control/knot/fit validation",
    "LayerStateManager": "LayerState + DxfDocument",
    "LayerStateRestoreMask": "LayerPropertiesRestoreFlags",
    "UcsOrthographicBase": "UCS orthographic base fields",
    "DxfDeclaredOwnership": "DxfObject::owner_handle",
}


def is_event_or_delegate_type(name: str) -> bool:
    return (
        name.endswith("EventArgs")
        or name.endswith("EventHandler")
        or name.endswith("ChangeEventArgs")
    )


def normalize_name(name: str) -> str:
    """Normalize PascalCase / snake_case identifiers for fuzzy matching."""
    return re.sub(r"[^a-z0-9]", "", name.lower())


@dataclass
class TypeInfo:
    name: str
    kind: str
    namespace_or_mod: str
    files: List[str] = field(default_factory=list)
    members: Set[str] = field(default_factory=set)


CS_TYPE_RE = re.compile(
    r"^\s*public\s+(?:(?:static|abstract|sealed|readonly|ref|partial)\s+)*"
    r"(class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

CS_MEMBER_RE = re.compile(
    r"^\s*public\s+(?:(?:static|virtual|override|readonly|const|new|abstract|sealed|extern|unsafe|async)\s+)*"
    r"(?:[A-Za-z0-9_<>,\.\[\]\?\s]+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|=>|\{|;)",
    re.MULTILINE,
)

RS_TYPE_RE = re.compile(
    r"^\s*pub\s+(struct|enum|trait|type)\s+([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

RS_BITFLAGS_RE = re.compile(
    r"pub\s+struct\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*[iu](?:8|16|32|64)",
    re.MULTILINE,
)

RS_FN_RE = re.compile(
    r"^\s*pub\s+(?:const\s+)?(?:async\s+)?fn\s+([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

RS_FIELD_RE = re.compile(
    r"^\s*pub\s+([a-z_][a-z0-9_]*)\s*:",
    re.MULTILINE,
)


def scan_csharp(root: Path) -> Dict[str, TypeInfo]:
    types: Dict[str, TypeInfo] = {}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
        rel_dir = os.path.relpath(dirpath, root)
        ns = "netDxf" if rel_dir == "." else f"netDxf.{rel_dir.replace(os.sep, '.')}"
        for fname in sorted(filenames):
            if not fname.endswith(".cs"):
                continue
            fpath = Path(dirpath) / fname
            text = fpath.read_text(encoding="utf-8", errors="ignore")
            rel_file = str(fpath.relative_to(root.parent))
            found_types = CS_TYPE_RE.findall(text)
            members = {
                m
                for m in CS_MEMBER_RE.findall(text)
                if m not in ("get", "set", "add", "remove", "operator", "implicit", "explicit")
            }
            for kind, name in found_types:
                if name not in types:
                    types[name] = TypeInfo(name=name, kind=kind, namespace_or_mod=ns)
                if rel_file not in types[name].files:
                    types[name].files.append(rel_file)
                types[name].members.update(members)
    return types


def scan_rust(root: Path) -> Dict[str, TypeInfo]:
    types: Dict[str, TypeInfo] = {}
    for dirpath, _, filenames in os.walk(root):
        rel_dir = os.path.relpath(dirpath, root)
        mod = "netdxf" if rel_dir == "." else f"netdxf::{rel_dir.replace(os.sep, '::')}"
        for fname in sorted(filenames):
            if not fname.endswith(".rs"):
                continue
            fpath = Path(dirpath) / fname
            text = fpath.read_text(encoding="utf-8", errors="ignore")
            rel_file = str(fpath.relative_to(root.parent.parent.parent))
            found = list(RS_TYPE_RE.findall(text))
            for bf in RS_BITFLAGS_RE.findall(text):
                found.append(("bitflags", bf))
            fns = set(RS_FN_RE.findall(text))
            fields = set(RS_FIELD_RE.findall(text))
            for kind, name in found:
                if name not in types:
                    types[name] = TypeInfo(name=name, kind=kind, namespace_or_mod=mod)
                if rel_file not in types[name].files:
                    types[name].files.append(rel_file)
                types[name].members.update(fns)
                types[name].members.update(fields)
    return types


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare C# netDxf and Rust netdxf public APIs.")
    parser.add_argument("--cs-root", default="netDxf", help="Path to C# netDxf source root")
    parser.add_argument("--rs-root", default="rust/netdxf/src", help="Path to Rust netdxf src root")
    parser.add_argument("--report", default=None, help="Optional Markdown report output path")
    parser.add_argument("--json", default=None, help="Optional JSON report output path")
    parser.add_argument(
        "--fail-on-missing",
        action="store_true",
        help="Exit non-zero if any unmapped public C# types are missing in Rust",
    )
    args = parser.parse_args()

    cs_types = scan_csharp(Path(args.cs_root))
    rs_types = scan_rust(Path(args.rs_root))

    rs_by_norm: Dict[str, TypeInfo] = {normalize_name(k): v for k, v in rs_types.items()}

    matched: List[Tuple[TypeInfo, TypeInfo]] = []
    mapped: List[Tuple[TypeInfo, str]] = []
    events: List[TypeInfo] = []
    missing: List[TypeInfo] = []

    for name in sorted(cs_types.keys()):
        info = cs_types[name]
        norm = normalize_name(name)
        if name in rs_types:
            matched.append((info, rs_types[name]))
        elif norm in rs_by_norm:
            matched.append((info, rs_by_norm[norm]))
        elif is_event_or_delegate_type(name):
            events.append(info)
        elif name in IDIOMATIC_MAPPINGS:
            mapped.append((info, IDIOMATIC_MAPPINGS[name]))
        else:
            missing.append(info)

    total = len(cs_types)
    covered = len(matched) + len(mapped) + len(events)
    pct = (covered / total * 100.0) if total else 100.0

    print(f"C# public types scanned:       {total}")
    print(f"Rust public types scanned:     {len(rs_types)}")
    print(f"Direct 1:1 matches:            {len(matched)}")
    print(f"Idiomatic Rust mappings:       {len(mapped)}")
    print(f"C# EventArgs/Delegates (N/A):  {len(events)}")
    print(f"Missing public C# types:       {len(missing)}")
    print(f"Overall API coverage:          {pct:.1f}%")

    if missing:
        print("\n--- Missing Public C# Types by Namespace ---")
        by_ns: Dict[str, List[TypeInfo]] = defaultdict(list)
        for t in missing:
            by_ns[t.namespace_or_mod].append(t)
        for ns in sorted(by_ns.keys()):
            names = ", ".join(f"{t.name} ({t.kind})" for t in by_ns[ns])
            print(f"  {ns}: {names}")

    if args.report:
        lines = [
            "# Automated API Parity Report: C# `netDxf` vs Rust `netdxf`",
            "",
            "Generated by `python3 tools/rust_port/compare_api.py`.",
            "",
            "| Metric | Count |",
            "|---|---|",
            f"| C# public types (`netDxf/`) | {total} |",
            f"| Rust public types (`rust/netdxf/src/`) | {len(rs_types)} |",
            f"| Direct 1:1 matched types | {len(matched)} |",
            f"| Idiomatic Rust mapped types | {len(mapped)} |",
            f"| C# EventArgs / EventHandlers (N/A in Rust) | {len(events)} |",
            f"| Missing public C# types | {len(missing)} |",
            f"| **Overall API Coverage** | **{pct:.1f}%** |",
            "",
        ]
        if missing:
            lines.append("## Missing Public C# Types")
            lines.append("")
            lines.append("| Namespace | Type | Kind | Source File |")
            lines.append("|---|---|---|---|")
            for t in missing:
                lines.append(f"| `{t.namespace_or_mod}` | `{t.name}` | `{t.kind}` | `{t.files[0]}` |")
            lines.append("")

        lines.append("## Direct 1:1 Matched Types")
        lines.append("")
        lines.append("| C# Type | Rust Type | Rust Module |")
        lines.append("|---|---|---|")
        for cs, rs in matched:
            lines.append(f"| `{cs.name}` | `{rs.name}` | `{rs.namespace_or_mod}` |")
        lines.append("")

        lines.append("## Idiomatic Rust Mappings")
        lines.append("")
        lines.append("| C# Type | Rust Equivalent |")
        lines.append("|---|---|")
        for cs, target in mapped:
            lines.append(f"| `{cs.name}` | `{target}` |")
        lines.append("")

        Path(args.report).write_text("\n".join(lines), encoding="utf-8")
        print(f"\nWrote Markdown report to {args.report}")

    if args.json:
        payload = {
            "cs_total": total,
            "rs_total": len(rs_types),
            "matched": len(matched),
            "mapped": len(mapped),
            "events": len(events),
            "missing": [
                {"name": t.name, "kind": t.kind, "namespace": t.namespace_or_mod, "files": t.files}
                for t in missing
            ],
            "coverage_percent": round(pct, 2),
        }
        Path(args.json).write_text(json.dumps(payload, indent=2), encoding="utf-8")

    return 1 if (args.fail_on_missing and missing) else 0


if __name__ == "__main__":
    sys.exit(main())
