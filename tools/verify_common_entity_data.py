#!/usr/bin/env python3
"""Verify common graphics packets independently with ezdxf and its low-level tag reader.

The low-level check is essential: ezdxf1.4.4's optimized LINE loader does not
populate proxy_graphic. We check the original AcDbEntity tags and decode the
independently authored proxy polyline separately, without rewriting any DXF.
"""
import argparse
import gzip
import hashlib
import io
import re
from pathlib import Path
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.proxygraphic import ProxyGraphic

YEARS = (2000, 2004, 2007, 2010, 2013, 2018)
PROFILES = dict(zip(YEARS, ('AC1015', 'AC1018', 'AC1021', 'AC1024', 'AC1027', 'AC1032')))
PROXY_HASH = 'b83ec4fae5bc455e0a7ad3b99a991bd55fc9101f3bba162afb6557ee58df22db'
NATIVE_SOURCE = 'sample_AC1024_ascii.dxf'
NATIVE_SOURCE_SHA256 = 'c97e857047ad4cecd84754ea1a8638e47508e339fd489f1e895ee7332127b372'


def check(value, message):
    if not value:
        raise ValueError(message)


def records(path, encoding):
    yield from records_bytes(path.read_bytes(), encoding)


def records_bytes(data, encoding):
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode(encoding), newline=None))
    record = []
    for tag in tags:
        if tag.code == 0 and record:
            yield record
            record = []
        record.append(tag)
    if record:
        yield record


def packet(record):
    common = []
    inside = False
    for tag in record:
        if tag.code == 100:
            if inside:
                break
            inside = tag.value == 'AcDbEntity'
        elif inside:
            common.append(tag)
    return common


def payload(common, year, expected):
    counts = [tag for tag in common if tag.code in (92, 160)]
    chunks = [tag for tag in common if tag.code == 310]
    if expected is None:
        check(not counts and not chunks, 'Absent proxy packet materialized')
        return
    check(len(counts) == 1 and counts[0].code == (92 if year < 2013 else 160), 'Wrong profile byte-count code')
    check(int(counts[0].value) == len(expected), 'Proxy count differs from expected bytes')
    check(all(common.index(counts[0]) < common.index(tag) for tag in chunks), 'Proxy chunks precede byte count')
    parts = [bytes.fromhex(tag.value) if isinstance(tag.value, str) else tag.value for tag in chunks]
    check(all(0 < len(part) <= 127 for part in parts), 'Invalid canonical chunk size')
    check(b''.join(parts) == expected, 'Opaque proxy bytes changed')


def optional(common, year, shadow=0):
    names = [decode_dxf_unicode(tag.value) for tag in common if tag.code == 430]
    shadows = [int(tag.value) for tag in common if tag.code == 284]
    check(names == (['ACME$青'] if year >= 2004 else []), 'Color-name presence/value changed')
    check(shadows == ([shadow] if year >= 2007 else []), 'Shadow-mode presence/value changed')


def native_packets():
    source = Path(__file__).resolve().parents[1] / 'tests' / 'fixtures' / 'table-oracle' / (NATIVE_SOURCE + '.gz')
    data = gzip.decompress(source.read_bytes())
    check(hashlib.sha256(data).hexdigest() == NATIVE_SOURCE_SHA256, 'Pinned native source SHA256 changed')
    expected = {}
    for record in records_bytes(data, 'utf-8'):
        common = packet(record)
        counts = [tag for tag in common if tag.code in (92, 160)]
        if not any(tag.code == 160 for tag in counts):
            continue
        check(len(counts) == 1, 'Native source contains duplicate proxy counts')
        parts = [bytes.fromhex(t.value) if isinstance(t.value, str) else t.value for t in common if t.code == 310]
        proxy = b''.join(parts)
        check(len(proxy) == int(counts[0].value), 'Native source proxy length mismatch')
        handle = next(t.value for t in record if t.code == 5)
        check(handle not in expected, 'Duplicate native source handle')
        expected[handle] = (record[0].value, proxy)
    check(len(expected) == 22 and sum(len(proxy) for _, proxy in expected.values()) == 45416, 'Native source inventory changed')
    return expected


def inspect_native(all_records, doc, year):
    check(year == 2010, 'Native carrier profile changed')
    expected = native_packets()
    carriers = [r for r in all_records if r[0].value == 'LINE']
    check(len(carriers) == len(expected), 'Native carrier inventory changed')
    check(len(doc.modelspace()) == len(expected), 'Unexpected native carrier entity')
    seen = set()
    for record in carriers:
        handle = next(t.value for t in record if t.code == 5)
        line = doc.entitydb[handle]
        mapping = list(line.get_xdata('NATIVE_PROXY_SOURCE'))
        check(len(mapping) == 4 and all(t.code == 1000 for t in mapping), 'Native source mapping shape changed')
        source_file, source_handle, source_type, source_hash = (t.value for t in mapping)
        check(source_file == NATIVE_SOURCE and source_handle in expected and source_handle not in seen, 'Native source mapping missing or duplicated')
        seen.add(source_handle)
        expected_type, proxy = expected[source_handle]
        check(source_type == expected_type, 'Native source entity type changed')
        check(source_hash == hashlib.sha256(proxy).hexdigest(), 'Native source cache SHA256 changed')
        payload(packet(record), year, proxy)
    check(seen == set(expected), 'Native source packets omitted')


def inspect(path):
    profile = re.search(r'AutoCad(20\d\d)-(False|True)', path.name)
    check(profile is not None, 'Missing profile filename')
    year = int(profile[1]); binary = profile[2] == 'True'
    data = path.read_bytes()
    check(data.startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == PROFILES[year], 'Wrong DXF version')
    all_records = list(records(path, 'utf-8' if year >= 2007 else 'cp1252'))
    if '-native160-' in path.name:
        inspect_native(all_records, doc, year)
    elif '-wire-' in path.name:
        size = int(path.stem.rsplit('-', 1)[1])
        if path.stem.endswith('--1'):
            size = -1
        expected = None if size < 0 else bytes((i * 37 + 19) % 256 for i in range(size))
        frames = [r for r in all_records if r[0].value == 'OLEFRAME']
        check(len(frames) == 2, 'Source/clone OLE frames missing')
        for record in frames:
            common = packet(record); optional(common, year); payload(common, year, expected)
            # OLE payload follows the subclass and is independent from proxy310.
            index = next(i for i, t in enumerate(record) if t.code == 100 and t.value == 'AcDbOleFrame')
            native = record[index + 1:]
            check([int(t.value) for t in native if t.code == 90] == [17], 'Native OLE count changed')
            parts = [bytes.fromhex(t.value) if isinstance(t.value, str) else t.value for t in native if t.code == 310]
            check(len(b''.join(parts)) == 17, 'Proxy packet consumed native OLE bytes')
        check(tuple(doc.modelspace().query('LINE')[0].dxf.start) == (10, 20, 30), 'Following entity changed')
    elif '-attributes-' in path.name:
        targets = [r for r in all_records if r[0].value in ('ATTDEF', 'ATTRIB')]
        check(len(targets) == 2, 'Attribute and definition required')
        expected = bytes((i * 37 + 19) % 256 for i in range(129))
        for record in targets:
            common = packet(record); optional(common, year); payload(common, year, expected)
        insert = doc.modelspace().query('INSERT')[0]
        check(insert.attribs[0].dxf.text == 'instance', 'Attribute text changed')
        check(doc.blocks['COMMON_BLOCK'].query('ATTDEF')[0].dxf.text == 'defined', 'Definition text changed')
    else:
        targets = [r for r in all_records if r[0].value in ('LINE', 'ATTDEF', 'ATTRIB')]
        check(len(targets) == 3, 'Independent graphics records missing')
        for record in targets:
            common = packet(record); optional(common, year, shadow=3)
            parts = [bytes.fromhex(t.value) if isinstance(t.value, str) else t.value for t in common if t.code == 310]
            proxy = b''.join(parts)
            check(hashlib.sha256(proxy).hexdigest() == PROXY_HASH, 'Independent proxy SHA256 changed')
            payload(common, year, proxy)
            virtual = list(ProxyGraphic(proxy).virtual_entities())
            check(len(virtual) == 1 and virtual[0].dxftype() == 'POLYLINE', 'Independent proxy command no longer decodes')
            check([tuple(v.dxf.location) for v in virtual[0].vertices] == [(float(i), float(i*i), 0.0) for i in range(6)], 'Decoded proxy geometry changed')
        check(tuple(doc.modelspace().query('POINT')[0].dxf.location) == (123, 456, 789), 'Independent following entity changed')
        line = doc.modelspace().query('LINE')[0]
        check(list(line.get_xdata('COMMON_DATA_QA')) == [(1000, 'after common data'), (1070, 73)], 'Independent XData changed')
    audit = doc.audit()
    check(not audit.errors and not audit.fixes, f'{path}: {len(audit.errors)} errors/{len(audit.fixes)} repairs')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    expected = {f'common-data-wire-AutoCad{y}-{b}-{s}.dxf' for y in YEARS for b in (False, True) for s in (-1, 0, 1, 127, 128, 129, 1025)}
    expected |= {f'common-data-{kind}-AutoCad{y}-{b}.dxf' for kind in ('attributes', 'producer') for y in YEARS for b in (False, True)}
    expected |= {f'common-data-native160-AutoCad2010-{b}.dxf' for b in (False, True)}
    paths = sorted(args.directory.glob('common-data-*.dxf'))
    check({p.name for p in paths} == expected, 'Expected all 110 common entity fixtures across six profiles and both transports')
    for path in paths:
        inspect(path)
    print(f'PASS ezdxf {ezdxf.__version__}: {len(paths)} drawings, 36 independently decoded proxy commands, 44 native cache copies / 90,832 bytes; zero audit errors/repairs')


if __name__ == '__main__':
    main()
