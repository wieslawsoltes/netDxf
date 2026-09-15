#!/usr/bin/env python3
"""Preserve IxMilia SECTION packets in deterministic, explicitly adapted carriers.

Original bytes are never modified. Carrier extraction changes only the entity's
identity 1D to F1000 and inserts a missing common owner pointing at the generated
model-space BLOCK_RECORD. Every other common field and the complete ordered
AcDbSection body remain exact. A temporary SECTION -> SECTIONOBJECT type-name
adapter is used only in memory for ezdxf structural audit; stored files retain
the documented SECTION entity spelling.
"""
from pathlib import Path
import gzip
import hashlib
import io
import json
import struct
import ezdxf
from ezdxf.entities import DXFClass
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import BinaryTagWriter, TagWriter
from ezdxf.lldxf.types import DXFTag

ROOT = Path(__file__).resolve().parent


def packets(tags):
    result, current = [], []
    for tag in tags:
        if tag.code == 0 and current:
            result.append(current)
            current = []
        current.append(tag)
    if current:
        result.append(current)
    return result


def load(data):
    loader = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    return packets(tag_compiler(loader))


def one(tags, code):
    values = [tag.value for tag in tags if tag.code == code]
    assert len(values) == 1, (code, values)
    return values[0]


def body(packet):
    return packet[packet.index(DXFTag(100, "AcDbSection")):]


def exact(value):
    if isinstance(value, float):
        return struct.pack("<d", value)
    if isinstance(value, (tuple, list)):
        return tuple(exact(v) for v in value)
    return value


def packet_exact(packet):
    return [(tag.code, exact(tag.value)) for tag in packet]


def write(content, version, binary):
    stream = io.BytesIO() if binary else io.StringIO()
    writer = BinaryTagWriter(stream, dxfversion=version, encoding="utf-8") if binary else TagWriter(stream, dxfversion=version)
    if binary:
        writer.write_signature()
    for packet in content:
        for tag in packet:
            if tag.code != 999:
                writer.write_tag(tag)
    result = stream.getvalue()
    return result.encode("utf-8") if isinstance(result, str) else result


def audit_adapted(data, version):
    content = load(data)
    changes = {"entity_type": 0, "class_name": 0}
    for packet in content:
        if packet[0] == DXFTag(0, "SECTION") and DXFTag(100, "AcDbSection") in packet:
            packet[0] = DXFTag(0, "SECTIONOBJECT")
            changes["entity_type"] += 1
        if packet[0] == DXFTag(0, "CLASS") and DXFTag(1, "SECTION") in packet:
            packet[packet.index(DXFTag(1, "SECTION"))] = DXFTag(1, "SECTIONOBJECT")
            changes["class_name"] += 1
    assert changes == {"entity_type": 1, "class_name": 1}
    adapted = write(content, version, False)
    doc = ezdxf.read(io.StringIO(adapted.decode("utf-8")))
    audit = doc.audit()
    assert not audit.errors and not audit.fixes, (audit.errors, audit.fixes)
    return changes


def main():
    assert ezdxf.__version__ == "1.4.4"
    ezdxf.options.write_fixed_meta_data_for_testing = True
    destination = ROOT / "extracted"
    destination.mkdir(exist_ok=True)
    gzip_root = ROOT / "originals-gzip"
    gzip_root.mkdir(exist_ok=True)
    source_manifest = json.loads((ROOT / "source-manifest.json").read_text())
    manifest = {
        "producer": source_manifest["producer"],
        "schema_source_commit": source_manifest["schema_source_commit"],
        "package_sha256": source_manifest["package_sha256"],
        "extractor": "ezdxf 1.4.4",
        "original_whole_file_import_qualified": False,
        "original_transformations": [],
        "extraction_contract": "Exact source entity packet except identity 1D->F1000 and insertion of absent common owner330 to generated model-space BLOCK_RECORD; all ordered AcDbSection fields and other common fields retained.",
        "audit_contract": "Only an in-memory entity type and corresponding CLASS name SECTION->SECTIONOBJECT adapter is used for ezdxf audit. Stored extraction files retain SECTION. No field values, counts, geometry, ownership or settings are repaired for audit.",
        "fixtures": [],
    }
    for fixture in source_manifest["files"]:
        path = ROOT / "originals" / fixture["file"]
        original = gzip.decompress((gzip_root / (path.name + ".gz")).read_bytes())
        assert hashlib.sha256(original).hexdigest() == fixture["sha256"]
        packed = gzip.compress(original, mtime=0)
        gzip_path = gzip_root / (path.name + ".gz")
        gzip_path.write_bytes(packed)
        source = [p for p in load(original) if p[0] == DXFTag(0, "SECTION") and DXFTag(100, "AcDbSection") in p]
        assert len(source) == 1
        source = source[0]
        assert one(source, 5) == "1D"
        common = source[:source.index(DXFTag(100, "AcDbEntity"))]
        assert not any(t.code == 330 for t in common)
        year = int(path.name.split("-R")[1][:4])
        binary = "-binary." in path.name
        doc = ezdxf.new("R" + str(year))
        placeholder = doc.modelspace().add_line((0, 0, 0), (1, 0, 0))
        placeholder_handle = placeholder.dxf.handle
        model_owner = doc.modelspace().block_record_handle
        assert "F1000" not in doc.entitydb
        replacement = [DXFTag(t.code, "F1000") if t.code == 5 else t for t in source]
        replacement.insert(replacement.index(DXFTag(100, "AcDbEntity")), DXFTag(330, model_owner))
        doc.classes.register(DXFClass.new(dxfattribs={"name": "SECTION", "cpp_class_name": "AcDbSection", "app_name": "ObjectDBX Classes", "flags": 1025, "was_a_proxy": 0, "is_an_entity": 1, "instance_count": 1}))
        stream = io.StringIO()
        doc.write(stream)
        content = load(stream.getvalue().encode("utf-8"))
        for i, packet in enumerate(content):
            if any(t.code == 5 and t.value == placeholder_handle for t in packet):
                content[i] = replacement
            if packet[0] == DXFTag(0, "CLASS") and DXFTag(1, "SECTION") in packet:
                count_index = next(j for j, t in enumerate(packet) if t.code == 91)
                packet[count_index] = DXFTag(91, 1)
            for j, tag in enumerate(packet[:-1]):
                if tag == DXFTag(9, "$HANDSEED"):
                    assert packet[j + 1].code == 5
                    packet[j + 1] = DXFTag(5, "F1001")
        slots = [i for i, p in enumerate(content) if p[0] == DXFTag(0, "CLASS")]
        ordered = sorted((content[i] for i in slots), key=lambda p: one(p, 1))
        for slot, packet in zip(slots, ordered):
            content[slot] = packet
        result = write(content, doc.dxfversion, binary)
        extracted = destination / path.name.replace("ixmilia-", "extracted-")
        extracted.write_bytes(result)
        actual = [p for p in load(result) if p[0] == DXFTag(0, "SECTION") and DXFTag(100, "AcDbSection") in p]
        assert len(actual) == 1
        assert packet_exact(actual[0]) == packet_exact(replacement)
        assert packet_exact(body(actual[0])) == packet_exact(body(source))
        adapted = audit_adapted(result, doc.dxfversion)
        manifest["fixtures"].append({
            "file": path.name,
            "source_sha256": hashlib.sha256(original).hexdigest(),
            "source_bytes": len(original),
            "gzip_file": gzip_path.name,
            "gzip_sha256": hashlib.sha256(packed).hexdigest(),
            "extracted_file": extracted.name,
            "extracted_sha256": hashlib.sha256(result).hexdigest(),
            "profile": doc.dxfversion,
            "binary": binary,
            "handle_map": {"1D": "F1000"},
            "inserted_common_owner": {"code": 330, "value": model_owner, "reason": "The original entity has no owner; the generated carrier places it in model space."},
            "exact_ordered_body": True,
            "all_other_common_fields_unchanged": True,
            "audit_type_name_changes": adapted,
            "audit_errors": 0,
            "audit_repairs": 0,
        })
        print("PASS", extracted.name, "exact source packet except mapped identity/inserted owner; adapted audit has zero errors/repairs")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
