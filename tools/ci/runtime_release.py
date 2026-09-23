#!/usr/bin/env python3
"""Retain and replay the complete package/runtime evidence in release assets."""
from __future__ import annotations
import argparse
import hashlib
import io
import json
from pathlib import Path
import stat
import tempfile
import zipfile
import pipeline
import runtime_assets

ARCHIVE_NAME = 'runtime-evidence.zip'
SUMMARY_NAME = 'runtime-qualification.json'
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
MAX_LOG_BYTES = 16 * 1024 * 1024


def profile_name(key: tuple[str, str, str]) -> str:
    return '-'.join(key)


def inventory() -> dict[str, int]:
    entries = {SUMMARY_NAME: 1024 * 1024}
    for key in runtime_assets.MATRIX:
        for name, limit in (('result.json', 65536), ('receipt.xml', 16384), ('run.log', MAX_LOG_BYTES)):
            entries[profile_name(key) + '/' + name] = limit
    return entries


def verify(packages: Path, archive_path: Path) -> dict:
    pipeline.require(archive_path.is_file() and not archive_path.is_symlink() and
                     archive_path.stat().st_size <= MAX_ARCHIVE_BYTES,
                     'Missing, linked or oversized runtime evidence archive')
    payload = archive_path.read_bytes()
    pipeline.require(len(payload) <= MAX_ARCHIVE_BYTES, 'Runtime archive size changed')
    wanted = inventory()
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        members = archive.infolist()
        pipeline.require(len(members) == len(wanted) and set(archive.namelist()) == set(wanted),
                         'Missing, extra, duplicate or unsafe runtime archive entry')
        pipeline.require(sum(info.file_size for info in members) <= MAX_TOTAL_BYTES,
                         'Runtime evidence exceeds decompressed size budget')
        for info in members:
            kind = stat.S_IFMT(info.external_attr >> 16)
            pipeline.require(not info.is_dir() and kind in (0, stat.S_IFREG) and not info.flag_bits & 1
                             and info.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
                             and 0 < info.file_size <= wanted[info.filename], 'Invalid runtime evidence member')
        # Only fixed, validated names are materialized, never caller-supplied paths.
        # No archive permissions, links or executable files are restored.
        with tempfile.TemporaryDirectory(prefix='netdxf-runtime-verify-') as temporary:
            root = Path(temporary)
            for info in members:
                path = root / info.filename
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(archive.read(info))
            stored = json.loads((root / SUMMARY_NAME).read_text(encoding='utf-8'))
            observed = runtime_assets.collect(packages, root, root / 'replayed-summary.json')
            pipeline.require(stored == observed, 'Runtime summary differs from replayed receipts and process logs')
    return {'sha256': hashlib.sha256(payload).hexdigest(), 'profiles': observed['profiles'],
            'scenario_executions': observed['scenario_executions']}


def capture(packages: Path, evidence: Path, output: Path) -> dict:
    pipeline.require(not output.exists() and not output.is_symlink(), 'Runtime evidence output already exists')
    with tempfile.TemporaryDirectory(prefix='netdxf-runtime-capture-') as temporary:
        root = Path(temporary)
        summary = root / SUMMARY_NAME
        runtime_assets.collect(packages, evidence, summary)
        paths = {SUMMARY_NAME: summary}
        for result in sorted(evidence.rglob('result.json')):
            data = json.loads(result.read_text(encoding='utf-8'))['observed']
            key = (data['host'], data['runtime'], data['asset'])
            for filename in ('result.json', 'receipt.xml', 'run.log'):
                name = profile_name(key) + '/' + filename
                pipeline.require(name not in paths, 'Duplicate runtime source profile')
                paths[name] = result.with_name(filename)
        limits = inventory()
        pipeline.require(set(paths) == set(limits), 'Incomplete runtime capture inventory')
        candidate = root / ARCHIVE_NAME
        with zipfile.ZipFile(candidate, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
            for name in sorted(paths):
                path = paths[name]
                pipeline.require(path.is_file() and not path.is_symlink() and path.stat().st_size <= limits[name],
                                 'Invalid or oversized runtime capture input')
                data = path.read_bytes()
                pipeline.require(0 < len(data) <= limits[name], 'Runtime source size changed during capture')
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, data)
        # Verify the captured bytes, not merely the files before they were read.
        result = verify(packages, candidate)
        output.parent.mkdir(parents=True, exist_ok=True)
        with output.open('xb') as stream:
            stream.write(candidate.read_bytes())
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('capture', 'verify'))
    parser.add_argument('--packages', type=Path, default=pipeline.ROOT / 'artifacts/packages')
    parser.add_argument('--evidence', type=Path, default=pipeline.ROOT / 'artifacts/runtime-reports')
    parser.add_argument('--archive', type=Path, default=pipeline.ROOT / 'artifacts/runtime-qualified' / ARCHIVE_NAME)
    args = parser.parse_args()
    result = capture(args.packages, args.evidence, args.archive) if args.command == 'capture' else verify(args.packages, args.archive)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
