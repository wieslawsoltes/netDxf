#!/usr/bin/env python3
"""Run the real official .NET SDKs, agent loop, CAD tools and UI in published WASM.

Only paid provider HTTP responses are replaced with deterministic wire fixtures. The
Studio, source model, immutable snapshots, transactions and agent runtime are real.
No API key, OAuth account, CAD server or development host is required.
"""
from __future__ import annotations
import argparse
from collections import deque
import json
from pathlib import Path
import time
import traceback
from playwright.sync_api import sync_playwright, expect
from blazor_studio_browser import static_host

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/blazor-agents'
ASSEMBLY = 'netDxf.Shadow.Studio.Browser'
KEY = 'verification-only-credential-never-use-for-inference'


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


def anthropic_stream():
    values = [
        {'type': 'message_start', 'message': {'id': 'msg_browser', 'type': 'message', 'role': 'assistant',
         'model': 'verification-model', 'content': [], 'stop_reason': None, 'stop_sequence': None,
         'usage': {'input_tokens': 200, 'output_tokens': 1}}},
        {'type': 'content_block_start', 'index': 0, 'content_block': {'type': 'text', 'text': ''}},
        {'type': 'content_block_delta', 'index': 0, 'delta': {'type': 'text_delta', 'text': 'Anthropic browser SDK complete.'}},
        {'type': 'content_block_stop', 'index': 0},
        {'type': 'message_delta', 'delta': {'stop_reason': 'end_turn', 'stop_sequence': None}, 'usage': {'output_tokens': 10}},
        {'type': 'message_stop'},
    ]
    return ''.join('event: ' + value['type'] + '\ndata: ' + json.dumps(value) + '\n\n' for value in values)


def google_stream():
    return 'data: ' + json.dumps({'candidates': [{'index': 0, 'content': {'role': 'model', 'parts': [
        {'text': 'Google browser SDK complete.'}]}, 'finishReason': 'STOP'}],
        'modelVersion': 'verification-model', 'responseId': 'google_browser',
        'usageMetadata': {'promptTokenCount': 200, 'candidatesTokenCount': 10, 'totalTokenCount': 210}}) + '\n\n'


def verify(base, mount, browser, passed):
    context = browser.new_context(viewport={'width': 1600, 'height': 1000}, accept_downloads=True)
    page = context.new_page(); page.set_default_timeout(30000)
    errors, requests, replies = [], [], deque()
    page.on('pageerror', lambda error: errors.append(str(error)))
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

    def configure(provider_id='openai', permission='fullAccess'):
        page.get_by_role('tab', name='Settings', exact=True).click()
        page.get_by_label('Model provider', exact=True).select_option(provider_id)
        page.get_by_label('Model ID', exact=True).fill('verification-model')
        page.get_by_label('API key or companion access token', exact=True).fill(KEY)
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

        for provider_id, stream, expected_text in [('google', google_stream(), 'Google browser SDK complete.'),
                                                    ('anthropic', anthropic_stream(), 'Anthropic browser SDK complete.')]:
            configure(provider_id, 'readOnly')
            replies.append(stream); send('Verify this provider uses the browser agent session.')
            expect(page.get_by_role('log', name='Agent conversation and tool activity')).to_contain_text(expected_text)
            check('official ' + provider_id + ' SDK streams in real WebAssembly', wait_idle()['status'] == 'completed')

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
    args = parser.parse_args(); OUTPUT.mkdir(parents=True, exist_ok=True)
    passed = []
    with static_host(args.published.resolve()) as (base, requests), sync_playwright() as playwright:
        options = {'headless': True, 'args': ['--disable-dev-shm-usage', '--disable-gpu', '--no-zygote']}
        if args.chromium:
            options['executable_path'] = str(args.chromium.resolve())
        browser = playwright.chromium.launch(**options)
        try:
            for mount in ('/', '/netDxf/'):
                verify(base, mount, browser, passed)
            if any(path.startswith('/api/') for path in requests):
                raise AssertionError('The static server received a Studio API request.')
        finally:
            browser.close()
    (OUTPUT / 'verification.json').write_text(json.dumps({'passed': True, 'checks': passed}, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: {len(passed)} published browser agent checks; static server only.')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc(); raise SystemExit(1)
