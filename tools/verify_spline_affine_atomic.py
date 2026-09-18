#!/usr/bin/env python3
"""Independent exact affine check of stored SPLINE controls, fits and tangents.

Uses Fraction dot products, not netDxf's dyadic implementation. The fixed source
controls/fit inputs are independently specified; fit-created control polygons
are metamorphic input, not independently requalified fitting. Every ordered tag
in each selected SPLINE record (including handles and proxy policy) is compared.
Unselected document records and auxiliary model normals are outside this gate.
"""
from __future__ import annotations
import argparse
from fractions import Fraction
import io
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

VERSIONS = {'AutoCad2000':'AC1015', 'AutoCad2004':'AC1018', 'AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024', 'AutoCad2013':'AC1027', 'AutoCad2018':'AC1032'}
MODES = ('identity', 'translation', 'reflection', 'scale', 'shear', 'collapse')
POINTS = [(1.,2.,3.), (-4.,5.,-6.), (7.,-8.,9.), (10.,11.,-12.)]
IDENTITY = ((1,0,0),(0,1,0),(0,0,1))
MATRICES = {'reflection':((-1,0,0),(0,1,0),(0,0,1)), 'scale':((2,0,0),(0,3,0),(0,0,4)),
            'shear':((1,2,0),(0,1,3),(4,0,1)), 'collapse':((0,0,0),(0,0,0),(0,0,0))}
TRANSLATIONS = {'translation':(5,-7,11), 'reflection':(2,3,4), 'scale':(-1,2,-3),
                'shear':(1,-2,3), 'collapse':(9,8,7)}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def packet(path: Path, binary: bool):
    data = path.read_bytes()
    require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    loader = binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0] == (0, 'SPLINE'):
                records.append(current)
            current = []
        value = cast_tag_value(tag.code, tag.value)
        if tag.code == 310:
            value = bytes(tag.value) if isinstance(tag.value, (bytes, bytearray)) else bytes.fromhex(str(tag.value))
        current.append((tag.code, value))
    if current and current[0] == (0, 'SPLINE'):
        records.append(current)
    require(len(records) == 1, 'Expected exactly one SPLINE packet')
    return records[0]


def vectors(record, code):
    result = []
    for i, (c, value) in enumerate(record):
        if c != code:
            continue
        require(i + 2 < len(record) and [c for c, _ in record[i:i+3]] == [code, code+10, code+20], 'Incomplete coordinate')
        result.append(tuple(float(v) for _, v in record[i:i+3]))
    return result


def expected_packet(source, kind, mode):
    expected_controls = POINTS if kind == 0 else POINTS[-2:] + POINTS
    if kind == 1:
        require(vectors(source, 11) == POINTS, 'Changed source fit points')
    else:
        require(vectors(source, 10) == expected_controls and not vectors(source, 11), 'Changed source controls')
    require(vectors(source, 12) == [(2.,-3.,5.)] and vectors(source, 13) == [(-7.,11.,13.)], 'Changed source tangents')
    if mode == 'identity':
        return source
    m, translation = MATRICES.get(mode, IDENTITY), TRANSLATIONS.get(mode, (0,0,0))
    result, i = [], 0
    while i < len(source):
        code, value = source[i]
        # Common proxy count uses 92 or 160 according to the typed profile.
        if code in (92, 160, 310):
            i += 1
            continue
        if code in (10, 11, 12, 13):
            require([c for c, _ in source[i:i+3]] == [code, code+10, code+20], 'Wrong vector framing')
            point = [Fraction(float(v)) for _, v in source[i:i+3]]
            t = translation if code in (10, 11) else (0,0,0)
            result.extend((code + 10*r, float(sum(Fraction(m[r][c])*point[c] for c in range(3)) + t[r])) for r in range(3))
            i += 3
            continue
        # IsClosed is derived from the control endpoints. A collapsed source
        # becomes closed; the stored periodic and creation flags remain intact.
        if code == 70 and mode == 'collapse':
            value |= 1
        result.append((code, value))
        i += 1
    return result


def validate(expected, actual):
    require(len(expected) == len(actual), 'Selected packet length changed')
    for wanted, found in zip(expected, actual):
        require(wanted == found, f'Selected packet changed: {wanted!r} != {found!r}')


def reject(expected, actual):
    try:
        validate(expected, actual)
    except ValueError:
        return 1
    raise AssertionError('Actual packet corruption escaped the positive validator')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    root = parser.parse_args().directory
    names = {f'spline-affine-atomic-{kind}-{mode}-{four}-{version}-{binary}-{phase}.dxf'
             for kind in range(3) for mode in MODES for four in (False, True)
             for version in VERSIONS for binary in (False, True) for phase in ('source','after')}
    require({p.name for p in root.glob('spline-affine-atomic-*.dxf')} == names, 'Missing or extra spline affine fixtures')
    cases = controls = points = 0
    for kind in range(3):
        for mode in MODES:
            for four in (False, True):
                for version, profile in VERSIONS.items():
                    for binary in (False, True):
                        stem = f'spline-affine-atomic-{kind}-{mode}-{four}-{version}-{binary}'
                        source = packet(root/(stem+'-source.dxf'), binary)
                        actual = packet(root/(stem+'-after.dxf'), binary)
                        expected = expected_packet(source, kind, mode)
                        validate(expected, actual)
                        points += sum(len(vectors(actual, code)) for code in (10,11,12,13))
                        for i, (code, value) in enumerate(actual):
                            altered = (value + b'\0' if isinstance(value, bytes) else value + '!' if isinstance(value, str) else value + 0.125 if isinstance(value, float) else value + 1)
                            changed = list(actual); changed[i] = (code, altered)
                            controls += reject(expected, changed)
                            controls += reject(expected, actual[:i] + actual[i+1:])
                            controls += reject(expected, actual[:i] + [actual[i]] + actual[i:])
                        for phase in ('source','after'):
                            doc = ezdxf.readfile(root/(stem+f'-{phase}.dxf'))
                            require(doc.dxfversion == profile, 'Wrong version')
                            audit = doc.audit()
                            require(not audit.errors and not audit.fixes, 'SPLINE graph requires repair')
                        cases += 1
    print(f'PASS ezdxf {ezdxf.__version__}: {cases} affine pairs / {cases*2} drawings / {points} exact WCS vectors; '
          f'{controls} ordered-packet corruptions rejected; exact output inventory; zero graph errors/repairs')


if __name__ == '__main__':
    main()
