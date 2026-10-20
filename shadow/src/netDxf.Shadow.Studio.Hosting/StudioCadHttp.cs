using netDxf.Shadow.Exchange.Dwg;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;



public static class StudioCadHttp
{
    public static void MapCadWorkspace(this WebApplication app)
    {
        var routes = app.MapGroup("/api/cad");
        routes.MapGet("/formats", () => new
        {
            provider = DrawingExchange.Provider,
            dwgWriteVersions = DrawingExchange.WriteVersions,
            defaultDwgVersion = "AC1032"
        });
        routes.MapGet("/documents", (StudioDocumentHub hub) => hub.Documents());
        routes.MapPost("/documents", (StudioDocumentHub hub, CadNewDocumentRequest request) => hub.Create(request));
        routes.MapPost("/{id}/close", (string id, StudioDocumentHub hub, HttpRequest http, CadCloseDocumentRequest request) => hub.Close(id, http, request));
        routes.MapGet("/{id}/state", (string id, string? knownServer, long? knownChange, StudioDocumentHub hub, HttpContext context) =>
            hub.Use(id, s => s.CadStateIfChanged(knownServer, knownChange) is { } state
                ? StudioHttp.Presentation(context, state) : Results.Text("null", "application/json")));
        routes.MapPost("/{id}/command", (string id, StudioDocumentHub hub, HttpRequest http, CadCommandRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadCommand(request, token))));
        routes.MapPost("/{id}/insert", (string id, StudioDocumentHub hub, HttpRequest http, CadInsertRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadInsert(request, token))));
        routes.MapGet("/{id}/modules", (string id, StudioDocumentHub hub, HttpRequest http) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadModules())));
        routes.MapPost("/{id}/modules/toggle", (string id, StudioDocumentHub hub, HttpRequest http, CadModuleToggleRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadSetModule(request))));
        routes.MapPost("/{id}/modules/symbols", (string id, StudioDocumentHub hub, HttpRequest http, CadModuleCatalogRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadModuleSymbols(request))));
        routes.MapPost("/{id}/modules/analyze", (string id, StudioDocumentHub hub, HttpRequest http, CadModuleAnalysisRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadAnalyzeModule(request, token))));
        routes.MapPost("/{id}/modules/schedule", (string id, StudioDocumentHub hub, HttpRequest http, CadModuleScheduleRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadModuleSchedule(request, token))));
        routes.MapPost("/{id}/modules/connectivity", (string id, StudioDocumentHub hub, HttpRequest http, CadModuleConnectivityRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadModuleConnectivity(request, token))));
        routes.MapPost("/{id}/modules/command", (string id, StudioDocumentHub hub, HttpRequest http, CadModuleExecuteRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadRunModule(request, token))));
        routes.MapPost("/{id}/references/capture", (string id, StudioDocumentHub hub, HttpRequest http, DrawingReferenceCaptureRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CaptureReferences(request))));
        routes.MapPost("/{id}/references/validate", (string id, StudioDocumentHub hub, HttpRequest http, DrawingReferenceValidationRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.ValidateReferences(request))));
        routes.MapPost("/{id}/health", (string id, StudioDocumentHub hub, HttpRequest http, CadHealthRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadHealth(request, token))));
        routes.MapPost("/{id}/health/select", (string id, StudioDocumentHub hub, HttpRequest http, CadHealthSelectRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadHealthSelect(request))));
        routes.MapGet("/{id}/edit-previews", (string id, StudioDocumentHub hub, HttpRequest http) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadEditPreviews())));
        routes.MapGet("/{id}/workflow/status", (string id, StudioDocumentHub hub, HttpRequest http) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadWorkflowStatus())));
        routes.MapPost("/{id}/workflow/start", (string id, StudioDocumentHub hub, HttpRequest http, CadWorkflowStartRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadWorkflowStart(request))));
        routes.MapPost("/{id}/workflow/stop", (string id, StudioDocumentHub hub, HttpRequest http, RevisionRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadWorkflowStop(request))));
        routes.MapGet("/{id}/workflow/export", (string id, StudioDocumentHub hub, HttpRequest http) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadWorkflowExport())));
        routes.MapPost("/{id}/workflow/replay", (string id, StudioDocumentHub hub, HttpRequest http, CadWorkflowReplayRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadWorkflowReplay(request, token))));
        routes.MapPost("/{id}/workflow/roundtrip", (string id, StudioDocumentHub hub, HttpRequest http, RevisionRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadWorkflowRoundTrip(request))));
        routes.MapPost("/{id}/edit-preview", (string id, StudioDocumentHub hub, HttpRequest http, CadEditPreviewRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadEditPreview(request, token))));
        routes.MapPost("/{id}/edit-preview/apply", (string id, StudioDocumentHub hub, HttpRequest http, CadEditPreviewTokenRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadEditPreviewApply(request))));
        routes.MapPost("/{id}/edit-preview/discard", (string id, StudioDocumentHub hub, HttpRequest http, CadEditPreviewTokenRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadEditPreviewDiscard(request))));
        routes.MapGet("/{id}/edit-preview/render", (string id, StudioDocumentHub hub, HttpRequest http, string previewId, bool after, int width, int height, string? backend, string? block, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => {
                var image = s.CadEditPreviewRender(previewId, after, width, height, backend ?? "svg", token, block);
                return Results.Bytes(image.Bytes, image.ContentType);
            })));
        routes.MapPost("/{id}/block/define", (string id, StudioDocumentHub hub, HttpRequest http, CadBlockDefinitionRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadDefineBlock(request, token))));
        routes.MapGet("/{id}/block-libraries", (string id, StudioDocumentHub hub, HttpRequest http) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, s.CadBlockLibraries)));
        routes.MapPost("/{id}/block-library/open", async (string id, StudioDocumentHub hub, HttpRequest http, long revision, string name, CancellationToken token) =>
        {
            hub.Use(id, s => StudioHttp.Execute(s, http, () => {
                if (s.CadRevision != revision) throw new ShadowConflictException("Drawing changed before library upload."); return true;
            }));
            using var stream = await ReadBounded(http, netDxf.Shadow.Symbols.DrawingBlockLibrary.MaximumBytes, token);
            return hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadOpenBlockLibrary(revision, name, stream.ToArray(), token)));
        });
        routes.MapGet("/{id}/block-library/export", (string id, StudioDocumentHub hub, HttpRequest http, long revision, string block, bool binary, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => Results.File(s.CadExportBlockLibrary(new(revision, new(block), binary), token), "application/dxf", "block-library.dxf"))));
        routes.MapGet("/{id}/attributes", (string id, StudioDocumentHub hub, HttpRequest http, long revision) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadReadAttributes(revision))));
        routes.MapPost("/{id}/insert-preview", (string id, StudioDocumentHub hub, HttpRequest http, CadInsertPreviewRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadInsertPreview(request, token))));
        routes.MapPost("/{id}/attributes", (string id, StudioDocumentHub hub, HttpRequest http, CadAttributeRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadEditAttributes(request, token))));
        routes.MapGet("/{id}/attribute-definitions", (string id, StudioDocumentHub hub, HttpRequest http, long revision, string? block, bool? currentSpace) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadReadAttributeDefinitions(revision, block, currentSpace ?? false))));
        routes.MapPost("/{id}/attribute-definitions", (string id, StudioDocumentHub hub, HttpRequest http, CadAttributeDefinitionRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadEditAttributeDefinitions(request, token))));
        routes.MapPost("/{id}/block/rename", (string id, StudioDocumentHub hub, HttpRequest http, CadBlockRenameRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadRenameBlock(request))));
        routes.MapPost("/{id}/hatch/create", (string id, StudioDocumentHub hub, HttpRequest http, CadHatchAuthoringRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadCreateHatch(request, token))));
        routes.MapGet("/{id}/hatch/pattern", (string id, StudioDocumentHub hub, HttpRequest http, long revision) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadReadHatchPattern(revision))));
        routes.MapPost("/{id}/hatch/pattern", (string id, StudioDocumentHub hub, HttpRequest http, CadHatchPatternRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadChangeHatchPattern(request, token))));
        routes.MapPost("/{id}/select", (string id, StudioDocumentHub hub, HttpRequest http, CadSelectionRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadSelect(request, token))));
        routes.MapPost("/{id}/selection-candidates", (string id, StudioDocumentHub hub, HttpRequest http, CadSelectionCandidatesRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadSelectionCandidates(request, token))));
        routes.MapPost("/{id}/snap", (string id, StudioDocumentHub hub, HttpRequest http, CadSnapRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadSnap(request, token))));
        routes.MapPost("/{id}/block/create", (string id, StudioDocumentHub hub, HttpRequest http, CadBlockCreateRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadCreateBlock(request, token))));
        routes.MapPost("/{id}/block/begin", (string id, StudioDocumentHub hub, HttpRequest http, CadBlockBeginRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadBeginBlock(request))));
        routes.MapPost("/{id}/block/finish", (string id, StudioDocumentHub hub, HttpRequest http, CadBlockFinishRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadFinishBlock(request, token))));
        routes.MapPost("/{id}/grip", (string id, StudioDocumentHub hub, HttpRequest http, CadGripRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadGrip(request))));
        routes.MapGet("/{id}/resources", (string id, StudioDocumentHub hub) => hub.Use(id, s => s.CadResources()));
        routes.MapPost("/{id}/resource", (string id, StudioDocumentHub hub, HttpRequest http, CadResourceRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadResource(request))));
        routes.MapPost("/{id}/layer-isolation", (string id, StudioDocumentHub hub, HttpRequest http, CadLayerIsolationRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayerIsolation(request, token))));
        routes.MapPost("/{id}/layer-state", (string id, StudioDocumentHub hub, HttpRequest http, CadLayerStateRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayerState(request, token))));
        routes.MapGet("/{id}/layer-state/details", (string id, StudioDocumentHub hub, HttpRequest http) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayerStateDetails(http.Query["id"].ToString()))));
        routes.MapGet("/{id}/layer-state/export", (string id, StudioDocumentHub hub, HttpRequest http, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayerStateExport(http.Query["id"].ToString(), token))));
        routes.MapPost("/{id}/layer-state/preview", (string id, StudioDocumentHub hub, HttpRequest http, CadLayerStatePreviewRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayerStatePreview(request, token))));
        routes.MapPost("/{id}/layer-state/import", (string id, StudioDocumentHub hub, HttpRequest http, CadLayerStateImportRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayerStateImport(request, token))));
        routes.MapPost("/{id}/layer", (string id, StudioDocumentHub hub, HttpRequest http, CadLayerRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadLayer(request))));
        routes.MapPost("/{id}/assign-layer", (string id, StudioDocumentHub hub, HttpRequest http, CadAssignLayerRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadAssignLayer(request))));
        routes.MapPost("/{id}/property", (string id, StudioDocumentHub hub, HttpRequest http, PropertyRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.CadProperty(request))));
        routes.MapPost("/{id}/history/{operation}", (string id, string operation, StudioDocumentHub hub, HttpRequest http, RevisionRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.History(operation, request.Revision); return s.CadState(); })));
        routes.MapPost("/{id}/layout", (string id, StudioDocumentHub hub, HttpRequest http, LayoutRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.SetLayout(request); return s.CadState(); })));
        routes.MapPost("/{id}/retain", (string id, StudioDocumentHub hub, HttpRequest http, RevisionRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.Retain(request.Revision); return s.CadState(); })));
        routes.MapPost("/{id}/fork", (string id, StudioDocumentHub hub, HttpRequest http, ForkRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.Fork(request); return s.CadState(); })));
        routes.MapPost("/{id}/workspace", (string id, StudioDocumentHub hub, HttpRequest http, WorkspaceRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.SwitchWorkspace(request); return s.CadState(); })));
        routes.MapPost("/{id}/merge/preview", (string id, StudioDocumentHub hub, HttpRequest http, MergePreviewRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.PreviewMerge(request, token))));
        routes.MapPost("/{id}/merge/apply", (string id, StudioDocumentHub hub, HttpRequest http, MergeApplyRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.ApplyMerge(request); return s.CadState(); })));
        routes.MapPost("/{id}/annotation-view", (string id, StudioDocumentHub hub, HttpRequest http, AnnotationViewRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.SetAnnotationView(request); return s.CadState(); })));
        routes.MapPost("/{id}/linetype-view", (string id, StudioDocumentHub hub, HttpRequest http, LinetypeViewRequest request) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.SetLinetypeView(request); return s.CadState(); })));
        routes.MapPost("/{id}/graph/query", (string id, StudioDocumentHub hub, HttpRequest http, GraphQueryRequest request, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.GraphQuery(request, token))));
        routes.MapGet("/{id}/graph", (string id, StudioDocumentHub hub, HttpRequest http, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () => s.Graph(token))));
        routes.MapGet("/{id}/render", (string id, StudioDocumentHub hub, HttpRequest http, int width, int height,
            double cx, double cy, double scale, string? backend, bool? white, long revision, long? snapshot, CancellationToken token) =>
            hub.Use(id, s => StudioHttp.Execute(s, http, () =>
            {
                var image = s.CadRender(width, height, cx, cy, scale, backend ?? "skia", white ?? false, revision, snapshot, token);
                return Results.Bytes(image.Bytes, image.ContentType);
            })));
        routes.MapPost("/{id}/save", (string id, StudioDocumentHub hub, HttpRequest http, CadSaveRequest request, CancellationToken token) =>
        {
            if (request.Format == "dxf")
                return Results.File(hub.Save(id, http, request.Revision), "application/dxf", "drawing.dxf");
            if (request.Format != "dwg") throw new ArgumentException("Choose DXF or DWG output.");
            var version = request.Version ?? "AC1032";
            if (!DrawingExchange.WriteVersions.Contains(version)) throw new ArgumentException("Unsupported DWG output version.");
            var bytes = hub.Save(id, http, request.Revision, dxf => new DrawingExchange().Convert(
                dxf, DrawingEncoding.Dxf, DrawingEncoding.Dwg, version, token).Content.ToArray());
            return Results.File(bytes, "application/acad", "drawing.dwg");
        });
        routes.MapPost("/{id}/load", async (string id, StudioDocumentHub hub, HttpRequest http, long revision, string name, CancellationToken token) =>
        {
            // Reject stale identities before spending conversion work. Conversion
            // holds no source references; Load rechecks the original receipt afterward.
            hub.Use(id, s => StudioHttp.Execute(s, http, () =>
            {
                if (s.CadRevision != revision) throw new ShadowConflictException("Drawing changed before import.");
                return true;
            }));
            using var stream = await ReadBounded(http, 20 * 1024 * 1024, token);
            var bytes = stream.ToArray();
            if (!DrawingExchange.IsDwg(bytes)) return hub.Load(id, http, stream, revision, name);
            var converted = new DrawingExchange().Convert(bytes, DrawingEncoding.Dwg, DrawingEncoding.Dxf, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            using var dxf = new MemoryStream(converted.Content.ToArray(), writable: false);
            return hub.Load(id, http, dxf, revision, name);
        });
        routes.MapPost("/{id}/font", async (string id, StudioDocumentHub hub, HttpRequest http, long revision, string name, CancellationToken token) =>
        {
            using var stream = await ReadBounded(http, 4 * 1024 * 1024, token);
            return hub.Use(id, s => StudioHttp.Execute(s, http, () => { s.LoadFont(revision, name, stream.ToArray()); return s.CadState(); }));
        });
    }
    private static async Task<MemoryStream> ReadBounded(HttpRequest request, int maximum, CancellationToken token)
    {
        if (request.ContentLength > maximum) throw new ArgumentException("Upload size limit exceeded.");
        var stream = new MemoryStream(); var buffer = new byte[32 * 1024];
        try
        {
            while (true)
            {
                var length = await request.Body.ReadAsync(buffer, token); if (length == 0) break;
                if (stream.Length + length > maximum) throw new ArgumentException("Upload size limit exceeded.");
                stream.Write(buffer, 0, length);
            }
            stream.Position = 0; return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
