using netDxf.Shadow.Exchange.Dwg;

namespace netDxf.Shadow.Studio.Browser;

internal sealed partial class BrowserApplication
{
    private sealed record SaveRequest(long Revision, string Format = "dxf", string? Version = null);
    private const int DrawingLimit = 20 * 1024 * 1024;
    private const int FontLimit = 4 * 1024 * 1024;
    private static readonly CancellationToken Token = CancellationToken.None;

    private void MapRoutes()
    {
        Map("GET", "/api/health", _ => BrowserReply.Value(new { status = "ok" }));
        Map("GET", "/api/browser/environment", _ => BrowserReply.Value(new
        {
            runtime = "blazor-webassembly", serverRequired = false,
            fonts = "Bundled Noto Sans; explicit TTF/OTF, SHX/SHP and BIGFONT resources.",
            routes = routes.Select(r => new { r.Method, r.Pattern }).ToArray()
        }));
        Map("GET", "/api/state", _ => BrowserReply.Value(studio.State()));
        Map("GET", "/api/render", r => studio.ExecuteRequest(r.Identity(), () =>
        {
            var image = studio.Render(r.Int("width"), r.Int("height"), r.OptionalDouble("cx"), r.OptionalDouble("cy"),
                r.OptionalDouble("scale"), r.Optional("backend") ?? "skia", r.OptionalLong("snapshot"), Token, r.Optional("merge"));
            return BrowserReply.Binary(image.Bytes, image.ContentType);
        }));
        Legacy("GET", "/api/graph", (s, _) => s.Graph(Token));
        Legacy("GET", "/api/object/{id}", (s, r) => s.Inspect(new(r.Parameter("id"))));
        Legacy("POST", "/api/pick", (s, r) => s.Pick(r.Body<PickRequest>()));
        Legacy("POST", "/api/select", (s, r) => s.Select(r.Body<SelectRequest>()));
        Legacy("POST", "/api/transform", (s, r) => s.Transform(r.Body<TransformRequest>()));
        Legacy("POST", "/api/translate", (s, r) => s.Translate(r.Body<MoveRequest>()));
        Legacy("POST", "/api/property", (s, r) => s.SetProperty(r.Body<PropertyRequest>()));
        Legacy("POST", "/api/history/{operation}", (s, r) => s.History(r.Parameter("operation"), r.Body<RevisionRequest>().Revision));
        Legacy("POST", "/api/annotation-view", (s, r) => s.SetAnnotationView(r.Body<AnnotationViewRequest>()));
        Legacy("POST", "/api/perspective-view", (s, r) => s.SetPerspectiveView(r.Body<PerspectiveViewRequest>()));
        Legacy("POST", "/api/translate-perspective", (s, r) => s.TranslatePerspective(r.Body<PerspectiveMoveRequest>()));
        Legacy("POST", "/api/linetype-view", (s, r) => s.SetLinetypeView(r.Body<LinetypeViewRequest>()));
        Legacy("POST", "/api/layout", (s, r) => s.SetLayout(r.Body<LayoutRequest>()));
        Legacy("POST", "/api/add", (s, r) => s.Add(r.Body<AddRequest>()));
        Legacy("POST", "/api/delete", (s, r) => s.Delete(r.Body<RevisionRequest>().Revision));
        Legacy("POST", "/api/source-demo", (s, r) => s.SourceDemo(r.Body<RevisionRequest>().Revision));
        Legacy("POST", "/api/snapshot", (s, r) => s.Retain(r.Body<RevisionRequest>().Revision));
        Legacy("POST", "/api/load", (s, r) =>
        {
            using var stream = new MemoryStream(r.Bytes(DrawingLimit), writable: false);
            s.Load(stream, r.Long("revision")); return s.State();
        });
        Legacy("POST", "/api/font", (s, r) => s.LoadBrowserFont(r.Long("revision"), r.Text("name"), r.Bytes(FontLimit)));
        Legacy("POST", "/api/bigfont-map", (s, r) => s.LoadBigFontMap(r.Long("revision"), r.Bytes(FontLimit), Token));
        Map("POST", "/api/save", r => studio.ExecuteRequest(r.Identity(), () => BrowserReply.Binary(studio.Save(r.Body<RevisionRequest>().Revision), "application/dxf")));
        Legacy("POST", "/api/branches", (s, r) => s.Fork(r.Body<ForkRequest>()));
        Legacy("POST", "/api/workspace", (s, r) => s.SwitchWorkspace(r.Body<WorkspaceRequest>()));
        Legacy("POST", "/api/branches/discard", (s, r) => s.DiscardBranch(r.Body<BranchDeleteRequest>()));
        Legacy("POST", "/api/merge/preview", (s, r) => s.PreviewMerge(r.Body<MergePreviewRequest>(), Token));
        Legacy("POST", "/api/merge/apply", (s, r) => s.ApplyMerge(r.Body<MergeApplyRequest>()));
        Map("GET", "/api/events", _ => BrowserReply.Error(400, "Use the browser event stream transport."));
        MapCadRoutes();
    }
}
