"""Bounded JSON transport for the native-browser verification harness.

Only the transport is chunked. The browser reassembles and parses each complete
JSON document before executing the existing corpus and its expected digests.
"""
import json


def transfer_json_payloads(page, payloads, chunk_size=262144):
    """Transfer ASCII JSON chunks without one oversized DevTools message.

    ``page`` is the Playwright page or a test double implementing ``evaluate``.
    The caller owns the page and consumes/deletes ``netDxfJsonPayloads``.
    A failed transfer is discarded and its original exception is propagated.
    """
    if isinstance(chunk_size, bool) or not isinstance(chunk_size, int) or not 1 <= chunk_size <= 1048576:
        raise ValueError('JSON transport chunk size must be between 1 and 1048576 characters.')
    page.evaluate('globalThis.netDxfJsonPayloads = Object.create(null)')
    try:
        for name, value in payloads.items():
            if not isinstance(name, str):
                raise TypeError('Browser JSON payload names must be strings.')
            text = json.dumps(value, ensure_ascii=True)
            page.evaluate('([name,length]) => globalThis.netDxfJsonPayloads[name] = {length,chunks:[]}', [name, len(text)])
            for offset in range(0, len(text), chunk_size):
                page.evaluate('([name,text]) => globalThis.netDxfJsonPayloads[name].chunks.push(text)', [name, text[offset:offset + chunk_size]])
    except BaseException:
        try:
            page.evaluate('delete globalThis.netDxfJsonPayloads')
        except Exception:
            pass
        raise
