"""Development-only independent mathematical audit using the installed MPFR library.

The subject is HighPrecisionMath, not the production reference-math backend.
The separately required .NET audit owns production compatibility.

Finite values and signed zeros are compared bit-for-bit at two reference precisions.
NaN classes (not payloads/signs) are compared here: the separate .NET math gate owns
NaN bit compatibility. This mathematical check never overrides a .NET mismatch.
No library is downloaded, installed, bundled, or loaded by production JavaScript.
"""
from __future__ import annotations
import ctypes
import ctypes.util
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import traceback

ROOT = Path(__file__).resolve().parent.parent
CONFIGURATION = os.environ.get('CONFIGURATION', 'Release')
NODE = os.environ.get('NODE', 'node')

class MpfrValue(ctypes.Structure):
    _fields_ = [('precision', ctypes.c_long), ('sign', ctypes.c_int),
                ('exponent', ctypes.c_long), ('limbs', ctypes.POINTER(ctypes.c_ulong))]

def node_json(program: str):
    return json.loads(subprocess.check_output([NODE, '--input-type=module', '-e', program],
                                            cwd=ROOT, text=True, timeout=120))

def fingerprints():
    return node_json("import {runtimeFingerprint,verificationFingerprint} from './tools/evidence.mjs';"
                     "console.log(JSON.stringify({runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()}));")

def bits(value: float) -> str:
    return struct.pack('>d', value).hex().upper()

report = {'configuration': CONFIGURATION, 'subject': 'HighPrecisionMath development reference', 'completed': False, 'fatal': None,
          'comparison': 'finite IEEE-754 bits and signed zero; nonfinite classes; .NET NaN payload/sign remains a separate exact gate',
          'precisions': [512, 1024], 'stats': {'comparisons': 0, 'finiteResults': 0, 'nanClassResults': 0, 'infiniteResults': 0, 'failures': 0},
          'failures': [], 'precisionDisagreements': []}
try:
    proof = fingerprints()
    report.update(proof)
    baseline = json.loads((ROOT/'baseline.json').read_text(encoding='utf-8'))
    report['sourceRef'] = baseline['ref']
    library = os.environ.get('MPFR_LIBRARY') or ctypes.util.find_library('mpfr')
    if not library:
        raise RuntimeError('Install a development MPFR shared library or set MPFR_LIBRARY; no fallback expected values are used.')
    lib = ctypes.CDLL(library)
    pointer = ctypes.POINTER(MpfrValue)
    lib.mpfr_get_version.restype = ctypes.c_char_p
    report['reference'] = {'name': 'MPFR', 'version': lib.mpfr_get_version().decode('ascii'), 'library': library}
    if Path('/proc/self/maps').is_file():
        for line in Path('/proc/self/maps').read_text().splitlines():
            filename = line.split()[-1]
            if '/libmpfr.so' in filename and Path(filename).is_file():
                report['reference']['librarySha256'] = hashlib.sha256(Path(filename).read_bytes()).hexdigest()
                break
    lib.mpfr_init2.argtypes = [pointer, ctypes.c_long]
    lib.mpfr_clear.argtypes = [pointer]
    lib.mpfr_set_d.argtypes = [pointer, ctypes.c_double, ctypes.c_int]
    lib.mpfr_get_d.argtypes = [pointer, ctypes.c_int]
    lib.mpfr_get_d.restype = ctypes.c_double
    functions = {}
    for name in ['sin', 'cos', 'tan', 'asin', 'acos', 'atan', 'atan2', 'fmod']:
        fn = getattr(lib, 'mpfr_'+name)
        fn.argtypes = [pointer, pointer, pointer, ctypes.c_int] if name in ['atan2', 'fmod'] else [pointer, pointer, ctypes.c_int]
        functions[name] = fn
    data = node_json("import {mathCorpus} from './tools/math-corpus.mjs';import {highPrecisionMathCall} from './tools/math-wire.mjs';"
                     "const requests=mathCorpus();console.log(JSON.stringify({requests,actual:highPrecisionMathCall(requests)}));")
    if len(data['requests']) != len(data['actual']) or not data['requests']:
        raise RuntimeError('Incomplete independent math corpus')
    if len({r['id'] for r in data['requests']}) != len(data['requests']):
        raise RuntimeError('Duplicate independent math case identity')
    references = []
    for precision in report['precisions']:
        a, b, output = MpfrValue(), MpfrValue(), MpfrValue()
        values = (a, b, output)
        for value in values:
            lib.mpfr_init2(ctypes.byref(value), precision)
        expected = []
        try:
            for request in data['requests']:
                args = [struct.unpack('>d', bytes.fromhex(h))[0] for h in request['args']]
                name = 'fmod' if request['member'] == 'Remainder' else request['member'].lower()
                lib.mpfr_set_d(ctypes.byref(a), args[0], 0)
                if len(args) == 2:
                    lib.mpfr_set_d(ctypes.byref(b), args[1], 0)
                    functions[name](ctypes.byref(output), ctypes.byref(a), ctypes.byref(b), 0)
                else:
                    functions[name](ctypes.byref(output), ctypes.byref(a), 0)
                expected.append(lib.mpfr_get_d(ctypes.byref(output), 0))
        finally:
            for value in values:
                lib.mpfr_clear(ctypes.byref(value))
        references.append(expected)
    for request, actual, low, high in zip(data['requests'], data['actual'], *references):
        if request['id'] != actual['id']:
            raise RuntimeError('Independent math case identity mismatch')
        result = struct.unpack('>d', bytes.fromhex(actual['result']))[0]
        report['stats']['comparisons'] += 1
        if not (math.isnan(low) and math.isnan(high)) and bits(low) != bits(high):
            report['precisionDisagreements'].append({'request': request, 'lower': bits(low), 'higher': bits(high)})
        if math.isnan(high):
            report['stats']['nanClassResults'] += 1
            passed = math.isnan(result)
        else:
            report['stats']['finiteResults' if math.isfinite(high) else 'infiniteResults'] += 1
            passed = bits(high) == actual['result']
        if not passed:
            report['stats']['failures'] += 1
            report['failures'].append({'request': request, 'expected': bits(high), 'actual': actual['result']})
    report['completed'] = not report['precisionDisagreements']
    if report['precisionDisagreements']:
        report['fatal'] = 'Reference precisions disagree; mathematical rounding is not qualified.'
    if fingerprints() != proof:
        raise RuntimeError('Executable code changed during independent math qualification')
except Exception:
    report['completed'] = False
    report['fatal'] = traceback.format_exc()
finally:
    path = ROOT/'artifacts/math-independent'/CONFIGURATION/'results.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ['completed', 'stats', 'precisionDisagreements', 'fatal']}))
if not report['completed'] or report['fatal'] or report['failures'] or report['precisionDisagreements']:
    raise SystemExit(1)
