"""Reviewed package inventory shared by packaging, consumers and release validation.

The catalog is deliberately explicit: adding a new library or runtime dependency
must update the release contract, rather than silently extending publication.
"""
from __future__ import annotations

from pathlib import Path
from typing import NamedTuple
import xml.etree.ElementTree as ET


class Package(NamedTuple):
    id: str
    project: str
    frameworks: tuple[str, ...]
    internal: tuple[str, ...] = ()
    external: tuple[tuple[str, str], ...] = ()

    @property
    def assembly(self) -> str:
        return self.id


CORE = 'netDxf.netstandard'
CORE_FRAMEWORKS = ('netstandard2.0', 'net471', 'net48', 'net6.0', 'net8.0')


def shadow(name: str, internal: tuple[str, ...] = (),
           external: tuple[tuple[str, str], ...] = ()) -> Package:
    return Package(name, f'shadow/src/{name}/{name}.csproj', ('net8.0',), internal, external)


# Dependency order also defines packing and deterministic manifest order.
PACKAGES = (
    Package(CORE, 'netDxf/netDxf.csproj', CORE_FRAMEWORKS),
    shadow('netDxf.Shadow'),
    shadow('netDxf.Shadow.NetDxf', ('netDxf.Shadow', CORE)),
    shadow('netDxf.Shadow.Geometry', ('netDxf.Shadow',)),
    shadow('netDxf.Shadow.Rendering', ('netDxf.Shadow.Geometry',)),
    shadow('netDxf.Shadow.Graph', ('netDxf.Shadow.Geometry',)),
    shadow('netDxf.Shadow.Editor', ('netDxf.Shadow.Rendering',)),
    shadow('netDxf.Shadow.Dimensions', ('netDxf.Shadow.NetDxf', 'netDxf.Shadow.Geometry')),
    shadow('netDxf.Shadow.Drafting', ('netDxf.Shadow.Editor',)),
    shadow('netDxf.Shadow.Rendering.Shx', ('netDxf.Shadow.Rendering',)),
    shadow('netDxf.Shadow.Rendering.SkiaSharp', ('netDxf.Shadow.Rendering',), (
        ('SkiaSharp', '3.119.4'), ('SkiaSharp.NativeAssets.Linux', '3.119.4'),
        ('SkiaSharp.HarfBuzz', '3.119.1'), ('HarfBuzzSharp', '8.3.1.5'),
        ('HarfBuzzSharp.NativeAssets.Linux', '8.3.1.5'))),
    shadow('netDxf.Shadow.Exchange.Dwg', (CORE,), (('ACadSharp', '3.8.0'),)),
    shadow('netDxf.Shadow.Mcp', ('netDxf.Shadow.NetDxf', 'netDxf.Shadow.Editor'),
           (('ModelContextProtocol.Core', '2.2.0'),)),
    shadow('netDxf.Shadow.Agents', ('netDxf.Shadow.Mcp',), (
        ('OpenAI', '2.14.0'), ('Anthropic', '12.58.0'), ('Google.GenAI', '1.26.1'),
        ('Microsoft.Extensions.AI.OpenAI', '10.11.0'), ('Microsoft.Extensions.AI.Abstractions', '10.11.0'))),
)
BY_ID = {package.id: package for package in PACKAGES}


def manifest(selected: str) -> list[dict]:
    return [{'id': item.id, 'project': item.project, 'assembly': item.assembly,
             'frameworks': list(item.frameworks),
             'dependencies': dict([(name, selected) for name in item.internal] + list(item.external))}
            for item in PACKAGES]


def validate_source(root: Path) -> None:
    """Reject omitted libraries, unreviewed dependencies or stale project mappings."""
    projects = {item.project for item in PACKAGES}
    discovered = {'netDxf/netDxf.csproj'} | {
        path.relative_to(root).as_posix() for path in (root / 'shadow/src').glob('*/*.csproj')}
    if projects != discovered or len(BY_ID) != len(PACKAGES):
        raise ValueError('Source libraries differ from the reviewed package catalog')
    by_project = {(root / item.project).resolve(): item.id for item in PACKAGES}
    seen = set()
    for item in PACKAGES:
        path = root / item.project
        project = ET.parse(path)
        actual_id = project.findtext('./PropertyGroup/PackageId') or path.stem
        actual_assembly = project.findtext('./PropertyGroup/AssemblyName') or path.stem
        if actual_id != item.id or actual_assembly != item.assembly:
            raise ValueError('Project package/assembly differs from catalog: ' + item.id)
        internal = []
        for reference in project.findall('./ItemGroup/ProjectReference'):
            name = by_project.get((path.parent / reference.attrib['Include']).resolve())
            if name is None:
                raise ValueError('Uncatalogued package project dependency: ' + item.id)
            internal.append(name)
        external = []
        for reference in project.findall('./ItemGroup/PackageReference'):
            if (reference.get('PrivateAssets') or reference.findtext('PrivateAssets') or '').lower() == 'all':
                continue
            external.append((reference.attrib['Include'], reference.get('Version') or reference.findtext('Version')))
        if (len(internal) != len(set(internal)) or set(internal) != set(item.internal) or
                len(external) != len(set(external)) or set(external) != set(item.external)):
            raise ValueError('Source dependencies differ from package catalog: ' + item.id)
        if not set(internal) <= seen:
            raise ValueError('Package catalog is not in dependency order: ' + item.id)
        seen.add(item.id)
