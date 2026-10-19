#!/usr/bin/env python3
"""Run the real official .NET SDKs, agent loop, CAD tools and UI in published WASM.

Only paid provider HTTP responses are replaced with deterministic wire fixtures. The
Studio, source model, immutable snapshots, transactions and agent runtime are real.
No API key, OAuth account, CAD server or development host is required.
"""
from __future__ import annotations
import argparse
from collections import deque
from contextlib import contextmanager, nullcontext
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import itertools
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time
import traceback
from urllib.request import Request, urlopen
from playwright.sync_api import sync_playwright, expect
from blazor_studio_browser import static_host
from browser_folder_checks import verify_browser_folder
from browser_storage_checks import verify_browser_recovery_storage
from browser_chat_layout_checks import verify_compact_chat_layout
from browser_model_discovery_checks import verify_model_discovery

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/blazor-agents'
ASSEMBLY = 'netDxf.Shadow.Studio.WebAssembly'
KEY = 'verification-only-credential-never-use-for-inference'
BRIDGE_TOKEN = 'verification-only-loopback-companion-token'
STREAM_IDS = itertools.count(1)


def read_tool_result(call, result, *, forbidden=()):
    """Read the harness's bounded-result archive without skipping privacy checks."""
    def check_private(value):
        if any(secret in json.dumps(value, ensure_ascii=False) for secret in forbidden):
            raise AssertionError('Credential appeared in a public tool result or archive page')

    check_private(result)
    if not result.get('truncated'):
        return result
    path, total = result['path'], result['totalCharacters']
    if not isinstance(total, int) or not 0 < total <= 16 * 1024 * 1024:
        raise AssertionError('Invalid tool result archive size')
    chunks, offset, version = [], 0, None
    while offset < total:
        part = call('workspace_read', {'path': path, 'offset': offset, 'length': 1000})
        check_private(part)
        if version is None:
            version = part['version']
        # Workspace offsets count .NET UTF-16 code units, not Python code points.
        count = len(part['content'].encode('utf-16-le', errors='surrogatepass')) // 2
        if (part['path'] != path or part['offset'] != offset or part['totalCharacters'] != total
                or part['version'] != version or not 0 < count <= min(1000, total - offset)
                or part['hasMore'] != (offset + count < total)):
            raise AssertionError('Tool result archive changed or did not advance consistently')
        chunks.append(part['content'])
        offset += count
    text = ''.join(chunks)
    check_private(text)  # Includes secrets that could straddle two archive pages.
    value = json.loads(text)
    check_private(value)
    return value


def openai_stream(text='Complete.', tool=None, call_id=None, *, finish=True, limited=False):
    call_id = call_id or f'call_browser_{next(STREAM_IDS)}'
    if tool is None and finish and not limited:
        tool = ('agent_finish', {'outcome': 'completed', 'summary': text, 'verification': ['Observed the recorded browser tool results.']})
    item = ({'type': 'function_call', 'id': 'fc_browser', 'call_id': call_id,
             'name': tool[0], 'arguments': json.dumps(tool[1]), 'status': 'completed'} if tool else
            {'type': 'message', 'id': 'msg_browser', 'role': 'assistant', 'status': 'completed',
             'content': [{'type': 'output_text', 'text': text, 'annotations': []}]})
    response = {'id': 'resp_browser', 'created_at': 1, 'model': 'verification-model', 'status': 'in_progress', 'output': []}
    values = [{'type': 'response.created', 'sequence_number': 0, 'response': response},
              {'type': 'response.output_item.added', 'sequence_number': 1, 'output_index': 0, 'item': item}]
    if not tool:
        values.append({'type': 'response.output_text.delta', 'sequence_number': 2,
                       'item_id': 'msg_browser', 'output_index': 0, 'content_index': 0, 'delta': text})
    values.extend([{'type': 'response.output_item.done', 'sequence_number': 3, 'output_index': 0, 'item': item},
                   {'type': 'response.incomplete' if limited else 'response.completed', 'sequence_number': 4, 'response': {
                       **response, 'status': 'incomplete' if limited else 'completed', 'store': False, 'output': [item],
                       **({'incomplete_details': {'reason': 'max_output_tokens'}} if limited else {}),
                       'usage': {'input_tokens': 200, 'output_tokens': 30, 'total_tokens': 230}}}])
    return ''.join('data: ' + json.dumps(value) + '\n\n' for value in values)


def anthropic_stream(tool=None, *, text='Anthropic browser SDK complete.', finish=True, limited=False):
    if tool is None and finish and not limited:
        tool = ('agent_finish', {'outcome': 'completed', 'summary': text, 'verification': ['Observed the recorded browser tool results.']})
    block_index = 1 if tool else 0
    values = [
        {'type': 'message_start', 'message': {'id': 'msg_browser', 'type': 'message', 'role': 'assistant',
         'model': 'verification-model', 'content': [], 'stop_reason': None, 'stop_sequence': None,
         'usage': {'input_tokens': 200, 'output_tokens': 1}}},
        *([
            {'type': 'content_block_start', 'index': 0, 'content_block': {'type': 'thinking', 'thinking': '', 'signature': ''}},
            {'type': 'content_block_delta', 'index': 0, 'delta': {'type': 'thinking_delta', 'thinking': 'Checking the workspace.'}},
            {'type': 'content_block_delta', 'index': 0, 'delta': {'type': 'signature_delta', 'signature': 'signature_fixture'}},
            {'type': 'content_block_stop', 'index': 0},
        ] if tool else []),
        {'type': 'content_block_start', 'index': block_index, 'content_block':
            {'type': 'tool_use', 'id': f'anthropic_browser_call_{next(STREAM_IDS)}', 'name': tool[0], 'input': {}} if tool else {'type': 'text', 'text': ''}},
        {'type': 'content_block_delta', 'index': block_index, 'delta':
            {'type': 'input_json_delta', 'partial_json': json.dumps(tool[1])} if tool else {'type': 'text_delta', 'text': text}},
        {'type': 'content_block_stop', 'index': block_index},
        {'type': 'message_delta', 'delta': {'stop_reason': 'max_tokens' if limited else 'tool_use' if tool else 'end_turn', 'stop_sequence': None}, 'usage': {'output_tokens': 10}},
        {'type': 'message_stop'},
    ]
    return ''.join('event: ' + value['type'] + '\ndata: ' + json.dumps(value) + '\n\n' for value in values)


def google_stream(tool=None, *, text='Google browser SDK complete.', finish=True, limited=False):
    if tool is None and finish and not limited:
        tool = ('agent_finish', {'outcome': 'completed', 'summary': text, 'verification': ['Observed the recorded browser tool results.']})
    return 'data: ' + json.dumps({'candidates': [{'index': 0, 'content': {'role': 'model', 'parts': [
        {'functionCall': {'name': tool[0], 'args': tool[1]}, 'thoughtSignature': 'c2lnbmF0dXJl'} if tool else
        {'text': text}]}, 'finishReason': 'MAX_TOKENS' if limited else 'STOP'}],
        'modelVersion': 'verification-model', 'responseId': 'google_browser',
        'usageMetadata': {'promptTokenCount': 200, 'candidatesTokenCount': 10, 'totalTokenCount': 210}}) + '\n\n'


@contextmanager
def companion_fixture(origin, executable):
    """Real companion and native SDK, with only its upstream paid API replaced."""
    upstream_requests = []
    class Upstream(BaseHTTPRequestHandler):
        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', '0'))))
            upstream_requests.append({'path': self.path, 'body': body})
            data = anthropic_stream().encode('utf-8')
            self.send_response(200); self.send_header('Content-Type', 'text/event-stream')
            self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)
        def log_message(self, *_):
            pass
    upstream = ThreadingHTTPServer(('127.0.0.1', 0), Upstream)
    thread = threading.Thread(target=upstream.serve_forever, daemon=True); thread.start()
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0)); port = reservation.getsockname()[1]
    with tempfile.TemporaryDirectory(prefix='shadow-agent-browser-') as temporary:
        directory = Path(temporary); workspace = directory / 'project'; workspace.mkdir()
        (workspace / 'hello.cs').write_text('// Native project\n', encoding='utf-8')
        log = (directory / 'companion.log').open('w+', encoding='utf-8')
        process = subprocess.Popen(['dotnet', str(executable), '--origin', origin, '--port', str(port),
            '--state-file', str(directory / 'registration.json'), '--workspace', str(workspace), '--allow-processes'],
            cwd=ROOT, env={**os.environ, 'SHADOW_AGENT_BRIDGE_TOKEN': BRIDGE_TOKEN}, stdout=log, stderr=subprocess.STDOUT)
        address = f'http://127.0.0.1:{port}'
        try:
            deadline = time.monotonic() + 30
            while True:
                try:
                    with urlopen(Request(address + '/health', headers={'Authorization': 'Bearer ' + BRIDGE_TOKEN}), timeout=1) as response:
                        if response.status == 200: break
                except OSError:
                    if process.poll() is not None or time.monotonic() > deadline:
                        log.seek(0); raise AssertionError('Companion failed to start: ' + log.read())
                    time.sleep(.05)
            yield address, f'http://127.0.0.1:{upstream.server_port}', workspace, upstream_requests
        finally:
            process.terminate()
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
            log.close(); upstream.shutdown(); upstream.server_close(); thread.join(timeout=5)


def verify(base, mount, browser, passed, companion=None, upstream=None, native_workspace=None, upstream_requests=None):
    context = browser.new_context(viewport={'width': 1600, 'height': 1000}, accept_downloads=True)
    page = context.new_page(); page.set_default_timeout(30000)
    errors, requests, replies, network, discovery_requests = [], [], deque(), [], []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('request', lambda request: network.append(request.url))
    tag = 'root' if mount == '/' else 'subpath'

    def check(name, value=True):
        if not value:
            raise AssertionError(name)
        passed.append(tag + ': ' + name); print('PASS:', passed[-1], flush=True)

    def provider(route):
        headers = {'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*',
                   'Access-Control-Allow-Methods': 'GET,POST,OPTIONS'}
        if route.request.method == 'OPTIONS':
            route.fulfill(status=204, headers=headers); return
        if route.request.method == 'GET':
            discovery_requests.append({'url': route.request.url, 'headers': route.request.headers})
            if route.request.url.startswith('https://generativelanguage.googleapis.com/'):
                payload = {'models': [
                    {'name': 'models/fixture-gemini', 'displayName': 'Fixture Gemini',
                     'inputTokenLimit': 131072, 'outputTokenLimit': 8192,
                     'supportedGenerationMethods': ['generateContent']},
                    {'name': 'models/fixture-embedding', 'displayName': 'Embedding only',
                     'inputTokenLimit': 2048, 'supportedGenerationMethods': ['embedContent']}]}
            elif route.request.url.startswith('https://api.openai.com/'):
                payload = {'object': 'list', 'data': [{'id': 'fixture-manual', 'object': 'model',
                                                      'owned_by': 'verification', 'created': 1}]}
            else:
                payload = {'data': [{'id': 'fixture-claude', 'display_name': 'Fixture Claude',
                                    'type': 'model', 'created_at': '2026-01-01T00:00:00Z'},
                                   {'id': 'fixture-claude-limits', 'display_name': 'Fixture Claude with limits',
                                    'type': 'model', 'created_at': '2026-01-01T00:00:00Z',
                                    'max_input_tokens': 200000, 'max_tokens': 8192}], 'has_more': False}
            route.fulfill(status=200, headers=headers, json=payload); return
        requests.append({'url': route.request.url, 'headers': route.request.headers, 'body': route.request.post_data_json})
        if not replies:
            route.fulfill(status=400, headers=headers, json={'error': {'message': 'Unexpected provider request in verification.'}}); return
        value = replies.popleft()
        if value == 'rate_limit':
            route.fulfill(status=429, headers={**headers, 'Retry-After': '0'}, json={'error': {'message': 'retry verification', 'type': 'rate_limit_error'}})
        else:
            route.fulfill(status=200, headers={**headers, 'Content-Type': 'text/event-stream'}, body=value)

    for pattern in ['https://api.openai.com/**', 'https://api.anthropic.com/**', 'https://generativelanguage.googleapis.com/**']:
        page.route(pattern, provider)

    def invoke(method, *args):
        return page.evaluate('async ({assembly,method,args}) => await DotNet.invokeMethodAsync(assembly,method,...args)',
                             {'assembly': ASSEMBLY, 'method': method, 'args': list(args)})

    def call(name, args=None, error=False):
        result = invoke('AgentMcpCall', name, args or {})
        if result.get('isError') and not error:
            raise AssertionError(name + ': ' + result['text'])
        return result if error else json.loads(result['text'])

    def wait_idle():
        deadline = time.monotonic() + 30
        while True:
            state = invoke('AgentPoll', 0)
            if not state['running']:
                return state
            if time.monotonic() > deadline:
                raise AssertionError('Agent did not finish: ' + json.dumps(state))
            page.wait_for_timeout(50)

    def identity():
        state = call('shadow_state')
        return {name: state[name] for name in ['documentId', 'workspaceId', 'viewVersion', 'revision']}

    def configure(provider_id='openai', permission='fullAccess', endpoint=''):
        page.get_by_role('tab', name='Settings', exact=True).click()
        page.get_by_label('Model provider', exact=True).select_option(provider_id)
        page.get_by_label('Model ID', exact=True).fill('verification-model')
        page.get_by_label('API key or companion access token', exact=True).fill(KEY)
        page.get_by_label('Provider endpoint or local companion URL', exact=True).fill(endpoint)
        page.get_by_label('Agent permission mode', exact=True).select_option(permission)
        page.get_by_role('button', name='Connect provider', exact=True).click()
        expect(page.get_by_role('tab', name='Conversation', exact=True)).to_have_attribute('aria-selected', 'true')

    def send(prompt):
        page.get_by_label('Message to coding agent', exact=True).fill(prompt)
        page.get_by_role('button', name='Send task', exact=True).click()

    try:
        page.goto(base + mount, wait_until='domcontentloaded')
        page.wait_for_function('document.documentElement.dataset.shadowReady || document.documentElement.dataset.shadowError', timeout=180000)
        check('published .NET WASM Studio boots', not page.locator('html').get_attribute('data-shadow-error'))
        page.get_by_role('button', name='Agents', exact=True).click()
        expect(page.get_by_role('complementary', name='Coding agent workspace')).to_be_visible()
        catalog = invoke('AgentMcpTools')
        names = {tool['name'] for tool in catalog}
        check('native DXF, shadow graph, Studio UI and workspace tools are discoverable',
              {'shadow_graph_edit', 'shadow_read', 'dxf_source_inspect', 'studio_routes', 'studio_ui',
               'studio_read', 'workspace_write', 'agent_plan'} <= names)
        check('tool schemas are available to the model and JSON console', all(tool['inputSchema']['type'] == 'object' for tool in catalog))
        verify_model_discovery(page, invoke, check, discovery_requests, KEY, OUTPUT, tag)
        configure()
        replies.extend(['rate_limit', openai_stream(tool=('shadow_state', {}), call_id='state_1'),
                        openai_stream(tool=('workspace_write', {'path': 'src/analysis.cs', 'content': '// Built in .NET WASM\n', 'expectedVersion': 0}), call_id='file_1'),
                        openai_stream('Drawing inspected and code saved.')])
        send('Inspect the real drawing, then write a source file.')
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('Drawing inspected and code saved.')
        state = wait_idle()
        check('official OpenAI Responses SDK executes multi-turn tools in WASM', state['status'] == 'completed' and len(requests) == 4)
        check('transient provider retry never duplicates a file write', invoke('AgentWorkspaceRead', 'src/analysis.cs')['version'] == 1)
        check('function results are serialized by the official SDK', 'function_call_output' in json.dumps(requests[-1]['body']))
        check('provider token usage is visualized', any(event['type'] == 'usage' for event in state['events']))
        check('provider API credential is absent from browser storage and checkpoint',
              KEY not in invoke('AgentExport') and page.evaluate('(key) => !JSON.stringify({...localStorage,...sessionStorage}).includes(key)', KEY))

        replies.extend([openai_stream(tool=('workspace_read', {'path': 'missing-error-fixture.txt'}), call_id='collapsed_error'),
                        openai_stream('Missing file error recorded.')])
        send('Inspect a missing workspace file and report the error.')
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('Missing file error recorded.')
        check('failed tool call completes its deterministic test turn', wait_idle()['status'] == 'completed')
        error_card = page.locator('details.agent-tool-card.error').last
        expect(error_card).to_be_visible()
        expect(error_card).not_to_have_attribute('open', '')
        check('failed tool arguments and results start collapsed')
        error_card.locator('summary').click()
        expect(error_card).to_have_attribute('open', '')
        expect(error_card.locator('.agent-tool-body')).to_contain_text('missing-error-fixture.txt')
        page.get_by_role('tab', name='Settings', exact=True).click()
        page.get_by_role('tab', name='Conversation', exact=True).click()
        expect(error_card).to_have_attribute('open', '')
        check('manually opened tool error remains expanded across conversation renders')
        error_card.locator('summary').click()
        expect(error_card).not_to_have_attribute('open', '')
        page.screenshot(path=str(OUTPUT / f'{tag}-collapsed-tool-error.png'), full_page=True)

        before = call('shadow_state'); original_count = before['data']['entities']
        created = call('shadow_graph_edit', {**identity(), 'description': 'Browser agent native CAD verification', 'operations': [
            {'op': 'addEntity', 'type': 'netDxf.Entities.Circle', 'alias': '@circle',
             'properties': {'Center': {'X': 20, 'Y': 30, 'Z': 0}, 'Radius': 12}}]})
        circle = created['data']['aliases']['@circle']
        check('generic MCP graph edit creates a real DXF entity', call('shadow_state')['data']['entities'] == original_count + 1)
        radius = call('shadow_read', {'id': circle, 'path': '/Radius'})
        check('lowest-level shadow property is readable', radius['data']['value']['value'] == '12')
        stale = call('shadow_edit', {**{k: before[k] for k in identity()}, 'edits': [{'id': circle, 'path': '/Radius', 'value': 14}]}, error=True)
        check('stale revision refuses a model mutation', stale['isError'])
        call('shadow_edit', {**identity(), 'edits': [{'id': circle, 'path': '/Center/X', 'value': 45}]})
        check('nested value edits reach the native model', call('shadow_read', {'id': circle, 'path': '/Center/X'})['data']['value']['value'] == '45')
        call('shadow_undo', identity())
        check('agent edits participate in native Studio undo', call('shadow_read', {'id': circle, 'path': '/Center/X'})['data']['value']['value'] == '20')
        call('shadow_redo', identity())
        check('agent edits participate in native Studio redo', call('shadow_read', {'id': circle, 'path': '/Center/X'})['data']['value']['value'] == '45')
        raw_ui = page.evaluate('() => shadowStudioAgentUi.snapshot()')
        ui_receipt = call('studio_ui_inspect')
        ui = read_tool_result(call, ui_receipt, forbidden=(KEY,))
        check('semantic Studio inspection omits credential controls before runtime redaction',
              KEY not in json.dumps(raw_ui) and 'availableActions' in raw_ui)
        check('semantic Studio inspection remains readable through bounded tool results',
              KEY not in json.dumps(ui) and 'availableActions' in ui and ui['ui']['controls'])
        routes = call('studio_routes', {'includeRequestTypes': True})
        discovered_routes = list(routes['routes'])
        next_offset = routes['nextOffset']
        while next_offset is not None:
            route_page = call('studio_routes', {'offset': next_offset})
            discovered_routes.extend(route_page['routes'])
            next_offset = route_page['nextOffset']
        check('complete Studio route and request schema discovery',
              len(discovered_routes) == routes['total'] and routes['total'] >= 64 and
              len(routes['requestTypes']) > 20)
        document_id = page.locator('#studio').get_attribute('data-document')
        cad_state = call('studio_read', {'path': f'/api/cad/{document_id}/state'})
        if cad_state.get('truncated'):
            cad_state = read_tool_result(call, cad_state, forbidden=(KEY,))
            check('large CAD tool results remain available through bounded paging')
        check('read-only Studio dispatcher reaches real CAD state',
              cad_state['state']['documentId'] == before['documentId'] and
              cad_state['state']['revision'] == call('shadow_state')['revision'])

        page.get_by_role('tab', name='Files', exact=True).click()
        page.get_by_role('button', name='src/analysis.cs', exact=True).click()
        expect(page.get_by_label('Workspace file content', exact=True)).to_have_value('// Built in .NET WASM\n')
        page.get_by_label('Workspace file content', exact=True).fill('// User edited\n')
        page.get_by_role('button', name='Save file', exact=True).click()
        expect(page.locator('.agent-alert')).to_contain_text('Saved src/analysis.cs.')
        check('workspace editor saves with a current file version', invoke('AgentWorkspaceRead', 'src/analysis.cs')['content'] == '// User edited\n')
        failed = call('workspace_write', {'path': 'src/analysis.cs', 'content': 'lost update', 'expectedVersion': 1}, error=True)
        check('workspace compare-and-swap rejects an obsolete version', failed['isError'])

        configure(permission='ask')
        replies.extend([openai_stream(tool=('workspace_write', {'path': 'review.txt', 'content': 'Approved content', 'expectedVersion': 0}), call_id='approve_1'), openai_stream('Approved edit complete.')])
        send('Create review.txt after permission approval.')
        expect(page.get_by_role('button', name='Approve once', exact=True)).to_be_visible()
        check('write remains uncommitted while permission is pending', 'review.txt' not in [f['path'] for f in invoke('AgentWorkspaceFiles')])
        page.get_by_role('button', name='Approve once', exact=True).click()
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('Approved edit complete.')
        check('approval executes the exact requested write', wait_idle()['status'] == 'completed' and invoke('AgentWorkspaceRead', 'review.txt')['content'] == 'Approved content')

        # Stop remains next to the prompt even when detailed progress is collapsed.
        # A pending write gives this test a deterministic, non-billable active turn.
        replies.append(openai_stream(tool=('workspace_write', {'path': 'stopped-review.txt', 'content': 'must not commit', 'expectedVersion': 0}), call_id='stop_approval'))
        send('Propose a workspace write, then wait for my decision.')
        expect(page.get_by_role('button', name='Approve once', exact=True)).to_be_visible()
        page.set_viewport_size({'width': 390, 'height': 844})
        approve = page.get_by_role('button', name='Approve once', exact=True)
        approve.click(trial=True)
        check('mobile compact conversation keeps write approval reachable')
        expect(page.locator('.agent-run-popover')).not_to_be_visible()
        page.locator('.agent-run-summary').click()
        expect(page.locator('.agent-run-popover')).to_be_visible()
        page.get_by_role('button', name='Close run details', exact=True).focus()
        page.keyboard.press('Escape')
        expect(page.locator('.agent-run-summary')).to_be_focused()
        check('Escape dismisses active progress without cancelling its pending approval', invoke('AgentPoll', 0)['running'])
        page.locator('.agent-mode-toggle').click()
        expect(page.get_by_label('Agent execution mode', exact=True)).to_be_disabled()
        expect(page.get_by_role('button', name='Apply mode', exact=True)).to_be_disabled()
        page.keyboard.press('Escape')
        expect(page.locator('.agent-mode-toggle')).to_be_focused()
        check('mode settings remain inspectable but cannot change an active turn')
        stop = page.get_by_role('button', name='Stop agent', exact=True)
        expect(stop).to_be_enabled()
        stop.click()
        stopped = wait_idle()
        check('compact composer Stop cancels a pending write without committing it',
              stopped['status'] == 'cancelled' and 'stopped-review.txt' not in {file['path'] for file in invoke('AgentWorkspaceFiles')})
        expect(page.locator('.agent-run-summary')).to_contain_text('Stopped')
        page.locator('.agent-run-summary').click()
        resume = page.get_by_role('button', name='Resume from current conversation', exact=True)
        expect(resume).to_be_enabled()
        replies.append(openai_stream('Stopped task resumed safely.'))
        resume.click()
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('Stopped task resumed safely.')
        check('collapsed progress exposes a working Resume action', wait_idle()['status'] == 'completed')
        page.get_by_label('Message to coding agent', exact=True).click()
        expect(page.locator('.agent-run-popover')).not_to_be_visible()
        page.set_viewport_size({'width': 1600, 'height': 1000})

        configure(permission='readOnly')
        denied = call('workspace_delete', {'path': 'review.txt', 'expectedVersion': invoke('AgentWorkspaceRead', 'review.txt')['version']}, error=True)
        check('read-only policy rejects direct MCP mutation too', denied['isError'])
        pin = invoke('AgentPin', 'Keep original drawing units and object identities.')
        saved = invoke('AgentExport'); invoke('AgentReset'); invoke('AgentImport', saved)
        check('portable checkpoint restores transcript, files and pinned context',
              invoke('AgentPoll', 0)['pins'][0]['id'] == pin['id'] and invoke('AgentWorkspaceRead', 'review.txt')['content'] == 'Approved content')

        configure('google', 'fullAccess')
        replies.extend([google_stream(('workspace_write', {'path': 'google.txt', 'content': 'Google browser tool call', 'expectedVersion': 0})), google_stream()])
        send('Verify Google uses the browser agent session and writes a workspace file.')
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('Google browser SDK complete.')
        check('official Google SDK streams in real WebAssembly', wait_idle()['status'] == 'completed')
        check('Google tool calls and thought signatures survive browser continuation', invoke('AgentWorkspaceRead', 'google.txt')['content'] == 'Google browser tool call'
              and 'c2lnbmF0dXJl' in json.dumps(requests[-1]['body']) and 'functionResponse' in json.dumps(requests[-1]['body']))

        configure('anthropic', 'fullAccess')
        requests_before = len(requests)
        replies.extend(['rate_limit', anthropic_stream(('workspace_write', {'path': 'anthropic.txt', 'content': 'Direct official SDK', 'expectedVersion': 0})), anthropic_stream()])
        send('Use the official Anthropic SDK directly in the browser, create a file, and finish.')
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('Anthropic browser SDK complete.')
        check('official Anthropic SDK streams and retries directly in real WebAssembly', wait_idle()['status'] == 'completed' and len(requests) == requests_before + 3)
        check('Anthropic tool calls execute and return their result without a companion', invoke('AgentWorkspaceRead', 'anthropic.txt')['content'] == 'Direct official SDK'
              and any(block.get('type') == 'tool_result' for message in requests[-1]['body']['messages'] for block in message.get('content', []) if isinstance(block, dict)))
        check('Anthropic browser transport supplies official direct-access headers', requests[-1]['headers'].get('anthropic-dangerous-direct-browser-access') == 'true'
              and requests[-1]['headers'].get('x-api-key') == KEY)
        check('Anthropic thinking signatures survive browser tool continuation', 'signature_fixture' in json.dumps(requests[-1]['body']))
        check('switching providers omits incompatible Google reasoning signatures from Anthropic requests',
              all('c2lnbmF0dXJl' not in json.dumps(request['body']) for request in requests[requests_before:]))
        configure('google', 'fullAccess')
        requests_before_google = len(requests)
        replies.append(google_stream())
        send('Continue this mixed-provider workspace using Google.')
        deadline = time.monotonic() + 30
        while len(requests) == requests_before_google:
            if time.monotonic() > deadline: raise AssertionError('Google provider switch did not make an inference request.')
            page.wait_for_timeout(50)
        check('switching back to Google preserves its signatures and omits incompatible Anthropic thinking',
              wait_idle()['status'] == 'completed' and 'signature_fixture' not in json.dumps(requests[-1]['body'])
              and 'c2lnbmF0dXJl' in json.dumps(requests[-1]['body']))
        check('all three API-key providers are browser-native capabilities', set(invoke('AgentCapabilities')['directBrowserProviders']) == {'openai', 'anthropic', 'google'})
        check('browser-only tools do not require a companion catalog', not any(t['name'].startswith('companion_') for t in invoke('AgentMcpTools')))

        # Exercise actual SDK wire terminals through the full WASM run loop. A complete-
        # looking function body in a truncated response must not execute or consume its ID.
        for provider_id, stream in [('openai', openai_stream), ('anthropic', anthropic_stream), ('google', google_stream)]:
            configure(provider_id, 'fullAccess')
            path = f'continued-{provider_id}.txt'
            bad = ('workspace_write', {'path': path, 'content': 'must not commit from a partial response', 'expectedVersion': 0})
            good = ('workspace_write', {'path': path, 'content': f'Recovered {provider_id}', 'expectedVersion': 0})
            terminal = f'{provider_id} completed after output exhaustion.'
            if provider_id == 'openai':
                replies.extend([stream(tool=bad, call_id='regenerate-openai', limited=True),
                                stream(tool=good, call_id='regenerate-openai'),
                                stream('The edit is ready; verifying completion.', finish=False), stream(terminal)])
            else:
                replies.extend([stream(tool=bad, limited=True), stream(tool=good),
                                stream(text='The edit is ready; verifying completion.', finish=False), stream(text=terminal)])
            send(f'Complete the {provider_id} workspace task even after a token limit and progress-only response.')
            expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text(terminal)
            completed = wait_idle()
            check(f'{provider_id} output exhaustion and text-only progress continue automatically',
                  completed['status'] == 'completed' and completed['run']['outputLimitCount'] == 1
                  and completed['run']['continuationCount'] >= 2 and not completed['run']['resumable'])
            recovered_file = invoke('AgentWorkspaceRead', path)
            writes = [event for event in completed['events'] if event['type'] == 'toolStarted'
                      and event.get('data', {}).get('name') == 'workspace_write'
                      and event['data'].get('arguments', {}).get('path') == path]
            check(f'{provider_id} truncated batch cannot mutate files or poison regenerated calls',
                  recovered_file['content'] == f'Recovered {provider_id}' and len(writes) == 1)
            expect(page.get_by_role('region', name='Agent run progress')).to_contain_text('Task completed')

        def set_mode(mode, criteria=''):
            page.get_by_role('tab', name='Conversation', exact=True).click()
            mode_menu = page.locator('details.agent-mode-menu')
            if not mode_menu.evaluate('(element) => element.open'):
                page.locator('.agent-mode-toggle').click()
            page.get_by_label('Agent execution mode', exact=True).select_option(mode)
            if mode == 'goal':
                page.get_by_label('Goal success criteria', exact=True).fill(criteria)
            apply = page.get_by_role('button', name='Apply mode', exact=True)
            if apply.is_enabled():
                apply.click()
            page.wait_for_function('mode => window.DotNet.invokeMethodAsync("' + ASSEMBLY + '","AgentPoll",0).then(state => state.run.mode.toLowerCase() === mode)', arg=mode)
            page.get_by_label('Message to coding agent', exact=True).click()
            expect(page.locator('.agent-mode-popover')).not_to_be_visible()

        configure('openai', 'fullAccess')
        set_mode('plan')
        planned_steps = [{'step': 'Create the reviewed workspace file', 'status': 'pending'},
                         {'step': 'Read back the saved file', 'status': 'pending'}]
        replies.extend([openai_stream(tool=('workspace_write', {'path': 'plan-denied.txt', 'content': 'must not commit', 'expectedVersion': 0}), call_id='plan-denied'),
                        openai_stream(tool=('shadow_state', {}), call_id='plan-inspect'),
                        openai_stream(tool=('agent_plan', {'steps': planned_steps}), call_id='plan-ready'),
                        openai_stream('The implementation plan is ready for review.')])
        send('Investigate and prepare a concrete plan without editing the workspace.')
        expect(page.locator('.agent-run-summary')).to_contain_text('Plan ready')
        page.locator('.agent-run-summary').click()
        expect(page.get_by_role('button', name='Execute plan', exact=True)).to_be_visible()
        planned = wait_idle()
        check('Plan mode performs inspection and prepares a reviewable pending plan',
              planned['status'] == 'completed' and planned['run']['mode'].lower() == 'plan'
              and all(step['status'] == 'pending' for step in planned['plan']))
        check('Plan mode prevents invented write tools even with Full access',
              'plan-denied.txt' not in {file['path'] for file in invoke('AgentWorkspaceFiles')})
        expect(page.get_by_role('region', name='Agent run progress')).to_contain_text('Plan ready')

        replies.extend([openai_stream(tool=('workspace_write', {'path': 'executed-plan.txt', 'content': 'Reviewed plan executed', 'expectedVersion': 0}), call_id='plan-implementation'),
                        openai_stream(tool=('workspace_read', {'path': 'executed-plan.txt'}), call_id='plan-verification'),
                        openai_stream(tool=('agent_plan', {'steps': [{**step, 'status': 'completed'} for step in planned_steps]}), call_id='plan-completed'),
                        openai_stream('The reviewed plan has been implemented and verified.')])
        page.get_by_role('button', name='Execute plan', exact=True).click()
        expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text('The reviewed plan has been implemented and verified.')
        executed_plan = wait_idle()
        check('Execute plan resumes the existing task in Normal mode and finishes it',
              executed_plan['run']['mode'].lower() == 'normal' and executed_plan['status'] == 'completed'
              and invoke('AgentWorkspaceRead', 'executed-plan.txt')['content'] == 'Reviewed plan executed')
        page.get_by_label('Message to coding agent', exact=True).click()

        set_mode('goal', 'Create goal.txt and verify that its saved content is exactly verified.')
        goal_steps = [{'step': 'Create and verify goal.txt', 'status': 'in_progress'}]
        replies.extend([openai_stream(tool=('agent_plan', {'steps': goal_steps}), call_id='goal-start'),
                        openai_stream('Premature completion must be refused.'),
                        openai_stream(tool=('workspace_write', {'path': 'goal.txt', 'content': 'verified', 'expectedVersion': 0}), call_id='goal-write'),
                        openai_stream(tool=('workspace_read', {'path': 'goal.txt'}), call_id='goal-read'),
                        openai_stream(tool=('agent_plan', {'steps': [{**step, 'status': 'completed'} for step in goal_steps]}), call_id='goal-plan-complete'),
                        openai_stream(tool=('agent_finish', {'outcome': 'completed', 'summary': 'Goal achieved and file verified.',
                            'verification': ['workspace_read returned goal.txt with content exactly verified.']}), call_id='goal-finish')])
        send('Achieve the saved-file goal and verify the success criteria.')
        expect(page.get_by_role('region', name='Agent run progress')).to_contain_text('Goal completed')
        achieved = wait_idle()
        check('Goal mode rejects premature completion and records a finished plan with verification',
              achieved['status'] == 'completed' and achieved['run']['verification']
              and all(step['status'] == 'completed' for step in achieved['plan'])
              and invoke('AgentWorkspaceRead', 'goal.txt')['content'] == 'verified')
        saved_run = json.loads(invoke('AgentExport'))['run']
        check('Task mode, progress and verification survive a browser checkpoint',
              saved_run['mode'].lower() == 'goal' and saved_run['phase'] == 'completed'
              and saved_run['verification'] == achieved['run']['verification'])
        verify_compact_chat_layout(page, check, OUTPUT, tag)
        set_mode('normal')

        runtime = call('workspace_runtime')
        check('browser coding runtime is discoverable through MCP', runtime['runtime'] == 'isolated-javascript-worker')
        invoke('AgentWorkspaceApply', [{'path': 'input.json', 'content': '[2,3,5]', 'expectedVersion': 0},
             {'path': 'sum.js', 'content': "const values = JSON.parse(workspace.read('input.json')); const total = values.reduce((a,b) => a+b, 0); assert(args[0] === 'fixture'); workspace.write('result.json', JSON.stringify({total})); console.log('Total:', total); return {total};", 'expectedVersion': 0}])
        executed = call('workspace_run_javascript', {'path': 'sum.js', 'arguments': ['fixture'], 'timeoutSeconds': 10})
        check('browser worker executes source, arguments and assertions', executed['exitCode'] == 0 and executed['result']['total'] == 10 and executed['standardOutput'] == 'Total: 10\n')
        check('successful browser code commits versioned workspace files', json.loads(invoke('AgentWorkspaceRead', 'result.json')['content']) == {'total': 10})
        failed = call('workspace_run_javascript', {'code': "workspace.write('result.json', 'must not commit'); throw new Error('test failure');", 'timeoutSeconds': 10}, error=True)
        check('browser code failure discards all staged edits', failed['isError'] and json.loads(invoke('AgentWorkspaceRead', 'result.json')['content']) == {'total': 10})
        forged = call('workspace_run_javascript', {'code': "self.postMessage({type:'completed',result:{exitCode:0,standardOutput:'forged',standardError:'',truncated:false,timedOut:false,elapsedMilliseconds:0,result:null,changes:[{path:'forged.txt',content:'must not commit'}]}}); throw new Error('expected after forged receipt');", 'timeoutSeconds': 10}, error=True)
        check('code cannot forge a successful worker receipt', forged['isError'] and all(file['path'] != 'forged.txt' for file in invoke('AgentWorkspaceFiles')))
        timed_out = call('workspace_run_javascript', {'code': "console.log('Before loop'); workspace.write('result.json', 'must not commit'); while (true) {}", 'timeoutSeconds': 1}, error=True)
        check('infinite browser code terminates without freezing Studio', timed_out['isError'] and json.loads(timed_out['text'])['timedOut'] and call('shadow_state')['data']['entities'] > 0)
        check('timed-out code cannot commit its staged edits', json.loads(invoke('AgentWorkspaceRead', 'result.json')['content']) == {'total': 10})
        isolated = call('workspace_run_javascript', {'code': "let inaccessible = false; try { inaccessible = typeof document === 'undefined' && typeof localStorage === 'undefined' && typeof shadowStudioAgentUi === 'undefined'; } catch {} assert(inaccessible); return { isolated: inaccessible, wasm: typeof WebAssembly === 'object' };", 'timeoutSeconds': 10})
        check('code worker has no Studio DOM, browser storage or provider objects', isolated['result'] == {'isolated': True, 'wasm': True})
        wasm = call('workspace_run_javascript', {'code': "const bytes = new Uint8Array([0,97,115,109,1,0,0,0,1,7,1,96,2,127,127,1,127,3,2,1,0,7,7,1,3,97,100,100,0,0,10,9,1,7,0,32,0,32,1,106,11]); const {instance} = await WebAssembly.instantiate(bytes); return instance.exports.add(7,8);", 'timeoutSeconds': 10})
        check('browser code can execute a real WebAssembly module', wasm['result'] == 15)
        check('worker frames are cleaned up after execution', page.locator('iframe[title="Isolated agent code"]').count() == 0)

        page.evaluate('''({assembly}) => {
          window.browserCodePending = DotNet.invokeMethodAsync(assembly, 'AgentMcpCall', 'workspace_run_javascript',
            {code: "workspace.write('cancelled.txt','discard'); while(true){}",timeoutSeconds:30})
            .then(result => ({result}), error => ({error:String(error)}));
        }''', {'assembly': ASSEMBLY})
        page.locator('iframe[title="Isolated agent code"]').wait_for(state='attached')
        invoke('AgentCancel')
        cancelled = page.evaluate('() => window.browserCodePending')
        check('agent cancellation stops an active browser worker and discards edits',
              (cancelled.get('error') or cancelled.get('result', {}).get('isError'))
              and all(file['path'] != 'cancelled.txt' for file in invoke('AgentWorkspaceFiles')))
        page.locator('iframe[title="Isolated agent code"]').wait_for(state='detached')
        wait_idle()
        # Direct interop cancellation completes before the browser client's next
        # authoritative poll. Wait for that rendered client state before asking
        # the UI to perform an operation guarded by AgentClient.ensureIdle().
        expect(page.get_by_role('complementary', name='Coding agent workspace')).to_have_attribute('aria-busy', 'false')

        page.get_by_role('tab', name='MCP', exact=True).click()
        check('browser MCP access requires an explicit UI grant', page.evaluate('() => typeof shadowStudioBrowserMcp === "undefined"'))
        page.get_by_role('button', name='Enable browser MCP', exact=True).click()
        page.wait_for_function('() => !!globalThis.shadowStudioBrowserMcp?.status().enabled')
        protocol = page.evaluate('''async () => {
          const connection = await shadowStudioBrowserMcp.createConnection(), port = connection.port;
          const pending = new Map();
          port.onmessage = event => { const callback = pending.get(event.data.id); if(callback) { pending.delete(event.data.id); callback(event.data); } };
          port.start();
          const send = message => new Promise(resolve => { pending.set(message.id,resolve); port.postMessage(message); });
          const initialized = await send({jsonrpc:'2.0',id:1,method:'initialize',params:{protocolVersion:'2025-11-25',capabilities:{},clientInfo:{name:'browser-verification',version:'1.0'}}});
          port.postMessage({jsonrpc:'2.0',method:'notifications/initialized'});
          const listed = await send({jsonrpc:'2.0',id:2,method:'tools/list',params:{}});
          const change = {jsonrpc:'2.0',id:3,method:'tools/call',params:{name:'workspace_write',arguments:{path:'browser-mcp.txt',content:'Official MCP over MessagePort',expectedVersion:0}}};
          const written = await send(change), duplicate = await send(change);
          const collision = await send({...change,params:{...change.params,arguments:{...change.params.arguments,content:'must not execute'}}});
          await connection.close(); port.close();
          return {initialized,listed,written,duplicate,collision};
        }''')
        check('official MCP initialization and tool discovery run over a browser MessagePort',
              'result' in protocol['initialized'] and any(tool['name'] == 'shadow_read' for tool in protocol['listed']['result']['tools']))
        check('browser MCP executes the same versioned tools without a server',
              not protocol['written']['result'].get('isError') and invoke('AgentWorkspaceRead', 'browser-mcp.txt')['content'] == 'Official MCP over MessagePort')
        check('duplicate browser MCP requests replay receipts without repeating writes', protocol['written'] == protocol['duplicate'] and 'error' in protocol['collision'])
        page.get_by_role('button', name='Disable browser MCP', exact=True).click()
        page.wait_for_function('() => typeof shadowStudioBrowserMcp === "undefined"')
        check('disabling browser MCP revokes the page capability', True)

        # Exercise real file-picker uploads and downloaded portable ZIP bytes.
        page.get_by_role('tab', name='Files', exact=True).click()
        page.get_by_text('Project import, export & folder access', exact=True).click()
        page.get_by_label('Import workspace text files', exact=True).set_input_files([
            {'name': 'uploaded.txt', 'mimeType': 'text/plain', 'buffer': b'Imported through browser picker\n'},
            {'name': 'check.js', 'mimeType': 'text/javascript', 'buffer': b"assert(workspace.read('uploaded.txt').includes('browser')); return 42;"}])
        expect(page.get_by_role('button', name='Apply selected files', exact=True)).to_be_enabled()
        page.get_by_role('button', name='Apply selected files', exact=True).click()
        expect(page.locator('.agent-alert')).to_contain_text('Imported 2 files')
        check('browser file import applies reviewed files atomically', invoke('AgentWorkspaceRead', 'uploaded.txt')['content'] == 'Imported through browser picker\n')
        with page.expect_download() as download:
            page.get_by_role('button', name='Download ZIP', exact=True).click()
        downloaded = Path(download.value.path()).read_bytes()
        import io, zipfile
        with zipfile.ZipFile(io.BytesIO(downloaded)) as archive:
            check('downloaded ZIP contains the complete real browser workspace', archive.read('uploaded.txt') == b'Imported through browser picker\n' and 'sum.js' in archive.namelist())
        compressed = io.BytesIO()
        with zipfile.ZipFile(compressed, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('zipped/nested.txt', 'Compressed UTF-8: zażółć\n')
        page.get_by_label('Import workspace ZIP or bundle', exact=True).set_input_files({'name': 'project.zip', 'mimeType': 'application/zip', 'buffer': compressed.getvalue()})
        expect(page.get_by_role('button', name='Apply selected files', exact=True)).to_be_enabled()
        page.get_by_role('button', name='Apply selected files', exact=True).click()
        expect(page.locator('.agent-alert')).to_contain_text('Imported 1 file')
        check('browser imports a compressed UTF-8 ZIP through the real picker', invoke('AgentWorkspaceRead', 'zipped/nested.txt')['content'] == 'Compressed UTF-8: zażółć\n')

        folder = verify_browser_folder(page)
        check('real browser folder handles read text files and skip generated directories',
              folder['realFileHandles'] and folder['importedTextFiles'] == 2 and folder['skippedGeneratedDirectories'] == 1)
        check('folder publishing rejects external changes before writing any selected file',
              folder['externalChangeRejected'] and folder['noPartialWritesBeforeValidation'] and folder['reviewDetectsExternalConflict'])
        check('reviewed browser folder changes persist through real writable file streams',
              folder['savedContents'] and sorted(folder['savedPaths']) == ['new.md', 'src/check.mjs'])

        recovery = verify_browser_recovery_storage(page)
        check('browser recovery migrates existing checkpoints without losing drafts or storing credentials',
              recovery['schemaVersion'] == 2 and recovery['migratedCheckpointUnchanged'] and recovery['credentialsAbsent'])
        check('saved-workspace listing and pruning read only compact metadata',
              recovery['metadataAllowlisted'] and recovery['fullCheckpointReadsForListAndSave'] == 0)
        check('browser recovery transactions preserve prior data after quota and deletion failures',
              recovery['failedSaveRolledBack'] and recovery['failedReplacementPreservedPrevious'] and recovery['failedDeleteRolledBack'])
        check('browser recovery enforces file-count and byte retention atomically',
              recovery['countRetention'] == 10 and recovery['byteRetention'] <= 64 * 1024 * 1024
              and recovery['metadataAndCheckpointsAgree'] and recovery['deleteRemovesBoth'])

        # Save an idle checkpoint, inspect real IndexedDB, reload and explicitly restore it.
        page.get_by_role('tab', name='Context', exact=True).click()
        page.get_by_role('button', name='Save checkpoint now', exact=True).click()
        expect(page.locator('.agent-storage-status')).to_contain_text('Saved on this device')
        stored = page.evaluate('''async () => await new Promise((resolve,reject) => {
          const request = indexedDB.open('netdxf-shadow-agents-v1');
          request.onerror = () => reject(request.error);
          request.onsuccess = () => { const database = request.result, transaction = database.transaction('sessions','readonly');
            const read = transaction.objectStore('sessions').getAll(); read.onsuccess = () => { resolve(read.result); database.close(); }; read.onerror = () => reject(read.error); };
        })''')
        check('IndexedDB retains a complete checkpoint without provider credentials', any('uploaded.txt' in item['checkpoint'] for item in stored) and KEY not in json.dumps(stored))
        if not companion:
            check('browser-only session never contacts a loopback companion', all(not url.startswith(('http://127.0.0.1:', 'http://localhost:')) or url.startswith(base + '/') for url in network))
        page.reload(); page.wait_for_function('document.documentElement.dataset.shadowReady === "true"')
        page.get_by_role('button', name='Agents', exact=True).click()
        page.get_by_role('tab', name='Context', exact=True).click()
        saved_select = page.get_by_label('Saved browser workspaces', exact=True)
        expect(saved_select.locator('option')).not_to_have_count(0)
        expect(page.get_by_role('button', name='Restore saved workspace', exact=True)).to_be_enabled()
        page.get_by_role('button', name='Restore saved workspace', exact=True).click()
        expect(page.locator('.agent-alert')).to_contain_text('workspace restored')
        check('reload recovery restores files and transcript from IndexedDB', invoke('AgentWorkspaceRead', 'uploaded.txt')['content'] == 'Imported through browser picker\n' and invoke('AgentPoll', 0)['messages'])
        page.get_by_role('tab', name='Settings', exact=True).click()
        expect(page.get_by_label('API key or companion access token', exact=True)).to_have_value('')
        check('reload never restores the provider credential', page.evaluate('() => !JSON.stringify({...localStorage,...sessionStorage}).includes(' + json.dumps(KEY) + ')'))

        # Optional regression coverage for users who explicitly attach native tools.
        if companion:
            configure('anthropic', 'fullAccess')
            page.get_by_role('tab', name='Settings', exact=True).click()
            page.get_by_text('Optional native tools & desktop MCP', exact=True).click()
            page.get_by_label('External MCP companion endpoint', exact=True).fill(companion)
            page.get_by_label('External MCP companion access token', exact=True).fill(BRIDGE_TOKEN)
            page.get_by_role('button', name='Connect local tools', exact=True).click()
            expect(page.locator('.agent-external-mcp .agent-auth-status')).to_contain_text('native tools')
            check('browser optionally attaches an authenticated native workspace', 'companion_workspace_read' in {t['name'] for t in invoke('AgentMcpTools')})
            native = call('companion_workspace_read', {'path': 'hello.cs'})
            check('browser reads optional companion source files', native['content'] == '// Native project\n')
            call('companion_workspace_write', {'path': f'{tag}.txt', 'content': 'Native browser write', 'expectedVersion': 0})
            check('browser commits optional versioned native file changes', (native_workspace / f'{tag}.txt').read_text() == 'Native browser write')
            executed = call('companion_workspace_execute', {'executable': 'dotnet', 'arguments': ['--version'], 'timeoutSeconds': 20})
            check('browser can execute the explicitly enabled native process tool', executed['exitCode'] == 0 and executed['standardOutput'].strip())
            raw_ui = page.evaluate('() => shadowStudioAgentUi.snapshot()')
            ui = read_tool_result(call, call('studio_ui_inspect'), forbidden=(KEY, BRIDGE_TOKEN))
            check('companion token is absent from checkpoint, public UI and browser storage',
                  BRIDGE_TOKEN not in invoke('AgentExport') and BRIDGE_TOKEN not in json.dumps(ui)
                  and BRIDGE_TOKEN not in json.dumps(raw_ui)
                  and page.evaluate('(key) => !JSON.stringify({...localStorage,...sessionStorage}).includes(key)', BRIDGE_TOKEN))

        page.get_by_role('tab', name='Conversation', exact=True).click()
        page.screenshot(path=str(OUTPUT / f'{tag}-conversation.png'), full_page=True)
        page.set_viewport_size({'width': 390, 'height': 844})
        check('mobile agent dock stays inside the viewport', page.locator('.agent-workspace').bounding_box()['width'] <= 391)
        page.screenshot(path=str(OUTPUT / f'{tag}-mobile.png'), full_page=True)
        check('agent flow produces no uncaught browser errors', not errors)
        check('all scripted provider responses were consumed', not replies)
    except Exception:
        page.screenshot(path=str(OUTPUT / f'{tag}-failure.png'), full_page=True)
        (OUTPUT / f'{tag}-diagnostic.json').write_text(json.dumps({'errors': errors,
            'providerRequests': [{**r, 'headers': {k: '[redacted]' if k.lower() in ('authorization', 'x-api-key', 'x-goog-api-key') else v for k, v in r['headers'].items()}} for r in requests],
            'bodyText': page.locator('body').inner_text()}, indent=2), encoding='utf-8')
        raise
    finally:
        context.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published', required=True, type=Path)
    parser.add_argument('--chromium', type=Path, help='Optional installed Chromium executable for local verification.')
    parser.add_argument('--with-companion', action='store_true', help='Also verify optional native file/process tools. Default verification has no companion.')
    parser.add_argument('--agent-host', type=Path, default=ROOT / 'shadow/samples/netDxf.Shadow.AgentHost/bin/Release/net8.0/netDxf.Shadow.AgentHost.dll',
                        help='Built native companion used only with --with-companion.')
    args = parser.parse_args(); OUTPUT.mkdir(parents=True, exist_ok=True)
    for pattern in ('*-failure.png', '*-diagnostic.json', 'verification.json'):
        for stale in OUTPUT.glob(pattern): stale.unlink()
    passed = []
    if args.with_companion and not args.agent_host.is_file():
        parser.error('Build shadow/samples/netDxf.Shadow.AgentHost in Release first, or supply --agent-host.')
    with static_host(args.published.resolve()) as (base, requests), \
            (companion_fixture(base, args.agent_host.resolve()) if args.with_companion else nullcontext((None, None, None, []))) as native, \
            sync_playwright() as playwright:
        options = {'headless': True, 'args': ['--disable-dev-shm-usage', '--disable-gpu', '--no-zygote']}
        if args.chromium:
            options['executable_path'] = str(args.chromium.resolve())
        browser = playwright.chromium.launch(**options)
        try:
            for mount in ('/', '/netDxf/'):
                verify(base, mount, browser, passed, *native)
            if any(path.startswith('/api/') for path in requests):
                raise AssertionError('The static server received a Studio API request.')
        finally:
            browser.close()
    (OUTPUT / 'verification.json').write_text(json.dumps({'passed': True, 'companionRequired': False, 'optionalCompanionVerified': args.with_companion, 'checks': passed}, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: {len(passed)} published browser agent checks; ' + ('optional native companion also verified.' if args.with_companion else 'no local companion or native process host.'))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc(); raise SystemExit(1)
