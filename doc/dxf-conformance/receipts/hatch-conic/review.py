"""Replay affine conic geometry with independent ezdxf construction primitives."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import numpy as np
import ezdxf
from ezdxf.entities.boundary_paths import ArcEdge, EllipseEdge, LineEdge, PolylinePath
from ezdxf.math import OCS, Vec3, bulge_to_arc

def edges(path):
    if not isinstance(path, PolylinePath):
        return list(path.edges)
    result = []
    vertices = path.vertices
    for i in range(len(vertices) if path.is_closed else len(vertices) - 1):
        a, b = vertices[i], vertices[(i + 1) % len(vertices)]
        if a[2] == 0:
            e = LineEdge(); e.start = a[:2]; e.end = b[:2]
        else:
            c, start, end, radius = bulge_to_arc(a[:2], b[:2], a[2])
            e = ArcEdge(); e.center = c; e.radius = radius
            e.start_angle = math.degrees(start); e.end_angle = math.degrees(end)
            e.ccw = a[2] > 0
        result.append(e)
    return result

def points(edge):
    if isinstance(edge, LineEdge):
        return np.array([(1-t)*np.array(edge.start)+t*np.array(edge.end) for t in np.linspace(0,1,41)])
    assert isinstance(edge, (ArcEdge, EllipseEdge))
    start = edge.start_param if isinstance(edge, EllipseEdge) else math.radians(edge.start_angle)
    end = edge.end_param if isinstance(edge, EllipseEdge) else math.radians(edge.end_angle)
    raw_span = edge.end_angle - edge.start_angle
    sweep = 0 if raw_span == 0 else 2*math.pi if abs(raw_span) == 360 else (end-start) % (2*math.pi)
    parameters = np.linspace(start, start+sweep,41)
    if isinstance(edge, EllipseEdge):
        pts = np.array([tuple(v)[:2] for v in edge.construction_tool().vertices(parameters)])
    else:
        pts = np.array([tuple(v) for v in edge.construction_tool().vertices(np.degrees(parameters))])
    return pts if edge.ccw else pts[::-1]

def matrix(index):
    if index == 0: return np.eye(3)
    if index == 1:
        c,s = math.cos(.4),math.sin(.4); rx=np.array([[1,0,0],[0,c,-s],[0,s,c]])
        c,s = math.cos(-.3),math.sin(-.3); ry=np.array([[c,0,s],[0,1,0],[-s,0,c]])
        return rx @ ry
    return {2:np.diag([2,3,.5]),3:np.array([[1,.75,-.2],[0,1,.5],[.3,0,1]]),4:np.diag([-1,1,1]),5:np.diag([.2,7,1]),6:np.diag([100,.1,2])}[index]

def world(pts,normal,elevation):
    ocs=OCS(Vec3(normal))
    return np.array([tuple(ocs.to_wcs((p[0],p[1],elevation))) for p in pts])

def main():
    p=argparse.ArgumentParser();p.add_argument('fixtures',type=Path);p.add_argument('artifacts',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
    sources={};results=[];segments=probes=0;max_error=0.0
    for file in sorted(a.artifacts.glob('hatch-conic-AutoCad*-*.dxf')):
        version,binary,operation,plane=file.stem.removeprefix('hatch-conic-AutoCad').split('-')
        operation=int(operation);plane=int(plane);key=(version,binary)
        if key not in sources:
            source=a.fixtures/f'ezdxf-hatch-conic-R{version}-{"binary" if binary=="True" else "ascii"}.dxf'
            sources[key]={h.dxf.layer:h for h in ezdxf.readfile(source).modelspace().query('HATCH')}
        doc=ezdxf.readfile(file);hatches=list(doc.modelspace().query('HATCH'));assert len(hatches)==25
        A=matrix(operation);t=np.array([7,-11,13]);normal=(0,0,1) if plane==0 else (1,2,3)
        local_max=0.0
        for h in hatches:
            original=sources[key][h.dxf.layer];se=edges(original.paths[0]);oe=edges(h.paths[0]);assert len(se)==len(oe)
            for before,after in zip(se,oe):
                expected=world(points(before),normal,4)@A.T+t
                actual=world(points(after),h.dxf.extrusion,h.dxf.elevation.z)
                errors=np.linalg.norm(expected-actual,axis=1)
                scale=np.maximum(1,np.maximum(np.linalg.norm(expected,axis=1),np.linalg.norm(actual,axis=1)))
                assert np.all(errors<=2e-10*scale),(file.name,h.dxf.layer,float(errors.max()))
                local_max=max(local_max,float(errors.max()));segments+=1;probes+=len(errors)
        audit=doc.audit();assert not audit.errors and not audit.fixes
        max_error=max(max_error,local_max)
        results.append({'file':file.name,'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'segments':sum(len(edges(h.paths[0])) for h in hatches),'max_world_error':local_max})
    assert len(results)==168
    report={'outputs':len(results),'segments':segments,'world_points':probes,'maximum_world_error':max_error,'audit_errors':0,'audit_repairs':0,'oracle':'ezdxf 1.4.4 construction primitives and OCS; independent matrix composition; ordered endpoints and parameter samples on actual output','harness_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'results':results}
    a.output.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items() if k!='results'}))

if __name__=='__main__':main()
