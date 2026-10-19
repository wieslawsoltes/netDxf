using netDxf;
using netDxf.Blocks;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeDefinitionNamingTests()
    {
        Run("attribute-definition-names/atomic-swap-retains-index-order-objects-handles-and-values", () =>
        {
            var block = new Block("NAMES"); var a = new AttributeDefinition("A") { Value = "default A" }; var b = new AttributeDefinition("B") { Value = "default B" };
            block.AttributeDefinitions.AddRange(new[] { a, b }); var doc = new DxfDocument(); var root = new Insert(block); doc.Entities.Add(root);
            var handles = new[] { a.Handle, b.Handle }; var tags = block.AttributeDefinitions.Tags; var values = block.AttributeDefinitions.Values;
            int events = 0; block.AttributeDefinitionAdded += (_, _) => events++; block.AttributeDefinitionRemoved += (_, _) => events++;
            var plan = block.PrepareAttributeDefinitionRenames(new Dictionary<string, string> { ["A"] = "B", ["B"] = "a" });
            Check(a.Tag == "A" && b.Tag == "B", "Preparation mutated definitions"); plan.Apply();
            Check(block.AttributeDefinitions["B"] == a && block.AttributeDefinitions["A"] == b && tags.SequenceEqual(new[] { "B", "a" }) && values.SequenceEqual(new[] { a, b }), "Rename lost dictionary indexes or order");
            Check(a.Handle == handles[0] && b.Handle == handles[1] && a.Value == "default A" && events == 0, "Rename replaced definitions or values");
            Check(root.Attributes.Select(x => x.Tag).SequenceEqual(new[] { "A", "B" }), "Rename unexpectedly changed existing instances");
            Throws<InvalidOperationException>(() => plan.Apply());
            block.RenameAttributeDefinitions(new Dictionary<string, string> { ["B"] = "A", ["a"] = "B" });
            Check(a.Tag == "A" && b.Tag == "B", "Reverse rename failed");
            Throws<InvalidOperationException>(() => plan.Apply());
        });
        Run("attribute-definition-names/collisions-unknown-empty-and-stale-plans-refuse-before-mutation", () =>
        {
            var block = new Block("REFUSE"); var a = new AttributeDefinition("A"); var b = new AttributeDefinition("B"); block.AttributeDefinitions.AddRange(new[] { a, b });
            Throws<ArgumentException>(() => block.RenameAttributeDefinitions(new Dictionary<string, string> { ["A"] = "b" }));
            Throws<ArgumentException>(() => block.RenameAttributeDefinitions(new Dictionary<string, string> { ["MISSING"] = "C" }));
            Throws<ArgumentException>(() => block.RenameAttributeDefinitions(new Dictionary<string, string> { ["A"] = "" }));
            var plan = block.PrepareAttributeDefinitionRenames(new Dictionary<string, string> { ["A"] = "C" }); block.AttributeDefinitions.Reorder(new[] { b, a });
            Throws<InvalidOperationException>(() => plan.Apply());
            Check(a.Tag == "A" && b.Tag == "B" && block.AttributeDefinitions["a"] == a, "Refused plan changed indexed tags");
        });
        Run("attribute-binding/rename-retains-assigned-value-packets-handles-and-restores-exact-binding", () =>
        {
            var block = new Block("BIND"); var definition = new AttributeDefinition("A"); block.AttributeDefinitions.Add(definition);
            var root = new Insert(block); var doc = new DxfDocument(); doc.Entities.Add(root); var a = root.Attributes.Single();
            a.Value = "assigned"; a.SetMText(new AttributeMText { Value = "embedded\\Pvalue" }); var handle = a.Handle;
            var plan = a.PrepareDefinitionBinding("RENAMED", definition);
            Throws<ArgumentException>(() => plan.Apply());
            block.RenameAttributeDefinitions(new Dictionary<string, string> { ["A"] = "RENAMED" }); plan.Apply();
            Check(a.Tag == "RENAMED" && a.Definition == definition && a.Value == "assigned" && a.GetMText().Value == "embedded\\Pvalue" && a.Handle == handle && a.Owner == root, "Binding changed authored state or ownership");
            plan.Rollback(); Check(a.Tag == "A" && a.Definition == definition && definition.Tag == "RENAMED", "Rollback did not restore the exact original binding");
            var orphan = a.PrepareDefinitionBinding("ORPHAN", null!); orphan.Apply(); orphan.Rollback();
            Check(a.Tag == "A" && a.Definition == definition, "A preexisting mismatched binding could not be restored");
        });
        Run("attribute-binding/stale-bindings-and-empty-tags-refuse", () =>
        {
            var first = new AttributeDefinition("A"); var replacement = new AttributeDefinition("A"); var a = new netDxf.Entities.Attribute(first);
            Throws<ArgumentException>(() => a.PrepareDefinitionBinding("", first));
            var plan = a.PrepareDefinitionBinding("A", replacement); a.SetDefinition(null!);
            Throws<InvalidOperationException>(() => plan.Apply()); Check(a.Definition == null && a.Tag == "A", "Stale plan overwrote a binding");
            a.SetDefinition(first); plan = a.PrepareDefinitionBinding("A", replacement); plan.Apply(); a.SetDefinition(first);
            Throws<InvalidOperationException>(() => plan.Rollback()); Check(a.Definition == first, "Stale rollback overwrote a binding");
        });
        foreach (bool binary in new[] { false, true }) Run("attribute-definition-names/dxf-transport/" + binary, () =>
        {
            var block = new Block("TRANSPORT"); var definition = new AttributeDefinition("BEFORE") { Value = "default" }; block.AttributeDefinitions.Add(definition);
            var root = new Insert(block); root.Attributes.Single().Value = "assigned"; var doc = new DxfDocument(); doc.Entities.Add(root);
            var binding = root.Attributes.Single().PrepareDefinitionBinding("AFTER", definition);
            block.RenameAttributeDefinitions(new Dictionary<string, string> { ["BEFORE"] = "AFTER" }); binding.Apply();
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Save failed"); stream.Position = 0;
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, "attribute-definition-rename-" + (binary ? "binary" : "text") + ".dxf"), stream.ToArray());
            var loaded = DxfDocument.Load(stream)!; var restored = loaded.Entities.Inserts.Single().Attributes.Single();
            Check(restored.Tag == "AFTER" && restored.Definition == loaded.Blocks["TRANSPORT"].AttributeDefinitions["AFTER"] && restored.Value == "assigned", "DXF lost renamed bindings or assigned values");
        });
    }
}
