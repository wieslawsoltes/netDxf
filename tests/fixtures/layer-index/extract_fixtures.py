#!/usr/bin/env python3
"""Extract pinned IxMilia application packets into independent ezdxf scaffolding.

The original 12 files remain unchanged in gzip archives. Only declared handles
are remapped in the six LAYER_INDEX/IDBUFFER packets. Unrelated malformed STYLE
and DIMSTYLE fields in the original drawing are not used as application evidence.
"""
from pathlib import Path
import gzip
import hashlib
import io
import json
import sys
import tempfile

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
        if tag.code == 0 and current:
            result.append(current)
            current = []
        current.append(tag)
    if current:
        result.append(current)
    return result


def main():
    assert ezdxf.__version__ == "1.4.4"
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = json.loads((ROOT / "manifest.json").read_text())
    for fixture in manifest["fixtures"]:
        original = gzip.decompress((ROOT / fixture["file"]).read_bytes())
        assert hashlib.sha256(original).hexdigest() == fixture["sha256"]
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "source.dxf"
            source.write_bytes(original)
            raw = wire_records(source)
            original_doc = ezdxf.readfile(source)
            original_parent = original_doc.rootdict["QA_LAYER_INDEX"]
            original_index = original_parent["INDEX"]
            original_empty = original_parent["EMPTY"]
            original_lines = list(original_doc.modelspace().query("LINE"))
            buffers = [tag.value for tag in raw[original_index.dxf.handle] if tag.code == 360]
            doc = ezdxf.new(original_doc.dxfversion)
            lines = [doc.modelspace().add_line(line.dxf.start, line.dxf.end) for line in original_lines]
            parent = doc.rootdict.add_new_dict("QA_LAYER_INDEX", hard_owned=True)
            index = doc.objects.add_dxf_object_with_reactor("ACDBPLACEHOLDER", {"owner": parent.dxf.handle})
            empty = doc.objects.add_dxf_object_with_reactor("ACDBPLACEHOLDER", {"owner": parent.dxf.handle})
            parent.add("INDEX", index)
            parent.add("EMPTY", empty)
            children = [doc.objects.add_dxf_object_with_reactor("ACDBPLACEHOLDER", {"owner": index.dxf.handle}) for _ in buffers]
            mapping = {original_parent.dxf.handle: parent.dxf.handle,
                       original_index.dxf.handle: index.dxf.handle,
                       original_empty.dxf.handle: empty.dxf.handle}
            mapping.update((before.dxf.handle, after.dxf.handle) for before, after in zip(original_lines, lines))
            mapping.update((before, after.dxf.handle) for before, after in zip(buffers, children))
            for name, cpp, count in [("LAYER_INDEX", "AcDbLayerIndex", 2), ("IDBUFFER", "AcDbIdBuffer", 4)]:
                doc.classes.register(DXFClass.new(dxfattribs={"name": name, "cpp_class_name": cpp,
                    "app_name": "ObjectDBX Classes", "flags": 0, "was_a_proxy": 0,
                    "is_an_entity": 0, "instance_count": count}))
            stream = io.StringIO()
            doc.write(stream)
            content = packets(tag_compiler(ascii_tags_loader(io.StringIO(stream.getvalue()))))
            class_slots = [i for i, packet in enumerate(content) if packet[0].value == "CLASS"]
            ordered_classes = sorted((content[i] for i in class_slots),
                key=lambda packet: next(tag.value for tag in packet if tag.code == 1))
            for slot, packet in zip(class_slots, ordered_classes):
                content[slot] = packet
            originals = {original_index.dxf.handle, original_empty.dxf.handle, *buffers}
            replacements = {mapping[handle]: [DXFTag(tag.code, mapping.get(tag.value, tag.value))
                if tag.code in (5, 330, 360) else tag for tag in raw[handle]] for handle in originals}
            for i, packet in enumerate(content):
                handle = next((tag.value for tag in packet if tag.code == 5), None)
                if handle in replacements:
                    content[i] = replacements[handle]
            binary = "-binary." in fixture["file"]
            target = ROOT / fixture["file"].replace("ixmilia-", "extracted-").removesuffix(".gz")
            if binary:
                stream = io.BytesIO()
                writer = BinaryTagWriter(stream, dxfversion=doc.dxfversion, encoding=doc.output_encoding)
                writer.write_signature()
            else:
                stream = io.StringIO()
                writer = TagWriter(stream, dxfversion=doc.dxfversion)
            for packet in content:
                for tag in packet:
                    if tag.code != 999:
                        writer.write_tag(tag)
            output = stream.getvalue()
            if isinstance(output, str):
                output = output.encode(doc.output_encoding)
            target.write_bytes(output)
            observed = wire_records(target)
            for handle in originals:
                assert observed[mapping[handle]] == replacements[mapping[handle]], "Source packet changed beyond explicit handles"
            audit = ezdxf.readfile(target).audit()
            assert not audit.errors and not audit.fixes, (audit.errors, audit.fixes)
            fixture["extracted_file"] = target.name
            fixture["extracted_sha256"] = hashlib.sha256(output).hexdigest()
            fixture["handle_map"] = mapping
            fixture["application_packets"] = sorted(originals)
            print("PASS", target.name, "six exact mapped source packets; no audit fixes")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
