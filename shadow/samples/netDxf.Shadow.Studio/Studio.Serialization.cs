using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Studio;

/// <summary>The Studio wire format, with generated writers for its large presentation lists.
/// Other response and request types retain the ordinary System.Text.Json contract.</summary>
public static class StudioJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(Studio.PresentationJsonResolver, new DefaultJsonTypeInfoResolver());
        return options;
    }
}

public sealed partial class Studio
{
    internal static IJsonTypeInfoResolver PresentationJsonResolver => PresentationJsonContext.Default;

    // These are the same immutable values used by both state APIs. Generating their
    // writers avoids thousands of reflection getter calls per response in WebAssembly,
    // while metadata generation preserves option and deserialization fallbacks.
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(ImmutableArray<StateItem>))]
    [JsonSerializable(typeof(ImmutableArray<StateDiagnostic>))]
    [JsonSerializable(typeof(ImmutableArray<CadItem>))]
    [JsonSerializable(typeof(ImmutableArray<CadBlock>))]
    [JsonSerializable(typeof(ImmutableArray<DraftingLayer>))]
    private sealed partial class PresentationJsonContext : JsonSerializerContext { }
}
