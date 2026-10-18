"""Installed-only Shadow package execution and source-bound release receipts."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

import package_catalog
import pipeline

RECEIPT = 'shadow-smoke.json'
LOG = 'shadow-smoke.log'
SCENARIOS = ('capture-sync', 'geometry-topology', 'editor-drafting', 'dimension-projection',
             'shx-program', 'vector-render', 'native-text-raster', 'dwg-exchange', 'mcp-tools', 'agent-workspace')
FIELDS = {'schema', 'passed', 'version', 'commit', 'tree', 'host', 'runtime_version',
          'scenarios', 'assemblies', 'packages', 'log_sha256'}


def source_mapping(path: Path, candidate: Path) -> None:
    root = ET.Element('configuration')
    sources = ET.SubElement(root, 'packageSources'); ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='candidate', value=str(candidate.resolve()))
    ET.SubElement(sources, 'add', key='public', value='https://api.nuget.org/v3/index.json')
    fallbacks = ET.SubElement(root, 'fallbackPackageFolders'); ET.SubElement(fallbacks, 'clear')
    mappings = ET.SubElement(root, 'packageSourceMapping')
    local = ET.SubElement(mappings, 'packageSource', key='candidate')
    # Exact identities outrank the public wildcard. Internal packages can never
    # restore from nuget.org, even if a newer or identically versioned package exists.
    for item in package_catalog.PACKAGES:
        ET.SubElement(local, 'package', pattern=item.id)
    public = ET.SubElement(mappings, 'packageSource', key='public')
    ET.SubElement(public, 'package', pattern='*')
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)


def package_hashes(directory: Path, selected: str) -> tuple[list[dict], dict[str, str]]:
    packages, assemblies = [], {}
    for item in package_catalog.PACKAGES:
        path = directory / f'{item.id}.{selected}.nupkg'
        pipeline.require(path.is_file() and not path.is_symlink(), 'Missing/linked Shadow package input')
        packages.append({'id': item.id, 'sha256': pipeline.digest(path)})
        with zipfile.ZipFile(path) as archive:
            assemblies[item.id] = hashlib.sha256(archive.read(f'lib/net8.0/{item.assembly}.dll')).hexdigest()
    return packages, assemblies


def validate(data: dict, selected: str, source: dict, packages: list[dict], assemblies: dict[str, str]) -> None:
    pipeline.require(set(data) == FIELDS and data['schema'] == 1 and data['passed'] is True,
                     'Incomplete/failed Shadow consumer receipt')
    pipeline.require(data['version'] == selected and all(data.get(key) == value for key, value in source.items()),
                     'Shadow consumer source/version mismatch')
    pipeline.require(data['host'] in ('Linux', 'Windows', 'Darwin') and
                     isinstance(data['runtime_version'], str) and data['runtime_version'].startswith('8.0.'),
                     'Wrong Shadow consumer host/runtime')
    pipeline.require(data['scenarios'] == list(SCENARIOS), 'Incomplete/duplicate Shadow consumer scenarios')
    pipeline.require(data['packages'] == packages, 'Shadow consumer package inventory/hash mismatch')
    observed = data['assemblies']
    pipeline.require(isinstance(observed, list) and len(observed) == len(assemblies) and
                     {record.get('id') for record in observed} == set(assemblies),
                     'Shadow loaded assembly inventory mismatch')
    for record in observed:
        pipeline.require(set(record) == {'id', 'sha256', 'framework', 'informational_version'} and
                         record['sha256'] == assemblies[record['id']] and
                         record['framework'] == '.NETCoreApp,Version=v8.0', 'Wrong Shadow loaded assembly')
        pipeline.require(record['informational_version'] in (selected, selected + '+' + source['commit']),
                         'Wrong Shadow assembly informational version')


def verify(directory: Path, selected: str, source: dict) -> dict:
    receipt, log = directory / RECEIPT, directory / LOG
    pipeline.require(receipt.is_file() and not receipt.is_symlink() and 0 < receipt.stat().st_size <= 524288,
                     'Missing, linked or oversized Shadow smoke receipt')
    pipeline.require(log.is_file() and not log.is_symlink() and 0 < log.stat().st_size <= 16 * 1024 * 1024,
                     'Missing, linked or oversized Shadow smoke process log')
    data = json.loads(receipt.read_text(encoding='utf-8'))
    packages, assemblies = package_hashes(directory, selected)
    validate(data, selected, source, packages, assemblies)
    pipeline.require(data['log_sha256'] == pipeline.digest(log), 'Shadow smoke process log changed')
    return {'sha256': pipeline.digest(receipt), 'log_sha256': data['log_sha256'],
            'packages': len(packages), 'scenarios': len(SCENARIOS)}


def execute(command: list[str], log: Path, env: dict, cwd: Path) -> None:
    result = subprocess.run(command, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            encoding='utf-8', errors='replace', check=False)
    output = result.stdout or ''
    with log.open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(command) + '\n' + output + '\n')
    print(output, end='', flush=True)
    if result.returncode:
        raise subprocess.CalledProcessError(result.returncode, command, output=output)


def run(directory: Path, selected: str) -> dict:
    selected = pipeline.version(selected)
    directory = directory.resolve()
    source = pipeline.identity()
    package_catalog.validate_source(pipeline.ROOT)
    pipeline.inspect_packages(directory, selected, source['commit'])
    log, receipt = directory / LOG, directory / RECEIPT
    pipeline.require(not any(path.exists() or path.is_symlink() for path in (log, receipt)),
                     'Shadow smoke evidence already exists; refusing stale receipts')
    # Copy only the standalone installed-package consumer outside the source tree:
    # no parent Directory.Build.* imports, ProjectReference or source build output.
    with tempfile.TemporaryDirectory(prefix='netdxf-shadow-consumer-') as temporary:
        work = Path(temporary)
        original = pipeline.ROOT / 'tests/netDxf.Shadow.PackageSmoke'
        for name in ('netDxf.Shadow.PackageSmoke.csproj', 'Program.cs'):
            shutil.copyfile(original / name, work / name)
        config = work / 'nuget.config'; source_mapping(config, directory)
        cache = work / 'cache'; cache.mkdir()
        env = dict(os.environ, NUGET_PACKAGES=str(cache), NUGET_FALLBACK_PACKAGES='',
                   DOTNET_ROLL_FORWARD='LatestPatch', NETDXF_SHADOW_VERSION=selected,
                   NETDXF_SHADOW_COMMIT=source['commit'], NETDXF_SHADOW_TREE=source['tree'],
                   NETDXF_SHADOW_RECEIPT=str(work / RECEIPT))
        project = str(work / 'netDxf.Shadow.PackageSmoke.csproj')
        properties = [f'-p:PackageVersion={selected}', f'-p:Version={selected}']
        execute(['dotnet', 'restore', project, '--configfile', str(config), '--packages', str(cache),
                 '--force', '--no-cache', *properties], log, env, work)
        output = work / 'app'
        execute(['dotnet', 'build', project, '--no-restore', '-c', 'Release', '-o', str(output), *properties], log, env, work)
        execute(['dotnet', str(output / 'netDxf.Shadow.PackageSmoke.dll')], log, env, work)
        observed = json.loads((work / RECEIPT).read_text(encoding='utf-8'))
        packages, assemblies = package_hashes(directory, selected)
        observed.update(packages=packages, log_sha256=pipeline.digest(log))
        validate(observed, selected, source, packages, assemblies)
        pipeline.require(observed['host'] == platform.system(), 'Shadow consumer host identity mismatch')
        pipeline.require(pipeline.identity() == source, 'Source changed during Shadow package execution')
        with receipt.open('x', encoding='utf-8') as stream:
            json.dump(observed, stream, indent=2); stream.write('\n')
    return verify(directory, selected, source)
