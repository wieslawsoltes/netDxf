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

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/blazor-agents'
ASSEMBLY = 'netDxf.Shadow.Studio.Browser'
KEY = 'verification-only-credential-never-use-for-inference'
BRIDGE_TOKEN = 'verification-only-loopback-companion-token'


def openai_stream(text='Complete.', tool=None, call_id='call_browser'):
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
                   {'type': 'response.completed', 'sequence_number': 4, 'response': {
                       **response, 'status': 'completed', 'store': False, 'output': [item],
                       'usage': {'input_tokens': 200, 'output_tokens': 30, 'total_tokens': 230}}}])
    return ''.join('data: ' + json.dumps(value) + '\n\n' for value in values)


def anthropic_stream(tool=None):
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
            {'type': 'tool_use', 'id': 'anthropic_browser_call', 'name': tool[0], 'input': {}} if tool else {'type': 'text', 'text': ''}},
        {'type': 'content_block_delta', 'index': block_index, 'delta':
            {'type': 'input_json_delta', 'partial_json': json.dumps(tool[1])} if tool else {'type': 'text_delta', 'text': 'Anthropic browser SDK complete.'}},
        {'type': 'content_block_stop', 'index': block_index},
        {'type': 'message_delta', 'delta': {'stop_reason': 'tool_use' if tool else 'end_turn', 'stop_sequence': None}, 'usage': {'output_tokens': 10}},
        {'type': 'message_stop'},
    ]
    return ''.join('event: ' + value['type'] + '\ndata: ' + json.dumps(value) + '\n\n' for value in values)


def google_stream(tool=None):
    return 'data: ' + json.dumps({'candidates': [{'index': 0, 'content': {'role': 'model', 'parts': [
        {'functionCall': {'name': tool[0], 'args': tool[1]}, 'thoughtSignature': 'c2lnbmF0dXJl'} if tool else
        {'text': 'Google browser SDK complete.'}]}, 'finishReason': 'STOP'}],
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
    errors, requests, replies, network = [], [], deque(), []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('request', lambda request: network.append(request.url))
    tag = 'root' if mount == '/' else 'subpath'

    def check(name, value=True):
        if not value:
            raise AssertionError(name)
        passed.append(tag + ': ' + name); print('PASS:', passed[-1], flush=True)

    def provider(route):
        headers = {'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*',
                   'Access-Control-Allow-Methods': 'POST,OPTIONS'}
        if route.request.method == 'OPTIONS':
            route.fulfill(status=204, headers=headers); return
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
        ui = call('studio_ui_inspect')
        check('semantic Studio inspection omits credential controls', KEY not in json.dumps(ui) and 'availableActions' in ui)
        routes = call('studio_routes')
        check('complete Studio route and request schema discovery', len(routes['routes']) >= 64 and len(routes['requestTypes']) > 20)
        document_id = page.locator('#studio').get_attribute('data-document')
        check('read-only Studio dispatcher reaches real CAD state', 'state' in call('studio_read', {'path': f'/api/cad/{document_id}/state'}))

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
            check('companion token is absent from checkpoint, public UI and browser storage',
                  BRIDGE_TOKEN not in invoke('AgentExport') and BRIDGE_TOKEN not in json.dumps(call('studio_ui_inspect'))
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
