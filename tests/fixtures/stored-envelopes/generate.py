#!/usr/bin/env python3
"""Generate public-schema envelopes; these are synthetic, never native VBA or spatial-index data."""
from pathlib import Path
import hashlib
import io
import json
import struct
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.lldxf.types import DXFTag, DXFBinaryTag
from ezdxf.lldxf.tagwriter import TagWriter, BinaryTagWriter

ROOT = Path(__file__).resolve().parent
YEARS = (2000, 2004, 2007, 2010, 2013, 2018)
PAYLOAD = bytes((i * 37 + 11) % 256 for i in range(300))
CHUNKS = [b"", PAYLOAD[:127], PAYLOAD[127:130], b"", PAYLOAD[130:257], PAYLOAD[257:], b""]


def records(tags):
    current = []
    for tag in tags:
        if tag.code == 0:
            if current:
                yield current
            current = []
        current.append(tag)
    if current:
        yield current


def generate():
    assert ezdxf.__version__ == "1.4.4"
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {"producer": "ezdxf 1.4.4 plus explicit public-schema tag patch", "qualification": "synthetic stored envelopes, no native CAD corpus", "sources": []}
    for kind in ("spatial", "vba"):
        for year in YEARS:
            doc = ezdxf.new(f"R{year}")
            doc.header["$TDCREATE"] = 2451544.5
            doc.header["$TDUPDATE"] = 2451544.5
            doc.appids.new("QA_STORED_ENVELOPES")
            line = doc.modelspace().add_line((1, 2, 3), (8, 13, 21))
            graph = doc.objects.rootdict.add_new_dict("QA_STORED_ENVELOPES", hard_owned=True)
            targets = []
            values = (2451544.5000000005, -0.0) if kind == "spatial" else (CHUNKS, [], [b"", b""])
            if kind == "spatial":
                doc.classes.add_class("SPATIAL_INDEX")
            for index, value in enumerate(values):
                # SPATIAL_INDEX has no ezdxf typed producer; the registered XRECORD is a common-metadata shell only.
                item = doc.objects.add_xrecord(owner=graph.dxf.handle) if kind == "spatial" else doc.objects.add_dxf_object_with_reactor("VBA_PROJECT", {"owner": graph.dxf.handle})
                if kind == "vba":
                    item.data = b"".join(value)
                item.set_reactors([graph.dxf.handle])
                graph.add(f"ITEM_{index}", item)
                item.set_xdata("QA_STORED_ENVELOPES", [(1000, "literal \\U+0041"), (1005, line.dxf.handle), (1070, index)])
                item.new_extension_dict().add_xrecord("NOTE").reset([(1, f"extension {index}"), (90, 77 + index)])
                targets.append({"handle": item.dxf.handle, "name": f"ITEM_{index}", "timestamp_hex": struct.pack(">d", value).hex() if kind == "spatial" else None,
                                "chunks": [chunk.hex() for chunk in value] if kind == "vba" else None,
                                "extension": item.get_extension_dict().dictionary.dxf.handle})
            following = doc.objects.add_xrecord(owner=graph.dxf.handle)
            following.reset([(1, "following envelope"), (90, 1234567)])
            graph.add("FOLLOWING", following)
            stream = io.StringIO(); doc.write(stream)
            source_records = list(records(list(tag_compiler(ascii_tags_loader(io.StringIO(stream.getvalue()))))))
            # ezdxf adds some automatic CLASS definitions through set iteration; pin their order.
            class_positions = [i for i, record in enumerate(source_records) if record[0].value == "CLASS"]
            ordered_classes = sorted((source_records[i] for i in class_positions), key=lambda record: next(tag.value for tag in record if tag.code == 1))
            for position, record in zip(class_positions, ordered_classes):
                source_records[position] = record
            by_handle = {entry["handle"]: (entry, value) for entry, value in zip(targets, values)}
            for record in source_records:
                handle = next((tag.value for tag in record if tag.code == 5), None)
                if handle not in by_handle:
                    continue
                entry, value = by_handle[handle]
                first = next(i for i, tag in enumerate(record) if tag.code == 100)
                xdata = next(i for i, tag in enumerate(record) if tag.code == 1001)
                prefix, suffix = record[:first], record[xdata:]
                if kind == "spatial":
                    prefix[0] = DXFTag(0, "SPATIAL_INDEX")
                    payload = [DXFTag(100, "AcDbIndex"), DXFTag(40, value), DXFTag(100, "AcDbSpatialIndex")]
                else:
                    # The native VBA exporter rechunks at 127 bytes. This explicit patch intentionally qualifies
                    # noncanonical short and empty physical chunks while retaining its count and concatenated data.
                    payload = [DXFTag(100, "AcDbVbaProject"), DXFTag(90, sum(map(len, value)))] + [DXFBinaryTag(310, chunk) for chunk in value]
                record[:] = prefix + payload + suffix
            for binary in (False, True):
                path = ROOT / f"independent-stored-{kind}-R{year}-{'binary' if binary else 'ascii'}.dxf"
                output = io.BytesIO() if binary else io.StringIO()
                writer = BinaryTagWriter(output, dxfversion=doc.dxfversion) if binary else TagWriter(output, dxfversion=doc.dxfversion)
                if binary:
                    writer.write_signature()
                for record in source_records:
                    for tag in record:
                        # ezdxf's binary writer skips empty byte strings. Preserve this explicit raw boundary.
                        if binary and tag.code == 310 and not tag.value:
                            output.write(struct.pack("<HB", 310, 0))
                        else:
                            writer.write_tag(tag)
                path.write_bytes(output.getvalue() if binary else output.getvalue().encode('utf-8'))
                check = ezdxf.readfile(path)
                audit = check.audit()
                assert not audit.errors and not audit.fixes, (path.name, audit.errors, audit.fixes)
                if kind == "vba":
                    assert [check.entitydb[entry["handle"]].data for entry in targets] == [b"".join(chunks) for chunks in values]
                manifest["sources"].append({"filename": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "version": doc.dxfversion,
                    "kind": kind, "binary": binary, "root": doc.objects.rootdict.dxf.handle, "owner": graph.dxf.handle,
                    "following": following.dxf.handle, "line": line.dxf.handle, "targets": targets})
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Generated {len(manifest['sources'])} synthetic inputs; all parse with zero ezdxf audit errors or repairs.")


if __name__ == "__main__":
    generate()
