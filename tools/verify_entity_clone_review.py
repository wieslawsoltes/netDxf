#!/usr/bin/env python3
"""Check clone state on the physical DXF wire with independent ezdxf decoding.

Two roots per drawing are compared with only their owned identities renamed.
All other ordered root/child/annotation tags must agree. Author-supplied constants
also prevent a matching pair of incorrect clone exports from passing. The test
qualifies storage and clone fidelity, not SHX rendering or the spline fitter.
"""
import argparse
import copy
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records
from verify_legacy_utilities import PROFILES
from verify_mleader_inputs import check

KINDS = ('solid', 'trace', 'shape', 'quadratic', 'cubic', 'leader', 'annotated-leader')
TYPES = dict(zip(KINDS, ('SOLID', 'TRACE', 'SHAPE', 'POLYLINE', 'POLYLINE', 'LEADER', 'LEADER')))
POINTS = [[1., 2., 3.], [4., 6., -2.], [8., 5., 4.], [11., -3., 7.]]
CORNERS = [[1., 2., -7.25], [4., 3., -7.25], [2., 7., -7.25], [6., 8., -7.25]]


def one(tags, code):
    values = [v for c, v in tags if c == code]
    check(len(values) == 1, f'Expected exactly one group {code}')
    return values[0]


def owned_graph(items, root):
    packet = items[root]
    check(one(packet, 5) == root, 'Physical root identity mismatch')
    graph = [(root, packet)]
    kind = packet[0][1]
    if kind == 'POLYLINE':
        graph += [(h, t) for h, t in items.items() if t[0][1] in ('VERTEX', 'SEQEND') and [330, root] in t]
        check(len(graph) == 30, 'Expected four control vertices, 24 fit vertices and SEQEND')
        check(graph[-1][1][0] == [0, 'SEQEND'], 'Missing/reordered SEQEND')
        for _, tags in graph[1:5]:
            check(one(tags, 70) == 48, 'Spline control vertex flags')
        for _, tags in graph[5:-1]:
            check(one(tags, 70) == 40, 'Spline fit vertex flags')
    if kind == 'LEADER':
        annotations = [v for c, v in packet if c == 340]
        check(len(annotations) <= 1, 'Duplicate leader annotation')
        for handle in annotations:
            check(handle in items and items[handle][0] == [0, 'MTEXT'], 'Dangling/wrong-kind annotation')
            check([330, root] in items[handle], 'Annotation lacks leader reactor')
            graph.append((handle, items[handle]))
    return graph


def normalized(graph):
    mapping = {handle: f'<identity-{i}>' for i, (handle, _) in enumerate(graph)}
    return [[[c, mapping.get(v, v) if isinstance(v, str) and (c in (5, 105, 1005) or 330 <= c <= 369) else v]
             for c, v in tags] for _, tags in graph]


def verify(items, kind):
    roots = [h for h, t in items.items() if t[0] == [0, TYPES[kind]]]
    check(len(roots) == 2, 'Expected exactly one source and one clone')
    graphs = [owned_graph(items, root) for root in roots]
    check(normalized(graphs[0]) == normalized(graphs[1]), 'Clone changed an ordered entity, child or annotation packet')
    for graph in graphs:
        tags = graph[0][1]
        check(one(tags, 8) == 'CloneReviewLayer' and one(tags, 62) == 5 and one(tags, 60) == 1, 'Common clone state')
        check(one(tags, 48) == 2.25 and one(tags, 370) == 25, 'Common line appearance')
        check([1001, 'CLONE_REVIEW'] in tags and [1000, 'independent clone'] in tags, 'Clone XData')
        if kind in ('solid', 'trace'):
            check([one(tags, c) for c in (10, 11, 12, 13)] == CORNERS, 'Lost planar vertices/elevation')
            check(one(tags, 39) == -2.5 and one(tags, 210) == [0., 0., 1.], 'Planar thickness/normal')
        elif kind == 'shape':
            for code, value in {2: 'TRACK1', 10: [1., 2., 7.25], 40: 3.5, 41: -2.75, 50: 27., 51: 15., 39: -2.5}.items():
                check(one(tags, code) == value, f'Shape group {code}')
        elif kind in ('quadratic', 'cubic'):
            check(one(tags, 70) == 12 and one(tags, 75) == (5 if kind == 'quadratic' else 6), 'Polyline smoothing flags/type')
            check([one(t, 10) for _, t in graph[1:5]] == POINTS, 'Polyline control geometry')
        else:
            check(one(tags, 211) == [-0.6000000000000001, .8, 0.], 'Leader direction')
            check(one(tags, 213) == [.5, -.75, -7.25], 'Leader offset/elevation')
            check(one(tags, 77) == 138 and one(tags, 71) == 0 and one(tags, 72) == 1, 'Leader appearance')
            annotated = kind == 'annotated-leader'
            check(one(tags, 75) == int(annotated) and one(tags, 73) == (0 if annotated else 3), 'Leader annotation state')
            expected = [[1., 2., -7.25], [4., 3., -7.25]]
            if annotated: expected.append([6.108, 7.856, -7.25])
            expected.append([6., 8., -7.25])
            check([v for c, v in tags if c == 10] == expected and one(tags, 76) == len(expected), 'Leader vertices')
            if annotated:
                check(one(graph[1][1], 1) == 'clone annotation', 'Leader annotation text')
    return graphs


def corruption_controls(items, kind):
    graphs = verify(items, kind)
    count = 0
    # Challenge every actual ordered packet value, not only the fields repaired.
    for handle, tags in graphs[1]:
        for index, (code, value) in enumerate(tags):
            if code == 5: continue  # identity is a declared rename, not copied state
            bad = copy.deepcopy(tags)
            if isinstance(value, list): changed = [v + 1 for v in value]
            elif isinstance(value, str): changed = value + '_CORRUPT'
            else: changed = value + 1
            bad[index] = [code, changed]
            candidate = dict(items); candidate[handle] = bad
            try: verify(candidate, kind)
            except (ValueError, KeyError): count += 1
            else: raise AssertionError(f'Accepted corrupted {kind} packet group {code}')
        candidate = dict(items); del candidate[handle]
        try: verify(candidate, kind)
        except (ValueError, KeyError): count += 1
        else: raise AssertionError('Accepted missing clone/owned record')
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    wanted = {f'clone-review-{k}-{v}-{b}-{c}.dxf' for k in KINDS for v in PROFILES for b in (False, True) for c in (0, 1)}
    check({p.name for p in directory.glob('clone-review-*.dxf')} == wanted, 'Exact clone output inventory required')
    controls = 0
    for kind in KINDS:
        for version, profile in PROFILES.items():
            for binary in (False, True):
                for cycle in (0, 1):
                    path = directory / f'clone-review-{kind}-{version}-{binary}-{cycle}.dxf'
                    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == (binary if cycle == 0 else not binary), 'Clone transport')
                    doc = ezdxf.readfile(path); check(doc.dxfversion == profile, 'Clone profile')
                    controls += corruption_controls(records(path), kind)
                    audit = doc.audit(); check(not audit.errors and not audit.fixes, 'Clone drawing requires audit repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {len(wanted)} drawings; {controls} actual-packet corruptions rejected; zero audit errors/repairs')


if __name__ == '__main__':
    main()
