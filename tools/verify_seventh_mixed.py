#!/usr/bin/env python3
"""Check native association/table packets and mixed mutable ownership from actual output.

Only R2004 and R2018 are exercised. Native DIMASSOC components use the declared
uniform extraction map and one external model-owner substitution. FIELD, UCS,
SECTION and SUN links are authored scaffolds; no evaluator or CAD execution runs.
"""
from pathlib import Path
import argparse, copy, gzip, hashlib, json, tempfile
import ezdxf
from verify_mleader_inputs import records, check, exact, json_value
from verify_section import body, one, owner, dictionary, extension, control, classes
from verify_section_settings import payload as settings_payload
from verify_sun import packet as sun_packet
from verify_dimassoc import source_inputs as dimassoc_sources

ROOT = Path(__file__).resolve().parents[1]
YEARS = (2004, 2018)
PHASES = ('original', 'released', 'cloned', 'erased', 'context')
PROFILES = {2004: 'AC1018', 2018: 'AC1032'}

def native_sources():
    manifest = json.loads((ROOT/'tests/fixtures/table-content/manifest.json').read_text())
    result = {}
    with tempfile.TemporaryDirectory() as temporary:
        for year, name in ((2004, 'sample_AC1018_ascii.dxf'), (2018, 'acad_table_with_blk_ref.dxf')):
            fixture = next(item for item in manifest['files'] if item['file'] == name)
            path = ROOT/'tests/fixtures/table-content'/fixture['fixture']; data = path.read_bytes()
            if path.suffix == '.gz': data = gzip.decompress(data)
            check(hashlib.sha256(data).hexdigest() == fixture.get('sha256', fixture['source_sha256']), 'Native table carrier hash differs')
            path = Path(temporary)/name; path.write_bytes(data); result[year] = records(path)
    return result

def field_payload(refs):
    return [[100,'AcDbField'],[1,'SeventhStored'],[2,r'no evaluation: \U+03A9'],[90,0],[97,len(refs)]] + [[331,h] for h in refs] + [
        [91,63],[92,0],[93,0],[7,'ACFD_FIELD_VALUE'],[90,0],[91,0],[301,'####'],[310,{'hex':'007fff'}],[320,'F0F0F0']]

def native_packets(wire, year, tables, associations):
    """Compare retained packets with pinned producer inputs, never emitted expectations."""
    native = tables[year]; fixture, association = associations[year]; mapping = fixture['handle_map']; checked = 0; geometries = set()
    models = [h for h,t in wire.items() if t[0] == [0,'BLOCK_RECORD'] and [2,'*Model_Space'] in t]
    check(len(models) == 1, 'One physical model-space owner is required')
    for source_handle in fixture['associations']:
        handle = mapping[source_handle]
        before = [[tag.code,json_value(tag.value)] for tag in association[handle]]
        check(exact(wire[handle]) == exact(before), 'Native DIMASSOC common/body/reactor packet differs: '+handle); checked += 1
        parent = owner(before)
        check(exact(wire[parent]) == exact([[tag.code,json_value(tag.value)] for tag in association[parent]]), 'Native DIMASSOC owner dictionary differs'); checked += 1
        dimension = one(body(before,'AcDbDimAssoc'),330)
        check(owner(wire[dimension]) == models[0], 'Native DIMENSION external model owner differs')
        check(extension(wire[dimension]) == parent and [330,handle] in control(wire[dimension],'{ACAD_REACTORS'), 'Native DIMENSION reciprocal association attachment differs')
        for code,target in body(before,'AcDbDimAssoc'):
            # Native42F is the explicitly opaque repeated POLYLINE/VERTEX path.
            # Its packet is exact above; internal VERTEX retention is a separate
            # ledger gap, so it has no qualified geometry projection here.
            if code == 331 and source_handle != '42F':
                check(target in wire and wire[target][0] == [association[target][0].code, association[target][0].value], 'Native source geometry identity/type differs')
                geometries.add(target)
    for handle in sorted(geometries):
        before = [[tag.code,json_value(tag.value)] for tag in association[handle]]; after = wire[handle]
        check(owner(after) == models[0], 'Native geometry external model owner differs')
        check(control(after,'{ACAD_REACTORS') == control(before,'{ACAD_REACTORS'), 'Native geometry reactor sequence differs')
        marker = 'AcDbPolyline' if before[0][1] == 'LWPOLYLINE' else 'AcDbCircle'
        old = body(before,marker); new = body(after,marker); codes = {c for c,v in old}
        # These two native geometry kinds materialize documented zero/default
        # fields. All original scalar/point sequences remain exactly compared.
        defaults = {38:0.,39:0.,42:0.,210:[0.,0.,1.]}
        check(all(c in defaults and exact(v) == exact(defaults[c]) for c,v in new if c not in codes), 'New native geometry field is not an independent default')
        check(exact([tag for tag in new if tag[0] in codes]) == exact(old), 'Native geometry scalar/point packet differs'); checked += 1
    # Opaque native geometry/maps and composite row descendants have complete
    # retained bodies. Compare every such original object, excluding generated
    # scaffold dictionaries and native entity codecs whose default presence can
    # legitimately differ after typed load.
    retained = {'ACAD_TABLE','TABLECONTENT','TABLEGEOMETRY','TABLESTYLE','CELLSTYLEMAP','DATATABLE','XRECORD'}
    wrappers = {owner(packet) for packet in native.values() if packet[0] == [0,'TABLECONTENT']}
    for handle, before in native.items():
        if before[0][1] not in retained: continue
        if before[0][1] == 'XRECORD' and year == 2018 and handle not in wrappers:
            # Other unrelated native XRecords are outside this mixed increment.
            continue
        markers = [i for i,t in enumerate(before) if t[0] == 100]
        if not markers: continue
        check(handle in wire and wire[handle][0] == before[0], 'Native table retained object missing or wrong kind')
        after = wire[handle]; start = before.index([100,'AcDbBlockReference']) if before[0][1] == 'ACAD_TABLE' else markers[0]
        check(exact(after[after.index(before[start]):]) == exact(before[start:]), 'Native table complete body differs: '+handle); checked += 1
        check(owner(after) == owner(before), 'Native table owned identity changed: '+handle)
        if before[0][1] == 'TABLECONTENT':
            for code,target in before[start:]:
                if (330 <= code <= 369 or 390 <= code <= 399 or code in (480,481)) and int(target,16):
                    check(target in native and target in wire and wire[target][0] == native[target][0], 'Native TABLECONTENT semantic target is not its original physical kind')
    return checked

def validate(path, year, binary, phase, tables, associations, override=None, audit=True):
    wire = records(path) if override is None else override
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Mixed output transport differs')
    doc = ezdxf.readfile(path); check(doc.dxfversion == PROFILES[year], 'Mixed output profile differs')
    folder = dictionary(wire, doc.rootdict.dxf.handle)['SEVENTH_MIXED']; slots = dictionary(wire,folder)
    check(set(slots) == {'PREFIX','FIELD'}, 'Mixed folder inventory differs')
    field = slots['FIELD']; check(owner(wire[field]) == owner(wire[slots['PREFIX']]) == folder, 'Mixed field/prefix ownership differs')
    check(body(wire[slots['PREFIX']], 'AcDbXrecord') == [[280,1],[1,'mutable sibling survives refused clone/erase']], 'Mutable sibling was changed by refused lifecycle operation')
    packet = wire[field][wire[field].index([100,'AcDbField']):]; refs = [v for c,v in packet if c == 331]
    for name,cpp in (('FIELD','AcDbField'),('DIMASSOC','AcDbDimAssoc'),('TABLECONTENT','AcDbTableContent')):
        definition = doc.classes.get(name).dxf
        check(definition.cpp_class_name == cpp and not definition.is_an_entity and
            definition.instance_count == sum(tags[0] == [0,name] for tags in wire.values()), 'Stored family CLASS identity or instance count differs: '+name)
    check(len(refs) == (9 if year == 2004 else 13), 'Mixed FIELD reference inventory differs')
    base,child,association,dimension,geometry,content,style,repeat,null = refs[:9]
    check(repeat == geometry and null == '0' and all(handle in wire for handle in refs if handle != '0'), 'Mixed FIELD repeated/null/physical target identity differs')
    check(packet == field_payload(refs), 'Complete FIELD code/cache/dependency packet differs')
    check(wire[association][0] == [0,'DIMASSOC'] and one(body(wire[association],'AcDbDimAssoc'),330) == dimension, 'FIELD association and dimension targets diverge')
    check(association == associations[year][0]['handle_map']['452'] and geometry == associations[year][0]['handle_map']['419'], 'Mixed fixture selected native identities changed')
    check([v for c,v in body(wire[association],'AcDbDimAssoc') if c == 331] == [geometry]*4, 'Native four-point geometry references differ')
    check(content == ('747' if year == 2004 else '116') and wire[content][0] == [0,'TABLECONTENT'], 'Mixed native content identity differs')
    check(one(body(wire[content],'AcDbTableContent'),340) == style and wire[style][0] == [0,'TABLESTYLE'], 'FIELD and TABLECONTENT actual table-style target differ')
    released = phase in ('released','cloned','erased')
    for handle,name in ((base,'SEVENTH_BASE_RENAMED' if released else 'SEVENTH_BASE'), (child,'SEVENTH_CHILD')):
        check(wire[handle][0] == [0,'UCS'] and one(wire[handle],2) == name, 'Shared UCS resource identity/name differs')
    bp = body(wire[base],'AcDbUCSTableRecord'); cp = body(wire[child],'AcDbUCSTableRecord')
    check(one(bp,10) == [1.25,-2.5,3.75] and one(bp,146) == 4.125, 'Base UCS geometric fields differ')
    check(one(cp,79) == (0 if released else 5), 'Stored UCS orthographic type differs')
    check([v for c,v in cp if c == 346] == ([] if released else [base]), 'Stored UCS base handle or absence differs')
    index = cp.index([71,6]); check(cp[index+1] == [13,[7.,8.,9.]], 'Independent UCS origin override differs')
    originals = {folder,field,slots['PREFIX'],base,child,association,dimension,geometry,content,style}; clones = set()
    copies = [h for h,t in wire.items() if t[0] == [0,'UCS'] and [2,'SEVENTH_UCS_COPY'] in t]
    check(len(copies) == (1 if phase == 'cloned' else 0), 'UCS clone inventory differs')
    for handle in copies:
        current = body(wire[handle],'AcDbUCSTableRecord')
        check([tag for tag in current if tag[0] != 2] == [tag for tag in cp if tag[0] != 2], 'Copied UCS stored packet differs')
        clones.add(handle)
    if year == 2018:
        section,settings,sun,table = refs[9:]; originals |= {section,settings,sun,table}
        check(wire[table][0] == [0,'ACAD_TABLE'], 'FIELD native TABLE reference type differs')
        # The native entity points to its wrapper; its TABLECONTENT identity is
        # established by the exact retained wrapper, not a synthesized cache.
        sections = [h for h,t in wire.items() if [100,'AcDbSection'] in t]
        suns = [h for h,t in wire.items() if t[0] == [0,'SUN'] and
            any(tag in ([2,'SEVENTH_SUN_OWNER'],[2,'SEVENTH_SUN_COPY']) for tag in wire[owner(t)])]
        check(len(sections) == len(suns) == (2 if phase == 'cloned' else 1), 'SECTION/SUN copy inventory differs')
        for current in sections:
            st = one(body(wire[current],'AcDbSection'),360)
            check(exact(body(wire[current],'AcDbSection')) == exact([[90,4],[91,17],[1,'Seventh shared geometry'],[10,[0.,0.,1.]],
                [40,5.25],[41,-2.75],[70,31],[62,6],[92,2],[11,[1.,2.,3.]],[11,[4.,5.,6.]],[93,0],[360,st]]), 'SECTION complete authored scalar packet differs')
            check(owner(wire[st]) == current, 'SECTION settings ownership differs')
            kind,bundles = settings_payload(wire[st]); check(kind == 4 and len(bundles) == 1, 'SECTION settings bundle grammar differs')
            bundle = bundles[0]
            check(bundle['sources'] == [current,st,dimension,geometry,content,base], 'SECTION self remap or shared native target differs')
            check(bundle['destination'] == owner(wire[current]) and bundle['file'] == 'seventh-inert.dwg', 'SECTION destination identity differs')
            check(bundle['type'] == 4 and bundle['options'] == 17, 'SECTION stored options differ')
            check(bundle['geometry'] == [{90:4,91:8,92:33,62:6,8:'0',6:'ByLayer',40:1.,1:'ByColor',370:-1,70:21,71:35,72:0,2:'',41:0.,42:1.,43:1.}], 'SECTION stored geometry appearance differs')
            if current != section: clones |= {current,st}
        for current in suns:
            host = owner(wire[current]); check(one(wire[host],361) == current, 'SUN reciprocal host differs')
            check(one(wire[host],2) == ('SEVENTH_SUN_OWNER' if current == sun else 'SEVENTH_SUN_COPY'), 'SUN clone host name differs')
            values = sun_packet([tuple(t) for t in wire[current]])
            check(values[40] == 1.625 and values[91] == 2455826 and values[92] == 43200000, 'SUN stored fields differ')
            ext = extension(wire[current]); links = dictionary(wire,ext)
            check(set(links) == {'LINKS','ALIAS'} and links['LINKS'] == links['ALIAS'], 'SUN extension alias identity differs')
            note = links['LINKS']; check(owner(wire[ext]) == current and owner(wire[note]) == ext, 'SUN owned extension graph differs')
            check(body(wire[note],'AcDbXrecord') == [[280,1],[330,host],[331,current],[340,base],[340,content],[340,association],[310,{'hex':'0700ff'}]], 'SUN remapped owned/shared external packet differs')
            if current == sun: originals |= {ext,note,host}
            else: clones |= {current,ext,note}
        if phase == 'erased':
            host = next(t for t in wire.values() if t[0] == [0,'VIEW'] and [2,'SEVENTH_SUN_COPY'] in t)
            check(not any(c == 361 for c,v in host), 'SUN teardown left a host slot')
        classes(path,wire)
    native_count = native_packets(wire,year,tables,associations)
    if audit:
        result = doc.audit(); check(not result.errors and not result.fixes, f'Mixed audit found {len(result.errors)} errors/{len(result.fixes)} repairs')
    return {'field':field,'base':base,'child':child,'association':association,'geometry':geometry,'content':content,'style':style,'originals':originals,'clones':clones,'native_packets':native_count},wire

def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path); args = parser.parse_args()
    check(ezdxf.__version__ == '1.4.4', 'Pinned independent parser differs')
    expected = {f'seventh-mixed-{phase}-AutoCad{year}-{binary}.dxf' for phase in PHASES for year in YEARS for binary in (False,True)}
    check({path.name for path in args.directory.glob('seventh-mixed-*.dxf')} == expected, 'Mandatory 20 mixed output inventory differs')
    tables = native_sources(); associations = dimassoc_sources(); controls = 0; native_count = 0
    for year in YEARS:
        for binary in (False,True):
            outputs = {}
            for phase in PHASES:
                path = args.directory/f'seventh-mixed-{phase}-AutoCad{year}-{binary}.dxf'
                outputs[phase] = validate(path,year,binary,phase,tables,associations)
                native_count += outputs[phase][0]['native_packets']
            original,ow = outputs['original']; cloned,cw = outputs['cloned']; erased,ew = outputs['erased']
            check(original['originals'] == cloned['originals'] == erased['originals'], 'Mutable clone/erase changed original cross-family identities')
            check(len(cloned['clones']) == (1 if year == 2004 else 6) and not cloned['clones'] & original['originals'], 'Mutable clones reused original ownership identities')
            check(not cloned['clones'] & set(ew), 'Erased owned copies remain physically registered')
            check(ow[original['field']] == ew[erased['field']], 'FIELD packet changed after other families were released/cloned/erased')
            path = args.directory/f'seventh-mixed-cloned-AutoCad{year}-{binary}.dxf'
            defects = ['ucs-base','field-native','association-geometry','content-style','missing-clone','native-byte','native-geometry','table-position']
            if year == 2018: defects += ['section-self','sun-owner-map','section-height','section-appearance']
            for defect in defects:
                corrupt = copy.deepcopy(cw)
                if defect == 'ucs-base': corrupt[cloned['child']].append([346,cloned['base']])
                elif defect == 'field-native':
                    i = corrupt[cloned['field']].index([331,cloned['content']]); corrupt[cloned['field']][i] = [331,cloned['style']]
                elif defect == 'association-geometry':
                    i = corrupt[cloned['association']].index([331,cloned['geometry']]); corrupt[cloned['association']][i] = [331,cloned['content']]
                elif defect == 'content-style':
                    packet = corrupt[cloned['content']]; i = packet.index([100,'AcDbTableContent']); j = next(j for j in range(i+1,len(packet)) if packet[j][0] == 340); packet[j] = [340,cloned['base']]
                elif defect == 'missing-clone': corrupt.pop(next(iter(cloned['clones'])))
                elif defect == 'native-byte':
                    packet = corrupt[cloned['content']]; i = next(i for i,t in enumerate(packet) if t[0] == 300); packet[i] = [300,'corrupted native string']
                elif defect == 'native-geometry':
                    packet = corrupt[cloned['geometry']]; i = next(i for i,t in enumerate(packet) if t[0] == 10); packet[i][1][0] += .125
                elif defect == 'table-position':
                    packet = next(t for t in corrupt.values() if t[0] == [0,'ACAD_TABLE']); i = packet.index([100,'AcDbBlockReference'])
                    j = next(j for j in range(i+1,len(packet)) if packet[j][0] == 10); packet[j][1][0] += .25
                elif defect in ('section-self','section-height','section-appearance'):
                    section = next(h for h in cloned['clones'] if [100,'AcDbSection'] in corrupt[h]); settings = one(body(corrupt[section],'AcDbSection'),360)
                    if defect == 'section-self':
                        packet = corrupt[settings]; i = packet.index([100,'AcDbSectionSettings']); j = next(j for j in range(i+1,len(packet)) if packet[j] == [330,section]); packet[j] = [330,cloned['geometry']]
                    elif defect == 'section-height':
                        packet = corrupt[section]; i = packet.index([40,5.25]); packet[i] = [40,5.5]
                    else:
                        packet = corrupt[settings]; i = packet.index([70,21]); packet[i] = [70,22]
                else:
                    sun = next(h for h in cloned['clones'] if corrupt[h][0] == [0,'SUN']); note = dictionary(corrupt,extension(corrupt[sun]))['LINKS']
                    packet = corrupt[note]; i = packet.index([330,owner(corrupt[sun])]); packet[i] = [330,cloned['base']]
                try: validate(path,year,binary,'cloned',tables,associations,override=corrupt,audit=False)
                except (ValueError,KeyError,StopIteration): controls += 1
                else: raise ValueError('Actual parsed corruption escaped validation: '+defect)
    check(controls == 40, 'Expected 40 cross-family parsed corruption controls')
    print(json.dumps({'outputs':20,'profiles':[2004,2018],'native_packet_comparisons':native_count,'negative_controls':controls,'audit_errors':0,'audit_repairs':0,'native_cad_execution':False,'evaluation':False},sort_keys=True))

if __name__ == '__main__': main()
