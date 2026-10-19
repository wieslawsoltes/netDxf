using netDxf.Shadow.Exchange.Dwg;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio.Browser;

internal sealed partial class BrowserApplication
{
    private void MapCadRoutes()
    {
        Map("GET", "/api/cad/formats", _ => BrowserReply.Value(new
        {
            provider = DrawingExchange.Provider, dwgWriteVersions = DrawingExchange.WriteVersions, defaultDwgVersion = "AC1032"
        }));
        Map("GET", "/api/cad/documents", _ => BrowserReply.Value(documents.Documents()));
        Map("POST", "/api/cad/documents", r => BrowserReply.Value(documents.Create(r.Body<CadNewDocumentRequest>())));
        Map("POST", "/api/cad/{id}/close", r => BrowserReply.Value(documents.Close(r.Parameter("id"), r.Identity(), r.Body<CadCloseDocumentRequest>())));
        Map("GET", "/api/cad/{id}/state", r => documents.Use(r.Parameter("id"), s => BrowserReply.Value(s.CadStateIfChanged(r.Optional("knownServer"), r.OptionalLong("knownChange")))));
        Cad("POST", "/api/cad/{id}/command", (s, r) => s.CadCommand(r.Body<CadCommandRequest>(), Token));
        Cad("POST", "/api/cad/{id}/insert", (s, r) => s.CadInsert(r.Body<CadInsertRequest>(), Token));
        Cad("GET", "/api/cad/{id}/modules", (s, r) => s.CadModules());
        Cad("POST", "/api/cad/{id}/modules/toggle", (s, r) => s.CadSetModule(r.Body<CadModuleToggleRequest>()));
        Cad("POST", "/api/cad/{id}/modules/symbols", (s, r) => s.CadModuleSymbols(r.Body<CadModuleCatalogRequest>()));
        Cad("POST", "/api/cad/{id}/modules/analyze", (s, r) => s.CadAnalyzeModule(r.Body<CadModuleAnalysisRequest>(), Token));
        Cad("POST", "/api/cad/{id}/modules/command", (s, r) => s.CadRunModule(r.Body<CadModuleExecuteRequest>(), Token));
        Cad("POST", "/api/cad/{id}/references/capture", (s, r) => s.CaptureReferences(r.Body<DrawingReferenceCaptureRequest>()));
        Cad("POST", "/api/cad/{id}/references/validate", (s, r) => s.ValidateReferences(r.Body<DrawingReferenceValidationRequest>()));
        Cad("POST", "/api/cad/{id}/health", (s, r) => s.CadHealth(r.Body<CadHealthRequest>(), Token));
        Cad("POST", "/api/cad/{id}/health/select", (s, r) => s.CadHealthSelect(r.Body<CadHealthSelectRequest>()));
        Cad("GET", "/api/cad/{id}/edit-previews", (s, r) => s.CadEditPreviews());
        Cad("GET", "/api/cad/{id}/workflow/status", (s, r) => s.CadWorkflowStatus());
        Cad("POST", "/api/cad/{id}/workflow/start", (s, r) => s.CadWorkflowStart(r.Body<CadWorkflowStartRequest>()));
        Cad("POST", "/api/cad/{id}/workflow/stop", (s, r) => s.CadWorkflowStop(r.Body<RevisionRequest>()));
        Cad("GET", "/api/cad/{id}/workflow/export", (s, r) => s.CadWorkflowExport());
        Cad("POST", "/api/cad/{id}/workflow/replay", (s, r) => s.CadWorkflowReplay(r.Body<CadWorkflowReplayRequest>(), Token));
        Cad("POST", "/api/cad/{id}/workflow/roundtrip", (s, r) => s.CadWorkflowRoundTrip(r.Body<RevisionRequest>()));
        Cad("POST", "/api/cad/{id}/edit-preview", (s, r) => s.CadEditPreview(r.Body<CadEditPreviewRequest>(), Token));
        Cad("POST", "/api/cad/{id}/edit-preview/apply", (s, r) => s.CadEditPreviewApply(r.Body<CadEditPreviewTokenRequest>()));
        Cad("POST", "/api/cad/{id}/edit-preview/discard", (s, r) => s.CadEditPreviewDiscard(r.Body<CadEditPreviewTokenRequest>()));
        Map("GET", "/api/cad/{id}/edit-preview/render", r => documents.Use(r.Parameter("id"), s => s.ExecuteRequest(r.Identity(), () => {
            var image = s.CadEditPreviewRender(r.Text("previewId"), r.Boolean("after"), r.Int("width"), r.Int("height"), r.Optional("backend") ?? "svg", Token, r.Optional("block"));
            return BrowserReply.Binary(image.Bytes, image.ContentType);
        })));
        Cad("POST", "/api/cad/{id}/block/define", (s, r) => s.CadDefineBlock(r.Body<CadBlockDefinitionRequest>(), Token));
        Cad("GET", "/api/cad/{id}/block-libraries", (s, r) => s.CadBlockLibraries());
        Cad("POST", "/api/cad/{id}/block-library/open", (s, r) => s.CadOpenBlockLibrary(r.Long("revision"), r.Text("name"), r.Bytes(netDxf.Shadow.Symbols.DrawingBlockLibrary.MaximumBytes), Token));
        Map("GET", "/api/cad/{id}/block-library/export", r => documents.Use(r.Parameter("id"), s => s.ExecuteRequest(r.Identity(), () =>
            BrowserReply.Binary(s.CadExportBlockLibrary(new(r.Long("revision"), new(r.Text("block")), r.Boolean("binary")), Token), "application/dxf"))));
        Cad("GET", "/api/cad/{id}/attributes", (s, r) => s.CadReadAttributes(r.Long("revision")));
        Cad("POST", "/api/cad/{id}/attributes", (s, r) => s.CadEditAttributes(r.Body<CadAttributeRequest>(), Token));
        Cad("POST", "/api/cad/{id}/insert-preview", (s, r) => s.CadInsertPreview(r.Body<CadInsertPreviewRequest>(), Token));
        Cad("GET", "/api/cad/{id}/attribute-definitions", (s, r) => s.CadReadAttributeDefinitions(r.Long("revision"), r.Optional("block"), r.Boolean("currentSpace")));
        Cad("POST", "/api/cad/{id}/attribute-definitions", (s, r) => s.CadEditAttributeDefinitions(r.Body<CadAttributeDefinitionRequest>(), Token));
        Cad("POST", "/api/cad/{id}/block/rename", (s, r) => s.CadRenameBlock(r.Body<CadBlockRenameRequest>()));
        Cad("POST", "/api/cad/{id}/hatch/create", (s, r) => s.CadCreateHatch(r.Body<CadHatchAuthoringRequest>(), Token));
        Cad("GET", "/api/cad/{id}/hatch/pattern", (s, r) => s.CadReadHatchPattern(r.Long("revision")));
        Cad("POST", "/api/cad/{id}/hatch/pattern", (s, r) => s.CadChangeHatchPattern(r.Body<CadHatchPatternRequest>(), Token));
        Cad("POST", "/api/cad/{id}/select", (s, r) => s.CadSelect(r.Body<CadSelectionRequest>(), Token));
        Cad("POST", "/api/cad/{id}/snap", (s, r) => s.CadSnap(r.Body<CadSnapRequest>(), Token));
        Cad("POST", "/api/cad/{id}/block/create", (s, r) => s.CadCreateBlock(r.Body<CadBlockCreateRequest>(), Token));
        Cad("POST", "/api/cad/{id}/block/begin", (s, r) => s.CadBeginBlock(r.Body<CadBlockBeginRequest>()));
        Cad("POST", "/api/cad/{id}/block/finish", (s, r) => s.CadFinishBlock(r.Body<CadBlockFinishRequest>(), Token));
        Cad("POST", "/api/cad/{id}/grip", (s, r) => s.CadGrip(r.Body<CadGripRequest>()));
        Cad("GET", "/api/cad/{id}/resources", (s, r) => s.CadResources());
        Cad("POST", "/api/cad/{id}/layer-state", (s, r) => s.CadLayerState(r.Body<CadLayerStateRequest>(), Token));
        Cad("POST", "/api/cad/{id}/resource", (s, r) => s.CadResource(r.Body<CadResourceRequest>()));
        Cad("POST", "/api/cad/{id}/layer", (s, r) => s.CadLayer(r.Body<CadLayerRequest>()));
        Cad("POST", "/api/cad/{id}/assign-layer", (s, r) => s.CadAssignLayer(r.Body<CadAssignLayerRequest>()));
        Cad("POST", "/api/cad/{id}/property", (s, r) => s.CadProperty(r.Body<PropertyRequest>()));
        Cad("POST", "/api/cad/{id}/history/{operation}", (s, r) => { s.History(r.Parameter("operation"), r.Body<RevisionRequest>().Revision); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/layout", (s, r) => { s.SetLayout(r.Body<LayoutRequest>()); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/retain", (s, r) => { s.Retain(r.Body<RevisionRequest>().Revision); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/fork", (s, r) => { s.Fork(r.Body<ForkRequest>()); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/workspace", (s, r) => { s.SwitchWorkspace(r.Body<WorkspaceRequest>()); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/merge/preview", (s, r) => s.PreviewMerge(r.Body<MergePreviewRequest>(), Token));
        Cad("POST", "/api/cad/{id}/merge/apply", (s, r) => { s.ApplyMerge(r.Body<MergeApplyRequest>()); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/annotation-view", (s, r) => { s.SetAnnotationView(r.Body<AnnotationViewRequest>()); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/linetype-view", (s, r) => { s.SetLinetypeView(r.Body<LinetypeViewRequest>()); return s.CadState(); });
        Cad("POST", "/api/cad/{id}/graph/query", (s, r) => s.GraphQuery(r.Body<GraphQueryRequest>(), Token));
        Cad("GET", "/api/cad/{id}/graph", (s, _) => s.Graph(Token));
        Map("GET", "/api/cad/{id}/render", r => documents.Use(r.Parameter("id"), s => s.ExecuteRequest(r.Identity(), () =>
        {
            var backend = r.Optional("backend") ?? "skia";
            if (r.Boolean("pixels") && backend != "skia") throw new ArgumentException("Pixel frames require the Skia backend.");
            var image = r.Boolean("pixels")
                ? s.CadRenderPixels(r.Int("width"), r.Int("height"), r.Double("cx"), r.Double("cy"), r.Double("scale"),
                    r.Boolean("white"), r.Long("revision"), r.OptionalLong("snapshot"), Token)
                : s.CadRender(r.Int("width"), r.Int("height"), r.Double("cx"), r.Double("cy"), r.Double("scale"),
                    backend, r.Boolean("white"), r.Long("revision"), r.OptionalLong("snapshot"), Token);
            return BrowserReply.Binary(image.Bytes, image.ContentType);
        })));
        Map("POST", "/api/cad/{id}/save", r =>
        {
            var request = r.Body<SaveRequest>(); var identity = r.Identity(); var id = r.Parameter("id");
            if (request.Format == "dxf") return BrowserReply.Binary(documents.Save(id, identity, request.Revision), "application/dxf");
            if (request.Format != "dwg") throw new ArgumentException("Choose DXF or DWG output.");
            var version = request.Version ?? "AC1032";
            if (!DrawingExchange.WriteVersions.Contains(version)) throw new ArgumentException("Unsupported DWG output version.");
            var bytes = documents.Save(id, identity, request.Revision, dxf => new DrawingExchange().Convert(
                dxf, DrawingEncoding.Dxf, DrawingEncoding.Dwg, version, Token).Content.ToArray());
            return BrowserReply.Binary(bytes, "application/acad");
        });
        Map("POST", "/api/cad/{id}/load", r =>
        {
            var id = r.Parameter("id"); var identity = r.Identity(); var revision = r.Long("revision");
            documents.Use(id, s => s.ExecuteRequest(identity, () =>
            {
                if (s.CadRevision != revision) throw new ShadowConflictException("Drawing changed before import.");
                return true;
            }));
            var bytes = r.Bytes(DrawingLimit);
            if (DrawingExchange.IsDwg(bytes))
                bytes = new DrawingExchange().Convert(bytes, DrawingEncoding.Dwg, DrawingEncoding.Dxf, cancellationToken: Token).Content.ToArray();
            using var stream = new MemoryStream(bytes, writable: false);
            return BrowserReply.Value(documents.Load(id, identity, stream, revision, r.Text("name")));
        });
        Cad("POST", "/api/cad/{id}/font", (s, r) =>
        {
            s.LoadBrowserFont(r.Long("revision"), r.Text("name"), r.Bytes(FontLimit)); return s.CadState();
        });
    }
}
