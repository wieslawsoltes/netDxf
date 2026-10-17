using netDxf.Shadow.Exchange.Dwg;

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
        Cad("POST", "/api/cad/{id}/block/define", (s, r) => s.CadDefineBlock(r.Body<CadBlockDefinitionRequest>(), Token));
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
