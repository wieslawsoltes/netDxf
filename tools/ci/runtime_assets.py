#!/usr/bin/env python3
"""Execute exact installed nupkg assets; no project reference or publication."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
import pipeline

ROOT = Path(__file__).resolve().parents[2]
MATRIX = tuple((host, runtime, asset)
    for host in ('ubuntu-latest', 'windows-latest')
    for runtime, asset in (('net8.0', 'netstandard2.0'), ('net6.0', 'net6.0'), ('net8.0', 'net8.0')))
MATRIX += (('windows-latest', 'net471', 'net471'), ('windows-latest', 'net48', 'net48'))
MONIKERS = {'netstandard2.0': '.NETStandard,Version=v2.0', 'net471': '.NETFramework,Version=v4.7.1',
            'net48': '.NETFramework,Version=v4.8', 'net6.0': '.NETCoreApp,Version=v6.0', 'net8.0': '.NETCoreApp,Version=v8.0'}
FIELDS = {'schema', 'passed', 'scenarios', 'host', 'runtime', 'asset', 'commit', 'tree',
          'package_sha256', 'assembly_sha256', 'asset_framework', 'consumer_framework',
          'runtime_version', 'framework_release', 'assembly_path'}


def validate_receipt(path: Path, expected: dict) -> dict:
    pipeline.require(path.is_file() and not path.is_symlink() and path.stat().st_size < 16384,
                     'Missing, linked or oversized target receipt')
    root = ET.fromstring(path.read_bytes())
    pipeline.require(root.tag == 'netdxf-target-smoke' and not list(root) and not (root.text or '').strip(), 'Unexpected receipt structure')
    data = dict(root.attrib)
    pipeline.require(set(data) == FIELDS, 'Incomplete/extra target evidence fields')
    pipeline.require(data['schema'] == '1' and data['passed'] == 'true' and data['scenarios'] == '12', 'Target scenarios failed/incomplete')
    key = (data['host'], data['runtime'], data['asset'])
    pipeline.require(key in MATRIX, 'Unknown target execution profile')
    for name, value in expected.items():
        pipeline.require(data.get(name) == str(value), 'Target evidence mismatch: ' + name)
    for name in ('commit', 'tree'):
        pipeline.require(re.fullmatch('[0-9a-f]{40}', data[name]) is not None, 'Invalid source identity')
    for name in ('package_sha256', 'assembly_sha256'):
        pipeline.require(re.fullmatch('[0-9a-f]{64}', data[name]) is not None, 'Invalid asset digest')
    pipeline.require(data['asset_framework'] == MONIKERS[data['asset']] and
                     data['consumer_framework'] == MONIKERS[data['runtime']], 'Wrong loaded/consumer framework')
    pipeline.require(data['assembly_path'].endswith('netDxf.netstandard.dll'), 'Missing loaded assembly path')
    runtime = data['runtime']
    if runtime in ('net471', 'net48'):
        release = data['framework_release']
        pipeline.require(data['runtime_version'].startswith('4.') and release.isdigit() and
                         int(release) >= (461308 if runtime == 'net471' else 528040), 'Incompatible installed Framework runtime')
    else:
        wanted = '6.0.' if runtime == 'net6.0' else '8.0.'
        pipeline.require(data['runtime_version'].startswith(wanted) and data['framework_release'] == '', 'Wrong actual runtime or major roll-forward')
    return data


def package_inputs(packages: Path) -> tuple[dict, Path, dict]:
    pipeline.verify(packages)
    source = pipeline.identity()
    build = json.loads((packages / 'build.json').read_text(encoding='utf-8'))
    pipeline.require(all(build.get(k) == v for k, v in source.items()), 'Candidate package source differs from checkout')
    selected = pipeline.version(build['version'])
    pipeline.require(build.get('package') == pipeline.PACKAGE and tuple(build['frameworks']) == pipeline.TFMS, 'Wrong package/target inventory')
    nupkg = packages / f'{pipeline.PACKAGE}.{selected}.nupkg'
    pipeline.inspect_package(nupkg, selected, source['commit'])
    hashes = {}
    with zipfile.ZipFile(nupkg) as archive:
        for target in pipeline.TFMS:
            hashes[target] = hashlib.sha256(archive.read(f'lib/{target}/{pipeline.PACKAGE}.dll')).hexdigest()
    return build, nupkg, hashes


def execute(command: list[str], log: Path, env: dict) -> None:
    # Never use a shell pipe: preserve the restore/build/consumer process exit code.
    completed = subprocess.run(command, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, encoding='utf-8', errors='replace', check=False)
    output = completed.stdout or ''
    with log.open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(command) + '\n' + output + '\n')
    print(output, end='', flush=True)
    if completed.returncode:
        raise subprocess.CalledProcessError(completed.returncode, command, output=output)


def source_mapping(path: Path, candidate: Path) -> None:
    root = ET.Element('configuration')
    sources = ET.SubElement(root, 'packageSources'); ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='candidate', value=str(candidate.resolve()))
    ET.SubElement(sources, 'add', key='public', value='https://api.nuget.org/v3/index.json')
    mappings = ET.SubElement(root, 'packageSourceMapping')
    # An exact package-ID mapping has priority over the public wildcard.
    local = ET.SubElement(mappings, 'packageSource', key='candidate')
    ET.SubElement(local, 'package', pattern=pipeline.PACKAGE)
    public = ET.SubElement(mappings, 'packageSource', key='public')
    ET.SubElement(public, 'package', pattern='*')
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)


def run(packages: Path, host: str, runtime: str, asset: str) -> None:
    pipeline.require((host, runtime, asset) in MATRIX, 'Unsupported host/runtime/asset combination')
    pipeline.require(platform.system() == ('Windows' if host == 'windows-latest' else 'Linux'), 'Host does not match actual operating system')
    build, nupkg, hashes = package_inputs(packages)
    expected = {k: build[k] for k in ('commit', 'tree')}
    expected.update(host=host, runtime=runtime, asset=asset,
                    package_sha256=pipeline.digest(nupkg), assembly_sha256=hashes[asset])
    work = ROOT / 'artifacts' / 'target-smoke' / f'{host}-{asset}'
    work.mkdir(parents=True, exist_ok=False)
    cache = work / 'cache'; cache.mkdir()
    config = work / 'nuget.config'; source_mapping(config, packages)
    receipt = work / 'receipt.xml'; log = work / 'run.log'
    log.write_text('', encoding='utf-8')
    env = dict(os.environ, NUGET_PACKAGES=str(cache), DOTNET_ROLL_FORWARD='LatestPatch')
    env.update({'NETDXF_SMOKE_' + k.upper(): str(v) for k, v in expected.items()})
    env['NETDXF_SMOKE_RECEIPT'] = str(receipt)
    properties = [f'-p:PackageVersion={build["version"]}', f'-p:SmokeTargetFramework={runtime}', f'-p:NetDxfAssetTarget={asset}']
    project = 'tests/netDxf.TargetSmoke/netDxf.TargetSmoke.csproj'
    execute(['dotnet', 'restore', project, '--configfile', str(config), '--packages', str(cache), *properties], log, env)
    output = work / 'app'
    execute(['dotnet', 'build', project, '--no-restore', '-c', 'Release', '-o', str(output), *properties], log, env)
    if runtime in ('net471', 'net48'):
        command = [str(output / 'netDxf.TargetSmoke.exe')]
    else:
        command = ['dotnet', str(output / 'netDxf.TargetSmoke.dll')]
    execute(command, log, env)
    observed = validate_receipt(receipt, expected)
    pipeline.require(pipeline.identity() == {k: build[k] for k in ('commit', 'tree')}, 'Source changed during target execution')
    result = {'schema': 1, 'passed': True, 'observed': observed,
              'receipt_sha256': pipeline.digest(receipt), 'log_sha256': pipeline.digest(log)}
    with (work / 'result.json').open('x', encoding='utf-8') as stream:
        json.dump(result, stream, indent=2); stream.write('\n')
    print(json.dumps(result, indent=2))


def collect(packages: Path, evidence: Path, output: Path) -> dict:
    build, nupkg, hashes = package_inputs(packages)
    expected_source = {k: build[k] for k in ('commit', 'tree')}
    expected_source['package_sha256'] = pipeline.digest(nupkg)
    results = {}
    for path in sorted(evidence.rglob('result.json')):
        pipeline.require(path.is_file() and not path.is_symlink(), 'Invalid target result file')
        report = json.loads(path.read_text(encoding='utf-8'))
        pipeline.require(set(report) == {'schema', 'passed', 'observed', 'receipt_sha256', 'log_sha256'} and
                         report['schema'] == 1 and report['passed'] is True, 'Invalid/failed target result')
        observed = report['observed']
        key = (observed['host'], observed['runtime'], observed['asset'])
        pipeline.require(key in MATRIX and key not in results, 'Duplicate or unknown target profile')
        receipt, log = path.with_name('receipt.xml'), path.with_name('run.log')
        expected = dict(expected_source, host=key[0], runtime=key[1], asset=key[2], assembly_sha256=hashes[key[2]])
        checked = validate_receipt(receipt, expected)
        pipeline.require(checked == observed and pipeline.digest(receipt) == report['receipt_sha256'], 'Changed target receipt')
        pipeline.require(log.is_file() and not log.is_symlink() and pipeline.digest(log) == report['log_sha256'], 'Missing/changed execution log')
        results[key] = report
    pipeline.require(set(results) == set(MATRIX), 'Incomplete target runtime matrix')
    summary = {'schema': 1, 'passed': True, **expected_source, 'profiles': len(results),
               'scenario_executions': 12 * len(results), 'results': [results[key] for key in MATRIX]}
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('x', encoding='utf-8') as stream:
        json.dump(summary, stream, indent=2); stream.write('\n')
    print(f'PASS: {len(results)} package target/runtime profiles; {12*len(results)} scenario executions; source {build["commit"]}')
    return summary


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('run', 'collect'))
    parser.add_argument('--packages', type=Path, default=ROOT / 'artifacts' / 'packages')
    parser.add_argument('--host', choices=('ubuntu-latest', 'windows-latest'))
    parser.add_argument('--runtime', choices=('net471', 'net48', 'net6.0', 'net8.0'))
    parser.add_argument('--asset', choices=pipeline.TFMS)
    parser.add_argument('--evidence', type=Path, default=ROOT / 'artifacts' / 'runtime-reports')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts' / 'runtime-qualified' / 'runtime-qualification.json')
    args = parser.parse_args()
    if args.command == 'run':
        run(args.packages, args.host, args.runtime, args.asset)
    else:
        collect(args.packages, args.evidence, args.output)


if __name__ == '__main__':
    main()
