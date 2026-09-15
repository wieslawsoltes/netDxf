"""Independent ezdxf object-model review of the owner's pattern output corpus.

This script executes no netDxf code and imports no module verifier. Its source
expectations are decoded from the pinned producer files. World mappings below
are independently written scalar operations; OCS construction is ezdxf's.
"""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path
import ezdxf
from ezdxf.math import OCS, Vec3

p = argparse.ArgumentParser()
p.add_argument('fixtures', type=Path)
p.add_argument('outputs', type=Path)
p.add_argument('report', type=Path)
a = p.parse_args()
manifest_file = a.fixtures / 'manifest.json'
manifest = json.loads(manifest_file.read_text())
assert ezdxf.__version__ == manifest['version'] == '1.4.4'
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def map_point(v, operation, translate=True):
    x, y, z = v
    if operation == 0: q = (x, y, z)
    elif operation == 1:
        angle = .43
        q = (math.cos(angle)*x-math.sin(angle)*y, math.sin(angle)*x+math.cos(angle)*y, z)
    elif operation == 2: q = (2*x, 3*y, z/2)
    elif operation == 3: q = (x + .75*y - .2*z, y + .5*z, z)
    elif operation == 4: q = (-x, y, z)
    elif operation == 5: q = (2*x, 3*y, 0)
    else: q = (100*x, y/10, 2*z)
    return Vec3(q) + (Vec3(7,-11,0) if translate else Vec3())
maximum = 0.0
points = 0
families = 0
output_hashes = {}
def compare(expected, actual):
    global maximum, points
    distance = expected.distance(actual)
    assert math.isfinite(distance) and distance <= 2e-10 * max(1, expected.magnitude, actual.magnitude), (expected, actual, distance)
    maximum = max(maximum, distance)
    points += 1
def world(ocs, line, offset, distance, elevation):
    x, y = line.base_point + offset * line.offset
    angle = math.radians(line.angle)
    return ocs.to_wcs((x + distance*math.cos(angle), y + distance*math.sin(angle), elevation))
for fixture in manifest['files']:
    path = a.fixtures / fixture['file']
    assert sha(path) == fixture['sha256']
    source = ezdxf.readfile(path)
    source_hatches = {h.dxf.layer:h for h in source.modelspace().query('HATCH')}
    assert set(source_hatches) == {'PREDEFINED','CUSTOM'}
    for operation in range(7):
        for plane in range(2):
            out = a.outputs / f'hatch-pattern-affine-AutoCad{fixture["year"]}-{fixture["binary"]}-{operation}-{plane}.dxf'
            drawing = ezdxf.readfile(out)
            audit = drawing.audit()
            assert not audit.errors and not audit.fixes
            assert drawing.dxfversion == source.dxfversion
            actual = {h.dxf.layer:h for h in drawing.modelspace().query('HATCH')}
            assert actual.keys() == source_hatches.keys()
            old_ocs = OCS((0,0,1) if plane == 0 else (1,2,3))
            for name, old in source_hatches.items():
                new = actual[name]
                assert old.pattern is not None and new.pattern is not None
                assert len(old.pattern.lines) == len(new.pattern.lines)
                assert old.dxf.pattern_type == new.dxf.pattern_type
                assert old.dxf.pattern_double == new.dxf.pattern_double == 0
                new_ocs = new.ocs()
                for before, after in zip(old.pattern.lines, new.pattern.lines):
                    assert len(before.dash_length_items) == len(after.dash_length_items)
                    angle = math.radians(before.angle)
                    direction = map_point(old_ocs.to_wcs((math.cos(angle),math.sin(angle),0)), operation, False)
                    stretch = direction.magnitude
                    actual_angle = math.radians(after.angle)
                    compare(direction / stretch, new_ocs.to_wcs((math.cos(actual_angle),math.sin(actual_angle),0)))
                    old_period = sum(map(abs,before.dash_length_items))
                    new_period = sum(map(abs,after.dash_length_items))
                    old_marks = [0.0]
                    new_marks = [0.0]
                    old_end = new_end = 0.0
                    for b, c in zip(before.dash_length_items, after.dash_length_items):
                        assert math.copysign(1,b) == math.copysign(1,c)
                        if b == 0: assert struct.pack('>d',b) == struct.pack('>d',c)
                        assert abs(c-b*stretch) <= 2e-10 * max(1,abs(b*stretch))
                        for fraction in (1/3,2/3,1):
                            old_marks.append(old_end + fraction*abs(b))
                            new_marks.append(new_end + fraction*abs(c))
                        old_end += abs(b)
                        new_end += abs(c)
                    if not before.dash_length_items:
                        old_marks = [-17.25,.125,31.75]
                        new_marks = [m*stretch for m in old_marks]
                    for row in (-4,1,6):
                        for cycle in (-3,0,2):
                            for b, c in zip(old_marks,new_marks):
                                expected = map_point(world(old_ocs,before,row,cycle*old_period+b,4),operation)
                                observed = world(new_ocs,after,row,cycle*new_period+c,new.dxf.elevation.z)
                                compare(expected,observed)
                    families += 1
                old_data = list(old.get_xdata('ACAD'))
                new_data = list(new.get_xdata('ACAD'))
                assert len(old_data) == len(new_data)
                index = next(i for i,t in enumerate(old_data) if t.code == 1010)
                compare(map_point(Vec3(old_data[index].value),operation),Vec3(new_data[index].value))
                assert new_data[index].value[2] == 0
                assert old_data[:index] + old_data[index+1:] == new_data[:index] + new_data[index+1:]
            output_hashes[out.name] = sha(out)
assert len(output_hashes) == 168 and families == 1008
report = {'scope':'Independent review of owner-produced output artifacts; no separate library execution and no native AutoCAD execution.',
          'reader':'ezdxf '+ezdxf.__version__, 'outputs':len(output_hashes), 'families':families,
          'world_comparisons':points, 'maximum_world_error':maximum,'audit_errors':0,'audit_fixes':0,
          'contract':'2e-10 * max(1, expected world magnitude, observed world magnitude)',
          'harness_sha256':sha(Path(__file__)), 'manifest_sha256':sha(manifest_file),'output_sha256':output_hashes}
a.report.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k!='output_sha256'}))
