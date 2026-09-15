#!/usr/bin/env python3
"""Independent VERTEX/SEQEND packet, identity, ownership and lifecycle gate."""
from pathlib import Path
import argparse
import copy
import hashlib
import json
import tempfile
import ezdxf
from ezdxf.lldxf.types import DXFTag
import verify_dimassoc as native

ROOT = Path(__file__).resolve().parents[1]
VERSIONS = native.VERSIONS
check = native.check
exact = native.extractor.exact
packets = native.packets


def identity(packet):
    depth = 0
    for tag in packet:
        if tag.code == 100 and depth == 0:
            break
        if tag.code == 102:
            depth += 1 if tag.value.startswith('{') else -1
        elif tag.code == 5 and depth == 0:
            return tag.value


def wire(data):
    result = {}
    for packet in packets(data):
        handle = identity(packet)
        if handle:
            check(handle not in result, 'Duplicate physical identity')
            result[handle] = packet
    return result


def children(content):
    return {h: p for h, p in content.items() if p[0].value in ('VERTEX', 'SEQEND')}


def equal(expected, actual, description):
    check(exact(expected) == exact(actual), description)


def output(path, year, binary):
    data = path.read_bytes()
    check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == VERSIONS[year], 'Wrong DXF profile')
    native.audit(doc)
    return wire(data), doc


def producer_sources():
    folder = ROOT / 'tests/fixtures/polygonmesh-records'
    manifest = json.loads((folder / 'manifest.json').read_text())
    check(manifest['producer'] == 'ezdxf 1.4.4' and len(manifest['fixtures']) == 12, 'Producer inventory')
    result = {}
    for fixture in manifest['fixtures']:
        data = (folder / fixture['file']).read_bytes()
        check(hashlib.sha256(data).hexdigest() == fixture['sha256'], 'Producer source hash')
        native.audit(ezdxf.readfile(folder / fixture['file']))
        result[fixture['year'], fixture['binary']] = (fixture, wire(data))
    return result


def native_sources():
    folder = ROOT / 'tests/fixtures/polygonmesh-cardinality'
    result = {}
    import gzip
    for fixture in json.loads((folder / 'native-manifest.json').read_text()):
        year = int(fixture['carrier'][1:5])
        original = ROOT / fixture['source']
        check(hashlib.sha256(gzip.decompress(original.read_bytes())).hexdigest() == fixture['sourceSha256'], 'Native uncompressed source hash')
        data = (folder / fixture['carrier']).read_bytes()
        check(hashlib.sha256(data).hexdigest() == fixture['carrierSha256'], 'Native carrier hash')
        source = wire(gzip.decompress(original.read_bytes())); carrier = wire(data)
        for handle, packet in children(carrier).items():
            equal(source[handle], packet, 'Native child packet was changed during extraction')
        check(len(children(carrier)) == 13, 'Native child inventory')
        result[year] = carrier
    check(set(result) == {2000, 2018}, 'Native mesh source profiles')
    return result


def native_output(path, year, binary, sources):
    before = sources[year]; after, _ = output(path, year, binary)
    check(children(after).keys() == children(before).keys(), 'Native child identity inventory')
    for handle, packet in children(before).items():
        equal(packet, after[handle], 'Native polygon mesh child packet changed')
        check(native.extractor.owner(after[handle]) == '20F', 'Native parent owner')


def producer_output(path, year, source_binary, binary, sources):
    fixture, before = sources[year, source_binary]
    after, doc = output(path, year, binary)
    check(children(before).keys() == children(after).keys(), 'Producer child identity inventory')
    for handle, packet in children(before).items():
        equal(packet, after[handle], 'Producer child packet changed')
    handles = fixture['handles']
    owner = doc.entitydb[handles['polyline']]
    check([v.dxf.handle for v in owner.vertices] == handles['vertices'], 'Independent reader vertex order')
    check(owner.seqend.dxf.handle == handles['seqend'], 'Independent reader SEQEND identity')
    check(owner.dxf.m_count == 3 and owner.dxf.n_count == 4, 'Asymmetric mesh dimensions')
    for i in range(3):
        for j in range(4):
            packet = after[handles['vertices'][i * 4 + j]]
            check(next(t.value for t in packet if t.code == 10) == (i + 0.125 * j, j + 0.25 * i, 100 * i + 7 * j), 'Physical vertex identity-to-coordinate mapping')
    for handle in handles['vertices']:
        check(native.extractor.owner(after[handle]) == handles['block_record'], 'Producer block-record owner changed')
    check(native.extractor.owner(after[handles['seqend']]) == handles['polyline'], 'Producer SEQEND owner changed')
    for name in ('vertex_xrecord', 'seqend_xrecord'):
        equal(before[handles[name]], after[handles[name]], 'Child owned XRECORD changed')


def clone_output(path, year, source_binary, sources):
    fixture, before = sources[year, source_binary]
    after, doc = output(path, year, not source_binary)
    original = fixture['handles']
    clone = list(doc.modelspace().query('POLYLINE'))
    check(len(clone) == 1, 'Clone parent inventory')
    clone = clone[0]
    old = original['plain_vertices'] + [original['plain_seqend']]
    new = [v.dxf.handle for v in clone.vertices] + [clone.seqend.dxf.handle]
    check(len(new) == len(old) == 7 and len(set(new)) == 7, 'Clone owned identity inventory')
    check(not set(old).intersection(new), 'Clone reused source child identity')
    for previous, current in zip(old, new):
        expected = []
        for tag in before[previous]:
            if tag.code == 5: tag = DXFTag(5, current)
            elif tag.code == 330: tag = DXFTag(330, clone.dxf.owner if previous != original['plain_seqend'] else clone.dxf.handle)
            elif tag.code == 8 and tag.value == 'VERTEX_ONLY': tag = DXFTag(8, 'CLONED_VERTEX_LAYER')
            expected.append(tag)
        equal(expected, after[current], 'Clone optional fields, XData or remapped owner changed')


def move_output(path, year, binary, sources):
    fixture, before = sources[year, binary]
    after, doc = output(path, year, binary)
    handles = fixture['handles']
    destination = doc.blocks['MOVED_VERTEX_PARENT'].block_record_handle
    moved = list(doc.blocks['MOVED_VERTEX_PARENT'].query('POLYLINE'))
    check(len(moved) == 1, 'Moved parent inventory')
    moved = moved[0]
    check(native.extractor.owner(after[moved.dxf.handle]) == destination, 'Moved parent block')
    for handle, packet in children(before).items():
        expected = [DXFTag(330, destination if handle in handles['plain_vertices'] else moved.dxf.handle)
                    if t.code == 330 and handle in handles['plain_vertices'] + [handles['plain_seqend']] else t for t in packet]
        equal(expected, after[handle], 'Move changed child identity or metadata')


def private_packet(packet, variant):
    result = list(packet)
    common = next(i for i, t in enumerate(result) if t.code == 100)
    xdata = next((i for i, t in enumerate(result) if t.code == 1001), len(result))
    if variant == 0: result[common:common] = [DXFTag(102, '{PRIVATE'), DXFTag(5, 'FFFFFF'), DXFTag(330, 'EEEEEE'), DXFTag(102, '}')]
    elif variant == 1: result[xdata:xdata] = [DXFTag(102, '{PRIVATE'), DXFTag(5, 'FFFFFF'), DXFTag(70, -1), DXFTag(10, (77., 88., 99.)), DXFTag(102, '}')]
    elif variant == 2: result[xdata:xdata] = [DXFTag(100, 'PrivateVertexClass'), DXFTag(5, 'FFFFFF'), DXFTag(70, -1), DXFTag(10, (77., 88., 99.)), DXFTag(330, 'EEEEEE')]
    elif variant == 3: result[common:common] = [DXFTag(102, '{ACAD_REACTORS'), DXFTag(330, '0'), DXFTag(102, '}')]
    elif variant == 4: result[common:common] = [DXFTag(102, '{ACAD_XDICTIONARY'), DXFTag(360, '0'), DXFTag(102, '}')]
    else: result[common:common] = [DXFTag(102, '{ACAD_REACTORS'), DXFTag(102, '}')]
    return result


def private_output(path, binary, variant, sources):
    fixture, before = sources[2018, binary]
    after, _ = output(path, 2018, binary)
    handle = fixture['handles']['plain_vertices'][0]
    equal(private_packet(before[handle], variant), after[handle], 'Private/null metadata packet changed')
    check('FFFFFF' not in after and 'EEEEEE' not in after, 'Private handle became physical identity')


def edited_output(path, binary, sources, metadata=False):
    fixture, before = sources[2018, binary]
    after, doc = output(path, 2018, binary)
    handles = fixture['handles']; first = handles['vertices'][0]
    if not metadata:
        for handle, packet in children(before).items():
            expected = [DXFTag(10, (91., 92., 93.)) if handle == first and t.code == 10 else t for t in packet]
            equal(expected, after[handle], 'Coordinate edit changed child identity/metadata')
    else:
        record = doc.entitydb[first]
        # ezdxf's object model deduplicates reactors on input. Inspect the original
        # independent tag stream to qualify multiplicity and order on the wire.
        start = after[first].index(DXFTag(102, '{ACAD_REACTORS'))
        end = after[first].index(DXFTag(102, '}'), start)
        check(after[first][start + 1:end] == [DXFTag(330, handles['vertices'][2])] * 2, 'Explicit duplicate reactor ordering')
        extension = record.get_extension_dict()
        check(extension.dictionary.dxf.owner == first, 'New extension owner is not the VERTEX')
        check(extension['NEW'].dxf.value == 'new child attachment', 'New owned metadata payload')
        for handle, packet in children(before).items():
            if handle != first: equal(packet, after[handle], 'Metadata edit changed another child')


def parent_reactor_move(path, binary, sources):
    fixture, before = sources[2018, binary]
    after, doc = output(path, 2018, binary)
    handles = fixture['handles']; block = doc.blocks['MOVED_PARENT_REACTOR']
    moved = list(block.query('POLYLINE'))
    check(len(moved) == 1, 'Moved reactor parent inventory')
    moved = moved[0]
    check(moved.dxf.handle != handles['plain_polyline'], 'Parent handle move was not exercised')
    for handle, packet in children(before).items():
        expected = [DXFTag(330, block.block_record_handle if handle in handles['plain_vertices'] else moved.dxf.handle)
                    if t.code == 330 and handle in handles['plain_vertices'] + [handles['plain_seqend']] else t for t in packet]
        if handle == handles['plain_vertices'][0]:
            at = next(i for i, tag in enumerate(expected) if tag.code == 100)
            expected[at:at] = [DXFTag(102, '{ACAD_REACTORS'), DXFTag(330, moved.dxf.handle), DXFTag(102, '}')]
        equal(expected, after[handle], 'Parent move retained a stale reactor or changed child metadata')


def corruption_controls(folder, sources):
    path = folder / 'polygonmesh-records-producer-AutoCad2018-False-False.dxf'
    content = packets(path.read_bytes()); fixture, _ = sources[2018, False]
    target = fixture['handles']['vertices'][0]
    with tempfile.TemporaryDirectory() as temporary:
        for fault in range(8):
            changed = copy.deepcopy(content)
            handle = fixture['handles']['seqend'] if fault == 5 else target
            packet = next(p for p in changed if identity(p) == handle)
            if fault == 6:
                first = next(i for i, p in enumerate(changed) if identity(p) == fixture['handles']['vertices'][0])
                second = next(i for i, p in enumerate(changed) if identity(p) == fixture['handles']['vertices'][1])
                changed[first], changed[second] = changed[second], changed[first]
            else:
                code = (5, 10, 8, 330, 41, 330, None, 70)[fault]
                at = next(i for i, tag in enumerate(packet) if tag.code == code)
                replacement = ('FFFFFFFE', (99., 98., 97.), 'WRONG_LAYER', '0', 87., '0', None, 32)[fault]
                packet[at] = DXFTag(code, replacement)
            corrupted = Path(temporary) / f'corrupt-{fault}.dxf'
            corrupted.write_bytes(native.extractor.write(changed, VERSIONS[2018]))
            try: producer_output(corrupted, 2018, False, False, sources)
            except (ValueError, AssertionError, ezdxf.DXFError): continue
            raise ValueError('Actual-output corruption escaped independent gate')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('artifacts', type=Path); args = parser.parse_args()
    check(ezdxf.__version__ == '1.4.4', 'Pinned oracle version differs')
    producer = producer_sources(); originals = native_sources(); expected = set()
    def verify(name, function, *parameters):
        expected.add(name); function(args.artifacts / name, *parameters)
    for year in VERSIONS:
        for source_binary in (False, True):
            for binary in (False, True):
                verify(f'polygonmesh-records-producer-AutoCad{year}-{source_binary}-{binary}.dxf', producer_output, year, source_binary, binary, producer)
            verify(f'polygonmesh-records-clone-AutoCad{year}-{source_binary}.dxf', clone_output, year, source_binary, producer)
            verify(f'polygonmesh-records-move-AutoCad{year}-{source_binary}.dxf', move_output, year, source_binary, producer)
    for binary in (False, True):
        for year in (2000, 2018):
            verify(f'polygonmesh-records-native-{year}-{binary}.dxf', native_output, year, binary, originals)
        for variant in range(6):
            verify(f'polygonmesh-records-private-{binary}-{variant}.dxf', private_output, binary, variant, producer)
        verify(f'polygonmesh-records-edited-{binary}.dxf', edited_output, binary, producer)
        verify(f'polygonmesh-records-metadata-edited-{binary}.dxf', edited_output, binary, producer, True)
        verify(f'polygonmesh-records-parent-reactor-move-{binary}.dxf', parent_reactor_move, binary, producer)
    check({p.name for p in args.artifacts.glob('polygonmesh-records-*.dxf')} == expected, 'Expected exact 70-output inventory')
    corruption_controls(args.artifacts, producer)
    print('POLYGONMESH VERTEX/SEQEND: 70 outputs; 2 pinned native originals/extractions; 12 unchanged producer fixtures; exact child packets, source identities, both owner forms, owned metadata, clones, moves with parent-reactor rebinding, coordinate edits, private groups and eight actual-output corruption controls passed; zero independent audit errors or repairs.')


if __name__ == '__main__': main()
