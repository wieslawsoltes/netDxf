#!/usr/bin/env python3
"""Check raw ELLIPSE plane edits against retained packets and independent curves."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, key, require, reject, audit_signature
from verify_raw_ellipse_geometry import geometry, selected, verify_curve

VERSIONS = {k: v for k, v in PROFILES.items() if k != 'AutoCad12'}
CODES = (10,20,30,11,21,31,40,41,42,210,220,230)
DEFAULTS = {30:0.0,31:0.0,210:0.0,220:0.0,230:1.0}


def target(variant):
    normals = ((2.,-3.,6.), (0.,2.,0.), (-2.,3.,-6.), (0.,0.,1.))
    major = (0.,0.,6.) if variant == 1 else (6.,-8.,0.) if variant == 3 else (3.,2.,0.)
    return (8.,-16.,32.), major, .25, .25, 5.75, normals[variant]


def values(definition):
    center, major, ratio, first, last, normal = definition
    return dict(zip(CODES, center + major + (ratio,first,last) + normal))


def check(before, after, variant):
    a,b = selected(before); c,d = selected(after)
    require(a == c, 'Ellipse record moved')
    require(list(map(key,before[:a])) == list(map(key,after[:c])), 'Prefix changed')
    require(list(map(key,before[b:])) == list(map(key,after[d:])), 'Suffix changed')
    source = before[a:b]; result = after[c:d]
    for code, value in values(geometry(variant,False)).items():
        wanted = [] if variant == 1 and code in DEFAULTS else [key((code,value))]
        require([key(t) for t in source if t[0] == code] == wanted, 'Regenerated source geometry differs')
    replacement = values(target(variant))
    present = {code for code,_ in source}
    missing = {code for code,default in DEFAULTS.items()
               if code not in present and (code >= 210 or key((code,default)) != key((code,replacement[code])))}
    anchor = max(i for i,(code,_) in enumerate(source) if code in CODES)
    expected = []
    for i,(code,value) in enumerate(source):
        expected.append((code,replacement.get(code,value)))
        if code in (20,21) and code+10 in missing:
            expected.append((code+10,replacement[code+10]))
        if i == anchor:
            expected.extend((code,replacement[code]) for code in (210,220,230) if code in missing)
    require(list(map(key,result)) == list(map(key,expected)), 'Unexpected plane-edit packet, values, defaults or order')
    return c,d


def check_curve(entity, variant):
    center, major, ratio, first, last, normal = target(variant)
    require(tuple(entity.dxf.center) == center and tuple(entity.dxf.major_axis) == major, 'Independent WCS definition differs')
    require(tuple(entity.dxf.extrusion) == normal, 'Extrusion components were normalized or changed')
    require((entity.dxf.ratio,entity.dxf.start_param,entity.dxf.end_param) == (ratio,first,last), 'Stored parameters changed')
    length = math.hypot(*normal)
    nx,ny,nz = (v/length for v in normal); ax,ay,az = major
    minor = ((ny*az-nz*ay)*ratio,(nz*ax-nx*az)*ratio,(nx*ay-ny*ax)*ratio)
    params = [first+(last-first)*i/16 for i in range(17)]
    actual = list(entity.vertices(params))
    require(len(actual) == len(params), 'Independent sampling inventory')
    for t,p in zip(params,actual):
        expected = tuple(center[i]+major[i]*math.cos(t)+minor[i]*math.sin(t) for i in range(3))
        require(all(math.isclose(p[i],expected[i],rel_tol=2e-13,abs_tol=2e-13) for i in range(3)), 'Independent plane curve differs')


def main(directory):
    specs = list(itertools.product(VERSIONS,(False,True),(False,True),range(4),(False,True)))
    packet_names = {f'ellipse-plane-packet-{layout}-{binary}.dxf' for layout in range(8) for binary in (False,True)}
    def packet_inventory(actual):
        require(actual == packet_names, 'Missing or extra vector-packet fixtures')
    packet_inventory({p.name for p in directory.glob('ellipse-plane-packet-*.dxf')})
    reject(lambda:packet_inventory(packet_names-{next(iter(packet_names))}))
    reject(lambda:packet_inventory(packet_names|{'ellipse-plane-packet-extra.dxf'}))
    stems = [f'ellipse-raw-plane-{v}-{bi}-{bl}-{variant}-{output}' for v,bi,bl,variant,output in specs]
    names = {stem+'-'+side+'.dxf' for stem in stems for side in ('before','after')}
    def inventory(actual):
        require(actual == names, 'Missing or extra raw plane fixtures')
    inventory({p.name for p in directory.glob('ellipse-raw-plane-*.dxf')})
    reject(lambda:inventory(names-{next(iter(names))}))
    reject(lambda:inventory(names|{'ellipse-raw-plane-extra.dxf'}))
    corruptions = 0
    for stem,(version,_,_,variant,binary) in zip(stems,specs):
        paths = [directory/(stem+'-'+side+'.dxf') for side in ('before','after')]
        before,after = map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
            at = tags.index((9,'$ACADVER'))
            require(tags[at+1] == (1,VERSIONS[version]), 'Declared version changed')
        a,b = check(before,after,variant)
        for at in range(a+1,b):
            code,value = after[at]
            for operation in ('change','delete','duplicate'):
                damaged = list(after)
                if operation == 'change':
                    damaged[at] = (code,value+.125 if isinstance(value,(int,float)) else str(value)+'_bad')
                elif operation == 'delete':
                    del damaged[at]
                else:
                    damaged.insert(at,damaged[at])
                corruptions += reject(lambda:check(before,damaged,variant))
        for edited,path in enumerate(paths):
            doc = ezdxf.readfile(path)
            require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent graph errors or repairs')
            entity = doc.entitydb['A']
            require(entity.dxftype() == 'ELLIPSE', 'Selected entity identity changed')
            if edited:
                check_curve(entity,variant)
            else:
                verify_curve(entity,variant,False)
    for name in sorted(packet_names):
        path = directory/name
        tags = load_tags(path)
        a,b = selected(tags)
        record = tags[a:b]
        vector = [(210,0.0),(220,2.0),(230,0.0)]
        def complete_vector(candidate):
            found = [(i,t) for i,t in enumerate(candidate) if t[0] in (210,220,230)]
            require([t for _,t in found] == vector and found[1][0] == found[0][0]+1
                    and found[2][0] == found[0][0]+2, 'Incomplete or split extrusion packet')
        complete_vector(record)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == name.endswith('-True.dxf'), 'Packet transport changed')
        at = tags.index((9,'$ACADVER'));require(tags[at+1] == (1,'AC1032'),'Packet version changed')
        for code in (210,220,230):
            at = next(i for i,t in enumerate(record) if t[0] == code)
            damaged = record[:at]+record[at+1:]
            corruptions += reject(lambda:complete_vector(damaged))
            damaged = record[:at]+[record[at]]+record[at:]
            corruptions += reject(lambda:complete_vector(damaged))
        at = next(i for i,t in enumerate(record) if t[0] == 220)
        corruptions += reject(lambda:complete_vector(record[:at]+[(999,'split')]+record[at:]))
        doc = ezdxf.readfile(path)
        require(not any(any(c.values()) for c in audit_signature(doc)), 'Vector-packet graph errors or repairs')
        check_curve(doc.entitydb['A'],1)
    print(f'PASS: {len(specs)} complete source/edit pairs plus {len(packet_names)} vector-packet drawings '
          f'/ {len(specs)*2+len(packet_names)} drawings; '
          f'{len(specs)*34+len(packet_names)*17} independent curve samples; {corruptions} tag corruptions '
          'and four inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_ellipse_raw_plane.py ARTIFACT_DIRECTORY')
    main(Path(sys.argv[1]))
