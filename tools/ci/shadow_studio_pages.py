#!/usr/bin/env python3
"""Export the local ASP.NET Studio as a read-only, API-free GitHub Pages gallery.

Standard library only. The real Studio runs on loopback during the export; its
server binaries, source files and live API frontend are never copied to Pages.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
from html import escape
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import time
from typing import Iterator
from urllib.error import URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener
import xml.etree.ElementTree as ET

PROJECT = "shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj"
WIDTH, HEIGHT = 1280, 800
MAX_RESPONSE = 32 * 1024 * 1024


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise RuntimeError("Studio export must not follow HTTP redirects")


class StudioClient:
    def __init__(self, base_url: str) -> None:
        url = urlsplit(base_url)
        if (url.scheme != "http" or url.hostname not in {"127.0.0.1", "::1"}
                or url.path not in {"", "/"} or url.query or url.fragment
                or url.username or url.password):
            raise ValueError("Studio export requires an HTTP loopback origin")
        self.base_url = base_url.rstrip("/")
        self.opener = build_opener(ProxyHandler({}), NoRedirect())

    def request(self, path: str, state: dict | None = None,
                payload: dict | None = None, timeout: float = 120) -> tuple[bytes, str]:
        if not path.startswith("/api/"):
            raise ValueError("Only Studio API paths may be exported")
        headers = {"X-Shadow-Studio": "1"}
        if state is not None:
            headers.update({
                "X-Shadow-Document": str(state["documentId"]),
                "X-Shadow-Workspace": str(state["workspaceId"]),
                "X-Shadow-View": str(state["viewVersion"]),
            })
        data = None
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        request = Request(self.base_url + path, data=data, headers=headers)
        with self.opener.open(request, timeout=timeout) as response:
            body = response.read(MAX_RESPONSE + 1)
            if len(body) > MAX_RESPONSE:
                raise ValueError(f"Studio response exceeds {MAX_RESPONSE} bytes: {path}")
            return body, response.headers.get_content_type()

    def json(self, path: str, state: dict | None = None,
             payload: dict | None = None) -> dict:
        body, content_type = self.request(path, state, payload)
        if content_type != "application/json":
            raise ValueError(f"Expected JSON from {path}, received {content_type}")
        value = json.loads(body)
        if not isinstance(value, dict):
            raise ValueError(f"Expected a JSON object from {path}")
        return value


@contextmanager
def studio_server(published: Path, log: Path) -> Iterator[StudioClient]:
    published = published.resolve()
    assembly = published / "netDxf.Shadow.Studio.dll"
    if not assembly.is_file():
        raise FileNotFoundError(f"Publish Studio first; missing {assembly}")
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    client = StudioClient(f"http://127.0.0.1:{port}")
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(
            ["dotnet", str(assembly), "--urls", client.base_url],
            cwd=published, stdout=output, stderr=subprocess.STDOUT,
            env={**os.environ, "ASPNETCORE_ENVIRONMENT": "Production"},
        )
        try:
            deadline = time.monotonic() + 60
            while True:
                if process.poll() is not None:
                    raise RuntimeError(f"Studio exited with {process.returncode}; see {log}")
                try:
                    body, mime = client.request("/api/health", timeout=1)
                    if mime != "application/json" or json.loads(body).get("status") != "ok":
                        raise RuntimeError("Unexpected Studio health response")
                    break
                except (URLError, TimeoutError):
                    if time.monotonic() >= deadline:
                        raise TimeoutError(f"Studio did not become healthy; see {log}")
                    time.sleep(0.25)
            yield client
            if process.poll() is not None:
                raise RuntimeError(f"Studio exited during export; see {log}")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def validate_render(body: bytes, content_type: str, backend: str) -> None:
    if backend == "skia":
        if (content_type != "image/png" or len(body) < 33
                or body[:8] != b"\x89PNG\r\n\x1a\n" or body[12:16] != b"IHDR"
                or int.from_bytes(body[16:20], "big") != WIDTH
                or int.from_bytes(body[20:24], "big") != HEIGHT):
            raise ValueError("Skia did not return the expected PNG image")
    elif backend == "svg":
        if content_type != "image/svg+xml" or b"<!DOCTYPE" in body or b"<!ENTITY" in body:
            raise ValueError("SVG renderer returned invalid content")
        root = ET.fromstring(body)
        if root.tag != "{http://www.w3.org/2000/svg}svg":
            raise ValueError("SVG renderer did not return an SVG root element")
    else:
        raise ValueError(f"Unsupported export backend: {backend}")


def index_html(manifest: dict) -> str:
    repository = escape(manifest["repository"], quote=True)
    commit = escape(manifest["commit"], quote=True)
    source = f"https://github.com/{repository}/tree/{commit}"
    cards = []
    for item in manifest["layouts"]:
        name = escape(item["name"], quote=True)
        stem = item["stem"]  # Generated ordinal, never a source layout name.
        cards.append(f'''<article>
<h2>{name}</h2>
<a href="layouts/{stem}.svg" aria-label="Open {name} as SVG"><img
 src="layouts/{stem}.png" alt="{name} rendered by Studio's Skia backend"
 width="{WIDTH}" height="{HEIGHT}" loading="lazy"></a>
<p>{item['occurrences']} rendered occurrences · {item['diagnostics']} diagnostics</p>
<nav aria-label="{name} exports"><a href="layouts/{stem}.svg">View SVG</a>
<a href="layouts/{stem}.png" download>Download PNG</a>
<a href="layouts/{stem}.json" download>Inspect state JSON</a></nav>
</article>''')
    return f'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<meta name="description" content="Read-only netDxf Shadow Studio gallery, rendered from the source commit using SkiaSharp and SVG.">
<title>netDxf Shadow Studio · Pages preview</title>
<style>
:root {{ color-scheme: dark; font-family: system-ui, sans-serif; background: #11151d; color: #e2e8f0; }}
* {{ box-sizing: border-box; }}
body {{ margin: 0 auto; padding: clamp(16px, 4vw, 48px); max-width: 1480px; line-height: 1.6; }}
a {{ color: #98ccff; text-underline-offset: 3px; }}
a:focus-visible {{ outline: 3px solid #ffcf72; outline-offset: 4px; }}
h1 {{ margin: 6px 0; font-size: clamp(28px, 4vw, 46px); line-height: 1.2; }}
h2 {{ font-size: 20px; margin-top: 0; }}
header, footer {{ margin-bottom: 28px; }}
.eyebrow {{ color: #a5b4cc; letter-spacing: .08em; text-transform: uppercase; font-size: 12px; }}
.notice {{ border-left: 3px solid #ffcf72; padding: 12px 20px; background: #202532; margin: 24px 0; }}
.gallery {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 480px), 1fr)); gap: 24px; }}
article {{ min-width: 0; background: #1a202c; border: 1px solid #354157; border-radius: 12px; padding: 20px; }}
img {{ display: block; width: 100%; height: auto; border-radius: 6px; background: white; }}
nav {{ display: flex; gap: 18px; flex-wrap: wrap; }}
p {{ overflow-wrap: anywhere; }}
pre {{ padding: 16px; background: #0c1017; overflow-x: auto; border-radius: 6px; }}
footer {{ color: #a5b4cc; margin-top: 32px; }}
</style>
</head>
<body>
<header><div class="eyebrow">netDxf · immutable shadow DOM</div>
<h1>Shadow Studio</h1><p>Build-time SkiaSharp and SVG rendering gallery</p>
<nav><a href="{source}/{PROJECT.rsplit('/', 1)[0]}">Studio source</a>
<a href="{source}/shadow">Documentation</a><a href="manifest.json">Build manifest</a></nav>
<div class="notice"><strong>Read-only GitHub Pages preview — not the live editor.</strong>
GitHub Pages cannot run Studio's ASP.NET backend. These images and state exports
were generated from the real sample during CI. DXF open/save, editing, selection,
undo/redo, workspaces and live rendering require the local host below.</div></header>
<main><section aria-label="Rendered Studio layouts" class="gallery">
{''.join(cards)}
</section>
<section aria-labelledby="run"><h2 id="run">Run the full Studio locally</h2>
<p>Install the .NET 8 SDK, clone the repository and run from its root.
On Linux, install fontconfig and usable system fonts.</p>
<pre><code>git clone --branch netstandard https://github.com/{repository}.git
cd {repository.split('/')[-1]}
dotnet run --project {PROJECT} -c Release -p:TargetFrameworks=net8.0</code></pre>
<p>Then open <a href="http://127.0.0.1:5080">http://127.0.0.1:5080</a>.
The server is intentionally loopback-only; this deployment does not expose it publicly.</p>
</section></main>
<footer>Source commit <a href="https://github.com/{repository}/commit/{commit}">{commit}</a>.
All gallery assets use relative URLs, including when served below a project path.</footer>
</body></html>
'''


def export_site(client: StudioClient, output: Path, repository: str, commit: str) -> dict:
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Repository must be owner/name")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("A full source commit SHA is required")
    # Never delete an existing export, checkout, or directory selected by mistake.
    if output.exists() or output.is_symlink():
        raise FileExistsError(f"Output must not already exist: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    state = client.json("/api/state")
    layouts = state.get("layouts")
    if (not isinstance(layouts, list) or not layouts or len(layouts) > 100
            or any(not isinstance(name, str) or not name for name in layouts)
            or len(set(layouts)) != len(layouts)):
        raise ValueError("Studio must expose 1–100 unique named layouts")
    if not {"skia", "svg"}.issubset(state.get("backends", [])):
        raise ValueError("Both Skia and SVG backends are required for the Pages export")
    document_id, workspace_id = state["documentId"], state["workspaceId"]
    manifest = {"schemaVersion": 1, "mode": "read-only", "repository": repository,
                "commit": commit, "width": WIDTH, "height": HEIGHT,
                "layouts": [], "assets": {}}
    with tempfile.TemporaryDirectory(prefix=".shadow-pages-", dir=output.parent) as temporary:
        staging = Path(temporary) / "site"
        (staging / "layouts").mkdir(parents=True)

        def write_asset(path: str, body: bytes) -> None:
            (staging / path).write_bytes(body)
            manifest["assets"][path] = {"bytes": len(body), "sha256": hashlib.sha256(body).hexdigest()}

        for index, name in enumerate(layouts):
            state = client.json("/api/layout", state, {"revision": state["revision"], "name": name})
            if (state.get("layout") != name or state.get("documentId") != document_id
                    or state.get("workspaceId") != workspace_id):
                raise ValueError("Studio changed document/workspace or did not select the requested layout")
            stem = f"{index:03d}"
            for backend, extension in (("skia", "png"), ("svg", "svg")):
                query = urlencode({"width": WIDTH, "height": HEIGHT, "backend": backend})
                body, content_type = client.request(f"/api/render?{query}", state)
                validate_render(body, content_type, backend)
                write_asset(f"layouts/{stem}.{extension}", body)
            write_asset(f"layouts/{stem}.json", (json.dumps(state, indent=2) + "\n").encode("utf-8"))
            manifest["layouts"].append({"name": name, "stem": stem,
                                       "occurrences": int(state["occurrences"]),
                                       "diagnostics": len(state["diagnostics"])})
            print(f"Exported layout {index + 1}/{len(layouts)}: {name}", flush=True)
        (staging / "index.html").write_text(index_html(manifest), encoding="utf-8")
        (staging / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        (staging / ".nojekyll").touch()
        staging.rename(output)
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--published", required=True, type=Path, help="dotnet publish output directory")
    parser.add_argument("--output", type=Path, default=Path("artifacts/shadow-studio-pages"))
    parser.add_argument("--log", type=Path, default=Path("artifacts/shadow-studio-pages.log"))
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY", "wieslawsoltes/netDxf"))
    parser.add_argument("--commit", default=os.environ.get("GITHUB_SHA"), required="GITHUB_SHA" not in os.environ)
    args = parser.parse_args()
    with studio_server(args.published, args.log) as client:
        manifest = export_site(client, args.output, args.repository, args.commit)
    print(f"Exported {len(manifest['layouts'])} layouts to {args.output}")


if __name__ == "__main__":
    main()
