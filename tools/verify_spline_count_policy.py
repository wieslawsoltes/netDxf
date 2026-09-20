#!/usr/bin/env python3
"""Check SPLINE/HELIX count packets independently from the typed library.

Exact group counts and default-versus-opt-in selected records are checked;
this is not a new spline geometry evaluator or a native AutoCAD qualification.
"""
from __future__ import annotations
import argparse
import io
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

VERSIONS = {'AutoCad2000':'AC1015', 'AutoCad2004':'AC1018', 'AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024', 'AutoCad2013':'AC1027', 'AutoCad2018':'AC1032'}
SMALL = ((6,4,0), (7,4,0), (9,6,0), (16,12,4), (7,4,0))
LIMITS = ((32767,32765,0), (32768,32766,0), (32769,32767,0), (32770,32768,0),
          (7,4,32767), (7,4,32768))
COUNTS = (72,73,74)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def record(path, binary):
    data = path.read_bytes()
    require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
    loader = binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0][1] in ('SPLINE','HELIX'):
                records.append(current)
            current = []
        current.append((tag.code, cast_tag_value(tag.code, tag.value)))
    if current and current[0][1] in ('SPLINE','HELIX'):
        records.append(current)
    require(len(records) == 1, 'Selected entity inventory differs')
    return records[0]


def spline_part(tags):
    starts = [i for i, pair in enumerate(tags) if pair == (100,'AcDbSpline')]
    require(len(starts) == 1, 'Spline subclass framing differs')
    start = starts[0] + 1
    end = next((i for i in range(start,len(tags)) if tags[i][0] == 100), len(tags))
    return tags[start:end]


def check(tags, sizes, present):
    part = spline_part(tags)
    for code, size in zip(COUNTS, sizes):
        values = [v for c,v in part if c == code]
        require(values == ([size] if present else []), 'Count missing, repeated or incorrect')
        require(not present or 0 <= size <= 32767, 'Count not representable as nonnegative Int16')
    if present:
        degree = next((i for i,(c,_) in enumerate(part) if c == 71), -1)
        require(degree >= 0 and [c for c,_ in part[degree+1:degree+4]] == list(COUNTS), 'Count order differs')
    for code, size in zip((40,10,11), sizes):
        require(sum(c == code for c,_ in part) == size, 'Count does not match independent payload inventory')
    for code, size in ((20,sizes[1]),(30,sizes[1]),(21,sizes[2]),(31,sizes[2]),(41,sizes[1])):
        require(sum(c == code for c,_ in part) == size, 'Incomplete vector or weight payload')


def rejected(function, value):
    try:
        function(value)
    except ValueError:
        return 1
    raise AssertionError('Corrupted count output escaped the positive validator')


def without_counts(tags):
    # Other subclasses do not currently have these codes, but explicitly scope
    # removal to AcDbSpline rather than concealing unrelated metadata differences.
    in_spline = False
    result = []
    for c,v in tags:
        if c == 100:
            in_spline = v == 'AcDbSpline'
        if in_spline and c in COUNTS:
            continue
        result.append((c,v))
    return result


def main(directory):
    directory = Path(directory)
    specs = []
    for version in VERSIONS:
        for binary in (False,True):
            for kind in range(5):
                if kind == 4 and version in ('AutoCad2000','AutoCad2004'):
                    continue
                for placement in range(3):
                    for policy in range(3):
                        specs.append((f'spline-counts-{version}-{binary}-{kind}-{placement}-{policy}.dxf',
                                      version,binary,SMALL[kind],policy != 0,kind,placement,policy))
    for kind,sizes in enumerate(LIMITS):
        for binary in (False,True):
            for policy in range(3):
                fits = max(sizes) <= 32767
                if policy == 2 and not fits:
                    continue
                specs.append((f'spline-counts-limit-{kind}-{binary}-{policy}.dxf',
                              'AutoCad2018',binary,sizes,policy != 0 and fits,-1,kind,policy))
    expected = {s[0] for s in specs}
    actual = {p.name for p in directory.glob('spline-counts-*.dxf')}
    inventory = lambda names: require(names == expected, 'Count fixture inventory differs')
    inventory(actual)
    controls = rejected(inventory, actual - {next(iter(actual))}) + rejected(inventory, actual | {'spline-counts-extra.dxf'})
    mutations = explicit = large = 0
    for name,version,binary,sizes,present,kind,placement,policy in specs:
        path = directory/name
        tags = record(path,binary); check(tags,sizes,present)
        require(tags[0] == (0,'HELIX' if kind == 4 else 'SPLINE'), 'Entity kind differs')
        validator = lambda data: check(data,sizes,present)
        if present:
            explicit += 1
            for i,(code,value) in enumerate(tags):
                if code not in COUNTS:
                    continue
                for operation in ('wrong','negative','missing','duplicate'):
                    changed = list(tags)
                    if operation == 'wrong': changed[i] = (code,value-1 if value else 1)
                    elif operation == 'negative': changed[i] = (code,-1)
                    elif operation == 'missing': del changed[i]
                    else: changed.insert(i,changed[i])
                    mutations += rejected(validator,changed)
        else:
            at = tags.index((100,'AcDbSpline')) + 1
            for code in COUNTS:
                changed = list(tags); changed.insert(at,(code,0))
                mutations += rejected(validator,changed)
        # Actual payload omissions must not be hidden by matching a stale count.
        start = tags.index((100,'AcDbSpline')) + 1
        end = start + len(spline_part(tags))
        for code in (40,10,11):
            at = next((i for i in range(start,end) if tags[i][0] == code), None)
            if at is not None:
                changed = list(tags); del changed[at]; mutations += rejected(validator,changed)
        if policy:
            base_name = (f'spline-counts-{version}-{binary}-{kind}-{placement}-0.dxf' if kind >= 0
                         else f'spline-counts-limit-{placement}-{binary}-0.dxf')
            require(without_counts(tags) == record(directory/base_name,binary), 'Non-count entity packet changed')
        loaded = ezdxf.readfile(path); require(loaded.dxfversion == VERSIONS[version], 'Version differs')
        audit = loaded.audit(); require(not audit.errors and not audit.fixes, 'Count output needs graph repairs')
        large += kind < 0
    print(f'PASS ezdxf {ezdxf.__version__}: {len(specs)} count-policy drawings, {explicit} explicit packets, '
          f'{large} Int16-boundary drawings; {mutations} actual packet corruptions and {controls} inventory controls rejected; zero graph errors/repairs')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path)
    main(parser.parse_args().directory)
