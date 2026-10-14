using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Editor;

/// <summary>Source-independent named header transactions. Use EditorSession.Execute for canonical history.</summary>
public static class CustomHeaderEdits
{
    public static ShadowGraphPatch Set(ShadowSnapshot snapshot, string name, short groupCode, ShadowValue value)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(value); ValidateName(name);
        var owner = snapshot.Document["DrawingVariables"];
        var entries = Entries(owner);
        var existing = entries.FirstOrDefault(v => string.Equals(v["Name"].Text, name, StringComparison.OrdinalIgnoreCase));
        var edit = new ShadowGraphEdit(snapshot);
        if (existing.Id is { } id && existing["GroupCode"].Number() == groupCode)
            edit.Set(id, "Value", value);
        else
        {
            var created = edit.Create("netDxf.Header.HeaderVariable", ShadowObjectKind.Object,
                new Dictionary<string, ShadowValue>
                {
                    ["Name"] = ShadowValue.From(name),
                    ["GroupCode"] = new ShadowValue.Atom("System.Int16", groupCode.ToString(CultureInfo.InvariantCulture)),
                    ["Value"] = value
                });
            var sequence = entries.Select(v => v.Id == existing.Id && existing.Exists
                ? (ShadowValue)new ShadowValue.Reference(created) : v.Value).ToList();
            if (!existing.Exists) sequence.Add(new ShadowValue.Reference(created));
            edit.ReplaceSequence(owner.Id!.Value, "CustomVariables", sequence);
            edit.PruneUnreachable();
        }
        return edit.Build();
    }

    public static ShadowGraphPatch Remove(ShadowSnapshot snapshot, string name)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ValidateName(name);
        var owner = snapshot.Document["DrawingVariables"]; var entries = Entries(owner);
        var edit = new ShadowGraphEdit(snapshot);
        edit.ReplaceSequence(owner.Id!.Value, "CustomVariables", entries.Where(v =>
            !string.Equals(v["Name"].Text, name, StringComparison.OrdinalIgnoreCase)).Select(v => v.Value));
        edit.PruneUnreachable(); return edit.Build();
    }

    private static ShadowReader[] Entries(ShadowReader owner)
    {
        if (owner.Id is null || owner["CustomVariables"].Value is not ShadowValue.Sequence)
            throw new NotSupportedException("This snapshot has no captured custom-header collection.");
        return owner["CustomVariables"].Items.ToArray();
    }
    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length < 2 || name[0] != '$' || name.Any(char.IsControl)) throw new ArgumentException("A header name must begin with '$' and contain no control characters.", nameof(name));
    }
}
