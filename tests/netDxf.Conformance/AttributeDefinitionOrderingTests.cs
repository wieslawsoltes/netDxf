using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeDefinitionOrderingTests()
    {
        Run("attribute-definition-order/live-views-insertion-replacement-and-removal", () =>
        {
            var dictionary = new AttributeDefinitionDictionary();
            var tags = dictionary.Tags; var values = dictionary.Values;
            var a = new AttributeDefinition("A"); var b = new AttributeDefinition("B"); var c = new AttributeDefinition("C");
            dictionary.AddRange(new[] { a, b, c }); dictionary.Reorder(new[] { c, a, b });
            Check(tags.SequenceEqual(new[] { "C", "A", "B" }) && values.SequenceEqual(new[] { c, a, b }), "Previously obtained views do not follow order");
            var replacement = new AttributeDefinition("a"); dictionary["A"] = replacement;
            dictionary.Remove("C"); dictionary.Add(new AttributeDefinition("D"));
            Check(tags.SequenceEqual(new[] { "a", "B", "D" }) && dictionary["A"] == replacement && tags.Contains("A"), "Replacement, lookup or insertion changed prompt order");
            var pairs = new KeyValuePair<string, AttributeDefinition>[4];
            ((ICollection<KeyValuePair<string, AttributeDefinition>>)dictionary).CopyTo(pairs, 1);
            Check(pairs.Skip(1).Select(x => x.Key).SequenceEqual(tags) && ((IDictionary<string, AttributeDefinition>)dictionary).Keys.SequenceEqual(tags), "Dictionary views disagree");
            Check(!((ICollection<KeyValuePair<string, AttributeDefinition>>)dictionary).Remove(new("missing", a)), "Removing an absent pair succeeded");
            Throws<NotSupportedException>(() => tags.Clear()); Throws<NotSupportedException>(() => values.Add(a));
            dictionary.Clear(); Check(tags.Count == 0 && values.Count == 0, "Clear left stale live views");
        });
        Run("attribute-definition-order/retains-owned-identities-handles-and-resource-references", () =>
        {
            var block = new Block("ORDER"); var layer = new Layer("DefinitionLayer");
            var a = new AttributeDefinition("A") { Layer = layer, Value = "default" }; var b = new AttributeDefinition("B");
            block.AttributeDefinitions.AddRange(new[] { a, b }); var doc = new DxfDocument(); var root = new Insert(block); doc.Entities.Add(root);
            var handles = new[] { a.Handle, b.Handle }; var oldAttributes = root.Attributes.ToArray(); var events = 0;
            block.AttributeDefinitionAdded += (_, _) => events++; block.AttributeDefinitionRemoved += (_, _) => events++;
            block.AttributeDefinitions.Reorder(new[] { b, a });
            Check(a.Handle == handles[0] && b.Handle == handles[1] && a.Owner == block && b.Owner == block && events == 0, "Ordering detached definitions");
            Check(!doc.Layers.Remove(layer) && a.Value == "default", "Ordering changed values or resource references");
            Check(root.Attributes.SequenceEqual(oldAttributes), "Definition ordering changed existing instances");
            Check(new Insert(block).Attributes.Select(x => x.Tag).SequenceEqual(new[] { "B", "A" }), "New insertion ignored prompt order");
            Check(((Block)block.Clone()).AttributeDefinitions.Tags.SequenceEqual(new[] { "B", "A" }), "Block clone lost order");
        });
        Run("attribute-definition-order/invalid-and-failing-enumerations-preserve-membership", () =>
        {
            var dictionary = new AttributeDefinitionDictionary(); var a = new AttributeDefinition("A"); var b = new AttributeDefinition("B");
            dictionary.AddRange(new[] { a, b });
            IEnumerable<AttributeDefinition> Broken() { yield return b; throw new InvalidOperationException("enumeration failed"); }
            Throws<ArgumentNullException>(() => dictionary.Reorder(null!));
            Throws<ArgumentException>(() => dictionary.Reorder(new[] { a }));
            Throws<ArgumentException>(() => dictionary.Reorder(new[] { a, a }));
            Throws<ArgumentException>(() => dictionary.Reorder(new[] { new AttributeDefinition("A"), b }));
            Throws<ArgumentException>(() => dictionary.Reorder(new[] { a, null! }));
            Throws<InvalidOperationException>(() => dictionary.Reorder(Broken()));
            Check(dictionary.Values.SequenceEqual(new[] { a, b }), "Invalid ordering partially changed membership");
            dictionary.Reorder(dictionary.Values); Check(dictionary["a"] == a, "No-op ordering broke case-insensitive lookup");
        });
        Run("attribute-definition-order/views-remain-coherent-after-notification-failure", () =>
        {
            var dictionary = new AttributeDefinitionDictionary(); var a = new AttributeDefinition("A");
            dictionary.AddItem += (_, _) => throw new InvalidOperationException("application callback");
            Throws<InvalidOperationException>(() => dictionary.Add(a));
            Check(dictionary.Count == 1 && dictionary.Values.Single() == a && dictionary.Tags.Single() == "A", "Add failure split ordered views and lookup");
            dictionary.RemoveItem += (_, _) => throw new InvalidOperationException("application callback");
            Throws<InvalidOperationException>(() => dictionary.Remove("A"));
            Check(dictionary.Count == 0 && !dictionary.Values.Any() && !dictionary.Tags.Any(), "Remove failure split ordered views and lookup");
        });
        foreach (bool binary in new[] { false, true }) Run("attribute-definition-order/dxf-roundtrip/" + binary, () =>
        {
            var doc = new DxfDocument(); var block = new Block("ROUNDTRIP");
            foreach (var tag in new[] { "FIRST", "SECOND", "THIRD" }) block.AttributeDefinitions.Add(new AttributeDefinition(tag) { Prompt = tag + " prompt", Value = tag + " value" });
            var expected = new[] { "THIRD", "FIRST", "SECOND" }; block.AttributeDefinitions.Reorder(expected.Select(tag => block.AttributeDefinitions[tag]));
            doc.Entities.Add(new Insert(block)); using var data = new MemoryStream(); Check(doc.Save(data, binary), "Save failed"); data.Position = 0;
            var loaded = DxfDocument.Load(data)!; var restored = loaded.Blocks[block.Name];
            Check(restored.AttributeDefinitions.Tags.SequenceEqual(expected) && loaded.Entities.Inserts.Single().Attributes.Select(x => x.Tag).SequenceEqual(expected), "DXF transport lost definition or insertion order");
            Check(restored.AttributeDefinitions.Values.All(d => d.Prompt == d.Tag + " prompt" && d.Value == d.Tag + " value"), "DXF transport lost definition values");
        });
    }
}
