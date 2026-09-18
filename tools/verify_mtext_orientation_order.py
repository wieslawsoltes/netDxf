#!/usr/bin/env python3
"""Check MTEXT orientation record order without borrowing a reader's precedence.

Autodesk's documented last-orientation rule is the explicit input oracle here.
The existing degree/OCS convention is retained; this is not a native AutoCAD run.
Canonical output has only one direction vector, so independent model loading
cannot silently choose an alternative angle-vs-vector policy.
"""
from __future__ import annotations
import argparse
import io
import math
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.math import OCS, Vec3

PROFILES = {'AutoCad2000':'AC1015','AutoCad2004':'AC1018','AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024','AutoCad2013':'AC1027','AutoCad2018':'AC1032'}
SEQUENCES = ((('a',37),),(('v',68),),(('a',37),('v',68)),(('v',68),('a',37)),
             (('a',37),('v',68),('a',-15)),(('v',68),('a',37),('v',123)),
             (('a',-725),('v',-30)),(('v',68),('a',360),('v',180)))
NORMALS = ((0.,0.,1.),(0.,0.,-1.),(0.,1.,0.),(2./7,3./7,6./7))
ORIENTATION = (11,21,31,50)


def require(ok, message):
    if not ok: raise ValueError(message)


def close(a,b):
    return math.isfinite(a) and math.isfinite(b) and math.isclose(a,b,rel_tol=1e-13,abs_tol=1e-12)


def direction(normal,degrees):
    angle=math.radians(degrees)
    return tuple(OCS(normal).to_wcs(Vec3(math.cos(angle),math.sin(angle),0)))


def expected_orientation(normal,sequence):
    result=[]
    for operation,degrees in sequence:
        if operation=='a': result.append((50,float(degrees)))
        else: result.extend(zip((11,21,31),direction(normal,degrees)))
    return result


def packet(data):
    tags = (binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else
            ascii_tags_loader(io.StringIO(data.decode('utf-8-sig','strict'), newline=None)))
    found=[]; current=[]
    for tag in tags:
        if tag.code==0:
            if current and current[0]==(0,'MTEXT'): found.append(current)
            current=[]
        current.append((tag.code,tag.value))
    if current and current[0]==(0,'MTEXT'): found.append(current)
    require(len(found)==1,'Wrong MTEXT inventory')
    return found[0]


def parts(tags):
    at=next((i for i,(code,_) in enumerate(tags) if code in (75,101,1001)),len(tags))
    return tags[:at],tags[at:]


def one(tags,code):
    values=[value for key,value in tags if key==code]
    require(len(values)==1,f'Missing/duplicate field {code}')
    return values[0]


def vector(tags,code):
    return tuple(float(one(tags,c)) for c in (code,code+10,code+20))


def float_code(code):
    return 10<=code<=59 or 110<=code<=149 or 210<=code<=239 or 460<=code<=469 or 1010<=code<=1059


def same_tags(actual,expected):
    require(len(actual)==len(expected),'Packet length changed')
    for (code,value),(ec,ev) in zip(actual,expected):
        require(code==ec,'Ordered packet code changed')
        if code in (5,330):
            require(isinstance(value,str) and value and all(c in '0123456789ABCDEFabcdef' for c in value),'Invalid identity framing')
        elif float_code(code): require(close(float(value),float(ev)),f'Numeric field {code} changed')
        else: require(str(value)==str(ev),f'Field {code} changed')


def common(tags):
    primary,secondary=parts(tags)
    return [(c,v) for c,v in primary if c not in ORIENTATION]+secondary


def check_output(tags,original,wanted):
    primary,_=parts(tags)
    require(not any(c==50 for c,_ in primary),'Output retained a competing rotation')
    actual=vector(primary,11)
    require(all(close(a,e) for a,e in zip(actual,wanted)),'Wrong resulting WCS direction')
    require(abs(sum(a*a for a in actual)-1)<2e-13,'Output direction is not unit')
    same_tags(common(tags),common(original))


def rejected(check,value):
    try: check(value)
    except (ValueError,TypeError,OverflowError): return 1
    raise AssertionError('An actual output mutation escaped the positive validator')


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory; expected_files=set(); cases=controls=audits=0
    for version,profile in PROFILES.items():
        for binary in (False,True):
            for context in ('plain','nested','direct','embedded'):
                if context=='direct' and version in ('AutoCad2000','AutoCad2004'): continue
                if context=='embedded' and version!='AutoCad2018': continue
                for n,normal in enumerate(NORMALS):
                    for sample,sequence in enumerate(SEQUENCES):
                        stem=f'mtext-orientation-{version}-{binary}-{context}-{n}-{sample}'
                        names=[stem+'-input.dxf',stem+'-output.dxf'];expected_files.update(names)
                        paths=[directory/name for name in names];data=[p.read_bytes() for p in paths]
                        require([b.startswith(b'AutoCAD Binary DXF') for b in data]==[binary,not binary],'Transport differs')
                        original,output=[packet(b) for b in data]
                        primary,tail=parts(original)
                        same_tags([(c,v) for c,v in primary if c in ORIENTATION],expected_orientation(normal,sequence))
                        require(all(close(a,b) for a,b in zip(vector(primary,210),normal)),'Input normal differs')
                        require(vector(primary,10)==(10,20,30),'Input position differs')
                        require(one(primary,1)=='orientation\\Pnext','Input text differs')
                        if context=='direct':
                            require([float(v) for c,v in tail if c==50]==[3,20.25,28.75,0],'Column height packet changed')
                        wanted=direction(normal,sequence[-1][1]);check=lambda tags:check_output(tags,original,wanted)
                        check(output)
                        # Every non-identity tag is challenged through the same
                        # complete ordered-packet/geometry validator.
                        for at,(code,value) in enumerate(output):
                            if code in (5,330): continue
                            changed=list(output)
                            if float_code(code): changed[at]=(code,float(value)+.25)
                            elif isinstance(value,(int,float)): changed[at]=(code,value+1)
                            else: changed[at]=(code,str(value)+'!')
                            controls+=rejected(check,changed)
                        for at in range(len(output)):
                            controls+=rejected(check,output[:at]+output[at+1:])
                        for path in paths:
                            document=ezdxf.readfile(path); require(document.dxfversion==profile,'DXF profile differs')
                            audit=document.audit();require(not audit.errors and not audit.fixes,'Graph needs repairs');audits+=1
                        canonical=ezdxf.readfile(paths[1])
                        loaded=list(canonical.blocks.get('ORIENTATION_BLOCK') if context=='nested' else canonical.modelspace())
                        entity=next(e for e in loaded if e.dxftype()=='MTEXT')
                        require(all(close(a,b) for a,b in zip(entity.dxf.text_direction,wanted)),'Independent model direction differs')
                        cases+=1
    actual={p.name for p in directory.glob('mtext-orientation-*.dxf')}
    check_inventory=lambda values:require(values==expected_files,'Output inventory differs')
    check_inventory(actual)
    inv=rejected(check_inventory,actual-{next(iter(actual))})+rejected(check_inventory,actual|{'extra'})
    print(f'PASS ezdxf {ezdxf.__version__}: {cases} ordered-orientation pairs / {audits} drawings, '
          f'{controls} actual-packet corruptions and {inv} inventory corruptions rejected; zero graph errors/repairs')


if __name__=='__main__': main()
