#!/usr/bin/env python3
"""Validate the standalone Studio contract and prepare a static GitHub Pages publish."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
STUDIO = ROOT / 'shadow/samples/netDxf.Shadow.Studio'
BROWSER = ROOT / 'shadow/samples/netDxf.Shadow.Studio.Browser'
FONT_BLOBS = {
    'NotoSans-Regular.ttf': 'd55220958aa51eeeb85048d746eabe43d2cd9f14',
    'NotoSans-Bold.ttf': '27131f8f479947ef1b6c1d0bc9ad5ab3d30bd831',
    'NotoSans-Italic.ttf': 'c7c67b281b9c6134c4be186cdd4639688e08855e',
    'NotoSans-BoldItalic.ttf': '1c06d9a8c3ef7067446f8a24a501b8648b549404',
    'OFL.txt': 'c82d72e422e2d08c5ab439b6bac7c2177ea0c565',
}

def source_contract():
    expected = set()
    for file in ('Program.cs', 'StudioHttp.cs', 'StudioCadHttp.cs'):
        source = (STUDIO / file).read_text(encoding='utf-8')
        prefix = '/api/cad' if file == 'StudioCadHttp.cs' else ''
        expected.update((method.upper(), prefix + path) for method, path in
                        re.findall(r'\.Map(Get|Post)\("([^"]+)"', source))
    actual = set()
    for file in ('BrowserRoutes.cs', 'BrowserCadRoutes.cs'):
        actual.update(re.findall(r'(?:Map|Legacy|Cad)\("(GET|POST)",\s*"([^"]+)"',
                                 (BROWSER / file).read_text(encoding='utf-8')))
    missing = expected - actual
    if missing:
        raise ValueError('Browser routes missing: ' + repr(sorted(missing)))
    print(f'PASS: all {len(expected)} server routes have browser transports.')
    return sorted(expected)

def prepare(published: Path):
    required = ('index.html', 'diagnostics.html', 'studio-shell.html', 'diagnostics-shell.html',
                'browser-main.mjs', 'browser-transport.mjs', '_framework/blazor.webassembly.js',
                'cad/app.mjs', 'cad/client.mjs', 'cad/cad.css', 'studio.js', 'studio.css', 'DWG-NOTICES.txt',
                'agents/workspace.mjs', 'agents/client.mjs', 'agents/contracts.mjs', 'agents/render.mjs',
                'agents/studio-control.mjs', 'agents/mcp-bridge.mjs', 'agents/browser-mcp.mjs', 'agents/agents.css',
                'agents/persistence.mjs', 'agents/file-transfer.mjs', 'agents/file-workspace.mjs',
                'agents/execution.mjs', 'agents/execution-worker.mjs', 'agents/execution-frame.html')
    for relative in required:
        if not (published / relative).is_file():
            raise ValueError(f'Missing browser publish asset: {relative}')
    if not list((published / '_framework').glob('*.wasm')):
        raise ValueError('No WebAssembly payload was published.')
    for name, expected in FONT_BLOBS.items():
        data = (published / 'fonts' / name).read_bytes()
        actual = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
        if actual != expected:
            raise ValueError(f'Bundled font/license differs from the pinned upstream blob: {name}')
    for source in (STUDIO / 'wwwroot').rglob('*'):
        if not source.is_file() or source.name in ('index.html', 'diagnostics.html'):
            continue
        relative = source.relative_to(STUDIO / 'wwwroot')
        if source.read_bytes() != (published / relative).read_bytes():
            raise ValueError(f'Shared UI asset drifted: {relative}')
    (published / '.nojekyll').write_text('', encoding='utf-8')
    # Pages serves the root and explicit HTML entry points. Do not serve an index fallback
    # at arbitrary nested paths with a wrong relative base URI.
    (published / '404.html').write_text('<!doctype html><html lang="en"><meta charset="utf-8"><title>Studio page not found</title><h1>Page not found</h1><p>Open the repository Pages root to launch Shadow Studio.</p></html>', encoding='utf-8')
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    manifest = {'runtime': 'blazor-webassembly', 'commit': commit, 'serverRequired': False,
                'routes': source_contract(), 'assets': []}
    for file in sorted(published.rglob('*')):
        if file.is_file() and file.name != 'studio-manifest.json':
            data = file.read_bytes()
            manifest['assets'].append({'path': file.relative_to(published).as_posix(), 'size': len(data),
                                       'sha256': hashlib.sha256(data).hexdigest()})
    (published / 'studio-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: validated {len(manifest["assets"])} assets, shared frontend and pinned fonts.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-source', action='store_true')
    parser.add_argument('--published', type=Path)
    args = parser.parse_args()
    if args.check_source:
        source_contract()
    if args.published:
        prepare(args.published.resolve())
    if not args.check_source and args.published is None:
        parser.error('Choose --check-source or --published.')
