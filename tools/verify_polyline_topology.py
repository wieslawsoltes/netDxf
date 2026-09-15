#!/usr/bin/env python3
"""Check retained 3D polyline topology outputs against native and producer packets."""
from pathlib import Path
import argparse
import copy
import tempfile
import ezdxf
from ezdxf.lldxf.types import DXFTag
import verify_polyline3d_records as records

native = records.native
check = records.check
VERSIONS = records.VERSIONS
NEW_POINT = (11., 12., 13.)
ZERO = (0., 0., 0.)
X = (1., 0., 0.)
Y = (0., 1., 0.)


def simple_output(path, year, binary, kind):
    after, doc = records.output(path, year, binary)
    parents = list(doc.modelspace().query('POLYLINE'))
    check(len(parents) == 1, 'Expected one edited POLYLINE')
    parent = parents[0]
    expected = {'edit': [ZERO, NEW_POINT, ZERO, Y], 'clone': [ZERO, ZERO, Y, NEW_POINT],
                'unicode': [NEW_POINT, ZERO, X, ZERO, Y], 'minimum': [NEW_POINT, ZERO, Y]}[kind]
    check([tuple(v.dxf.location) for v in parent.vertices] == expected, 'Point order or coordinates changed')
    owned = [v.dxf.handle for v in parent.vertices] + [parent.seqend.dxf.handle]
    check(len(set(owned)) == len(owned) and set(owned) == records.children(after).keys(), 'Child identity inventory')
    for index, vertex in enumerate(parent.vertices):
        check(vertex.dxf.owner == parent.dxf.handle and vertex.dxf.flags == 32, 'Ordinary inserted owner or flags')
        layer = 'Łódź层' if kind == 'unicode' and index == 0 else '0'
        check(vertex.dxf.layer == layer, 'Inserted or surviving resource identity')
        check(not vertex.has_xdata('TOPOLOGY_REF') and not vertex.has_extension_dict, 'Unexpected inserted metadata')
    check(parent.seqend.dxf.owner == parent.dxf.handle, 'SEQEND owner')
    return after


def producer_output(path, year, input_binary, binary, sources):
    fixture, before = sources[year, input_binary]
    after, doc = records.output(path, year, binary)
    handles = fixture['handles']
    for handle, packet in before.items():
        if packet[0].value in ('VERTEX', 'SEQEND', 'XRECORD'):
            records.equal(packet, after[handle], 'Original child or owned metadata packet changed')
    old = set(records.children(before))
    new = set(records.children(after)) - old
    check(old <= set(after) and len(new) == 1, 'Exactly one new retained child required')
    inserted = next(iter(new))
    check(int(inserted, 16) < int(doc.header['$HANDSEED'], 16), 'Inserted identity exceeds the allocated handle seed')
    parent = doc.entitydb[handles['polyline']]
    check([v.dxf.handle for v in parent.vertices] == [inserted, *handles['vertices'][1:], handles['vertices'][0]], 'Metadata did not follow moved identity')
    child = doc.entitydb[inserted]
    check(tuple(child.dxf.location) == NEW_POINT and child.dxf.flags == 32, 'Inserted geometry')
    check(child.dxf.owner == parent.dxf.handle and child.dxf.layer == parent.dxf.layer, 'Inserted owner or parent layer')
    check(not child.has_extension_dict and not child.get_reactors() and not child.xdata, 'Inserted child inherited metadata')
    check(parent.seqend.dxf.handle == handles['seqend'], 'SEQEND identity changed')
    return after


def corruption_controls(folder, sources):
    path = folder / 'polyline-topology-producer-AutoCad2018-False-False.dxf'
    content = native.packets(path.read_bytes())
    fixture, before = sources[2018, False]
    handles = fixture['handles']
    after = records.wire(path.read_bytes())
    inserted = next(iter(set(records.children(after)) - set(records.children(before))))
    with tempfile.TemporaryDirectory() as directory:
        for fault in range(8):
            changed = copy.deepcopy(content)
            target = inserted if fault < 4 else handles['vertices'][0] if fault < 7 else handles['seqend']
            packet = next(p for p in changed if records.identity(p) == target)
            code = (5, 330, 10, 70, 8, 41, 330, 330)[fault]
            at = next(i for i, tag in enumerate(packet) if tag.code == code)
            packet[at] = DXFTag(code, ('FFFFFFFE', '0', (91., 92., 93.), 16, '0', 17., '0', '0')[fault])
            corrupted = Path(directory) / f'corrupt-{fault}.dxf'
            corrupted.write_bytes(native.extractor.write(changed, VERSIONS[2018]))
            try:
                producer_output(corrupted, 2018, False, False, sources)
            except (ValueError, AssertionError, ezdxf.DXFError):
                continue
            raise ValueError(f'Actual-output corruption {fault} escaped the topology verifier')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    check(ezdxf.__version__ == '1.4.4', 'Pinned independent reader version')
    sources = records.producer_sources()
    originals = native.source_inputs()
    expected = set()
    def verify(name, function, *parameters):
        expected.add(name)
        function(args.artifacts / name, *parameters)
    for year in VERSIONS:
        for input_binary in (False, True):
            for binary in (False, True):
                verify(f'polyline-topology-edit-AutoCad{year}-{input_binary}-{binary}.dxf', simple_output, year, binary, 'edit')
                verify(f'polyline-topology-producer-AutoCad{year}-{input_binary}-{binary}.dxf', producer_output, year, input_binary, binary, sources)
                verify(f'polyline-topology-native-AutoCad{year}-{input_binary}-{binary}.dxf', records.native_output, year, binary, originals)
            for kind in ('clone', 'unicode'):
                verify(f'polyline-topology-{kind}-AutoCad{year}-{input_binary}.dxf', simple_output, year, input_binary, kind)
    for binary in (False, True):
        verify(f'polyline-topology-minimum-{binary}.dxf', simple_output, 2018, binary, 'minimum')
    check({p.name for p in args.artifacts.glob('polyline-topology-*.dxf')} == expected, 'Exact 98-output topology inventory')
    corruption_controls(args.artifacts, sources)
    print('Polyline topology: 98 outputs; all six profiles and both transports; exact native/producer child packets, metadata, insertion, final-index movement, deletion, clones, Unicode resources and eight actual-output corruption controls; zero independent audit errors or repairs.')


if __name__ == '__main__':
    main()
