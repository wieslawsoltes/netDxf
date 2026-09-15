#!/usr/bin/env python3
"""Exact native POLYFACE chains in declared neutral carriers; no native CAD execution."""
import gzip
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import ezdxf
from ezdxf.lldxf.types import DXFTag

ROOT = Path(__file__).resolve().parent
source_root = ROOT.parent / 'dimassoc'
spec = importlib.util.spec_from_file_location('native_extractor', source_root / 'extract_fixtures.py')
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def main():
    assert ezdxf.__version__ == '1.4.4'
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {'contract': 'All eleven native POLYFACE packets preserved except the declared parent owner. Named carrier layers are synthetic, not original layer-table qualification.', 'native_application_executed': False, 'fixtures': []}
    for source in json.loads((source_root / 'source-manifest.json').read_text())['sources']:
        compressed = (source_root / 'originals-gzip' / source['gzip_file']).read_bytes()
        original = gzip.decompress(compressed)
        assert hashlib.sha256(compressed).hexdigest() == source['gzip_sha256']
        assert hashlib.sha256(original).hexdigest() == source['source_sha256']
        packets = native.packets(original)
        start = next(i for i, packet in enumerate(packets) if native.identity(packet) == '4E4')
        selected = []
        for packet in packets[start:]:
            selected.append(packet)
            if packet[0].value == 'SEQEND': break
        assert len(selected) == 11
        doc = ezdxf.new(f"R{source['year']}")
        for packet in selected:
            for tag in packet:
                if tag.code == 8 and tag.value not in doc.layers: doc.layers.new(tag.value)
        placeholder = doc.modelspace().add_line((0, 0, 0), (1, 0, 0)).dxf.handle
        owner = doc.modelspace().block_record_handle
        mapped = [[DXFTag(330, owner) if j == 0 and tag.code == 330 else tag for tag in packet] for j, packet in enumerate(selected)]
        stream = io.StringIO(); doc.write(stream)
        carrier = []
        for packet in native.packets(stream.getvalue().encode('utf-8')):
            if native.identity(packet) == placeholder: carrier.extend(mapped); continue
            for i, tag in enumerate(packet[:-1]):
                if tag == DXFTag(9, '$HANDSEED'): packet[i + 1] = DXFTag(5, '5000')
            carrier.append(packet)
        data = native.write(carrier, doc.dxfversion)
        path = ROOT / f"native-R{source['year']}.dxf"; path.write_bytes(data)
        actual = {native.identity(p): p for p in native.packets(data) if native.identity(p)}
        for packet in mapped: assert native.exact(packet) == native.exact(actual[native.identity(packet)])
        audit = ezdxf.readfile(path).audit(); assert not audit.errors and not audit.fixes
        manifest['fixtures'].append({'year': source['year'], 'file': path.name, 'sha256': hashlib.sha256(data).hexdigest(), 'source': source, 'parent': '4E4', 'coordinates': [f'{i:X}' for i in range(0x4E5, 0x4EB)], 'faces': ['4EB', '4EC', '4ED'], 'seqend': '4EE', 'native_owner': '1F', 'carrier_owner': owner, 'packet_count': 11})
    (ROOT / 'native-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__': main()
