#!/usr/bin/env python3
"""Pin all outputs from the unmodified IxMilia 0.8.4 behavior probe.

Usage: python tests/fixtures/sunstudy-producer/pin_sources.py ATTEMPTS PACKAGE.nupkg
Failed writes remain separately labelled incomplete bytes, never valid fixtures.
"""
import argparse
import gzip
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tools'))
from verify_sunstudy_producer import (ASSEMBLY_SHA, PACKAGE_SHA, carrier_records, check,
                                     digest, encode_records, packet_graph, records)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('attempts', type=Path)
    parser.add_argument('package', type=Path)
    args = parser.parse_args()
    target = Path(__file__).resolve().parent
    report = json.loads((args.attempts / 'results.json').read_text())
    check(digest(args.package.read_bytes()) == PACKAGE_SHA, 'Unexpected NuGet package')
    check(report['assemblySha256'] == ASSEMBLY_SHA, 'Unexpected producer assembly')
    check(not report['unexpected'] and len(report['attempts']) == 16, 'Unexpected producer outcomes')
    entries = []
    for attempt in report['attempts']:
        names = [attempt['name']]
        if attempt['stage'] == 'complete':
            names.append(attempt['name'].replace('.dxf', '-resaved.dxf'))
        for name in names:
            kind = ('failed-save' if not attempt['saved'] else 'failed-reload' if not attempt['reloaded']
                    else 'resaved' if '-resaved.dxf' in name else 'original')
            folder = {'failed-save': 'failed-saves-gzip', 'failed-reload': 'failed-reloads-gzip',
                      'resaved': 'resaved-gzip', 'original': 'originals-gzip'}[kind]
            raw = (args.attempts / name).read_bytes()
            compressed = gzip.compress(raw, mtime=0)
            relative = folder + '/' + name + '.gz'
            (target / folder).mkdir(exist_ok=True)
            (target / relative).write_bytes(compressed)
            item = {'name': name, 'kind': kind, 'storedFile': relative, 'bytes': len(raw),
                    'sha256': digest(raw), 'gzipSha256': digest(compressed),
                    'hours': '-no-hours' not in name, 'producerSelfReloaded': attempt['reloaded']}
            if kind == 'failed-reload':
                item['malformedPacket'] = next(row for row in records(raw) if row[0] == [0, 'SUNSTUDY'])
            elif kind != 'failed-save':
                item['graph'] = packet_graph(records(raw), item['hours'])
                if kind == 'original':
                    rows, changes = carrier_records(records(raw))
                    carrier = encode_records(rows, raw.startswith(b'AutoCAD Binary DXF'))
                    relative_carrier = 'carriers/' + name
                    (target / 'carriers').mkdir(exist_ok=True)
                    (target / relative_carrier).write_bytes(carrier)
                    item['carrier'] = {'file': relative_carrier, 'sha256': digest(carrier),
                                       'transformations': changes}
            entries.append(item)
    manifest = {'producer': report['producer'], 'packageSha256': PACKAGE_SHA,
                'schemaSourceCommit': '3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567',
                'sourceCommitIsPackageBuildCommit': False, 'transformations': [], 'files': entries}
    (target / 'source-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (target / 'producer-results.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'Pinned {len(entries)} outputs, including explicitly incomplete save failures.')


if __name__ == '__main__':
    main()
