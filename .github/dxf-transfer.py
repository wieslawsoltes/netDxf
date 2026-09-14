"""Ephemeral transfer decoder: no source is applied without exact SHA-256 validation."""
import base64, gzip, itertools, json, os, subprocess, zlib
from pathlib import Path
alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/='

def repair(text, expected_crc, size):
    def matches(candidate):
        return len(candidate) == size and zlib.crc32(candidate.encode('ascii')) == expected_crc
    if matches(text):
        return text
    candidates = set()
    delta = len(text) - size
    if 1 <= delta <= 4:
        for i in range(len(text) - delta + 1):
            candidate = text[:i] + text[i + delta:]
            if matches(candidate): candidates.add(candidate)
    elif delta == 0:
        for i in range(len(text)):
            for char in alphabet:
                candidate = text[:i] + char + text[i + 1:]
                if matches(candidate): candidates.add(candidate)
    elif -2 <= delta <= -1:
        for i in range(len(text) + 1):
            for chars in itertools.product(alphabet, repeat=-delta):
                candidate = text[:i] + ''.join(chars) + text[i:]
                if matches(candidate): candidates.add(candidate)
    if len(candidates) != 1:
        raise ValueError('Transport chunk cannot be uniquely recovered: ' + str(expected_crc))
    return candidates.pop()

if __name__ == '__main__':
    import hashlib
    payload = json.loads(Path(os.environ['BLOB_JSON']).read_text())
    text = base64.b64decode(payload['content']).decode('utf-8')
    Path(os.environ['RUNNER_TEMP'], 'staged-envelope.txt').write_text(text)
    overrides = json.loads(os.environ.get('ROW_OVERRIDES', '{}'))
    lines = text.splitlines()
    size = int(os.environ['ENCODED_LENGTH'])
    assert len(lines) == (size + 127) // 128
    blocks = []
    for i, line in enumerate(lines):
        number, crc, block = line.split(maxsplit=2)
        assert int(number) == i
        block = overrides.get(str(i), block)
        blocks.append(repair(block, int(crc, 16), min(128, size - 128 * i)))
    encoded = ''.join(blocks)
    data = gzip.decompress(base64.b64decode(encoded, validate=True))
    assert hashlib.sha256(data).hexdigest() == os.environ['PATCH_SHA256']
    subprocess.run(['git', 'apply', '--index', '-'], input=data, check=True)
