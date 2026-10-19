using System.Collections.Immutable;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;
using Att = netDxf.Entities.Attribute;

namespace netDxf.Shadow.NetDxf;

public sealed record AttributeSynchronizationOptions(bool ConvertTextModes = false, int MaximumReferences = 10_000,
    int MaximumAttributes = 100_000);
public sealed record AttributeSynchronizationRemoval(ObjectId Attribute, string Tag, string FallbackValue, string? MultilineValue);
public sealed record AttributeSynchronizationReference(ObjectId Insert, ImmutableArray<ObjectId> Added,
    ImmutableArray<ObjectId> Retained, ImmutableArray<AttributeSynchronizationRemoval> Removed,
    ImmutableArray<string> RemovedXData, ImmutableArray<string> TextConversions);
public sealed record AttributeSynchronizationPlan(ObjectId Block, string BlockName, ShadowGraphPatch Patch,
    ImmutableArray<AttributeSynchronizationReference> References);

/// <summary>Prepares all references of a named block from immutable state. No live source is accessed.</summary>
/// <remarks>Preserves retained attribute identities and assigned values, resets definition appearance,
/// and removes INSERT XData. The native adapter validates reference/ownership constraints when applying
/// the patch. Callers must review the reset and removed-value report before applying one transaction.</remarks>
public static class AttributeSynchronization
{
    private static readonly string[] Geometry = ["Position", "Normal", "Height", "Width", "WidthFactor", "ObliqueAngle",
        "Rotation", "Alignment", "IsBackward", "IsUpsideDown"];
    private static readonly string[] Appearance = ["Layer", "Linetype", "Color", "Transparency", "Style", "Flags",
        "Lineweight", "LinetypeScale", "IsVisible", "ColorName", "ShadowMode"];

    public static AttributeSynchronizationPlan Plan(ShadowSnapshot snapshot, ObjectId blockId,
        AttributeSynchronizationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); cancellationToken.ThrowIfCancellationRequested(); options ??= new();
        if (options.MaximumReferences is < 1 or > 100_000 || options.MaximumAttributes is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(options));
        var block = snapshot.Read(blockId);
        if (block.Type != "Block" || block["Name"].Text.StartsWith('*') || block["IsXRef"].Boolean()
            || !snapshot.Document["Blocks"].Items.Any(b => b.Id == blockId))
            throw new ArgumentException("Choose a local named block definition.");
        var definitions = block["AttributeDefinitions"]["Values"].Items.Take(options.MaximumAttributes + 1).ToArray();
        if (definitions.Length > options.MaximumAttributes) throw new ArgumentException("Attribute definition budget exceeded.");
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
            if (definition.Type != "AttributeDefinition" || definition["Owner"].Id != blockId || !tags.Add(definition["Tag"].Text))
                throw new ArgumentException("The block has inconsistent attribute definitions.");
        var inserts = snapshot.EntityIds.Select(snapshot.Read).Where(e => e.Type == "Insert" && e["Block"].Id == blockId)
            .Take(options.MaximumReferences + 1).OrderBy(e => e.Id!.Value.Value, StringComparer.Ordinal).ToArray();
        if (inserts.Length > options.MaximumReferences) throw new ArgumentException("Block reference budget exceeded.");
        var conversion = new DetachedTextConversion(snapshot);
        var nativeDefinitions = definitions.Select(d => { cancellationToken.ThrowIfCancellationRequested(); return conversion.Definition(d); }).ToArray();
        var frame = new Block("ATTRIBUTE_FRAME") { Origin = conversion.Value<Vector3>(block["Origin"]) };
        frame.Record.Units = conversion.Value<DrawingUnits>(block["Record"]["Units"]);
        var mirrorText = snapshot.Document["DrawingVariables"]["MirrText"].Boolean();
        var edit = new ShadowGraphEdit(snapshot); var reports = ImmutableArray.CreateBuilder<AttributeSynchronizationReference>();
        var memberships = new Dictionary<ObjectId, HashSet<ObjectId?>>();
        var lockedDefinitions = definitions.Any(d => d["Layer"]["IsLocked"].Boolean());
        int count = 0;
        foreach (var insert in inserts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = insert["Owner"];
            if (owner.Type != "Block" || owner["IsXRef"].Boolean())
                throw new ArgumentException("A block reference has inconsistent or external ownership.");
            if (!memberships.TryGetValue(owner.Id!.Value, out var members))
                memberships[owner.Id.Value] = members = owner["Entities"].Items.Select(e => e.Id).ToHashSet();
            if (!members.Contains(insert.Id)) throw new ArgumentException("A block reference is absent from its owner's entity collection.");
            if (insert["Layer"]["IsLocked"].Boolean()) throw new InvalidOperationException("A block reference is on a locked layer.");
            var attributes = insert["Attributes"].Items.Take(options.MaximumAttributes + 1).ToArray();
            count = checked(count + attributes.Length + definitions.Length);
            if (count > options.MaximumAttributes) throw new ArgumentException("Attribute synchronization budget exceeded.");
            if (attributes.Any(a => a.Type != "Attribute" || a["Owner"].Id != insert.Id)
                || attributes.Select(a => a.Id).Distinct().Count() != attributes.Length)
                throw new ArgumentException("An INSERT has inconsistent attribute ownership.");
            if (attributes.Any(a => a["Layer"]["IsLocked"].Boolean()) || lockedDefinitions)
                throw new InvalidOperationException("An attribute or destination definition uses a locked layer.");
            var nativeInsert = Insert.CreateWithAttributes(frame, Array.Empty<Att>());
            nativeInsert.Position = conversion.Value<Vector3>(insert["Position"]);
            nativeInsert.Normal = conversion.Value<Vector3>(insert["Normal"]);
            nativeInsert.Scale = conversion.Value<Vector3>(insert["Scale"]);
            nativeInsert.Rotation = conversion.Value<double>(insert["Rotation"]);
            var units = conversion.Value<DrawingUnits>(insert["Owner"]["Record"]["Layout"].Exists
                ? snapshot.Document["DrawingVariables"]["InsUnits"] : insert["Owner"]["Record"]["Units"]);
            var matrix = nativeInsert.GetTransformation(units); var translation = nativeInsert.Position - matrix * frame.Origin;
            var membership = new List<ShadowValue>(); var added = ImmutableArray.CreateBuilder<ObjectId>();
            var retained = ImmutableArray.CreateBuilder<ObjectId>(); var conversions = ImmutableArray.CreateBuilder<string>();
            var byTag = attributes.ToLookup(a => a["Tag"].Text, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definitions.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = definitions[i];
                var projected = Att.CreateTransformed(nativeDefinitions[i], matrix, translation, mirrorText);
                var properties = Geometry.ToDictionary(name => name,
                    name => conversion.Read(SourceAccessors.Find(typeof(Att), name)!.Get(projected)), StringComparer.Ordinal);
                foreach (var name in Appearance) properties[name] = definition[name].Value;
                properties["Definition"] = definition.Value; properties["Owner"] = insert.Value;
                properties["ProxyGraphics"] = ShadowValue.Null;
                var matches = byTag[definition["Tag"].Text].ToArray();
                if (matches.Length == 0) Write(default);
                else foreach (var previous in matches) Write(previous); // Imported duplicate tags keep distinct values and identities.

                void Write(ShadowReader previous)
                {
                    var fields = new Dictionary<string, ShadowValue>(properties);
                    var text = projected.GetTextData(false);
                    var oldContent = previous["TextData"]["Content"];
                    string fallback = previous.Exists ? previous["Value"].Text : projected.Value;
                    if (previous.Exists && oldContent.Exists != (text?.Content is not null))
                    {
                        if (!options.ConvertTextModes)
                            throw new InvalidOperationException("Attribute " + definition["Tag"].Text + " changes text mode. Review and explicitly enable text conversion.");
                        if (text?.Content is { } content)
                        {
                            content.Value = EscapeMText(fallback);
                            conversions.Add(previous.Id + ": single-line value becomes literal multiline text; fallback retained.");
                        }
                        else
                        {
                            var oldText = conversion.Value<AttributeMText>(oldContent);
                            fallback = oldText.ToMText().PlainText().Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                            conversions.Add(previous.Id + ": visible multiline text replaces the fallback; inline formatting and paragraph breaks are removed.");
                        }
                    }
                    else if (previous.Exists && text?.Content is { } content) content.Value = oldContent["Value"].Text;
                    fields["Value"] = ShadowValue.From(fallback);
                    fields["TextData"] = conversion.Read(text);
                    fields["IsPositionLocked"] = conversion.Read(text?.Locked);
                    fields["HasMText"] = ShadowValue.From(text?.Content is not null);
                    ObjectId id;
                    if (previous.Id is { } existing)
                    {
                        id = existing; retained.Add(id);
                        foreach (var (name, value) in fields) edit.Set(id, name, value);
                    }
                    else
                    {
                        fields["Tag"] = definition["Tag"].Value;
                        id = edit.Create(typeof(Att).FullName!, ShadowObjectKind.Object, fields); added.Add(id);
                    }
                    membership.Add(new ShadowValue.Reference(id));
                }
            }
            var removed = attributes.Where(a => !tags.Contains(a["Tag"].Text)).Select(a => new AttributeSynchronizationRemoval(
                a.Id!.Value, a["Tag"].Text, a["Value"].Text, a["TextData"]["Content"].Exists ? a["TextData"]["Content"]["Value"].Text : null)).ToImmutableArray();
            edit.ReplaceSequence(insert.Id!.Value, "Attributes", membership);
            edit.Set(insert.Id.Value, "ProxyGraphics", ShadowValue.Null);
            var xdata = insert["XData"]; var appIds = xdata["AppIds"].Items.Select(v => v.Text).ToImmutableArray();
            if (appIds.Length > 0)
            {
                edit.ReplaceSequence(insert.Id.Value, "XData", []);
                edit.ReplaceSequence(xdata.Id!.Value, "Values", []);
                edit.ReplaceSequence(xdata.Id.Value, "AppIds", []);
            }
            reports.Add(new(insert.Id.Value, added.ToImmutable(), retained.ToImmutable(), removed, appIds, conversions.ToImmutable()));
        }
        cancellationToken.ThrowIfCancellationRequested(); edit.PruneUnreachable();
        return new(blockId, block["Name"].Text, edit.Build(), reports.ToImmutable());
    }

    private static string EscapeMText(string value) => value.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
        .Replace("\r\n", "\\P").Replace("\r", "\\P").Replace("\n", "\\P");

    // Materialize only the numerical/text carriers needed by native transformation.
    // Resources remain immutable references in the proposed patch, including branches.
    private sealed class DetachedTextConversion(ShadowSnapshot snapshot)
    {
        private readonly ShadowCapture values = new(new());
        private readonly Dictionary<ObjectId, TextStyle> styles = new();
        private readonly Dictionary<string, ShadowValue> styleReferences = snapshot.Document["TextStyles"].Items
            .ToDictionary(s => s["Name"].Text, s => s.Value, StringComparer.OrdinalIgnoreCase);
        public T Value<T>(ShadowReader reader) => (T)SourceValueConversion.ConvertValue(reader.Value, typeof(T), Resolve)!;
        private object Resolve(ObjectId id)
        {
            if (styles.TryGetValue(id, out var style)) return style;
            var node = snapshot.Read(id);
            if (node.Type != "TextStyle") throw new ArgumentException("Attribute geometry refers to an invalid text style.");
            style = TextStyle.FromStoredFontFiles(node["Name"].Text, node["FontFile"].Text, node["BigFont"].Text);
            styles.Add(id, style); return style;
        }
        public AttributeDefinition Definition(ShadowReader source)
        {
            var definition = new AttributeDefinition(source["Tag"].Text) { Value = source["Value"].Text };
            foreach (var name in Geometry)
            {
                var accessor = SourceAccessors.Find(typeof(AttributeDefinition), name)!;
                accessor.Set(definition, SourceValueConversion.ConvertValue(source[name].Value, accessor.ValueType, Resolve));
            }
            definition.SetTextData(Value<AttributeTextData?>(source["TextData"]));
            return definition;
        }
        public ShadowValue Read(object? value) => value switch
        {
            TextStyle style => styleReferences.TryGetValue(style.Name, out var reference) ? reference : throw new ArgumentException("Missing embedded text style."),
            AttributeTextData or AttributeMText => AttributeTextValues.Read(value, (item, _) => Read(item), 4096),
            _ => values.Preview(value)
        };
    }
}
