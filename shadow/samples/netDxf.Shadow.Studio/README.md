# Shadow Studio hosting

Studio is a **local ASP.NET Core application with a browser UI**, not a
browser-hosted .NET/WebAssembly application. Its editor, renderer, DXF IO,
workspaces and event stream depend on the server in `Program.cs`.

## GitHub Pages

The **Shadow Studio Pages** workflow in
[`.github/workflows/shadow-studio-pages.yml`](../../../.github/workflows/shadow-studio-pages.yml)
builds the sample and publishes a **read-only rendering gallery**. It does not
publish a nonfunctional copy of `wwwroot` or imply that Pages runs ASP.NET.

On the Actions runner, the workflow publishes the .NET 8 host, installs
fontconfig and system fonts, starts the host on an ephemeral loopback port and
exports every available layout through Studio's existing HTTP API. Both Skia
PNG and SVG output are required. Each layout includes its captured state JSON;
`manifest.json` records the source commit and asset sizes/SHA-256 hashes. The
host is stopped on success or failure, and its log is retained separately.
Only the generated static gallery is uploaded, never the host binaries, fonts,
source tree, or live API frontend. Diagnostic messages remain available in the
state JSON rather than being hidden.

All internal URLs are relative, so the gallery works at `/netDxf/`, other
repository paths and a custom domain root. The generated page has no JavaScript,
API requests, fake editing controls, external fonts or CDN dependencies.

### Enable deployment

1. Merge the workflow into `netstandard`.
2. In **Settings → Pages → Build and deployment**, select **GitHub Actions** as
   the source. This one-time repository setting is not changed by the workflow;
   no personal access token is required.
3. If the `github-pages` environment has deployment branch restrictions, allow
   `netstandard`. Existing approval/protection rules are respected.
4. Run **Actions → Shadow Studio Pages → Run workflow** on `netstandard`, or
   push a matching source change. The deployment job reports the actual site URL.

For `wieslawsoltes/netDxf`, the default project URL is
`https://wieslawsoltes.github.io/netDxf/` unless a custom domain is configured.
GitHub Pages provides one site per repository: this workflow owns that site's
root, so coordinate it with any future documentation-site workflow.

Pushes to `netstandard` affecting Studio, shadow/core dependencies, shared build
configuration or the exporter trigger deployment. Pull requests targeting
`netstandard` build and export the same site without deployment permissions.
Manual runs on other branches validate only. Production runs are serialized and
not cancelled while deploying; superseded PR runs may be cancelled.
All Actions are commit-pinned, checkout credentials are not persisted, and only
the deploy job receives `pages: write` and `id-token: write`.

### Reproduce the export

Run from the repository root with the .NET 8 SDK and Python 3.12 installed.
On Ubuntu, install `libfontconfig1 fonts-dejavu-core fonts-liberation` first.

```sh
python -m unittest discover -s tests/shadow_pages -p 'test_*.py' -v
dotnet publish shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj \
  -c Release -p:TargetFrameworks=net8.0 -p:NoWarn=1591 \
  --self-contained false -o artifacts/shadow-studio-host
python tools/ci/shadow_studio_pages.py \
  --published artifacts/shadow-studio-host \
  --output artifacts/shadow-studio-pages \
  --commit "$(git rev-parse HEAD)"
python -m http.server 8080 --bind 127.0.0.1 --directory artifacts/shadow-studio-pages
```

Open `http://127.0.0.1:8080`. The exporter refuses to overwrite an existing output
directory; use a fresh `--output` path for another export. It stages all assets
before publishing the directory, rejects invalid images or unexpected API
responses, and carries the current document/workspace/view headers for guarded
requests. No editor security checks are bypassed.

## Full interactive CAD workspace

The default frontend is now the source-backed CAD workspace. See
[CAD workspace](../../docs/CadWorkspace.md) for commands, precision input,
selection/grips, layers, documents, reusable APIs and explicit feature boundaries.
The previous diagnostics frontend is retained at `/diagnostics.html`.

## Run the local Studio

```sh
dotnet run --project shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj \
  -c Release -p:TargetFrameworks=net8.0
```

Open `http://127.0.0.1:5080`. See [the shadow workspace README](../../README.md)
for features and architecture. The full app deliberately remains loopback-only.
A fully interactive standalone Pages deployment would require a separate
WebAssembly port; publishing the ASP.NET DLLs or copying `wwwroot` is not such a
port. This workflow does not relax the host's security policy or expose its
single-user API as a public service.

References: [GitHub Pages hosting](https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages)
and [custom Pages workflows](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).
