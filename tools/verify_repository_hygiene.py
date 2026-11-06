#!/usr/bin/env python3
"""Source-only documentation/workflow guard; this is not CAD conformance evidence."""
from __future__ import annotations

import ast
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
# Keep an exact inventory: the isolated Pages workflow does not replace either
# package/release workflow, and unknown publishers must still be rejected.
WORKFLOWS = {
    '.github/workflows/ci-build.yml',
    '.github/workflows/release.yml',
    '.github/workflows/shadow-studio-pages.yml',
}
GUIDES = 'doc/dxf-conformance/'
ENTRY_POINTS = {'README.md': 32_768, 'doc/CI-RELEASE.md': 32_768}
# Check ordinary inline links and reference-style definitions.
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
    require(paths == WORKFLOWS,
            f'Workflow inventory mismatch: missing {sorted(WORKFLOWS - paths)}; '
            f'unexpected {sorted(paths - WORKFLOWS)}')


def size_limit(content: bytes, limit: int, label: str) -> None:
    require(0 < len(content) <= limit, f'{label} is empty or exceeds its {limit}-byte entry-point budget')


def check_links(root: Path, document: str, text: str) -> int:
    count = 0
    for destination in destinations(text):
        target = local_target(root, document, destination)
        if target is None:
            continue
        require((root / target).exists(), f'Missing inline link target in {document}: {destination}')
        count += 1
    return count


def pipeline_commands(source: str) -> set[str]:
    choices = [keyword.value for node in ast.walk(ast.parse(source))
               if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
               and node.func.attr == 'add_argument' and node.args
               and isinstance(node.args[0], ast.Constant) and node.args[0].value == 'command'
               for keyword in node.keywords if keyword.arg == 'choices']
    require(len(choices) == 1, 'Cannot identify the pipeline command inventory')
    return set(ast.literal_eval(choices[0]))


def check_pipeline_commands(text: str, choices: set[str]) -> None:
    for command in re.findall(r'^\s*python(?:3)?\s+tools/ci/pipeline\.py\s+([\w-]+)', text, re.MULTILINE):
        require(command in choices or command == '--help', f'Unknown documented pipeline command: {command}')


def check_guide_references(root: Path, paths: list[str]) -> None:
    """Check incoming links from the rest of the repository as guides evolve."""
    for path in paths:
        if not path.lower().endswith('.md'):
            continue
        text = (root / path).read_text(encoding='utf-8-sig')
        for destination in destinations(text):
            target = local_target(root, path, destination)
            if target is not None and target.startswith(GUIDES):
                require((root / target).exists(),
                        f'Missing guide link target in {path}: {destination}')


def self_test() -> int:
    workflow_inventory(set(WORKFLOWS))
    count = 0
    for path in sorted(WORKFLOWS):
        count += reject(lambda: workflow_inventory(WORKFLOWS - {path}))
    count += reject(lambda: workflow_inventory(WORKFLOWS | {'.github/workflows/temporary-publish.yml'}))
    size_limit(b'x', 1, 'control')
    count += reject(lambda: size_limit(b'', 1, 'control'))
    count += reject(lambda: size_limit(b'xx', 1, 'control'))
    choices = pipeline_commands("parser.add_argument('command', choices=('metadata', 'verify'))")
    check_pipeline_commands('python tools/ci/pipeline.py verify --directory artifacts/packages', choices)
    for command in ('build', 'conformance'):
        count += reject(lambda: check_pipeline_commands('python tools/ci/pipeline.py ' + command, choices))
    with tempfile.TemporaryDirectory(prefix='netdxf-hygiene-') as directory:
        root = Path(directory)
        (root / 'README.md').write_text('control', encoding='utf-8')
        require(check_links(root, 'index.md', '[ok](README.md#anchor)') == 1, 'Existing link control')
        require(check_links(root, 'index.md', '[external](https://example.invalid/missing.md) [anchor](#section)') == 0, 'External/anchor control')
        count += reject(lambda: check_links(root, 'index.md', '[missing](absent.md)'))
        count += reject(lambda: check_links(root, 'index.md', '[escape](../outside.md)'))
        count += reject(lambda: check_links(root, 'index.md', '[escape](%2e%2e/outside.md)'))
        require(local_target(root, 'doc/dxf-conformance/README.md', 'io.md') == GUIDES + 'io.md',
                'Relative guide resolution')
        require(local_target(root, 'README.md', 'https://github.com/example/repo/blob/pinned/old-guide.md') is None,
                'Immutable archive URL must remain external')
        (root / 'caller.md').write_text('[old guide](doc/dxf-conformance/missing.md)', encoding='utf-8')
        count += reject(lambda: check_guide_references(root, ['caller.md']))
        (root / 'caller.md').write_text('[archive](https://github.com/example/repo/blob/pinned/missing.md)', encoding='utf-8')
        check_guide_references(root, ['caller.md'])
    return count


def main() -> None:
    count = self_test()
    raw = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT)
    paths = [path for path in raw.decode('utf-8').split('\0') if path]
    require(paths and len(paths) == len(set(paths)), 'Missing or duplicate tracked-file inventory')
    workflow_inventory({path for path in paths if path.startswith('.github/workflows/') and path.lower().endswith(('.yml', '.yaml'))})
    choices = pipeline_commands((ROOT / 'tools/ci/pipeline.py').read_text(encoding='utf-8'))
    links = 0
    guides = {path: 8_192 for path in paths if path.startswith(GUIDES) and path.endswith('.md')}
    require(GUIDES + 'README.md' in guides, 'Missing DXF guide index')
    documents = {**ENTRY_POINTS, **guides}
    for path, budget in documents.items():
        content = (ROOT / path).read_bytes()
        size_limit(content, budget, path)
        links += check_links(ROOT, path, content.decode('utf-8-sig'))
        check_pipeline_commands(content.decode('utf-8-sig'), choices)
    index = (ROOT / GUIDES / 'README.md').read_text(encoding='utf-8-sig')
    require('Full AutoCAD parity is not established' in index, 'Missing explicit support boundary')
    check_guide_references(ROOT, paths)
    print(f'PASS: two core workflows plus the isolated Pages workflow, {len(documents)} maintained documentation pages, '
          f'{links} existing local links, valid pipeline command names and incoming guide links; {count} negative controls rejected. '
          'Source hygiene only, not additional CAD conformance or native acceptance.')


if __name__ == '__main__':
    require(len(sys.argv) in (1, 2), 'Usage: verify_repository_hygiene.py [ARTIFACTS]')
    main()
