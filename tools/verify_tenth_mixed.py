#!/usr/bin/env python3
"""Check the native TABLE to authored manager/SECTION/Polyface/opaque/HATCH graph."""
import argparse
import copy
import json
import math
import struct
from pathlib import Path
import tempfile
import numpy as np
import ezdxf
from verify_fourth_mixed_modules import load, metadata, dictionary_edges
from verify_stored_table_content import source, check, exact
from verify_ninth_mixed import field, public, unique, native_packets, section_packet, settings_packet
from verify_hatch_pattern_affine import families, samples, near, acad, matrix
from verify_hatch_conic import basis, world, paths, points

ROOT = Path(__file__).resolve().parents[1]
FILES = {'acad_table_simple.dxf':2013, 'acad_table_with_blk_ref.dxf':2018}
PHASES = ('input','linked','manager-erased','opaque-guard','released','retry')
OPAQUE = 'QUALIFIED_FUTURE_CURVE'


def first(row, code):
    return next(value for group,value in row if group == code)


def native_record(row):
    # These two selected R2013/R2018 originals use layer 0 with all other
    # AcDbEntity defaults omitted. The existing writer emits them explicitly.
    if row[0] == (0,'ACAD_TABLE'):
        at = row.index((100,'AcDbEntity'))+1
        check(row[at] == (8,'0') and row[at+1][0] == 160, 'Pinned native TABLE common default envelope changed')
        return row[:at]+[(67,0),(8,'0'),(62,256),(6,'ByLayer'),(370,-1),(48,1.),(60,0)]+row[at+1:]
    if row[0] == (0,'TABLESTYLE'):
        check(row[2] == (102,'{ACAD_REACTORS'), 'Pinned native TABLESTYLE reactor order changed')
        end = row.index((102,'}'),3)+1; check(row[end] == (102,'{ACAD_XDICTIONARY'),'Pinned native TABLESTYLE extension block changed')
        extension_end = row.index((102,'}'),end+1)+1
        check([c for c,v in row[end:extension_end]] == [102,360,102], 'Native TABLESTYLE extension packet changed')
        # Preserve both complete blocks and all values; only their physical order changes.
        return row[:2]+row[end:extension_end]+row[2:end]+row[extension_end:]
    return row


def geometry_packet(row, manager_handle, phase):
    result = copy.deepcopy(row); start = result.index((100,'AcDbTableGeometry'))+1
    check([c for c,v in result[start:start+3]] == [90,91,92], 'Native geometry header grammar changed')
    count = result[start+2][1]; at = start+3
    for cell in range(count):
        check([c for c,v in result[at:at+5]] == [93,40,41,330,94], 'Native geometry cell grammar changed')
        length = result[at+4][1]
        check(result[at+3] == (330,'0'), 'Pinned geometry target must begin null')
        if cell == 0 and phase != 'input':
            result[at] = (93,result[at][1]^128); result[at+1] = (40,result[at+1][1]+.5); result[at+2] = (41,result[at+2][1]-.25)
            result[at+3] = (330,manager_handle if phase in ('linked','retry') else '0')
        at += 5
        for _ in range(length):
            check([c for c,v in result[at:at+7]] == [10,11,43,44,45,46,95], 'Native geometry content grammar changed')
            if cell == 0 and phase != 'input': result[at+2] = (43,result[at+2][1]+.125)
            at += 7
    check(at == len(result), 'Unexpected geometry packet suffix')
    return result


def content_packet(row, phase, year):
    result = copy.deepcopy(row); start = result.index((100,'AcDbLinkedData'))
    check([c for c,v in result[start:start+3]] == [100,1,300], 'Native content small header grammar changed')
    frames = []
    for index,tag in enumerate(result):
        if tag != (1,'CELLCONTENT_BEGIN') or result[index-1] != (302,'CONTENT'):continue
        if result[index+1:index+3] != [(90,1),(300,'VALUE')] or result[index+3] not in ((93,2),(93,4),(93,6)):continue
        scalar = index+5; kind = result[scalar-1]
        if kind[0] != 90 or kind[1] not in (1,2,4,32) or result[scalar][0] != {1:91,2:140,4:1,32:11}[kind[1]]:continue
        after = scalar+1
        if [c for c,v in result[after:after+4]] != [94,300,302,304] or result[after+3] != (304,'ACVALUE_END'):continue
        if result[after+4:after+6] != [(91,0),(309,'CELLCONTENT_END')]:continue
        frames.append((scalar,after+2))
    check(len(frames) == (7 if year == 2013 else 20), 'Pinned qualified native scalar frame inventory changed')
    if phase != 'input':
        result[start+1] = (1,'Tenth edited name'); result[start+2] = (300,'Tenth edited description')
        scalar,display = frames[0]; code,value = result[scalar]
        value = value+7 if code == 91 else value+.125 if code == 140 else 'Tenth '+value if code == 1 else tuple(a+b for a,b in zip(value,(1.,2.,3.)))
        result[scalar] = (code,value); result[display] = (302,'Tenth explicit display')
    return result


def mesh_packet(records):
    identity,row = unique(records,'POLYLINE',lambda r:(8,'TENTH_GRAPH') in r)
    ordered = list(records.items()); at = next(i for i,(h,_) in enumerate(ordered) if h == identity)
    children = ordered[at+1:at+7]
    check([r[0][1] for h,r in children] == ['VERTEX']*5+['SEQEND'], 'Retained Polyface physical sequence framing changed')
    face = children[0]; coordinates = children[1:5]; end = children[5]
    check(field(row,70) == 64 and field(row,71) == -17 and field(row,72) == 123, 'Polyface advisory counts/header flags changed')
    check(field(face[1],70) == 128 and [(c,v) for c,v in face[1] if 71<=c<=74] == [(74,-32768),(72,0),(71,-1),(73,32767)], 'Face-first inactive values/order or active signed slot changed')
    check(first(face[1],10) == (0.,0.,0.), 'Face dummy coordinate changed')
    expected = [(0.,0.,0.),(2.,0.,0.),(0.,3.,0.),(2.,3.,0.)]
    for (h,record), point in zip(coordinates,expected):
        check(field(record,70) == 192 and first(record,10) == point, 'Coordinate order/position/role changed')
        check(not any(71<=c<=74 for c,v in record), 'Coordinate gained face slots')
    check(all(metadata(record)[0] == identity for h,record in children), 'Polyface child actual owner changed')
    return identity,row,children,face


def verify_pattern(row, before, transformed, year):
    marker = row.index((100,'AcDbHatch')); old_marker = before.index((100,'AcDbHatch'))
    check(row[:marker] == before[:old_marker], 'HATCH common identity/metadata changed')
    old = basis((0,0,1) if year == 2013 else (1,2,3)); ocs = basis(first(row,210)); elevation = first(row,10)[2]
    if not transformed:
        near(old[:,2],np.array(first(row,210)),'Input pattern plane normal')
        check(first(row,10) == (0.,0.,4.) and first(row,52) == 27 and first(row,41) == .75, 'Input HATCH global plane/pattern fields changed')
        for code,value in ((2,'AFFINE_EXPLICIT'),(70,0),(71,0),(75,0),(76,2),(77,0),(78,1),(98,1)):
            check(first(row,code)==value,'Input pattern header changed: '+str(code))
        expected_base = .75*np.array([[math.cos(math.radians(27)),-math.sin(math.radians(27))],[math.sin(math.radians(27)),math.cos(math.radians(27))]])@np.array([1.,2.])
        line = families(row); check(len(line) == 1,'Input pattern family count changed')
        near(expected_base,line[0]['base'],'Input pattern local base/global frame')
        angle = math.radians(42); expected_offset = .75*np.array([[math.cos(angle),-math.sin(angle)],[math.sin(angle),math.cos(angle)]])@np.array([.5,3.])
        near(expected_offset,line[0]['offset'],'Input pattern longitudinal/perpendicular offset')
        check(line[0]['angle'] == 42 and line[0]['dashes'] == [1.5,-.75,0.,-0.,-.375], 'Input pattern signed family data changed')
        check(struct.pack('>d',line[0]['dashes'][2])==struct.pack('>d',0.) and struct.pack('>d',line[0]['dashes'][3])==struct.pack('>d',-0.),'Input signed-zero dots changed')
        near(np.array([3.,4.,0.]),np.array(first(acad(row),1010)),'Input separate WCS pattern origin')
        flags,edges = paths(row); check(len(edges) == 4 and all(e['kind']==1 for e in edges), 'Input rectangular boundary changed')
        expected_edges = [((0.,0.),(2.,0.)),((2.,0.),(2.,3.)),((2.,3.),(0.,3.)),((0.,3.),(0.,0.))]
        for edge,(start,end) in zip(edges,expected_edges): near(np.array(start),edge['start'],'Input boundary start'); near(np.array(end),edge['end'],'Input boundary end')
        seed_at = next(i for i,t in enumerate(row) if t[0] == 98); check(row[seed_at+1] == (10,(0.,0.)), 'Input HATCH seed point changed')
        return 0
    check(first(row,10)[:2] == (0.,0.), 'HATCH elevation point X/Y must remain zero')
    a = matrix(3); translation = np.array([7.,-11.,0.])
    x = a@old[:,0]; y = a@old[:,1]; normal = np.cross(x,y); normal /= np.linalg.norm(normal)
    if np.dot(normal,a@old[:,2]) < 0: normal = -normal
    near(normal,np.array(first(row,210)),'Actual output HATCH normal')
    near(np.array([np.dot(normal,a@(4*old[:,2])+translation)]),np.array([elevation]),'Actual output HATCH elevation')
    direction = np.array([math.cos(math.radians(first(before,52))),math.sin(math.radians(first(before,52))),0.])*first(before,41)
    mapped = ocs.T@a@old@direction
    near(np.array([np.linalg.norm(mapped[:2])]),np.array([first(row,41)]),'Stored global pattern scale')
    angle = math.degrees(math.atan2(mapped[1],mapped[0]))%360
    near(np.array([angle]),np.array([first(row,52)]),'Stored global pattern angle')
    old_lines,new_lines = families(before),families(row); check(len(old_lines)==len(new_lines)==1,'Pattern family count changed')
    probes = 0
    for wanted,line in zip(old_lines,new_lines):
        direction = a@old@np.append(wanted['direction'],0); stretch = np.linalg.norm(direction)
        near(direction/stretch,ocs@np.append(line['direction'],0),'World family direction')
        check(len(line['dashes']) == len(wanted['dashes']),'Pattern dash inventory changed')
        for previous,current in zip(wanted['dashes'],line['dashes']):
            check(math.copysign(1,previous)==math.copysign(1,current) and (previous==0)==(current==0),'Signed dash/gap/dot changed')
            near(np.array([previous*stretch]),np.array([current]),'World signed dash length')
            if previous == 0:check(struct.pack('>d',previous)==struct.pack('>d',current),'Signed-zero dot storage changed')
        expected = world(old,samples(wanted),4)@a.T+translation; observed = world(ocs,samples(line),elevation)
        near(expected,observed,'World pattern family/phase/dash samples'); probes += len(expected)
        near(world(old,np.array([wanted['offset']]),0)@a.T,world(ocs,np.array([line['offset']]),0),'World family offset')
    old_flags,old_edges = paths(before); flags,edges = paths(row)
    check(flags == old_flags and len(edges) == len(old_edges) == 4 and all(e['kind']==1 for e in edges),'Boundary flags/edge grammar changed')
    for previous,current in zip(old_edges,edges): near(world(old,points(previous),4)@a.T+translation,world(ocs,points(current),elevation),'World HATCH boundary')
    prior_acad,actual_acad = acad(before),acad(row); old_origin = next(i for i,t in enumerate(prior_acad) if t[0]==1010)
    near(np.array(prior_acad[old_origin][1])@a.T+translation,np.array(actual_acad[old_origin][1]),'Separate WCS pattern Origin')
    check(actual_acad[old_origin][1][2] == 0 and prior_acad[:old_origin]+prior_acad[old_origin+1:] == actual_acad[:old_origin]+actual_acad[old_origin+1:],'Other ACAD data changed')
    allowed = {10,11,41,43,44,45,46,49,52,53,210,1010}
    check([(c,exact(v)) for c,v in row if c not in allowed] == [(c,exact(v)) for c,v in before if c not in allowed], 'HATCH fields outside qualified affine data changed')
    check([c for c,v in row] == [c for c,v in before] and first(row,98) == 1,'HATCH raw packet order/seed inventory changed')
    seed_at = next(i for i,t in enumerate(row) if t[0] == 98); old_seed_at = next(i for i,t in enumerate(before) if t[0] == 98)
    check(row[seed_at+1][0] == 10 and before[old_seed_at+1][0] == 10, 'HATCH seed point grammar changed')
    near(world(old,np.array([before[old_seed_at+1][1]]),4)@a.T+translation,world(ocs,np.array([row[seed_at+1][1]]),elevation),'World HATCH seed point')
    return probes


def section_inventory(records):
    values = [(field(public(r,'AcDbSection'),1),h,r) for h,r in records.items() if r[0] == (0,'SECTIONOBJECT')]
    check(len(values) == len({name for name,h,r in values}), 'Duplicate named SECTION records')
    return {name:(h,r) for name,h,r in values}


def validate(records, classes, original, before, phase, year):
    map_handle = native_packets(records,original)
    manager_handle,old_manager = unique(before,'SECTION_MANAGER'); root = metadata(old_manager)[0]
    check(root in records and metadata(records[root])[0] in (None,'0'),'Actual original root identity changed')
    erased = phase in ('manager-erased','opaque-guard','released'); released = phase == 'released'; linked = phase in ('linked','retry')
    initial_sections = section_inventory(before); sections = section_inventory(records)
    check(set(sections) == ({'Tenth second'} if phase in ('opaque-guard','released') else {'Tenth first','Tenth second'}),'SECTION lifecycle inventory changed')
    mesh_handle,old_mesh,children,face = mesh_packet(before); face_handle = face[0]
    opaque_handle,old_opaque = unique(before,OPAQUE)
    hatch_handle,old_hatch = unique(before,'HATCH',lambda r:(8,'TENTH_PATTERN') in r)
    for h,row in original.items():
        if row[0][1] in ('ACAD_TABLE','TABLESTYLE','CELLSTYLEMAP'):
            check(h in records and exact(records[h]) == exact(native_record(row)),'Complete native stored carrier packet changed: '+row[0][1]+'/'+h)
        if row[0] == (0,'TABLECONTENT'):
            check(h in records and exact(records[h]) == exact(content_packet(row,phase,year)),'Explicit native content header/scalar/display edit or untouched packet changed')
            owner = metadata(row)[0]; check(records[owner] == original[owner], 'Native content wrapper packet changed')
        if row[0] == (0,'TABLEGEOMETRY'):
            check(h in records and exact(records[h]) == exact(geometry_packet(row,manager_handle,phase)),'Explicit native geometry edit/reference packet changed')
            owner = metadata(row)[0]; check(records[owner] == original[owner],'Native geometry wrapper packet changed')
    if erased:
        check(not any(r[0][1] in ('SECTION_MANAGER','SECTIONMANAGER') for r in records.values()),'Erased manager survived or was replaced')
        check(not any(name.upper() == 'ACAD_SECTION_MANAGER' for name,c,h in dictionary_edges(records[root])),'Erased manager anchor survived')
        check(manager_handle not in records,'Erased manager identity was reused')
    else:
        identity,row = unique(records,'SECTION_MANAGER'); check(identity==manager_handle,'Actual manager identity was rewritten')
        check(row[:row.index((100,'AcDbSectionManager'))] == old_manager[:old_manager.index((100,'AcDbSectionManager'))],'Complete manager common packet changed')
        first_section,second_section = initial_sections['Tenth first'][0],initial_sections['Tenth second'][0]
        targets = [second_section,first_section,second_section] if linked else [first_section,second_section,first_section]
        check(public(row,'AcDbSectionManager') == [(70,0 if linked else 1),(90,3)]+[(330,h) for h in targets],'Manager ordered update/list packet changed')
        check(('ACAD_SECTION_MANAGER',350,manager_handle) in dictionary_edges(records[root]),'Original manager soft root anchor changed')
        check(metadata(row)==(root,[root],None),'Canonical manager root ownership/reactor metadata changed')
    expected_root = list(before[root])
    if erased:
        anchor = expected_root.index((3,'ACAD_SECTION_MANAGER')); check(expected_root[anchor+1] == (350,manager_handle), 'Original root anchor frame changed'); del expected_root[anchor:anchor+2]
    check(records[root] == expected_root, 'Root common flags/metadata or unrelated named entries changed')
    settings_ids = set()
    for name,(identity,row) in sections.items():
        old_identity,old_row = initial_sections[name]; settings = field(public(old_row,'AcDbSection'),360); settings_ids.add(settings)
        check(identity == old_identity and row == old_row,'Surviving SECTION full plane/appearance/identity packet changed')
        check(public(row,'AcDbSection') == section_packet(name,settings),'Authored SECTION public packet changed')
        check(settings in records and metadata(records[settings])[0] == identity,'SECTION settings owner changed')
        old_settings = before[settings]; prefix = old_settings[:old_settings.index((100,'AcDbSectionSettings'))]
        targets = [map_handle] if phase in ('opaque-guard','released') else [map_handle,face_handle,'0',face_handle]
        check(records[settings] == prefix+settings_packet(targets),'SECTION generic source order/repetition/null/count packet changed')
    check({h for h,r in records.items() if r[0] == (0,'SECTIONSETTINGS')} == settings_ids,'Erased SECTION settings survived or were replaced')
    if released:
        check(not any(r[0] == (0,'POLYLINE') and (8,'TENTH_GRAPH') in r for r in records.values()),'Released Polyface was retained or replaced')
        check(not any(r[0] == (0,OPAQUE) for r in records.values()),'Released opaque source was retained or replaced')
        check(not any(r[0] == (0,'HATCH') and (8,'TENTH_PATTERN') in r for r in records.values()),'Released HATCH was retained or replaced')
        check(all(h not in records for h in [mesh_handle,opaque_handle,hatch_handle]+[h for h,r in children]),'Retired graph identity was reused')
        probes = 0
    else:
        identity,row,current_children,current_face = mesh_packet(records)
        check(identity == mesh_handle and row == old_mesh and current_children == children,'Retained Polyface full header/physical child packets changed')
        check((1001,'TENTH_FACE_LINK') in current_face[1] and (1000,'authored retained face reference') in current_face[1] and (1005,hatch_handle) in current_face[1],'Authored face-to-HATCH semantic XData changed')
        identity,row = unique(records,OPAQUE)
        check(identity == opaque_handle and row == old_opaque,'Complete opaque source packet/identity changed')
        check(row == [(0,OPAQUE),(5,opaque_handle),(330,metadata(old_mesh)[0]),(100,'AcDbEntity'),(8,'TENTH_GRAPH'),(100,'AcDbQualifiedFutureCurve'),(340,face_handle)],'Declared opaque source pointer/owner contract changed')
        check(face_handle in records,'Opaque target lacks its physical source record')
        identity,row = unique(records,'HATCH',lambda r:(8,'TENTH_PATTERN') in r); check(identity == hatch_handle,'Retained HATCH identity changed')
        probes = verify_pattern(row,old_hatch,phase != 'input',year)
    declarations = [r for r in classes if (1,'SECTION_MANAGER') in r]
    check(declarations == [[(0,'CLASS'),(1,'SECTION_MANAGER'),(2,'AcDbSectionManager'),(3,'ObjectDBX Classes'),(90,1024),(91,0 if erased else 1),(280,0),(281,0)]],'Manager CLASS metadata/count changed')
    for name,subclass,flags,entity in [('SECTIONOBJECT','AcDbSection',1025,1),('SECTIONSETTINGS','AcDbSectionSettings',1024,0)]:
        declarations = [r for r in classes if (1,name) in r]
        check(declarations == [[(0,'CLASS'),(1,name),(2,subclass),(3,'ObjectDBX Classes'),(90,flags),(91,len(sections)),(280,0),(281,entity)]], 'SECTION/settings CLASS metadata or live count changed')
    check(not any((1,OPAQUE) in row for row in classes),'Declared class-free opaque source gained CLASS metadata')
    return probes


def remap_records(records, old, new):
    mapped = {}
    for identity,row in records.items():
        mapped[new if identity == old else identity] = [(c,new if v == old and (c in (5,105,1005) or 330<=c<=369 or 390<=c<=399 or c in (480,481)) else v) for c,v in row]
    return mapped



def ascii_pairs(data):
    lines=data.decode('utf-8-sig').splitlines();check(len(lines)%2==0,'ASCII control packet framing')
    return [(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]


def ascii_row(pairs,kind,identity=None,name=None):
    starts=[i for i,t in enumerate(pairs) if t[0]==0]
    for index,start in enumerate(starts):
        end=starts[index+1] if index+1<len(starts) else len(pairs);row=pairs[start:end]
        if row[0] == (0,kind) and (identity is None or (5,identity) in row) and (name is None or (1,name) in row):return start,end,list(row)
    raise ValueError('Missing ASCII control source row '+kind)


def append_section_record(pairs,section,row):
    current=None
    for index,(code,value) in enumerate(pairs):
        if (code,value)==(0,'SECTION'):current=pairs[index+1][1]
        elif (code,value)==(0,'ENDSEC'):
            if current==section:pairs[index:index]=row;return
            current=None
    raise ValueError('Missing target physical section '+section)


def qualified_handle_code(code):
    return code in (5,105,1005,480,481) or 330<=code<=369 or 390<=code<=399


def actual_ascii_controls(artifacts):
    file='acad_table_with_blk_ref.dxf';year=2018;basename='tenth-mixed-'+file+'-False-False-'
    original,_,_=source(ROOT,file);_,before,_=load(artifacts/(basename+'input.dxf'),year,False)
    source_pairs=ascii_pairs((artifacts/(basename+'input.dxf')).read_bytes())
    manager_handle,manager_row=unique(before,'SECTION_MANAGER');root=metadata(manager_row)[0]
    second,second_row=section_inventory(before)['Tenth second'];settings=field(public(second_row,'AcDbSection'),360)
    mesh_handle,mesh,children,face=mesh_packet(before);hatch_handle,_=unique(before,'HATCH',lambda r:(8,'TENTH_PATTERN') in r)
    cases=('manager-identity','section-identity','settings-identity','new-manager-after-release','new-polyface-after-release','new-opaque-after-release')
    results=[]
    with tempfile.TemporaryDirectory() as temporary:
        for kind in cases:
            phase='released' if 'after-release' in kind else 'linked'
            pairs=ascii_pairs((artifacts/(basename+phase+'.dxf')).read_bytes())
            if kind in ('manager-identity','section-identity','settings-identity'):
                old={'manager-identity':manager_handle,'section-identity':second,'settings-identity':settings}[kind]
                replacement={'manager-identity':'BEEF01','section-identity':'BEEF02','settings-identity':'BEEF03'}[kind]
                pairs=[(c,replacement if v==old and qualified_handle_code(c) else v) for c,v in pairs]
            elif kind=='new-manager-after-release':
                handle='BEEF40';start,end,row=ascii_row(pairs,'DICTIONARY',root);pairs[start:end]=row+[(3,'ACAD_SECTION_MANAGER'),(350,handle)]
                start,end,row=ascii_row(pairs,'CLASS',name='SECTION_MANAGER');pairs[start:end]=[(c,'1' if c==91 else v) for c,v in row]
                append_section_record(pairs,'OBJECTS',[(0,'SECTION_MANAGER'),(5,handle),(102,'{ACAD_REACTORS'),(330,root),(102,'}'),(330,root),(100,'AcDbSectionManager'),(70,'0'),(90,'1'),(330,second)])
            elif kind=='new-polyface-after-release':
                old_handles=[mesh_handle]+[h for h,r in children];mapping={h:format(0xBEEF50+i,'X') for i,h in enumerate(old_handles)};rows=[]
                for index,handle in enumerate(old_handles):
                    code='POLYLINE' if index==0 else children[index-1][1][0][1]
                    _,_,row=ascii_row(source_pairs,code,handle)
                    rows.extend((c,mapping[v] if qualified_handle_code(c) and v in mapping else second if c==1005 and v==hatch_handle else v) for c,v in row)
                append_section_record(pairs,'ENTITIES',rows)
            else:
                owner=metadata(mesh)[0]
                append_section_record(pairs,'ENTITIES',[(0,OPAQUE),(5,'BEEF70'),(330,owner),(100,'AcDbEntity'),(8,'TENTH_GRAPH'),(100,'AcDbQualifiedFutureCurve'),(340,second)])
            seed=next(i+1 for i,t in enumerate(pairs) if t==(9,'$HANDSEED'));check(pairs[seed][0]==5,'HANDSEED framing');pairs[seed]=(5,'C00000')
            path=Path(temporary)/(kind+'.dxf');path.write_text(''.join(f'{c}\n{v}\n' for c,v in pairs),encoding='utf-8')
            doc,records,classes=load(path,year,False);audit=doc.audit();check(not audit.errors and not audit.fixes,'Coherent replacement control is not independently valid: '+kind)
            try:validate(records,classes,original,before,phase,year)
            except (ValueError,KeyError,StopIteration,IndexError) as error:results.append({'control':kind,'rejected_for':str(error),'audit_errors':0,'audit_repairs':0})
            else:raise ValueError('Coherent actual-byte replacement escaped: '+kind)
    check(len(results)==6,'Coherent ASCII control inventory changed')
    return results


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    expected={f'tenth-mixed-{file}-{i}-{o}-{phase}.dxf' for file in FILES for i in (False,True) for o in (False,True) for phase in PHASES}
    check({p.name for p in args.artifacts.glob('tenth-mixed-*.dxf')} == expected,'All 48 mixed graph outputs are mandatory')
    outputs=controls=probes=0
    for file,year in FILES.items():
        original,_,actual=source(ROOT,file);check(actual==year,'Pinned native source profile changed')
        for input_binary in (False,True):
            for output_binary in (False,True):
                basename=f'tenth-mixed-{file}-{input_binary}-{output_binary}-'
                _,before,_=load(args.artifacts/(basename+'input.dxf'),year,output_binary)
                for phase in PHASES:
                    doc,records,classes=load(args.artifacts/(basename+phase+'.dxf'),year,output_binary)
                    probes+=validate(records,classes,original,before,phase,year);audit=doc.audit();check(not audit.errors and not audit.fixes,'Mixed output requires audit repair')
                    # Coherent identity rewrites preserve all incoming references. The input
                    # snapshot must still reject replacement of a surviving physical identity.
                    defects=['map','geometry','manager-class','section-identity','settings-identity','content-header','content-display','section-class','root-flag']
                    if phase not in ('manager-erased','opaque-guard','released'):defects+=['manager-identity','manager-order']
                    if phase!='released':defects+=['polyface-counts','inactive-slot','opaque-pointer','pattern-offset','pattern-origin','pattern-elevation-xy','pattern-seed']
                    for defect in defects:
                        bad=copy.deepcopy(records);bad_classes=copy.deepcopy(classes)
                        if defect=='map':h,r=unique(bad,'CELLSTYLEMAP');r.append((1,'unexpected'))
                        elif defect.startswith('content-'):
                            h,r=unique(bad,'TABLECONTENT')
                            at=r.index((100,'AcDbLinkedData'))+1 if defect=='content-header' else next(i+1 for i,t in enumerate(r) if t==(300,'') and i+1<len(r) and r[i+1][0]==302)
                            r[at]=(r[at][0],'Corrupted explicit text')
                        elif defect=='geometry':h,r=unique(bad,'TABLEGEOMETRY');at=r.index((100,'AcDbTableGeometry'));r[at+3]=(92,r[at+3][1]+1)
                        elif defect=='root-flag':
                            _,m=unique(before,'SECTION_MANAGER');r=bad[metadata(m)[0]];at=next(i for i,t in enumerate(r) if t[0]==281);r[at]=(281,2 if r[at][1]!=2 else 3)
                        elif defect=='section-class':
                            name='SECTIONOBJECT' if PHASES.index(phase)%2==0 else 'SECTIONSETTINGS';r=next(r for r in bad_classes if (1,name) in r);at=next(i for i,t in enumerate(r) if t[0]==91);r[at]=(91,37)
                        elif defect=='manager-class':r=next(r for r in bad_classes if (1,'SECTION_MANAGER') in r);at=next(i for i,t in enumerate(r) if t[0]==91);r[at]=(91,37)
                        elif defect in ('section-identity','settings-identity'):
                            h,r=section_inventory(bad)['Tenth second'];old=h if defect=='section-identity' else field(public(r,'AcDbSection'),360);bad=remap_records(bad,old,'BEEF10')
                        elif defect=='manager-identity':h,r=unique(bad,'SECTION_MANAGER');bad=remap_records(bad,h,'BEEF11')
                        elif defect=='manager-order':h,r=unique(bad,'SECTION_MANAGER');at=r.index((100,'AcDbSectionManager'));r[at+3],r[at+4]=r[at+4],r[at+3]
                        elif defect=='polyface-counts':h,r,_,_=mesh_packet(bad);at=next(i for i,t in enumerate(r) if t[0]==71);r[at]=(71,4)
                        elif defect=='inactive-slot':_,_,_,(_,r)=mesh_packet(bad);at=next(i for i,t in enumerate(r) if t[0]==73);r[at]=(73,4)
                        elif defect=='opaque-pointer':h,r=unique(bad,OPAQUE);at=next(i for i,t in enumerate(r) if t[0]==340);r[at]=(340,unique(bad,'HATCH',lambda r:(8,'TENTH_PATTERN') in r)[0])
                        else:
                            h,r=unique(bad,'HATCH',lambda r:(8,'TENTH_PATTERN') in r)
                            if defect=='pattern-seed':at=next(i+1 for i,t in enumerate(r) if t[0]==98);r[at]=(10,(r[at][1][0]+.25,r[at][1][1]))
                            elif defect=='pattern-elevation-xy':at=next(i for i,t in enumerate(r) if t[0]==10);r[at]=(10,(.125,r[at][1][1],r[at][1][2]))
                            elif defect=='pattern-offset':at=next(i for i,t in enumerate(r) if t[0]==45);r[at]=(45,r[at][1]+.125)
                            else:at=next(i for i,t in enumerate(r) if t[0]==1010);r[at]=(1010,(r[at][1][0]+1,*r[at][1][1:]))
                        try:validate(bad,bad_classes,original,before,phase,year)
                        except (ValueError,KeyError,StopIteration,IndexError):controls+=1
                        else:raise ValueError('Mixed corruption escaped: '+phase+'/'+defect)
                    outputs+=1
    check(outputs==48 and controls==760 and probes==5280,'Mixed output/control/world-probe totals changed')
    coherent=actual_ascii_controls(args.artifacts)
    print(json.dumps({'coherent_actual_ascii_controls':coherent,'total_controls':controls+len(coherent),'outputs':outputs,'actual_output_corruptions_rejected':controls,'world_pattern_probes':probes,'audit_errors':0,'audit_repairs':0,'native_cad_execution':False,'synthetic_retained_polyface':True,'declared_schema_opaque_neighbor':True},sort_keys=True))

if __name__=='__main__':main()
