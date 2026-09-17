#!/usr/bin/env python3
"""Verify actual C# FIELD evaluator output with independently derived expectations.

Pure checker/model tests do not execute or qualify the C# implementation.
"""
from __future__ import annotations
import argparse
import copy
import datetime as dt
import json
import math
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata
from verify_field_results import body, changed_field, compare, exact, FIELDS
from verify_legacy_utilities import PROFILES, bits, from_bits, clock_ticks, expected_serial, formatted
from verify_mleader_inputs import check
from verify_stored_fields import source_records

ROOT = Path(__file__).resolve().parents[1]
MODES = ('variable', 'date', 'angle', 'expression', 'child', 'unknown')
CLOCK_TICKS = clock_ticks(2026, 9, 17, 13, 5, 9)
DATE_INPUTS = (0, clock_ticks(1900,3,1), clock_ticks(2000,2,29,12,30,59), CLOCK_TICKS,
               clock_ticks(2026,12,31,23,59,59), 3155378975999999999)
DATE_MASKS = ('yyyy-MM-dd HH:mm:ss', 'd dd ddd dddd', 'M MM MMM MMMM', 'y yy yyy yyyy',
              'h hh H HH m mm s ss t tt', "'clock' HH:mm:ss", "''\\d''", 'dd/MM/yyyy')
ANGLE_INPUTS = (-2*math.pi, -math.pi/4, -0.0, .125, math.pi/4, math.pi/2, math.pi, 10.0)


def clock_from_ticks(ticks):
    check(type(ticks) is int and 0 <= ticks <= 3155378975999999999, 'Invalid Gregorian ticks')
    days, within = divmod(ticks, 864000000000)
    return dt.datetime(1,1,1) + dt.timedelta(days=days, seconds=within//10000000)


def expected_date(ticks, expression):
    """Eight fixed independent blueprints, not a port of the C# mask compiler."""
    v = clock_from_ticks(ticks)
    y,m,d,h,minute,s = v.year,v.month,v.day,v.hour,v.minute,v.second
    if expression == DATE_MASKS[0]: return f'{y:04d}-{m:02d}-{d:02d} {h:02d}:{minute:02d}:{s:02d}'
    if expression == DATE_MASKS[1]:
        name = ('Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday')[v.weekday()]
        return f'{d} {d:02d} {name[:3]} {name}'
    if expression == DATE_MASKS[2]:
        name = ('January','February','March','April','May','June','July','August','September','October','November','December')[m-1]
        return f'{m} {m:02d} {name[:3]} {name}'
    if expression == DATE_MASKS[3]: return f'{y%100} {y%100:02d} {y:04d} {y:04d}'
    if expression == DATE_MASKS[4]:
        twelve=(h+11)%12+1; am='AM' if h<12 else 'PM'
        return f'{twelve} {twelve:02d} {h} {h:02d} {minute} {minute:02d} {s} {s:02d} {am[0]} {am}'
    if expression == DATE_MASKS[5]: return f'clock {h:02d}:{minute:02d}:{s:02d}'
    if expression == DATE_MASKS[6]: return "'d'"
    if expression == DATE_MASKS[7]: return f'{d:02d}/{m:02d}/{y:04d}'
    raise ValueError('Undeclared date blueprint')


def expected_angle(raw_bits, mode, precision):
    check(mode in range(5) and precision in range(9), 'Undeclared angular format')
    value=from_bits(raw_bits);check(math.isfinite(value), 'Nonfinite source angle')
    number=value if mode==3 else (math.fmod(value,2*math.pi) if mode==4 else value)*((200 if mode==2 else 180)/math.pi)
    check(math.isfinite(number), 'Unrepresentable converted angle')
    settings=dict(bits=bits(number),places=precision,decimal='.',leading=False,trailing=False)
    result=formatted('survey' if mode==4 else 'dms' if mode==1 else 'dec', settings)
    return result.replace('%%d','°') if mode in (1,4) else result


def format_inventory():
    result={('date',str(t),mask) for t in DATE_INPUTS for mask in DATE_MASKS}
    result.update(('angle',bits(v),f'%au{mode}%pr{p}') for v in ANGLE_INPUTS for mode in range(5) for p in range(9))
    return result


def check_format_row(row):
    check(isinstance(row,dict),'Format row is not an object')
    kind=row.get('kind')
    if kind=='date':
        check(set(row)=={'kind','ticks','expression','result'} and isinstance(row['ticks'],str),'Unexpected date row fields')
        identity=(kind,row['ticks'],row['expression']);check(identity in format_inventory(),'Undeclared date input')
        wanted=expected_date(int(row['ticks']),row['expression'])
    else:
        check(kind=='angle' and set(row)=={'kind','bits','expression','result'},'Unexpected angle row fields')
        identity=(kind,row['bits'],row['expression']);check(identity in format_inventory(),'Undeclared angle input')
        wanted=expected_angle(row['bits'],int(row['expression'][3]),int(row['expression'][7]))
    check(type(row['result']) is str and row['result']==wanted,'Date/angular output differs')
    return identity


def check_format_matrix(rows):
    check(type(rows) is list and len(rows)==408,'Expected 48 date and 360 angular results')
    identities=[check_format_row(r) for r in rows]
    check(len(set(identities))==len(identities) and set(identities)==format_inventory(),'Repeated/missing format inputs')
    count=0
    for row in rows:
        try: check_format_row(dict(row,result=row['result']+'_CORRUPT'))
        except ValueError: count+=1
        else: raise AssertionError('Format corruption escaped validation')
    return count


def source_commands(mode):
    child_id='AcExpr' if mode=='expression' else 'PrivateEvaluator' if mode=='unknown' else 'AcVar'
    code={'date':'\\AcVar Date \\f "yyyy-MM-dd HH:mm:ss"','angle':'\\AcVar Angle \\f "%au0%pr2"',
          'expression':'\\AcExpr (2^3 + 6/4) \\f "%lu2%pr3"','unknown':'private code stays inert'}.get(mode,'\\AcVar Amount \\f "%lu2%pr2"')
    commands={'14F':(child_id,code)}
    if mode=='child':commands['14E']=('AcExpr','\\AcExpr (%<\\_FldIdx 0>% * 2) \\f "%lu2%pr2"')
    return commands


def synthetic_source(native,mode):
    source={key:[list(t) for t in native[key]] for key in FIELDS}
    for key,(evaluator,code) in source_commands(mode).items():
        tags=source[key];at=tags.index([100,'AcDbField'])
        check(tags[at+1][0]==1 and tags[at+2][0]==2,'Pinned code framing changed')
        tags[at+1]=[1,evaluator];tags[at+2]=[2,code]
        while tags[at+3][0]==3:del tags[at+3]
    return source


def retained_failure(tags,status,error,message):
    result=copy.deepcopy(tags);data=next(i for i,t in enumerate(tags) if t[0]==93)
    for code in (94,95,96,300):
        matches=[i for i in range(data) if tags[i][0]==code];check(len(matches)==1,'Ambiguous FIELD metadata')
        index=matches[0]
        value=(tags[index][1]&~4)|8 if code==94 else status if code==95 else error if code==96 else message
        result[index]=[code,value]
    return result


def expected_records(before,profile,mode,native):
    check(mode in MODES,'Unknown operation')
    check({key for key,t in before.items() if t[0]==[0,'FIELD']}==set(FIELDS),'Unexpected FIELD inventory')
    source=synthetic_source(native,mode)
    for key in FIELDS:check(exact(body(before[key]))==exact(body(source[key])),'Before FIELD differs from explicit source construction: '+key)
    wanted=dict(before)
    if mode=='unknown':
        wanted['14F']=retained_failure(before['14F'],4,1,'No bounded standard evaluator for this FIELD ID.')
        wanted['14E']=retained_failure(before['14E'],64,7,'A failed child requires an explicit parent failure or host-selected fallback.')
    else:
        value=expected_serial(CLOCK_TICKS) if mode=='date' else math.pi/2 if mode=='angle' else 9.5 if mode=='expression' else 21.0
        display='2026-09-17 13:05:09' if mode=='date' else '90.00' if mode=='angle' else '9.500' if mode=='expression' else '21.00'
        wanted['14F']=changed_field(before['14F'],profile,value,display,display)
        pv,pd=(42.0,'42.00') if mode=='child' else (display,display)
        wanted['14E']=changed_field(before['14E'],profile,pv,pd,pd)
    return wanted


def check_pair(before,after,profile,mode,native):
    before,after=normalize_save_metadata(before),normalize_save_metadata(after)
    wanted=expected_records(before,profile,mode,native);compare(wanted,after);controls=0
    def reject(candidate):
        nonlocal controls
        try:compare(wanted,candidate)
        except (ValueError,KeyError):controls+=1
        else:raise AssertionError('FIELD corruption escaped whole-record validation')
    for key in ('14E','14F'):
        for index,(code,value) in enumerate(after[key]):
            candidate=dict(after);candidate[key]=list(after[key])
            candidate[key][index]=[code,value+'_CORRUPT' if isinstance(value,str) else value+1];reject(candidate)
    for key in after:
        candidate=dict(after)
        if key in ('14E','14F'):del candidate[key]
        else:candidate[key]=after[key]+[[999,'CORRUPT']]
        reject(candidate)
    return controls


def drawing_inventory():
    return {f'standard-fields-{phase}-{version}-{binary}-{mode}.dxf' for version in PROFILES for binary in (False,True) for mode in MODES for phase in ('before','after')}


def check_inventory(directory):
    check({p.name for p in directory.glob('standard-fields-*.dxf')}==drawing_inventory(),'Exact standard FIELD drawing inventory required')
    check((directory/'standard-field-formats.json').is_file(),'Missing date/angular matrix')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory;check_inventory(directory)
    matrix_controls=check_format_matrix(json.loads((directory/'standard-field-formats.json').read_text(encoding='utf-8')))
    manifest=json.loads((ROOT/'tests/fixtures/field-oracle/manifest.json').read_text(encoding='utf-8'))
    native={item['profile']:source_records(item) for item in manifest['files']}
    check(set(native)=={'AC1015','AC1032'},'Pinned producer inventory changed');pairs=controls=0
    for version,profile in PROFILES.items():
        source=native['AC1015' if profile<'AC1021' else 'AC1032']
        for binary in (False,True):
            for mode in MODES:
                paths=[directory/f'standard-fields-{phase}-{version}-{binary}-{mode}.dxf' for phase in ('before','after')]
                for path in paths:
                    check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
                    doc=ezdxf.readfile(path);check(doc.dxfversion==profile,'Profile changed');audit=doc.audit()
                    check(not audit.errors and not audit.fixes,'FIELD output needs independent repairs')
                controls+=check_pair(records(paths[0]),records(paths[1]),profile,mode,source);pairs+=1
    check(pairs==72,'Expected all 72 FIELD pairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {pairs} pairs; 408 format results; {controls+matrix_controls} corruptions rejected; no native evaluator qualification')


if __name__=='__main__':main()
