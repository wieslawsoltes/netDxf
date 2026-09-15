"""Independent periodic review using homogeneous de Boor evaluation of actual packets.

No project verifier or captured expected sample implementation is imported.
The source DXF files are pinned by their manifest hashes; all numeric expectations
come from the actual source packet, including its original parameter domain.
"""
import argparse
import bisect
import hashlib
import itertools
import json
import math
from pathlib import Path
import ezdxf

p = argparse.ArgumentParser()
p.add_argument('repository', type=Path)
p.add_argument('artifacts', type=Path)
p.add_argument('output', type=Path)
a = p.parse_args()
fixtures = a.repository / 'tests/fixtures/hatch-periodic-conversion'
manifest = json.loads((fixtures/'manifest.json').read_text())
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
max_error = 0.0
comparisons = 0
def near(actual, expected):
    global max_error, comparisons
    av = tuple(actual); ev = tuple(expected)
    assert len(av) == len(ev)
    err = math.dist(av, ev); scale = max(1.0, math.hypot(*ev))
    assert err <= 2e-10 * scale, (av, ev, err)
    max_error = max(max_error, err); comparisons += 1
def cross(x, y): return (x[1]*y[2]-x[2]*y[1], x[2]*y[0]-x[0]*y[2], x[0]*y[1]-x[1]*y[0])
def unit(v):
    mag = math.hypot(*v)
    return tuple(x/mag for x in v)
def world(point, plane):
    normal = (0.0,0.0,1.0) if plane == 0 else unit((1.0,2.0,3.0))
    axis = (0.0,1.0,0.0) if abs(normal[0]) < 1/64 and abs(normal[1]) < 1/64 else (0.0,0.0,1.0)
    xaxis = unit(cross(axis,normal)); yaxis = cross(normal,xaxis)
    return tuple(point[0]*xaxis[k]+point[1]*yaxis[k]+4*normal[k] for k in range(3))
def evaluate(controls, weights, knots, degree, parameter):
    span = bisect.bisect_right(knots, parameter)-1
    assert degree <= span < len(controls)
    d = [[*(float(c)*weights[j] for c in controls[j]),weights[j]] for j in range(span-degree,span+1)]
    for level in range(1, degree+1):
        for j in range(degree,level-1,-1):
            index = span-degree+j
            fraction = (parameter-knots[index])/(knots[index+degree-level+1]-knots[index])
            d[j] = [(1-fraction)*x+fraction*y for x,y in zip(d[j-1],d[j])]
    assert d[degree][-1] > 0
    return tuple(c/d[degree][-1] for c in d[degree][:-1])
def indexed(drawing, kind):
    entities = list(drawing.modelspace().query(kind))
    result = {item.dxf.layer:item for item in entities}
    assert len(result) == len(entities) == 12
    return result
inventory = {}; source_packets = {}; audits = 0; curves = 0; samples = 0
for entry in manifest['files']:
    source = fixtures/entry['file']; assert sha(source) == entry['sha256']
    doc = ezdxf.readfile(source)
    source_packets[(entry['year'],entry['binary'])] = indexed(doc,'HATCH')
for year, binary, compact, plane, link in itertools.product((2000,2004,2007,2010,2013,2018),(False,True),(False,True),(0,1),(False,True)):
    stem = f'hatch-periodic-AutoCad{year}-{binary}-{compact}-{plane}-{link}'
    packet_path = a.artifacts/(stem+'.dxf'); sample_path = a.artifacts/(stem+'.json')
    drawing = ezdxf.readfile(packet_path); hatches = indexed(drawing,'HATCH'); splines = indexed(drawing,'SPLINE')
    sample_values = json.loads(sample_path.read_text())
    original = source_packets[(year,binary)]
    assert hatches.keys() == splines.keys() == sample_values.keys() == original.keys()
    for name, source_hatch in original.items():
        source_edge = source_hatch.paths[0].edges[0]
        controls = [tuple(v)+(0.0,) for v in source_edge.control_points]
        degree = source_edge.degree; knots = list(source_edge.knot_values)
        weights = list(source_edge.weights) or [1.0]*len(controls)
        hatch = hatches[name]; stored = hatch.paths[0].edges[0]; spline = splines[name]
        assert stored.degree == spline.dxf.degree == degree
        assert stored.periodic == 1 and spline.dxf.flags & 2
        assert stored.rational == source_edge.rational
        assert list(stored.knot_values) == list(spline.knots) == knots
        assert list(stored.weights) == (list(source_edge.weights)[degree:] if compact else list(source_edge.weights))
        expected_controls = controls[degree:] if compact else controls
        assert len(stored.control_points) == len(expected_controls)
        for actual, expected in zip(stored.control_points,expected_controls): near(actual,expected[:2])
        assert list(spline.weights) == weights
        assert len(spline.control_points) == len(controls)
        for actual, expected in zip(spline.control_points,controls): near(actual,world(expected,plane))
        near(hatch.dxf.elevation,(0,0,4)); near(hatch.dxf.extrusion,(0,0,1) if plane==0 else unit((1,2,3)))
        assert bool(hatch.dxf.associative) == link
        assert hatch.paths[0].source_boundary_objects == ([spline.dxf.handle] if link else [])
        assert len(sample_values[name]) == 64
        lo,hi = knots[degree],knots[len(controls)]
        for i, actual in enumerate(sample_values[name]):
            parameter = lo+(hi-lo)*i/64
            expected = world(evaluate(controls,weights,knots,degree,parameter),plane)
            near(actual,expected)
            near(evaluate(list(spline.control_points),list(spline.weights),list(spline.knots),degree,parameter),expected)
            samples += 1
        curves += 1
    valid_path = a.artifacts/(stem+'-curves.dxf') if compact else packet_path
    valid_drawing = ezdxf.readfile(valid_path)
    if compact:
        assert not list(valid_drawing.modelspace().query('HATCH'))
        canonical = indexed(valid_drawing,'SPLINE')
        for name, spline in canonical.items():
            near(spline.control_points[0],splines[name].control_points[0])
            assert list(spline.knots) == list(splines[name].knots)
            assert list(spline.weights) == list(splines[name].weights)
            for actual, expected in zip(spline.control_points,splines[name].control_points): near(actual,expected)
    audit = valid_drawing.audit(); assert not audit.errors and not audit.fixes, (valid_path,len(audit.errors),len(audit.fixes))
    audits += 1
    for path in {packet_path,sample_path,valid_path}: inventory[path.name] = sha(path)
result = {'review':'Independent homogeneous de Boor evaluation of actual source and output packets',
          'case_drawings':96,'curves':curves,'netDxf_samples':samples,'point_comparisons':comparisons,
          'maximum_absolute_error':max_error,'tolerance':'2e-10 * max(1, expected point magnitude)',
          'audited_canonical_drawings':audits,'audit_errors':0,'audit_repairs':0,
          'legacy_compact_hatch_scope':'48 preserved compact HATCH drawings (576 periodic packets); only their separately emitted canonical curve drawings audited',
          'harness_sha256':sha(Path(__file__)),'producer_manifest_sha256':sha(fixtures/'manifest.json'),
          'output_sha256':inventory}
a.output.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({k:v for k,v in result.items() if k!='output_sha256'}))
