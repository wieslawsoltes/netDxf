#!/usr/bin/env python3
"""Independent retained POLYFACE identities, packets, geometry and metadata gate.

Checks six pinned native chains and twelve unchanged ezdxf producer inputs.
Raw packets, rather than ezdxf's projected child properties, define fidelity.
"""
from pathlib import Path
import argparse
import copy
import gzip
import hashlib
import json
import ezdxf
from ezdxf.lldxf.types import DXFTag
import verify_polygonmesh_records as common

ROOT = Path(__file__).resolve().parents[1]
check, equal, wire, children = common.check, common.equal, common.wire, common.children
VERSIONS = common.VERSIONS
POINTS = [(1., 2., 3.), (4., 7., 11.), (8., 12., 17.), (13., 19., 23.), (29., 31., 37.), (41., 43., 47.)]


def source_inputs():
    folder = ROOT / 'tests/fixtures/polyface-records'
    producer = {}
    negatives = json.loads((folder / 'negative-manifest.json').read_text())['fixtures']
    check(len(negatives) == 2, 'Pinned malformed XData fixture inventory')
    for item in negatives:
        data = (folder / item['file']).read_bytes()
        check(hashlib.sha256(data).hexdigest() == item['sha256'], 'Pinned malformed XData bytes changed')
    manifest = json.loads((folder / 'manifest.json').read_text())
    check(manifest['producer'] == 'ezdxf 1.4.4' and len(manifest['fixtures']) == 12, 'Producer inventory')
    for item in manifest['fixtures']:
        data = (folder / item['file']).read_bytes()
        check(hashlib.sha256(data).hexdigest() == item['sha256'], 'Producer bytes changed')
        common.native.audit(ezdxf.readfile(folder / item['file']))
        producer[item['year'], item['binary']] = item, wire(data)
    natives = {}
    manifest = json.loads((folder / 'native-manifest.json').read_text())
    check(len(manifest['fixtures']) == 6, 'Six native profiles required')
    for item in manifest['fixtures']:
        source = item['source']; packed = (ROOT / 'tests/fixtures/dimassoc/originals-gzip' / source['gzip_file']).read_bytes()
        check(hashlib.sha256(packed).hexdigest() == source['gzip_sha256'], 'Native gzip hash')
        original = gzip.decompress(packed)
        check(hashlib.sha256(original).hexdigest() == source['source_sha256'], 'Native source hash')
        check(hashlib.sha1(b'blob ' + str(len(original)).encode() + b'\0' + original).hexdigest() == source['git_blob_sha'], 'Native Git blob hash')
        data = (folder / item['file']).read_bytes(); check(hashlib.sha256(data).hexdigest() == item['sha256'], 'Native carrier hash')
        before, after = wire(original), wire(data)
        for handle in item['coordinates'] + item['faces'] + [item['seqend']]: equal(before[handle], after[handle], 'Native extracted child packet')
        parent = [DXFTag(330, item['carrier_owner']) if tag == DXFTag(330, item['native_owner']) else tag for tag in before[item['parent']]]
        equal(parent, after[item['parent']], 'Native parent changed beyond declared carrier owner')
        natives[item['year']] = item, after
    return producer, natives


def output(path, year, binary):
    return common.output(path, year, binary)[0]


def chain(records, parent):
    packets = list(records.values()); start = next(i for i, p in enumerate(packets) if common.identity(p) == parent)
    result = []
    for packet in packets[start + 1:]:
        check(packet[0].value in ('VERTEX', 'SEQEND'), 'Interrupted retained child chain')
        result.append(common.identity(packet))
        if packet[0].value == 'SEQEND': return result
    raise ValueError('Missing SEQEND')


def geometry(records, handles, edited=False):
    for i, handle in enumerate(handles['coordinates']):
        packet = records[handle]
        check(common.native.one(packet, 70) == 192, 'Coordinate role flags')
        expected = (81., 82., 83.) if edited and i == 2 else POINTS[i]
        check(common.native.one(packet, 10) == expected, 'Coordinate identity-to-position mapping')
    first = records[handles['faces'][0]]; second = records[handles['faces'][1]]
    check([(tag.code, tag.value) for tag in first if 71 <= tag.code <= 74] == list(enumerate([-1, -6 if edited else 2, -3, 4], 71)), 'First signed face slots')
    check([(tag.code, tag.value) for tag in second if 71 <= tag.code <= 74] == list(enumerate([4, -5, 6], 71)), 'Second signed face slots')
    check(common.native.one(first, 70) == common.native.one(second, 70) == 128, 'Face role flags')


def native_check(after, item, before):
    check(list(children(after)) == list(children(before)), 'Native physical child sequence')
    for handle, packet in children(before).items(): equal(packet, after[handle], 'Complete native child packet')
    expected = list(before[item['parent']]); at = expected.index(next(t for t in expected if t.code == 8))
    expected[at:at] = [DXFTag(67, 0)]; at += 1
    expected[at + 1:at + 1] = [DXFTag(62, 256), DXFTag(6, 'ByLayer'), DXFTag(370, -1), DXFTag(48, 1.), DXFTag(60, 0)]
    equal(expected, after[item['parent']], 'Complete native header beyond six declared existing defaults')


def producer_check(after, item, before):
    h = item['handles']; check(list(children(after)) == list(children(before)), 'Producer physical identity/order inventory')
    for handle, packet in children(before).items(): equal(packet, after[handle], 'Complete producer child packet')
    for name in ('face_xrecord', 'seqend_xrecord'): equal(before[h[name]], after[h[name]], 'Owned child XRECORD packet')
    geometry(after, h)
    check(chain(after, h['mesh']) == h['coordinates'] + h['faces'] + [h['seqend']], 'Physical producer role sequence')
    for handle in h['coordinates'] + h['faces']: check(common.native.extractor.owner(after[handle]) == h['block_record'], 'Actual containing BLOCK_RECORD owner')
    check(common.native.extractor.owner(after[h['seqend']]) == h['mesh'], 'Actual SEQEND parent')


def replace_code(packet, code, value):
    result = list(packet); at = next(i for i, tag in enumerate(result) if tag.code == code); result[at] = DXFTag(code, value); return result


def face_common_edit(packet, layer, color):
    result = []; done = False
    for tag in packet:
        if tag == DXFTag(100, 'AcDbFaceRecord'):
            if color is not None: result.append(DXFTag(62, color))
            done = True
        if not done and tag.code in (62, 420, 430): continue
        if not done and tag.code == 8:
            if layer is not None: result.append(DXFTag(8, layer))
        else: result.append(tag)
    return result


def edited_check(after, item, before, inherited=False):
    h = item['handles']; expected_ids = h['coordinates'] + h['faces'] + [h['seqend']]
    check(list(children(after)) == expected_ids, 'Edited mesh physical child inventory')
    for handle in expected_ids:
        expected = before[handle]
        if handle == h['coordinates'][2]: expected = replace_code(expected, 10, (81., 82., 83.))
        if handle == h['faces'][0]: expected = face_common_edit(replace_code(expected, 72, -6), None if inherited else 'EDITED_FACE', None if inherited else 4)
        equal(expected, after[handle], 'Face/coordinate edit changed unrelated physical packet fields')
    geometry(after, h, edited=True)


def clone_check(after, item, before):
    parents = [h for h, p in after.items() if p[0].value == 'POLYLINE']; check(len(parents) == 1, 'Clone parent inventory')
    parent = parents[0]; ids = chain(after, parent); h = item['handles']; original = h['plain_coordinates'] + h['plain_faces'] + [h['plain_seqend']]
    check(len(ids) == 6 and not set(ids).intersection(original), 'Clone fresh child identities')
    block = common.native.extractor.owner(after[parent])
    for i, (old, new) in enumerate(zip(original, ids)):
        expected = [DXFTag(5, new) if t.code == 5 else DXFTag(330, parent if i == 5 else block) if t.code == 330 else t for t in before[old]]
        equal(expected, after[new], 'Clone retained child packet beyond actual identity/owner remapping')


def readopt_check(after, item, before):
    h = item['handles']; check(list(children(after)) == list(children(before)), 'Readoption child identities/order')
    parent = common.native.extractor.owner(after[h['plain_seqend']]); check(after[parent][0] == DXFTag(0,'POLYLINE') and chain(after,parent) == h['plain_coordinates'] + h['plain_faces'] + [h['plain_seqend']], 'Readoption actual containing parent')
    equal(header_body(before[h['plain_mesh']]),header_body(after[parent]),'Readoption parent subclass packet')
    for handle, packet in children(before).items():
        expected = face_common_edit(packet, None, None) if handle == h['plain_faces'][0] else packet
        if handle == h['plain_seqend']: expected = replace_owner(expected, common.native.extractor.owner(after[handle]))
        equal(expected, after[handle], 'Readoption identity or nullable face metadata changed')


def private_check(after, item, before, variant):
    h = item['handles']; handle = h['plain_coordinates'][0]; expected = list(before[handle]); at = next((i for i,t in enumerate(expected) if t.code == 1001), len(expected)); common_at = next(i for i,t in enumerate(expected) if t.code == 100)
    if variant == 0: expected[common_at:common_at] = [DXFTag(102,'{PRIVATE'), DXFTag(5,'FFFFFF'), DXFTag(330,'EEEEEE'), DXFTag(102,'}')]
    elif variant == 1: expected[at:at] = [DXFTag(102,'{PRIVATE'), DXFTag(5,'FFFFFF'), DXFTag(70,-1), DXFTag(10,(77.,88.,99.)), DXFTag(102,'}')]
    elif variant == 2: expected[at:at] = [DXFTag(100,'PrivateVertexClass'), DXFTag(5,'FFFFFF'), DXFTag(70,-1), DXFTag(10,(77.,88.,99.)), DXFTag(330,'EEEEEE')]
    elif variant == 3: expected[common_at:common_at] = [DXFTag(102,'{ACAD_REACTORS'), DXFTag(330,'0'), DXFTag(102,'}')]
    elif variant == 4: expected[common_at:common_at] = [DXFTag(102,'{ACAD_XDICTIONARY'), DXFTag(360,'0'), DXFTag(102,'}')]
    else: expected[common_at:common_at] = [DXFTag(102,'{ACAD_REACTORS'), DXFTag(102,'}')]
    for key, packet in children(before).items(): equal(expected if key == handle else packet, after[key], 'Private child exact packet')


def controls(after, item, before):
    h = item['handles']; count = 0
    for fault in range(10):
        damaged = copy.deepcopy(after)
        if fault == 0: damaged[h['coordinates'][2]] = replace_code(damaged[h['coordinates'][2]], 10, (81.,82.,83.))
        elif fault == 1: damaged[h['faces'][0]] = replace_code(damaged[h['faces'][0]], 71, 1)
        elif fault == 2: damaged[h['faces'][0]] = replace_code(damaged[h['faces'][0]], 72, 0)
        elif fault == 3: damaged[h['faces'][0]] = replace_code(damaged[h['faces'][0]], 70, 192)
        elif fault == 4: damaged[h['faces'][0]] = replace_owner(damaged[h['faces'][0]], h['coordinates'][0])
        elif fault == 5: damaged[h['seqend']] = replace_code(damaged[h['seqend']], 330, h['block_record'])
        elif fault == 6: damaged[h['faces'][0]] = replace_code(damaged[h['faces'][0]], 8, '0')
        elif fault == 7: damaged[h['faces'][0]] = replace_code(damaged[h['faces'][0]], 62, 4)
        elif fault == 8: del damaged[h['seqend']]
        else: damaged[h['face_xrecord']] = replace_code(damaged[h['face_xrecord']], 330, h['coordinates'][1])
        try: producer_check(damaged, item, before)
        except (ValueError, KeyError, StopIteration): count += 1
        else: raise ValueError(f'Undetected corruption control {fault}')
    return count


def replace_owner(packet, owner):
    result=list(packet); depth=0
    for i,tag in enumerate(result):
        if tag.code==102: depth += 1 if tag.value.startswith('{') else -1
        elif tag.code==330 and depth==0: result[i]=DXFTag(330,owner);return result
    raise ValueError('Missing ordinary owner')


def move_check(after,item,before,reactor=False):
    h=item['handles']; parent=common.native.extractor.owner(after[h['plain_seqend']]);block=common.native.extractor.owner(after[parent])
    for handle,packet in children(before).items():
        expected=packet
        if handle in h['plain_coordinates']+h['plain_faces']: expected=replace_owner(expected,block)
        if handle==h['plain_seqend']:expected=replace_owner(expected,parent)
        if reactor and handle==h['plain_coordinates'][0]:
            expected=list(expected);at=next(i for i,t in enumerate(expected) if t.code==100);expected[at:at]=[DXFTag(102,'{ACAD_REACTORS'),DXFTag(330,parent),DXFTag(102,'}')]
        equal(expected,after[handle],'Moved child owner/identity/metadata packet')
    check(chain(after,parent)==h['plain_coordinates']+h['plain_faces']+[h['plain_seqend']],'Moved child sequence')


def coordinate_edit_check(after,item,before):
    h=item['handles']
    for handle,packet in children(before).items():equal(replace_code(packet,10,(91.,92.,93.)) if handle==h['coordinates'][0] else packet,after[handle],'Single coordinate edit packet')


def metadata_group(packet,name):
    active=False;result=[]
    for tag in packet:
        if tag==DXFTag(102,name):active=True;continue
        if active and tag==DXFTag(102,'}'):return result
        if active:result.append(tag)
    return result


def strip_metadata(packet):
    result=[];skip=False
    for tag in packet:
        if tag.code==102 and tag.value in ('{ACAD_REACTORS','{ACAD_XDICTIONARY'):skip=True;continue
        if skip and tag==DXFTag(102,'}'):skip=False;continue
        if not skip:result.append(tag)
    return result


def metadata_check(after,item,before):
    h=item['handles'];first=h['coordinates'][0]
    for handle,packet in children(before).items():
        equal(strip_metadata(packet) if handle==first else packet,strip_metadata(after[handle]) if handle==first else after[handle],'Common metadata edit changed unrelated child fields')
    check(metadata_group(after[first],'{ACAD_REACTORS')==[DXFTag(330,h['coordinates'][2])]*2,'Duplicate ordered reactor identities')
    extension=metadata_group(after[first],'{ACAD_XDICTIONARY');check(len(extension)==1 and extension[0].code==360,'New child extension attachment')
    dictionary=after[extension[0].value];check(common.native.extractor.owner(dictionary)==first,'New child reciprocal dictionary owner')
    check(DXFTag(3,'NEW') in dictionary,'New child dictionary entry')


def header_body(packet):return packet[packet.index(DXFTag(100,'AcDbPolyFaceMesh')):]


def private_header_check(after,item,before,variant):
    h=item['handles'];parent=h['mesh'];expected=list(header_body(before[parent]));fake=[DXFTag(70,16),DXFTag(71,-12),DXFTag(72,0),DXFTag(210,(7.,8.,9.))]
    if variant==2:expected=[tag for tag in expected if tag.code not in (71,72)]
    if variant==1:expected += [DXFTag(210,(0.,1.,0.)),DXFTag(100,'PrivatePolyfaceHeader')]+fake
    elif variant!=3:
        expected += [DXFTag(102,'{PRIVATE_POLYFACE_HEADER')]+fake
        if variant>=4:expected.append(DXFTag(330 if variant==4 else 320,h['plain_coordinates'][0]))
        expected.append(DXFTag(102,'}'))
        if variant<3:expected.append(DXFTag(210,(0.,1.,0.)))
    equal(expected,header_body(after[parent]),'Context-qualified complete private subclass header')
    ids=list(children(before))
    if variant==5:ids=[i for i in ids if i not in h['plain_coordinates']+h['plain_faces']+[h['plain_seqend']]]
    check(list(children(after))==ids,'Private header child inventory')
    for handle in ids:
        expected=before[handle]
        if variant==3 and handle==h['faces'][0]:
            expected=[t for t in expected if t.code not in (8,62,420,430)];at=expected.index(DXFTag(100,'AcDbFaceRecord'))
            expected[at:at]=[DXFTag(8,'PUBLIC_FACE'),DXFTag(62,4),DXFTag(100,'PrivateBeforeFace'),DXFTag(8,'PRIVATE_LAYER'),DXFTag(62,5),DXFTag(71,-6),DXFTag(70,192)]
            expected=replace_code(expected,72,-6)
        equal(expected,after[handle],'Private header/face edit changed unrelated packet')




def slot_edit_check(after,variant):
    from verify_polyface_grammar import POINTS, EXPECTED
    parents=[p for p in after.values() if p[0]==DXFTag(0,'POLYLINE')];check(len(parents)==1,'Slot-edit parent inventory');parent=parents[0];handle=common.identity(parent)
    check(common.native.one(parent,71)==-17 and common.native.one(parent,72)==123,'Slot edit normalized advisory counts')
    ids=chain(after,handle);packets=[after[i] for i in ids];check(len(ids)==6,'Slot-edit child inventory')
    face=packets[0 if variant%2 else 4];coordinates=packets[1:5] if variant%2 else packets[:4]
    for i,p in enumerate(coordinates):check(common.native.one(p,70)==192 and common.native.one(p,10)==((131.,132.,133.) if i==2 else POINTS[i]),'Slot-edit coordinate role/order/position')
    slots=[[-1,0,32767,-32768],[1,-2,0,4],[1,-2,3,0],[-1,2,-3,4],[1,0,-4,0],[-1,2,-3,4],[1,2,1,0],[1,2,3,0]][variant];slots[0]*=-1
    order=[3,1,0,2] if variant==5 else list(range(4));expected=[(71+i,slots[i]) for i in order if not(variant==4 and i in (1,3)) and not(variant==7 and i==3)]
    check([(t.code,t.value) for t in face if 71<=t.code<=74]==expected,'Qualified edit normalized inactive signed face slots/order')
    check(common.native.one(face,70)==128,'Slot-edit face role');check(all(common.native.extractor.owner(p)==handle for p in packets),'Slot-edit actual parent owner')


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('artifacts', type=Path); args = parser.parse_args()
    producer, natives = source_inputs(); expected = set(); controls_count = 0
    def use(name, year, binary, validate):
        expected.add(name); after = output(args.artifacts / name, year, binary); validate(after); print('PASS', name); return after
    for year in VERSIONS:
        for binary in (False, True):
            item, before = producer[year,binary]
            use(f'polyface-records-native-AutoCad{year}-{binary}.dxf', year,binary, lambda a: native_check(a,*natives[year]))
            for target in (False, True):
                after = use(f'polyface-records-producer-AutoCad{year}-{binary}-{target}.dxf',year,target,lambda a: producer_check(a,item,before))
                controls_count += controls(after,item,before)
            use(f'polyface-records-clone-AutoCad{year}-{binary}.dxf',year,not binary,lambda a: clone_check(a,item,before))
            use(f'polyface-records-edited-AutoCad{year}-{binary}.dxf',year,binary,lambda a: edited_check(a,item,before))
            use(f'polyface-records-inherited-AutoCad{year}-{binary}.dxf',year,binary,lambda a: edited_check(a,item,before,True))
            use(f'polyface-records-readopt-AutoCad{year}-{binary}.dxf',year,binary,lambda a: readopt_check(a,item,before))
            # The move case is validated independently below once all output roles are decoded.
            use(f'polyface-records-move-AutoCad{year}-{binary}.dxf',year,binary,lambda a: move_check(a,item,before))
    for binary in (False,True):
        item,before=producer[2018,binary]
        for variant in range(8): use(f'polyface-records-slot-edited-{binary}-{variant}.dxf',2018,binary,lambda a: slot_edit_check(a,variant))
        for variant in range(6): use(f'polyface-records-private-{binary}-{variant}.dxf',2018,binary,lambda a: private_check(a,item,before,variant))
        for variant in range(6): use(f'polyface-records-private-header-{binary}-{variant}.dxf',2018,binary,lambda a: private_header_check(a,item,before,variant))
        use(f'polyface-records-edited-{binary}.dxf',2018,binary,lambda a: coordinate_edit_check(a,item,before))
        use(f'polyface-records-metadata-edited-{binary}.dxf',2018,binary,lambda a: metadata_check(a,item,before))
        use(f'polyface-records-parent-reactor-move-{binary}.dxf',2018,binary,lambda a: move_check(a,item,before,True))
    check({p.name for p in args.artifacts.glob('polyface-records-*.dxf')} == expected, 'Mandatory 142-output inventory differs')
    check(len(expected)==142 and controls_count==240, 'Exact output/control inventory')
    print(f'PASS {len(expected)} outputs, {controls_count} actual corruption controls, zero audit errors or repairs')


if __name__ == '__main__': main()
