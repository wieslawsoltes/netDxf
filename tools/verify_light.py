#!/usr/bin/env python3
"""Independently inspect the published LIGHT parameter set using ezdxf (development only)."""
import argparse
import math
from pathlib import Path
import ezdxf

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    expected = {f'light-AutoCad{v}-{b}-{k}.dxf' for v in (2007, 2010, 2013, 2018)
                for b in ('False', 'True') for k in (1, 2, 3)}
    paths = sorted(args.artifacts.glob('light-*.dxf'))
    if {p.name for p in paths} != expected:
        raise ValueError('Expected exactly 24 LIGHT profile/transport/type fixtures')
    for path in paths:
        doc = ezdxf.readfile(path)
        lights = list(doc.modelspace().query('LIGHT'))
        if len(lights) != 2:
            raise ValueError(f'{path}: missing source or clone')
        kind = int(path.stem.rsplit('-', 1)[1])
        fields = {'version': 0, 'name': 'Lamp Żółć', 'type': kind, 'status': 0, 'plot_glyph': 1,
                  'intensity': 1.2500000000000002, 'attenuation_type': kind - 1, 'use_attenuation_limits': 1,
                  'attenuation_start_limits': 0.125, 'attenuation_end_limits': 2048.0, 'hotspot_angle': 37.5,
                  'falloff_angle': 62.75, 'cast_shadows': 0, 'shadow_type': 1, 'shadow_map_size': 1024,
                  'shadow_map_softness': 7, 'color': 3}
        for light in lights:
            for name, value in fields.items():
                if getattr(light.dxf, name) != value:
                    raise ValueError(f'{path}: {name} changed: {getattr(light.dxf, name)!r}')
            if tuple(light.dxf.location) != (2, -3, 5) or tuple(light.dxf.target) != (-7, 11, 13):
                raise ValueError(f'{path}: LIGHT WCS geometry changed')
            if list(light.get_xdata('LIGHT_TEST'))[0].value != 'retained light data':
                raise ValueError(f'{path}: LIGHT XData changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following entity changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 24 files / 48 LIGHT entities; zero audit errors/repairs')

if __name__ == '__main__':
    main()
