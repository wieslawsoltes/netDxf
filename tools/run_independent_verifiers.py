#!/usr/bin/env python3
"""Run every checked-in independent DXF verifier and retain each result.

This is a development/CI entry point, not a netDxf runtime dependency. Each
verifier defines its own fixture requirements and semantic or tag-level scope.
An empty suite, missing fixture, timeout or failed verifier fails the command.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import importlib.metadata
import json
from pathlib import Path
import platform
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[1]


def run_verifier(script: Path, artifacts: Path, output: Path, timeout: float) -> dict:
    started = time.monotonic()
    log = output / (script.stem + ".log")
    timed_out = False
    with log.open("w", encoding="utf-8") as stream:
        try:
            result = subprocess.run(
                [sys.executable, str(script), str(artifacts)],
                cwd=ROOT,
                stdout=stream,
                stderr=subprocess.STDOUT,
                timeout=timeout,
                check=False,
            )
            code = result.returncode
        except subprocess.TimeoutExpired:
            code = -1
            timed_out = True
            stream.write(f"\nVerifier exceeded {timeout:g} seconds.\n")
        except OSError as error:
            code = -1
            stream.write(f"\nCould not execute verifier: {error}\n")
    return {
        "script": script.relative_to(ROOT).as_posix(),
        "passed": code == 0,
        "exit_code": code,
        "timed_out": timed_out,
        "seconds": round(time.monotonic() - started, 3),
        "log": log.name,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path, help="Conformance fixture directory")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/independent")
    parser.add_argument("--jobs", type=int, default=4)
    parser.add_argument("--timeout", type=float, default=180)
    args = parser.parse_args()
    if not 1 <= args.jobs <= 16 or args.timeout <= 0:
        parser.error("jobs must be 1–16 and timeout must be positive")
    artifacts = args.artifacts.resolve()
    if not artifacts.is_dir():
        parser.error(f"Fixture directory does not exist: {artifacts}")
    scripts = sorted((ROOT / "tools").glob("verify_*.py"))
    if not scripts:
        parser.error("No independent verifier scripts found")
    try:
        reader_version = importlib.metadata.version("ezdxf")
    except importlib.metadata.PackageNotFoundError:
        parser.error("Install tools/requirements-independent.txt before running verifiers")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    results = []
    with ThreadPoolExecutor(max_workers=args.jobs) as executor:
        pending = [executor.submit(run_verifier, script, artifacts, output, args.timeout)
                   for script in scripts]
        for future in as_completed(pending):
            result = future.result()
            results.append(result)
            print(f"{'PASS' if result['passed'] else 'FAIL'} {result['script']} "
                  f"({result['seconds']:.2f}s)", flush=True)
    results.sort(key=lambda result: result["script"])
    failed = sum(not result["passed"] for result in results)
    report = {
        "python": platform.python_version(),
        "independent_reader": f"ezdxf {reader_version}",
        "passed": len(results) - failed,
        "failed": failed,
        "results": results,
    }
    (output / "results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Independent DXF verification: {len(results) - failed} passed; {failed} failed.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
