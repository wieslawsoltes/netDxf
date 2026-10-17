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

    /// <summary>Use presentation writers for a response while preserving the host's
    /// configured encoding, converters and other JSON settings. The input options are unchanged.</summary>
    public static JsonSerializerOptions CreateResponseOptions(JsonSerializerOptions configured)
    {
        var options = new JsonSerializerOptions(configured);
        // A host resolver or modifier may intentionally change these exact contracts.
        // In that case its result takes precedence over this optional optimization.
        if (configured.TypeInfoResolver is not null &&
            (configured.TypeInfoResolverChain.Count == 0 || configured.TypeInfoResolverChain.Any(resolver =>
                resolver.GetType() != typeof(DefaultJsonTypeInfoResolver) || ((DefaultJsonTypeInfoResolver)resolver).Modifiers.Count != 0)))
            return options;
        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(Studio.PresentationJsonResolver,
            configured.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver());
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
    // .NET 8 cannot reflect nullable wrappers around generated constructor-based
    // struct converters. Include the public math wrappers used by request contracts.
    [JsonSerializable(typeof(Vec3?))]
    [JsonSerializable(typeof(ObjectId?))]
    [JsonSerializable(typeof(Bounds2?))]
    private sealed partial class PresentationJsonContext : JsonSerializerContext { }
}
