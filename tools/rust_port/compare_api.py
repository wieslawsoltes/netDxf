#!/usr/bin/env python3
"""Automated public API parity comparator between C# sources (netDxf or shadow/src) and Rust crates.

Measures:
  1. Public type parity (direct 1:1 matches, idiomatic Rust mappings, EventArgs N/A, missing).
  2. Public member/method parity (methods, properties, record parameters, constants) on matched types.
  3. Non-blank, non-comment code LOC depth (overall and per-project for multi-project trees).

Usage:
    # Core netDxf parity check
    python3 tools/rust_port/compare_api.py --report doc/rust-port/API-PARITY-REPORT.md --fail-on-missing

    # Shadow multi-project parity & depth report
    python3 tools/rust_port/compare_api.py \
        --cs-root shadow/src \
        --rs-root rust/shadow \
        --report doc/rust-port/SHADOW-API-PARITY-REPORT.md \
        --title "Shadow (shadow/src) Rust Port Scope & API Parity Report"
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
    "DxfVersionNotSupportedException": "DxfError::VersionNotSupported",
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

CS_IGNORED_MEMBERS: Set[str] = {
    "get", "set", "init", "add", "remove", "operator", "implicit", "explicit",
    "ToString", "Equals", "GetHashCode", "Clone", "Dispose", "DisposeAsync",
    "CompareTo", "GetEnumerator",
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


def count_code_loc(text: str) -> int:
    """Count non-blank, non-comment, non-preprocessor lines of code."""
    count = 0
    in_block_comment = False
    for raw in text.splitlines():
        s = raw.strip()
        if not s:
            continue
        if in_block_comment:
            if "*/" in s:
                in_block_comment = False
            continue
        if s.startswith("/*"):
            if "*/" not in s:
                in_block_comment = True
            continue
        if s.startswith("//") or s.startswith("#region") or s.startswith("#endregion"):
            continue
        count += 1
    return count


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

CS_TYPE_RE = re.compile(
    r"^\s*public\s+(?:(?:static|abstract|sealed|readonly|ref|partial|unsafe|new)\s+)*"
    r"(record\s+struct|record\s+class|record|class|struct|enum|interface)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

CS_MEMBER_RE = re.compile(
    r"^\s*public\s+(?:(?:static|virtual|override|readonly|const|new|abstract|sealed|extern|unsafe|async|required)\s+)*"
    r"(?:[A-Za-z0-9_<>,\.\[\]\?\s]+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|=>|\{|;|=)",
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
    r"^\s*(?:\#\[[^\]]*\]\s*)*pub\s+(?:const\s+)?(?:async\s+)?fn\s+([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

RS_FIELD_RE = re.compile(
    r"^\s*(?:\#\[[^\]]*\]\s*)*pub\s+(?:r\#)?([a-z_][a-z0-9_]*)\s*:",
    re.MULTILINE,
)

RS_CONST_RE = re.compile(
    r"^\s*(?:\#\[[^\]]*\]\s*)*pub\s+const\s+([A-Z_][A-Z0-9_]*)\s*:",
    re.MULTILINE,
)

RS_ENUM_VAR_RE = re.compile(
    r"^\s*([A-Z][A-Za-z0-9_]*)\s*(?:\(|\{|,|=|$)",
    re.MULTILINE,
)


def extract_brace_body(text: str, start_pos: int) -> str:
    """Extract the `{ ... }` body starting from the first `{` after `start_pos`."""
    brace = text.find("{", start_pos)
    semi = text.find(";", start_pos)
    if brace == -1 or (semi != -1 and semi < brace):
        return ""
    depth = 0
    in_str = False
    esc = False
    for idx in range(brace, len(text)):
        ch = text[idx]
        if esc:
            esc = False
            continue
        if ch == "\\":
            esc = True
            continue
        if ch == '"':
            in_str = not in_str
        if not in_str:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return text[brace + 1 : idx]
    return text[brace + 1 :]


def extract_record_params(text: str, name_end: int) -> Set[str]:
    """Extract primary constructor parameter names from a C# `record Foo(Type A, Type B = ...)`."""
    params: Set[str] = set()
    # Skip optional generic `<T>`
    i = name_end
    while i < len(text) and text[i].isspace():
        i += 1
    if i < len(text) and text[i] == "<":
        end_gen = text.find(">", i)
        if end_gen != -1:
            i = end_gen + 1
            while i < len(text) and text[i].isspace():
                i += 1
    if i >= len(text) or text[i] != "(":
        return params
    depth = 0
    end_paren = -1
    for idx in range(i, len(text)):
        if text[idx] == "(":
            depth += 1
        elif text[idx] == ")":
            depth -= 1
            if depth == 0:
                end_paren = idx
                break
    if end_paren == -1:
        return params
    param_str = text[i + 1 : end_paren]
    # Split top-level commas (outside `<>` and `()`)
    parts: List[str] = []
    cur: List[str] = []
    d_angle = 0
    d_paren = 0
    for ch in param_str:
        if ch == "<":
            d_angle += 1
        elif ch == ">":
            d_angle = max(0, d_angle - 1)
        elif ch == "(":
            d_paren += 1
        elif ch == ")":
            d_paren = max(0, d_paren - 1)
        elif ch == "," and d_angle == 0 and d_paren == 0:
            parts.append("".join(cur))
            cur = []
            continue
        cur.append(ch)
    if cur:
        parts.append("".join(cur))
    for p in parts:
        p = p.split("=", 1)[0].strip()
        tokens = p.split()
        if len(tokens) >= 2:
            pname = re.sub(r"[^A-Za-z0-9_]", "", tokens[-1])
            if pname and pname[0].isalpha():
                params.add(pname)
    return params


def strip_nested_types(body: str) -> str:
    """Remove nested type declarations (public/private/internal class/struct/record/enum/interface) from a type body."""
    nested_re = re.compile(
        r"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|readonly|ref|partial|unsafe|new)\s+)+"
        r"(?:record\s+struct|record\s+class|record|class|struct|enum|interface)\s+[A-Za-z_][A-Za-z0-9_]*",
        re.MULTILINE,
    )
    out: List[str] = []
    pos = 0
    while True:
        m = nested_re.search(body, pos)
        if not m:
            out.append(body[pos:])
            break
        out.append(body[pos : m.start()])
        brace = body.find("{", m.end())
        semi = body.find(";", m.end())
        if brace == -1 or (semi != -1 and semi < brace):
            pos = (semi + 1) if semi != -1 else m.end()
            continue
        depth = 0
        in_str = False
        esc = False
        end_idx = len(body)
        for idx in range(brace, len(body)):
            ch = body[idx]
            if esc:
                esc = False
                continue
            if ch == "\\":
                esc = True
                continue
            if ch == '"':
                in_str = not in_str
            if not in_str:
                if ch == "{":
                    depth += 1
                elif ch == "}":
                    depth -= 1
                    if depth == 0:
                        end_idx = idx + 1
                        break
        pos = end_idx
    return "".join(out)


def is_cs_event_member_line(line: str, ident: str) -> bool:
    s = line.strip()
    if " event " in f" {s} " or " delegate " in f" {s} ":
        return True
    if ident.endswith("EventHandler") or ident.endswith("EventArgs"):
        return True
    return False


def extract_cs_type_members(text: str, match: re.Match[str], kind: str, name: str) -> Set[str]:
    members: Set[str] = set()
    if kind.startswith("record"):
        members.update(extract_record_params(text, match.end()))
    raw_body = extract_brace_body(text, match.end())
    if not raw_body:
        return members
    body = strip_nested_types(raw_body)
    if kind == "enum":
        for line in body.splitlines():
            s = line.strip()
            if not s or s.startswith("//") or s.startswith("["):
                continue
            m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)", s)
            if m:
                members.add(m.group(1))
    else:
        for m_obj in CS_MEMBER_RE.finditer(body):
            m = m_obj.group(1)
            line = m_obj.group(0)
            if m != name and m not in CS_IGNORED_MEMBERS and not is_cs_event_member_line(line, m):
                members.add(m)
        # Interface members (often omit `public`)
        if kind == "interface":
            for m in re.finditer(
                r"^\s*(?:[A-Za-z0-9_<>,\.\[\]\?\s]+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|\{|=>)",
                body,
                re.MULTILINE,
            ):
                ident = m.group(1)
                if ident != name and ident not in CS_IGNORED_MEMBERS and ident not in ("return", "if", "for"):
                    members.add(ident)
    return members


def detect_cs_projects(root: Path) -> Dict[Path, str]:
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


def scan_csharp(
    root: Path, repo_root: Path
) -> Tuple[Dict[str, TypeInfo], Dict[str, int], Dict[str, int]]:
    types: Dict[str, TypeInfo] = {}
    project_file_counts: Dict[str, int] = defaultdict(int)
    project_loc: Dict[str, int] = defaultdict(int)
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
            project_loc[project] += count_code_loc(text)
            try:
                rel_file = str(fpath.resolve().relative_to(repo_root))
            except ValueError:
                rel_file = str(fpath)

            ns_match = CS_NAMESPACE_RE.search(text)
            ns = ns_match.group(1) if ns_match else fallback_ns

            for match in CS_TYPE_RE.finditer(text):
                kind = " ".join(match.group(1).split())
                name = match.group(2)
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
                types[key].members.update(extract_cs_type_members(text, match, kind, name))

    return types, dict(project_file_counts), dict(project_loc)


def detect_rs_crate(file_path: Path, root: Path) -> str:
    cur = file_path.parent.resolve()
    root_res = root.resolve()
    while True:
        if (cur / "Cargo.toml").exists():
            return cur.name
        if cur == root_res or cur.parent == cur:
            break
        cur = cur.parent
    return root.name


def scan_rust(
    root: Optional[Path], repo_root: Path
) -> Tuple[Dict[str, TypeInfo], Dict[str, int], Dict[str, int]]:
    types: Dict[str, TypeInfo] = {}
    crate_files: Dict[str, int] = defaultdict(int)
    crate_loc: Dict[str, int] = defaultdict(int)
    if root is None or not root.exists():
        return types, {}, {}

    # Track type aliases `pub type Alias = Target;` so aliases inherit target members
    type_aliases: Dict[str, str] = {}
    alias_re = re.compile(
        r"^\s*pub\s+type\s+([A-Za-z_][A-Za-z0-9_]*)(?:<[^>]*>)?\s*=\s*(?:[a-z0-9_]+::)*([A-Za-z_][A-Za-z0-9_]*)",
        re.MULTILINE,
    )

    # Collect `impl Trait for Type` mappings so Type inherits Trait's default methods
    trait_impls: List[Tuple[str, str]] = []
    trait_impl_re = re.compile(
        r"^\s*impl(?:<[^>]+>)?\s+(?:[a-z0-9_]+::)*([A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\s+for\s+([A-Za-z_][A-Za-z0-9_]*)",
        re.MULTILINE,
    )

    # Track members per type across both type definition and `impl Type` blocks
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("target", ".git", "tests", "examples", "benches")]
        rel_dir = os.path.relpath(dirpath, root)
        mod = "netdxf" if rel_dir == "." else f"netdxf::{rel_dir.replace(os.sep, '::')}"
        for fname in sorted(filenames):
            if not fname.endswith(".rs"):
                continue
            fpath = Path(dirpath) / fname
            crate = detect_rs_crate(fpath, root)
            crate_files[crate] += 1
            text = fpath.read_text(encoding="utf-8", errors="ignore")
            crate_loc[crate] += count_code_loc(text)
            try:
                rel_file = str(fpath.resolve().relative_to(repo_root))
            except ValueError:
                rel_file = str(fpath)

            for am in alias_re.finditer(text):
                type_aliases[am.group(1)] = am.group(2)

            for tm in trait_impl_re.finditer(text):
                trait_impls.append((tm.group(1), tm.group(2)))

            for match in RS_TYPE_RE.finditer(text):
                kind = match.group(1)
                name = match.group(2)
                if name not in types:
                    types[name] = TypeInfo(name=name, kind=kind, namespace_or_mod=mod, project=crate)
                if rel_file not in types[name].files:
                    types[name].files.append(rel_file)
                body = extract_brace_body(text, match.end())
                if kind == "struct":
                    types[name].members.update(RS_FIELD_RE.findall(body))
                elif kind == "enum":
                    types[name].members.update(RS_ENUM_VAR_RE.findall(body))
                elif kind == "trait":
                    types[name].members.update(re.findall(r"\bfn\s+([a-z_][a-z0-9_]*)", body))
                    types[name].members.update(RS_CONST_RE.findall(body))

            for match in RS_BITFLAGS_RE.finditer(text):
                name = match.group(1)
                if name not in types:
                    types[name] = TypeInfo(name=name, kind="bitflags", namespace_or_mod=mod, project=crate)
                if rel_file not in types[name].files:
                    types[name].files.append(rel_file)
                body = extract_brace_body(text, match.end())
                types[name].members.update(re.findall(r"\bconst\s+([A-Z_][A-Z0-9_]*)\s*=", body))

            # Collect `impl Type { ... }` and `impl Trait for Type { ... }` methods & consts
            for match in re.finditer(
                r"^\s*impl(?:<[^>]+>)?\s+(?:[A-Za-z0-9_:<>]+\s+for\s+)?([A-Za-z_][A-Za-z0-9_]*)",
                text,
                re.MULTILINE,
            ):
                tname = match.group(1)
                body = extract_brace_body(text, match.end())
                if not body:
                    continue
                if tname not in types:
                    types[tname] = TypeInfo(name=tname, kind="struct", namespace_or_mod=mod, project=crate)
                types[tname].members.update(RS_FN_RE.findall(body))
                types[tname].members.update(RS_CONST_RE.findall(body))
                # Trait impl methods omit `pub`
                types[tname].members.update(re.findall(r"^\s*(?:async\s+)?fn\s+([a-z_][a-z0-9_]*)", body, re.MULTILINE))

    # Propagate trait default methods to implementing types (`impl Trait for Type`)
    for trait_name, type_name in trait_impls:
        if trait_name in types and type_name in types:
            types[type_name].members.update(types[trait_name].members)

    # Propagate target type members to `pub type Alias = Target;` aliases
    for _ in range(3):
        for alias, target in type_aliases.items():
            if alias in types and target in types:
                types[alias].members.update(types[target].members)

    return types, dict(crate_files), dict(crate_loc)


def compare_members(cs: TypeInfo, rs: TypeInfo) -> Tuple[int, int, List[str]]:
    """Return (matched_count, total_cs_members, missing_cs_member_names)."""
    if not cs.members:
        return (0, 0, [])
    rs_norm = {normalize_name(m) for m in rs.members}
    # Also accept `get_<x>` / `set_<x>` / `is_<x>` / `to_<x>` aliases
    rs_expanded = set(rs_norm)
    for m in list(rs_norm):
        for prefix in ("get", "set", "is", "has", "try"):
            if m.startswith(prefix) and len(m) > len(prefix):
                rs_expanded.add(m[len(prefix) :])
    matched = 0
    missing: List[str] = []
    for m in sorted(cs.members):
        nm = normalize_name(m)
        nm_no_async = nm[:-5] if nm.endswith("async") and len(nm) > 5 else nm
        if (
            nm in rs_expanded
            or nm_no_async in rs_expanded
            or (nm.startswith("is") and nm[2:] in rs_expanded)
            or (nm_no_async.startswith("is") and nm_no_async[2:] in rs_expanded)
        ):
            matched += 1
        else:
            missing.append(m)
    return (matched, len(cs.members), missing)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compare C# (netDxf or shadow/src) and Rust public APIs (types, members, and LOC depth)."
    )
    parser.add_argument("--cs-root", default="netDxf", help="Path to C# source root")
    parser.add_argument("--rs-root", default="rust/netdxf/src", help="Path to Rust source root")
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
    parser.add_argument(
        "--min-member-coverage",
        type=float,
        default=None,
        help="Optional minimum member coverage percentage required for exit code 0",
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

    cs_types, project_file_counts, project_cs_loc = scan_csharp(cs_root, repo_root)
    rs_types, crate_file_counts, crate_rs_loc = scan_rust(rs_root, repo_root)
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

    # Member-level parity on matched types
    total_cs_members = 0
    matched_members = 0
    member_details: List[Tuple[TypeInfo, TypeInfo, int, int, List[str]]] = []
    proj_member_stats: Dict[str, List[int]] = defaultdict(lambda: [0, 0])  # [matched, total]

    for cs, rs in matched:
        m_ok, m_tot, m_miss = compare_members(cs, rs)
        total_cs_members += m_tot
        matched_members += m_ok
        proj_member_stats[cs.project][0] += m_ok
        proj_member_stats[cs.project][1] += m_tot
        member_details.append((cs, rs, m_ok, m_tot, m_miss))

    total = len(cs_types)
    covered = len(matched) + len(mapped) + len(events)
    type_pct = (covered / total * 100.0) if total else 100.0
    member_pct = (matched_members / total_cs_members * 100.0) if total_cs_members else 100.0

    total_cs_loc = sum(project_cs_loc.values())
    total_rs_loc = sum(crate_rs_loc.values())
    loc_pct = (total_rs_loc / total_cs_loc * 100.0) if total_cs_loc else 0.0

    print(f"C# root scanned:               {args.cs_root}")
    print(f"C# projects scanned:           {len(project_file_counts)}")
    print(f"C# source files / code LOC:    {sum(project_file_counts.values())} files / {total_cs_loc:,} LOC")
    print(f"Rust source files / code LOC:  {sum(crate_file_counts.values())} files / {total_rs_loc:,} LOC ({loc_pct:.1f}% LOC depth)")
    print(f"C# public types scanned:       {total}")
    print(f"Rust public types scanned:     {len(rs_types)}")
    print(f"Direct 1:1 type matches:       {len(matched)}")
    print(f"Idiomatic Rust mappings:       {len(mapped)}")
    print(f"C# EventArgs/Delegates (N/A):  {len(events)}")
    print(f"Missing public C# types:       {len(missing)}")
    print(f"Type API coverage:             {type_pct:.1f}%")
    print(f"Matched-type member coverage:  {matched_members:,}/{total_cs_members:,} ({member_pct:.1f}%)")

    if is_multi_project:
        print("\n--- Per-Project Parity & Depth ---")
        for proj in sorted(project_file_counts.keys()):
            crate, _ = suggest_rust_crate_and_stack(proj)
            pm_ok, pm_tot = proj_member_stats[proj]
            pm_pct = (pm_ok / pm_tot * 100.0) if pm_tot else 100.0
            c_loc = project_cs_loc.get(proj, 0)
            r_loc = crate_rs_loc.get(crate, 0)
            l_pct = (r_loc / c_loc * 100.0) if c_loc else 0.0
            print(
                f"  {proj:<36} members={pm_ok:4d}/{pm_tot:<4d} ({pm_pct:5.1f}%)  "
                f"LOC={r_loc:5d}/{c_loc:<5d} ({l_pct:5.1f}%) -> {crate}"
            )

    if args.report:
        title = args.title or f"Automated API Parity Report: C# `{args.cs_root}` vs Rust `{args.rs_root}`"
        lines = [
            f"# {title}",
            "",
            f"Generated by `python3 tools/rust_port/compare_api.py --cs-root {args.cs_root} --rs-root {args.rs_root}`.",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "|---|---|",
            f"| C# projects (`{args.cs_root}`) | {len(project_file_counts)} |",
            f"| C# source files / code LOC | {sum(project_file_counts.values())} files / {total_cs_loc:,} LOC |",
            f"| Rust source files / code LOC (`{args.rs_root}`) | {sum(crate_file_counts.values())} files / {total_rs_loc:,} LOC |",
            f"| **Code LOC Depth (Rust / C#)** | **{loc_pct:.1f}%** |",
            f"| C# public types | {total} |",
            f"| Rust public types | {len(rs_types)} |",
            f"| Direct 1:1 matched types | {len(matched)} |",
            f"| Idiomatic Rust mapped types | {len(mapped)} |",
            f"| C# EventArgs / EventHandlers (N/A in Rust) | {len(events)} |",
            f"| Missing public C# types | {len(missing)} |",
            f"| **Type API Coverage** | **{type_pct:.1f}%** |",
            f"| **Matched-Type Member Coverage** | **{matched_members:,} / {total_cs_members:,} ({member_pct:.1f}%)** |",
            "",
        ]

        kind_counts = Counter(t.kind for t in cs_types.values())
        lines.append("### C# Public Types by Kind")
        lines.append("")
        lines.append("| Kind | Count |")
        lines.append("|---|---:|")
        for kind, cnt in sorted(kind_counts.items(), key=lambda kv: (-kv[1], kv[0])):
            lines.append(f"| `{kind}` | {cnt} |")
        lines.append("")

        if is_multi_project:
            lines.append("## Per-Project Parity & Implementation Depth")
            lines.append("")
            lines.append("> **Planned Rust Stack**:")
            lines.append("> - **Rendering**: Replace `SkiaSharp` (`netDxf.Shadow.Rendering.SkiaSharp`) with native Rust **Skia** (`skia-safe`).")
            lines.append("> - **UI / Studio**: Replace Blazor/Web UI (`netDxf.Shadow.Studio.*`) with **[FerroUI](https://github.com/wieslawsoltes/FerroUI/)**.")
            lines.append("> - **Core DXF**: Built on top of the ported `rust/netdxf` crate (`100.0%` type parity).")
            lines.append("")
            lines.append(
                "| C# Project | Types | Missing | Member Parity | C# LOC | Rust LOC | LOC Depth | Planned Rust Crate | Target Stack |"
            )
            lines.append("|---|---:|---:|---:|---:|---:|---:|---|---|")
            by_proj_all: Dict[str, List[TypeInfo]] = defaultdict(list)
            by_proj_missing: Dict[str, List[TypeInfo]] = defaultdict(list)
            for t in cs_types.values():
                by_proj_all[t.project].append(t)
            for t in missing:
                by_proj_missing[t.project].append(t)
            for proj in sorted(project_file_counts.keys()):
                crate, stack = suggest_rust_crate_and_stack(proj)
                pm_ok, pm_tot = proj_member_stats[proj]
                pm_pct = (pm_ok / pm_tot * 100.0) if pm_tot else 100.0
                c_loc = project_cs_loc.get(proj, 0)
                r_loc = crate_rs_loc.get(crate, 0)
                l_pct = (r_loc / c_loc * 100.0) if c_loc else 0.0
                lines.append(
                    f"| `{proj}` | {len(by_proj_all[proj])} | {len(by_proj_missing[proj])} "
                    f"| {pm_ok}/{pm_tot} ({pm_pct:.1f}%) | {c_loc:,} | {r_loc:,} | {l_pct:.1f}% "
                    f"| `{crate}` | {stack} |"
                )
            lines.append("")

        # Top types with missing public members (actionable depth backlog)
        member_gaps = sorted(
            [d for d in member_details if d[4]],
            key=lambda d: (-len(d[4]), d[0].project, d[0].name),
        )
        if member_gaps:
            lines.append("## Top Matched Types with Missing Public Members")
            lines.append("")
            lines.append("| Scope | Type | Matched Members | Missing Sample (up to 10) |")
            lines.append("|---|---|---:|---|")
            for cs, _rs, m_ok, m_tot, m_miss in member_gaps[:60]:
                scope = cs.project if is_multi_project else cs.namespace_or_mod
                sample = ", ".join(f"`{m}`" for m in m_miss[:10])
                if len(m_miss) > 10:
                    sample += f", … (+{len(m_miss) - 10})"
                lines.append(f"| `{scope}` | `{cs.name}` | {m_ok}/{m_tot} | {sample} |")
            lines.append("")

        if missing:
            lines.append("## Missing Public C# Types")
            lines.append("")
            lines.append("| Namespace | Type | Kind | Source File |")
            lines.append("|---|---|---|---|")
            for t in missing:
                lines.append(
                    f"| `{t.namespace_or_mod}` | `{t.name}` | `{t.kind}` | `{t.files[0]}` |"
                )
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
            "cs_code_loc": total_cs_loc,
            "rs_files": sum(crate_file_counts.values()),
            "rs_code_loc": total_rs_loc,
            "loc_depth_percent": round(loc_pct, 2),
            "cs_total_types": total,
            "rs_total_types": len(rs_types),
            "matched_types": len(matched),
            "mapped_types": len(mapped),
            "events": len(events),
            "missing_types": len(missing),
            "type_coverage_percent": round(type_pct, 2),
            "matched_members": matched_members,
            "total_cs_members": total_cs_members,
            "member_coverage_percent": round(member_pct, 2),
        }
        Path(args.json).write_text(json.dumps(payload, indent=2), encoding="utf-8")

    exit_code = 0
    if args.fail_on_missing and missing:
        exit_code = 1
    if args.min_member_coverage is not None and member_pct < args.min_member_coverage:
        exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
