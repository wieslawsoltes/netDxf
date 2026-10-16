# Shadow Studio hosting

Shadow Studio has two hosts for the same source-backed CAD engine and frontend:

- **Standalone Blazor WebAssembly** in [`../netDxf.Shadow.Studio.Browser`](../netDxf.Shadow.Studio.Browser). This is the interactive GitHub Pages application. The .NET engine, SkiaSharp, HarfBuzz and DWG exchange execute in the browser; no ASP.NET server is needed.
- **Local ASP.NET Core** in this project. This preserves the existing loopback API, native deployment and server-side integration tests.

The CAD workspace is the default interface. The complete diagnostics interface is available at `diagnostics.html` in either host. See [CAD workspace](../../docs/CadWorkspace.md) for commands, precision input, selection/grips, layers, document management and existing feature boundaries.

## GitHub Pages

The [Shadow Studio Pages workflow](../../../.github/workflows/shadow-studio-pages.yml) publishes the **interactive Blazor application**, not the old read-only PNG/SVG gallery. It installs the .NET 10 WebAssembly toolchain, publishes the browser project, verifies route coverage, checks every shared frontend asset and the pinned font/license payloads, then runs Chromium against a static-only server at both `/` and `/netDxf/`.

The test drives real drawing commands, undo/redo, workspaces/merge, blocks, hatches, native PNG/SVG rendering, DXF and DWG exchange, and diagnostics notifications. DXF downloads are also audited with the independent ezdxf reader. The test fails if any `/api/` request reaches the static server. Screenshots and results are retained as Actions artifacts. `studio-manifest.json` identifies the tested source and hashes the published assets.

Only the published `wwwroot` directory is deployed. It includes the .NET WebAssembly application and licensed font assets, but no ASP.NET host or service. Application URLs are relative, so the same publish works at a repository subpath or a custom-domain root. There are no runtime font-CDN requests. `.nojekyll` preserves `_framework` on static hosts.

### Deployment settings

In **Settings → Pages → Build and deployment**, select **GitHub Actions** as the source. Allow `netstandard` in any `github-pages` environment branch restrictions. Existing approvals and protections remain in effect; no personal access token is needed.

For this repository the default site is `https://wieslawsoltes.github.io/netDxf/`, unless a custom domain is configured. The workflow owns the repository's Pages root. Pushes to `netstandard` affecting the app or its dependencies deploy after validation. Pull requests and manual runs on other branches validate without deploying. Only the production deployment job receives `pages: write` and `id-token: write`; it cannot publish packages. Actions remain commit-pinned and checkout credentials are not persisted.

### Build and run the browser application

From the repository root, with the .NET 10 SDK and Python installed:

```sh
dotnet workload install wasm-tools
dotnet publish shadow/samples/netDxf.Shadow.Studio.Browser/netDxf.Shadow.Studio.Browser.csproj \
  -c Release -f net10.0 -p:NoWarn=1591 -o artifacts/shadow-studio-browser
python tools/ci/shadow_studio_browser.py --published artifacts/shadow-studio-browser/wwwroot
python -m http.server 8080 --bind 127.0.0.1 --directory artifacts/shadow-studio-browser/wwwroot
```

Open `http://127.0.0.1:8080/`. Do not apply the local host's global `TargetFrameworks=net8.0` override to the browser project: it targets .NET 10 while the libraries retain their existing frameworks. See the [browser README](../netDxf.Shadow.Studio.Browser/README.md) for architecture and runtime boundaries.

## Run the local Studio

```sh
dotnet run --project shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj \
  -c Release -p:TargetFrameworks=net8.0
```

Open `http://127.0.0.1:5080/`. The host remains loopback-only. Its request marker, document/workspace/view guards, CSP and publication restrictions have not been relaxed to support Pages. The transport-independent document hub is shared; the original `HttpRequest` overloads remain available in the server-only adapter.

## Optional read-only gallery export

The earlier `tools/ci/shadow_studio_pages.py` exporter and `tests/shadow_pages` suite are retained for producing offline, script-free galleries. They are no longer the Pages deployment path.

```sh
dotnet publish shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj \
  -c Release -p:TargetFrameworks=net8.0 -p:NoWarn=1591 \
  --self-contained false -o artifacts/shadow-studio-host
python tools/ci/shadow_studio_pages.py \
  --published artifacts/shadow-studio-host --output artifacts/shadow-studio-gallery \
  --commit "$(git rev-parse HEAD)"
```

The gallery export requires the local host's native rendering dependencies (on Ubuntu: `libfontconfig1 fonts-dejavu-core fonts-liberation`). Use a fresh output directory; the exporter does not overwrite an existing gallery.
