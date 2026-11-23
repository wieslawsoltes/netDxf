#!/usr/bin/env python3
"""Automated public API parity comparator between C# sources (netDxf or shadow/src) and Rust crates.

Usage:
    # Core netDxf parity check
    python3 tools/rust_port/compare_api.py --report doc/rust-port/API-PARITY-REPORT.md --fail-on-missing

    # Shadow multi-project inventory & parity report
    python3 tools/rust_port/compare_api.py \
        --cs-root shadow/src \
        --rs-root rust/shadow \
        --report doc/rust-port/SHADOW-API-PARITY-REPORT.md \
        --title "Automated API Parity Report: C# shadow/src vs Rust"
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple

# Intentional idiomatic Rust mappings for core netDxf where C# uses .NET-specific patterns
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


def suggest_rust_crate_and_stack(project_name: str) -> Tuple[str, str]:
    """Map a C# Shadow project name to its planned Rust crate name and target stack."""
    lower = project_name.lower()
    if lower == "netdxf.shadow.rendering.skiasharp":
        return ("netdxf-shadow-rendering-skia", "Skia (`skia-safe`, replacing SkiaSharp)")
    if lower == "netdxf.shadow.studio.web":
        return ("netdxf-shadow-studio-ui", "FerroUI (desktop/web UI replacing Studio.Web)")
    if lower == "netdxf.shadow.studio.webassembly":
        return ("netdxf-shadow-studio-wasm", "FerroUI + WebAssembly (replacing Blazor WASM)")
    if lower.startswith("netdxf.shadow.studio."):
        suffix = lower.replace("netdxf.shadow.studio.", "").replace(".", "-")
        return (f"netdxf-shadow-studio-{suffix}", "FerroUI + Pure Rust Studio core/host")
    if lower == "netdxf.shadow.netdxf":
        return ("netdxf-shadow-netdxf", "Pure Rust adapter over `netdxf` crate")
    if lower.startswith("netdxf.shadow."):
        suffix = lower.replace("netdxf.shadow.", "").replace(".", "-")
        return (f"netdxf-shadow-{suffix}", "Pure Rust (`#![forbid(unsafe_code)]`)")
    if lower == "netdxf.shadow":
        return ("netdxf-shadow", "Pure Rust (`#![forbid(unsafe_code)]`)")
    return (lower.replace(".", "-"), "Pure Rust")


@dataclass
class TypeInfo:
    name: str
    kind: str
    namespace_or_mod: str
    project: str = ""
    files: List[str] = field(default_factory=list)
    members: Set[str] = field(default_factory=set)


CS_NAMESPACE_RE = re.compile(r"^\s*namespace\s+([A-Za-z0-9_\.]+)\s*[;{]", re.MULTILINE)

# Supports classes, structs, enums, interfaces, and C# 9/10 records (record, record class, record struct)
CS_TYPE_RE = re.compile(
    r"^\s*public\s+(?:(?:static|abstract|sealed|readonly|ref|partial|unsafe|new)\s+)*"
    r"(record\s+struct|record\s+class|record|class|struct|enum|interface)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

CS_MEMBER_RE = re.compile(
    r"^\s*public\s+(?:(?:static|virtual|override|readonly|const|new|abstract|sealed|extern|unsafe|async|required)\s+)*"
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


def detect_cs_projects(root: Path) -> Dict[Path, str]:
    """Find all .csproj directories under root (if multi-project)."""
    projects: Dict[Path, str] = {}
    for csproj in sorted(root.rglob("*.csproj")):
        projects[csproj.parent.resolve()] = csproj.stem
    return projects


def resolve_project(file_path: Path, root: Path, projects: Dict[Path, str]) -> str:
    cur = file_path.parent.resolve()
    root_res = root.resolve()
    while True:
        if cur in projects:
            return projects[cur]
        if cur == root_res or cur.parent == cur:
            break
        cur = cur.parent
    return root.name


def scan_csharp(root: Path, repo_root: Path) -> Tuple[Dict[str, TypeInfo], Dict[str, int]]:
    types: Dict[str, TypeInfo] = {}
    project_file_counts: Dict[str, int] = defaultdict(int)
    projects = detect_cs_projects(root)
    is_multi_project = len(projects) > 1

    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".git")]
        rel_dir = os.path.relpath(dirpath, root)
        fallback_ns = root.name if rel_dir == "." else f"{root.name}.{rel_dir.replace(os.sep, '.')}"
        for fname in sorted(filenames):
            if not fname.endswith(".cs"):
                continue
            fpath = Path(dirpath) / fname
            project = resolve_project(fpath, root, projects)
            project_file_counts[project] += 1
            text = fpath.read_text(encoding="utf-8", errors="ignore")
            try:
                rel_file = str(fpath.resolve().relative_to(repo_root))
            except ValueError:
                rel_file = str(fpath)

            ns_match = CS_NAMESPACE_RE.search(text)
            ns = ns_match.group(1) if ns_match else fallback_ns

            found_types = CS_TYPE_RE.findall(text)
            members = {
                m
                for m in CS_MEMBER_RE.findall(text)
                if m not in ("get", "set", "init", "add", "remove", "operator", "implicit", "explicit")
            }
            for raw_kind, name in found_types:
                kind = " ".join(raw_kind.split())
                # For multi-project trees, key by (project, name) if duplicate names exist across projects
                key = f"{project}::{name}" if is_multi_project else name
                if key not in types:
                    types[key] = TypeInfo(
                        name=name,
                        kind=kind,
                        namespace_or_mod=ns,
                        project=project,
                    )
                if rel_file not in types[key].files:
                    types[key].files.append(rel_file)
                types[key].members.update(members)

    return types, dict(project_file_counts)


def scan_rust(root: Optional[Path], repo_root: Path) -> Dict[str, TypeInfo]:
    types: Dict[str, TypeInfo] = {}
    if root is None or not root.exists():
        return types
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("target", ".git")]
        rel_dir = os.path.relpath(dirpath, root)
        mod = "netdxf" if rel_dir == "." else f"netdxf::{rel_dir.replace(os.sep, '::')}"
        for fname in sorted(filenames):
            if not fname.endswith(".rs"):
                continue
            fpath = Path(dirpath) / fname
            text = fpath.read_text(encoding="utf-8", errors="ignore")
            try:
                rel_file = str(fpath.resolve().relative_to(repo_root))
            except ValueError:
                rel_file = str(fpath)
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
    parser = argparse.ArgumentParser(
        description="Compare C# (netDxf or shadow/src) and Rust public APIs."
    )
    parser.add_argument("--cs-root", default="netDxf", help="Path to C# source root")
    parser.add_argument(
        "--rs-root",
        default="rust/netdxf/src",
        help="Path to Rust source root (optional if not yet ported)",
    )
    parser.add_argument("--report", default=None, help="Optional Markdown report output path")
    parser.add_argument("--json", default=None, help="Optional JSON report output path")
    parser.add_argument("--title", default=None, help="Optional custom Markdown report title")
    parser.add_argument(
        "--use-core-mappings",
        action="store_true",
        default=None,
        help="Apply core netDxf idiomatic mappings (default: auto when --cs-root is netDxf)",
    )
    parser.add_argument(
        "--fail-on-missing",
        action="store_true",
        help="Exit non-zero if any unmapped public C# types are missing in Rust",
    )
    args = parser.parse_args()

    repo_root = Path.cwd().resolve()
    cs_root = Path(args.cs_root)
    rs_root = Path(args.rs_root) if args.rs_root else None

    if not cs_root.exists():
        print(f"Error: C# root '{cs_root}' does not exist.", file=sys.stderr)
        return 2

    use_core_mappings = (
        args.use_core_mappings
        if args.use_core_mappings is not None
        else cs_root.resolve().name.lower() == "netdxf"
    )

    cs_types, project_file_counts = scan_csharp(cs_root, repo_root)
    rs_types = scan_rust(rs_root, repo_root)
    is_multi_project = len(project_file_counts) > 1

    rs_by_norm: Dict[str, TypeInfo] = {normalize_name(k): v for k, v in rs_types.items()}

    matched: List[Tuple[TypeInfo, TypeInfo]] = []
    mapped: List[Tuple[TypeInfo, str]] = []
    events: List[TypeInfo] = []
    missing: List[TypeInfo] = []

    for key in sorted(cs_types.keys()):
        info = cs_types[key]
        name = info.name
        norm = normalize_name(name)
        if name in rs_types:
            matched.append((info, rs_types[name]))
        elif norm in rs_by_norm:
            matched.append((info, rs_by_norm[norm]))
        elif is_event_or_delegate_type(name):
            events.append(info)
        elif use_core_mappings and name in IDIOMATIC_MAPPINGS:
            mapped.append((info, IDIOMATIC_MAPPINGS[name]))
        else:
            missing.append(info)

    total = len(cs_types)
    covered = len(matched) + len(mapped) + len(events)
    pct = (covered / total * 100.0) if total else 100.0

    print(f"C# root scanned:               {args.cs_root}")
    print(f"C# projects scanned:           {len(project_file_counts)}")
    print(f"C# source files scanned:       {sum(project_file_counts.values())}")
    print(f"C# public types scanned:       {total}")
    print(f"Rust public types scanned:     {len(rs_types)}")
    print(f"Direct 1:1 matches:            {len(matched)}")
    print(f"Idiomatic Rust mappings:       {len(mapped)}")
    print(f"C# EventArgs/Delegates (N/A):  {len(events)}")
    print(f"Missing public C# types:       {len(missing)}")
    print(f"Overall API coverage:          {pct:.1f}%")

    if is_multi_project:
        print("\n--- Breakdown by C# Project ---")
        by_proj: Dict[str, List[TypeInfo]] = defaultdict(list)
        for t in cs_types.values():
            by_proj[t.project].append(t)
        for proj in sorted(project_file_counts.keys()):
            ptypes = by_proj.get(proj, [])
            pmissing = sum(1 for t in missing if t.project == proj)
            crate, _ = suggest_rust_crate_and_stack(proj)
            print(
                f"  {proj:<42} files={project_file_counts[proj]:<3} "
                f"types={len(ptypes):<3} missing={pmissing:<3} -> {crate}"
            )
    elif missing:
        print("\n--- Missing Public C# Types by Namespace ---")
        by_ns: Dict[str, List[TypeInfo]] = defaultdict(list)
        for t in missing:
            by_ns[t.namespace_or_mod].append(t)
        for ns in sorted(by_ns.keys()):
            names = ", ".join(f"{t.name} ({t.kind})" for t in by_ns[ns])
            print(f"  {ns}: {names}")

    if args.report:
        title = args.title or f"Automated API Parity Report: C# `{args.cs_root}` vs Rust `{args.rs_root}`"
        lines = [
            f"# {title}",
            "",
            f"Generated by `python3 tools/rust_port/compare_api.py --cs-root {args.cs_root} --rs-root {args.rs_root}`.",
            "",
            "## Summary",
            "",
            "| Metric | Count |",
            "|---|---|",
            f"| C# projects (`{args.cs_root}`) | {len(project_file_counts)} |",
            f"| C# source files (`.cs`) | {sum(project_file_counts.values())} |",
            f"| C# public types | {total} |",
            f"| Rust public types (`{args.rs_root}`) | {len(rs_types)} |",
            f"| Direct 1:1 matched types | {len(matched)} |",
            f"| Idiomatic Rust mapped types | {len(mapped)} |",
            f"| C# EventArgs / EventHandlers (N/A in Rust) | {len(events)} |",
            f"| Missing public C# types | {len(missing)} |",
            f"| **Overall API Coverage** | **{pct:.1f}%** |",
            "",
        ]

        kind_counts = Counter(t.kind for t in cs_types.values())
        lines.append("### C# Public Types by Kind")
        lines.append("")
        lines.append("| Kind | Count |")
        lines.append("|---|---|")
        for kind, cnt in sorted(kind_counts.items(), key=lambda kv: (-kv[1], kv[0])):
            lines.append(f"| `{kind}` | {cnt} |")
        lines.append("")

        if is_multi_project:
            lines.append("## Target Rust Architecture & Per-Project Breakdown")
            lines.append("")
            lines.append("> **Planned Rust Stack**:")
            lines.append("> - **Rendering**: Replace `SkiaSharp` (`netDxf.Shadow.Rendering.SkiaSharp`) with native Rust **Skia** (`skia-safe`).")
            lines.append("> - **UI / Studio**: Replace Blazor/Web UI (`netDxf.Shadow.Studio.*`) with **[FerroUI](https://github.com/wieslawsoltes/FerroUI/)**.")
            lines.append("> - **Core DXF**: Build on top of the ported `rust/netdxf` crate (`100.0%` API parity).")
            lines.append("")
            lines.append(
                "| C# Project | `.cs` Files | Public Types | Missing | Planned Rust Crate | Target Stack |"
            )
            lines.append("|---|---:|---:|---:|---|---|")
            by_proj_all: Dict[str, List[TypeInfo]] = defaultdict(list)
            by_proj_missing: Dict[str, List[TypeInfo]] = defaultdict(list)
            for t in cs_types.values():
                by_proj_all[t.project].append(t)
            for t in missing:
                by_proj_missing[t.project].append(t)
            for proj in sorted(project_file_counts.keys()):
                crate, stack = suggest_rust_crate_and_stack(proj)
                lines.append(
                    f"| `{proj}` | {project_file_counts[proj]} | {len(by_proj_all[proj])} "
                    f"| {len(by_proj_missing[proj])} | `{crate}` | {stack} |"
                )
            lines.append("")

        if missing:
            lines.append("## Missing Public C# Types")
            lines.append("")
            if is_multi_project:
                by_proj_missing_sorted: Dict[str, List[TypeInfo]] = defaultdict(list)
                for t in missing:
                    by_proj_missing_sorted[t.project].append(t)
                for proj in sorted(by_proj_missing_sorted.keys()):
                    crate, stack = suggest_rust_crate_and_stack(proj)
                    ptypes = sorted(
                        by_proj_missing_sorted[proj],
                        key=lambda x: (x.namespace_or_mod, x.name),
                    )
                    lines.append(f"### `{proj}` → `{crate}` ({len(ptypes)} types)")
                    lines.append("")
                    lines.append(f"- **Target Stack**: {stack}")
                    lines.append("")
                    lines.append("| Namespace | Type | Kind | Source File |")
                    lines.append("|---|---|---|---|")
                    for t in ptypes:
                        lines.append(
                            f"| `{t.namespace_or_mod}` | `{t.name}` | `{t.kind}` | `{t.files[0]}` |"
                        )
                    lines.append("")
            else:
                lines.append("| Namespace | Type | Kind | Source File |")
                lines.append("|---|---|---|---|")
                for t in missing:
                    lines.append(
                        f"| `{t.namespace_or_mod}` | `{t.name}` | `{t.kind}` | `{t.files[0]}` |"
                    )
                lines.append("")

        if matched:
            lines.append("## Direct 1:1 Matched Types")
            lines.append("")
            lines.append("| C# Type | Rust Type | Rust Module |")
            lines.append("|---|---|---|")
            for cs, rs in matched:
                lines.append(f"| `{cs.name}` | `{rs.name}` | `{rs.namespace_or_mod}` |")
            lines.append("")

        if mapped:
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
            "cs_root": args.cs_root,
            "rs_root": args.rs_root,
            "cs_projects": len(project_file_counts),
            "cs_files": sum(project_file_counts.values()),
            "cs_total": total,
            "rs_total": len(rs_types),
            "matched": len(matched),
            "mapped": len(mapped),
            "events": len(events),
            "missing": [
                {
                    "project": t.project,
                    "name": t.name,
                    "kind": t.kind,
                    "namespace": t.namespace_or_mod,
                    "files": t.files,
                }
                for t in missing
            ],
            "coverage_percent": round(pct, 2),
        }
        Path(args.json).write_text(json.dumps(payload, indent=2), encoding="utf-8")

    return 1 if (args.fail_on_missing and missing) else 0


if __name__ == "__main__":
    sys.exit(main())
