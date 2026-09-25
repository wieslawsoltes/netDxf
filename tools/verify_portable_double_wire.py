#!/usr/bin/env python3
"""Check typed POINT numeric packets from independently injected decimal inputs."""
import math
from pathlib import Path
import struct
import sys
import ezdxf
from verify_portable_double_parsing import boundary, require, reject
from verify_ellipse_axis_proxies import load_visibility_tags
from verify_raw_line_geometry import audit_signature

PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}


def specifications():
    edges = (0, 1, 0xfffffffffffff, 0x3ff0000000000000, 0x7fefffffffffffff)
    return [row for i, edge in enumerate(edges) for row in boundary(f'wire/{i}', edge, 1) if row[2] is not None]


def one(record, code):
    values = [value for c, value in record if c == code]
    require(len(values) == 1, 'Missing/duplicate field ' + str(code))
    return values[0]


def point_records(tags):
    starts = [i for i, tag in enumerate(tags) if tag == (0, 'POINT')]
    return [tags[start:next(i for i in range(start + 1, len(tags)) if tags[i][0] == 0)] for start in starts]


def check_points(records, expected):
    require(len(records) == len(expected), 'POINT inventory')
    handles = []
    for record, (_, _, bits) in zip(records, expected):
        require(struct.pack('>d', one(record, 10)).hex() == bits, 'Incorrect exact POINT coordinate')
        require(one(record, 20) == 2.0 and one(record, 30) == 3.0, 'Following coordinates')
        handles.append(one(record, 5))
        require(type(one(record, 330)) is str, 'Owner missing')
    require(len(set(handles)) == len(handles), 'Duplicate POINT handles')


def main(directory):
    expected = specifications()
    require(len(expected) == 26, 'Wire specification count')
    names = {f'portable-double-wire-AutoCad{year}-{transport}-{stage}.dxf'
             for year in PROFILES for transport in ('text', 'binary') for stage in ('source', 'output', 'resave')}
    inventory = lambda actual: require(actual == names, 'Wire file inventory')
    inventory({path.name for path in directory.glob('portable-double-wire-*.dxf')})
    controls = reject(lambda: inventory(names - {next(iter(names))})) + reject(lambda: inventory(names | {'extra.dxf'}))
    for year in PROFILES:
        for transport in ('text', 'binary'):
            source_handles = None
            for stage in ('source', 'output', 'resave'):
                path = directory / f'portable-double-wire-AutoCad{year}-{transport}-{stage}.dxf'
                data = path.read_bytes()
                require(data.startswith(b'AutoCAD Binary DXF') == (transport == 'binary' and stage == 'output'), 'Transport')
                tags = load_visibility_tags(path)
                index = tags.index((9, '$ACADVER'))
                require(tags[index + 1] == (1, PROFILES[year]), 'Version')
                records = point_records(tags)
                check_points(records, expected)
                handles = [one(record, 5) for record in records]
                if source_handles is None: source_handles = handles
                require(handles == source_handles, 'Handles changed across save')
                for row in records:
                    damaged = [list(record) for record in records]
                    target = records.index(row)
                    index = next(i for i, tag in enumerate(row) if tag[0] == 10)
                    bits = struct.unpack('>Q', struct.pack('>d', row[index][1]))[0] ^ 1
                    damaged[target][index] = (10, struct.unpack('>d', struct.pack('>Q', bits))[0])
                    controls += reject(lambda: check_points(damaged, expected))
                doc = ezdxf.readfile(path)
                require(not any(any(counts.values()) for counts in audit_signature(doc)), 'Independent audit errors/repairs')
                points = list(doc.modelspace().query('POINT'))
                require(len(points) == len(expected), 'Independent point inventory')
                for point, (_, _, bits) in zip(points, expected):
                    require(struct.pack('>d', point.dxf.location.x).hex() == bits, 'Independent coordinate bits')
                    require(tuple(point.dxf.location)[1:] == (2., 3.), 'Independent Y/Z')
                lines = list(doc.modelspace().query('LINE'))
                require(len(lines) == 1 and tuple(lines[0].dxf.start) == (1., 2., 3.)
                        and tuple(lines[0].dxf.end) == (4., 5., 6.), 'Independent following LINE')
    print(f'PASS: {len(names)} typed decimal drawings / {len(names)*len(expected)} POINT records; '
          f'{controls} corruption/inventory controls rejected; zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_portable_double_wire.py ARTIFACTS')
    main(Path(sys.argv[1]))
