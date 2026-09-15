#!/usr/bin/env python3
"""Verify terminal-declaration MESH controls and actual output corruption detection."""
import argparse
import copy
import json
from pathlib import Path

import ezdxf
from verify_mesh_override_declaration import EXPECTED, packets, selected, validate
from verify_mleader_inputs import check

SCENARIOS = ('private', 'nested', 'later-subclass', 'private-subclass', 'xdata', 'comments')
TRAILING_FIELDS = ((71, 3), (72, 0), (90, 0), (91, 7), (92, 0), (93, 0),
                   (94, 0), (95, 0), (10, [99.0, 98.0, 97.0]),
                   (20, 98.0), (30, 97.0), (140, 9.0))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    count, original = 0, None
    for year, profile in ((2010, 'AC1024'), (2013, 'AC1027'), (2018, 'AC1032')):
        for binary in (False, True):
            for optional in (False, True):
                expected = EXPECTED[:13] + [[94, 0], [95, 0], [90, 0]] if optional else EXPECTED
                for scenario in SCENARIOS:
                    path = args.artifacts / f'mesh-terminal-AutoCad{year}-{binary}-{optional}-{scenario}.dxf'
                    data = path.read_bytes()
                    check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
                    records = packets(data)
                    validate(records, expected)
                    check([5, '200'] in selected(records, 'MESH'), 'Mesh identity changed')
                    check([5, '201'] in selected(records, 'LINE'), 'Following entity identity changed')
                    document = ezdxf.readfile(path)
                    check(document.dxfversion == profile, 'Profile changed')
                    mesh = document.entitydb['200']
                    check(mesh.dxftype() == 'MESH' and mesh.dxf.subdivision_levels == 3, 'Subdivision changed')
                    count_before = len(document.entitydb)
                    audit = document.audit()
                    check(not audit.errors and not audit.fixes and len(document.entitydb) == count_before,
                          'Audit required an error, repair or entity removal')
                    if original is None:
                        original = records
                    count += 1
    controls = []
    for code, value in TRAILING_FIELDS:
        corrupt = copy.deepcopy(original)
        mesh = selected(corrupt, 'MESH')
        mesh.insert(mesh.index([1001, 'MESH_READ']), [code, value])
        try:
            validate(corrupt, EXPECTED)
        except (ValueError, AssertionError):
            controls.append(code)
        else:
            raise ValueError(f'Trailing field {code} in an actual output escaped the wire oracle')
    print(json.dumps({'passed': True, 'outputs': count, 'negative_controls': controls,
                      'exact_mesh_and_following_identities': count,
                      'audit_errors': 0, 'audit_repairs': 0, 'audit_removed_entities': 0,
                      'ezdxf': ezdxf.__version__}))


if __name__ == '__main__':
    main()
