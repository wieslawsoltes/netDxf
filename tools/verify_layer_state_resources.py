#!/usr/bin/env python3
"""Check renamed linetype identities and complete typed saved-state packets."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_populated_layer_state_identity import load
from verify_layer_state_identity import canonical, one, require, reject, records, PROFILES
from verify_raw_line_geometry import audit_signature

STAGES = ('source', 'output', 'resave')
NAMES = ('FIRST', 'SECOND')


def check_states(indexed, dictionaries, layers, types):
    table, outer, inner = dictionaries
    outer_id, inner_id = one(outer, 5), one(inner, 5)
    require(outer == [(0, 'DICTIONARY'), (5, outer_id), (330, one(table, 5)), (100, 'AcDbDictionary'),
                      (280, 1), (281, 1), (3, 'ACAD_LAYERSTATES'), (360, inner_id)], 'Layer-state extension shape')
    require(inner[:6] == [(0, 'DICTIONARY'), (5, inner_id), (330, outer_id), (100, 'AcDbDictionary'), (280, 1), (281, 1)]
            and len(inner) == 10, 'State dictionary shape')
    current = 'NEW_DASH' if 'NEW_DASH' in types else 'OLD_DASH'
    states = {}
    for i, name in enumerate(NAMES):
        label, pointer = inner[6+2*i:8+2*i]
        require(label == (3, name) and pointer[0] == 350, 'State name/order/pointer')
        handle = canonical(pointer[1]); row = indexed.get(handle)
        wanted = [(0, 'XRECORD'), (5, handle), (102, '{ACAD_REACTORS'), (330, inner_id), (102, '}'),
                  (330, inner_id), (100, 'AcDbXrecord'), (280, 1), (91, 2047),
                  (301, 'Saved linetype references' if i == 0 else 'Independent settings'), (290, 0), (302, 'Walls' if i == 0 else '0'),
                  (330, layers['0']), (90, 8), (62, 7), (370, -3), (331, types['Continuous']), (440, 0),
                  (330, layers['Walls']), (90, 12), (62, 1), (370, -3), (331, types[current]), (440, 0),
                  (330, layers['Doors']), (90, 8), (62, 3), (370, -3),
                  (331, types[current if i == 0 else 'OTHER_DASH']), (440, 33554495)]
        require(row == wanted, 'Saved state identity/fields/reference changed: ' + name)
        require(handle not in states, 'Duplicate state identity'); states[handle] = row
    return states


def inspect(path, year, binary, changed):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
    tags = load(path, year)
    require(tags[tags.index((9, '$ACADVER'))+1] == (1, PROFILES[year]), 'Profile differs')
    type_name = 'NEW_DASH' if changed else 'OLD_DASH'
    require(tags[tags.index((9, '$CELTYPE'))+1] == (6, type_name), 'Current linetype not renamed')
    rows = records(tags); indexed = {}
    for row in rows:
        if row[0] in ((0, 'SECTION'), (0, 'ENDSEC'), (0, 'EOF')): continue
        identities = [v for c,v in row if c in (5,105)]
        if identities:
            require(len(identities) == 1, 'Repeated identity declaration')
            handle = canonical(identities[0]); require(handle not in indexed, 'Duplicate identity'); indexed[handle] = row
    layer_rows = [r for r in rows if r[0] == (0,'LAYER')]
    layers = {one(r,2):one(r,5) for r in layer_rows}
    require(set(layers) == {'0','Walls','Doors'} and len(layer_rows) == 3, 'Layer inventory')
    type_rows = [r for r in rows if r[0] == (0,'LTYPE')]
    types = {one(r,2):one(r,5) for r in type_rows}
    require(set(types) == {'ByLayer','ByBlock','Continuous','OTHER_DASH',type_name} and len(type_rows) == 5, 'Linetype inventory/recreated old name')
    table, = [r for r in rows if r[:2] == [(0,'TABLE'),(2,'LAYER')]]
    outer = indexed[one(table,360)]; inner = indexed[one(outer,360)]
    dictionaries = (table,outer,inner)
    states = check_states(indexed,dictionaries,layers,types)
    require({one(r,5) for r in rows if r[0] == (0,'XRECORD')} == set(states), 'Unexpected XRECORDs')
    count = 0
    for handle,row in states.items():
        for i in range(1,len(row)):
            for mode in ('remove','duplicate','change'):
                damaged = list(row)
                if mode == 'remove': damaged.pop(i)
                elif mode == 'duplicate': damaged.insert(i,damaged[i])
                else:
                    c,v = damaged[i]; damaged[i] = (c,v+'_bad' if isinstance(v,str) else int(v)^1)
                bad=dict(indexed);bad[handle]=damaged
                count += reject(lambda:check_states(bad,dictionaries,layers,types))
    doc = ezdxf.readfile(path)
    for handle, row in states.items():
        record = doc.entitydb[handle]
        require(record.dxftype() == 'XRECORD' and record.dxf.owner == one(inner,5), 'Independent state identity/owner')
        require([(t.code,t.value) for t in record.tags] == row[8:], 'Independent complete state packet')
    require(doc.header['$CELTYPE'] == type_name and doc.linetypes.get(type_name).dxf.handle == types[type_name], 'Independent resource name/header')
    line, = [r for r in rows if r[0] == (0,'LINE')]
    for c,v in {10:1.,20:2.,30:3.,11:4.,21:5.,31:6.}.items(): require(one(line,c)==v,'Following geometry')
    require(not any(any(v.values()) for v in audit_signature(doc)), 'Independent audit errors/repairs')
    # Only the selected LTYPE's name changes. Every state packet, handle and pointer
    # remains exact. Layout regeneration/time fields are outside this targeted check.
    normalized_types = {one(r,5): [(c,'<renamed-linetype>' if c==2 and v==type_name else v) for c,v in r] for r in type_rows}
    retained = (sorted(indexed),dictionaries,layer_rows,normalized_types,states,line)
    return retained,count


def main(directory):
    names={f'layer-state-resources-AutoCad{v}-{t}-{s}.dxf' for v,t,s in itertools.product(PROFILES,('text','binary'),STAGES)}
    def inventory(actual): require(actual==names,'Missing/extra resource-rename drawings')
    inventory({p.name for p in directory.glob('layer-state-resources-*.dxf')})
    count=reject(lambda:inventory(names- {next(iter(names))}))+reject(lambda:inventory(names|{'extra.dxf'}))
    for year,transport in itertools.product(PROFILES,('text','binary')):
        expected=None
        for stage in STAGES:
            actual,rejected=inspect(directory/f'layer-state-resources-AutoCad{year}-{transport}-{stage}.dxf',year,
                                    (transport=='binary') != (stage=='output'),stage!='source')
            count+=rejected
            if expected is None:expected=actual
            else:
                require(actual==expected,'Unselected resource/state identities or settings changed')
                damaged=list(actual);damaged[0]=damaged[0]+['ABCDEF']
                count+=reject(lambda:require(tuple(damaged)==expected,'Coherent identity substitution'))
    print(f'PASS: {len(names)} drawings / {len(names)*2} saved states; {count} corruption/inventory controls rejected; '
          'renamed linetype, exact state packets and resource identities; zero independent audit repairs. No native AutoCAD claim.')


if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_layer_state_resources.py ARTIFACTS');main(Path(sys.argv[1]))
