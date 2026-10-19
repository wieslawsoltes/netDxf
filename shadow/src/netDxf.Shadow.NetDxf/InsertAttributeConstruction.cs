using System.Collections.Immutable;
using netDxf.Entities;

namespace netDxf.Shadow.NetDxf;

public sealed record InsertAttributeAssignment(ObjectId Definition, string Value);

/// <summary>Expands an empty INSERT into explicit owned attributes for preview, native adoption and history.</summary>
/// <remarks>Uses immutable definition state and native text transformation. No live source or global settings are changed.</remarks>
public static class InsertAttributeConstruction
{
    public static ShadowGraphPatch Plan(ShadowSnapshot snapshot, ObjectId insertId,
        ImmutableArray<InsertAttributeAssignment> assignments = default, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); token.ThrowIfCancellationRequested();
        if (!assignments.IsDefault && assignments.Length > 10_000) throw new ArgumentException("Insertion attribute assignment budget exceeded.");
        var before = snapshot; var initialized = AttributeSynchronization.Initialize(snapshot, insertId, token).Apply(snapshot);
        var attributes = initialized.Read(insertId)["Attributes"].Items.ToDictionary(a => a["Definition"].Id!.Value);
        var seen = new HashSet<ObjectId>(); var edit = new ShadowGraphEdit(initialized); long characters = 0;
        foreach (var assignment in assignments.IsDefault ? [] : assignments)
        {
            token.ThrowIfCancellationRequested();
            if (assignment is null || !seen.Add(assignment.Definition) || !attributes.TryGetValue(assignment.Definition, out var attribute))
                throw new ArgumentException("Each insertion value must identify a unique definition in the chosen block.");
            var value = assignment.Value;
            if (value is null || value.Length > 1_000_000 || (characters += value.Length) > 4_000_000 || value.Contains('\0'))
                throw new ArgumentException("Insertion values exceed the text budget or contain NUL.");
            var content = attribute["TextData"]["Content"];
            if (!content.Exists && value.Any(c => c is '\r' or '\n')) throw new ArgumentException("Single-line attributes cannot contain paragraph breaks.");
            if (!Enum.TryParse<AttributeFlags>(attribute["Flags"].Text, out var flags)) throw new ArgumentException("Invalid definition flags.");
            var normalized = content.Exists ? value.Replace("\r\n", "\\P").Replace("\r", "\\P").Replace("\n", "\\P") : value;
            if ((flags & AttributeFlags.Constant) != 0 && normalized != (content.Exists ? content["Value"].Text : attribute["Value"].Text))
                throw new InvalidOperationException("Constant attributes retain their definition defaults.");
            if (content.Value is ShadowValue.Structure text && attribute["TextData"].Value is ShadowValue.Structure packet)
                edit.Set(attribute.Id!.Value, "TextData", packet with { Fields = packet.Fields.SetItem("Content",
                    text with { Fields = text.Fields.SetItem("Value", ShadowValue.From(normalized)) }) });
            else edit.Set(attribute.Id!.Value, "Value", ShadowValue.From(normalized));
        }
        token.ThrowIfCancellationRequested();
        return ShadowGraphPatch.Between(before, edit.Build().Apply(initialized));
    }
}
