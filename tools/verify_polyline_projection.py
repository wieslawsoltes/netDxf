#!/usr/bin/env python3
"""Verify Polyline3D projections, explicit planes and retained ordinary metadata."""
import itertools
from pathlib import Path
import sys
from curve_projection_oracle import PROFILES, CONTROLS, require, rejected, packet, lw_expected, check_packet, corruptions, audit


def main():
    directory=Path(sys.argv[1])
    cases={f'polyline-projection-{n}-{closed}-{plane}-{version}-{binary}.dxf':(n,closed,plane,version,binary)
           for n,closed,plane,version,binary in itertools.product(range(6),(False,True),range(3),PROFILES,(False,True))}
    expected_inventory=set(cases); actual={p.name for p in directory.glob('polyline-projection-*')}
    check_inventory=lambda value:require(value==expected_inventory,'Projection fixture inventory differs')
    check_inventory(actual)
    inventory_controls=rejected(check_inventory,actual-{next(iter(actual))})+rejected(check_inventory,actual|{'extra.dxf'})
    negative=0
    for name,(n,closed,plane,version,binary) in cases.items():
        path=directory/name;actual=packet(path.read_bytes())
        expected=lw_expected(CONTROLS,n,closed,(0.,-7.5,12.)[plane],version,True)
        check=lambda value:check_packet(value,expected)
        check(actual);negative+=corruptions(actual,check);audit(path,version,binary)
    print(f'PASS: {len(cases)} projection drawings / {len(cases)*5} OCS vertices; {negative} packet and '
          f'{inventory_controls} inventory corruptions rejected; zero graph errors/repairs')


if __name__=='__main__':main()
