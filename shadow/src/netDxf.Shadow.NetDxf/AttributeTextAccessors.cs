using netDxf.Entities;

namespace netDxf.Shadow.NetDxf;

/// <summary>Native detached text packets are values, not transient graph identities.</summary>
internal static class AttributeTextAccessors
{
    internal static SourceAccessor[] Extend(Type type, SourceAccessor[] accessors)
    {
        SourceAccessor? text = type == typeof(netDxf.Entities.Attribute)
            ? new("TextData", typeof(AttributeTextData), true,
                source => ((netDxf.Entities.Attribute)source).GetTextData(false),
                (source, value) => ((netDxf.Entities.Attribute)source).SetTextData((AttributeTextData?)value))
            : type == typeof(AttributeDefinition)
            ? new("TextData", typeof(AttributeTextData), true,
                source => ((AttributeDefinition)source).GetTextData(false),
                (source, value) => ((AttributeDefinition)source).SetTextData((AttributeTextData?)value))
            : null;
        return text is null ? accessors : accessors.Append(text).OrderBy(a => a.Name, StringComparer.Ordinal).ToArray();
    }
}
