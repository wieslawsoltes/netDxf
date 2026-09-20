#!/usr/bin/env python3
"""Independent exact Cox-de Boor derivative and selected DXF geometry checks."""
from __future__ import annotations
from fractions import Fraction as F
from functools import lru_cache
import io
import json
import math
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

VERSIONS = {"AutoCad2000": "AC1015", "AutoCad2004": "AC1018", "AutoCad2007": "AC1021",
            "AutoCad2010": "AC1024", "AutoCad2013": "AC1027", "AutoCad2018": "AC1032"}
DEGREES = (1, 2, 3, 5, 10)
FRACTIONS = (0., .125, .5, .875, 1.)
ORDERS = (0, 1, 3, 10, 3)

def require(ok, why):
    if not ok:
        raise ValueError(why)

def bits(v):
    return struct.pack(">d", v).hex().upper()

def unbits(v):
    require(isinstance(v, str) and len(v) == 16, "Invalid binary64 field")
    return struct.unpack(">d", bytes.fromhex(v))[0]

def definition(p, form, variant):
    n = 2 * (p + 1) if form == 3 else p + 3
    scale = math.ldexp(1., (variant - 1) * 300)
    ks = math.ldexp(1., (variant - 1) * 20)
    ws = math.ldexp(1., (variant - 1) * 700)
    points = [((i * 7 % 11 - 5) * scale, ((i*i + 3) % 13 - 6) * scale, (i % 3 - 1) * scale) for i in range(n)]
    weights = [(1 + i % 4) * ws for i in range(n)]
    if form == 0:
        knots = [0.] * (p+1) + [.25, .75] + [1.] * (p+1)
    elif form == 1:
        knots = [float(i-p) for i in range(n+p+1)]
    elif form == 2:
        knots = [(i-p)/4. for i in range(n+2*p+1)]
    else:
        knots = [0.] * (p+1) + [.5] * (p+1) + [1.] * (p+1)
    return points, weights, [k*ks for k in knots]

def expand(points, weights, p, periodic):
    return (points[-p:] + points, weights[-p:] + weights) if periodic else (points, weights)

def derivatives(points, weights, knots, p, u, order, left=False):
    """Full-basis differentiation, not production's local homogeneous Taylor jet."""
    knots = tuple(map(F, knots)); u = F(u)
    n = len(points)
    spans = [i for i in range(p, n) if knots[i] < knots[i+1] and
             (knots[i] < u <= knots[i+1] if left or u == knots[n] else knots[i] <= u < knots[i+1])]
    require(len(spans) == 1, "Independent span ambiguity")
    span = spans[0]

    @lru_cache(None)
    def basis(i, degree, derivative):
        if derivative > degree:
            return F(0)
        if degree == 0:
            return F(int(i == span)) if derivative == 0 else F(0)
        lo = knots[i+degree]-knots[i]
        hi = knots[i+degree+1]-knots[i+1]
        if derivative:
            a = degree * basis(i, degree-1, derivative-1) / lo if lo else F(0)
            b = degree * basis(i+1, degree-1, derivative-1) / hi if hi else F(0)
            return a-b
        a = (u-knots[i])*basis(i, degree-1, 0)/lo if lo else F(0)
        b = (knots[i+degree+1]-u)*basis(i+1, degree-1, 0)/hi if hi else F(0)
        return a+b

    h = []
    for k in range(order+1):
        q = [F(weights[i])*basis(i, p, k) for i in range(n)]
        h.append([sum((q[i]*F(points[i][c]) for i in range(n)), F(0)) for c in range(3)] + [sum(q, F(0))])
    require(h[0][3] != 0, "Independent zero denominator")
    curve = []
    for k in range(order+1):
        curve.append([(h[k][c] - sum((math.comb(k, i)*h[i][3]*curve[k-i][c] for i in range(1, k+1)), F(0)))
                      / h[0][3] for c in range(3)])
    return [[bits(float(v)) for v in row] for row in curve]

@lru_cache(None)
def numerical_expected(p, form, variant, sample):
    points, weights, knots = definition(p, form, variant)
    points, weights = expand(points, weights, p, form == 2)
    f = FRACTIONS[sample]; u = (1-f)*knots[p] + f*knots[len(points)]
    return derivatives(points, weights, knots, p, u, ORDERS[sample])

def check_row(row, p, form, variant, sample):
    points, weights, knots = definition(p, form, variant)
    expanded, ew = expand(points, weights, p, form == 2)
    f = FRACTIONS[sample]; u = (1-f)*knots[p] + f*knots[len(expanded)]
    expected = {
        "id": f"{p}-{form}-{variant}-{sample}", "degree": p, "form": form, "variant": variant,
        "sample": sample, "order": ORDERS[sample], "parameter": bits(u),
        "controls": [[bits(v) for v in q] for q in points],
        "weights": list(map(bits, weights)), "knots": list(map(bits, knots))
    }
    require(set(row) == set(expected) | {"derivatives"}, "Numerical row schema")
    for key in expected:
        require(row[key] == expected[key], "Regenerated input differs: " + key)
    require(row["derivatives"] == numerical_expected(p, form, variant, sample), "Exact derivative bits differ")

def records(data):
    tags = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None))
    result = []; current = []
    for tag in tags:
        if tag.code == 0:
            if current and current[0][1] in ("SPLINE", "POINT"):
                result.append(current)
            current = []
        current.append((tag.code, tag.value))
    if current and current[0][1] in ("SPLINE", "POINT"):
        result.append(current)
    return result

def values(record, code):
    return [value for c, value in record if c == code]

@lru_cache(None)
def wire_point(form, fraction):
    points, weights, knots = definition(3, form, 1)
    points, weights = expand(points, weights, 3, form == 2)
    u = (1-fraction)*knots[3] + fraction*knots[len(points)]
    return derivatives(points, weights, knots, 3, u, 0)[0]

def check_records(actual, form):
    points, weights, knots = definition(3, form, 1)
    points, weights = expand(points, weights, 3, form == 2)
    require([r[0][1] for r in actual] == ["SPLINE"] + ["POINT"]*5, "Selected entity inventory")
    source = actual[0]
    require([float(v) for v in values(source, 71)] == [3], "Degree changed")
    for c, code in enumerate((10, 20, 30)):
        require([bits(float(v)) for v in values(source, code)] == [bits(q[c]) for q in points], "Control net changed")
    for code, expected in ((40, knots), (41, weights)):
        require([bits(float(v)) for v in values(source, code)] == list(map(bits, expected)), "Knots or weights changed")
    for f, record in zip(FRACTIONS, actual[1:]):
        u = (1-f)*knots[3] + f*knots[len(points)]
        target = wire_point(form, f)
        for c, code in enumerate((10, 20, 30)):
            require([bits(float(v)) for v in values(record, code)] == [target[c]], "Evaluated POINT differs")

def rejected(fn):
    try:
        fn()
    except (ValueError, OverflowError):
        return 1
    raise AssertionError("A corruption escaped the positive validator")

def main(directory):
    names = {f"spline-parameter-{v}-{b}-{f}.dxf" for v in VERSIONS for b in (False, True) for f in range(4)}
    names.add("spline-parameter-numerics.json")
    def inventory(actual):
        require(actual == names, "Missing or extra parameter fixtures")
    inventory({p.name for p in directory.glob("spline-parameter-*")})
    rejected(lambda: inventory(names - {"spline-parameter-numerics.json"}))
    rejected(lambda: inventory(names | {"spline-parameter-extra.dxf"}))
    rows = json.loads((directory/"spline-parameter-numerics.json").read_text())
    scenarios = [(p, f, v, j) for p in DEGREES for f in range(4) for v in range(3) for j in range(5)]
    require(len(rows) == len(scenarios), "Numerical corpus count")
    components = numerical_controls = 0
    for row, spec in zip(rows, scenarios):
        check_row(row, *spec)
        components += 3*len(row["derivatives"])
        for axis in range(3):
            damaged = dict(row); jet = [list(v) for v in row["derivatives"]]
            jet[-1][axis] = bits(math.nextafter(unbits(jet[-1][axis]), math.inf))
            damaged["derivatives"] = jet
            numerical_controls += rejected(lambda: check_row(damaged, *spec))
    drawings = corruptions = 0
    for version, code in VERSIONS.items():
        for binary in (False, True):
            for form in range(4):
                path = directory/f"spline-parameter-{version}-{binary}-{form}.dxf"
                data = path.read_bytes()
                require(data.startswith(b"AutoCAD Binary DXF") == binary, "Transport changed")
                actual = records(data); check_records(actual, form)
                for i, record in enumerate(actual):
                    codes = (10, 20, 30, 40, 41, 71) if i == 0 else (10, 20, 30)
                    for j, (tag, value) in enumerate(record):
                        if tag not in codes:
                            continue
                        for op in ("change", "missing", "duplicate"):
                            damaged = [list(r) for r in actual]
                            if op == "change":
                                damaged[i][j] = (tag, float(value) + .125)
                            elif op == "missing":
                                del damaged[i][j]
                            else:
                                damaged[i].insert(j, (tag, value))
                            corruptions += rejected(lambda: check_records(damaged, form))
                doc = ezdxf.readfile(path); require(doc.dxfversion == code, "DXF version differs")
                audit = doc.audit(); require(not audit.errors and not audit.fixes, "DXF graph errors or repairs")
                drawings += 1
    print(f"PASS: {len(rows)} regenerated exact derivative scenarios / {components} components; "
          f"{drawings} drawings; {numerical_controls} numerical and {corruptions} parsed-record corruptions rejected; "
          "two inventory controls; zero graph errors/repairs.")

if __name__ == "__main__":
    require(len(sys.argv) == 2, "Usage: verify_spline_parameters.py ARTIFACT_DIRECTORY")
    main(Path(sys.argv[1]))
