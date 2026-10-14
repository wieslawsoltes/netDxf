#!/usr/bin/env python3
"""Exercise a private Studio process using only Python's standard library.

Never sends mutations to a user-supplied server. The child binds port zero; the
actual loopback address is recovered from its startup log. No browser is required.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[2]
STUDIO = ROOT / 'shadow/samples/netDxf.Shadow.Studio'


def run(dotnet: str, configuration: str) -> None:
    candidates = list((STUDIO / 'bin').glob(f'**/{configuration}/net8.0/netDxf.Shadow.Studio.dll'))
    if len(candidates) != 1:
        raise RuntimeError(f'Build exactly one {configuration}/net8.0 Studio target first; found {len(candidates)} assemblies.')
    reports = []
    complete = False
    def passed(name: str) -> None:
        reports.append(name)
        print('PASS http/' + name, flush=True)
    with tempfile.TemporaryDirectory(prefix='shadow-http-') as temp:
        log_path = Path(temp) / 'server.log'
        with log_path.open('wb') as log:
            process = subprocess.Popen([dotnet, str(candidates[0]), '--urls', 'http://127.0.0.1:0'],
                                       cwd=STUDIO, stdout=log, stderr=subprocess.STDOUT)
        streams = []
        try:
            deadline = time.monotonic() + 30
            base = None
            while time.monotonic() < deadline:
                match = re.search(r'Now listening on: (http://127\.0\.0\.1:\d+)', log_path.read_text(errors='replace'))
                if match:
                    base = match.group(1); break
                if process.poll() is not None:
                    raise RuntimeError(log_path.read_text(errors='replace'))
                time.sleep(.05)
            if not base:
                raise TimeoutError('Private Studio did not start: ' + log_path.read_text(errors='replace'))

            def headers(s=None):
                return {} if s is None else {'X-Shadow-Studio': '1', 'X-Shadow-Document': s['documentId'],
                    'X-Shadow-Workspace': s['workspaceId'], 'X-Shadow-View': str(s['viewVersion'])}
            def request(path, data=None, s=None, expected=200, raw=False, custom=None):
                h = headers(s); h.update(custom or {})
                if data is not None and not isinstance(data, bytes):
                    h['Content-Type'] = 'application/json'; data = json.dumps(data).encode()
                req = Request(base + '/api/' + path, data=data, headers=h)
                try:
                    response = urlopen(req, timeout=15)
                except HTTPError as error:
                    response = error
                with response:
                    body = response.read(); status = response.status
                    if status != expected:
                        raise AssertionError(f'{path}: expected {expected}, got {status}: {body[:600]!r}')
                    return body if raw else (json.loads(body) if body else None)
            def state(): return request('state')
            def post(path, s, **values): return request(path, {'revision': s['revision'], **values}, s)
            def notice(stream):
                fields = {}
                while True:
                    raw_line = stream.readline()
                    if not raw_line: raise EOFError('SSE stream ended before a complete event.')
                    line = raw_line.decode().rstrip('\r\n')
                    if not line:
                        if 'data' in fields: return fields['id'], json.loads(fields['data'])
                        continue
                    if ':' in line:
                        name, value = line.split(':', 1); fields[name] = value.lstrip(' ')

            initial = state(); count = initial['entities']
            request('health'); passed('private host startup')
            for asset in ['/', '/studio.js', '/studio-state.mjs', '/workspaces.css']:
                with urlopen(base + asset, timeout=15) as response:
                    payload = response.read()
                    assert response.status == 200 and payload
                    if asset.endswith(('.js', '.mjs')): assert 'javascript' in response.headers['Content-Type']
                    assert "script-src 'self'" in response.headers['Content-Security-Policy']
            passed('real static module and stylesheet delivery under CSP')
            request('add', {'revision': 0, 'kind': 'line', 'x': 0, 'y': 0}, expected=403)
            request('graph', expected=409)
            request('add', {'revision': 0, 'kind': 'line', 'x': 0, 'y': 0}, expected=409, custom={'X-Shadow-Studio':'1'})
            assert state()['revision'] == 0; passed('missing mutation and workspace headers are refused')
            request('linetype-view', {'revision': initial['revision'], 'alignment': 'A'}, expected=403)
            request('linetype-view', {'revision': initial['revision'], 'alignment': '1'}, initial, expected=400)
            fitted = post('linetype-view', initial, alignment='A')
            assert fitted['revision'] == initial['revision'] and fitted['undoCount'] == initial['undoCount']
            assert fitted['linetypeAlignment'] == 'A' and fitted['viewVersion'] > initial['viewVersion']
            request('graph', s=initial, expected=409)
            passed('fitting view is source-neutral and fences stale requests')
            same = post('linetype-view', fitted, alignment='A')
            assert same['viewVersion'] == fitted['viewVersion']
            initial = post('linetype-view', fitted, alignment='Periodic')
            assert initial['revision'] == 0 and initial['linetypeAlignment'] == 'Periodic'
            passed('fitting no-op and periodic restore retain source revision')
            request('annotation-view', {'revision': initial['revision'], 'mode': 'Enabled', 'paperUnits': 1, 'drawingUnits': 25}, expected=403)
            request('annotation-view', {'revision': initial['revision'], 'mode': 'Enabled', 'paperUnits': 0, 'drawingUnits': 25}, initial, expected=400)
            scaled = post('annotation-view', initial, mode='Enabled', paperUnits=1, drawingUnits=25)
            assert scaled['revision'] == initial['revision'] and scaled['undoCount'] == initial['undoCount']
            assert scaled['annotationMode'] == 'Enabled' and scaled['annotationDrawingUnits'] == 25
            assert scaled['viewVersion'] > initial['viewVersion']
            request('graph', s=initial, expected=409)
            passed('model annotation view is guarded source-neutral and fences old requests')
            same = post('annotation-view', scaled, mode='Enabled', paperUnits=1, drawingUnits=25)
            assert same['viewVersion'] == scaled['viewVersion']
            initial = post('annotation-view', same, mode='Drawing')
            assert initial['revision'] == 0 and initial['annotationMode'] == 'Drawing'
            passed('model annotation no-op and drawing restore preserve source history')
            stream = urlopen(base + '/api/events', timeout=15); streams.append(stream)
            cursor, first = notice(stream)
            assert first['reset'] and first['documentId'] == initial['documentId']; passed('SSE initial identity')
            branch_state = post('branches', initial, name='HTTP experiment'); branch = branch_state['workspaceId']
            _, changed = notice(stream)
            assert changed['workspaceId'] == branch and changed['changeVersion'] > first['changeVersion']; passed('SSE workspace invalidation')
            request('add', {'revision': 0, 'kind': 'line', 'x': 0, 'y': 0}, initial, expected=409)
            request('save', {'revision': branch_state['revision']}, branch_state, expected=400); passed('branch identity and source-export isolation')
            line = next(i['id'] for i in branch_state['items'] if i['type'] == 'Line' and not i['inserted'])
            branch_state = post('property', branch_state, id=line, property='EndPoint', value={'Y': 15})
            live = post('workspace', branch_state, id='live')
            assert live['revision'] == 0 and live['entities'] == count
            live = post('property', live, id=line, property='EndPoint', value={'Y': 30}); passed('branch edits are detached from source')
            def preview(s, choice='Unresolved'):
                return post('merge/preview', s, documentId=s['documentId'], branchId=branch,
                            targetRevision=s['sourceRevision'], incomingRevision=branch_state['revision'], choice=choice)
            conflict = preview(live)
            assert not conflict['canApply'] and conflict['planId'] is None and conflict['conflictCount'] == 1
            assert conflict['conflicts'][0]['path'] == ['EndPoint','Y']; passed('field-level unresolved conflict response')
            plan = preview(live, 'Incoming'); assert plan['canApply']
            for backend, magic in [('skia', b'\x89PNG'), ('svg', b'<')]:
                image = request(f'render?width=600&height=400&backend={backend}&merge={plan["planId"]}', s=live, raw=True)
                assert image.startswith(magic), image[:100]
            passed('shared Skia and SVG merge preview rendering')
            live = post('merge/apply', live, documentId=live['documentId'], planId=plan['planId'],
                        targetRevision=plan['targetRevision'], incomingRevision=plan['incomingRevision'])
            assert live['undoCount'] == 2 and live['workspaceId'] == 'live'; passed('one canonical merge command')
            exported = request('save', {'revision': live['revision']}, live, raw=True)
            assert len(exported) > 1000
            saved = state(); assert saved['revision'] >= live['revision'] and saved['undoCount'] == live['undoCount']
            undone = post('history/undo', saved)
            info = request('object/' + line, s=undone)
            value = next(p['value'] for p in info['properties'] if p['name'] == 'EndPoint')
            assert float(value['Y']) == 30
            live = post('history/redo', undone); passed('save normalization preserves merge undo and redo')
            graph = request('graph', s=live)
            assert graph['workspaceId'] == 'live' and graph['layout'] == live['layout'] and graph['revision'] == live['revision']; passed('graph receipt binds workspace layout and revision')
            request('save', s=live, expected=405, raw=True); passed('GET never invokes the mutating DXF writer')
            old_live = live
            live = post('add', live, kind='circle', x=220, y=100)
            request(f'load?revision={old_live["revision"]}', exported, live, expected=409)
            assert state()['entities'] == live['entities']; passed('upload refuses an intervening edit')
            loaded = request(f'load?revision={live["revision"]}', exported, live)
            assert loaded['documentId'] != live['documentId'] and not loaded['branches']
            request('graph', s=live, expected=409)
            passed('reload expires prior document and branch identities')
            latest = state()
            reconnect = urlopen(Request(base + '/api/events', headers={'Last-Event-ID': cursor}), timeout=15); streams.append(reconnect)
            _, reset = notice(reconnect)
            assert reset['reset'] and reset['documentId'] == latest['documentId']; passed('SSE reconnect resynchronizes after replacement')
            complete = True
        finally:
            for stream in streams: stream.close()
            process.terminate()
            try: process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait(timeout=8)
            out = ROOT / 'artifacts'; out.mkdir(exist_ok=True)
            (out / 'http-contract-server.log').write_text(log_path.read_text(errors='replace'))
            (out / 'http-contract.json').write_text(json.dumps({'source':os.getenv('GITHUB_SHA'),
                'configuration':configuration, 'complete':complete, 'passed':len(reports), 'tests':reports}, indent=2))
    print(f'HTTP verification: {len(reports)} passed.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet'); parser.add_argument('--configuration', default='Release')
    args = parser.parse_args(); run(args.dotnet, args.configuration)
