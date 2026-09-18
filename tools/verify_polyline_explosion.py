#!/usr/bin/env python3
"""Independently verify detached 3D-polyline LINE output and its common metadata.

The wire corpus is unsmoothed; model tests cover the existing spline evaluator.
All selected LINE fields except handle values are compared in their physical
order. Identity framing, common ownership and the graph are checked separately.
"""
import io
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_polygon_mesh_affine import PROFILES, require, rejected, corrupt


def records(data):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    selected, current = [], []
    for tag in loader:
        if tag.code == 0:
            if current and current[0] == (0, 'LINE'):
                selected.append(current)
            current = []
        current.append((tag.code, cast_tag_value(tag.code, tag.value)))
    if current and current[0] == (0, 'LINE'):
        selected.append(current)
    require(selected, 'Missing LINE output')
    identities, owners, result = set(), set(), []
    for packet in selected:
        require([c for c, _ in packet[:3]] == [0, 5, 330], 'LINE identity framing')
        require(sum(c == 5 for c, _ in packet) == 1 and sum(c == 330 for c, _ in packet) == 1, 'Repeated LINE identity')
        handle, owner = int(packet[1][1], 16), int(packet[2][1], 16)
        require(handle > 0 and owner > 0 and handle not in identities, 'Invalid LINE identity')
        identities.add(handle); owners.add(owner); result.append([packet[0]] + packet[3:])
    require(len(owners) == 1, 'Inconsistent LINE owners')
    return result


def expected_packets(closed):
    points = [(float(1+i), float(2-i), 3.+i*.125) for i in range(6)]
    result = []
    for i in range(6 if closed else 5):
        result.append([(0, 'LINE'), (100, 'AcDbEntity'), (67, 0), (8, 'LEGACY_AFFINE'),
                       (62, 3), (6, 'ByLayer'), (370, -1), (48, 1.75), (60, 1), (100, 'AcDbLine'),
                       *zip((10, 20, 30), points[i]), *zip((11, 21, 31), points[(i+1)%6]),
                       (39, 0.), (210, 2.*(1./7.)), (220, -3.*(1./7.)), (230, 6.*(1./7.)),
                       (1001, 'LEGACY_AFFINE'), (1000, 'unchanged')])
    return result


def main():
    directory = Path(sys.argv[1])
    inventory = {f'polyline-explosion-{closed}-{version}-{binary}.dxf': (closed, version, binary)
                 for closed, version, binary in itertools.product((False, True), PROFILES, (False, True))}
    def check_inventory(actual):
        require(actual == set(inventory), 'Exploded file inventory differs')
    actual = {p.name for p in directory.glob('polyline-explosion-*')}
    check_inventory(actual)
    inventory_controls = rejected(check_inventory, actual - {next(iter(actual))})
    inventory_controls += rejected(check_inventory, actual | {'polyline-explosion-extra.dxf'})
    controls = lines = 0
    for name, (closed, version, binary) in inventory.items():
        path = directory / name; data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
        packets, expected = records(data), expected_packets(closed)
        check = lambda value: require(value == expected, 'Selected LINE packet differs')
        check(packets); lines += len(packets)
        for r, packet in enumerate(packets):
            controls += rejected(check, packets[:r] + packets[r+1:])
            for n, (code, value) in enumerate(packet):
                changed = list(packets); changed[r] = list(packet)
                changed[r][n] = (code, corrupt(value)); controls += rejected(check, changed)
                changed[r] = packet[:n] + packet[n+1:]; controls += rejected(check, changed)
        document = ezdxf.readfile(path)
        require(document.dxfversion == PROFILES[version], 'Wrong profile')
        require(len(document.modelspace()) == len(expected), 'Wrong modelspace inventory')
        audit = document.audit(); require(not audit.errors and not audit.fixes, 'Graph requires repair')
    print(f'PASS: {len(inventory)} exploded drawings / {lines} LINE packets; {controls} packet and '
          f'{inventory_controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
