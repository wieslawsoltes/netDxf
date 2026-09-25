#!/usr/bin/env python3
"""Source-only documentation/workflow guard; this is not CAD conformance evidence."""
from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = {'.github/workflows/ci-build.yml', '.github/workflows/release.yml'}
FRONT_DOORS = {'README.md': 32_768, 'doc/dxf-conformance/README.md': 24_576}
REPORT = 'doc/dxf-conformance/remaining-major-gaps.md'
RETIRED = {
    'doc/dxf-conformance/checkpoint-2026-09-13.md',
    'doc/dxf-conformance/checkpoint-hatch-2026-09-13.md',
}
# PR #58's checkpoint is NOT retired: the pinned ledger and HATCH audit need it.
REQUIRED_CHECKPOINT = 'doc/dxf-conformance/checkpoint-2026-09-14.md'
# Maintained entry points use ordinary inline links. Reference-style definitions
# are also inspected when looking for live retired-file references.
INLINE = re.compile(r'!?\[[^\]\n]*\]\(([^)\n]+)\)')
DEFINITION = re.compile(r'^\s{0,3}\[[^\]\n]+\]:\s*(\S+)', re.MULTILINE)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def reject(action) -> int:
    try:
        action()
    except ValueError:
        return 1
    raise ValueError('Hygiene negative control was accepted')


def destinations(text: str):
    for match in INLINE.finditer(text):
        value = match[1].strip()
        if value.startswith('<') and '>' in value:
            yield value[1:value.index('>')]
        else:
            yield value.split(None, 1)[0]
    for match in DEFINITION.finditer(text):
        yield match[1].strip('<>')


def local_target(root: Path, document: str, destination: str) -> str | None:
    url = urlsplit(destination)
    if url.scheme or url.netloc or not url.path:
        return None
    decoded = unquote(url.path)
    require('\\' not in decoded, f'Nonportable link in {document}: {destination}')
    target = (root / document).parent.joinpath(decoded).resolve()
    try:
        return target.relative_to(root.resolve()).as_posix()
    except ValueError as error:
        raise ValueError(f'Link escapes repository in {document}: {destination}') from error


def workflow_inventory(paths: set[str]) -> None:
    require(paths == WORKFLOWS, f'Expected only the two core workflows; got {sorted(paths)}')


def size_limit(content: bytes, limit: int, label: str) -> None:
    require(0 < len(content) <= limit, f'{label} is empty or exceeds its {limit}-byte entry-point budget')


def check_links(root: Path, document: str, text: str) -> int:
    count = 0
    for destination in destinations(text):
        target = local_target(root, document, destination)
        if target is None:
            continue
        require((root / target).exists(), f'Missing inline link target in {document}: {destination}')
        require(target not in RETIRED, f'Live retired-checkpoint link in {document}: {destination}')
        count += 1
    return count


def strings(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, list):
        for item in value:
            yield from strings(item)
    elif isinstance(value, dict):
        for key, item in value.items():
            yield from strings(key)
            yield from strings(item)


def check_retired_references(root: Path, paths: list[str]) -> None:
    names = {Path(path).name for path in RETIRED}
    problems = []
    for path in paths:
        if not path.lower().endswith('.md'):
            continue
        text = (root / path).read_text(encoding='utf-8-sig')
        for destination in destinations(text):
            url = urlsplit(destination)
            if url.scheme or url.netloc or Path(unquote(url.path)).name not in names:
                continue
            if local_target(root, path, destination) in RETIRED:
                problems.append(f'{path}: {destination}')
    # Historical evidence files are deliberately not rewritten. A document needed
    # by the active source-pinned ledger must stay, not lose its evidence link.
    ledger = json.loads((root / 'doc/dxf-conformance/coverage.json').read_text(encoding='utf-8-sig'))
    for value in strings(ledger):
        url = urlsplit(value)
        if not url.scheme and not url.netloc and Path(unquote(url.path)).name in names:
            problems.append('coverage.json: ' + value)
    require(not problems, 'Retired files still have live references; migrate links to immutable history or retain the file:\n' + '\n'.join(problems))
    require(REQUIRED_CHECKPOINT in paths and (root / REQUIRED_CHECKPOINT).is_file(),
            'Source-pinned ledger/HATCH audit checkpoint must remain available')


def self_test() -> int:
    workflow_inventory(set(WORKFLOWS))
    count = reject(lambda: workflow_inventory(WORKFLOWS - {next(iter(WORKFLOWS))}))
    count += reject(lambda: workflow_inventory(WORKFLOWS | {'.github/workflows/temporary-publish.yml'}))
    size_limit(b'x', 1, 'control')
    count += reject(lambda: size_limit(b'', 1, 'control'))
    count += reject(lambda: size_limit(b'xx', 1, 'control'))
    with tempfile.TemporaryDirectory(prefix='netdxf-hygiene-') as directory:
        root = Path(directory)
        (root / 'README.md').write_text('control', encoding='utf-8')
        require(check_links(root, 'index.md', '[ok](README.md#anchor)') == 1, 'Existing link control')
        require(check_links(root, 'index.md', '[external](https://example.invalid/missing.md) [anchor](#section)') == 0, 'External/anchor control')
        count += reject(lambda: check_links(root, 'index.md', '[missing](absent.md)'))
        count += reject(lambda: check_links(root, 'index.md', '[escape](../outside.md)'))
        count += reject(lambda: check_links(root, 'index.md', '[escape](%2e%2e/outside.md)'))
        require(local_target(root, 'doc/dxf-conformance/README.md', 'checkpoint-2026-09-13.md') in RETIRED,
                'Relative retired-target resolution')
        require(local_target(root, 'doc/dxf-conformance/README.md', 'checkpoint-2026-09-14.md') not in RETIRED,
                'Referenced evidence is not redundant narrative')
        require(local_target(root, 'README.md', 'https://github.com/example/repo/blob/pinned/checkpoint-2026-09-13.md') is None,
                'Immutable archive URL must remain external')
    return count


def main() -> None:
    count = self_test()
    raw = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT)
    paths = [path for path in raw.decode('utf-8').split('\0') if path]
    require(paths and len(paths) == len(set(paths)), 'Missing or duplicate tracked-file inventory')
    workflow_inventory({path for path in paths if path.startswith('.github/workflows/') and path.lower().endswith(('.yml', '.yaml'))})
    require(not RETIRED.intersection(paths), 'Retired narrative checkpoints are still tracked')
    links = 0
    for path, budget in FRONT_DOORS.items():
        content = (ROOT / path).read_bytes()
        size_limit(content, budget, path)
        links += check_links(ROOT, path, content.decode('utf-8-sig'))
    report = (ROOT / REPORT).read_text(encoding='utf-8-sig')
    require('Full AutoCAD parity' in report and 'not established' in report, 'Missing explicit report qualification boundary')
    links += check_links(ROOT, REPORT, report)
    check_retired_references(ROOT, paths)
    print(f'PASS: two core workflow paths, three maintained documentation entry points, '
          f'{links} existing local links, no live retired-checkpoint references; {count} negative controls rejected. '
          'Source hygiene only, not additional CAD conformance or native acceptance.')


if __name__ == '__main__':
    require(len(sys.argv) in (1, 2), 'Usage: verify_repository_hygiene.py [ARTIFACTS]')
    main()
