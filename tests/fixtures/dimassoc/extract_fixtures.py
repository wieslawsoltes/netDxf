#!/usr/bin/env python3
"""Extract exact native DIMASSOC components into explicit neutral carriers.

All association, dictionary, dimension and referenced geometry packets retain
their source values and order except declared handle mapping. Generated layer
and dimension-style resources and empty dimension display blocks supply the
names used by source entities; this does not qualify original rendered geometry.
"""
from pathlib import Path
import gzip, hashlib, io, json, struct
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import TagWriter
from ezdxf.lldxf.types import DXFTag

ROOT = Path(__file__).resolve().parent
ASSOCIATIONS = ['3B4', '42F', '452', '466', '47E', '496', '4AE', '4C6']

def packets(data):
    try: text = data.decode('utf-8-sig')
    except UnicodeDecodeError: text = data.decode('cp1252')
    result, packet = [], []
    for tag in tag_compiler(ascii_tags_loader(io.StringIO(text, newline=None))):
        if tag.code == 0 and packet:
            result.append(packet); packet = []
        packet.append(tag)
    if packet: result.append(packet)
    return result

def identity(packet):
    for tag in packet:
        if tag.code == 100: break
        if tag.code == 5: return tag.value

def one(packet, code):
    values = [t.value for t in packet if t.code == code]
    assert len(values) == 1, (code, values)
    return values[0]

def owner(packet):
    depth = 0
    for tag in packet:
        if tag.code == 100: break
        if tag.code == 102: depth += 1 if tag.value.startswith('{') else -1
        elif tag.code == 330 and depth == 0: return tag.value

def write(content, version):
    stream = io.StringIO(); writer = TagWriter(stream, dxfversion=version)
    for packet in content:
        for tag in packet:
            if tag.code != 999: writer.write_tag(tag)
    return stream.getvalue().encode('utf-8')

def exact(packet):
    def value(v):
        if isinstance(v, float): return struct.pack('<d', v)
        if isinstance(v, (list, tuple)): return tuple(value(x) for x in v)
        return v
    return [(t.code, value(t.value)) for t in packet]

def main():
    assert ezdxf.__version__ == '1.4.4'
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {'extractor': 'ezdxf 1.4.4', 'original_whole_file_import_qualified': False,
        'contract': 'Exact selected native association, owner dictionary, dimension and source geometry packets modulo declared handle map; generated named layers/styles and empty dimension display blocks are synthetic carrier resources.', 'fixtures': []}
    for source in json.loads((ROOT/'source-manifest.json').read_text())['sources']:
        packed = (ROOT/'originals-gzip'/source['gzip_file']).read_bytes(); original = gzip.decompress(packed)
        assert hashlib.sha256(packed).hexdigest() == source['gzip_sha256']
        assert hashlib.sha256(original).hexdigest() == source['source_sha256']
        old = packets(original); by_handle = {identity(p): p for p in old if identity(p)}
        selected = set(ASSOCIATIONS); dictionaries = []; dimensions = []; geometry = set()
        for handle in ASSOCIATIONS:
            packet = by_handle[handle]; dictionaries.append(owner(packet))
            body = packet[packet.index(DXFTag(100, 'AcDbDimAssoc')) + 1:]
            dimensions.append(one(body, 330)); geometry.update(t.value for t in body if t.code == 331)
        selected.update(dictionaries); selected.update(dimensions); selected.update(geometry)
        # Include the actual POLYLINE's owned VERTEX and SEQEND packets. Their
        # identities remain raw evidence; netDxf deliberately leaves 42F opaque.
        for packet in old:
            if packet[0].value in ('VERTEX', 'SEQEND') and owner(packet) in geometry: selected.add(identity(packet))
        application = [p for p in old if identity(p) in selected]
        entities = [p for p in application if p[0].value not in ('DIMASSOC', 'DICTIONARY')]
        doc = ezdxf.new('R'+str(source['year']))
        for packet in entities:
            for t in packet:
                if t.code == 8 and t.value not in doc.layers: doc.layers.new(t.value)
            if packet[0].value in ('DIMENSION', 'ARC_DIMENSION'):
                name = one(packet, 2)
                if name not in doc.blocks: doc.blocks.new(name)
                style = one(packet, 3)
                if style not in doc.dimstyles: doc.dimstyles.new(style)
        placeholder = doc.modelspace().add_line((0,0,0),(1,0,0)); placeholder_handle = placeholder.dxf.handle
        mapping = {handle: format(0xF1000+i, 'X') for i, handle in enumerate(sorted(selected, key=lambda h: int(h,16)))}
        original_owners = {owner(p) for p in entities if p[0].value not in ('VERTEX','SEQEND')}
        assert original_owners == {'1F'}
        mapping['1F'] = doc.modelspace().block_record_handle
        def mapped(packet):
            result = []
            for tag in packet:
                handle = tag.code in (5,105,1005) or 320 <= tag.code <= 369 or 390 <= tag.code <= 399 or tag.code in (480,481)
                result.append(DXFTag(tag.code, mapping.get(tag.value,tag.value)) if handle else tag)
            return result
        translated = [mapped(p) for p in application]
        stream = io.StringIO(); doc.write(stream); content = packets(stream.getvalue().encode('utf-8'))
        output = []; section = None
        for packet in content:
            if packet[0] == DXFTag(0,'SECTION'): section = packet[1].value
            if identity(packet) == placeholder_handle: output.extend(mapped(p) for p in entities); continue
            if section == 'OBJECTS' and packet[0] == DXFTag(0,'ENDSEC'):
                output.extend(mapped(p) for p in application if p[0].value in ('DIMASSOC','DICTIONARY'))
            if section == 'CLASSES' and packet[0] == DXFTag(0,'ENDSEC'):
                output.extend(p for p in old if p[0] == DXFTag(0,'CLASS') and one(p,1) in ('DIMASSOC','ARC_DIMENSION'))
            for i,tag in enumerate(packet[:-1]):
                if tag == DXFTag(9,'$HANDSEED'): packet[i+1] = DXFTag(5,'F2000')
            output.append(packet)
        slots = [i for i,p in enumerate(output) if p[0] == DXFTag(0,'CLASS')]
        for i,p in zip(slots, sorted((output[i] for i in slots),key=lambda p: one(p,1))): output[i]=p
        data = write(output, doc.dxfversion); path = ROOT/'extracted'/f'dimassoc-R{source["year"]}.dxf'; path.write_bytes(data)
        extracted = {identity(p):p for p in packets(data) if identity(p)}
        for packet in translated: assert exact(extracted[identity(packet)]) == exact(packet)
        audit = ezdxf.read(io.StringIO(data.decode('utf-8'))).audit()
        print(source['year'], 'packets', len(application), 'audit', len(audit.errors), len(audit.fixes), [(e.code,e.message) for e in audit.errors+audit.fixes])
        manifest['fixtures'].append({'year':source['year'],'profile':doc.dxfversion,'file':str(path.relative_to(ROOT)),
            'sha256':hashlib.sha256(data).hexdigest(),'source_file':source['gzip_file'],'handle_map':mapping,
            'application_packets':[identity(p) for p in application],'associations':ASSOCIATIONS,
            'opaque_association':'42F','synthetic_resources':'Named layers and dimension styles; empty named dimension display blocks.',
            'audit_errors':len(audit.errors),'audit_repairs':len(audit.fixes)})
    (ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')

if __name__ == '__main__': main()
