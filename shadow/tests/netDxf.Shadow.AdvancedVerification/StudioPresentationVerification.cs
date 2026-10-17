using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Studio;

internal static class StudioPresentationVerification
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        Add("conditional state polls distinguish unchanged receipts, selection and server identity", () =>
        {
            using var studio = new Studio();
            var before = Json(studio.CadState()); var receipt = before.GetProperty("state");
            Require(studio.CadStateIfChanged(Server(receipt), Change(receipt)) is null);
            Require(studio.CadStateIfChanged("another-server", Change(receipt)) is not null);
            Require(studio.CadStateIfChanged(null, Change(receipt)) is not null);
            Require(studio.CadStateIfChanged(Server(receipt), null) is not null);

            var id = before.GetProperty("items")[0].GetProperty("id").GetString()!;
            studio.CadSelect(new(Revision(receipt), "entity", Entity: id));
            var selected = Json(studio.CadStateIfChanged(Server(receipt), Change(receipt))!);
            Require(selected.GetProperty("selectedEntities").EnumerateArray().Any(value => value.GetString() == id));
            var next = selected.GetProperty("state");
            Require(Revision(next) == Revision(receipt) && Change(next) > Change(receipt));
            Require(studio.CadStateIfChanged(Server(next), Change(next)) is null);
            Require(before.GetProperty("items").GetRawText() == selected.GetProperty("items").GetRawText());
        });

        Add("cached metadata follows source edits and both history directions", () =>
        {
            using var studio = new Studio(false);
            var empty = Json(studio.CadState());
            studio.Add(new(0, "line", 50, 60));
            var added = Json(studio.CadState());
            Require(added.GetProperty("itemCount").GetInt32() == 1);
            Require(added.GetProperty("items")[0].GetProperty("bounds").GetProperty("maxX").GetDouble() >= 70);
            var stable = Json(studio.CadState());
            Require(added.GetRawText() == stable.GetRawText());
            studio.History("undo", Revision(added.GetProperty("state")));
            var undone = Json(studio.CadState());
            Require(undone.GetProperty("itemCount").GetInt32() == empty.GetProperty("itemCount").GetInt32());
            Require(undone.GetProperty("items").GetArrayLength() == 0);
            studio.History("redo", Revision(undone.GetProperty("state")));
            var redone = Json(studio.CadState());
            Require(added.GetProperty("items").GetRawText() == redone.GetProperty("items").GetRawText());
        });

        Add("scene metadata preserves source root order, all insert bounds and occurrence counts", () =>
        {
            var definition = new Block("SyntheticPair");
            definition.Entities.Add(new Line(new Vector3(-4, -2, 0), new Vector3(3, 5, 0)));
            definition.Entities.Add(new Line(new Vector3(3, 5, 0), new Vector3(17, -9, 0)));
            var document = new DxfDocument();
            document.Entities.Add(new Insert(definition, new Vector3(20, 10, 0)));
            document.Entities.Add(new Line(new Vector3(-100, -100, 0), new Vector3(-90, -95, 0)));
            document.Entities.Add(new Insert(definition, new Vector3(80, 50, 0)));
            using var studio = Loaded(document);
            var data = Json(studio.CadState()); var rows = data.GetProperty("items");
            Require(data.GetProperty("itemCount").GetInt32() == 3 && rows.GetArrayLength() == 3);
            Require(rows[0].GetProperty("type").GetString() == "Insert" && rows[1].GetProperty("type").GetString() == "Line" && rows[2].GetProperty("type").GetString() == "Insert");
            foreach (var index in new[] { 0, 2 }) Require(rows[index].GetProperty("count").GetInt32() == 2);
            var bounds = rows[0].GetProperty("bounds");
            Require(bounds.GetProperty("minX").GetDouble() <= 16 && bounds.GetProperty("maxX").GetDouble() >= 37);
            Require(bounds.GetProperty("minY").GetDouble() <= 1 && bounds.GetProperty("maxY").GetDouble() >= 15);
            Require(rows.GetRawText() == Json(studio.CadState()).GetProperty("items").GetRawText());
        });

        Add("display row limits preserve complete drawing counts", () =>
        {
            var document = new DxfDocument();
            for (var index = 0; index < 5003; index++)
                document.Entities.Add(new Line(new Vector3(index * 2, 0, 0), new Vector3(index * 2 + 1, 1, 0)));
            using var studio = Loaded(document);
            var first = Json(studio.CadState());
            Require(first.GetProperty("items").GetArrayLength() == 5000 && first.GetProperty("itemCount").GetInt32() == 5003);
            Require(first.GetProperty("state").GetProperty("items").GetArrayLength() == 2000);
            Require(first.GetProperty("state").GetProperty("occurrences").GetInt32() == 5003);
            Require(first.GetRawText() == Json(studio.CadState()).GetRawText());
            RequireSerializationParity(studio.CadState(), StudioJson.CreateOptions());
        });

        Add("generated presentation writers preserve exact JSON across every sample layout", () =>
        {
            using var studio = new Studio();
            var layouts = Json(studio.State()).GetProperty("layouts").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var options = StudioJson.CreateOptions();
            foreach (var name in layouts)
            {
                studio.SetLayout(new(studio.CadRevision, name));
                RequireSerializationParity(studio.State(), options);
                RequireSerializationParity(studio.CadState(), options);
            }
        });

        Add("generated presentation metadata honors serializer option fallbacks", () =>
        {
            using var studio = new Studio(); var data = studio.CadState();
            var plain = StudioJson.CreateOptions(); plain.PropertyNamingPolicy = null;
            var snake = StudioJson.CreateOptions(); snake.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            var pretty = StudioJson.CreateOptions(); pretty.WriteIndented = true;
            var omit = StudioJson.CreateOptions(); omit.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
            var numbers = StudioJson.CreateOptions(); numbers.NumberHandling = JsonNumberHandling.WriteAsString;
            var custom = StudioJson.CreateOptions(); custom.Converters.Add(new BoundsWriter());
            foreach (var options in new[] { plain, snake, pretty, omit, numbers, custom })
                RequireSerializationParity(data, options);
        });

        Add("generated math metadata preserves request deserialization", () =>
        {
            var options = StudioJson.CreateOptions();
            var request = JsonSerializer.Deserialize<CadSelectionRequest>("""
                {"revision":7,"mode":"window","first":{"x":"0.125","y":-2.5,"z":0},"second":{"x":3,"y":4,"z":5}}
                """, options)!;
            Require(request.Revision == 7 && request.Mode == "window" && request.First == new Vec3(0.125, -2.5, 0) && request.Second == new Vec3(3, 4, 5));
        });

        Add("generated nullable math contracts configure every native request metadata graph", () =>
        {
            var http = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;
            foreach (var options in new[] { StudioJson.CreateOptions(), StudioJson.CreateResponseOptions(http) })
            {
                options.MakeReadOnly();
                // Minimal API endpoint initialization configures these complete graphs
                // before invoking any route, including an unrelated GET state request.
                foreach (var type in typeof(Studio).Assembly.GetTypes().Where(type => type.IsPublic && type.Name.EndsWith("Request", StringComparison.Ordinal)))
                    _ = options.GetTypeInfo(type);
                var snap = JsonSerializer.Deserialize<CadSnapRequest>("""
                    {"point":{"x":1,"y":2,"z":3},"tolerance":0.5,"anchor":{"x":"0.125","y":-2.5,"z":0}}
                    """, options)!;
                var layer = JsonSerializer.Deserialize<CadLayerRequest>("""
                    {"revision":7,"layer":{"id":{"value":"N:synthetic"},"name":"Synthetic"}}
                    """, options)!;
                Require(snap.Anchor == new Vec3(0.125, -2.5, 0) && layer.Layer.Id == new ObjectId("N:synthetic"));
                Require(JsonSerializer.Deserialize<Vec3?>("null", options) is null);
                Require(JsonSerializer.Deserialize<ObjectId?>("null", options) is null);
                Require(JsonSerializer.Deserialize<Bounds2?>("{\"minX\":1,\"minY\":2,\"maxX\":3,\"maxY\":4}", options) == new Bounds2(1, 2, 3, 4));
            }
        });

        Add("response options preserve host encoding and converters without changing request options", () =>
        {
            var document = new DxfDocument();
            document.Entities.Add(new Line(Vector3.Zero, new Vector3(3, 4, 0)) { Layer = new netDxf.Tables.Layer("Synthetic-É-日本語") });
            using var studio = Loaded(document); var data = studio.CadState();
            foreach (var customize in new[] { false, true })
            {
                var configured = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;
                if (customize)
                {
                    configured.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                    configured.WriteIndented = true;
                    configured.Converters.Add(new BoundsWriter());
                }
                var originalResolver = configured.TypeInfoResolver;
                var response = StudioJson.CreateResponseOptions(configured);
                Require(!ReferenceEquals(response, configured) && ReferenceEquals(response.Encoder, configured.Encoder));
                Require(ReferenceEquals(configured.TypeInfoResolver, originalResolver));
                Require(JsonSerializer.SerializeToUtf8Bytes(data, configured).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(data, response)));
                _ = configured.GetTypeInfo(typeof(CadSnapRequest));
            }
        });

        Add("response options preserve custom host presentation resolvers and modifiers", () =>
        {
            using var studio = new Studio(); var data = studio.CadState();
            var modified = new DefaultJsonTypeInfoResolver(); modified.Modifiers.Add(HidePresentationLayer);
            foreach (var resolver in new IJsonTypeInfoResolver[] { modified, new PresentationResolver() })
            {
                var configured = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;
                configured.TypeInfoResolver = resolver;
                var expected = JsonSerializer.SerializeToUtf8Bytes(data, configured);
                var response = StudioJson.CreateResponseOptions(configured);
                Require(ReferenceEquals(response.TypeInfoResolver, resolver));
                Require(expected.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(data, response)));
                using var json = JsonDocument.Parse(expected);
                Require(!json.RootElement.GetProperty("items")[0].TryGetProperty("layer", out _));
                Require(!json.RootElement.GetProperty("state").GetProperty("items")[0].TryGetProperty("layer", out _));
            }
        });

        Add("view changes invalidate metadata without changing the source revision", () =>
        {
            using var studio = new Studio();
            var model = Json(studio.CadState()); var receipt = model.GetProperty("state");
            studio.SetLayout(new(Revision(receipt), "Hatch islands"));
            var paper = Json(studio.CadStateIfChanged(Server(receipt), Change(receipt))!);
            Require(paper.GetProperty("state").GetProperty("layout").GetString() == "Hatch islands");
            Require(paper.GetProperty("items").GetRawText() != model.GetProperty("items").GetRawText());
            studio.SetLayout(new(Revision(paper.GetProperty("state")), "Model"));
            var returned = Json(studio.CadState());
            Require(returned.GetProperty("items").GetRawText() == model.GetProperty("items").GetRawText());
        });

        Add("retained snapshots and equal-revision document replacement invalidate state receipts", () =>
        {
            using var studio = new Studio(false);
            var before = Json(studio.CadState()).GetProperty("state");
            studio.Retain(0);
            var retained = Json(studio.CadStateIfChanged(Server(before), Change(before))!).GetProperty("state");
            Require(retained.GetProperty("snapshots").GetArrayLength() == 1);
            using var bytes = new MemoryStream(); new DxfDocument().Save(bytes); bytes.Position = 0;
            studio.Load(bytes, 0);
            var replaced = Json(studio.CadStateIfChanged(Server(retained), Change(retained))!).GetProperty("state");
            Require(replaced.GetProperty("documentId").GetString() != retained.GetProperty("documentId").GetString());
            Require(Revision(replaced) == Revision(retained) && replaced.GetProperty("snapshots").GetArrayLength() == 0);
        });

        Add("raw Studio frames retain revision, snapshot and allocation guards", () =>
        {
            using var studio = new Studio(false);
            studio.Add(new(0, "line", 0, 0)); studio.Retain(1);
            var original = studio.CadRenderPixels(80, 60, 0, 0, 1, false, 1, 1, default);
            Require(original.ContentType == "application/x-shadow-rgba" && original.Bytes.Length == 80 * 60 * 4);
            studio.Add(new(1, "circle", 15, 5));
            var retained = studio.CadRenderPixels(80, 60, 0, 0, 1, false, 2, 1, default);
            Require(original.Bytes.AsSpan().SequenceEqual(retained.Bytes));
            Throws<ShadowConflictException>(() => studio.CadRenderPixels(80, 60, 0, 0, 1, false, 1, null, default));
            Throws<ShadowConflictException>(() => studio.CadRenderPixels(80, 60, 0, 0, 1, false, 2, 99, default));
            Throws<ArgumentException>(() => studio.CadRenderPixels(4096, 4096, 0, 0, 1, false, 2, null, default));
        });

        Add("conditional state and pixel reads reject disposed studios", () =>
        {
            var studio = new Studio(false); var receipt = Json(studio.CadState()).GetProperty("state");
            studio.Dispose();
            Throws<ObjectDisposedException>(() => studio.CadStateIfChanged(Server(receipt), Change(receipt)));
            Throws<ObjectDisposedException>(() => studio.CadRenderPixels(80, 60, 0, 0, 1, false, 0, null, default));
        });

        void Add(string name, Action run) => tests.Add(("Studio presentation: " + name, run));
    }

    private static Studio Loaded(DxfDocument document)
    {
        using var stream = new MemoryStream(); document.Save(stream); stream.Position = 0;
        var studio = new Studio(false);
        try { studio.Load(stream); return studio; }
        catch { studio.Dispose(); throw; }
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Web);
    private static void RequireSerializationParity(object value, JsonSerializerOptions generated)
    {
        var reflection = new JsonSerializerOptions(generated) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        Require(JsonSerializer.SerializeToUtf8Bytes(value, reflection).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(value, generated)));
    }
    private sealed class BoundsWriter : JsonConverter<Bounds2>
    {
        public override Bounds2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Bounds2 value, JsonSerializerOptions options) => writer.WriteStringValue($"{value.MinX:R}:{value.MaxY:R}");
    }
    private static void HidePresentationLayer(JsonTypeInfo info)
    {
        if (info.Type.DeclaringType == typeof(Studio) && (info.Type.Name is "StateItem" or "CadItem"))
            foreach (var property in info.Properties.Where(property => property.Name == "layer"))
                property.ShouldSerialize = static (_, _) => false;
    }
    private sealed class PresentationResolver : DefaultJsonTypeInfoResolver
    {
        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var info = base.GetTypeInfo(type, options); HidePresentationLayer(info); return info;
        }
    }
    private static string Server(JsonElement state) => state.GetProperty("serverId").GetString()!;
    private static long Change(JsonElement state) => state.GetProperty("changeVersion").GetInt64();
    private static long Revision(JsonElement state) => state.GetProperty("revision").GetInt64();
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Studio presentation invariant failed."); }
    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
}
