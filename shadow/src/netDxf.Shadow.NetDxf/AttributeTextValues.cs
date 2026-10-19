using System.Collections.Immutable;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Shadow.NetDxf;

internal static class AttributeTextValues
{
    internal static ShadowValue Read(object data, Func<object?, int, ShadowValue> read, int maximumItems)
    {
        ShadowValue Field(object? value) => read(value, 1);
        ShadowValue Tags(IReadOnlyCollection<DxfTag> tags)
        {
            if (tags.Count > maximumItems) throw new InvalidOperationException("Shadow collection budget exceeded.");
            return new ShadowValue.Sequence(tags.Select(tag =>
            (ShadowValue)new ShadowValue.Structure(typeof(DxfTag).FullName!, new Dictionary<string, ShadowValue>
            { ["Code"] = read(tag.Code, 4), ["Value"] = read(tag.Value, 4) }.ToImmutableDictionary())).ToImmutableArray());
        }
        var fields = new Dictionary<string, ShadowValue>();
        if (data is AttributeMText content)
        {
            fields["Value"] = Field(content.Value); fields["Style"] = Field(content.Style);
            fields["Fields"] = Tags(content.GetStoredFields());
        }
        else
        {
            var text = (AttributeTextData)data;
            fields["Version"] = Field(text.Version); fields["Kind"] = Field(text.Kind);
            fields["SecondaryFlag"] = Field(text.SecondaryFlag); fields["FieldLength"] = Field(text.FieldLength);
            fields["Locked"] = Field(text.Locked); fields["SecondaryAlignment"] = Field(text.SecondaryAlignment);
            fields["Content"] = Field(text.Content); fields["Auxiliary"] = Tags(text.Auxiliary.ToArray());
        }
        return new ShadowValue.Structure(data.GetType().FullName!, fields.ToImmutableDictionary());
    }

    internal static object Convert(ShadowValue.Structure value, Type target, Func<ShadowValue, Type, object?> convert)
    {
        if (value.TypeName != target.FullName) throw new ArgumentException("Attribute text value has the wrong stored type.");
        T? Field<T>(string name) => (T?)convert(value.Fields[name], typeof(T));
        void RequireFields(params string[] names)
        {
            if (value.Fields.Count != names.Length || names.Any(name => !value.Fields.ContainsKey(name)))
                throw new ArgumentException("Attribute text value has missing or unknown fields.");
        }
        DxfTag[] Tags(string name, int maximum)
        {
            if (!value.Fields.TryGetValue(name, out var item) || item is not ShadowValue.Sequence sequence || sequence.Items.Length > maximum)
                throw new ArgumentException("Attribute text tags require a bounded sequence.");
            return sequence.Items.Select(v =>
            {
                if (v is not ShadowValue.Structure tag || tag.TypeName != typeof(DxfTag).FullName || tag.Fields.Count != 2
                    || !tag.Fields.ContainsKey("Code") || !tag.Fields.ContainsKey("Value"))
                    throw new ArgumentException("Invalid attribute text tag.");
                return new DxfTag((short)convert(tag.Fields["Code"], typeof(short))!, convert(tag.Fields["Value"], typeof(object))!);
            }).ToArray();
        }
        if (target == typeof(AttributeMText))
        {
            RequireFields("Value", "Style", "Fields");
            return AttributeMText.FromStoredFields(Field<string>("Value")!, Field<TextStyle>("Style")!, Tags("Fields", 128));
        }
        RequireFields("Version", "Kind", "SecondaryFlag", "FieldLength", "Locked", "SecondaryAlignment", "Content", "Auxiliary");
        var result = new AttributeTextData { Version = Field<short?>("Version"), Kind = Field<short?>("Kind"),
            SecondaryFlag = Field<short?>("SecondaryFlag"), FieldLength = Field<short?>("FieldLength"),
            Locked = Field<bool?>("Locked"), SecondaryAlignment = Field<Vector3?>("SecondaryAlignment"), Content = Field<AttributeMText>("Content") };
        foreach (var tag in Tags("Auxiliary", 4096)) result.Auxiliary.Add(tag);
        return result;
    }
}
