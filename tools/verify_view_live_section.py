#!/usr/bin/env python3
"""Verify named VIEW live-section references, physical identity and null presence."""
from pathlib import Path
import argparse
import copy
import hashlib
import json
import ezdxf
from verify_datatable import records, first, check

ROOT = Path(__file__).resolve().parents[1]
YEARS = {2007:'AC1021', 2010:'AC1024', 2013:'AC1027', 2018:'AC1032'}

def public(tags, marker):
    result = []; active = False; depth = 0; xdata = False
    for code,value in tags:
        if code == 1001: xdata = True
        if code == 102:
            if value.startswith('{'): depth += 1
            elif value == '}': depth = max(0, depth-1)
        elif code == 100 and depth == 0: active = value == marker
        elif active and depth == 0 and not xdata: result.append((code,value))
    return result

def validate(wire, kind):
    views = {first(t,2):t for t in wire.values() if t[0] == (0,'VIEW')}
    names = {'producer':{'LiveSectionProducer','NullSectionProducer','AbsentSectionProducer'},
             'authored':{'Live','Null','Absent'}, 'foreign':{'Foreign'}}[kind]
    check(set(views) == names, 'Named VIEW inventory changed')
    sections = {h:t for h,t in wire.items() if t[0] == (0,'SECTIONOBJECT')}
    check(len(sections) == 1, 'Physical SECTIONOBJECT inventory changed')
    section_handle, section = next(iter(sections.items()))
    payload = public(section, 'AcDbSection')
    check(first(payload,92) == 2 and len([v for c,v in payload if c == 11]) == 2, 'SECTION vertex packet changed')
    if kind == 'producer':
        check(first(payload,1) == 'Carrier section' and first(payload,40) == 10 and first(payload,41) == -5, 'Producer SECTION carrier values changed')
    else:
        check(first(payload,40) == 5.25 and first(payload,41) == -15.5 and first(payload,91) == 17, 'Authored SECTION values changed')
    main = {'producer':'LiveSectionProducer','authored':'Live','foreign':'Foreign'}[kind]
    for name,tags in views.items():
        values = [v for c,v in public(tags,'AcDbViewTableRecord') if c == 334]
        expected = [section_handle] if name == main else ['0'] if name in ('Null','NullSectionProducer') else []
        check(values == expected, 'VIEW334 exact identity or field presence changed: ' + name)
        for value in values:
            if int(value,16): check(value in sections, 'VIEW334 target is not a physical SECTIONOBJECT')
    return views[main], section

def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('artifacts',type=Path); args = parser.parse_args()
    fixture = ROOT/'tests/fixtures/view-live-section'; manifest = json.loads((fixture/'manifest.json').read_text())
    check(manifest['producer'] == 'ezdxf 1.4.4' and len(manifest['fixtures']) == 8, 'Producer manifest changed')
    for entry in manifest['fixtures']:
        data = (fixture/entry['path']).read_bytes()
        check(hashlib.sha256(data).hexdigest() == entry['sha256'], 'Producer bytes changed')
        wire = records(data); validate(wire,'producer')
        check(entry['section_handle'] in wire and entry['view_handle'] in wire, 'Producer physical identities missing')
    outputs = 0; controls = 0
    for year, profile in YEARS.items():
        for binary in (False,True):
            for kind in ('producer','authored','foreign'):
                path = args.artifacts/f'view-live-section-{kind}-AutoCad{year}-{binary}.dxf'
                data = path.read_bytes(); check(data.startswith(b'AutoCAD Binary DXF') == binary,'Wrong output transport')
                wire = records(data); view,section = validate(wire,kind)
                doc = ezdxf.readfile(path); check(doc.dxfversion == profile,'Wrong output profile')
                audit = doc.audit(); check(not audit.errors and not audit.fixes,'Output requires independent audit repairs')
                view_handle = first(view,5); section_handle = first(section,5)
                check(doc.views.get(first(view,2)).dxf.live_selection_handle == section_handle,'Independent VIEW projection lost reference')
                layer = next(h for h,t in wire.items() if t[0] == (0,'LAYER'))
                for defect in ('missing','wrong-type','null','duplicate','section-value'):
                    corrupt = copy.deepcopy(wire)
                    if defect == 'section-value':
                        tags = corrupt[section_handle]; at = next(i for i,t in enumerate(tags) if t[0] == 40); tags[at] = (40,999)
                    else:
                        tags = corrupt[view_handle]; at = next(i for i,t in enumerate(tags) if t[0] == 334)
                        if defect == 'missing': del tags[at]
                        elif defect == 'duplicate': tags.insert(at,tags[at])
                        else: tags[at] = (334,layer if defect == 'wrong-type' else '0')
                    try: validate(corrupt,kind)
                    except ValueError: controls += 1
                    else: raise ValueError('Actual output corruption escaped validator: ' + defect)
                outputs += 1
    check(outputs == 24 and controls == 120,'Required output or corruption count changed')
    print(json.dumps({'outputs':outputs,'independent_view_producer_inputs':8,'actual_output_corruptions_rejected':controls,
        'audit_errors':0,'audit_repairs':0,'section_carriers':'explicitly authored','native_cad_execution':False,'section_evaluation':False},sort_keys=True))

if __name__ == '__main__': main()
