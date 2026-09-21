#!/usr/bin/env python3
"""Independently check transformed ELLIPSE parameters, planes and tiny sweeps."""
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from verify_raw_line_geometry import PROFILES, load_tags, require, reject, audit_signature

VERSIONS = {k: v for k, v in PROFILES.items() if k not in ('AutoCad12', 'AutoCad13', 'AutoCad14')}
CENTER = (13.0, 22.0, 29.0)
MAJOR = (0.0, 0.0, -6.0)
NORMAL = (1.0, 0.0, 0.0)


def parameters(kind):
    if kind == 0:
        return 0.0, math.tau
    if kind == 1:
        return 0.0, math.pi / 2
    # Independently convert the original untransformed polar endpoints.
    return tuple(math.atan2(math.sin(math.radians(a)), math.cos(math.radians(a)) / 3)
                 for a in (30.0, 30.0000000001))


def check(tags, kind):
    starts = [i for i, tag in enumerate(tags) if tag == (0, 'ELLIPSE')]
    require(len(starts) == 1, 'Selected ELLIPSE inventory')
    start = starts[0]
    end = next(i for i in range(start + 1, len(tags)) if tags[i][0] == 0)
    record = tags[start:end]
    fields = dict(zip((10, 20, 30), CENTER))
    fields.update(zip((11, 21, 31), MAJOR))
    fields.update(zip((210, 220, 230), NORMAL))
    fields.update({40: 1/3, 62: 3, 60: 1})
    first, last = parameters(kind)
    fields.update({41: first, 42: last})
    values = {}
    for code, wanted in fields.items():
        actual = [v for c, v in record if c == code]
        require(len(actual) == 1, f'Missing or repeated group {code}')
        value = actual[0]
        require(isinstance(value, (int, float)) and math.isfinite(value), f'Nonfinite group {code}')
        # A target OCS rotation can shift zero to 2*pi; compare parameter phase.
        error = math.remainder(value - wanted, math.tau) if code in (41, 42) else value - wanted
        require(abs(error) <= 3e-14, f'Incorrect group {code}: {value!r}, expected {wanted!r}')
        values[code] = value
    require(not any(c in (92, 160, 310) for c, _ in record), 'Stale proxy retained')
    span = values[42] - values[41]
    if kind == 0:
        require(abs(span - math.tau) <= 3e-14, 'Full ellipse lost its complete parameter span')
    else:
        span %= math.tau
        require(span > 0, 'Distinct arc endpoints collapsed')
        require(abs(span - (last - first)) <= 3e-14, 'Arc traversal changed')
        if kind == 2:
            require(span < 1e-10, 'Tiny arc became a long arc or a full ellipse')
    # Evaluate the physical packet, not values returned by the netDxf reader.
    major = tuple(values[c] for c in (11, 21, 31))
    n = tuple(values[c] for c in (210, 220, 230))
    norm = math.sqrt(sum(v*v for v in n))
    n = tuple(v/norm for v in n)
    cross = (n[1]*major[2] - n[2]*major[1], n[2]*major[0] - n[0]*major[2], n[0]*major[1] - n[1]*major[0])
    minor = tuple(v * values[40] for v in cross)
    for i in range(17):
        t = values[41] + span * i / 16
        source_t = first + (last - first) * i / 16
        actual = tuple(values[c] + major[j]*math.cos(t) + minor[j]*math.sin(t) for j, c in enumerate((10, 20, 30)))
        # Source (6*cos(t),2*sin(t),0) transformed by the explicit matrix.
        expected = (13.0, 22.0 + 2*math.sin(source_t), 29.0 - 6*math.cos(source_t))
        require(all(abs(a-b) <= 5e-12 for a, b in zip(actual, expected)), 'Independent WCS arc sample differs')
    return start, end, fields


def main(directory):
    cases = list(itertools.product(VERSIONS, (False, True), range(3)))
    names = {f'ellipse-affine-safety-{v}-{b}-{k}.dxf' for v, b, k in cases}
    def inventory(actual):
        require(actual == names, 'Missing or extra ellipse affine fixtures')
    inventory({p.name for p in directory.glob('ellipse-affine-safety-*.dxf')})
    reject(lambda: inventory(names - {next(iter(names))}))
    reject(lambda: inventory(names | {'ellipse-affine-safety-extra.dxf'}))
    corruptions = 0
    for version, binary, kind in cases:
        path = directory / f'ellipse-affine-safety-{version}-{binary}-{kind}.dxf'
        tags = load_tags(path)
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport changed')
        at = tags.index((9, '$ACADVER'))
        require(tags[at+1] == (1, VERSIONS[version]), 'Version changed')
        start, end, fields = check(tags, kind)
        for code in fields:
            at = next(i for i in range(start, end) if tags[i][0] == code)
            bad = list(tags); bad[at] = (code, float(tags[at][1]) + 1)
            for altered in (bad, tags[:at]+tags[at+1:], tags[:at]+[tags[at]]+tags[at:]):
                corruptions += reject(lambda: check(altered, kind))
        corruptions += reject(lambda: check(tags[:end]+[(92, 1), (310, b'x')]+tags[end:], kind))
        if kind == 2:
            for replacement in (0.0, math.tau):
                bad = [(c, replacement if c == 42 else v) if start <= i < end else (c, v)
                       for i, (c, v) in enumerate(tags)]
                corruptions += reject(lambda: check(bad, kind))
        doc = ezdxf.readfile(path)
        require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent graph errors or repairs')
        entities = list(doc.modelspace().query('ELLIPSE'))
        require(len(entities) == 1 and entities[0].proxy_graphic is None, 'Independent entity/cache inventory')
    print(f'PASS: {len(cases)} drawings / {len(cases)*17} independently derived WCS samples; '
          f'{corruptions} packet corruptions and two inventory controls rejected; zero graph errors or repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_ellipse_affine_safety.py ARTIFACT_DIRECTORY')
    main(Path(sys.argv[1]))
