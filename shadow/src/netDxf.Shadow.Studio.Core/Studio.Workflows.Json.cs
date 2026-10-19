using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace netDxf.Shadow.Studio;

internal static class WorkflowWireJson
{
    internal static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = StudioJson.CreateOptions(); options.Converters.Add(new EmptyImmutableArrays()); return options;
    }
    private sealed class EmptyImmutableArrays : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ArrayConverter<>).MakeGenericType(type.GenericTypeArguments[0]))!;
    }
    private sealed class ArrayConverter<T> : JsonConverter<ImmutableArray<T>>
    {
        public override ImmutableArray<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => (JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? []).ToImmutableArray();
        public override void Write(Utf8JsonWriter writer, ImmutableArray<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray(); if (!value.IsDefault) foreach (var item in value) JsonSerializer.Serialize(writer, item, options); writer.WriteEndArray();
        }
    }
}
