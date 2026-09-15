#!/usr/bin/env python3
"""Verify 64 zero-override MESH outputs, exact native bodies and corruption controls."""
import argparse
import copy
import gzip
import hashlib
import io
import json
import struct
from pathlib import Path

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from verify_mleader_inputs import check, json_value

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/fixtures/table-oracle/sample_AC1024_ascii.dxf.gz'
SOURCE_SHA = 'c97e857047ad4cecd84754ea1a8638e47508e339fd489f1e895ee7332127b372'
SCENARIOS = ('zero', 'absent', 'private-before', 'private-after', 'private-nested',
             'later-subclass', 'xdata-trailer', 'comments')
EXPECTED = [[100, 'AcDbSubDMesh'], [71, 2], [72, 1], [91, 3], [92, 3],
            [10, [1e-20, 0.0, 0.0]], [10, [1.0, 0.0, 0.0]], [10, [0.0, 1.0, 0.0]],
            [93, 4], [90, 3], [90, 0], [90, 1], [90, 2], [94, 1], [90, 0],
            [90, 2], [95, 1], [140, 1.25], [90, 0]]


def packets(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig')))
    result, current = [], []
    for tag in tag_compiler(loader):
        if tag.code == 0:
            if current:
                result.append(current)
            current = []
        current.append([tag.code, json_value(tag.value)])
    if current:
        result.append(current)
    return result


def selected(records, name):
    values = [r for r in records if r[0] == [0, name]]
    check(len(values) == 1, f'Expected exactly one {name}')
    return values[0]


def body(record):
    begin = record.index([100, 'AcDbSubDMesh'])
    end = next((i for i in range(begin, len(record)) if record[i][0] == 1001), len(record))
    return record[begin:end]


def exact(value):
    if isinstance(value, float):
        return struct.pack('<d', value)
    if isinstance(value, list):
        return [exact(item) for item in value]
    return value


def validate(records, expected):
    mesh = selected(records, 'MESH')
    check(exact(body(mesh)) == exact(expected), 'Ordered MESH topology/override packet changed')
    xdata = mesh[mesh.index([1001, 'MESH_READ']):]
    check(xdata == [[1001, 'MESH_READ'], [1000, 'following XData']], 'Following XData changed')
    line = selected(records, 'LINE')
    check([10, [7.0, 8.0, 9.0]] in line and [11, [10.0, 11.0, 12.0]] in line, 'Following LINE changed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    raw = gzip.decompress(SOURCE.read_bytes())
    check(hashlib.sha256(raw).hexdigest() == SOURCE_SHA, 'Pinned native source digest changed')
    native = {next(value for code, value in record if code == 5): body(record)
              for record in packets(raw) if record[0] == [0, 'MESH']}
    check(set(native) == {'343', '380'}, 'Native MESH inventory changed')
    check(all(tags[-1] == [90, 0] for tags in native.values()), 'Native control is not zero-override')
    count, original, native_original = 0, None, None
    for year, profile in ((2010, 'AC1024'), (2013, 'AC1027'), (2018, 'AC1032')):
        for binary in (False, True):
            for scenario in SCENARIOS:
                path = args.artifacts / f'mesh-override-AutoCad{year}-{binary}-{scenario}.dxf'
                data = path.read_bytes()
                check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Output transport changed')
                records = packets(data)
                validate(records, EXPECTED)
                document = ezdxf.readfile(path)
                check(document.dxfversion == profile, 'Output profile changed')
                audit = document.audit()
                check(not audit.errors and not audit.fixes, 'Output required errors/repairs')
                original = records if original is None else original
                count += 1
            for scenario in ('zero', 'absent'):
                path = args.artifacts / f'mesh-override-optional-AutoCad{year}-{binary}-{scenario}.dxf'
                data = path.read_bytes()
                check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Optional-edge transport changed')
                expected = EXPECTED[:13] + [[94, 0], [95, 0], [90, 0]]
                validate(packets(data), expected)
                document = ezdxf.readfile(path)
                check(document.dxfversion == profile, 'Optional-edge output profile changed')
                audit = document.audit()
                check(not audit.errors and not audit.fixes, 'Optional-edge output required errors/repairs')
                count += 1
    for binary in (False, True):
        for handle, expected in native.items():
            path = args.artifacts / f'mesh-override-native-{handle}-{binary}.dxf'
            data = path.read_bytes()
            check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Native carrier transport changed')
            records = packets(data)
            validate(records, expected)
            document = ezdxf.readfile(path)
            check(document.dxfversion == 'AC1024', 'Native carrier profile changed')
            audit = document.audit()
            check(not audit.errors and not audit.fixes, 'Native carrier required errors/repairs')
            native_original = (records, expected)
            count += 1
    controls = []
    for name in ('nonzero-count', 'subdivision', 'missing-vertex', 'following-line', 'missing-mesh'):
        corrupt = copy.deepcopy(original)
        mesh = selected(corrupt, 'MESH')
        if name == 'nonzero-count':
            mesh[mesh.index([1001, 'MESH_READ']) - 1] = [90, 1]
        elif name == 'subdivision':
            mesh[mesh.index([91, 3])] = [91, 7]
        elif name == 'missing-vertex':
            mesh.remove([10, [1e-20, 0.0, 0.0]])
        elif name == 'following-line':
            line = selected(corrupt, 'LINE')
            line[line.index([10, [7.0, 8.0, 9.0]])] = [10, [70.0, 8.0, 9.0]]
        else:
            corrupt.remove(mesh)
        try:
            validate(corrupt, EXPECTED)
        except (ValueError, AssertionError):
            controls.append(name)
        else:
            raise ValueError(f'Actual output corruption escaped: {name}')
    corrupt = copy.deepcopy(native_original[0])
    mesh = selected(corrupt, 'MESH')
    mesh[mesh.index([1001, 'MESH_READ']) - 1] = [90, 1]
    try:
        validate(corrupt, native_original[1])
    except (ValueError, AssertionError):
        controls.append('native-override-count')
    else:
        raise ValueError('Native packet corruption escaped')
    print(json.dumps({'passed': True, 'outputs': count, 'native_source_packets': 2,
                      'exact_native_output_bodies': 4, 'negative_controls': controls,
                      'audit_errors': 0, 'audit_fixes': 0, 'ezdxf': ezdxf.__version__}))


if __name__ == '__main__':
    main()
