using netDxf.Blocks;
using netDxf.Entities;
using Att = netDxf.Entities.Attribute;

namespace netDxf.Shadow.NetDxf;

/// <summary>Definition names and instance bindings move between collection removal and adoption.</summary>
internal sealed class SourceAttributeIdentityPlan
{
    private sealed record Rename(AttributeDefinition Definition, Block Owner, string Before, string After);
    private readonly List<Rename[]> groups = new();
    private readonly List<AttributeDefinitionBindingChange> bindings = new();
    private readonly Dictionary<ObjectId, HashSet<string>> consumed = new();
    private readonly Dictionary<AttributeDefinition, string> names = new(ReferenceEqualityComparer.Instance);
    private int appliedGroups;
    private int appliedBindings;

    internal string Tag(AttributeDefinition definition) => names.GetValueOrDefault(definition, definition.Tag);
    internal bool Handles(ObjectId id, string name) => consumed.TryGetValue(id, out var properties) && properties.Contains(name);

    internal static SourceAttributeIdentityPlan Prepare(ShadowGraphPatch patch, ShadowSnapshot target, Func<ObjectId, object> resolve,
        ShadowSnapshot? bindingHistory = null)
    {
        var plan = new SourceAttributeIdentityPlan();
        var owners = new Dictionary<Block, List<Rename>>(ReferenceEqualityComparer.Instance);
        var memberships = new Dictionary<ObjectId, HashSet<ObjectId?>>();
        foreach (var change in patch.Changes.Where(c => c.Before is not null && c.After is not null &&
                     c.After.TypeName == typeof(AttributeDefinition).FullName && !ShadowValue.Equivalent(c.Before["Tag"], c.After["Tag"])))
        {
            var definition = (AttributeDefinition)resolve(change.Id);
            if (definition.Owner is not { } owner || change.Before!["Owner"] is not ShadowValue.Reference ownerReference ||
                !ShadowValue.Equivalent(change.Before["Owner"], change.After!["Owner"]))
                throw new NotSupportedException("Rename definitions while they remain in their owning block.");
            var tag = RequiredTag(change.After["Tag"]);
            if (owner.Flags.HasFlag(BlockTypeFlags.ExternallyDependent)) throw new InvalidOperationException("Externally dependent definitions cannot be renamed.");
            if (!memberships.TryGetValue(ownerReference.Id, out var members))
            {
                var entries = target.Read(ownerReference.Id)["AttributeDefinitions"].Items.ToArray();
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                    if (!unique.Add(entry["Key"].Text) || !string.Equals(entry["Key"].Text, entry["Value"]["Tag"].Text, StringComparison.Ordinal))
                        throw new ArgumentException("Definition renames require matching unique dictionary tags in the proposed graph.");
                memberships.Add(ownerReference.Id, members = entries.Select(e => e["Value"].Id).ToHashSet());
            }
            if (!members.Contains(change.Id)) throw new ArgumentException("A renamed definition must remain in its block.");
            if (!owners.TryGetValue(owner, out var group)) owners.Add(owner, group = new());
            group.Add(new(definition, owner, definition.Tag, tag)); plan.names.Add(definition, tag);
            plan.consumed.Add(change.Id, new(StringComparer.Ordinal) { "Tag" });
        }
        plan.groups.AddRange(owners.Values.Select(group => group.ToArray()));
        foreach (var change in patch.Changes.Where(c => c.After?.TypeName == typeof(Att).FullName))
        {
            if (change.Before is { } previous && ShadowValue.Equivalent(previous["Tag"], change.After!["Tag"]) &&
                ShadowValue.Equivalent(previous["Definition"], change.After["Definition"])) continue;
            var after = change.After!; var attribute = (Att)resolve(change.Id);
            var definition = after["Definition"] is ShadowValue.Reference reference ? (AttributeDefinition)resolve(reference.Id) : null;
            var definitionTag = after["Definition"] is ShadowValue.Reference id ? target.Read(id.Id)["Tag"].Text : null;
            var tag = after["Tag"] is ShadowValue.NullValue && change.Before is null && definitionTag is not null
                ? definitionTag : RequiredTag(after["Tag"]);
            var restoring = bindingHistory is not null && bindingHistory.Objects.TryGetValue(change.Id, out var saved) &&
                ShadowValue.Equivalent(saved["Tag"], ShadowValue.From(tag)) && ShadowValue.Equivalent(saved["Definition"], after["Definition"]);
            if (!restoring && definition is not null && !string.Equals(tag, definitionTag, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The proposed attribute tag must match its proposed definition.");
            plan.bindings.Add(attribute.PrepareDefinitionBinding(tag, definition, allowTagMismatch: restoring));
            plan.consumed.Add(change.Id, new(StringComparer.Ordinal) { "Tag", "Definition" });
        }
        return plan;
    }

    internal void Apply()
    {
        // Prepare every block before changing any tag. Actual removals have now freed
        // tags that the destination reuses, but all retained definitions remain owned.
        var prepared = groups.Select(group => group[0].Owner.PrepareAttributeDefinitionRenames(
            group.Select(rename => new KeyValuePair<string, string>(rename.Before, rename.After)))).ToArray();
        foreach (var rename in prepared) { rename.Apply(); appliedGroups++; }
        foreach (var binding in bindings) { binding.Apply(); appliedBindings++; }
    }

    internal void Rollback()
    {
        var errors = new List<Exception>();
        for (int i = appliedBindings - 1; i >= 0; i--)
            try { bindings[i].Rollback(); } catch (Exception error) { errors.Add(error); }
        appliedBindings = 0;
        foreach (var group in groups.Take(appliedGroups))
            try { group[0].Owner.RenameAttributeDefinitions(group.Select(rename => new KeyValuePair<string, string>(rename.After, rename.Before))); }
            catch (Exception error) { errors.Add(error); }
        appliedGroups = 0;
        if (errors.Count != 0) throw new AggregateException("Attribute identity rollback was incomplete.", errors);
    }

    private static string RequiredTag(ShadowValue value) => value is ShadowValue.Atom { Text.Length: > 0 } atom && atom.TypeName == typeof(string).FullName
        ? atom.Text : throw new ArgumentException("Attribute tags must be nonempty strings.");
}
