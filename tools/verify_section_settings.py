#!/usr/bin/env python3
"""Verify SECTIONSETTINGS packets, source provenance, graphs, and malformed-output controls."""
from pathlib import Path
import argparse, copy, gzip, hashlib, json, tempfile
import ezdxf
from verify_mleader_inputs import records, exact, decode_once, check
from verify_layer_index import owner, dictionary, audit, lines
ROOT = Path(__file__).resolve().parents[1]
VERSIONS = {2007:'AC1021', 2010:'AC1024', 2013:'AC1027', 2018:'AC1032'}

def read(path, year, binary):
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong settings transport')
    doc = ezdxf.readfile(path); check(doc.dxfversion == VERSIONS[year], 'Wrong settings profile')
    return doc, records(path)

def payload(tags):
    start = tags.index([100, 'AcDbSectionSettings']) + 1
    stop = next((i for i in range(start, len(tags)) if tags[i][0] == 1001), len(tags))
    data = tags[start:stop]; at = 0
    def take(code, value=None):
        nonlocal at
        check(at < len(data) and data[at][0] == code, f'Missing or misplaced section group {code}')
        actual = data[at][1]; at += 1
        check(value is None or actual == value, 'Wrong section boundary marker')
        return decode_once(actual) if isinstance(actual, str) else actual
    section_type = take(90); count = take(91); check(0 <= count <= 1024, 'Invalid settings type count'); types = []
    for _ in range(count):
        take(1, 'SectionTypeSettings'); current = {'type':take(90), 'options':take(91)}
        sources = take(92); check(0 <= sources <= 1048576, 'Invalid settings source count')
        current['sources'] = [take(330) for _ in range(sources)]
        current['destination'] = take(331); current['file'] = take(1)
        geometry = take(93); check(0 <= geometry <= 65536, 'Invalid geometry count')
        initial = at < len(data) and data[at] == [2, 'SectionGeometrySettings']
        if initial: take(2, 'SectionGeometrySettings')
        check(initial or geometry == 0, 'Missing initial geometry marker')
        values = []; repeat = None
        for ordinal in range(geometry):
            if ordinal:
                marker = at < len(data) and data[at] == [2, 'SectionGeometrySettings']
                check(repeat is None or repeat == marker, 'Mixed geometry marker layouts'); repeat = marker
                if marker: take(2, 'SectionGeometrySettings')
            value = {90:take(90), 91:take(91), 92:take(92)}
            check(at < len(data) and data[at][0] in (62, 63), 'Missing indexed color')
            color = data[at][0]; value[color] = take(color); check(0 <= value[color] <= 256, 'Invalid indexed color')
            for code in (8, 6, 40, 1, 370, 70, 71, 72, 2, 41, 42, 43): value[code] = take(code)
            take(3, 'SectionGeometrySettingsEnd'); values.append(value)
        current['repeat'] = not initial if geometry == 0 else True if repeat is None else repeat
        current['geometry'] = values; take(3, 'SectionTypeSettingsEnd'); types.append(current)
    check(at == len(data), 'Unconsumed settings payload'); return section_type, types

def appearance(ordinal, color):
    return {90:-17+ordinal, 91:1<<ordinal, 92:-2147483643, color:256 if ordinal == 2 else ordinal+1,
        8:'*_BackgroundLines' if ordinal == 0 else '東京', 6:r'Literal\U+0041' if ordinal == 1 else 'ByLayer',
        40:1.125+ordinal, 1:r'plot\U+0041', 370:40, 70:ordinal*30, 71:100, 72:ordinal,
        2:'' if ordinal == 0 else 'SectionGeometrySettings' if ordinal == 1 else 'ANSI31',
        41:-7.125+ordinal, 42:21.5+ordinal, 43:-0.125-ordinal}

def graph(path, year, binary, copied=False, override=None):
    doc, wire = read(path, year, binary)
    if override is not None: wire = override
    parent = dictionary(wire, doc.rootdict.dxf.handle)['COPY' if copied else 'QA_SECTION_SETTINGS']; entries = dictionary(wire, parent)
    check(set(entries) == {'MAIN', 'MAIN_ALIAS', 'EMPTY'} and entries['MAIN'] == entries['MAIN_ALIAS'], 'Settings dictionary aliases changed')
    main, empty = entries['MAIN'], entries['EMPTY']; a, b = lines(doc)
    check(owner(wire[main]) == parent and owner(wire[empty]) == parent, 'Settings common ownership changed')
    section_type, types = payload(wire[main]); check(section_type == 4 and len(types) == 4, 'Outer settings values changed')
    check(payload(wire[empty]) == (-1, []), 'Empty settings changed')
    check([t['type'] for t in types] == [4,2,0,1] and [t['options'] for t in types] == [17,33,0,1], 'Stored type/option integers changed')
    check(types[0]['sources'] == [a,b,a,'0',main,empty] and types[1]['sources'] == [b], 'Ordered source identities changed')
    check(types[0]['destination'] == doc.blocks['SECTION_OUTPUT'].block_record_handle, 'Destination block identity changed')
    check(types[0]['file'] == r'inert\U+0041.dwg' and all(t['destination'] == '0' and t['file'] == '' for t in types[1:]), 'Inert destination data changed')
    check([t['repeat'] for t in types] == [False,True,False,True], 'Physical geometry marker layout changed')
    for bundle, color in ((0,63),(1,62)):
        check(len(types[bundle]['geometry']) == 3, 'Wrong appearance count')
        for ordinal, actual in enumerate(types[bundle]['geometry']): check(exact(actual) == exact(appearance(ordinal,color)), 'Stored appearance values changed')
    check(all(not t['sources'] and not t['geometry'] for t in types[2:]), 'Empty type bundles changed')
    tags = wire[main]; xdata = tags[tags.index([1001,'SECTION_SETTINGS_APP']):]
    check(xdata == [[1001,'SECTION_SETTINGS_APP'],[1004,{'hex':'0700ff'}],[1005,empty]], 'Binary or mapped handle XData changed')
    at = tags.index([102,'{ACAD_REACTORS']); check(tags[at+1:at+4] == [[330,a],[330,parent],[102,'}']], 'Settings reactors changed')
    at = tags.index([102,'{ACAD_XDICTIONARY']); check(tags[at+1][0] == 360 and tags[at+2] == [102,'}'], 'Extension envelope changed')
    extension = tags[at+1][1]; check(owner(wire[extension]) == main, 'Extension owner changed'); note = dictionary(wire,extension)['NOTE']; check(owner(wire[note]) == extension, 'XRecord owner changed')
    check(wire[note][-2:] == [[1,'section settings metadata'],[330,empty]], 'Extension XRecord payload changed')
    cls = doc.classes.get('SECTIONSETTINGS'); check(cls.dxf.cpp_class_name == 'AcDbSectionSettings' and cls.dxf.instance_count == 2 and cls.dxf.is_an_entity == 0, 'Settings CLASS changed')
    audit(doc); return {'main':main,'empty':empty,'parent':parent,'extension':extension,'note':note,'a':a,'b':b}, wire

def erased(path, year, binary, before):
    doc, wire = read(path, year, binary)
    check(not any(packet[0][1] in ('SECTIONSETTINGS','SECTION_SETTINGS') for packet in wire.values()), 'Erased settings returned')
    for key in ('main','empty','parent','extension','note'): check(before[key] not in wire, 'Erased identity survived')
    check('COPY' not in dictionary(wire,doc.rootdict.dxf.handle), 'Erased root entry survived')
    check(lines(doc) == (before['a'],before['b']) and 'SECTION_OUTPUT' in doc.blocks, 'Erasure removed external resources')
    check(doc.classes.get('SECTIONSETTINGS').dxf.instance_count == 0, 'Stale erased CLASS count'); audit(doc)

def source_fixtures():
    folder = ROOT/'tests/fixtures/section-settings'; manifest = json.loads((folder/'manifest.json').read_text()); check(len(manifest['fixtures']) == 8, 'Expected eight producer fixtures')
    result = {}
    with tempfile.TemporaryDirectory() as temporary:
        for fixture in manifest['fixtures']:
            packed = (folder/fixture['file']).read_bytes(); check(hashlib.sha256(packed).hexdigest() == fixture['gzip_sha256'], 'Producer archive changed')
            original = gzip.decompress(packed); check(hashlib.sha256(original).hexdigest() == fixture['sha256'], 'Producer original changed')
            path = Path(temporary)/'original.dxf'; path.write_bytes(original); old = records(path)
            extracted = folder/fixture['extracted_file']; check(hashlib.sha256(extracted.read_bytes()).hexdigest() == fixture['extracted_sha256'], 'Extracted producer changed'); wire = records(extracted)
            check(len(fixture['application_packets']) == 1, 'Producer application inventory changed'); handle = fixture['application_packets'][0]; mapping = fixture['handle_map']
            expected = [[c,mapping.get(v,v) if c in (5,330,331) else v] for c,v in old[handle]]
            check(exact(wire[mapping[handle]]) == exact(expected), 'Producer packet differs beyond explicit handle mapping')
            audit(ezdxf.readfile(extracted)); year = int(fixture['file'].split('-R')[1].split('-')[0]); result[(year,'-binary.' in fixture['file'])] = expected
    return result

def native(path, binary):
    folder = ROOT/'tests/fixtures/section'; manifest = json.loads((folder/'manifest.json').read_text()); packed = (folder/'LiveSection1.dxf.gz').read_bytes()
    check(hashlib.sha256(packed).hexdigest() == manifest['gzip_sha256'], 'Native archive changed'); original = gzip.decompress(packed)
    check(hashlib.sha256(original).hexdigest() == manifest['source_sha256'], 'Native source changed')
    with tempfile.TemporaryDirectory() as temporary:
        source = Path(temporary)/'native.dxf'; source.write_bytes(original); old = records(source)
    doc, wire = read(path,2018,binary); check(exact(wire['22A']) == exact(old['22A']), 'Native complete settings packet changed')
    check(owner(wire['22A']) == '228' and [360,'22A'] in wire['228'], 'Native reciprocal section ownership changed')
    kind, types = payload(wire['22A']); check(kind == 4 and len(types) == 1 and types[0]['options'] == 17 and types[0]['repeat'], 'Native settings envelope changed')
    check([g[91] for g in types[0]['geometry']] == [1,2,4,8], 'Native explicit geometry91 values changed'); audit(doc)

def opaque(path, binary, variant, baseline):
    doc, wire = read(path,2018,binary); parent = dictionary(wire,doc.rootdict.dxf.handle)['QA_SECTION_SETTINGS']; main = dictionary(wire,parent)['MAIN']; expected = copy.deepcopy(baseline[main])
    first = next(i for i,t in enumerate(expected) if t[0] == 100); xdata = expected.index([1001,'SECTION_SETTINGS_APP'])
    if variant == 0: expected.insert(first,[1,'private header'])
    elif variant == 1: expected[first:first] = [[102,'{PRIVATE'],[70,7],[102,'}']]
    elif variant == 2: expected[first] = [100,'PrivateSectionSettings']
    elif variant == 3: expected[xdata:xdata] = [[100,'PrivateSectionExtension'],[91,99]]
    elif variant == 4: at = next(i for i,t in enumerate(expected) if t[0] == 63); expected.insert(at+1,[420,0x123456])
    elif variant == 5: at = next(i for i,t in enumerate(expected) if t[0] == 63); expected.insert(at,[62,7])
    else: expected.insert(xdata,[300,'private payload'])
    check(exact(wire[main]) == exact(expected), 'Whole private settings packet changed'); audit(doc)

def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path); args = parser.parse_args(); folder = args.directory
    expected = {f'section-settings-{kind}-AutoCad{year}-{binary}.dxf' for kind in ('authored','copy','erased') for year in VERSIONS for binary in (False,True)}
    expected |= {f'section-settings-producer-AutoCad{year}-{source}-{binary}.dxf' for year in VERSIONS for source in (False,True) for binary in (False,True)}
    expected |= {f'section-settings-opaque-{variant}-{binary}.dxf' for variant in range(7) for binary in (False,True)}
    expected |= {f'section-settings-native-AutoCad2018-{binary}.dxf' for binary in (False,True)}
    check({p.name for p in folder.glob('section-settings-*.dxf')} == expected, 'Expected all 56 settings outputs')
    inputs = source_fixtures(); baselines = {}
    for year in VERSIONS:
        for binary in (False,True):
            before, wire = graph(folder/f'section-settings-authored-AutoCad{year}-{binary}.dxf',year,binary)
            copied, _ = graph(folder/f'section-settings-copy-AutoCad{year}-{binary}.dxf',year,binary,True)
            check(all(before[k] != copied[k] for k in ('main','empty','parent','extension','note')), 'Cloning reused source graph identities')
            erased(folder/f'section-settings-erased-AutoCad{year}-{binary}.dxf',year,binary,copied)
            if year == 2018: baselines[binary] = wire
            for source in (False,True):
                doc, actual = read(folder/f'section-settings-producer-AutoCad{year}-{source}-{binary}.dxf',year,binary); packet = inputs[(year,source)]
                handle = next(v for c,v in packet if c == 5); check(exact(actual[handle]) == exact(packet), 'Complete producer packet changed'); payload(actual[handle]); audit(doc)
    for binary in (False,True):
        native(folder/f'section-settings-native-AutoCad2018-{binary}.dxf',binary)
        for variant in range(7): opaque(folder/f'section-settings-opaque-{variant}-{binary}.dxf',binary,variant,baselines[binary])
    path = folder/'section-settings-authored-AutoCad2018-False.dxf'; ids, wire = graph(path,2018,False); rejected = 0
    for mutation in ('option','count','marker','destination'):
        altered = copy.deepcopy(wire); tags = altered[ids['main']]
        if mutation == 'option': at = next(i for i,t in enumerate(tags) if t == [91,17]); tags[at] = [91,1]
        elif mutation == 'count': at = next(i for i,t in enumerate(tags) if t == [93,3]); tags[at] = [93,2]
        elif mutation == 'marker': tags.pop(tags.index([2,'SectionGeometrySettings']))
        else: at = next(i for i,t in enumerate(tags) if t[0] == 331); tags[at] = [331,ids['a']]
        try: graph(path,2018,False,override=altered)
        except (ValueError,KeyError): rejected += 1
    check(rejected == 4, 'Independent corruption controls were not detected')
    print(f'PASS ezdxf {ezdxf.__version__}: 56 settings outputs, 8 pinned producer packets/extractions, exact native packet, zero audits, graph/clone/erase/opaque checks and 4 rejected corruptions')

if __name__ == '__main__': main()
