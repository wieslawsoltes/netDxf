#!/usr/bin/env python3
"""Verify world-space ARC/CIRCLE affine images with ezdxf's independent OCS.

Samples the original parameterized arc and circular locus, and compares the
transformed extrusion vector. Does not use an emitted expected-coordinate file.
"""
import argparse
import copy
import math
from pathlib import Path
import ezdxf
from ezdxf.math import Vec3, OCS
from verify_mleader_inputs import check
from verify_legacy_utilities import PROFILES

MODES = {
    'translate': ((1,0,0),(0,1,0),(0,0,1)),
    'rotate': ((0,-1,0),(1,0,0),(0,0,1)),
    'swap': ((0,1,0),(1,0,0),(0,0,1)),
    'mirror': ((-1,0,0),(0,1,0),(0,0,1)),
    'negative': ((-2,0,0),(0,-2,0),(0,0,-2)),
    'axial': ((2,0,0),(0,2,0),(0,0,-3)),
    'tilted': ((0,-2,0),(0,0,2),(2,0,0)),
    'normal-shear': ((1,0,.5),(0,1,0),(0,0,1)),
}
CENTER, TRANSLATION = Vec3(3,-4,5), Vec3(11,-7,13)


def multiply(matrix, vector):
    return Vec3(*(sum(row[i] * vector[i] for i in range(3)) for row in matrix))


def close(actual, expected, name):
    if isinstance(actual, (Vec3, tuple, list)):
        check(all(math.isclose(a, b, rel_tol=1e-11, abs_tol=1e-10) for a,b in zip(actual,expected)), name)
    else:
        check(math.isclose(actual, expected, rel_tol=1e-11, abs_tol=1e-10), name)


def verify(entity, mode):
    matrix = MODES[mode]
    normal = Vec3(1,2,3).normalize() if mode == 'tilted' else Vec3(0,0,1)
    source_ocs = OCS(normal)
    target_ocs = entity.ocs()
    actual_center = target_ocs.to_wcs(entity.dxf.center)
    wanted_center = multiply(matrix, CENTER) + TRANSLATION
    close(actual_center, wanted_center, 'circular WCS center differs')
    thickness = 0 if mode == 'normal-shear' else -1.75
    close(Vec3(entity.dxf.extrusion) * entity.dxf.thickness, multiply(matrix, normal * thickness), 'signed extrusion differs')
    close(Vec3(entity.dxf.extrusion).magnitude, 1, 'non-unit extrusion')
    check(entity.dxf.invisible == 1 and entity.dxf.color == 3, 'appearance changed')
    check(entity.proxy_graphic is None, 'stale proxy retained')
    if entity.dxftype() == 'ARC':
        span = (entity.dxf.end_angle - entity.dxf.start_angle) % 360
        close(span, 260, 'arc sweep changed')
        for i in range(33):
            a = math.radians(25 + 260 * i / 32)
            source_point = CENTER + source_ocs.to_wcs(Vec3(2.5*math.cos(a),2.5*math.sin(a),0))
            wanted = multiply(matrix, source_point) + TRANSLATION
            target_a = math.radians(entity.dxf.start_angle + span * i / 32)
            actual = actual_center + target_ocs.to_wcs(Vec3(entity.dxf.radius*math.cos(target_a),entity.dxf.radius*math.sin(target_a),0))
            close(actual, wanted, 'parameterized arc image differs')
    else:
        for i in range(32):
            a = math.tau * i / 32
            source_point = CENTER + source_ocs.to_wcs(Vec3(2.5*math.cos(a),2.5*math.sin(a),0))
            delta = multiply(matrix, source_point) + TRANSLATION - actual_center
            close(delta.magnitude, entity.dxf.radius, 'transformed circle radius differs')
            close(delta.dot(entity.dxf.extrusion), 0, 'transformed circle plane differs')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    expected = {f'circular-review-{version}-{binary}-{mode}.dxf' for version in PROFILES for binary in (False,True) for mode in MODES}
    check({p.name for p in directory.glob('circular-review-*.dxf')} == expected, 'Circular fixture inventory differs')
    controls = 0
    for version,profile in PROFILES.items():
        for binary in (False,True):
            for mode in MODES:
                path = directory / f'circular-review-{version}-{binary}-{mode}.dxf'
                check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
                doc = ezdxf.readfile(path)
                check(doc.dxfversion == profile, 'Profile changed')
                entities = list(doc.modelspace())
                check([e.dxftype() for e in entities] == ['CIRCLE','ARC'], 'Entity inventory differs')
                for entity in entities:
                    verify(entity,mode)
                    for field in ['center','radius','extrusion','thickness','invisible','color'] + (['start_angle','end_angle'] if entity.dxftype() == 'ARC' else []):
                        changed = entity.copy()
                        value = changed.dxf.get(field)
                        setattr(changed.dxf, field, value + Vec3(.25,.5,.75) if field in ('center','extrusion') else value + 1)
                        try:
                            verify(changed,mode)
                        except ValueError:
                            controls += 1
                        else:
                            raise AssertionError('Corrupted circular field escaped verifier: ' + field)
                audit = doc.audit()
                check(not audit.errors and not audit.fixes, 'Circular output needs graph repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: 96 circular drawings, 192 entities sampled in world coordinates, {controls} corrupted fields rejected')


if __name__ == '__main__':
    main()
