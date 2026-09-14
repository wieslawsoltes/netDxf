#!/usr/bin/env python3
"""Independently qualify 18 mixed STYLE/DIMSTYLE/plot/SAT/MLEADER drawings."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from verify_mleader_inputs import decode_once, nested_context, json_value
from verify_acis_sat import packet as sat_packet, geometry as sat_geometry, decoded as sat_decoded


PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021',
            2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
COPY_YEARS = (2000, 2010, 2018)
FONT_BITS = struct.unpack('<i', struct.pack('<I', 0xF3123456))[0]
OVERRIDES = {'dimtsz': (142, 1040, 2.75), 'dimtvp': (145, 1040, -.875),
             'dimupt': (288, 1070, 0), 'dimrnd': (45, 1040, .125),
             'dimalt': (170, 1070, 1)}


def check(value, message):
    if not value:
        raise ValueError(message)


def read_wire(path, year):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else (
        ascii_tags_loader(io.StringIO(data.decode('utf-8-sig' if year >= 2007 else 'cp1252'), newline=None)))
    tags = list(tag_compiler(loader))
    all_records, current = [], []
    for tag in tags:
        if tag.code == 0 and current:
            all_records.append(current)
            current = []
        current.append([tag.code, json_value(tag.value)])
    if current:
        all_records.append(current)
    records = {}
    for record in all_records:
        if record[0][1] in ('SECTION', 'ENDSEC', 'EOF', 'CLASS'):
            continue
        prefix = record[:next((i for i, (code, _) in enumerate(record) if code == 100), len(record))]
        identity = [value for code, value in prefix if code in (5, 105)]
        check(len(identity) <= 1, 'Duplicate object identity marker')
        if identity:
            check(identity[0] not in records, 'Duplicate database handle')
            records[identity[0]] = record
    return tags, records


def body(record, name, decode=True):
    start = record.index([100, name]) + 1
    end = next((i for i in range(start, len(record)) if record[i][0] in (100, 1001)), len(record))
    return [[code, decode_once(value) if decode and isinstance(value, str) else value]
            for code, value in record[start:end]]


def scalar(tags, code, expected, label):
    actual = [value for key, value in tags if key == code]
    check(actual == [expected], f'{label}: group{code}={actual!r}, expected {expected!r}')


def data(record, app):
    active, result = False, []
    for code, value in record:
        if code == 1001:
            if active:
                break
            active = value == app
        elif active:
            result.append([code, decode_once(value) if code in (1000, 1003) else value])
    return result


def one(items, label):
    items = list(items)
    check(len(items) == 1, f'{label}: expected one, got {len(items)}')
    return items[0]


def class_declaration(doc, records, name, cpp_name, year, entity=False):
    count = sum(record[0] == [0, name] for record in records.values())
    check(count > 0, name + ': no physical objects')
    declaration = doc.classes.get(name)
    check(declaration.dxf.cpp_class_name == cpp_name and declaration.dxf.is_an_entity == int(entity),
          name + ': CLASS type metadata changed')
    check(declaration.dxf.instance_count == count if year >= 2004 else
          not declaration.dxf.hasattr('instance_count'), name + ': CLASS count/profile changed')


def links(record, role, handles):
    check(data(record, 'THIRD_MIXED') == [[1000, role]] + [[1005, handle] for handle in handles],
          role + ': exact XData payload/reference targets changed')


def normalized(value, roles):
    if isinstance(value, list):
        if len(value) == 2 and isinstance(value[0], int) and (
                320 <= value[0] <= 369 or value[0] == 1005) and isinstance(value[1], str):
            check(value[1] in roles or value[1] == '0', 'Unaccounted reference in mixed snapshot: ' + repr(value))
            return [value[0], roles.get(value[1], '0')]
        return [normalized(item, roles) for item in value]
    if isinstance(value, float):
        return struct.pack('<d', value if value else 0.).hex()
    return value


def plot_settings(record, render, label):
    tags = body(record, 'AcDbPlotSettings')
    for code, expected in ((2, r'Printer\U+0041 青'), (48, -10.), (49, -20.),
                           (140, 200.), (141, 300.), (74, 4), (75, 25), (147, .02)):
        scalar(tags, code, expected, label)
    check([value for code, value in tags if code == 333] == ([render] if render else []),
          label + ': optional shade target changed')
    return tags


def validate(path, year, binary, sources):
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Actual transport differs from filename')
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == PROFILES[year], 'Actual profile differs from filename')
    raw, records = read_wire(path, year)
    model = doc.modelspace()
    entities = list(model)
    expected_types = ['DIMENSION', 'TEXT', 'INSERT', 'WIPEOUT', 'POINT'] + (
        ['MULTILEADER', 'MULTILEADER'] if year >= 2007 else [])
    check([entity.dxftype() for entity in entities] == expected_types, 'Modelspace order/type inventory changed')
    check(all(entity.dxf.owner == model.block_record_handle for entity in entities), 'Modelspace ownership changed')
    font = doc.styles.get('MIXED_FONT')
    dimstyle = doc.dimstyles.get('MIXED_DIM')
    linetype = doc.linetypes.get('MIXED_LINE')
    block = doc.blocks.get('MIXED_BLOCK')
    definition = one(block.query('ATTDEF'), 'Shared ATTDEF')
    page_dictionary = doc.rootdict['ACAD_PLOTSETTINGS']
    check(list(page_dictionary.keys()) == ['MIXED_PAGE'], 'Named page dictionary inventory changed')
    page = page_dictionary['MIXED_PAGE']
    wipe = doc.rootdict['ACAD_WIPEOUT_VARS']
    handles = {'font': font.dxf.handle, 'dimstyle': dimstyle.dxf.handle,
               'linetype': linetype.dxf.handle, 'block': block.block_record_handle,
               'definition': definition.dxf.handle, 'page': page.dxf.handle,
               'wipeout_variables': wipe.dxf.handle}
    if year >= 2007:
        leader_dictionary = doc.rootdict['ACAD_MLEADERSTYLE']
        leader_style = leader_dictionary['MIXED_LEADER']
        render = doc.rootdict['MIXED_RENDER']
        handles.update(leader_style=leader_style.dxf.handle, render=render.dxf.handle)
    else:
        render = None
    check(len(set(handles.values())) == len(handles), 'Distinct resource roles alias unexpectedly')
    font_tags = body(records[font.dxf.handle], 'AcDbTextStyleTableRecord')
    for code, expected in ((2, 'MIXED_FONT'), (3, 'Arial.ttf'), (70, 0x4074), (71, 0x4006), (42, 9.75)):
        scalar(font_tags, code, expected, 'STYLE')
    check(data(records[font.dxf.handle], 'ACAD') == [[1000, 'Mixed Font 青'], [1071, FONT_BITS],
          [1002, '{'], [1000, 'font payload retained'], [1071, 73], [1002, '}']],
          'STYLE full font prefix or unrelated suffix changed')
    check(font.dxf.font == 'Arial.ttf' and font.dxf.flags == 0x4074, 'Independent STYLE interpretation changed')
    family, italic, bold = font.get_extended_font_data()
    check((decode_once(family), italic, bold) == ('Mixed Font 青', True, True), 'Font family/style interpretation changed')
    dim_tags = body(records[dimstyle.dxf.handle], 'AcDbDimStyleTableRecord')
    for code, value in ((142, 1.5), (145, -.625), (288, 1), (340, font.dxf.handle)):
        scalar(dim_tags, code, value, 'DIMSTYLE')
    for name, code, value in (('$DIMTSZ', 40, 0.), ('$DIMTVP', 40, -2.), ('$DIMUPT', 70, 0)):
        matches = [index for index, tag in enumerate(raw) if tag.code == 9 and tag.value == name]
        check(len(matches) == 1, 'Explicit HEADER field missing/duplicated: ' + name)
        following = raw[matches[0] + 1]
        check(following.code == code and following.value == value and doc.header[name] == value,
              'Explicit HEADER was derived from table style: ' + name)
    dimension, text, insert, wipeout, point = entities[:5]
    check(dimension.dxf.dimstyle == 'MIXED_DIM', 'DIMENSION style reference changed')
    check(tuple(dimension.dxf.defpoint2) == (0, 0, 0) and tuple(dimension.dxf.defpoint3) == (8, 0, 0),
          'Dimension measured segment changed')
    check(tuple(dimension.dxf.defpoint) == (8, 2, 0), 'Dimension line offset changed')
    geometry_block = dimension.dxf.get('geometry')
    check(geometry_block and geometry_block in doc.blocks and len(doc.blocks.get(geometry_block)) > 0,
          'DIMENSION has no nonempty geometry block')
    override_data = data(records[dimension.dxf.handle], 'ACAD')
    interpreted = dimension.get_acad_dstyle(dimstyle)
    for name, (key, code, value) in OVERRIDES.items():
        found = [override_data[i + 1] for i, tag in enumerate(override_data[:-1]) if tag == [1070, key]]
        check(found == [[code, value]] and interpreted[name] == value, name + ': override type/value changed')
    check(text.dxf.style == 'MIXED_FONT' and text.dxf.text == 'Shared font' and
          tuple(text.dxf.insert) == (-4, 3, 0) and text.dxf.height == 2, 'TEXT shared STYLE/geometry changed')
    check(definition.dxf.tag == 'LABEL' and definition.dxf.style == 'MIXED_FONT' and
          definition.dxf.text == 'Definition' and tuple(definition.dxf.insert) == (1, 2, 0) and definition.dxf.height == 2,
          'Shared ATTDEF data changed')
    check(definition.dxf.owner == block.block_record_handle, 'ATTDEF block owner changed')
    line = one(block.query('LINE'), 'Shared block LINE')
    check(tuple(line.dxf.start) == (0, 0, 0) and tuple(line.dxf.end) == (5, 0, 0), 'Shared block line changed')
    check(insert.dxf.name == 'MIXED_BLOCK' and tuple(insert.dxf.insert) == (10, 20, 0), 'INSERT target/placement changed')
    attribute = one(insert.attribs, 'INSERT attribute')
    check(attribute.dxf.tag == 'LABEL' and attribute.dxf.style == 'MIXED_FONT' and
          attribute.dxf.text == 'Insert label' and tuple(attribute.dxf.insert) == (11, 22, 0) and
          attribute.dxf.height == 2 and attribute.dxf.owner == insert.dxf.handle, 'INSERT attribute/font changed')
    check(tuple(point.dxf.location) == (123, 456, 789), 'Following POINT framing changed')
    boundary = [tuple(vertex) for vertex in wipeout.boundary_path_wcs()]
    check(set(boundary) == {(0, 0, 0), (4, 0, 0), (4, 3, 0), (0, 3, 0)}, 'Wipeout rectangle changed')
    check(wipe.dxftype() == 'WIPEOUTVARIABLES' and wipe.dxf.owner == doc.rootdict.dxf.handle,
          'Global wipeout object type/owner changed')
    scalar(body(records[wipe.dxf.handle], 'AcDbWipeoutVariables'), 70, 1, 'Wipeout frames')
    check(page.dxftype() == 'PLOTSETTINGS' and page.dxf.owner == page_dictionary.dxf.handle and
          page.get_reactors() == [page_dictionary.dxf.handle], 'Named page owner/reactor changed')
    links(records[page.dxf.handle], 'page resources', [font.dxf.handle, block.block_record_handle])
    sheet = doc.layouts.get('MIXED_SHEET').dxf_layout
    check(sheet.dxf.owner == doc.rootdict['ACAD_LAYOUT'].dxf.handle, 'Embedded layout owner changed')
    plot_tags = plot_settings(records[page.dxf.handle], render.dxf.handle if render else None, 'Named page')
    embedded_tags = plot_settings(records[sheet.dxf.handle], render.dxf.handle if render else None, 'Embedded page')
    # Group1 is the page-setup name: a named object and embedded layout may use
    # different names while sharing every actual print setting.
    check([t for t in plot_tags if t[0] != 1] == [t for t in embedded_tags if t[0] != 1],
          'Named and embedded public plot settings disagree')
    leaders = entities[5:]
    leader_packets = []
    if year >= 2007:
        check(render.dxftype() == 'VISUALSTYLE' and render.dxf.owner == doc.rootdict.dxf.handle,
              'External333 target type/owner changed')
        original = ezdxf.readfile(sources / 'output-settings' / f'independent-output-settings-R{year}.dxf')
        check(original.dxfversion == PROFILES[year], 'Independent VISUALSTYLE source profile changed')
        original_page = original.rootdict['ACAD_PLOTSETTINGS']['Independent-07']
        _, original_records = read_wire(sources / 'output-settings' / f'independent-output-settings-R{year}.dxf', year)
        check(body(records[render.dxf.handle], 'AcDbVisualStyle') ==
              body(original_records[original_page.dxf.shade_plot_handle], 'AcDbVisualStyle'),
              'Independent VISUALSTYLE public payload changed')
        check([value for code, value in records[leader_dictionary.dxf.handle] if code == 3] == ['MIXED_LEADER'],
              'Wire MLEADERSTYLE dictionary inventory changed')
        check(leader_style.dxf.owner == leader_dictionary.dxf.handle and
              leader_style.get_reactors() == [], 'Style owner or absent reactor set changed')
        check(leader_style.dxf.text_style_handle == font.dxf.handle and
              leader_style.dxf.leader_linetype_handle == linetype.dxf.handle and
              leader_style.dxf.block_record_handle == block.block_record_handle and
              leader_style.dxf.char_height == 2.25, 'MLEADERSTYLE shared resources or height changed')
        links(records[leader_style.dxf.handle], 'leader resources', [font.dxf.handle, block.block_record_handle])
        check([leader.dxf.content_type for leader in leaders] == [2, 1], 'Leader text/block alternatives changed')
        for index, leader in enumerate(leaders):
            record = records[leader.dxf.handle]
            packet = body(record, 'AcDbMLeader')
            check(packet[0] == [270, 2] and nested_context(packet, year) == (1, 1), 'MLEADER nested packet inventory changed')
            check(leader.dxf.style_handle == leader_style.dxf.handle and leader.dxf.text_style_handle == font.dxf.handle and
                  leader.dxf.leader_linetype_handle == linetype.dxf.handle and
                  leader.dxf.block_record_handle == block.block_record_handle, 'MLEADER reference graph changed')
            scalar(body(record, 'AcDbEntity'), 430, 'MIXED$青', 'MLEADER common color name')
            links(record, 'text leader' if index == 0 else 'block leader', [])
            context = leader.context
            check(tuple(context.base_point) == (3, 4, 0) and context.char_height == 2.25, 'Leader context placement/height changed')
            branch = one(context.leaders, 'Leader node')
            branch_line = one(branch.lines, 'Leader line')
            check([tuple(vertex) for vertex in branch_line.vertices] == [(-2, -3, 0), (3, 4, 0)] and
                  not branch.breaks and not branch_line.breaks and not leader.arrow_heads, 'Leader vertices/breaks changed')
            if index == 0:
                content = context.mtext
                check(content is not None and context.block is None and not leader.block_attribs, 'Text content discriminant changed')
                check(decode_once(content.default_content) == 'Mixed leader 青' and content.style_handle == font.dxf.handle and
                      tuple(content.insert) == (3, 4, 0) and content.width == 8 and content.defined_height == 3,
                      'MLEADER embedded MTEXT data changed')
            else:
                content = context.block
                check(content is not None and context.mtext is None and content.block_record_handle == block.block_record_handle and
                      tuple(content.insert) == (3, 4, 0) and content._matrix == [], 'MLEADER embedded block data changed')
                override = one(leader.block_attribs, 'MLEADER block attribute')
                check(override.handle == definition.dxf.handle and override.index == 0 and override.width == 1.25 and
                      decode_once(override.text) == 'Leader label', 'MLEADER ATTDEF association/value changed')
            if year >= 2010:
                check(leader.dxf.text_attachment_direction == 0 and context.top_attachment == context.bottom_attachment == 9,
                      'Later text attachment fields changed')
            if year >= 2013:
                check(leader.dxf.leader_extend_to_text == 1, 'R2013 extension flag changed')
            leader_packets.append(packet)
        class_declaration(doc, records, 'MULTILEADER', 'AcDbMLeader', year, True)
        class_declaration(doc, records, 'MLEADERSTYLE', 'AcDbMLeaderStyle', year)
        class_declaration(doc, records, 'VISUALSTYLE', 'AcDbVisualStyle', year)
    check(len(block) == (3 if year <= 2010 else 2), 'Shared block entity inventory changed')
    bodies = list(block.query('BODY'))
    check(len(bodies) == (1 if year <= 2010 else 0), 'SAT capability profile changed')
    sat_snapshot = None
    if bodies:
        source_path = sources / 'acis-sat' / f'independent-acis-body-R{year}-ascii.dxf'
        original_doc = ezdxf.readfile(source_path)
        check(original_doc.dxfversion == PROFILES[year], 'Independent SAT source profile changed')
        original = original_doc.modelspace().query('BODY')[0]
        payload, reference = sat_packet(path), sat_packet(source_path)
        check(payload == reference and sat_decoded(payload['chunks']) == list(original.sat),
              'Exact inert SAT envelope, encoded chunks or decoded lines changed')
        solid = bodies[0]
        check(solid.dxf.owner == block.block_record_handle and solid.dxf.color == 5, 'BODY owner/common color changed')
        for field in ('color_name', 'shadow_mode'):
            check(solid.dxf.hasattr(field) == original.dxf.hasattr(field) and solid.dxf.get(field) == original.dxf.get(field),
                  'BODY optional common field changed: ' + field)
        check(list(solid.get_xdata('QA_ACIS_SAT')) == list(original.get_xdata('QA_ACIS_SAT')), 'SAT trailing XData changed')
        geometry = sat_geometry(solid)
        check(geometry == sat_geometry(original), 'Independent cube topology changed')
        check(geometry[0] == 1 and len(geometry[1]) == 1 and
              {tuple(vertex) for vertex in geometry[1][0]} == {(x, y, z) for x in (-.5, .5) for y in (-.5, .5) for z in (-.5, .5)} and
              len(geometry[2][0]) == 6, 'Known producer cube geometry changed')
        sat_snapshot = payload
    class_declaration(doc, records, 'PLOTSETTINGS', 'AcDbPlotSettings', year)
    class_declaration(doc, records, 'WIPEOUTVARIABLES', 'AcDbWipeoutVariables', year)
    for kind, count in (('PLOTSETTINGS', 1), ('WIPEOUTVARIABLES', 1),
                        ('MULTILEADER', 2 if year >= 2007 else 0),
                        ('MLEADERSTYLE', 1 if year >= 2007 else 0),
                        ('VISUALSTYLE', 1 if year >= 2007 else 0)):
        check(sum(record[0] == [0, kind] for record in records.values()) == count,
              kind + ': unexpected physical record inventory')
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, f'Independent audit: {len(audit.errors)} errors/{len(audit.fixes)} repairs')
    roles = {handle: role for role, handle in handles.items()}
    snapshot = {'font': font_tags, 'font_xdata': data(records[font.dxf.handle], 'ACAD'),
                'plot': normalized(plot_tags, roles), 'leaders': normalized(leader_packets, roles), 'sat': sat_snapshot}
    if year >= 2007:
        snapshot['leader_style'] = normalized(body(records[leader_style.dxf.handle], 'AcDbMLeaderStyle'), roles)
    return handles, snapshot


def check_provenance(sources):
    sat = json.loads((sources / 'acis-sat' / 'manifest.json').read_text())
    for year in (2000, 2004, 2007, 2010):
        name = f'independent-acis-body-R{year}-ascii.dxf'
        fixture = one((item for item in sat['sources'] if item['filename'] == name), 'SAT provenance row')
        check(hashlib.sha256((sources / 'acis-sat' / name).read_bytes()).hexdigest() == fixture['sha256'], 'SAT source digest changed')
    plot = json.loads((sources / 'output-settings' / 'manifest.json').read_text())
    check(plot['producer'] == 'ezdxf 1.4.4', 'Plot source producer changed')
    for year in (2007, 2010, 2013, 2018):
        name = f'independent-output-settings-R{year}.dxf'
        fixture = one((item for item in plot['fixtures'] if item['file'] == name), 'Plot provenance row')
        check(hashlib.sha256((sources / 'output-settings' / name).read_bytes()).hexdigest() == fixture['sha256'], 'Plot source digest changed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--sources', type=Path, default=Path(__file__).resolve().parents[1] / 'tests/fixtures')
    args = parser.parse_args()
    expected = {f'third-mixed-{kind}-AutoCad{year}-{binary}.dxf' for kind, years in
                (('source', PROFILES), ('copy', COPY_YEARS)) for year in years for binary in (False, True)}
    actual = {path.name for path in args.directory.glob('third-mixed-*.dxf')}
    check(actual == expected, f'Exact18 output inventory required; missing={expected-actual}, unexpected={actual-expected}')
    expected_maps = {f'third-mixed-map-AutoCad{year}-{binary}.json' for year in COPY_YEARS for binary in (False, True)}
    check({path.name for path in args.directory.glob('third-mixed-map-*.json')} == expected_maps, 'Exact6 mapping sidecars required')
    check_provenance(args.sources)
    results = {}
    for year in PROFILES:
        for binary in (False, True):
            for kind in (('source', 'copy') if year in COPY_YEARS else ('source',)):
                name = f'third-mixed-{kind}-AutoCad{year}-{binary}.dxf'
                try:
                    results[kind, year, binary] = validate(args.directory / name, year, binary, args.sources)
                except Exception as error:
                    raise ValueError(name + ': ' + str(error)) from error
                print('PASS ' + name)
    for year in COPY_YEARS:
        for binary in (False, True):
            source_handles, source_snapshot = results['source', year, binary]
            target_handles, target_snapshot = results['copy', year, binary]
            maps = json.loads((args.directory / f'third-mixed-map-AutoCad{year}-{binary}.json').read_text())
            check(maps == {'source': source_handles, 'target': target_handles}, 'Map sidecar disagrees with actual located resources')
            check(all(source_handles[role] != target_handles[role] for role in source_handles), 'Copy retained a source resource identity')
            check(source_snapshot == target_snapshot, 'Mapped copy changed normalized resource payloads or nested context')
    for kind, years in (('source', PROFILES), ('copy', COPY_YEARS)):
        for year in years:
            check(results[kind, year, False][1] == results[kind, year, True][1],
                  'Text/binary transport changed normalized mixed payloads')
    print(f'PASS ezdxf {ezdxf.__version__}:18 mixed drawings,6 actual handle-map comparisons; '
          'full font metadata, dimension/header overrides, print settings, SAT cube topology, '
          'nested MLEADER/ATTDEF graphs and zero audit errors/repairs')


if __name__ == '__main__':
    main()
