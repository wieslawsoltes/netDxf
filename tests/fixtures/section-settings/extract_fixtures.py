#!/usr/bin/env python3
"""Copy pinned SECTIONSETTINGS packets, with explicit handle maps, into clean independent carriers."""
from pathlib import Path
import gzip, hashlib, io, json, sys, tempfile
import ezdxf
from ezdxf.entities import DXFClass
from ezdxf.lldxf.tags import DXFTag
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import BinaryTagWriter, TagWriter
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[2] / "tools"))
from verify_typed_container_inputs import wire_records

def packets(tags):
    result, current = [], []
    for tag in tags:
        if tag.code == 0 and current: result.append(current); current = []
        current.append(tag)
    if current: result.append(current)
    return result

def main():
    assert ezdxf.__version__ == "1.4.4"
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = json.loads((ROOT / "manifest.json").read_text())
    for fixture in manifest["fixtures"]:
        packed = (ROOT / fixture["file"]).read_bytes()
        assert hashlib.sha256(packed).hexdigest() == fixture["gzip_sha256"]
        original = gzip.decompress(packed); assert hashlib.sha256(original).hexdigest() == fixture["sha256"]
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "source.dxf"; source.write_bytes(original)
            old = ezdxf.readfile(source); raw = wire_records(source)
            settings = old.rootdict["QA_SECTION_SETTINGS_PRODUCER"]
            doc = ezdxf.new(old.dxfversion)
            for line in old.modelspace().query("LINE"): doc.modelspace().add_line(line.dxf.start, line.dxf.end)
            placeholder = doc.objects.add_dxf_object_with_reactor("ACDBPLACEHOLDER", {"owner": doc.rootdict.dxf.handle})
            doc.rootdict.add("QA_SECTION_SETTINGS_PRODUCER", placeholder)
            mapping = {old.rootdict.dxf.handle: doc.rootdict.dxf.handle, settings.dxf.handle: placeholder.dxf.handle}
            replacement = [DXFTag(tag.code, mapping.get(tag.value, tag.value)) if tag.code in (5, 330, 331) else tag for tag in raw[settings.dxf.handle]]
            doc.classes.register(DXFClass.new(dxfattribs={"name":"SECTIONSETTINGS", "cpp_class_name":"AcDbSectionSettings", "app_name":"ObjectDBX Classes", "flags":1024, "was_a_proxy":0, "is_an_entity":0, "instance_count":1}))
            stream = io.StringIO(); doc.write(stream)
            content = packets(tag_compiler(ascii_tags_loader(io.StringIO(stream.getvalue()))))
            slots = [i for i, packet in enumerate(content) if packet[0].value == "CLASS"]
            ordered = sorted((content[i] for i in slots), key=lambda packet: next(tag.value for tag in packet if tag.code == 1))
            for slot, packet in zip(slots, ordered): content[slot] = packet
            for i, packet in enumerate(content):
                if next((tag.value for tag in packet if tag.code == 5), None) == placeholder.dxf.handle: content[i] = replacement
            binary = "-binary." in fixture["file"]
            stream = io.BytesIO() if binary else io.StringIO()
            writer = BinaryTagWriter(stream, dxfversion=doc.dxfversion, encoding=doc.output_encoding) if binary else TagWriter(stream, dxfversion=doc.dxfversion)
            if binary: writer.write_signature()
            for packet in content:
                for tag in packet:
                    if tag.code != 999: writer.write_tag(tag)
            output = stream.getvalue(); output = output.encode(doc.output_encoding) if isinstance(output, str) else output
            target = ROOT / fixture["file"].replace("ixmilia-", "extracted-").removesuffix(".gz"); target.write_bytes(output)
            assert wire_records(target)[placeholder.dxf.handle] == replacement
            audit = ezdxf.readfile(target).audit(); assert not audit.errors and not audit.fixes
            fixture.update(extracted_file=target.name, extracted_sha256=hashlib.sha256(output).hexdigest(), handle_map=mapping, application_packets=[settings.dxf.handle])
            print("PASS", target.name, "exact source packet and zero audit repairs")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")

if __name__ == "__main__": main()
