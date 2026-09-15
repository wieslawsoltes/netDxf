#!/usr/bin/env python3
"""Exact TABLECONTENT/resource/owned-graph extraction into generated carriers.

Original source bytes live in ../table-oracle and remain unchanged. Native content
and dependency handles remain unchanged. Only explicitly listed common carrier
owners/reactors change; generated scaffold handles are moved out of their range.
"""
from pathlib import Path
import gzip
import hashlib
import importlib.util
import io
import json
from collections import Counter
import ezdxf
from ezdxf.lldxf.types import DXFTag

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
spec = importlib.util.spec_from_file_location("section_packets", ROOT.parent / "section-producer" / "extract_fixtures.py")
packet_tools = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packet_tools)
load, write, one, packet_exact = packet_tools.load, packet_tools.write, packet_tools.one, packet_tools.packet_exact


def handle(packet):
    return next((str(t.value).upper() for t in packet if t.code in (5, 105)), None)


def owner(packet):
    depth = 0
    for tag in packet[1:]:
        if tag.code == 100:
            break
        if tag.code == 102:
            depth += 1 if str(tag.value).startswith("{") else -1
        if tag.code == 330 and depth == 0:
            return tag.value
    return None


def semantic(tag):
    return 330 <= tag.code <= 369 or 390 <= tag.code <= 399 or tag.code in (480, 481, 1005)


def all_handle(tag):
    return semantic(tag) or tag.code in (5, 105) or 320 <= tag.code <= 329


def main():
    assert ezdxf.__version__ == "1.4.4"
    ezdxf.options.write_fixed_meta_data_for_testing = True
    source_manifest = json.loads((REPO / "tools/table_oracle/fixtures.json").read_text())
    manifest = {"schema": 1, "extractor": "ezdxf 1.4.4", "original_transformations": [], "files": []}
    for source in source_manifest["files"]:
        if not source["file"].startswith("sample_"):
            manifest["files"].append({"file": source["file"], "source_sha256": source["sha256"], "profile": source["profile"], "scope": "whole unchanged original", "fixture": "../table-oracle/" + source["file"] + ".gz"})
            continue
        original = gzip.decompress((ROOT.parent / "table-oracle" / (source["file"] + ".gz")).read_bytes())
        assert hashlib.sha256(original).hexdigest() == source["sha256"]
        native = load(original)
        by_handle = {handle(p): p for p in native if handle(p)}
        source_sections, current_section = {}, None
        for packet in native:
            if packet[0].value == "SECTION": current_section = packet[1].value
            if handle(packet): source_sections[handle(packet)] = current_section
            if packet[0].value == "ENDSEC": current_section = None

        contents = [p for p in native if p[0] == DXFTag(0, "TABLECONTENT")]
        wrappers = {owner(p) for p in contents}
        selected = set(wrappers)
        selected.update(handle(p) for p in contents)
        external_owners = {owner(by_handle[h]) for h in wrappers}
        styles = {one(p[p.index(DXFTag(100, "AcDbTableContent")):], 340) for p in contents}
        selected.update(styles)
        external_owners.update(owner(by_handle[h]) for h in styles)
        # Native owned descendants, exposed dependencies, and complete referenced block definitions.
        changed = True
        while changed:
            previous = set(selected)
            for key, packet in by_handle.items():
                if owner(packet) in selected:
                    selected.add(key)
            for key in list(selected):
                packet = by_handle[key]
                owner_packet = by_handle.get(owner(packet))
                if owner_packet is not None and owner_packet[0].value == "BLOCK_RECORD":
                    owner_name = one(owner_packet, 2)
                    if not owner_name.lower().startswith(("*model_space", "*paper_space")):
                        selected.add(handle(owner_packet))
                payload_start = next((i for i, tag in enumerate(packet) if tag.code == 100), len(packet))
                # Persistent reactors are real source identities too. Preserve their
                # native entities/graphs, excluding the explicitly replaced outer
                # owner and avoiding table-container expansion.
                for tag in packet[:payload_start]:
                    if semantic(tag) and tag.value != owner(packet) and tag.value != "0" and tag.value not in external_owners:
                        target = by_handle.get(tag.value)
                        if target is not None and target[0].value != "TABLE":
                            selected.add(tag.value)
                for tag in packet[payload_start:]:
                    if semantic(tag) and tag.value != "0" and tag.value not in external_owners:
                        target = by_handle.get(tag.value)
                        if target is not None and target[0].value != "TABLE":
                            selected.add(tag.value)
                # TABLESTYLE's classic text style references are stored by name.
                if packet[0].value == "TABLESTYLE":
                    for tag in packet:
                        if tag.code == 7:
                            for candidate, style in by_handle.items():
                                if style[0].value == "STYLE" and any(t.code == 2 and t.value == tag.value for t in style):
                                    selected.add(candidate)
            changed = previous != selected
        selected_packets = [p for p in native if handle(p) in selected]
        allowed_symbols = {"STYLE", "LTYPE", "BLOCK_RECORD", "LAYER", "APPID"}
        # Table ownership is carrier metadata, never a recursive reason to import an entire table.
        assert not any(p[0].value == "TABLE" for p in selected_packets)
        doc = ezdxf.new(source["profile"])
        stream = io.StringIO(); doc.write(stream)
        generated = load(stream.getvalue().encode("utf-8"))
        for packet in generated:
            for i, tag in enumerate(packet):
                if all_handle(tag) and tag.value != "0":
                    packet[i] = DXFTag(tag.code, format(int(tag.value, 16) + 0xF00000, "X"))
        table_owners = {one(p, 2): handle(p) for p in generated if p[0].value == "TABLE"}
        root_packet = next(p for p in generated if p[0].value == "DICTIONARY" and owner(p) == "0")
        root_handle = handle(root_packet)
        replacements = {}
        for packet in selected_packets:
            if packet[0].value not in allowed_symbols:
                continue
            name = next(t.value for t in packet if t.code == 2)
            existing = next((p for p in generated if p[0].value == packet[0].value and any(t.code == 2 and t.value == name for t in p)), None)
            if existing is not None:
                replacements[handle(existing)] = handle(packet)
                generated.remove(existing)
        for packet in generated:
            for i, tag in enumerate(packet):
                if all_handle(tag) and tag.value in replacements:
                    packet[i] = DXFTag(tag.code, replacements[tag.value])
        model_handle = next(handle(p) for p in generated if p[0].value == "BLOCK_RECORD" and one(p, 2).lower() == "*model_space")
        source_model_handles = {handle(p) for p in native if p[0].value == "BLOCK_RECORD" and one(p, 2).lower() == "*model_space"}
        modifications = []
        imported = []
        for packet in selected_packets:
            copy = list(packet); key = handle(packet); changes = []
            new_owner = root_handle if key in styles or key in wrappers and owner(packet) not in selected else model_handle if owner(packet) in source_model_handles else table_owners.get(packet[0].value)
            if new_owner is not None:
                old_owner = owner(packet)
                boundary = next((i for i, tag in enumerate(copy) if tag.code == 100), len(copy))
                for i in range(boundary):
                    if copy[i].code == 330 and copy[i].value == old_owner:
                        changes.append({"index": i, "code": 330, "before": old_owner, "after": new_owner})
                        copy[i] = DXFTag(330, new_owner)
            imported.append(copy)
            modifications.append({"handle": key, "type": packet[0].value, "changes": changes})
        for index, key in enumerate(sorted((key for key in wrappers if owner(by_handle[key]) not in selected), key=lambda value: int(value, 16))):
            root_packet.extend([DXFTag(3, "CONTENT_WRAPPER_" + str(index)), DXFTag(350, key)])
        for index, key in enumerate(sorted(styles, key=lambda value: int(value, 16))):
            root_packet.extend([DXFTag(3, "CONTENT_STYLE_" + str(index)), DXFTag(350, key)])
        symbols = {name: [p for p in imported if p[0].value == name] for name in allowed_symbols}
        blocks = [p for p in imported if source_sections[handle(p)] == "BLOCKS"]
        entities = [p for p in imported if source_sections[handle(p)] == "ENTITIES"]
        objects = [p for p in imported if source_sections[handle(p)] == "OBJECTS"]
        assert len(blocks) + len(entities) + len(objects) + sum(len(values) for values in symbols.values()) == len(imported)
        object_counts = Counter(p[0].value for p in objects)
        classes = []
        for packet in native:
            if packet[0].value == "CLASS" and one(packet, 1) in object_counts:
                classes.append([DXFTag(t.code, object_counts[one(packet, 1)]) if t.code == 91 else t for t in packet])
        result, section, table = [], None, None
        for packet in generated:
            if packet[0].value == "SECTION": section = next(t.value for t in packet if t.code == 2)
            if packet[0].value == "TABLE": table = one(packet, 2)
            if packet[0].value == "ENDTAB": result.extend(symbols.get(table, [])); table = None
            if packet[0].value == "ENDSEC":
                if section == "CLASSES": result.extend(sorted(classes, key=lambda p: one(p, 1)))
                if section == "BLOCKS": result.extend(blocks)
                if section == "ENTITIES": result.extend(entities)
                if section == "OBJECTS": result.extend(objects)
                section = None
            if packet[0].value == "CLASS" and one(packet, 1) in object_counts: continue
            result.append(packet)
        # ezdxf may enumerate generated CLASS definitions from a hash set.
        class_start = next(i for i, p in enumerate(result) if p[0].value == "SECTION" and p[1] == DXFTag(2, "CLASSES")) + 1
        class_end = next(i for i in range(class_start, len(result)) if result[i][0].value == "ENDSEC")
        result[class_start:class_end] = sorted(result[class_start:class_end], key=lambda p: one(p, 1))
        data = write(result, source["profile"], False)
        output = ROOT / "extracted" / source["file"]
        output.write_bytes(data)
        recovered = {handle(p): p for p in load(data) if handle(p)}
        for record in modifications:
            expected = list(by_handle[record["handle"]])
            for change in record["changes"]: expected[change["index"]] = DXFTag(change["code"], change["after"])
            assert packet_exact(expected) == packet_exact(recovered[record["handle"]]), record["handle"]
            for tag in recovered[record["handle"]]:
                if semantic(tag) and int(tag.value, 16) != 0:
                    assert tag.value in recovered, (record["handle"], "missing retained physical reference", tag.code, tag.value)
        audit = ezdxf.read(io.StringIO(data.decode("utf-8"))).audit()
        assert not audit.errors and not audit.fixes, ([str(e) for e in audit.errors], [str(e) for e in audit.fixes])
        manifest["files"].append({"file": source["file"], "source_sha256": source["sha256"], "profile": source["profile"], "scope": "exact selected native packets with explicit carrier ownership changes", "fixture": "extracted/" + source["file"], "sha256": hashlib.sha256(data).hexdigest(), "content_handles": [handle(p) for p in contents], "records": modifications, "audit_errors": 0, "audit_repairs": 0})
        print(source["file"], len(contents), "contents", len(imported), "native records")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
