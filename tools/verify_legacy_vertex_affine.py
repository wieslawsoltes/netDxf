#!/usr/bin/env python3
"""Check legacy WCS sequences using independent rational affine expectations.

Reuses the independent polygon parser and Fraction/Decimal math, not C# helpers.
Checks complete selected packets except identity values; parser checks identity
framing and child ownership. This is not native AutoCAD qualification.
"""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_polygon_mesh_affine import (
    PROFILES, MODES, matrix, image, normal, records, check_packets, require,
    rejected, corrupt,
)


def expected_packets(polyface, version, mode):
    proxy = [(160 if version in ('AutoCad2013', 'AutoCad2018') else 92, 4),
             (310, bytes((2, 3, 5, 7)))] if mode == 'identity' else []
    head = [(0, 'POLYLINE'), (100, 'AcDbEntity'), (67, 0), (8, 'LEGACY_AFFINE'),
            (62, 3), (6, 'ByLayer'), (370, -1), (48, 1.75), (60, 1)] + proxy + [
            (100, 'AcDbPolyFaceMesh' if polyface else 'AcDb3dPolyline'),
            (10, 0.), (20, 0.), (30, 0.), (70, 64 if polyface else 137), (75, 0),
            *zip((210, 220, 230), normal(mode)),
            (1001, 'LEGACY_AFFINE'), (1000, 'unchanged')]
    packets = [head]
    translation = (7., -11., 13.) if mode in ('translate', 'collapse') else (0., 0., 0.)
    for i in range(6):
        vertex = (float(1+i), float(2-i), 3.+i*.125)
        point = tuple(float(v) for v in image(matrix(mode), vertex, translation))
        packets.append([(0, 'VERTEX'), (100, 'AcDbEntity'), (8, 'LEGACY_AFFINE'), (62, 3),
                        (100, 'AcDbVertex'), (100, 'AcDbPolyFaceMeshVertex' if polyface else 'AcDb3dPolylineVertex'),
                        *zip((10, 20, 30), point), (70, 192 if polyface else 32), (40, 0.), (41, 0.)])
    if polyface:
        for indices in ((1, -2, 3), (3, 4, -5, 6)):
            packets.append([(0, 'VERTEX'), (100, 'AcDbEntity'), (100, 'AcDbFaceRecord'),
                            *zip(range(71, 71+len(indices)), indices),
                            (10, 0.), (20, 0.), (30, 0.), (70, 128), (40, 0.), (41, 0.)])
    packets.append([(0, 'SEQEND'), (100, 'AcDbEntity'), (8, 'LEGACY_AFFINE')])
    return packets


def main():
    directory = Path(sys.argv[1])
    inventory = {f'legacy-vertex-affine-{kind}-{version}-{binary}-{mode}.dxf': (kind, version, binary, mode)
                 for kind, version, binary, mode in itertools.product((False, True), PROFILES, (False, True), MODES)}
    def check_inventory(actual):
        require(actual == set(inventory), 'Legacy affine file inventory differs')
    actual = {p.name for p in directory.glob('legacy-vertex-affine-*')}
    check_inventory(actual)
    inventory_controls = rejected(check_inventory, actual - {next(iter(actual))})
    inventory_controls += rejected(check_inventory, actual | {'legacy-vertex-affine-extra.dxf'})
    controls = 0
    for name, (kind, version, binary, mode) in inventory.items():
        path = directory / name
        data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
        expected = expected_packets(kind, version, mode)
        packets = records(data)
        check = lambda value: check_packets(value, expected)
        check(packets)
        for r, packet in enumerate(packets):
            controls += rejected(check, packets[:r] + packets[r+1:])
            for n, (code, value) in enumerate(packet):
                changed = list(packets); changed[r] = list(packet)
                changed[r][n] = (code, corrupt(value)); controls += rejected(check, changed)
                changed[r] = packet[:n] + packet[n+1:]; controls += rejected(check, changed)
        document = ezdxf.readfile(path)
        require(document.dxfversion == PROFILES[version], 'Wrong profile')
        require(len(document.modelspace().query('POLYLINE')) == 1, 'Wrong placement')
        audit = document.audit()
        require(not audit.errors and not audit.fixes, 'Graph requires repair')
    print(f'PASS: {len(inventory)} legacy affine drawings / {len(inventory)*6} exact WCS vertices; '
          f'{controls} packet and {inventory_controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__ == '__main__':
    main()
