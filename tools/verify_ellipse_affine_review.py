#!/usr/bin/env python3
"""Independently check affine ELLIPSE loci and directed arc samples in WCS.

The oracle transforms the original parametric source, not production principal
axes or an emitted coordinate manifest. Uses ezdxf's independent OCS and parser.
"""
import argparse
import math
from pathlib import Path
import ezdxf
from ezdxf.math import OCS, Vec3
from verify_mleader_inputs import check
from verify_legacy_utilities import PROFILES

MODES = {
    'translate': ((1,0,0),(0,1,0),(0,0,1)),
    'rotate': ((0,-1,0),(1,0,0),(0,0,1)),
    'swap': ((0,1,0),(1,0,0),(0,0,1)),
    'mirror': ((-1,0,0),(0,1,0),(0,0,1)),
    'negative': ((-2,0,0),(0,-2,0),(0,0,-2)),
    'anisotropic': ((2,0,0),(0,3,0),(0,0,4)),
    'shear': ((1,.75,0),(.2,1,0),(0,0,1)),
    'tilted': ((1,.75,.2),(-.3,2,.1),(.4,.25,1)),
    'flat': ((1,0,0),(0,1,0),(0,0,0)),
    'small': ((1e-4,0,0),(0,1e-4,0),(0,0,1e-4)),
}
CENTER, TRANSLATION = Vec3(3,-4,5), Vec3(11,-7,13)


def multiply(matrix, vector):
    return Vec3(*(sum(row[i] * vector[i] for i in range(3)) for row in matrix))


def close(actual, expected, name):
    if isinstance(actual, (Vec3, tuple, list)):
        check(all(math.isclose(a,b,rel_tol=1e-11,abs_tol=1e-10) for a,b in zip(actual,expected)), name)
    else:
        check(math.isclose(actual,expected,rel_tol=1e-10,abs_tol=1e-9), name)


def source_axes(mode):
    normal = Vec3(1,2,3).normalize() if mode == 'tilted' else Vec3(0,0,1)
    axes = OCS(normal)
    angle = math.radians(37)
    return (axes.to_wcs(Vec3(4*math.cos(angle),4*math.sin(angle),0)),
            axes.to_wcs(Vec3(-1.5*math.sin(angle),1.5*math.cos(angle),0)))


def polar_parameter(degrees):
    angle = math.radians(degrees)
    return math.atan2(math.sin(angle),.375*math.cos(angle)) % math.tau


def verify(entity, mode, full):
    matrix = MODES[mode]
    source_major, source_minor = source_axes(mode)
    first, second = multiply(matrix,source_major), multiply(matrix,source_minor)
    center = multiply(matrix,CENTER) + TRANSLATION
    actual_center = Vec3(entity.dxf.center)
    major = Vec3(entity.dxf.major_axis)
    normal = Vec3(entity.dxf.extrusion)
    ratio = entity.dxf.ratio
    close(actual_center,center,'WCS ellipse center differs')
    check(all(math.isfinite(v) for v in (*major,*normal,ratio,entity.dxf.start_param,entity.dxf.end_param)), 'Nonfinite ellipse geometry')
    check(major.magnitude > 0 and 0 < ratio <= 1,'Invalid ellipse major/ratio')
    close(normal.magnitude,1,'Ellipse normal is not unit length')
    close(normal,first.cross(second).normalize(),'Ellipse normal orientation changed')
    close(major.dot(normal),0,'Major axis is outside ellipse plane')
    minor = normal.cross(major) * ratio
    start, end = entity.dxf.start_param, entity.dxf.end_param
    if full:
        close(abs(end-start),math.tau,'Full ellipse parameter interval changed')
    else:
        check(abs(end-start) < math.tau,'Partial ellipse became full')
        span = (end-start) % math.tau
        source_start = polar_parameter(25)
        source_span = (polar_parameter(285)-source_start) % math.tau
        close(span,source_span,'Directed elliptical sweep changed')
    for index in range(33):
        parameter = math.tau*index/32 if full else source_start+source_span*index/32
        point = center+first*math.cos(parameter)+second*math.sin(parameter)
        delta = point-actual_center
        close(delta.dot(normal),0,'Transformed ellipse point not in output plane')
        # Orthogonal major/minor coordinates must satisfy the implicit ellipse.
        x, y = delta.dot(major)/major.magnitude_square, delta.dot(minor)/minor.magnitude_square
        close(x*x+y*y,1,'Transformed point not on output ellipse')
        if not full:
            output_parameter = start+span*index/32
            actual = actual_center+major*math.cos(output_parameter)+minor*math.sin(output_parameter)
            close(actual,point,'Parameterized arc point or traversal changed')
    check(entity.dxf.invisible == 1 and entity.dxf.color == 3,'Appearance changed')
    check(entity.proxy_graphic is None,'Stale ellipse proxy retained')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path)
    directory = parser.parse_args().directory
    wanted = {f'ellipse-review-{version}-{binary}-{mode}.dxf' for version in PROFILES for binary in (False,True) for mode in MODES}
    check({p.name for p in directory.glob('ellipse-review-*.dxf')} == wanted,'Exact ellipse fixture inventory required')
    controls = 0
    for version,profile in PROFILES.items():
        for binary in (False,True):
            for mode in MODES:
                path = directory/f'ellipse-review-{version}-{binary}-{mode}.dxf'
                check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary,'Ellipse transport changed')
                doc = ezdxf.readfile(path)
                check(doc.dxfversion == profile,'Ellipse profile changed')
                entities = list(doc.modelspace())
                check(len(entities) == 2 and all(e.dxftype() == 'ELLIPSE' for e in entities),'Ellipse output inventory changed')
                for entity,full in zip(entities,(True,False)):
                    verify(entity,mode,full)
                    fields = ['center','major_axis','extrusion','ratio','invisible','color']
                    if not full: fields += ['start_param','end_param']
                    for field in fields:
                        changed = entity.copy()
                        value = changed.dxf.get(field)
                        setattr(changed.dxf,field,Vec3(value)+Vec3(.25,.5,.75) if field in ('center','major_axis','extrusion') else value+.1 if field == 'ratio' else value+1)
                        try: verify(changed,mode,full)
                        except ValueError: controls += 1
                        else: raise AssertionError('Changed ellipse field escaped the same verifier: '+field)
                    changed = entity.copy(); changed.proxy_graphic = b'\x01\x02\x03'
                    try: verify(changed,mode,full)
                    except ValueError: controls += 1
                    else: raise AssertionError('Stale proxy escaped the same verifier')
                audit = doc.audit()
                check(not audit.errors and not audit.fixes,'Ellipse output needs independent repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: 120 ellipse drawings, 240 WCS loci/arcs, {controls} field/proxy corruptions rejected')


if __name__ == '__main__':
    main()
