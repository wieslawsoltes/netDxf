#!/usr/bin/env python3
"""Verify native map/resource packets and authored SECTION-to-mesh source links."""
import argparse
import copy
import json
from pathlib import Path
from verify_fourth_mixed_modules import load, metadata, dictionary_edges
from verify_stored_table_content import source, check

ROOT = Path(__file__).resolve().parents[1]
FILES = {'acad_table_simple.dxf': 2013, 'acad_table_with_blk_ref.dxf': 2018}
PHASES = ('input', 'edited', 'released', 'retry')

def field(row, code):
    values = [v for c, v in row if c == code]
    check(len(values) == 1, 'Expected one public group ' + str(code))
    return values[0]

def public(row, marker):
    start = row.index((100, marker)) + 1
    return row[start:next((i for i in range(start, len(row)) if row[i][0] in (100, 1001)), len(row))]

def unique(records, kind, predicate=lambda row: True):
    rows = [(h, row) for h, row in records.items() if row[0] == (0, kind) and predicate(row)]
    check(len(rows) == 1, 'Physical inventory changed: ' + kind)
    return rows[0]

def native_packets(records, original):
    handle, before = unique(original, 'CELLSTYLEMAP')
    check(records.get(handle) == before, 'Complete native CELLSTYLEMAP record changed')
    owner, _, _ = metadata(before)
    check(('ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP', 360, handle) in dictionary_edges(records[owner]), 'Native map owning entry changed')
    style_owner = metadata(original[owner])[0]
    check(metadata(records[owner]) == metadata(original[owner]) and metadata(records[style_owner])[2] == owner, 'Native TABLESTYLE extension ownership changed')
    check(records[style_owner][0] == (0, 'TABLESTYLE'), 'Native map ancestry changed type')
    check(records['14'] == original['14'], 'Complete native LTYPE14 packet changed')
    row = original['11']; marker = row.index((100, 'AcDbTextStyleTableRecord')); start = next(i for i, tag in enumerate(row) if tag[0] == 1001)
    values = row[marker + 1:start]
    check([c for c, _ in values] == [2, 70, 40, 41, 50, 71, 42, 3, 4] and values[-1] == (4, ''), 'Pinned STYLE normalization premise changed')
    expected = row[:marker + 1] + [(code, field(values, code)) for code in (2, 3, 70, 71, 40, 41, 42, 50)] + row[start:]
    check(records['11'] == expected, 'Complete native STYLE11 values/XData or declared normalization changed')
    check((340, '11') in before and (340, '14') in before, 'Native map shared resource references changed')
    return handle

def section_packet(name, settings):
    return [(90, 4), (91, 17), (1, name), (10, (1., 2., 3.)), (40, 5.25), (41, -15.5),
            (70, 70), (62, 9), (63, 256), (411, 'Book$Color'), (92, 2),
            (11, (1., 2., 3.)), (11, (-4., 5., 6.)), (93, 1), (12, (9., 8., 7.)), (360, settings)]

def settings_packet(targets):
    return [(100, 'AcDbSectionSettings'), (90, 1), (91, 1), (1, 'SectionTypeSettings'),
            (90, 1), (91, 17), (92, len(targets))] + [(330, target) for target in targets] + [
            (331, '0'), (1, ''), (93, 0), (3, 'SectionTypeSettingsEnd')]

def mesh_inventory(records):
    handle, mesh = unique(records, 'POLYLINE', lambda row: (8, 'NINTH_GRAPH') in row)
    children = [(h, row) for h, row in records.items() if row[0][1] in ('VERTEX', 'SEQEND') and metadata(row)[0] == handle]
    vertices = [(h, row) for h, row in children if row[0] == (0, 'VERTEX')]
    ends = [(h, row) for h, row in children if row[0] == (0, 'SEQEND')]
    check(len(vertices) == 4 and len(ends) == 1, 'Polygon mesh physical child count changed')
    body = public(mesh, 'AcDbPolygonMesh')
    check(field(body, 71) == 2 and field(body, 72) == 2 and field(body, 75) == 0, 'Polygon mesh declared dimensions/smoothing changed')
    check(field(body, 10) == (0., 0., 0.) and field(body, 210) == (0., 0., 1.), 'Polygon mesh dummy point/normal changed')
    check(field(body, 70) & 16, 'Polygon mesh flag missing')
    for _, vertex in vertices:
        check((100, 'AcDbPolygonMeshVertex') in vertex and field(vertex, 70) == 64, 'Mesh child subclass/flags changed')
    check((1001, 'NINTH_MESH_METADATA') in vertices[0][1] and (1000, 'retained vertex metadata') in vertices[0][1], 'Retained VERTEX XData changed')
    return handle, mesh, vertices, ends[0]

def validate(records, classes, original, before, phase):
    map_handle = native_packets(records, original)
    manager_handle, manager = unique(records, 'SECTION_MANAGER')
    original_manager_handle, original_manager = unique(before, 'SECTION_MANAGER')
    check(manager_handle == original_manager_handle, 'Retained manager identity was rewritten')
    check(manager[:manager.index((100, 'AcDbSectionManager'))] == original_manager[:original_manager.index((100, 'AcDbSectionManager'))], 'Complete manager common identity/metadata changed')
    root = metadata(manager)[0]
    check(('ACAD_SECTION_MANAGER', 350, manager_handle) in dictionary_edges(records[root]) and metadata(records[root])[0] in (None, '0'), 'Manager source root anchor changed')
    initial = phase in ('input', 'retry')
    sections = {field(public(row, 'AcDbSection'), 1): (handle, row) for handle, row in records.items() if row[0] == (0, 'SECTIONOBJECT') and (100, 'AcDbSection') in row}
    original_sections = {field(public(row, 'AcDbSection'), 1): (handle, row) for handle, row in before.items() if row[0] == (0, 'SECTIONOBJECT') and (100, 'AcDbSection') in row}
    check(set(sections) == ({'Ninth first', 'Ninth second'} if initial else {'Ninth second'}), 'SECTION lifecycle inventory changed')
    second = sections['Ninth second'][0]
    targets = [sections['Ninth first'][0], second, sections['Ninth first'][0]] if initial else [second, second]
    check(public(manager, 'AcDbSectionManager') == [(70, 1 if initial else 0), (90, len(targets))] + [(330, target) for target in targets], 'Manager flag/count/ordered target packet changed')
    mesh_handle, before_mesh, before_vertices, before_end = mesh_inventory(before)
    mesh_targets = [map_handle, before_vertices[0][0], '0', before_vertices[0][0], '11', '14']
    expected_sources = [map_handle, '11', '14'] if phase == 'released' else mesh_targets
    for name, (handle, row) in sections.items():
        settings = field(public(row, 'AcDbSection'), 360)
        original_handle, original_section = original_sections[name]
        original_settings = field(public(original_section, 'AcDbSection'), 360)
        check(handle == original_handle and settings == original_settings, 'Retained SECTION or owned settings identity was rewritten')
        check(row[:row.index((100, 'AcDbSection'))] == original_section[:original_section.index((100, 'AcDbSection'))], 'SECTION common identity/metadata changed')
        check(public(row, 'AcDbSection') == section_packet(name, settings), 'Complete SECTION plane/vertices/counts/appearance changed')
        check(settings in records and metadata(records[settings])[0] == handle, 'SECTION/settings reciprocal ownership changed')
        check(records[settings][:records[settings].index((100, 'AcDbSectionSettings'))] == before[settings][:before[settings].index((100, 'AcDbSectionSettings'))], 'SECTIONSETTINGS common identity/metadata changed')
        body = records[settings][records[settings].index((100, 'AcDbSectionSettings')):]
        check(body == settings_packet(expected_sources), 'Explicit generic source pointers/count/null/repetition changed')
        for target in expected_sources:
            check(target == '0' or target in records, 'Actual settings source identity missing')
    _, view = unique(records, 'VIEW', lambda row: (2, 'NINTH_LIVE_VIEW') in row)
    check([v for c, v in public(view, 'AcDbViewTableRecord') if c == 334] == ([sections['Ninth first'][0]] if initial else []), 'VIEW release order/reference presence changed')
    if phase == 'released':
        check(mesh_handle not in records and all(h not in records for h, _ in before_vertices + [before_end]), 'Retired mesh or child identity survived output')
        check(not any(row[0] == (0, 'POLYLINE') and (8, 'NINTH_GRAPH') in row for row in records.values()), 'Released mesh was replaced under a fresh identity')
    else:
        _, mesh, vertices, end = mesh_inventory(records)
        check([h for h, _ in vertices] == [h for h, _ in before_vertices] and end == before_end, 'Retained mesh identity/order/SEQEND packet changed')
        expected_mesh = [(c, v | 1 if c == 70 and phase == 'edited' else v) for c, v in before_mesh]
        check(mesh == expected_mesh, 'Mesh parent changed beyond explicit U closure')
        positions = [(0., 0., 0.), (0., 3., 0.), (4., 0., 2.) if phase == 'edited' else (2., 0., 0.), (2., 3., 0.)]
        for index, ((_, row), (_, previous)) in enumerate(zip(vertices, before_vertices)):
            expected = [(c, positions[index] if c == 10 else v) for c, v in previous]
            check(row == expected and field(row, 10) == positions[index], 'Complete retained mesh child geometry/metadata changed')
    expected_settings = {field(public(row, 'AcDbSection'), 360) for _, row in sections.values()}
    check({handle for handle, row in records.items() if row[0] == (0, 'SECTIONSETTINGS')} == expected_settings, 'SECTIONSETTINGS erasure inventory changed')
    for name, cpp, proxy, entity in [('SECTION_MANAGER', 'AcDbSectionManager', 1024, 0), ('CELLSTYLEMAP', 'AcDbCellStyleMap', 1152, 0), ('SECTIONOBJECT', 'AcDbSection', 1025, 1), ('SECTIONSETTINGS', 'AcDbSectionSettings', 1024, 0)]:
        declarations = [row for row in classes if (1, name) in row]
        count = sum(row[0] == (0, name) for row in records.values())
        check(len(declarations) == 1 and all(tag in declarations[0] for tag in [(2, cpp), (3, 'ObjectDBX Classes'), (90, proxy), (91, count), (280, 0), (281, entity)]), 'Mixed CLASS fields/count changed: ' + name)

def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('artifacts', type=Path); args = parser.parse_args()
    expected = {f'ninth-mixed-{file}-{binary}-{phase}.dxf' for file in FILES for binary in (False, True) for phase in PHASES}
    check({p.name for p in args.artifacts.glob('ninth-mixed-*.dxf')} == expected, 'All16 mixed output drawings are required')
    outputs = controls = 0
    for file, year in FILES.items():
        original, _, actual_year = source(ROOT, file); check(actual_year == year, 'Pinned native profile changed')
        for binary in (False, True):
            _, before, _ = load(args.artifacts / f'ninth-mixed-{file}-{binary}-input.dxf', year, binary)
            for phase in PHASES:
                path = args.artifacts / f'ninth-mixed-{file}-{binary}-{phase}.dxf'
                doc, records, classes = load(path, year, binary); validate(records, classes, original, before, phase)
                audit = doc.audit(); check(not audit.errors and not audit.fixes, 'Mixed output requires independent audit repair')
                defects = ['map-payload', 'style-xdata', 'linetype-payload', 'manager-count', 'manager-flag', 'root', 'section-plane', 'section-count', 'settings-count', 'settings-target', 'class', 'manager-identity', 'section-identity', 'settings-identity']
                if phase != 'released': defects += ['mesh-count', 'mesh-normal', 'vertex-position', 'vertex-owner', 'vertex-metadata', 'seqend-owner']
                else: defects += ['replacement-mesh']
                for defect in defects:
                    bad = copy.deepcopy(records); declarations = copy.deepcopy(classes)
                    def remap(values, old, new):
                        for key, row in list(values.items()):
                            rewritten = [(c, new if v == old and (c in (5, 105, 1005, 480, 481) or 330 <= c <= 369 or 390 <= c <= 399) else v) for c, v in row]
                            if key == old: del values[key]
                            values[new if key == old else key] = rewritten
                    if defect.endswith('-identity'):
                        if defect == 'manager-identity': old = unique(bad, 'SECTION_MANAGER')[0]
                        else:
                            old, row = unique(bad, 'SECTIONOBJECT', lambda row: (1, 'Ninth second') in row)
                            if defect == 'settings-identity': old = field(public(row, 'AcDbSection'), 360)
                        remap(bad, old, '7FFFFFFFFFFFFE00')
                    elif defect == 'replacement-mesh':
                        handle, mesh, vertices, end = mesh_inventory(before)
                        added = {h: copy.deepcopy(row) for h, row in [(handle, mesh)] + vertices + [end]}
                        for index, old in enumerate(list(added)): remap(added, old, format(0x7FFFFFFFFFFFFE00 + index, 'X'))
                        bad.update(added)
                    elif defect == 'map-payload': row = unique(bad, 'CELLSTYLEMAP')[1]; row[row.index((100, 'AcDbCellStyleMap')) + 1] = (90, 999)
                    elif defect == 'style-xdata': row = bad['11']; row[row.index((1000, 'Arial'))] = (1000, 'Wrong font')
                    elif defect == 'linetype-payload': bad['14'].append((3, 'Wrong line'))
                    elif defect in ('manager-count', 'manager-flag'):
                        row = unique(bad, 'SECTION_MANAGER')[1]; start = row.index((100, 'AcDbSectionManager')); index = start + (2 if defect == 'manager-count' else 1); code, value = row[index]; row[index] = (code, 999)
                    elif defect == 'root':
                        row = unique(bad, 'SECTION_MANAGER')[1]; root = metadata(row)[0]; bad[root] = [(c, 'BAD_ROOT' if c == 3 and v == 'ACAD_SECTION_MANAGER' else v) for c, v in bad[root]]
                    elif defect.startswith('section-'):
                        row = unique(bad, 'SECTIONOBJECT', lambda row: (1, 'Ninth second') in row)[1]; code = 10 if defect == 'section-plane' else 92; index = next(i for i, tag in enumerate(row) if tag[0] == code); row[index] = (code, (0., 0., 1.) if code == 10 else 99)
                    elif defect.startswith('settings-'):
                        section = unique(bad, 'SECTIONOBJECT', lambda row: (1, 'Ninth second') in row)[1]; row = bad[field(public(section, 'AcDbSection'), 360)]; index = row.index((100, 'AcDbSectionSettings')) + (6 if defect == 'settings-count' else 7); code, value = row[index]; row[index] = (code, 999 if code == 92 else '0')
                    elif defect == 'class': declarations = []
                    else:
                        handle, mesh, vertices, end = mesh_inventory(bad)
                        if defect in ('mesh-count', 'mesh-normal'): row = mesh; code = 71 if defect == 'mesh-count' else 210; value = 99 if code == 71 else (1., 0., 0.)
                        elif defect == 'seqend-owner': row = end[1]; code = 330; value = '0'
                        else: row = vertices[0][1]; code = {'vertex-position': 10, 'vertex-owner': 330, 'vertex-metadata': 1000}[defect]; value = (9., 9., 9.) if code == 10 else '0' if code == 330 else 'Wrong metadata'
                        index = next(i for i, tag in enumerate(row) if tag[0] == code); row[index] = (code, value)
                    try: validate(bad, declarations, original, before, phase)
                    except (ValueError, KeyError, StopIteration): controls += 1
                    else: raise ValueError('Actual mixed corruption escaped validator: ' + defect)
                outputs += 1
    check(outputs == 16 and controls == 300, 'Mixed output/control inventory changed')
    print(json.dumps({'outputs': outputs, 'actual_output_corruptions_rejected': controls,
        'native_map_packet_comparisons': outputs, 'native_linetype_packet_comparisons': outputs,
        'native_style_comparisons_with_declared_normalization': outputs, 'audit_errors': 0, 'audit_repairs': 0,
        'section_source_links': 'explicitly authored generic pointer storage', 'section_evaluation': False, 'native_cad_execution': False}, sort_keys=True))

if __name__ == '__main__': main()
