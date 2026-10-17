# Shadow Studio — Blazor WebAssembly

This standalone .NET 10 application runs the existing Shadow CAD Studio on static hosting, including GitHub Pages. It is not a screenshot gallery, a server-side Blazor application, or a JavaScript reimplementation of netDxf.

## Shared implementation

The project compiles the original `Studio*.cs` services and references the same netDxf, immutable shadow model, editor, drafting, graph, rendering, SHX and managed DWG exchange projects. Only ASP.NET-specific entry points and adapters are excluded. `StudioDocumentHub` accepts transport-neutral request identities, and the local server retains its original overloads in `StudioDocumentHub.Http.cs`.

All existing frontend modules, styles and HTML bodies are linked from the original Studio project. The Blazor bootstrap loads the shared CAD or diagnostics shell, starts the .NET runtime, waits for an explicit callback from the initialized C# application, installs its local transport and then imports the original UI entry point. The callback includes asynchronous font loading: `Blazor.start()` alone does not establish application readiness. CI verifies that shared UI assets remain byte-identical.

`BrowserRuntime` exposes a bounded transport facade over JavaScript interop. `BrowserRoutes` and `BrowserCadRoutes` dispatch every original endpoint to the original engine method. Mutable operations retain the same document, workspace, view-version and revision guards. Responses preserve the original HTTP-style status codes so stale receipts and failed edits behave consistently. Writes are serialized; no failed operation is retried automatically. Binary files and images cross interop as byte arrays, not base64 strings.

`browser-transport.mjs` handles only same-origin Studio API paths, including the legacy root-absolute CAD URLs and repository-relative diagnostics URLs. Other requests use native fetch. Diagnostics notifications use the engine's bounded/coalescing subscription channels through local EventSource and ReadableStream adapters; no polling API server is needed.

## Rendering and fonts

Both SkiaSharp and HarfBuzz are linked as native WebAssembly dependencies. SVG and PNG output still use the same render compilers and backends. The app restores four Noto Sans styles from an immutable upstream commit at build time, verifies their blob hashes before deployment, and publishes the SIL Open Font License alongside them. Runtime font downloads are same-origin.

Interactive Skia frames use tightly packed, straight RGBA8888 pixels through binary interop, avoiding a PNG encode/decode cycle inside the same browser. The pixel path uses the same renderer, camera, antialiasing, selection and retained-snapshot guards as PNG export; regression checks compare it with decoded PNG pixels. Browsers without `ImageData`/`createImageBitmap` use PNG frames, and PNG/SVG downloads retain their original formats. Replaced and obsolete `ImageBitmap` instances are released explicitly.

JSON replies also use UTF-8 byte arrays, avoiding secondary JSON escaping and Fetch's string encoding for large state responses. Generated writers handle the immutable presentation lists with the same fields, row order and double precision. The ordinary serializer remains the fallback for other types, custom converters and unsupported writer options. The [synthetic presentation benchmark](../../benchmarks/netDxf.Shadow.PresentationBenchmarks/README.md) measures these costs independently and checks every result for exact JSON equality.

The viewport coalesces identical pending frames and reuses an already completed frame for an unchanged rendering receipt. Pan and pinch gestures transform the previous frame while input is captured, then render the exact final camera at the normal quality. Explicit regeneration bypasses completed-frame reuse. Background CAD state polls carry the previous server/change receipt: an unchanged result is the JSON literal `null`, so idle polling does not compile geometry or serialize the object explorer. Immutable explorer metadata is cached only for the exact snapshot and compiled scene; selection, history, workspaces and resource changes remain revision guarded.

Browsers do not expose the desktop host's system font database. The browser host supplies deterministic Noto Sans typefaces through a per-engine resolver. To reproduce a drawing's original typography, use the existing **Load SHX / SHP** action: in the browser its file picker additionally accepts **TTF and OTF** fonts. Explicit fonts are matched by filename alias and embedded family name, with bold/italic style matching. Each drawing owns up to 32 explicit TTF/OTF files of at most 4 MiB each. Malformed uploads are parsed before registration; document and revision guards still apply. Font uploads are local resources, not sent to a server or embedded into DXF saves.

An unavailable font falls back to Noto Sans and can have different metrics or glyph coverage. Authored font names remain unchanged in the drawing. Existing SHX/SHP upload, BIGFONT mappings, paragraph layout, dimensions and text-outline rendering remain engine features rather than approximations in JavaScript.

## Browser lifecycle

Each tab owns its own drawing session, with the same document/branch bounds as the local sample. Opening and saving use browser file selection and downloads. Unsaved changes trigger a navigation warning; save drawings before reloading or closing the tab. Sessions are not a shared remote server and are not durable IndexedDB backups. The diagnostics entry point opened in another tab has an independent session.

The baseline build uses the WebAssembly interpreter with native rendering, full globalization data and registered legacy code pages. Trimming is disabled deliberately because the model and exchange libraries use reflection. Threads are disabled so Pages does not require COOP/COEP headers. Large synchronous CAD operations run on the runtime's main thread. Abort signals cancel queued operations and fence responses, but cannot roll back an already-started synchronous edit.

Migration parity means the features implemented by the existing Studio, not additional AutoCAD features that Studio has not implemented. The existing CAD capability boundaries and diagnostic errors remain applicable.

## Build and verify

```sh
dotnet workload install wasm-tools
dotnet publish shadow/samples/netDxf.Shadow.Studio.Browser/netDxf.Shadow.Studio.Browser.csproj \
  -c Release -f net10.0 -p:NoWarn=1591 -o artifacts/shadow-studio-browser
python tools/ci/shadow_studio_browser.py --check-source
python tools/ci/shadow_studio_browser.py --published artifacts/shadow-studio-browser/wwwroot
python -m pip install playwright==1.57.0 ezdxf==1.4.4
python -m playwright install --with-deps chromium
python shadow/tests/blazor_studio_browser.py --published artifacts/shadow-studio-browser/wwwroot
python shadow/tests/blazor_capabilities_browser.py --published artifacts/shadow-studio-browser/wwwroot
```

Serve only the complete published `wwwroot` directory. Open its root for CAD or `diagnostics.html` for the original diagnostics UI. Source `wwwroot` is not itself a deployable application: the Blazor framework and native WASM payloads must be generated by publish.

The browser suite drives real UI edits, branch/block workflows and DXF/DWG exchange; independently audits DXF output; renders every original sample layout with both backends; proves that explicit fonts affect rendering only in their owning drawing; and rejects malformed/stale font uploads. Both suites reject API requests reaching their static-only server. Transport unit tests additionally cover queued/in-flight cancellation, failed-write non-replay, binary interop and event subscription lifetimes.
